using System.Text.Json;
using FluentAssertions;
using IoTPlatform.Configuration;
using IoTPlatform.Data;
using IoTPlatform.Infrastructure.Protocol.AnSheng;
using IoTPlatform.IntegrationTests.Infrastructure;
using IoTPlatform.Models;
using IoTPlatform.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace IoTPlatform.IntegrationTests.AnSheng;

/// <summary>
/// T6「事件识别与处理管道」的<b>接线 + 落库</b>集成验收（设计文档 §9.3 验收 #4 / #5 / #6）。
///
/// ═══════════════════════════════════════════════════════════════════════
/// 【为什么这三条必须是集成测试，单测替代不了】
/// ═══════════════════════════════════════════════════════════════════════
/// <c>IoTPlatform.AnSheng.Tests</c> 里 616 条单测覆盖的是<b>纯数据层</b>：
/// 归一化字段、分支判定、去抖计时器语义——都是「给定输入、检查返回值」。
/// 但 T6 真正容易出事的地方全在单测的盲区里：
///   · <c>AnShengUplinkPipeline</c> 是 Singleton，<b>构造时</b>才订阅静态总线，
///     若 <c>Program.cs</c> 少了那一行 <c>GetRequiredService</c>，所有事件会<b>静默</b>丢失
///     ——不报错、不抛异常、就是没数据，单测永远发现不了；
///   · 管道跑在 <c>Task.Run</c> 后台线程，<c>ITenantContextAccessor.Current</c> 为 null，
///     <c>AppCode</c> 必须由写入方显式填；填漏了单测照样绿，落库的行却是空租户；
///   · 去抖窗口跨 Scope（<c>Task.Delay</c> 活得比 <c>AppDbContext</c> 长），
///     到期回调能不能真的把 <c>Device.Status</c> 改成 offline，只有连着真实 MySQL 才知道。
/// 因此这三条一律走「真实 TestServer + 真实 MySQL + 真实 DI 图」，
/// 只把最外层的 MQTT 适配器换成录制替身。
///
/// ═══════════════════════════════════════════════════════════════════════
/// 【等待策略：只用 DrainAsync，不用 Thread.Sleep】
/// ═══════════════════════════════════════════════════════════════════════
/// 管道是异步的，睡固定时长要么不够（偶发红）要么太久（跑得慢）。
/// <c>AnShengUplinkPipeline.DrainAsync</c> 自旋等 <c>_inFlight == 0</c>，是精确完成信号。
/// 唯一的例外是验收 #5——它<b>必须</b>真实等待去抖窗口到期，那是被测行为本身，
/// 不是等待手段（窗口已在 appsettings.Testing.json 压到 2 秒）。
///
/// ═══════════════════════════════════════════════════════════════════════
/// 【本类刻意<b>不</b>依赖 HTTP】
/// ═══════════════════════════════════════════════════════════════════════
/// 事件管道的入口是 MQTT 上行，不是 REST。用 <c>Adapter.RaiseAnShengUplink</c>
/// 直投静态总线，与生产 <c>AnShengMqttProtocolAdapter.OnMessageReceivedAsync</c>
/// 的第 535 行 <c>AnShengUplinkHub.Publish(...)</c> 是同一个接缝。
/// </summary>
[Collection(SharedTestConstants.CollectionName)]
public sealed class AnShengEventTests : IntegrationTestBase
{
    /// <summary>管道排空的等待上限。真实 MySQL 首次建连 + EF 首次编译查询可能慢，给足余量。</summary>
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(15);

    /// <summary>轮询等待「异步副作用落库」的上限（用于 fire-and-forget 的数据桥）。</summary>
    private static readonly TimeSpan SideEffectTimeout = TimeSpan.FromSeconds(15);

    public AnShengEventTests(DatabaseFixture fixture) : base(fixture)
    {
    }

    /// <summary>事件管道单例。断言前必须用它 <c>DrainAsync</c>，否则读到的是半成品状态。</summary>
    private AnShengUplinkPipeline Pipeline =>
        Fixture.Factory.Services.GetRequiredService<AnShengUplinkPipeline>();

    /// <summary>离线去抖器单例，用于观察窗口是否真的被 Arm / Cancel。</summary>
    private AnShengOfflineDebouncer Debouncer =>
        Fixture.Factory.Services.GetRequiredService<AnShengOfflineDebouncer>();

    /// <summary>生效中的事件管道配置（用来核对 appsettings.Testing.json 真的被加载了）。</summary>
    private AnShengEventOptions EventOptions =>
        Fixture.Factory.Services.GetRequiredService<IOptions<AnShengEventOptions>>().Value;

