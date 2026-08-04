using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IoTPlatform.Configuration;
using IoTPlatform.Data;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IoTPlatform.Infrastructure.Protocol.AnSheng;

/// <summary>
/// 安圣事件管道入口（决策 B-1）。
///
/// ═══════════════════════════════════════════════════════════════════════════
/// 【为什么挂静态总线 <see cref="AnShengUplinkHub"/>，而不是 <c>ProtocolConfigService</c>】
/// ═══════════════════════════════════════════════════════════════════════════
/// 这是 T6 最关键的架构决定，理由是<b>物理约束</b>而非偏好：
///
///   <c>AnShengMqttProtocolAdapter.OnMessageReceivedAsync</c> 的执行顺序是
///     ① <c>AnShengUplinkHub.Publish(...)</c>
///     ② <c>if (IsWillMessage) { HandleWillMessage(...); return; }</c>   ← <b>早退</b>
///     ③ <c>DataReceived?.Invoke(...)</c>
///
///   也就是说 <c>close</c>（遗嘱）<b>永远不会</b>触发 <c>DataReceived</c>。
///   若管道挂在 <c>ProtocolConfigService.OnProtocolAdapterDataReceived</c> 上，
///   <c>close</c> 事件与 30 秒离线去抖（验收 #5）<b>在物理上无法实现</b>。
///   而 ① 位于 Will 判定之前，是唯一 100% 覆盖全部上行（含 close）的挂载点。
///
///   附带收益：既有 <c>DataReceived → ProtocolConfigService → IDataCollectionService</c>
///   通路<b>一行不动</b>，AutoReport 分支天然 100% 复用，
///   比「改造既有通路去调 Router」更彻底地满足 D4 §370「并存而非替换」。
///
/// ═══════════════════════════════════════════════════════════════════════════
/// 【生命周期：Singleton，且必须在启动时被解析一次】
/// ═══════════════════════════════════════════════════════════════════════════
/// 订阅动作发生在构造函数里。DI 的 Singleton 是<b>惰性</b>构造的 ——
/// 若没有任何地方解析它，构造函数永远不执行，订阅永远不发生，
/// 所有事件用例会<b>静默</b>失败（不报错、就是没数据），极难定位。
/// 因此 <c>Program.cs</c> 必须在 <c>app.Run()</c> 之前调用
/// <c>app.Services.GetRequiredService&lt;AnShengUplinkPipeline&gt;()</c>。
/// 与 <c>AnShengProbeService</c> 是同款模式。
///
/// ═══════════════════════════════════════════════════════════════════════════
/// 【线程模型与异常边界（设计文档 §7.4）】
/// ═══════════════════════════════════════════════════════════════════════════
///   · <c>Publish</c> 是<b>同步</b>的，回调直接跑在 MQTT 接收线程 ⇒
///     <see cref="OnUplink"/> 必须立即 <c>Task.Run</c> 卸载，否则阻塞整个 MQTT 消费；
///   · Hub 对每个订阅者做了 try/catch 隔离，但 <b><c>Task.Run</c> 内的异常不在其中</b> ⇒
///     本类内部必须自己 try/catch 到顶，任何异常只 <c>LogError</c>，<b>绝不抛出</b>
///     （否则 <c>TaskScheduler.UnobservedTaskException</c> 会打崩进程）；
///   · <see cref="DrainAsync"/> 供集成测试等待异步完成，<b>生产代码不调用</b>。
/// </summary>
public sealed class AnShengUplinkPipeline : IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AnShengUplinkPipeline> _logger;
    private readonly AnShengEventOptions _options;
    private readonly EventHandler<AnShengUplinkEventArgs> _uplinkHandler;

    /// <summary>在途处理中的报文数。仅由 <see cref="Interlocked"/> 读写。</summary>
    private int _inFlight;

    private bool _disposed;

    /// <summary>
    /// 构造管道并<b>立即订阅</b>上行总线。
    /// </summary>
    /// <param name="scopeFactory">作用域工厂。Singleton 取 Scoped 服务的唯一合法途径。</param>
    /// <param name="options">事件管道配置。</param>
    /// <param name="logger">日志器。</param>
    public AnShengUplinkPipeline(
        IServiceScopeFactory scopeFactory,
        IOptions<AnShengEventOptions> options,
        ILogger<AnShengUplinkPipeline> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? new AnShengEventOptions();

        // 把委托存成字段：反订阅时必须是同一个实例，
        // 写成 `Uplink -= OnUplink` 虽然编译期会生成等价委托，但显式持有更不容易在重构中出错。
        _uplinkHandler = OnUplink;
        AnShengUplinkHub.Uplink += _uplinkHandler;

        _logger.LogInformation("AnShengUplinkPipeline 已订阅上行总线");
    }

    /// <summary>当前仍在处理中的报文数。供测试与健康检查观察。</summary>
    public int InFlightCount => Volatile.Read(ref _inFlight);

    /// <summary>
    /// 已处理完成的报文总数（含失败）。
    /// 集成测试可用它断言「管道确实被触发过」，避免把「订阅没生效」误判为「业务没命中」。
    /// </summary>
    public long ProcessedCount => Interlocked.Read(ref _processedCount);

    private long _processedCount;

    /// <summary>
    /// 等待所有在途报文处理完毕。
    ///
    /// 【为什么集成测试必须用它而不是 <c>Thread.Sleep</c>】
    ///   管道是 <c>Task.Run</c> 异步的，睡固定时长要么不够（偶发红）要么太久（跑得慢）。
    ///   自旋等 <c>_inFlight == 0</c> 是精确的完成信号。
    /// </summary>
    /// <param name="timeout">最长等待时长。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>在超时前排空返回 <c>true</c>；超时返回 <c>false</c>。</returns>
    public async Task<bool> DrainAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        while (sw.Elapsed < timeout)
        {
            if (Volatile.Read(ref _inFlight) == 0)
            {
                return true;
            }

            // 10ms 粒度：足够快（单个用例最多多等 10ms），又不至于把 CPU 空转满。
            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        }

        return Volatile.Read(ref _inFlight) == 0;
    }

    /// <summary>
    /// 总线回调。<b>跑在 MQTT 接收线程上，必须立刻返回。</b>
    /// </summary>
    /// <param name="sender">总线不传发送者，恒为 null。</param>
    /// <param name="args">上行事件参数。</param>
    private void OnUplink(object? sender, AnShengUplinkEventArgs args)
    {
        if (_disposed || args == null || string.IsNullOrWhiteSpace(args.Imei))
        {
            return;
        }

        // ★ 先自增再 Task.Run ★
        // 反过来写会有窗口：Task 还没排上队时 _inFlight 仍是 0，
        // DrainAsync 会误判「已排空」直接返回，集成测试随机爆红。
        Interlocked.Increment(ref _inFlight);

        _ = Task.Run(async () =>
        {
            try
            {
                await ProcessAsync(args).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // 兜底 catch-all。Hub 的 try/catch 管不到 Task.Run 内部，
                // 这里漏出去就是未观察的任务异常。
                _logger.LogError(ex,
                    "[AnShengPipeline] 处理上行报文时发生未预期异常 imei={Imei} method={Method}",
                    args.Imei, args.Method);
            }
            finally
            {
                Interlocked.Increment(ref _processedCount);
                Interlocked.Decrement(ref _inFlight);
            }
        });
    }

    /// <summary>
    /// 在独立 Scope 内完成上下文组装与路由。
    /// </summary>
    /// <param name="args">上行事件参数。</param>
    private async Task ProcessAsync(AnShengUplinkEventArgs args)
    {
        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;

        var db = sp.GetRequiredService<AppDbContext>();
        var router = sp.GetRequiredService<AnShengMessageRouter>();

        var ctx = await BuildContextAsync(db, args).ConfigureAwait(false);

        await router.RouteAsync(ctx, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 组装上下文：按 IMEI 补齐 <c>DeviceId</c> / <c>AppCode</c> / <c>Profile</c>。
    ///
    /// 【多租户 ★ 陷阱说明（设计文档 §7.2）】
    ///   本方法跑在后台线程，<c>ITenantContextAccessor.Current</c> 为 null，
    ///   <c>AppDbContext</c> 的全局租户过滤器<b>不生效</b> ⇒ 能按 IMEI 查到全租户数据。
    ///   这正是我们需要的（设备只认 IMEI，不认租户），
    ///   但也意味着<b>写入时必须自己填对 AppCode</b>，EF 不会替你填。
    ///
    /// 【为什么用 <c>AsNoTracking</c>】
    ///   这三次查询只为读取标识信息，不参与后续更新。
    ///   不加会把 Device 实体挂进变更追踪，Handler 里的 <c>SaveChanges</c> 可能连带写回意外字段。
    /// </summary>
    /// <param name="db">数据库上下文。</param>
    /// <param name="args">上行事件参数。</param>
    /// <returns>组装完成的上下文。</returns>
    private async Task<AnShengUplinkContext> BuildContextAsync(
        AppDbContext db,
        AnShengUplinkEventArgs args)
    {
        var imei = args.Imei;

        // method 优先取总线参数：报文体解析失败时它往往仍然可用（适配器从 topic/局部解析拿到）。
        var method = !string.IsNullOrWhiteSpace(args.Method)
            ? args.Method
            : args.Message?.Method ?? string.Empty;

        long? deviceId = null;
        string? appCode = null;
        AnShengDeviceProfile? profile = null;

        try
        {
            var device = await db.Devices
                .AsNoTracking()
                .Where(d => d.SerialNumber == imei)
                .Select(d => new { d.Id, d.AppCode })
                .FirstOrDefaultAsync()
                .ConfigureAwait(false);

            if (device != null)
            {
                deviceId = device.Id;
                appCode = device.AppCode;
            }

            profile = await db.AnShengDeviceProfiles
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Imei == imei)
                .ConfigureAwait(false);

            // 优先级 2：档案上的租户码。
            if (string.IsNullOrWhiteSpace(appCode) && !string.IsNullOrWhiteSpace(profile?.AppCode))
            {
                appCode = profile!.AppCode;
            }

            // 优先级 3：待认领池里的租户码（设备已被发现但尚未认领时唯一的租户线索）。
            if (string.IsNullOrWhiteSpace(appCode))
            {
                appCode = await db.DiscoveredAnShengDevices
                    .AsNoTracking()
                    .Where(d => d.Imei == imei)
                    .Select(d => d.AppCode)
                    .FirstOrDefaultAsync()
                    .ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            // 查库失败不应让整条报文被丢弃：至少把 Ignored/日志留下。
            _logger.LogError(ex,
                "[AnShengPipeline] 组装上下文时查库失败 imei={Imei} method={Method}", imei, method);
        }

        // 优先级 4：配置的默认租户；优先级 5：空串 + Warn。
        if (string.IsNullOrWhiteSpace(appCode))
        {
            appCode = _options.DefaultAppCode;
        }

        if (string.IsNullOrWhiteSpace(appCode))
        {
            appCode = string.Empty;
            _logger.LogWarning(
                "[AnShengPipeline] 无法解析租户码，事件将以空 AppCode 落库 imei={Imei} method={Method}。" +
                "可在 AnSheng:Event:DefaultAppCode 配置兜底租户。",
                imei, method);
        }

        return new AnShengUplinkContext(imei, args.Message, args.RawPayload, args.ReceivedAt)
        {
            Method = method,
            FrameId = args.Message?.FrameId,
            DeviceId = deviceId,
            AppCode = appCode,
            Profile = profile
        };
    }

    /// <summary>
    /// 退订总线。
    ///
    /// 【为什么要有 Dispose】进程内 Singleton 正常不会被释放，但集成测试的
    /// <c>WebApplicationFactory</c> 会在 Dispose 时释放根容器。若不退订，
    /// 多个 TestServer 依次创建后总线上会挂着一串指向已释放容器的旧管道，
    /// 它们的 <c>CreateScope</c> 会抛 <c>ObjectDisposedException</c>。
    ///
    /// ⚠️ 这里只摘自己那一个委托，<b>绝不</b>调用 <c>AnShengUplinkHub.Reset()</c> ——
    /// 那会连 <c>AnShengProbeService</c> 的订阅一起拔掉。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        AnShengUplinkHub.Uplink -= _uplinkHandler;
        _logger.LogInformation("AnShengUplinkPipeline 已退订上行总线");
    }
}
