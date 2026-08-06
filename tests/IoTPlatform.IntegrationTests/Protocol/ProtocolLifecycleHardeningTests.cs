using System;
using System.Threading.Tasks;
using FluentAssertions;
using IoTPlatform.IntegrationTests.Infrastructure;
using IoTPlatform.IntegrationTests.Infrastructure.Mqtt;
using IoTPlatform.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IoTPlatform.IntegrationTests.Protocol;

/// <summary>
/// 协议启停「短路判活」加固的集成验收 —— 补齐缺口 A / B / C。
///
/// ═══════════════════════════════════════════════════════════════════════
/// 【这三条分支此前为什么是零覆盖】
/// ═══════════════════════════════════════════════════════════════════════
/// <c>ProtocolConfigService</c> 的启停短路从「只看 DB 的 Status」升级为
/// 「DB 状态 + 进程内适配器」双条件后，新增了三处只有在<b>状态失配</b>时才走到的分支：
///   · A 判活恢复：<c>Status=="active"</c> 但 <c>GetAdapter==null</c> → 必须重新拉起；
///   · B 对称停止清理：<c>Status=="inactive"</c> 但 <c>GetAdapter!=null</c> → 必须完整清理；
///   · C 订阅去重：放宽短路后「已 active 再走一遍完整启动」会真实发生，
///     必须区分「同实例跳过」与「旧实例解绑重挂」。
/// 而 <c>FakeProtocolAdapterFactory.GetAdapter</c> 原先恒返回非 null，
/// <b>物理上构造不出失配现场</b>，这三条分支写了等于没验。
/// 工程师已为替身补上 <c>SimulateAdapterAbsent</c> / <c>SimulateAdapterRebuilt</c> 等开关，
/// 本文件就是用它们把「零覆盖」变成「真验证」。
///
/// ═══════════════════════════════════════════════════════════════════════
/// 【每条正向用例都配一条证伪对照，理由】
/// ═══════════════════════════════════════════════════════════════════════
/// 「走了恢复启动」的断言锚点是 <c>Adapter.DataCollectionStarted == true</c>。
/// 单看它无法排除「其实每次 Start 都会走完整流程、短路根本没生效」这种情况 ——
/// 那样断言恒真，用例是假绿。所以 A / B 各配一条对照用例：
/// 在<b>不制造失配</b>的前提下断言短路确实发生（同一锚点取反）。
/// 两条一起看，才能证明被测的是「失配时自愈」而不是「永远重跑」。
///
/// ═══════════════════════════════════════════════════════════════════════
/// 【落库断言为什么用 marker，而不是数行数】
/// ═══════════════════════════════════════════════════════════════════════
/// 数据桥是 fire-and-forget，一个用例里会先后投多条报文；
/// 只数 <c>DeviceDataRecords.Count</c> 分不清「哪条报文落的」，
/// 幽灵订阅带来的多余记录与正常记录会混成一个数字。
/// 因此每条报文都带唯一 <c>qa_marker</c>，断言直接落在「该 marker 有几行」上，
/// 既能表达「必须有」也能表达「必须没有」，且不受其它用例残留影响。
/// </summary>
[Collection(SharedTestConstants.CollectionName)]
public sealed class ProtocolLifecycleHardeningTests : IntegrationTestBase
{
    /// <summary>等待「异步副作用落库」的上限。真实 MySQL 首次建连 + EF 首次编译查询可能慢，给足余量。</summary>
    private static readonly TimeSpan LandTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// 负向断言（「这条报文<b>不该</b>落库」）的静置观察窗。
    ///
    /// 【为什么不是纯睡 3 秒赌运气】
    ///   B / C 的负向断言之前都先有一条<b>正向对照</b>已经落库成功（同一条数据桥、同一台设备），
    ///   证明「链路此刻是通的、耗时远小于本窗口」。在此前提下再静置 3 秒仍未出现，
    ///   才能判定「确实没落」而不是「还没来得及落」。
    ///   （A-2 那条没有同用例内的正向对照，因为它的主断言是瞬时的
    ///   <c>DataCollectionStarted == false</c>，marker 只是补刀。）
    /// </summary>
    private static readonly TimeSpan QuietWindow = TimeSpan.FromSeconds(3);