    // ══════════════════════════════════════════════════════════════════
    // 验收 #4：keyEvent 上行 → 事件溯源表落库 + 双出口投递
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 验收 #4：注入一条 <c>keyEvent</c> 上行，<c>ansheng_device_events</c> 应新增恰好一行，
    /// 且 IMEI / Method / Kind / AppCode / DeviceId / SlotNum / OccurredAt 全部正确，
    /// <c>DispatchedToRules</c> 为 true。
    ///
    /// 【关于「要不要配一条 DataRule 来断言规则被触发」——本例的取舍】
    ///   设计文档允许两种做法。本例<b>不</b>配 DataRule，理由：
    ///     · <c>DispatchedToRules</c> 这个布尔只能证明「调用没抛异常」，证明力偏弱，
    ///       所以本例<b>额外</b>断言出口② 真的产生了 <see cref="DeviceDataRecord"/> 落库
    ///       ——这是比 DataRule 更靠前、更稳定的物证：规则引擎要能命中，
    ///       前提就是数据点已经进了采集管道并落了库；
    ///     · 反过来，配 DataRule 会把「规则引擎的条件表达式求值」这一整套 T9 的逻辑
    ///       拖进 T6 的验收里。一旦规则引擎有 bug，本用例会红，
    ///       但红的原因与「事件管道接没接对」无关——那是<b>错误归因</b>，
    ///       会误导工程师去改管道。验收边界必须与被测模块边界重合。
    ///   遗留：「事件 → DataRule 条件命中 → 产生 AlertRecord」这条尾巴不在本例覆盖内，
    ///   已在交付报告的「遗留问题」里显式登记，留给 T9 的告警验收。
    /// </summary>
    [Fact(DisplayName = "验收#4 keyEvent 上行 → 事件表落库(IMEI/Method/Kind/AppCode/SlotNum/OccurredAt) + DispatchedToRules=true")]
    public async Task Acceptance4_KeyEvent_Should_Persist_Event_Row_And_Dispatch_To_Rules()
    {
        // ── Arrange ───────────────────────────────────────────────────
        var imei = Seed.Imei;

        // 设备时间戳取「刚刚」：OccurredAt 的合理性区间是 [ReceivedAt-24h, ReceivedAt+5min]，
        // 用单测里那个写死的 1745456483（2025-04-24）会因超出 24h 而回退到平台时间，
        // 于是「OccurredAt 取设备时间」这条规则就永远测不到——必须用动态时间戳。
        var deviceTimestamp = DateTimeOffset.UtcNow.AddSeconds(-3).ToUnixTimeSeconds();
        var expectedDeviceTimeUtc = DateTimeOffset.FromUnixTimeSeconds(deviceTimestamp).UtcDateTime;

        // slotNum=1：多路机型的 keyEvent 会带位路号，用它验证 SlotNum 列确实从报文抽取，
        // 而不是永远为 null（永远 null 也能让「非空断言以外」的用例通过，是典型的假绿）。
        var payload = $$"""
        {"method":"keyEvent","imei":"{{imei}}","key":2,"slotNum":1,"timestamp":{{deviceTimestamp}}}
        """;

        (await QueryDbAsync(db => db.AnShengDeviceEvents.CountAsync()))
            .Should().Be(0, "每个用例开始前 Respawn 都会清空事件表");

        var processedBefore = Pipeline.ProcessedCount;

        // ── Act ───────────────────────────────────────────────────────
        Adapter.RaiseAnShengUplink(imei, "keyEvent", payload);

        (await Pipeline.DrainAsync(DrainTimeout))
            .Should().BeTrue("管道应在 {0}s 内处理完这条上行", DrainTimeout.TotalSeconds);

        // 先证明「管道确实被触发过」，再谈业务断言。
        // 少了这一步，一旦 Program.cs 漏掉强制解析导致订阅没生效，
        // 下面的「事件表为空」会被误读成「Handler 逻辑不对」，排查方向直接跑偏。
        Pipeline.ProcessedCount.Should().BeGreaterThan(
            processedBefore,
            "管道未处理任何报文——极可能是 AnShengUplinkPipeline 没被解析、总线订阅未建立");

        // ── Assert：出口① 事件溯源表 ──────────────────────────────────
        var events = await QueryDbAsync(db => db.AnShengDeviceEvents
            .AsNoTracking()
            .Where(e => e.Imei == imei)
            .ToListAsync());

        var ev = events.Should().ContainSingle("一条 keyEvent 上行应恰好产生一行事件").Subject;

        ev.Imei.Should().Be(imei);
        ev.Method.Should().Be("keyEvent", "Method 列保真存原始 method，不做归一化");
        ev.Kind.Should().Be(AnShengEventKind.Key);
        ev.Severity.Should().Be(AnShengEventSeverity.Info, "按键事件属常规事件");
        ev.SlotNum.Should().Be(1, "报文里的 slotNum 应被抽取到 SlotNum 列");

        // ★ 多租户陷阱的护栏：管道跑在后台线程，租户过滤器不生效，AppCode 必须由写入方显式填。
        //   这里若为空串，说明 AnShengEventHandlerBase 漏了 `AppCode = ctx.AppCode`，
        //   或 AnShengUplinkPipeline.BuildContextAsync 的租户解析链断了。
        ev.AppCode.Should().Be(
            SharedTestConstants.AppCode,
            "事件必须落在设备所属租户下（AppCode 由 Device.AppCode 解析而来）");

        ev.DeviceId.Should().Be(Seed.DeviceId, "IMEI 已能匹配到已注册设备，DeviceId 不应为 null");

        // OccurredAt 取值规则（设计 §3.1）：设备时间戳可信时用设备时间，不回退。
        ev.DeviceTimestampUtc.Should().NotBeNull();
        ev.DeviceTimestampUtc!.Value.Should().BeCloseTo(expectedDeviceTimeUtc, TimeSpan.FromSeconds(1));
        ev.OccurredAt.Should().BeCloseTo(
            expectedDeviceTimeUtc,
            TimeSpan.FromSeconds(1),
            "设备时间戳落在合理区间内，OccurredAt 应取设备时间而非平台时间");
        ev.ReceivedAt.Should().BeAfter(ev.OccurredAt.AddSeconds(-10), "ReceivedAt 应是平台收报时刻");

        // 归一化快照与原始报文都要留痕（前者给规则引擎复现，后者给取证重放）。
        ev.PayloadJson.Should().NotBeNullOrWhiteSpace();
        ev.RawJson.Should().Contain("keyEvent", "RawJson 应保存原始报文全文");

        using (var doc = JsonDocument.Parse(ev.PayloadJson!))
        {
            var root = doc.RootElement;
            root.GetProperty("event").GetString().Should().Be("keyEvent");
            root.GetProperty("event_key").GetInt32().Should().Be(2);
            root.GetProperty("slot_num").GetInt32().Should().Be(1);
            root.TryGetProperty("ts_fallback", out _)
                .Should().BeFalse("设备时间戳可信，不应打回退标记");
        }

        // ── Assert：出口② 规则引擎投递 ────────────────────────────────
        ev.DispatchedToRules.Should().BeTrue("双出口的出口② 必须成功投递");
        ev.DispatchError.Should().BeNull();

        // 比布尔更硬的物证：出口② 走的是 IDataCollectionService，成功即应落一条采集记录。
        var dispatchedRecords = await QueryDbAsync(db => db.DeviceDataRecords
            .AsNoTracking()
            .Where(r => r.DeviceId == Seed.DeviceId)
            .ToListAsync());

        dispatchedRecords.Should().NotBeEmpty(
            "DispatchedToRules=true 却没有任何采集记录，说明出口② 只是没抛异常、并未真正落库");

        dispatchedRecords[0].AppCode.Should().Be(
            SharedTestConstants.AppCode,
            "出口② 落库同样跑在后台线程，AppCode 必须显式透传");
    }

