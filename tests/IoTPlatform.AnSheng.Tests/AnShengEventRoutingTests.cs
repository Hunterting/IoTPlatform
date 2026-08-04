using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IoTPlatform.Data;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Infrastructure.Protocol.AnSheng;
using IoTPlatform.Models;
using IoTPlatform.Services;
using IoTPlatform.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IoTPlatform.AnSheng.Tests;

/// <summary>
/// T6 事件识别与处理管道 —— 路由判定单元测试（QA 独立编写，不复用工程师自测）。
///
/// 【覆盖的验收项】
///   · 验收 #1：七种事件报文经 <c>Classify</c> 均判为 <c>Event</c>；
///   · 验收 #2：<c>getDevStatus</c> 未知 frameId ⇒ AutoReport，在途 frameId ⇒ Response；
///   · 验收 #3：<c>delayEvent</c> 携带在途 frameId 仍判 Event，且<b>不消耗</b>在途条目。
///
/// 【为什么这些用例必须存在】
///   Router 的五级判定顺序是整条管道的分水岭：判错一条，事件就会被当成命令应答默默吞掉，
///   或反过来把命令回包写成设备事件污染溯源表。这类缺陷在运行期<b>没有任何异常</b>，
///   只能靠断言判定顺序本身来防守。
///
/// 【隔离】Classify 只依赖在途命令表；其余构造依赖（Profile / DbContext / Parser）
///   用 InMemory 与最小替身满足非空校验，不参与判定逻辑，避免测试与无关实现耦合。
/// </summary>
public sealed class AnShengEventRoutingTests : IDisposable
{
    private const string Imei = "864536072949900";
    private const string OtherImei = "864536072949901";

    /// <summary>协议附录 A.3 定义的全部上行事件方法（硬白名单 6 + 软白名单 1）。</summary>
    public static readonly string[] AllSevenEventMethods =
    {
        "connected", "close", "keyEvent", "delayEvent", "timeEvent", "recv485", "simCheck"
    };

    private readonly AppDbContext _db;
    private readonly AnShengPendingCommandStore _pending;
    private readonly AnShengMessageParser _parser = new();
    private readonly AnShengMessageRouter _router;