    public ProtocolLifecycleHardeningTests(DatabaseFixture fixture) : base(fixture)
    {
    }

    /// <summary>本用例播种出的协议配置主键（生产侧按 <c>int</c> 使用）。</summary>
    private int ConfigId => (int)Seed.ProtocolConfigId;

    /// <summary>替身工厂：失配现场的开关面板。</summary>
    private FakeProtocolAdapterFactory AdapterFactory => Fixture.AdapterFactory;

    // ══════════════════════════════════════════════════════════════════
    // 缺口 A：判活恢复 —— DB 说 active，但适配器不在内存
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// A-1：<c>Status=="active"</c> 且适配器不在内存（模拟进程重启）时，
    /// <c>StartProtocolAsync</c> 必须走<b>完整启动流程</b>把适配器真正拉起来，而不是短路返回。
    ///
    /// 【三个断言各自证明什么，缺一不可】
    ///   ① <c>DataCollectionStarted == true</c> —— 证明 <c>ConnectAsync + StartDataCollectionAsync</c>
    ///      真的被调用了。它是「短路 vs 完整流程」最直接的分水岭：短路分支一行都不会执行。
    ///   ② <c>IsSimulatedAbsent == false</c> —— 替身的 <c>CreateAdapter</c> 会撤销缺席标记，
    ///      所以标记消失等价于「<c>CreateAdapter</c> 被调用过」，与 ① 互为独立佐证：
    ///      即使将来有人把 <c>StartDataCollectionAsync</c> 挪走，这条仍然守得住。
    ///   ③ marker 报文落库 —— 证明恢复启动<b>顺带把数据桥订阅也重建了</b>。
    ///      这才是运维真正在意的东西：适配器起来了但没订阅，等于起了个哑巴，
    ///      broker 上有报文、库里一条记录都没有，与「没启动」在现象上完全一致。
    ///
    /// 【为什么不改库】本例刻意保持播种出的 <c>Status="active"</c> 原样 ——
    /// 失配现场的定义就是「DB 说活、内存说没有」，先改成 inactive 再启动那是普通启动路径，
    /// 测不到恢复分支。
    /// </summary>
    [Fact(DisplayName = "缺口A-1 DB=active 但适配器不在内存 → StartProtocol 走恢复启动(采集启动+重建数据桥订阅)")]
    public async Task GapA_StartProtocol_WhenDbActiveButAdapterAbsent_Should_PerformRecoveryStartup()
    {
        // ── Arrange：构造「DB 说 active、内存里没有适配器」的失配现场 ──
        var initialState = await ReadProtocolStateAsync();
        initialState.Status.Should().Be(
            "active", "SeedData 播种的协议配置本就是 active —— 失配现场必须保留这一半，不能改库");

        AdapterFactory.SimulateAdapterAbsent(ConfigId);
        AdapterFactory.IsSimulatedAbsent(ConfigId).Should().BeTrue(
            "缺席开关没生效的话，后面测的就只是一次普通的重复启动，恢复分支根本不会被触达");
        AdapterFactory.PeekAdapter(ConfigId).Should().BeNull("缺席期间解析适配器必须为 null");

        Adapter.DataCollectionStarted.Should().BeFalse(
            "每个用例前 AdapterFactory.Reset() 都会复位采集标志——前置不干净会让下面的断言恒真");

        // ── Act ───────────────────────────────────────────────────────
        await StartProtocolAsync();

        // ── Assert ① 适配器真的被拉起 ──────────────────────────────────
        Adapter.DataCollectionStarted.Should().BeTrue(
            "DB 已 active 但适配器不在内存时必须自愈重启；" +
            "若为 false，说明短路又退回成「只看 Status」，接口会返回 200 而适配器压根没起来");

        // ── Assert ② CreateAdapter 确实被调用（缺席标记被撤销）──────────
        AdapterFactory.IsSimulatedAbsent(ConfigId).Should().BeFalse(
            "替身的 CreateAdapter 会撤销缺席标记；标记还在，说明恢复分支没走到创建适配器这一步");

        // ── Assert ③ 数据桥订阅一并重建 ────────────────────────────────
        const string marker = "gapA1-recovered-bridge";
        Adapter.RaiseDataReceived(0L, Seed.Imei, MarkerPayload(marker), SharedTestConstants.AppCode);

        (await WaitForMarkerAsync(marker)).Should().BeTrue(
            "恢复启动必须连同 DataReceived 订阅一起重建；" +
            "适配器起来了却没订阅 = 哑巴适配器，现象与没启动完全一致（broker 有报文、库里没记录）");

        // ── Assert ④ 收尾状态自洽 ─────────────────────────────────────
        var finalState = await ReadProtocolStateAsync();
        finalState.Status.Should().Be("active", "恢复启动结束后 DB 状态应保持 active");
        finalState.IsActive.Should().BeTrue("Status 与 IsActive 必须同步，否则消费侧筛不到这条配置");
    }