    // ══════════════════════════════════════════════════════════════════
    // 验收 #5：close → 窗口内 connected → 设备保持在线（决策 3 的解耦证明）
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 验收 #5：已认领设备收到 <c>close</c> 后，在去抖窗口内收到 <c>connected</c>，
    /// 设备状态应<b>始终</b>保持 online，窗口到期后也不得被迟到的定时器改成 offline。
    ///
    /// ═══════════════════════════════════════════════════════════════
    /// 【本例如何证明「解耦有效」——以及它能证明到什么程度，必须说清楚】
    /// ═══════════════════════════════════════════════════════════════
    /// 决策 3 的核心是：<c>close</c> 不再由 <c>AnShengDiscoveryService</c> 直连
    /// <c>DeviceWill</c> 立即置离线，而是走 <c>CloseEventHandler → AnShengOfflineDebouncer</c>。
    /// 本例用<b>时间轴上的三个观测点</b>来证明：
    ///
    ///   t0  投 close，DrainAsync 返回（管道已处理完）
    ///       ├─ 断言 Status 仍为 "online"   ← ★ 若还有任何「收到 close 就立即置离线」的直连通路
    ///       │                                （无论是 DiscoveryService 订阅 DeviceWill，
    ///       │                                 还是 CloseEventHandler 自己调 OnDeviceOfflineAsync），
    ///       │                                 此刻必然已经是 "offline"，本断言立刻爆红。
    ///       └─ 断言 Debouncer.IsArmed == true  ← 证明走的确实是去抖通路，而不是「什么都没发生」
    ///
    ///   t1  投 connected，DrainAsync 返回
    ///       └─ 断言 IsArmed == false        ← 证明窗口被撤销，而非被无视
    ///
    ///   t2  等待「窗口时长 + 余量」
    ///       └─ 断言 Status 仍为 "online"   ← 证明没有迟到的定时器把设备打下线
    ///
    /// 【诚实声明：本例<b>无法</b>覆盖的一段】
    ///   集成测试把整个 <c>IProtocolAdapter</c> 换成了录制替身，生产
    ///   <c>AnShengMqttProtocolAdapter.HandleWillMessage</c> 这段代码在本测试进程里<b>根本不会执行</b>。
    ///   所以 t0 断言严格说来证明的是「<b>管道侧</b>没有立即置离线的通路」，
    ///   而不是「适配器侧的 DeviceWill 订阅已被摘除」。后者由代码审查 +
    ///   <c>AnShengDiscoveryService</c> 中那段显式注释保证，已在交付报告中登记为核对项。
    ///   不把这一点写清楚，本用例就会给人「解耦已被端到端证明」的错觉——那是更危险的假绿。
    ///
    /// 【为什么还要配一条 <see cref="Guard_Close_Without_Connected_Should_Set_Device_Offline"/>】
    ///   只有本例的话，「设备一直 online」有一个平凡解：去抖窗口压根没工作、
    ///   或 <c>OnDeviceOfflineAsync</c> 根本改不动库。那样本例照样全绿，但毫无意义。
    ///   那条护栏用例证明「不投 connected 时设备<b>确实</b>会在窗口到期后变 offline」，
    ///   两条合起来才构成有效证明。
    /// </summary>
    [Fact(DisplayName = "验收#5 close 后窗口内收到 connected → 设备保持 online（去抖生效、无直连置离线通路）")]
    public async Task Acceptance5_Connected_Within_DebounceWindow_Should_Keep_Device_Online()
    {
        // ── Arrange ───────────────────────────────────────────────────
        var imei = Seed.Imei;
        var debounceSeconds = EventOptions.EffectiveCloseDebounceSeconds;

        // 配置自检：窗口若还是生产默认的 30s，说明 appsettings.Testing.json 的
        // AnSheng:Event 节没被加载，本用例会白等半分钟然后超时——先把成因摆明。
        debounceSeconds.Should().BeLessThanOrEqualTo(
            5,
            "去抖窗口应由 appsettings.Testing.json 的 AnSheng:Event:CloseDebounceSeconds 压到 2s；" +
            "读到 {0}s 说明测试配置未生效", debounceSeconds);

        await MarkSeedDeviceClaimedAndOnlineAsync();

        (await ReadDeviceStatusAsync(imei)).Should().Be("online", "前置条件：设备应处于在线态");

        // ── Act & Assert：t0 —— close 之后必须「按兵不动」 ─────────────
        Adapter.RaiseAnShengUplink(imei, "close", BuildClosePayload(imei));
        (await Pipeline.DrainAsync(DrainTimeout)).Should().BeTrue("close 报文应在超时前处理完");

        (await ReadDeviceStatusAsync(imei)).Should().Be(
            "online",
            "★ 决策 3 的核心断言：收到 close 时设备<b>不得</b>被立即置离线。" +
            "此处若为 offline，说明存在绕过 AnShengOfflineDebouncer 的直连置离线通路" +
            "（检查 CloseEventHandler 是否直接调了 OnDeviceOfflineAsync，" +
            "以及 AnShengDiscoveryService 是否又订阅回了 DeviceWill）");

        Debouncer.IsArmed(imei).Should().BeTrue(
            "close 应起一个去抖窗口；未 Arm 说明 CloseEventHandler 没跑，" +
            "或事件被路由成了 AutoReport/Ignored 而非 Event");

        // 事件表也应留痕，且严重级别为 Warning（close 属需关注事件）。
        var closeEvent = await QueryDbAsync(db => db.AnShengDeviceEvents
            .AsNoTracking()
            .SingleAsync(e => e.Imei == imei && e.Method == "close"));
        closeEvent.Kind.Should().Be(AnShengEventKind.Close);
        closeEvent.Severity.Should().Be(AnShengEventSeverity.Warning);
        closeEvent.AppCode.Should().Be(SharedTestConstants.AppCode);

        // ── Act & Assert：t1 —— 窗口内 connected 应撤销窗口 ────────────
        Adapter.RaiseAnShengUplink(imei, "connected", BuildConnectedPayload(imei));
        (await Pipeline.DrainAsync(DrainTimeout)).Should().BeTrue("connected 报文应在超时前处理完");

        Debouncer.IsArmed(imei).Should().BeFalse(
            "窗口内收到 connected 应撤销去抖窗口（ConnectedEventHandler → Debouncer.Cancel）");

        // ── Act & Assert：t2 —— 等过窗口，确认没有迟到的置离线 ─────────
        // 这里的等待是「被测行为本身」（窗口到期），不是等待手段，故必须真实经过墙钟时间。
        // 余量取 1.5s：覆盖 Task.Delay 的调度抖动 + OnDeviceOfflineAsync 的一次 SaveChanges。
        await Task.Delay(TimeSpan.FromSeconds(debounceSeconds + 1.5));

        (await ReadDeviceStatusAsync(imei)).Should().Be(
            "online",
            "窗口已被 connected 撤销，到期后不应再有任何置离线动作；" +
            "此处为 offline 说明 Debouncer.Cancel 没能真正取消那个 Task.Delay");

        Debouncer.IsArmed(imei).Should().BeFalse("窗口早已撤销，不应又冒出新的在途窗口");
    }

