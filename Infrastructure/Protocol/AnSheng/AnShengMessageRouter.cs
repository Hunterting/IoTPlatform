using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IoTPlatform.Data;
using IoTPlatform.Services;
using IoTPlatform.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace IoTPlatform.Infrastructure.Protocol.AnSheng;

/// <summary>
/// 安圣上行报文三分支路由器（D4 Option B 的判定核心）。
///
/// 【职责边界】
///   · <see cref="Classify"/> 是<b>纯判定</b>：给定上下文返回归属，不碰数据库、不产生副作用。
///     这让验收 #1 #2 #3 可以用毫秒级单元测试覆盖。
///   · <see cref="RouteAsync"/> 在判定之后执行各分支的动作（分发 / 摘在途 / 刷档案）。
///
/// 【生命周期】Scoped —— 持有 <see cref="AppDbContext"/> 与
///   <see cref="IAnShengDeviceProfileService"/>，不可被 Singleton 直接注入。
///   <see cref="AnShengUplinkPipeline"/> 通过 <c>IServiceScopeFactory.CreateScope()</c> 取用。
///
/// 【判定顺序是唯一权威，必须逐级短路】（设计文档 §3.2）
/// <code>
/// 1. 硬白名单（AnShengCommandCatalog.EventMethods，6 个）        ⇒ Event
///    frameId 一律忽略 —— delayEvent 带 frameId 仍是事件（验收 #3）
/// 2. 软白名单（SoftEventMethods，1 个：simCheck）
///      带在途 frameId ⇒ Response（用户主动查 SIM 的应答）
///      否则           ⇒ Event（设备主动报 SIM 异常）
/// 3. frameId 非空 且 在途                                        ⇒ Response
/// 4. 其余                                                        ⇒ AutoReport
/// 5. method 为空 / 报文解析失败                                  ⇒ Ignored
/// </code>
/// 第 5 条在实现上必须<b>前置</b>——没有 method 就无从谈白名单。
/// 文档把它列在末尾是按「优先级从高到低」叙述，代码里按「可判定性」排序，语义等价。
/// </summary>
public sealed class AnShengMessageRouter
{
    /// <summary>
    /// <c>getDevStatus</c> 方法名。
    ///
    /// 【为什么在这里定义而不是加进 <c>AnShengCommandCatalog</c>】
    ///   决策 4 明确要求 T6 <b>不改</b> <c>AnShengCommandCatalog</c>（该文件是协议目录的
    ///   唯一事实来源，任何改动都会牵动适配器的 <c>CommandResponse</c> 触发逻辑）。
    ///   路由层只需要「哪个方法带能力快照」这一条本地知识，就地声明即可。
    /// </summary>
    public const string MethodGetDevStatus = "getDevStatus";

    /// <summary>
    /// 软事件白名单（T6 新增，决策 4）。
    ///
    /// 【为什么不直接把 simCheck 加进 <c>AnShengCommandCatalog.EventMethods</c>】
    ///   <c>AnShengCommandCatalog.IsEvent</c> 不只被本路由器使用：
    ///   <c>AnShengMqttProtocolAdapter</c> 用 <c>frameId 非空 &amp;&amp; !IsEvent</c>
    ///   决定是否抛 <c>CommandResponse</c> 事件。
    ///   一旦把 simCheck 划为事件，<b>用户主动下发 simCheck 查询 SIM 状态时，
    ///   设备应答将不再触发 CommandResponse</b>，T7 的命令应答关联直接断掉。
    ///   这是一个隐蔽但致命的副作用，所以「软白名单」只作用于路由层。
    ///
    /// 【一致性护栏】<c>AnShengProtocolConformanceTests</c> 断言
    ///   「硬白名单 ∪ 软白名单」恰为规格 A.3 的 7 个方法，防止两处白名单各自漂移。
    /// </summary>
    public static readonly IReadOnlyCollection<string> SoftEventMethods =
        new HashSet<string>(StringComparer.Ordinal) { "simCheck" };

    /// <summary>
    /// 硬白名单 ∪ 软白名单 —— 规格 A.3 声明的 7 个「会被判为事件」的方法。
    /// 供一致性测试与文档核对使用。
    /// </summary>
    public static IReadOnlyCollection<string> AllEventMethods { get; } =
        new HashSet<string>(
            AnShengCommandCatalog.EventMethods.Concat(SoftEventMethods),
            StringComparer.Ordinal);

    private readonly IAnShengPendingCommandStore _pendingStore;
    private readonly IAnShengDeviceProfileService _profileService;
    private readonly AppDbContext _db;
    private readonly AnShengMessageParser _parser;
    private readonly ILogger<AnShengMessageRouter> _logger;
    private readonly AnShengEventDispatcher? _dispatcher;