    /// <summary>
    /// A-2（证伪对照）：<c>Status=="active"</c> <b>且</b>适配器在内存时，
    /// <c>StartProtocolAsync</c> 必须短路返回，不得重复执行启动动作。
    ///
    /// 【本例的唯一职责是给 A-1 兜底】
    ///   若没有它，A-1 里那句 <c>DataCollectionStarted == true</c> 有可能是因为
    ///   「Start 永远走完整流程」而恒真 —— 那样 A-1 是假绿，且加固代码即使被整段删掉也照样绿。
    ///   本例断言同一个锚点<b>为 false</b>，两条一起才构成「失配才自愈、不失配就短路」的完整语义。
    ///
    /// 【顺带守住重复启动的副作用】
    ///   短路失效不只是多跑一遍：它会让同一份报文被处理两次（重复落库 + 重复触发上线通知），
    ///   正是订阅去重分支要拦的那种事故。这里在源头再加一道。
    /// </summary>
    [Fact(DisplayName = "缺口A-2(证伪) DB=active 且适配器在内存 → StartProtocol 必须短路(不重复启动采集)")]
    public async Task GapA_StartProtocol_WhenDbActiveAndAdapterPresent_Should_ShortCircuit()
    {
        // ── Arrange：不制造任何失配 ────────────────────────────────────
        (await ReadProtocolStateAsync()).Status.Should().Be("active");
        AdapterFactory.IsSimulatedAbsent(ConfigId).Should().BeFalse("本例刻意不打缺席标记");
        AdapterFactory.PeekAdapter(ConfigId).Should().NotBeNull("默认替身对任意 configId 都在内存");
        Adapter.DataCollectionStarted.Should().BeFalse();

        // ── Act ───────────────────────────────────────────────────────
        await StartProtocolAsync();

        // ── Assert ────────────────────────────────────────────────────
        Adapter.DataCollectionStarted.Should().BeFalse(
            "双条件都满足（DB 说活 + 内存有适配器）时必须短路；" +
            "这里若为 true，说明每次 Start 都在重跑完整流程，A-1 的同名断言随之退化成恒真式");

        const string marker = "gapA2-shortcircuit-no-bridge";
        Adapter.RaiseDataReceived(0L, Seed.Imei, MarkerPayload(marker), SharedTestConstants.AppCode);

        await AssertMarkerCountStaysAtMostAsync(marker, 0,
            "短路返回意味着没有建立任何订阅，这条报文不该产生落库副作用");
    }