    /// <summary>
    /// 验收 #5 的<b>反证护栏</b>：不投 <c>connected</c> 时，窗口到期后设备<b>必须</b>变成 offline。
    ///
    /// 【为什么这条不可省】
    ///   <see cref="Acceptance5_Connected_Within_DebounceWindow_Should_Keep_Device_Online"/>
    ///   断言的是「设备保持 online」。这类「什么都没发生」型断言有个致命弱点：
    ///   只要置离线链路整体失效（定时器没跑、回调抛异常被吞、SQL 没提交），它一样全绿。
    ///   本例证明同一条链路在<b>该触发时确实会触发</b>，从而把上一条的绿灯变成有效信号。
    ///   这是 T6 里「去抖」这一决策唯一的双向证明，删掉任何一条，另一条都会退化成摆设。
    /// </summary>
    [Fact(DisplayName = "验收#5-护栏 close 后无 connected → 窗口到期设备置 offline（证明上一条断言非平凡真）")]
    public async Task Guard_Close_Without_Connected_Should_Set_Device_Offline()
    {
        var imei = Seed.Imei;
        var debounceSeconds = EventOptions.EffectiveCloseDebounceSeconds;
        debounceSeconds.Should().BeLessThanOrEqualTo(5, "测试配置未生效，见上一条用例的说明");

        await MarkSeedDeviceClaimedAndOnlineAsync();

        Adapter.RaiseAnShengUplink(imei, "close", BuildClosePayload(imei));
        (await Pipeline.DrainAsync(DrainTimeout)).Should().BeTrue();
        Debouncer.IsArmed(imei).Should().BeTrue("close 应起窗口");

        // 轮询而非死等：窗口到期后还要经过一次跨 Scope 的 SaveChanges，
        // 死等固定时长在慢机器上会偶发红。上限给足，命中即返回。
        var wentOffline = await WaitUntilAsync(
            async () => await ReadDeviceStatusAsync(imei) == "offline",
            TimeSpan.FromSeconds(debounceSeconds + 8));

        wentOffline.Should().BeTrue(
            "无人撤销时去抖窗口到期必须真正置离线；" +
            "否则设备将永远停留在 online，掉线彻底不可见（比误报更严重的故障模式）");
    }

