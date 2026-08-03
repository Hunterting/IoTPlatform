using IoTPlatform.Configuration;
using IoTPlatform.Infrastructure.Protocol;
using IoTPlatform.Infrastructure.Protocol.Adapters;
using IoTPlatform.Infrastructure.Protocol.AnSheng;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace IoTPlatform.Services;

/// <summary>
/// <see cref="IAnShengProbeService"/> 的默认实现。
///
/// 【关联模型：为什么用 (imei, method) 而不是 frameId】
///   直觉上 frameId 才是"请求-响应"的正解，但安圣固件的实际行为不支持这么做：
///   部分固件在应答 <c>getDevInfo</c> 时<b>不回显</b>请求的 frameId，
///   有的干脆自己生成一个新的。押注 frameId 会让探测在这些固件上 100% 超时。
///   而「同一台设备的同一个方法同时只会有一次在途探测」这个前提是成立的
///   （<see cref="ProbeAsync"/> 串行执行，且同 IMEI 并发认领会被唯一键挡住），
///   因此 (imei, method) 足以唯一定位等待者。
///   <b>由此推论：测试不得断言 frameId。</b>
///
/// 【三条不可违反的约束】
///   1. <b>先登记 TCS，再发指令</b>。反过来会有竞态：设备应答极快时，
///      上行回调先于 TryAdd 执行，等待者永远收不到已经到达的应答。
///   2. <b>TCS 必须用 RunContinuationsAsynchronously</b>。否则 <c>SetResult</c> 会在
///      MQTT 接收线程上<b>同步</b>执行等待方的后续代码（含 EF 落库、HTTP 响应写回），
///      轻则拖慢整条上行链路，重则死锁。
///   3. <b>deviceId 传 0L</b>。认领之前数据库里根本没有 Device 行，
///      传任何非零值都是编造。<c>AnShengDiscoveryService.ScanUnclaimedDevicesAsync</c>
///      已有同样的先例。
/// </summary>
public class AnShengProbeService : IAnShengProbeService, IDisposable
{
    private const string MethodGetDevInfo = "getDevInfo";
    private const string MethodGetDevStatus = "getDevStatus";

    private readonly IProtocolAdapterFactory _adapterFactory;
    private readonly AnShengMessageParser _parser;
    private readonly ILogger<AnShengProbeService>? _logger;
    private readonly AnShengProbeOptions _options;

    /// <summary>
    /// 在途等待表：<c>"{imei}|{method}"</c> → 等待应答的 TaskCompletionSource。
    /// </summary>
    private readonly ConcurrentDictionary<string, TaskCompletionSource<AnShengMessage>> _pending = new();

    private readonly EventHandler<AnShengUplinkEventArgs> _uplinkHandler;
    private bool _disposed;

    /// <summary>
    /// 构造函数。构造即订阅上行总线。
    /// </summary>
    /// <param name="adapterFactory">协议适配器工厂。</param>
    /// <param name="options">探测参数。</param>
    /// <param name="logger">日志器，可为空。</param>
    public AnShengProbeService(
        IProtocolAdapterFactory adapterFactory,
        IOptions<AnShengProbeOptions>? options = null,
        ILogger<AnShengProbeService>? logger = null)
    {
        _adapterFactory = adapterFactory ?? throw new ArgumentNullException(nameof(adapterFactory));
        _options = options?.Value ?? new AnShengProbeOptions();
        _logger = logger;
        _parser = new AnShengMessageParser();

        // 保存委托引用，Dispose 时才退得掉订阅（匿名 lambda 无法 -=）。
        _uplinkHandler = OnUplink;
        AnShengUplinkHub.Uplink += _uplinkHandler;
    }