    // ══════════════════════════════════════════════════════════════════
    // 缺口 B：对称停止清理 —— DB 说 inactive，但适配器仍在内存
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// B-1：<c>Status=="inactive"</c> 但适配器仍残留在内存时，
    /// <c>StopProtocolAsync</c> 必须走<b>完整停止流程</b>：反注册订阅 + 释放适配器 + 回落状态。
    ///
    /// 【被防的是什么事故】
    ///   启动中途抛异常（Status 没落到 active）或外部直接改库，都会造出这个失配。
    ///   若停止被短路吞掉，适配器会泄漏在进程里继续连着 broker、继续往采集管道灌数据，
    ///   而管理界面显示「已停止」—— 一个查不出来源的幽灵数据源。
    ///
    /// 【断言链路】
    ///   ① <c>WasReleased == true</c> —— <c>ReleaseAdapter</c> 是完整停止流程唯一的外部可观测副作用；
    ///   ② 停止后再投报文<b>不落库</b> —— 证明事件订阅确实被反注册（幽灵数据源已掐断）；
    ///      为让这条负向断言站得住，停止前先投了一条同构报文并确认它<b>落了</b>（正向对照）；
    ///   ③ Status / IsActive 双双回落 —— 消费侧筛的是 IsActive，只改 Status 等于没停。
    /// </summary>
    [Fact(DisplayName = "缺口B-1 DB=inactive 但适配器残留 → StopProtocol 完整清理(释放适配器+反注册订阅+状态回落)")]
    public async Task GapB_StopProtocol_WhenDbInactiveButAdapterResident_Should_ReleaseAndUnsubscribe()
    {
        // ── Arrange ①：先正常启动一次，建立真实的数据桥订阅 ─────────────
        await SetProtocolStatusAsync("inactive");
        await StartProtocolAsync();
        Adapter.DataCollectionStarted.Should().BeTrue("前置启动必须成功，否则后面停的是个空气");

        // 正向对照：证明此刻数据桥是通的，且落库耗时远小于静置窗口。
        // 没有这一步，后面「停止后不落库」就可能只是「还没来得及落」。
        const string liveMarker = "gapB1-before-stop-live";
        Adapter.RaiseDataReceived(0L, Seed.Imei, MarkerPayload(liveMarker), SharedTestConstants.AppCode);
        (await WaitForMarkerAsync(liveMarker)).Should().BeTrue(
            "停止之前数据桥必须是通的，否则「停止后不落库」这条断言毫无证明力");

        // ── Arrange ②：直接改库制造失配（适配器仍在内存）───────────────
        await SetProtocolStatusAsync("inactive");
        (await ReadProtocolStateAsync()).Status.Should().Be("inactive");
        AdapterFactory.PeekAdapter(ConfigId).Should().NotBeNull(
            "失配现场的另一半：适配器必须还在内存里，否则走的是短路而非清理分支");
        AdapterFactory.WasReleased(ConfigId).Should().BeFalse("停止之前不应有任何释放留痕");

        // ── Act ───────────────────────────────────────────────────────
        await StopProtocolAsync();

        // ── Assert ① 适配器被释放 ─────────────────────────────────────
        AdapterFactory.WasReleased(ConfigId).Should().BeTrue(
            "DB 已 inactive 但适配器仍在内存时必须完整清理；" +
            "若为 false，说明停止被短路吞掉，适配器会作为幽灵数据源继续灌数据");
        AdapterFactory.ReleasedConfigIds.Should().Contain(ConfigId);

        // ── Assert ② 事件订阅确已反注册 ───────────────────────────────
        const string ghostMarker = "gapB1-after-stop-ghost";
        Adapter.RaiseDataReceived(0L, Seed.Imei, MarkerPayload(ghostMarker), SharedTestConstants.AppCode);

        await AssertMarkerCountStaysAtMostAsync(ghostMarker, 0,
            "停止后适配器上不应再挂着任何 handler；" +
            "这条报文一旦落库，就说明反注册失效、幽灵订阅仍在往采集管道灌数据");

        // ── Assert ③ 状态回落自洽 ─────────────────────────────────────
        var finalState = await ReadProtocolStateAsync();
        finalState.Status.Should().Be("inactive");
        finalState.IsActive.Should().BeFalse(
            "IsActive 必须与 Status 同步回落，否则 AnShengDiscoveryService / CommandService 仍会选中这条已停止的配置");
    }

    /// <summary>
    /// B-2（证伪对照）：<c>Status=="inactive"</c> <b>且</b>适配器不在内存时，
    /// <c>StopProtocolAsync</c> 必须短路返回，不得产生任何释放动作。
    ///
    /// 【给 B-1 兜底】
    ///   若 Stop 无论如何都会走完整流程，B-1 的 <c>WasReleased == true</c> 就是恒真式。
    ///   本例断言同一锚点<b>为 false</b>，两条合起来才说明「失配才清理、不失配就短路」。
    ///
    /// 【为什么要显式打缺席标记】
    ///   替身的 <c>ReleaseAdapter</c> <b>刻意不</b>自动置缺席（那会改变既有数百条用例的默认语义），
    ///   所以「适配器不在内存」这一半必须由用例自己声明。
    /// </summary>
    [Fact(DisplayName = "缺口B-2(证伪) DB=inactive 且适配器不在内存 → StopProtocol 必须短路(零释放动作)")]
    public async Task GapB_StopProtocol_WhenDbInactiveAndAdapterAbsent_Should_ShortCircuit()
    {
        // ── Arrange：双条件都指向「已停止」──────────────────────────────
        await SetProtocolStatusAsync("inactive");
        AdapterFactory.SimulateAdapterAbsent(ConfigId);
        AdapterFactory.PeekAdapter(ConfigId).Should().BeNull();
        AdapterFactory.WasReleased(ConfigId).Should().BeFalse();

        // ── Act ───────────────────────────────────────────────────────
        await StopProtocolAsync();

        // ── Assert ────────────────────────────────────────────────────
        AdapterFactory.WasReleased(ConfigId).Should().BeFalse(
            "DB 说停、内存也确实没有适配器 —— 这是真·已停止，必须短路；" +
            "这里若为 true，说明 Stop 永远在跑完整流程，B-1 的同名断言随之退化成恒真式");
        AdapterFactory.ReleasedConfigIds.Should().BeEmpty();
    }