    // ══════════════════════════════════════════════════════════════════
    // 验收 #6：getDevStatus 自动上报 → DeviceDataRecord 展平字段落库
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 验收 #6：注入一条 <c>getDevStatus</c> 自动上报，<c>device_data_records</c> 应新增一行，
    /// <c>SensorData</c> 里带 <c>slot1_voltage</c> / <c>slot1_power</c> / <c>temperature</c>
    /// 等展平字段，且 <c>Temperature</c> / <c>ElectricPower</c> / <c>ElectricKWh</c> 专用列被正确填充，
    /// 同时<b>全部旧键一个不少</b>（线上已有 DataRule 依赖它们）。
    ///
    /// ═══════════════════════════════════════════════════════════════
    /// 【本例为什么要投<b>两条</b>通道，而不是只调一次 RaiseAnShengUplink】
    /// ═══════════════════════════════════════════════════════════════
    /// 这是 T6 架构里最容易被误解的一点，必须在测试里如实还原：
    ///   · <b>事件总线通道</b>（<c>RaiseAnShengUplink</c> → Hub → Pipeline → Router）：
    ///     <c>getDevStatus</c> 不在事件白名单里，被判为 <b>AutoReport</b>。
    ///     而 AutoReport 分支（决策 B-1）<b>只刷设备能力档案，不落库、不投规则引擎</b>。
    ///   · <b>数据桥通道</b>（<c>RaiseDataReceived</c> → ProtocolConfigService → IDataCollectionService）：
    ///     这才是 <c>DeviceDataRecord</c> 的唯一来源，是 T6 之前就存在、且被要求「一行不动」的既有通路。
    /// 生产 <c>AnShengMqttProtocolAdapter.OnMessageReceivedAsync</c> 对同一条报文
    /// 会<b>先后</b>触发这两条通道（第 535 行 Publish、第 548 行 DataReceived），
    /// 所以本例也必须两条都投——只投前者会得到「一条记录都没有」的假红，
    /// 只投后者则测不到「AutoReport 不应污染事件表」这条语义。
    ///
    /// 【DataReceived 携带的是归一化产物，不是原始报文】
    ///   生产代码第 544-546 行传的是 <c>NormalizeForSensorData(message, topic)</c> 的结果。
    ///   本例用同一个 <see cref="AnShengMessageParser"/> 生成它，而不是自己手拼一份 JSON——
    ///   手拼等于把归一化契约抄第二遍，生产改了字段名测试却不知道，是典型的双份真相。
    /// </summary>
    [Fact(DisplayName = "验收#6 getDevStatus 自动上报 → DeviceDataRecord 落库(slot1_voltage/slot1_power/temperature + 专用列 + 旧键)")]
    public async Task Acceptance6_GetDevStatus_AutoReport_Should_Persist_Flattened_Slot_Fields()
    {
        // ── Arrange ───────────────────────────────────────────────────
        var imei = Seed.Imei;
        var rawPayload = BuildDevStatusPayload(imei);

        // 数据桥的订阅由 ProtocolConfigService.StartProtocolAsync 建立。
        // 集成环境移除了 IHostedService，没人替我们启协议，必须显式启一次。
        await StartProtocolBridgeAsync();

        (await QueryDbAsync(db => db.DeviceDataRecords.CountAsync()))
            .Should().Be(0, "每个用例开始前 Respawn 都会清空采集记录表");

        var processedBefore = Pipeline.ProcessedCount;

        // ── Act ①：事件总线通道（对应生产第 535 行 Publish）─────────────
        Adapter.RaiseAnShengUplink(imei, AnShengMessageRouter.MethodGetDevStatus, rawPayload);
        (await Pipeline.DrainAsync(DrainTimeout)).Should().BeTrue("管道应在超时前处理完自动上报");

        Pipeline.ProcessedCount.Should().BeGreaterThan(
            processedBefore, "管道未处理任何报文——检查总线订阅是否建立");

        // AutoReport 分支的语义护栏：不得落进事件溯源表。
        // 若这里出现了行，说明 getDevStatus 被误判成 Event（白名单被污染），
        // 事件表会被高频状态上报灌爆——这是设计文档反复强调要避免的失败模式。
        (await QueryDbAsync(db => db.AnShengDeviceEvents.CountAsync(e => e.Imei == imei)))
            .Should().Be(0, "getDevStatus 属 AutoReport 分支，只刷档案，不得写入事件溯源表");

        // ── Act ②：数据桥通道（对应生产第 548 行 DataReceived）──────────
        var normalizedForSensorData = BuildNormalizedSensorData(rawPayload);
        Adapter.RaiseDataReceived(0L, imei, normalizedForSensorData, SharedTestConstants.AppCode);

        // 数据桥是 fire-and-forget 的 async 事件处理器，没有 Drain 之类的完成信号，只能轮询。
        var landed = await WaitUntilAsync(
            async () => await QueryDbAsync(db =>
                db.DeviceDataRecords.AnyAsync(r => r.DeviceId == Seed.DeviceId)),
            SideEffectTimeout);

        landed.Should().BeTrue(
            "{0}s 内未见采集记录落库。排查顺序：" +
            "① ProtocolConfigService 的 DataReceived 订阅是否真的建立（StartProtocolAsync 是否被 active 状态短路）；" +
            "② IMEI → DeviceId 映射是否命中；③ DataCollectionService 是否吞了异常",
            SideEffectTimeout.TotalSeconds);

        // ── Assert ────────────────────────────────────────────────────
        var record = await QueryDbAsync(db => db.DeviceDataRecords
            .AsNoTracking()
            .Where(r => r.DeviceId == Seed.DeviceId)
            .OrderByDescending(r => r.Id)
            .FirstAsync());

        record.AppCode.Should().Be(SharedTestConstants.AppCode);
        record.SensorData.Should().NotBeNullOrWhiteSpace();

        using var doc = JsonDocument.Parse(record.SensorData!);
        var root = doc.RootElement;

        // ① 展平字段（T6 新增的加法部分）—— 字段名与单测
        //    NormalizeDevStatus_Should_Emit_Flattened_Slot_Fields 保持逐字一致。
        root.GetProperty("slot1_state").GetInt32().Should().Be(1);
        root.GetProperty("slot1_voltage").GetDouble().Should().Be(220.5d);
        root.GetProperty("slot1_current").GetDouble().Should().Be(1.2d);
        root.GetProperty("slot1_power").GetDouble().Should().Be(264.6d);
        root.GetProperty("slot1_energy").GetDouble().Should().Be(10.5d);
        root.GetProperty("slot2_state").GetInt32().Should().Be(0);
        root.GetProperty("slot2_voltage").GetDouble().Should().Be(221.0d);
        root.GetProperty("temperature").GetDouble().Should().Be(36.5d);

        // 位路序号从 1 开始，绝不能出现 slot0_*（差一错误会让所有 DataRule 的位路条件错位一路）。
        root.EnumerateObject()
            .Select(p => p.Name)
            .Should().NotContain(n => n.StartsWith("slot0_", StringComparison.Ordinal));

        // ② 向后兼容硬约束：展平只做加法，旧键一个都不能少（设计文档 §10 第 7 条）。
        //    线上已有 DataRule 依赖它们，删一个就是一次线上事故。
        foreach (var legacyKey in new[]
                 {
                     "method", "imei", "net_type", "iccid", "signal", "temperature",
                     "slot_count", "slots", "total_power", "total_energy", "total_current",
                     "avg_voltage", "em_data", "raw_timestamp", "timestamp_utc"
                 })
        {
            root.TryGetProperty(legacyKey, out _)
                .Should().BeTrue("旧键 {0} 缺失，将击穿既有 DataRule", legacyKey);
        }

        // ③ 专用列映射：DataCollectionService 的精确映射表应把汇总量搬到强类型列上。
        //    total_power = 264.6 + 0 = 264.6；total_energy = 10.5 + 0 = 10.5。
        record.Temperature.Should().Be(36.5d, "temperature → Temperature 列");
        record.ElectricPower.Should().Be(264.6d, "total_power → ElectricPower 列");
        record.ElectricKWh.Should().Be(10.5d, "total_energy → ElectricKWh 列");
    }