    /// <inheritdoc />
    public async Task<AnShengProbeResult> ProbeAsync(
        int protocolConfigId,
        string imei,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imei))
        {
            return AnShengProbeResult.Fail("IMEI 不能为空。");
        }

        var adapter = _adapterFactory.GetAdapter(protocolConfigId);
        if (adapter == null)
        {
            return AnShengProbeResult.Fail($"协议配置 {protocolConfigId} 尚未建立适配器连接。");
        }

        if (!adapter.IsConnected)
        {
            return AnShengProbeResult.Fail($"协议配置 {protocolConfigId} 的适配器当前未连接。");
        }

        // ① getDevInfo —— 探测的必要条件。它拿不到就整体判失败。
        var infoMessage = await RequestAsync(adapter, imei, MethodGetDevInfo, cancellationToken);
        if (infoMessage == null)
        {
            return AnShengProbeResult.Fail($"设备 {imei} 未在 {_options.TimeoutMs} ms 内应答 {MethodGetDevInfo}。");
        }

        var devInfo = SafeParse(() => _parser.ParseDevInfo(infoMessage), MethodGetDevInfo, imei);

        // ② getDevStatus —— 尽力而为。
        // 它超时不判整体失败：getDevInfo 已足够定品类（version/slotAmount/netType 都在里面），
        // 状态只是锦上添花（signal / iccid）。为了一个可选字段把认领整个否掉不合算。
        AnShengDevStatus? devStatus = null;
        var statusMessage = await RequestAsync(adapter, imei, MethodGetDevStatus, cancellationToken);
        if (statusMessage != null)
        {
            devStatus = SafeParse(() => _parser.ParseDevStatus(statusMessage), MethodGetDevStatus, imei);
        }
        else
        {
            _logger?.LogWarning(
                "安圣探测：设备 {Imei} 未应答 {Method}，将仅凭 {InfoMethod} 建档。",
                imei, MethodGetDevStatus, MethodGetDevInfo);
        }

        return AnShengProbeResult.Ok(devInfo, devStatus);
    }

    /// <inheritdoc />
    public void ClearPending()
    {
        foreach (var key in _pending.Keys)
        {
            if (_pending.TryRemove(key, out var tcs))
            {
                // 用 TrySetCanceled 而非丢弃：还挂在上面的 await 会立刻以取消收场，
                // 而不是一直吊到超时才醒，避免上一个用例的等待拖慢下一个用例。
                tcs.TrySetCanceled();
            }
        }
    }

    /// <summary>
    /// 下发一条指令并等待同 (imei, method) 的应答。
    /// </summary>
    /// <param name="adapter">协议适配器。</param>
    /// <param name="imei">设备 IMEI。</param>
    /// <param name="method">方法名。</param>
    /// <param name="cancellationToken">外部取消令牌。</param>
    /// <returns>应答报文；超时/取消/下发失败返回 <c>null</c>。</returns>
    private async Task<AnShengMessage?> RequestAsync(
        IProtocolAdapter adapter,
        string imei,
        string method,
        CancellationToken cancellationToken)
    {
        var key = BuildKey(imei, method);

        // 【约束 2】RunContinuationsAsynchronously：
        // 不加这个标志，SetResult 会在 MQTT 接收线程上同步跑完调用方的后续代码。
        var tcs = new TaskCompletionSource<AnShengMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        // 【约束 1】先登记再下发。顺序颠倒会丢掉"秒回"设备的应答。
        if (!_pending.TryAdd(key, tcs))
        {
            _logger?.LogWarning("安圣探测：{Key} 已有在途请求，拒绝并发探测。", key);
            return null;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_options.TimeoutMs);

        // 令牌取消时把等待方唤醒。registration 随 using 释放，不会泄漏回调。
        await using var registration = timeoutCts.Token.Register(() => tcs.TrySetCanceled());

        try
        {
            // 【约束 3】deviceId 传 0L —— 认领前数据库里没有 Device 行。
            await adapter.SendCommandAsync(0L, imei, method, string.Empty, cancellationToken);

            return await tcs.Task;
        }
        catch (OperationCanceledException)
        {
            _logger?.LogWarning("安圣探测超时或被取消: IMEI={Imei}, Method={Method}", imei, method);
            return null;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "安圣探测下发失败: IMEI={Imei}, Method={Method}", imei, method);
            return null;
        }
        finally
        {
            // 无论成功失败都要摘掉，否则下次同 (imei, method) 探测会撞 TryAdd 失败。
            _pending.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// 上行总线回调：按 (imei, method) 唤醒等待者。
    ///
    /// 【运行线程】MQTT 接收线程。本方法必须极快返回且绝不抛异常
    /// （Hub 已兜底，但不能依赖兜底写代码）。
    /// </summary>
    /// <param name="sender">事件源，恒为 null。</param>
    /// <param name="e">上行事件参数。</param>
    private void OnUplink(object? sender, AnShengUplinkEventArgs e)
    {
        if (e?.Message == null || string.IsNullOrWhiteSpace(e.Imei) || string.IsNullOrWhiteSpace(e.Method))
        {
            return;
        }

        var key = BuildKey(e.Imei, e.Method);
        if (_pending.TryGetValue(key, out var tcs))
        {
            // 用 TrySetResult：等待方可能已因超时取消，此时 TCS 已终结，Set 会抛。
            tcs.TrySetResult(e.Message);
        }
    }

    /// <summary>
    /// 构造在途等待表的键。
    /// </summary>
    /// <param name="imei">设备 IMEI。</param>
    /// <param name="method">方法名。</param>
    /// <returns>形如 <c>864...900|getDevInfo</c> 的键。</returns>
    private static string BuildKey(string imei, string method) => $"{imei}|{method}";

    /// <summary>
    /// 包一层异常保护的解析：解析失败返回 null 而不是把异常抛给认领流程。
    /// </summary>
    /// <typeparam name="T">解析目标类型。</typeparam>
    /// <param name="parse">解析委托。</param>
    /// <param name="method">方法名，仅用于日志。</param>
    /// <param name="imei">设备 IMEI，仅用于日志。</param>
    /// <returns>解析结果或 null。</returns>
    private T? SafeParse<T>(Func<T?> parse, string method, string imei) where T : class
    {
        try
        {
            return parse();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "安圣探测应答解析失败: IMEI={Imei}, Method={Method}", imei, method);
            return null;
        }
    }

    /// <summary>
    /// 退订上行总线并唤醒全部在途等待。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        AnShengUplinkHub.Uplink -= _uplinkHandler;
        ClearPending();
        GC.SuppressFinalize(this);
    }
}