    // ══════════════════════════════════════════════════════════════════
    // 缺口 C：订阅去重 —— 同实例跳过 / 旧实例解绑重挂
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// C-①：登记的适配器与本次要挂载的是<b>同一个实例</b>时，必须跳过重复订阅 ——
    /// 一条报文只能落一行，不能落两行。
    ///
    /// 【现场怎么造出来的】
    ///   放宽短路后，「DB 已 active、适配器却不在内存」会真的再走一遍完整启动流程。
    ///   而替身的 <c>CreateAdapter</c> 返回的仍是同一个 <c>DefaultAdapter</c>，
    ///   于是「同实例上已有登记」这个分支被精确命中 —— 这正是生产进程重启恢复时的真实形态。
    ///
    /// 【为什么必须拦】
    ///   不拦的话同一份报文会被处理两次：<c>DeviceDataRecord</c> 重复落库，
    ///   且 <c>OnDeviceOnlineAsync</c> 被重复触发，直接加剧安圣设备已知的并发问题。
    ///
    /// 【断言口径：恰好 1 行，且窗口内持续为 1】
    ///   只断言「至少 1 行」测不出重复；只在某一瞬间断言「== 1」又可能抢在第二行落库之前。
    ///   所以先等第一行出现，再在静置窗口内持续断言「不超过 1 行」。
    /// </summary>
    [Fact(DisplayName = "缺口C-① 恢复启动命中同一适配器实例 → 跳过重复订阅(一条报文只落一行)")]
    public async Task GapC_RecoveryStartOnSameAdapterInstance_Should_SkipDuplicateSubscription()
    {
        // ── Arrange ①：第一次启动，在 DefaultAdapter 上登记订阅 ──────────
        await SetProtocolStatusAsync("inactive");
        await StartProtocolAsync();
        (await ReadProtocolStateAsync()).Status.Should().Be("active", "第一次启动应把状态推到 active");

        // ── Arrange ②：制造失配，逼出第二次「完整启动流程」───────────────
        AdapterFactory.SimulateAdapterAbsent(ConfigId);

        // ── Act：恢复启动。CreateAdapter 仍返回同一个 DefaultAdapter ─────
        await StartProtocolAsync();

        AdapterFactory.IsSimulatedAbsent(ConfigId).Should().BeFalse(
            "缺席标记应被 CreateAdapter 撤销 —— 它是「第二次确实走了完整启动流程」的证据；" +
            "标记还在说明被短路了，去重分支根本没被触达，本用例失去意义");
        AdapterFactory.PeekAdapter(ConfigId).Should().BeSameAs(Adapter,
            "两次启动必须落在同一个适配器实例上，才谈得上「同实例跳过」");

        // ── Assert：一条报文只落一行 ───────────────────────────────────
        const string marker = "gapC1-single-subscription";
        Adapter.RaiseDataReceived(0L, Seed.Imei, MarkerPayload(marker), SharedTestConstants.AppCode);

        (await WaitForMarkerAsync(marker)).Should().BeTrue("订阅应当有效，至少要落一行");

        await AssertMarkerCountStaysAtMostAsync(marker, 1,
            "同一实例上重复挂载 handler 会让一条报文被处理两次；" +
            "出现第 2 行即证明去重分支失效（重复落库 + 重复触发上线通知）");
    }