    // ══════════════════════════════════════════════════════════════════
    // 辅助方法
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 把基线设备改造成「已认领 + 在线」，作为验收 #5 的前置状态。
    ///
    /// 【为什么必须显式改】
    ///   <see cref="Seed.SeedData"/> 播种的设备是 <c>Status="offline"</c> +
    ///   <c>IsClaimed=false</c>。若不改就直接跑 #5，「窗口到期置离线」这个动作
    ///   会把 offline 改成 offline —— 状态没有可观测的变化，
    ///   于是无论去抖是否生效，断言都恒真。那是彻底的假绿。
    /// </summary>
    private async Task MarkSeedDeviceClaimedAndOnlineAsync()
    {
        await ExecuteDbAsync(async db =>
        {
            var device = await db.Devices.SingleAsync(d => d.SerialNumber == Seed.Imei);
            device.Status = "online";
            device.UpdatedAt = DateTime.UtcNow;

            var discovered = await db.DiscoveredAnShengDevices.SingleAsync(d => d.Imei == Seed.Imei);
            discovered.IsClaimed = true;
            discovered.ClaimedDeviceId = device.Id;
            discovered.LastSeenAt = DateTime.UtcNow;

            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        });
    }

    /// <summary>
    /// 显式启动协议，建立 <c>DataReceived → IDataCollectionService</c> 的数据桥订阅。
    ///
    /// 【两个坑，缺一不可】
    ///   ① <c>TestWebAppFactory</c> 移除了全部 <c>IHostedService</c>，
    ///      生产里负责开机启协议的后台服务不存在，测试必须自己启；
    ///   ② <see cref="Seed.SeedData"/> 播种的 ProtocolConfig 是 <c>Status="active"</c>，
    ///      而 <c>StartProtocolAsync</c> 开头就有「已激活直接 return」的短路，
    ///      不先置 inactive 的话它一行都不会执行，订阅永远建不起来，
    ///      症状是 #6 明明投了报文却一条记录都没有。
    /// </summary>
    private async Task StartProtocolBridgeAsync()
    {
        await ExecuteDbAsync(async db =>
        {
            var config = await db.ProtocolConfigs.SingleAsync(c => c.Id == Seed.ProtocolConfigId);
            config.Status = "inactive";
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        });

        await using var scope = CreateScope();
        var protocolService = scope.ServiceProvider.GetRequiredService<IProtocolConfigService>();
        await protocolService.StartProtocolAsync(Seed.ProtocolConfigId, SharedTestConstants.AppCode);
    }

