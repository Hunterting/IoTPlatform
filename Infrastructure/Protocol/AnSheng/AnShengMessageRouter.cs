using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using IoTPlatform.Data;
using IoTPlatform.Models;
using IoTPlatform.Services;
using IoTPlatform.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
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
    /// <c>getDelayTasks</c> 方法名（写后回读用，T8）。
    ///
    /// 与 <see cref="MethodGetDevStatus"/> 同理，路由层只在本地需要知道「哪个方法携带延时任务镜像」，
    /// 因此就地声明，不复用 <c>AnShengCommandCatalog</c> 也不反向依赖 <c>AnShengScheduleService</c> 的
    /// 私有常量（避免跨层耦合）。
    /// </summary>
    private const string MethodGetDelayTasks = "getDelayTasks";

    /// <summary>
    /// 设备应答缺少 <c>result</c> 字段时写入的错误码（T7-4）。
    ///
    /// 单列一个常量而不是复用设备原文，是为了让运维能按码聚合出「哪些固件在违反协议」，
    /// 而不用去 <c>LIKE '%result%'</c> 匹配自由文本。
    /// </summary>
    public const string MissingResultErrorCode = "NO_RESULT";

    /// <summary>
    /// <c>AnShengCommandRecord.ErrorCode</c> 的列长上限，落库前按它截断设备回包的 <c>result</c>。
    /// </summary>
    private const int ErrorCodeMaxLength = 64;

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
    private readonly IAnShengScheduleService _schedule;

    /// <summary>
    /// 构造路由器。
    /// </summary>
    /// <param name="pendingStore">在途命令表（Singleton，注入 Scoped 是允许方向）。</param>
    /// <param name="profileService">设备能力档案服务。</param>
    /// <param name="db">数据库上下文，用于分支动作后的 SaveChanges。</param>
    /// <param name="parser">报文解析器，用于把 <c>getDevStatus</c> 转成能力快照。</param>
    /// <param name="logger">日志器。</param>
    /// <param name="schedule">
    /// 延时任务调度服务（T8）。命令应答（action / actions / getDelayTasks / getDevStatus）携带的
    /// <c>slots[]</c> / <c>tasks[]</c> 快照由它写回平台镜像与档案（设计 D-H）。已注册为 Scoped，
    /// 与路由器同生命周期，由 DI 注入（非可选）。
    /// </param>
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
        IAnShengScheduleService schedule,
        AnShengEventDispatcher? dispatcher = null)
    {
        _pendingStore = pendingStore ?? throw new ArgumentNullException(nameof(pendingStore));
        _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
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
    /// Response 分支：摘除在途条目 → <b>回填命令记录终态</b> → 若报文是有效状态快照顺带刷档案。
    ///
    /// 【为什么应答也要刷档案】<c>getDevStatus</c> 的应答与自动上报是<b>同一份</b>状态数据，
    /// 只是触发方式不同。只在自动上报时刷档案，会让「刚探测过的设备档案反而更旧」。
    ///
    /// 【回填必须在摘除成功之后】<see cref="IAnShengPendingCommandStore.CompleteAsync"/> 返回
    /// 非 null 才代表本次调用<b>赢得了 CAS</b>（<c>ConcurrentDictionary.TryRemove</c>）。
    /// 返回 null 时条目已被超时清扫宿主摘走 —— 那条记录的终态归它写（<c>Timeout</c>），
    /// 本方法此时若还去写 <c>Succeeded</c>，就会把「已判超时」的命令改写成成功，
    /// 破坏 T7 §7 的「终态只由一个组件写」不变式。
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

            if (completed != null)
            {
                await BackfillCommandRecordAsync(completed, ctx, cancellationToken).ConfigureAwait(false);
            }
        }

        // T8-5（设计 D-H）：命令应答若携带 slots[] / tasks[] 快照，写回平台镜像与档案。
        // 后台作用域内的写回由 IAnShengScheduleService 内部 IgnoreQueryFilters + 显式 AppCode 定位。
        await ApplyResponseMirrorAsync(ctx.DeviceId, ctx.Method, ctx.Message, cancellationToken)
            .ConfigureAwait(false);

        await RefreshProfileAsync(ctx, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 把设备应答回填进 <see cref="AnShengCommandRecord"/>，写入终态
    /// <see cref="AnShengCommandStatus.Succeeded"/> / <see cref="AnShengCommandStatus.Failed"/>（T7-4）。
    ///
    /// 【AppCode 陷阱 —— 与清扫宿主同源的坑（T6 §7.2）】
    ///   本路由器虽是 Scoped，但它跑在 <c>AnShengUplinkPipeline</c> 用
    ///   <c>IServiceScopeFactory.CreateScope()</c> 造出来的<b>后台作用域</b>里，没有 HTTP 上下文 ⇒
    ///   <c>ITenantContextAccessor.Current</c> 为 null ⇒ <c>AppDbContext</c> 的全局租户过滤器
    ///   会把所有行都滤掉，普通查询<b>一行都查不到</b>（表现为「应答收到了但记录永远是 Sent」）。
    ///   故这里与 <c>AnShengCommandSweepHostedService</c> 一样：
    ///   <c>IgnoreQueryFilters()</c> + <b>按主键 <c>RecordId</c> 定位</b>。
    ///   主键定位天然跨租户安全 —— RecordId 是我们下发时自己写进在途表的，不来自设备。
    ///
    /// 【幂等：只更新 <c>Status IN (Pending, Sent)</c>】（设计文档 T7-4 要点 3）
    ///   设备重发应答、或人工干预已置过终态时，这道判定保证不会把 <c>Timeout</c> 改回
    ///   <c>Succeeded</c>。CAS 已经挡掉了绝大多数并发，这里是纵深防御。
    ///
    /// 【异常一律吞掉】回填失败不得冒泡：它会打断 <see cref="RefreshProfileAsync"/>，
    ///   进而让整条上行管道对该报文的处理半途而废。记录停在 Sent 由超时清扫兜底。
    /// </summary>
    /// <param name="completed">刚被摘除的在途条目（携带 <c>RecordId</c>）。</param>
    /// <param name="ctx">上行上下文，取应答原文与成败判据。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task BackfillCommandRecordAsync(
        PendingCommand completed,
        AnShengUplinkContext ctx,
        CancellationToken cancellationToken)
    {
        if (completed.RecordId <= 0)
        {
            // T6 遗留调用与纯内存单测登记的条目没有库记录，跳过是正常路径而非异常。
            _logger.LogDebug(
                "[AnShengRouter] 在途条目 {Imei}:{FrameId} 无 RecordId，跳过记录回填（未落库的登记）。",
                completed.Imei, completed.FrameId);
            return;
        }

        try
        {
            var record = await _db.AnShengCommandRecords
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(r => r.Id == completed.RecordId, cancellationToken)
                .ConfigureAwait(false);

            if (record == null)
            {
                _logger.LogWarning(
                    "[AnShengRouter] 找不到命令记录 Id={RecordId}（{Imei}:{FrameId}），跳过回填。",
                    completed.RecordId, completed.Imei, completed.FrameId);
                return;
            }

            if (record.Status != AnShengCommandStatus.Pending &&
                record.Status != AnShengCommandStatus.Sent)
            {
                _logger.LogDebug(
                    "[AnShengRouter] 记录 Id={RecordId} 已是终态 {Status}，跳过回填（终态互斥）。",
                    record.Id, record.Status);
                return;
            }

            var message = ctx.Message;
            var succeeded = message != null && message.IsOk;
            var now = DateTime.UtcNow;

            record.Status = succeeded ? AnShengCommandStatus.Succeeded : AnShengCommandStatus.Failed;
            record.CompletedAt = now;
            record.DurationMs = ComputeDurationMs(record.SentAt ?? completed.SentAt, now);

            // ★ 掩码集以「我方下发的 method」（record.Method）为准，而不是设备回包自称的 ctx.Method：
            //   否则设备只要在应答里谎报一个无敏感字段的 method，就能让口令绕过掩码原样落库。
            //   敏感性是命令语义决定的，不该由对端自述决定。
            record.ResponseJson = AnShengCommandRecord.TruncateJson(
                AnShengSecretMasker.MaskResponseJson(record.Method, message?.RawJson));

            if (!succeeded)
            {
                record.ErrorCode = NormalizeDeviceErrorCode(message?.Result);
                record.ErrorMessage = AnShengCommandRecord.TruncateErrorMessage(
                    $"设备应答失败：result={(string.IsNullOrWhiteSpace(message?.Result) ? "(缺失)" : message!.Result)}" +
                    $"（method={record.Method}，frameId={completed.FrameId}）");
            }

            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "[AnShengRouter] 命令记录回填完成 Id={RecordId} {Imei} {Method} => {Status}，耗时 {Duration}ms。",
                record.Id, record.Imei, record.Method, record.Status, record.DurationMs);
        }
        catch (Exception ex)
        {
            // 回填失败绝不冒泡：条目已从在途表摘除，记录会停在 Sent，
            // 由「Status=Sent 且 TimeoutAt < now」的运维巡检兜底（风险 R6）。
            _logger.LogError(ex,
                "[AnShengRouter] 回填命令记录失败 RecordId={RecordId} imei={Imei} frameId={FrameId}",
                completed.RecordId, completed.Imei, completed.FrameId);
        }
    }

    /// <summary>
    /// 把设备回包的 <c>result</c> 归一化成可落库的机器可读错误码。
    ///
    /// 【为什么不直接存原文】<c>AnShengCommandRecord.ErrorCode</c> 列长 64；
    /// 而 <c>result</c> 来自设备，长度不可信（异常固件可能回几 KB 的堆栈）。
    ///
    /// 【为什么 result 缺失也算失败】协议规定下行命令的应答<b>必带</b> <c>result</c>
    /// （目录里 <c>ResultOk</c> / <c>ResultUnsupported</c> 就是它的取值）。
    /// 缺失属于协议违例，静默判成功会把「设备行为异常」永久隐藏；
    /// 判 Failed 并给出专用错误码 <c>NO_RESULT</c>，运维一眼可辨、可按码聚合告警。
    /// </summary>
    /// <param name="result">设备回包的 <c>result</c> 字段，可为 null。</param>
    /// <returns>不超过 64 字符的错误码。</returns>
    private static string NormalizeDeviceErrorCode(string? result)
    {
        if (string.IsNullOrWhiteSpace(result))
        {
            return MissingResultErrorCode;
        }

        var trimmed = result.Trim();
        return trimmed.Length <= ErrorCodeMaxLength ? trimmed : trimmed[..ErrorCodeMaxLength];
    }

    /// <summary>
    /// 计算往返耗时（毫秒）。
    ///
    /// 与 <c>AnShengCommandSweepHostedService.ComputeDurationMs</c> 同口径：
    /// 时钟回拨造成的负值钳到 0，溢出钳到 <see cref="int.MaxValue"/>，
    /// 避免出现「耗时 -3 毫秒」这类让人怀疑数据可信度的值。
    /// </summary>
    /// <param name="sentAt">发布时刻（UTC），可为 null。</param>
    /// <param name="completedAt">终态时刻（UTC）。</param>
    /// <returns>毫秒数；无法计算时为 null。</returns>
    private static int? ComputeDurationMs(DateTime? sentAt, DateTime completedAt)
    {
        if (sentAt == null)
        {
            return null;
        }

        var ms = (completedAt - sentAt.Value).TotalMilliseconds;
        if (ms < 0)
        {
            return 0;
        }

        return ms > int.MaxValue ? int.MaxValue : (int)ms;
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

    /// <summary>
    /// 应答镜像写回钩子（T8-5，设计 D-H）。
    ///
    /// 在 <see cref="BackfillCommandRecordAsync"/>（终态回填）之后、<see cref="RefreshProfileAsync"/>
    /// （getDevStatus 能力刷新）之前调用。按 method 把设备应答里的 <c>slots[]</c> / <c>tasks[]</c>
    /// 分发到 <see cref="IAnShengScheduleService"/> 的快照 / 镜像写回：
    ///   · action / actions / getDevStatus 应答带 slots[]  ⇒ <c>UpdateSlotsSnapshotAsync</c>
    ///   · getDelayTasks 应答带 tasks[]（按下标 +1 推导插槽）         ⇒ <c>ApplyDelayTasksReadbackAsync</c>
    ///
    /// 【后台作用域】本方法运行在 AnShengUplinkPipeline 后台作用域，ITenantContextAccessor.Current 为 null；
    /// 因此写回路径上的所有 EF 查询都在 IAnShengScheduleService 内部 IgnoreQueryFilters + 显式 AppCode 定位（§7.1）。
    ///
    /// 【异常一律吞掉】镜像写回失败绝不可冒泡，否则会打断 RefreshProfileAsync 乃至整条上行链路。
    /// </summary>
    /// <param name="deviceId">平台设备主键，可为 null（未认领设备）。</param>
    /// <param name="method">应答方法名。</param>
    /// <param name="message">已解析的应答报文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task ApplyResponseMirrorAsync(
        long? deviceId, string method, AnShengMessage? message, CancellationToken cancellationToken)
    {
        if (!deviceId.HasValue || message == null || string.IsNullOrWhiteSpace(method))
        {
            return;
        }

        // 只有这四类应答携带可写回的快照 / 镜像数据。getDelayTasks 携带 tasks[]（非 slots[]），
        // 其余三类携带 slots[]；二者不重叠，避免把 getDelayTasks 的 tasks[] 误当 slots[] 写回档案。
        bool isDelayTasks = string.Equals(method, MethodGetDelayTasks, StringComparison.Ordinal);
        bool isSlotsCarrier = !isDelayTasks &&
            (string.Equals(method, "action", StringComparison.Ordinal) ||
             string.Equals(method, "actions", StringComparison.Ordinal) ||
             string.Equals(method, MethodGetDevStatus, StringComparison.Ordinal));

        if (!isDelayTasks && !isSlotsCarrier)
        {
            return;
        }

        try
        {
            var body = AnShengMessageParser.GetBodyJson(message);
            if (string.IsNullOrWhiteSpace(body))
            {
                return;
            }

            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (isSlotsCarrier)
            {
                var slots = ParseSlotsArray(root);
                if (slots != null)
                {
                    await _schedule.UpdateSlotsSnapshotAsync(deviceId.Value, slots, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            else // isDelayTasks
            {
                var tasks = ParseDelayTaskItems(root);
                if (tasks != null)
                {
                    await _schedule.ApplyDelayTasksReadbackAsync(deviceId.Value, tasks, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[AnShengRouter] 应答镜像写回失败 imei={Imei} method={Method}", message.Imei, method);
        }
    }

    /// <summary>
    /// 从应答报文体提取 <c>slots[]</c> 数组（int 列表，0=关 1=开），用于插槽状态快照写回。
    /// 报文不规范（缺字段 / 非数组）时返回 null，由上层按「本帧没带」跳过。
    /// </summary>
    /// <param name="root">报文体根元素。</param>
    /// <returns>插槽状态数组；无有效数据时为 null。</returns>
    private static List<int>? ParseSlotsArray(JsonElement root)
    {
        if (!root.TryGetProperty("slots", out var slotsEl) ||
            slotsEl.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var list = new List<int>(slotsEl.GetArrayLength());
        foreach (var item in slotsEl.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var v))
            {
                list.Add(v);
            }
        }

        return list.Count > 0 ? list : null;
    }

    /// <summary>
    /// 从 <c>getDelayTasks</c> 应答报文体提取 <c>tasks[]</c> 数组，映射为
    /// <see cref="AnShengDelayTaskItem"/> 列表（下标 i 对应插槽 i+1，由调度服务推导）。
    ///
    /// 设备字段名以请求报文（startDelayTask 的 enable/sAction/eAction/secs）为权威回显，
    /// 容错接受 sec/count 等同义写法；缺字段时回落默认值，绝不抛异常。
    /// </summary>
    /// <param name="root">报文体根元素。</param>
    /// <returns>延时任务项列表；无有效数据时为 null。</returns>
    private static List<AnShengDelayTaskItem>? ParseDelayTaskItems(JsonElement root)
    {
        if (!root.TryGetProperty("tasks", out var tasksEl) ||
            tasksEl.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var items = new List<AnShengDelayTaskItem>(tasksEl.GetArrayLength());
        foreach (var el in tasksEl.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            items.Add(new AnShengDelayTaskItem
            {
                Enable = TryGetBool(el, "enable"),
                SAction = TryGetString(el, "sAction") ?? "none",
                EAction = TryGetString(el, "eAction") ?? "off",
                Secs = TryGetInt(el, new[] { "secs", "sec" }),
                Cnt = TryGetInt(el, new[] { "cnt", "count" }),
            });
        }

        return items.Count > 0 ? items : null;
    }

    /// <summary>宽松读取布尔字段（支持 true/false、0/1、字符串）。</summary>
    private static bool TryGetBool(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var v))
        {
            return false;
        }

        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => v.TryGetInt32(out var n) && n != 0,
            JsonValueKind.String => bool.TryParse(v.GetString(), out var b) && b,
            _ => false,
        };
    }

    /// <summary>宽松读取字符串字段（空白回落 null）。</summary>
    private static string? TryGetString(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String)
        {
            var s = v.GetString();
            return string.IsNullOrEmpty(s) ? null : s;
        }

        return null;
    }

    /// <summary>宽松读取整数字段，按候选名依次尝试（支持数字 / 数字字符串）。</summary>
    private static int TryGetInt(JsonElement element, string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var v) &&
                ((v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)) ||
                 (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out n))))
            {
                return n;
            }
        }

        return 0;
    }
}