    /// <summary>
    /// C-②：登记的是<b>旧适配器实例</b>时，必须先从旧实例摘掉 handler、再挂到新实例上 ——
    /// 旧实例投报文不得落库，新实例投报文必须落库。
    ///
    /// 【被防的是什么缺陷】
    ///   若反注册时按 configId 重新 <c>GetAdapter</c> 取实例，<c>-=</c> 就作用在<b>新</b>对象上，
    ///   旧实例的 handler 永远摘不掉。只要还有任何引用持有旧实例（生产里是尚未 GC 的 MQTT 客户端回调），
    ///   它就会继续往采集管道灌数据 —— 幽灵订阅。加固后改用登记时保存的实例引用反注册。
    ///
    /// 【报文投递顺序刻意「先旧后新」】
    ///   旧的先投：若幽灵订阅存在，它的记录会先于（至少不晚于）新实例的记录落库。
    ///   于是「等到新实例的记录出现 + 静置窗口」之后旧 marker 仍为 0，
    ///   就不是「还没来得及落」而是「确实没落」。反过来先投新的，这条推理链就断了。
    /// </summary>
    [Fact(DisplayName = "缺口C-② 适配器实例被重建 → 解绑旧实例并挂载新实例(旧实例不落库/新实例正常落库)")]
    public async Task GapC_StartAfterAdapterRebuilt_Should_UnbindOldInstance_AndBindNewOne()
    {
        // ── Arrange ①：在旧实例上建立订阅，并确认它是通的 ────────────────
        var oldAdapter = Adapter;

        await SetProtocolStatusAsync("inactive");
        await StartProtocolAsync();

        const string oldLiveMarker = "gapC2-old-instance-live";
        oldAdapter.RaiseDataReceived(0L, Seed.Imei, MarkerPayload(oldLiveMarker), SharedTestConstants.AppCode);
        (await WaitForMarkerAsync(oldLiveMarker)).Should().BeTrue(
            "旧实例此刻必须是活的数据桥；否则后面「旧实例不再落库」根本无从对比");

        // ── Arrange ②：重建适配器实例（模拟进程内 stop→start / 进程重启恢复）──
        var newAdapter = AdapterFactory.SimulateAdapterRebuilt(ConfigId);
        newAdapter.Should().NotBeSameAs(oldAdapter, "重建必须换成另一个对象，否则测的是 C-① 而不是 C-②");
        AdapterFactory.PeekAdapter(ConfigId).Should().BeSameAs(newAdapter);

        // 置回 inactive 才能走完整启动流程（否则会被「双条件成立」短路挡在门外）
        await SetProtocolStatusAsync("inactive");

        // ── Act：触发「旧实例解绑 → 新实例挂载」───────────────────────────
        await StartProtocolAsync();

        newAdapter.DataCollectionStarted.Should().BeTrue(
            "本次启动应作用在新实例上；为 false 说明工厂没把新实例交出去，后续断言全部失去意义");

        // ── Assert：先投旧实例（应静默），再投新实例（应落库）──────────────
        const string ghostMarker = "gapC2-old-instance-ghost";
        const string newMarker = "gapC2-new-instance-live";

        oldAdapter.RaiseDataReceived(0L, Seed.Imei, MarkerPayload(ghostMarker), SharedTestConstants.AppCode);
        newAdapter.RaiseDataReceived(0L, Seed.Imei, MarkerPayload(newMarker), SharedTestConstants.AppCode);

        (await WaitForMarkerAsync(newMarker)).Should().BeTrue(
            "新实例必须被挂上订阅；否则重建之后整条采集链路就断了（比幽灵订阅更严重）");

        await AssertMarkerCountStaysAtMostAsync(ghostMarker, 0,
            "旧实例的 handler 必须已被摘除；" +
            "这条报文一旦落库，说明反注册作用在了错误的对象上，旧实例成为永久幽灵订阅");
    }

    // ══════════════════════════════════════════════════════════════════
    // 辅助：DB 状态操控 / 服务调用 / 落库观测
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 直接改库把协议配置置成指定 <c>Status</c>（绕过服务，用于制造「DB 与内存失配」）。
    ///
    /// 【只改 Status 就够了】<c>ReconcileDerivedFields</c> 以 Status 为唯一真相推导
    /// <c>IsActive</c> / <c>ProtocolType</c>，这里再手工同步一次反而会掩盖「补齐逻辑失效」的缺陷。
    /// </summary>
    private Task SetProtocolStatusAsync(string status) =>
        ExecuteDbAsync(async db =>
        {
            var config = await db.ProtocolConfigs.SingleAsync(c => c.Id == Seed.ProtocolConfigId);
            config.Status = status;
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        });

