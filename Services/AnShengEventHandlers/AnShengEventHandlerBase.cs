using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IoTPlatform.Data;
using IoTPlatform.Infrastructure.Protocol.AnSheng;
using IoTPlatform.Models;
using IoTPlatform.Services;
using IoTPlatform.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace IoTPlatform.Services.AnShengEventHandlers;

/// <summary>
/// 事件 Handler 抽象基类 —— 收敛「双出口」公共逻辑，7 个具体 Handler 只写各自业务动作。
///
/// 【模板方法流程（设计 §3.3，所有 Handler 共享，保证双出口一致）】
///   1. <c>outcome = await OnHandleAsync(ctx)</c>                 —— 子类业务动作
///   2. <c>dataPoints = outcome.DataPoints ?? Normalizer.Normalize(message)</c>
///   3. 解析 <c>OccurredAt</c>（带合理性回退，脏时钟回退到 ReceivedAt）
///   4. 出口① <c>PersistEvent</c> 为真 → 组装 <see cref="AnShengDeviceEvent"/> → <c>SaveChangesAsync</c>
///   5. 出口② <c>DispatchToRules</c> 为真且 ctx.DeviceId 有值 →
///      <c>Collector.ProcessDeviceDataAsync(...)</c>；
///      成功置 <c>DispatchedToRules=true</c>，失败捕获写 <c>DispatchError</c>，<b>不回滚出口①</b>
///
/// 【多租户 ★】出口① 的 <see cref="AnShengDeviceEvent.AppCode"/> 必须<b>显式</b>填 ctx.AppCode，
///   后台线程的全局租户过滤器不生效，EF 不会替你填（设计 §7.2）。
///
/// 【生命周期】Scoped（与 Router / Normalizer / 同组服务一致），内部持有
///   <see cref="AppDbContext"/>、<see cref="AnShengDataNormalizer"/>、<see cref="IDataCollectionService"/>。
/// </summary>
public abstract class AnShengEventHandlerBase : IAnShengEventHandler
{
    /// <summary>
    /// 方法名 → 事件类型映射（唯一权威）。
    /// 与设计 §3.4 的 Kind 列、<see cref="AnShengCommandCatalog"/> 的事件方法集保持一致。
    /// </summary>
    private static readonly Dictionary<string, AnShengEventKind> MethodToKind = new(StringComparer.Ordinal)
    {
        ["connected"] = AnShengEventKind.Connected,
        ["close"] = AnShengEventKind.Close,
        ["keyEvent"] = AnShengEventKind.Key,
        ["delayEvent"] = AnShengEventKind.Delay,
        ["timeEvent"] = AnShengEventKind.Time,
        ["recv485"] = AnShengEventKind.Recv485,
        ["simCheck"] = AnShengEventKind.SimCheck,
    };

    private readonly AnShengDataNormalizer _normalizer;
    private readonly IDataCollectionService _collector;
    private readonly AppDbContext _db;