    /// <summary>
    /// 构造路由器。
    /// </summary>
    /// <param name="pendingStore">在途命令表（Singleton，注入 Scoped 是允许方向）。</param>
    /// <param name="profileService">设备能力档案服务。</param>
    /// <param name="db">数据库上下文，用于分支动作后的 SaveChanges。</param>
    /// <param name="parser">报文解析器，用于把 <c>getDevStatus</c> 转成能力快照。</param>
    /// <param name="logger">日志器。</param>
    /// <param name="dispatcher">
    /// 事件分发器；<b>可选</b>。
    /// T6-1 阶段 <c>AnShengEventDispatcher</c> 尚未实现/注册，此时为 <c>null</c>，
    /// Event 分支只记日志不抛异常（T6-1 完成判据）。T6-3 注册后自动接入。
    /// .NET DI 对「带默认值的构造参数」在服务未注册时会使用默认值，无需额外适配。
    /// </param>
    public AnShengMessageRouter(
        IAnShengPendingCommandStore pendingStore,
        IAnShengDeviceProfileService profileService,
        AppDbContext db,
        AnShengMessageParser parser,
        ILogger<AnShengMessageRouter> logger,
        AnShengEventDispatcher? dispatcher = null)
    {
        _pendingStore = pendingStore ?? throw new ArgumentNullException(nameof(pendingStore));
        _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dispatcher = dispatcher;
    }

    /// <summary>
    /// 三分支判定。<b>纯函数语义</b>：除了可能触发在途表的惰性过期外无任何副作用。
    /// </summary>
    /// <param name="ctx">上行上下文。</param>
    /// <returns>判定结果。</returns>
    public AnShengRouteResult Classify(AnShengUplinkContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        var imei = ctx.Imei;
        var method = ctx.Method;
        var frameId = ctx.FrameId;

        // 第 5 级（实现上前置）：没有可用报文就无从判定。
        if (!ctx.HasUsableMessage)
        {
            return new AnShengRouteResult(
                AnShengRouteKind.Ignored, imei, method, frameId,
                ctx.Message == null ? "报文解析失败" : "method 为空");
        }

        // 第 1 级：硬白名单。★ 必须在任何 frameId 判断之前短路 ★
        // 验收 #3：delayEvent 带 frameId 仍判 Event，且不得触碰在途表。
        if (AnShengCommandCatalog.IsEvent(method))
        {
            return new AnShengRouteResult(
                AnShengRouteKind.Event, imei, method, frameId,
                "命中硬事件白名单（AnShengCommandCatalog.EventMethods）");
        }

        // 第 2 级：软白名单（simCheck）。双向方法，按「是否为我方查询的应答」二分。
        if (SoftEventMethods.Contains(method))
        {
            if (!string.IsNullOrWhiteSpace(frameId) && _pendingStore.IsInFlight(imei, frameId!))
            {
                return new AnShengRouteResult(
                    AnShengRouteKind.Response, imei, method, frameId,
                    "软白名单方法且 frameId 在途，判为下发命令的应答");
            }

            return new AnShengRouteResult(
                AnShengRouteKind.Event, imei, method, frameId,
                "命中软事件白名单（无在途 frameId，判为设备主动上报）");
        }

        // 第 3 级：普通命令的应答。
        if (!string.IsNullOrWhiteSpace(frameId) && _pendingStore.IsInFlight(imei, frameId!))
        {
            return new AnShengRouteResult(
                AnShengRouteKind.Response, imei, method, frameId,
                "frameId 在途，判为下发命令的应答");
        }

        // 第 4 级：兜底自动上报。
        // 注意「frameId 非空但不在途」也落这里 —— 验收 #2a 的「未知 frameId」分支。
        return new AnShengRouteResult(
            AnShengRouteKind.AutoReport, imei, method, frameId,
            string.IsNullOrWhiteSpace(frameId)
                ? "无 frameId，判为设备自动上报"
                : "frameId 不在途，判为设备自动上报");
    }

    /// <summary>
    /// 判定并执行对应分支的动作。
    /// </summary>
    /// <param name="ctx">上行上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>判定结果（动作是否成功不改变判定值，失败只记日志）。</returns>
    public async Task<AnShengRouteResult> RouteAsync(
        AnShengUplinkContext ctx,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        var result = Classify(ctx);

        if (result.Kind == AnShengRouteKind.Ignored)
        {
            _logger.LogWarning(
                "[AnShengRouter] {Imei} {Method} => {Kind} ({Reason})",
                result.Imei, result.Method, result.Kind, result.Reason);
            return result;
        }

        _logger.LogDebug(
            "[AnShengRouter] {Imei} {Method} => {Kind} ({Reason}) frameId={FrameId}",
            result.Imei, result.Method, result.Kind, result.Reason, result.FrameId ?? "(none)");

        switch (result.Kind)
        {
            case AnShengRouteKind.Event:
                await HandleEventAsync(ctx, result, cancellationToken).ConfigureAwait(false);
                break;

            case AnShengRouteKind.Response:
                await HandleResponseAsync(ctx, result, cancellationToken).ConfigureAwait(false);
                break;

            case AnShengRouteKind.AutoReport:
                await HandleAutoReportAsync(ctx, cancellationToken).ConfigureAwait(false);
                break;
        }

        return result;
    }