    /// <summary>读取协议配置当前的 <c>(Status, IsActive)</c>。</summary>
    private Task<(string Status, bool IsActive)> ReadProtocolStateAsync() =>
        QueryDbAsync(async db =>
        {
            var row = await db.ProtocolConfigs
                .AsNoTracking()
                .Where(c => c.Id == Seed.ProtocolConfigId)
                .Select(c => new { c.Status, c.IsActive })
                .SingleAsync();
            return (row.Status, row.IsActive);
        });

    /// <summary>在独立作用域里调用 <c>StartProtocolAsync</c>（服务是 Scoped，必须每次新开作用域）。</summary>
    private async Task StartProtocolAsync()
    {
        await using var scope = CreateScope();
        var protocolService = scope.ServiceProvider.GetRequiredService<IProtocolConfigService>();
        await protocolService.StartProtocolAsync(Seed.ProtocolConfigId, SharedTestConstants.AppCode);
    }

    /// <summary>在独立作用域里调用 <c>StopProtocolAsync</c>。</summary>
    private async Task StopProtocolAsync()
    {
        await using var scope = CreateScope();
        var protocolService = scope.ServiceProvider.GetRequiredService<IProtocolConfigService>();
        await protocolService.StopProtocolAsync(Seed.ProtocolConfigId, SharedTestConstants.AppCode);
    }

    /// <summary>
    /// 构造一条带唯一标记的传感数据报文。
    ///
    /// <c>temperature</c> 是 <c>DataCollectionService</c> 精确映射表里的字段，
    /// 带上它能顺带证明这条记录真的走完了采集管道（而不是半途落了个空壳）。
    /// </summary>
    /// <param name="marker">本条报文的唯一标记，落进 <c>SensorData</c> 供断言检索。</param>
    private static string MarkerPayload(string marker) =>
        $$"""{"temperature":36.5,"qa_marker":"{{marker}}"}""";

    /// <summary>统计带指定 marker 的采集记录行数。</summary>
    private Task<int> CountByMarkerAsync(string marker) =>
        QueryDbAsync(db => db.DeviceDataRecords
            .AsNoTracking()
            .CountAsync(r => r.SensorData != null && r.SensorData.Contains(marker)));

    /// <summary>
    /// 等待带指定 marker 的记录至少出现一行（正向断言用）。
    /// </summary>
    /// <param name="marker">报文标记。</param>
    /// <returns>超时前出现返回 <c>true</c>。</returns>
    private Task<bool> WaitForMarkerAsync(string marker) =>
        WaitUntilAsync(async () => await CountByMarkerAsync(marker) > 0, LandTimeout);

    /// <summary>
    /// 静置观察窗内持续断言「带该 marker 的记录数不超过 <paramref name="expected"/> 行」。
    ///
    /// 【为什么要在窗口内<b>持续</b>查，而不是睡完再查一次】
    ///   幽灵订阅可能先落一行、再被别的路径改写；睡完只查一次会漏掉中间态。
    ///   持续查一旦超标立刻失败，失败时刻也更接近成因发生点，便于排障。
    /// </summary>
    /// <param name="marker">报文标记。</param>
    /// <param name="expected">允许出现的最大行数。</param>
    /// <param name="because">失败说明。</param>
    private async Task AssertMarkerCountStaysAtMostAsync(string marker, int expected, string because)
    {
        var deadline = DateTime.UtcNow + QuietWindow;
        while (DateTime.UtcNow < deadline)
        {
            (await CountByMarkerAsync(marker)).Should().BeLessThanOrEqualTo(expected, because);
            await Task.Delay(100);
        }

        (await CountByMarkerAsync(marker)).Should().BeLessThanOrEqualTo(expected, because);
    }

    /// <summary>
    /// 轮询等待条件成立。命中即返回，最坏情况才走满超时。
    /// </summary>
    private static async Task<bool> WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return true;
            }

            await Task.Delay(50);
        }

        return await condition();
    }
}