    /// <summary>
    /// 构造基类。
    /// </summary>
    /// <param name="normalizer">报文归一化器（Scoped）。</param>
    /// <param name="collector">数据采集服务（出口② 落库）。</param>
    /// <param name="db">数据库上下文（出口① 落库）。</param>
    /// <param name="logger">日志器。</param>
    protected AnShengEventHandlerBase(
        AnShengDataNormalizer normalizer,
        IDataCollectionService collector,
        AppDbContext db,
        ILogger logger)
    {
        _normalizer = normalizer ?? throw new ArgumentNullException(nameof(normalizer));
        _collector = collector ?? throw new ArgumentNullException(nameof(collector));
        _db = db ?? throw new ArgumentNullException(nameof(db));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>日志器，供子类使用。</summary>
    protected ILogger Logger { get; }

    /// <inheritdoc />
    public abstract string Method { get; }

    /// <inheritdoc />
    public async Task<AnShengEventOutcome> HandleAsync(AnShengUplinkContext ctx, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        var outcome = await OnHandleAsync(ctx, cancellationToken).ConfigureAwait(false);

        // ① 数据点：子类已给则直接用，否则统一归一化（Normalize 对 null 报文返回空字典，安全）。
        var dataPoints = outcome.DataPoints ?? _normalizer.Normalize(ctx.Message);
        var json = AnShengDataNormalizer.ToJson(dataPoints);

        // ② 业务时间轴（带合理性回退，脏时钟回退到平台时间）。
        var occurredAt = AnShengDeviceEvent.ResolveOccurredAt(
            ctx.DeviceTimestampUtc, ctx.ReceivedAt, out var usedFallback);
        if (usedFallback)
        {
            // 回退标记随数据点落库，便于后续排障识别「这条事件用的是平台时间」。
            dataPoints["ts_fallback"] = true;
            json = AnShengDataNormalizer.ToJson(dataPoints);
        }

        // ③ 出口①：事件溯源表。
        AnShengDeviceEvent? ev = null;
        if (outcome.PersistEvent)
        {
            ev = new AnShengDeviceEvent
            {
                // ★ 显式赋值 AppCode（后台线程租户过滤器不生效，见 §7.2）。
                AppCode = ctx.AppCode,
                Imei = ctx.Imei,
                DeviceId = ctx.DeviceId,
                Method = ctx.Method,
                Kind = ResolveKind(ctx.Method),
                Severity = outcome.Severity,
                SlotNum = outcome.SlotNum,
                FrameId = ctx.FrameId,
                OccurredAt = occurredAt,
                DeviceTimestampUtc = ctx.DeviceTimestampUtc,
                ReceivedAt = ctx.ReceivedAt,
                PayloadJson = json,
                RawJson = ctx.RawPayload,
                CreatedAt = DateTime.UtcNow,
            };
            _db.AnShengDeviceEvents.Add(ev);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        // ④ 出口②：投递规则引擎（失败不回滚出口①）。
        var dispatched = false;
        string? dispatchError = null;
        if (outcome.DispatchToRules && ctx.DeviceId is long deviceId)
        {
            try
            {
                await _collector
                    .ProcessDeviceDataAsync(deviceId, ctx.AppCode, json, occurredAt)
                    .ConfigureAwait(false);
                dispatched = true;
            }
            catch (Exception ex)
            {
                dispatchError = Truncate(ex.Message, AnShengDeviceEvent.DispatchErrorMaxLength);
                Logger.LogWarning(ex,
                    "[AnShengEvent] 规则引擎投递失败 imei={Imei} method={Method} eventId={EventId}",
                    ctx.Imei, ctx.Method, ev?.Id ?? 0);
            }
        }

        if (ev != null)
        {
            ev.DispatchedToRules = dispatched;
            ev.DispatchError = dispatchError;
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            Logger.LogInformation(
                "[AnShengEvent] {Imei} {Method} eventId={Id} kind={Kind} dispatched={Dispatched}",
                ctx.Imei, ctx.Method, ev.Id, ev.Kind, dispatched);
        }

        return outcome;
    }

    /// <summary>
    /// 子类各自的业务动作。返回 <see cref="AnShengEventOutcome"/> 描述双出口行为。
    /// </summary>
    /// <param name="ctx">已组装完成的上行上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>双出口行为描述。</returns>
    protected abstract Task<AnShengEventOutcome> OnHandleAsync(
        AnShengUplinkContext ctx, CancellationToken cancellationToken);

    /// <summary>
    /// 供子类复用：归一化事件报文为数据点字典（包装 Normalizer，避免子类直接持有它）。
    /// </summary>
    /// <param name="message">已解析报文。</param>
    /// <returns>数据点字典。</returns>
    protected IDictionary<string, object?> NormalizeEvent(AnShengMessage message)
        => _normalizer.NormalizeEvent(message);

    private static AnShengEventKind ResolveKind(string method)
        => MethodToKind.TryGetValue(method, out var kind) ? kind : AnShengEventKind.Unknown;

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }
}