    /// <summary>读取设备当前状态列。</summary>
    private Task<string> ReadDeviceStatusAsync(string imei) =>
        QueryDbAsync(db => db.Devices
            .AsNoTracking()
            .Where(d => d.SerialNumber == imei)
            .Select(d => d.Status)
            .FirstAsync());

    /// <summary>
    /// 轮询等待某个条件成立。
    ///
    /// 【为什么不是 Task.Delay(固定时长) 后一次性断言】
    ///   固定时长要么不够（慢机器上偶发红），要么过长（每条用例白等）。
    ///   轮询命中即返回，最坏情况才走满超时，是「又快又稳」的唯一写法。
    ///   注意本方法只用于<b>没有完成信号</b>的异步副作用；
    ///   凡是管道内的等待一律用 <c>DrainAsync</c>，那才是精确信号。
    /// </summary>
    /// <param name="condition">条件谓词，返回 true 即停止等待。</param>
    /// <param name="timeout">等待上限。</param>
    /// <returns>超时前条件成立返回 true。</returns>
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

    /// <summary>
    /// 用生产解析器生成 <c>DataReceived</c> 该携带的归一化 JSON，
    /// 逐字复刻 <c>AnShengMqttProtocolAdapter.OnMessageReceivedAsync</c> 第 544-546 行。
    /// </summary>
    /// <param name="rawPayload">设备原始报文。</param>
    /// <returns>归一化后的 SensorData JSON。</returns>
    private static string BuildNormalizedSensorData(string rawPayload)
    {
        var parser = new AnShengMessageParser();
        var message = parser.Parse(rawPayload);
        message.Should().NotBeNull("测试报文本身必须能被生产解析器解析，否则用例在测自己的拼写");

        return parser.NormalizeForSensorData(message!, $"/ansheng/{message!.Imei}/up");
    }