    /// <summary>
    /// Event 分支：交给责任链分发器。
    /// </summary>
    /// <param name="ctx">上行上下文。</param>
    /// <param name="result">判定结果，仅用于日志。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task HandleEventAsync(
        AnShengUplinkContext ctx,
        AnShengRouteResult result,
        CancellationToken cancellationToken)
    {
        if (_dispatcher == null)
        {
            // T6-1 阶段的合法状态：分发器尚未接入。
            // 只记日志不抛 —— 抛异常会让整条上行链路在 T6-3 落地前完全不可用。
            _logger.LogInformation(
                "[AnShengRouter] 事件 {Imei} {Method} 已识别，但 AnShengEventDispatcher 尚未接入，本次不处理",
                result.Imei, result.Method);
            return;
        }

        await _dispatcher.DispatchAsync(ctx, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Response 分支：摘除在途条目；若报文是有效状态快照，顺带刷新档案。
    ///
    /// 【为什么应答也要刷档案】<c>getDevStatus</c> 的应答与自动上报是<b>同一份</b>状态数据，
    /// 只是触发方式不同。只在自动上报时刷档案，会让「刚探测过的设备档案反而更旧」。
    /// </summary>
    /// <param name="ctx">上行上下文。</param>
    /// <param name="result">判定结果。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task HandleResponseAsync(
        AnShengUplinkContext ctx,
        AnShengRouteResult result,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(result.FrameId))
        {
            var completed = await _pendingStore
                .CompleteAsync(ctx.Imei, result.FrameId!, ctx.Message)
                .ConfigureAwait(false);

            _logger.LogDebug(
                "[AnShengRouter] 应答关联 {Imei} frameId={FrameId} matched={Matched}",
                ctx.Imei, result.FrameId, completed != null);
        }

        await RefreshProfileAsync(ctx, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// AutoReport 分支：<b>管道内不落库、不投递规则引擎</b>，只刷档案（决策 B-1）。
    ///
    /// 落库完全由既有通路 <c>DataReceived → ProtocolConfigService → IDataCollectionService</c>
    /// 承担。这比「改造既有通路去调用 Router」更彻底地满足 D4 §370「并存而非替换」。
    /// </summary>
    /// <param name="ctx">上行上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private Task HandleAutoReportAsync(AnShengUplinkContext ctx, CancellationToken cancellationToken)
        => RefreshProfileAsync(ctx, cancellationToken);

    /// <summary>
    /// 用上行报文自学习到的能力刷新档案。
    ///
    /// 【决策 A 在此可见】<c>RefreshAsync</c> 查不到档案时返回 <c>null</c> 且<b>不建档</b>，
    /// 未认领设备继续留在 <c>DiscoveredAnShengDevice</c> 池里等认领，
    /// 不再产生「孤儿档案」污染 <c>KindSource</c> 语义。
    /// </summary>
    /// <param name="ctx">上行上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task RefreshProfileAsync(AnShengUplinkContext ctx, CancellationToken cancellationToken)
    {
        // 只有 getDevStatus 携带能力信息，其余报文没有可刷新的内容。
        if (ctx.Message == null ||
            !string.Equals(ctx.Method, MethodGetDevStatus, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            var status = _parser.ParseDevStatus(ctx.Message);
            if (status == null)
            {
                return;
            }

            var snapshot = AnShengCapabilitySnapshot.FromDevStatus(status);
            var profile = await _profileService
                .RefreshAsync(ctx.Imei, ctx.AppCode, snapshot, cancellationToken)
                .ConfigureAwait(false);

            if (profile == null)
            {
                // 决策 A：不建档。这是正常业务状态（设备尚未认领），用 Debug 而非 Warn。
                _logger.LogDebug(
                    "[AnShengRouter] {Imei} 无能力档案，跳过刷新（决策 A：不隐式建档）", ctx.Imei);
                return;
            }

            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 刷档案失败不应影响路由判定结果，更不能冒泡到 Pipeline 打断后续处理。
            _logger.LogWarning(ex,
                "[AnShengRouter] 刷新设备档案失败 imei={Imei} method={Method}", ctx.Imei, ctx.Method);
        }
    }
}