    public AnShengEventRoutingTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"router-{Guid.NewGuid():N}")
            .Options;
        _db = new AppDbContext(options);

        _pending = new AnShengPendingCommandStore(NullLogger<AnShengPendingCommandStore>.Instance);

        _router = new AnShengMessageRouter(
            _pending,
            new NoopProfileService(),
            _db,
            _parser,
            NullLogger<AnShengMessageRouter>.Instance,
            new NoopScheduleService());
    }

    public void Dispose()
    {
        _pending.ClearAll();
        _db.Dispose();
    }

    /// <summary>
    /// <see cref="IAnShengScheduleService"/> 的最小替身：T6 路由判定测试只关心 <c>Classify</c> 的五级顺序，
    /// 不触发任何写后回读 / 镜像更新路径，因此这里全部走 no-op，仅满足 Router 构造的非空校验。
    /// </summary>
    private sealed class NoopScheduleService : IAnShengScheduleService
    {
        public Task<AnShengDelayTaskResultDto> StartDelayTaskAsync(
            long deviceId, int slotNum, bool enable, string sAction, string eAction, int secs,
            CancellationToken ct = default) =>
            Task.FromResult(new AnShengDelayTaskResultDto());

        public Task<AnShengDelayTaskResultDto> StopDelayTaskAsync(
            long deviceId, int slotNum, CancellationToken ct = default) =>
            Task.FromResult(new AnShengDelayTaskResultDto());

        public Task<List<AnShengDelayTaskDto>> GetDelayTasksAsync(
            long deviceId, CancellationToken ct = default) =>
            Task.FromResult(new List<AnShengDelayTaskDto>());

        public Task ApplyDelayTasksReadbackAsync(
            long deviceId, IReadOnlyList<AnShengDelayTaskItem> tasks, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task ApplyDelayEventAsync(
            long deviceId, int slotNum, IReadOnlyList<int>? slots, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task UpdateSlotsSnapshotAsync(
            long deviceId, IReadOnlyList<int> slots, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    // ─────────────────────────────────────────────────────────────
    // 验收 #1：七种事件报文全部判为 Event
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 验收 #1：A.3 的 7 个上行事件方法，在<b>无在途 frameId</b> 的常态下必须全部判为 Event。
    /// 少一个都意味着该类事件永远进不了责任链，事件溯源表会出现整类缺口。
    /// </summary>
    [Theory]
    [InlineData("connected")]
    [InlineData("close")]
    [InlineData("keyEvent")]
    [InlineData("delayEvent")]
    [InlineData("timeEvent")]
    [InlineData("recv485")]
    [InlineData("simCheck")]
    public void Classify_Should_Return_Event_For_All_Seven_Uplink_Methods(string method)
    {
        var result = _router.Classify(Context(method));

        Assert.Equal(AnShengRouteKind.Event, result.Kind);
        Assert.True(result.IsEvent);
        Assert.Equal(method, result.Method);
        Assert.Equal(Imei, result.Imei);
    }

    /// <summary>
    /// 验收 #1 补强：即使报文<b>携带</b> frameId（但不在途），事件方法依然是 Event。
    ///
    /// 【为什么单独测】固件会在部分事件里带上 frameId 作为流水号。若实现把
    /// 「有 frameId」当成「是命令应答」的充分条件，这批事件会被整体误判为 AutoReport。
    /// </summary>
    [Theory]
    [InlineData("connected")]
    [InlineData("close")]
    [InlineData("keyEvent")]
    [InlineData("delayEvent")]
    [InlineData("timeEvent")]
    [InlineData("recv485")]
    public void Classify_Should_Return_Event_For_Hard_Whitelist_Even_With_Unknown_FrameId(string method)
    {
        var result = _router.Classify(Context(method, frameId: "1745456483900"));

        Assert.Equal(AnShengRouteKind.Event, result.Kind);
    }

    // ─────────────────────────────────────────────────────────────
    // 验收 #2：getDevStatus 的 frameId 分流
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 验收 #2a：<c>getDevStatus</c> 携带<b>未知</b> frameId ⇒ AutoReport。
    ///
    /// 设备重启后可能沿用旧流水号，平台侧在途表早已过期；此时必须退化为自动上报，
    /// 而不是被当成某条已消失命令的应答而丢弃。
    /// </summary>
    [Fact]
    public void Classify_GetDevStatus_With_Unknown_FrameId_Should_Be_AutoReport()
    {
        var result = _router.Classify(Context("getDevStatus", frameId: "9999999999999999"));

        Assert.Equal(AnShengRouteKind.AutoReport, result.Kind);
        Assert.Contains("不在途", result.Reason);
    }

    /// <summary>验收 #2a 变体：<c>getDevStatus</c> 完全不带 frameId ⇒ AutoReport。</summary>
    [Fact]
    public void Classify_GetDevStatus_Without_FrameId_Should_Be_AutoReport()
    {
        var result = _router.Classify(Context("getDevStatus"));

        Assert.Equal(AnShengRouteKind.AutoReport, result.Kind);
    }

    /// <summary>
    /// 验收 #2b：<c>getDevStatus</c> 携带<b>在途</b> frameId ⇒ Response。
    /// 这是平台主动查询的回包，必须摘在途条目而不是写成事件。
    /// </summary>
    [Fact]
    public void Classify_GetDevStatus_With_InFlight_FrameId_Should_Be_Response()
    {
        const string frameId = "1745456483901";
        Register(Imei, frameId, "getDevStatus");

        var result = _router.Classify(Context("getDevStatus", frameId: frameId));

        Assert.Equal(AnShengRouteKind.Response, result.Kind);
    }

    /// <summary>
    /// 在途表必须按 <c>imei + frameId</c> 联合隔离。
    ///
    /// 【为什么是必测项】两台设备并发时 frameId 有真实碰撞概率（安圣 frameId 取自毫秒时间戳）。
    /// 若 key 只用 frameId，设备 B 的自动上报会被设备 A 的在途命令「认领」为应答，
    /// 表现为 B 的数据凭空消失——极难定位。
    /// </summary>
    [Fact]
    public void Classify_Should_Not_Match_InFlight_FrameId_Across_Devices()
    {
        const string frameId = "1745456483902";
        Register(Imei, frameId, "getDevStatus");

        var result = _router.Classify(Context("getDevStatus", imei: OtherImei, frameId: frameId));

        Assert.Equal(AnShengRouteKind.AutoReport, result.Kind);
    }

    // ─────────────────────────────────────────────────────────────
    // 验收 #3：delayEvent 带在途 frameId 仍是 Event
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 验收 #3：<c>delayEvent</c> 即便 frameId 命中在途表，仍必须判为 Event。
    ///
    /// 【判定顺序的核心断言】硬白名单必须在 frameId 判断<b>之前</b>短路。
    /// 协议 asopen.md:2291 明确 delayEvent 携带 frameId，但它是设备主动上报的延时事件，
    /// 平台根本无法下发同名命令；一旦被判成 Response，延时告警会整类失踪。
    /// </summary>
    [Fact]
    public void Classify_DelayEvent_With_InFlight_FrameId_Should_Still_Be_Event()
    {
        const string frameId = "1745456483903";
        Register(Imei, frameId, "delayEvent");

        var result = _router.Classify(Context("delayEvent", frameId: frameId));

        Assert.Equal(AnShengRouteKind.Event, result.Kind);
        Assert.Contains("硬事件白名单", result.Reason);
    }

    /// <summary>
    /// 验收 #3 补强：判为 Event 的硬白名单方法<b>不得消耗</b>在途条目。
    ///
    /// 若 Classify 顺手摘了条目，那条真正在途的命令就会永远等不到应答而超时——
    /// 一个「事件正确 + 命令莫名超时」的复合缺陷。
    /// </summary>
    [Fact]
    public void Classify_DelayEvent_Should_Not_Consume_InFlight_Entry()
    {
        const string frameId = "1745456483904";
        Register(Imei, frameId, "someCommand");

        _router.Classify(Context("delayEvent", frameId: frameId));

        Assert.True(_pending.IsInFlight(Imei, frameId), "硬白名单事件不应摘除在途条目");
        Assert.Equal(1, _pending.Count);
    }

    // ─────────────────────────────────────────────────────────────
    // simCheck 双向语义
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// simCheck 是<b>双向</b>方法：带在途 frameId ⇒ 我方查询的应答（Response）。
    /// </summary>
    [Fact]
    public void Classify_SimCheck_With_InFlight_FrameId_Should_Be_Response()
    {
        const string frameId = "1745456483905";
        Register(Imei, frameId, "simCheck");

        var result = _router.Classify(Context("simCheck", frameId: frameId));

        Assert.Equal(AnShengRouteKind.Response, result.Kind);
        Assert.Contains("软白名单", result.Reason);
    }

    /// <summary>
    /// simCheck 不带在途 frameId ⇒ 设备主动上报（Event）。
    /// 与上一条共同锁死「双向语义」，任何一侧退化都会被检出。
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("8888888888888888")]
    public void Classify_SimCheck_Without_InFlight_FrameId_Should_Be_Event(string? frameId)
    {
        var result = _router.Classify(Context("simCheck", frameId: frameId));

        Assert.Equal(AnShengRouteKind.Event, result.Kind);
    }

    // ─────────────────────────────────────────────────────────────
    // 防漂移护栏
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 防漂移：<b>硬白名单 ∪ 软白名单 == 附录 A.3 的 7 个上行事件方法</b>。
    ///
    /// 【为什么必须有这条】既有 <c>AnShengProtocolConformanceTests</c> 断言
    /// 「<c>AnShengCommandCatalog.IsEvent("simCheck")</c> 为 false」（simCheck 是可下发命令），
    /// 该断言在 T6 之后<b>依然为真</b>——Router 的软白名单是路由层概念，不改命令目录。
    /// 但正因为真相分散在两处，将来给目录加事件方法却忘了同步 Router（或反之）不会有任何报错。
    /// 本用例把两处并起来与协议清单对账，是唯一能捕获该漂移的护栏。
    /// </summary>
    [Fact]
    public void Hard_And_Soft_Whitelists_Union_Should_Equal_The_Seven_Protocol_Event_Methods()
    {
        var expected = AllSevenEventMethods.OrderBy(m => m, StringComparer.Ordinal).ToArray();
        var actual = AnShengMessageRouter.AllEventMethods
            .OrderBy(m => m, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// 防漂移：硬白名单与软白名单<b>不得重叠</b>。
    /// 重叠意味着某方法同时受两级判定管辖，第 2 级永远成为死代码，simCheck 的
    /// 「在途 ⇒ Response」语义会静默失效。
    /// </summary>
    [Fact]
    public void Hard_And_Soft_Whitelists_Should_Not_Overlap()
    {
        var overlap = AnShengCommandCatalog.EventMethods
            .Intersect(AnShengMessageRouter.SoftEventMethods, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(overlap);
    }

    /// <summary>
    /// 与既有一致性测试的交叉验证：simCheck 在<b>命令目录</b>里仍是可下发命令（非事件），
    /// 同时在<b>路由层</b>属于软事件白名单。两者并存正是双向语义的实现方式。
    /// </summary>
    [Fact]
    public void SimCheck_Remains_Downlink_Command_In_Catalog_While_Being_Soft_Event_In_Router()
    {
        Assert.False(AnShengCommandCatalog.IsEvent("simCheck"));
        Assert.Contains("simCheck", AnShengMessageRouter.SoftEventMethods);
    }

    // ─────────────────────────────────────────────────────────────
    // Ignored 分支
    // ─────────────────────────────────────────────────────────────

    /// <summary>报文解析失败（Message 为 null）⇒ Ignored，不得抛异常。</summary>
    [Fact]
    public void Classify_Should_Return_Ignored_When_Message_Is_Null()
    {
        var ctx = new AnShengUplinkContext(Imei, null, "not-a-json", DateTime.UtcNow);

        var result = _router.Classify(ctx);

        Assert.Equal(AnShengRouteKind.Ignored, result.Kind);
        Assert.Equal("报文解析失败", result.Reason);
    }

    /// <summary>method 为空串 ⇒ Ignored。</summary>
    [Fact]
    public void Classify_Should_Return_Ignored_When_Method_Is_Empty()
    {
        var message = _parser.Parse($"{{\"method\":\"connected\",\"imei\":\"{Imei}\"}}");
        Assert.NotNull(message);

        var ctx = new AnShengUplinkContext(Imei, message, "{}", DateTime.UtcNow) { Method = string.Empty };

        var result = _router.Classify(ctx);

        Assert.Equal(AnShengRouteKind.Ignored, result.Kind);
        Assert.Equal("method 为空", result.Reason);
    }

    /// <summary>Classify 对 null 上下文必须快速失败，而不是返回一个语义模糊的结果。</summary>
    [Fact]
    public void Classify_Should_Throw_On_Null_Context()
    {
        Assert.Throws<ArgumentNullException>(() => _router.Classify(null!));
    }

    // ─────────────────────────────────────────────────────────────
    // 在途命令表行为
    // ─────────────────────────────────────────────────────────────

    /// <summary>过期条目应被惰性摘除，使对应上行自然退化为 AutoReport。</summary>
    [Fact]
    public void Expired_Pending_Entry_Should_Degrade_To_AutoReport()
    {
        const string frameId = "1745456483906";
        _pending.TryRegister(Imei, frameId,
            PendingCommand.Create(1, Imei, frameId, "getDevStatus", TimeSpan.FromMilliseconds(-1)));

        var result = _router.Classify(Context("getDevStatus", frameId: frameId));

        Assert.Equal(AnShengRouteKind.AutoReport, result.Kind);
        Assert.False(_pending.IsInFlight(Imei, frameId));
    }

    /// <summary><c>CompleteAsync</c> 摘条目后，同一 frameId 的后续上行应退化为 AutoReport。</summary>
    [Fact]
    public async Task Completed_Pending_Entry_Should_Degrade_To_AutoReport()
    {
        const string frameId = "1745456483907";
        Register(Imei, frameId, "getDevStatus");

        var completed = await _pending.CompleteAsync(Imei, frameId, null);
        Assert.NotNull(completed);

        var result = _router.Classify(Context("getDevStatus", frameId: frameId));

        Assert.Equal(AnShengRouteKind.AutoReport, result.Kind);
    }

    // ─────────────────────────────────────────────────────────────
    // 辅助
    // ─────────────────────────────────────────────────────────────

    private void Register(string imei, string frameId, string method)
    {
        var ok = _pending.TryRegister(imei, frameId,
            PendingCommand.Create(1, imei, frameId, method, TimeSpan.FromSeconds(30)));
        Assert.True(ok, "在途命令登记失败，用例前置条件不成立");
    }

    /// <summary>用真实 Parser 从 JSON 构造上下文，确保 Method/FrameId 的取值路径与生产一致。</summary>
    private AnShengUplinkContext Context(string method, string? imei = null, string? frameId = null)
    {
        var deviceImei = imei ?? Imei;
        var json = frameId == null
            ? $"{{\"method\":\"{method}\",\"imei\":\"{deviceImei}\",\"timestamp\":1745456483}}"
            : $"{{\"method\":\"{method}\",\"imei\":\"{deviceImei}\",\"frameId\":\"{frameId}\",\"timestamp\":1745456483}}";

        var message = _parser.Parse(json);
        Assert.NotNull(message);

        return new AnShengUplinkContext(deviceImei, message, json, DateTime.UtcNow);
    }

    /// <summary>
    /// 最小 Profile 服务替身：仅满足 Router 构造函数的非空校验。
    ///
    /// <c>Classify</c> 完全不触碰档案服务；若将来实现开始依赖它，这里的
    /// <c>NotSupportedException</c> 会立刻让用例爆红，提醒 QA 补齐替身语义，
    /// 而不是让测试在「档案恒为 null」的假设下静默通过。
    /// </summary>
    private sealed class NoopProfileService : IAnShengDeviceProfileService
    {
        public Task<AnShengDeviceProfile?> GetByImeiAsync(string imei, CancellationToken cancellationToken = default)
            => Task.FromResult<AnShengDeviceProfile?>(null);

        public Task<AnShengDeviceProfile?> GetByDeviceIdAsync(long deviceId, CancellationToken cancellationToken = default)
            => Task.FromResult<AnShengDeviceProfile?>(null);

        public Task<AnShengDeviceProfile> GetOrCreateAsync(string imei, string appCode, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Classify 不应调用 GetOrCreateAsync");

        public (AnShengDeviceKind Kind, AnShengKindSource Source) ResolveKind(
            AnShengDeviceProfile? profile,
            AnShengCapabilitySnapshot snapshot,
            AnShengDeviceKind? manualKind = null)
            => throw new NotSupportedException("Classify 不应调用 ResolveKind");

        public Task<AnShengDeviceProfile> ApplyProbeAsync(
            string imei,
            string appCode,
            AnShengCapabilitySnapshot snapshot,
            AnShengDeviceKind? manualKind = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Classify 不应调用 ApplyProbeAsync");

        public Task<AnShengDeviceProfile> ApplyProbeFailureAsync(
            string imei,
            string appCode,
            string? error,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Classify 不应调用 ApplyProbeFailureAsync");

        public Task<AnShengDeviceProfile?> RefreshAsync(
            string imei,
            string appCode,
            AnShengCapabilitySnapshot snapshot,
            CancellationToken cancellationToken = default)
            => Task.FromResult<AnShengDeviceProfile?>(null);

        public void AttachDevice(AnShengDeviceProfile profile, long deviceId)
            => throw new NotSupportedException("Classify 不应调用 AttachDevice");
    }
}