    /// <summary>
    /// <c>getDevStatus</c> 状态快照报文。
    /// 结构与字段值与单测 <c>AnShengEventPipelineUnitTests.DevStatusPayload()</c> 保持一致，
    /// 这样单测与集成测出现分歧时，可以直接判定是「落库环节」而非「归一化环节」的问题。
    /// </summary>
    private static string BuildDevStatusPayload(string imei) => $$"""
        {"method":"getDevStatus","imei":"{{imei}}","result":"success",
         "timestamp":{{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}},
         "netType":"4G","iccid":"89860000000000000000","signal":25,"temperature":36.5,
         "slotAmount":2,"slots":[1,0],
         "EMdata":[{"v":220.5,"c":1.2,"p":264.6,"e":10.5,"pf":0.98},
                   {"v":221.0,"c":0,"p":0,"e":0,"pf":0}]}
        """;

    /// <summary>
    /// 遗嘱（<c>close</c>）报文。真实设备的 will 报文极简，只有 method 与 imei，
    /// 这里刻意<b>不</b>加多余字段——离线判定只看 <c>method == "close"</c>，
    /// 加字段会掩盖「判定误依赖其它字段」的缺陷。
    /// </summary>
    private static string BuildClosePayload(string imei) => $$"""
        {"method":"close","imei":"{{imei}}"}
        """;

    /// <summary>设备上线（<c>connected</c>）报文。</summary>
    private static string BuildConnectedPayload(string imei) => $$"""
        {"method":"connected","imei":"{{imei}}","fwBuild":"20260801",
         "timestamp":{{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}}}
        """;
}
