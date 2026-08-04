using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IoTPlatform.Configuration;
using IoTPlatform.Infrastructure.Protocol.AnSheng;
using IoTPlatform.Models;
using IoTPlatform.Services;
using IoTPlatform.Services.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace IoTPlatform.AnSheng.Tests;

/// <summary>
/// T6 管道构件单元测试（QA 独立编写）：归一化器、事件时间戳判定、离线去抖器。
///
/// 【覆盖的验收项（数据层）】
///   · 验收 #4 数据层：<c>AnShengDeviceEvent.ResolveOccurredAt</c> 的取值与回退规则；
///   · 验收 #5 数据层：<c>AnShengOfflineDebouncer</c> 的 Arm / Cancel 语义（注入短窗口）；
///   · 验收 #6 数据层：<c>getDevStatus</c> 归一化后出现 <c>slot{n}_*</c> 展平字段。
///
/// 【与集成测试的分工】本文件只验证「构件行为正确」，
///   「构件被正确接线并真的落库」由 <c>IoTPlatform.IntegrationTests</c> 验证。
///   两层缺一不可：只有单测会漏掉 DI 未注册，只有集成会让失败定位困难。
/// </summary>
public sealed class AnShengEventPipelineUnitTests
{
    private const string Imei = "864536072949900";

    private readonly AnShengMessageParser _parser = new();

    private AnShengDataNormalizer NewNormalizer() => new(_parser);

    // ─────────────────────────────────────────────────────────────
    // 验收 #6 数据层：getDevStatus 展平出 slot 字段
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 验收 #6：<c>getDevStatus</c> 归一化后必须出现按路展平的 <c>slot{n}_*</c> 字段。
    ///
    /// 【为什么这是验收项】平台的 DataRule 按<b>字段名</b>取值，数组永远命中不了任何规则。
    /// 没有这批展平键，「第 1 路电压超 250V 告警」这类最基本的规则物理上无法配置。
    /// </summary>
    [Fact]
    public void NormalizeDevStatus_Should_Emit_Flattened_Slot_Fields()
    {
        var message = _parser.Parse(DevStatusPayload());
        Assert.NotNull(message);

        var points = NewNormalizer().Normalize(message);

        // 第 1 路
        Assert.Equal(1, points["slot1_state"]);
        Assert.Equal(220.5d, points["slot1_voltage"]);
        Assert.Equal(1.2d, points["slot1_current"]);
        Assert.Equal(264.6d, points["slot1_power"]);
        Assert.Equal(10.5d, points["slot1_energy"]);
        Assert.Equal(0.98d, points["slot1_pf"]);

        // 第 2 路
        Assert.Equal(0, points["slot2_state"]);
        Assert.Equal(221.0d, points["slot2_voltage"]);

        // 序号从 1 开始，不得出现 slot0_*
        Assert.DoesNotContain(points.Keys, k => k.StartsWith("slot0_", StringComparison.Ordinal));
    }

    /// <summary>
    /// 向后兼容硬约束：展平只做「加法」，既有旧键<b>一个都不能少</b>。
    /// 线上已有 DataRule 依赖它们，删一个就是一次线上事故（设计文档 §10 第 7 条）。
    /// </summary>
    [Fact]
    public void NormalizeDevStatus_Should_Preserve_All_Legacy_Keys()
    {
        var message = _parser.Parse(DevStatusPayload());
        Assert.NotNull(message);

        var points = NewNormalizer().Normalize(message);

        foreach (var legacyKey in new[]
                 {
                     "method", "imei", "net_type", "iccid", "signal", "temperature", "gps",
                     "slot_count", "slots", "total_power", "total_energy", "total_current",
                     "avg_voltage", "em_data", "tasks", "raw_timestamp", "timestamp_utc"
                 })
        {
            Assert.True(points.ContainsKey(legacyKey), $"旧键 {legacyKey} 缺失，将击穿既有 DataRule");
        }

        // 汇总量算法不得漂移
        Assert.Equal(264.6d + 0d, (double)points["total_power"]!, 3);
        Assert.Equal(10.5d + 0.0d, (double)points["total_energy"]!, 3);
        Assert.Equal((220.5d + 221.0d) / 2, (double)points["avg_voltage"]!, 3);
    }

    /// <summary>
    /// 设备只上报了部分量时，未上报的量<b>不得</b>被写成 0。
    /// 否则规则引擎会把「没上报」误当成「实测为 0」，触发假告警。
    /// </summary>
    [Fact]
    public void NormalizeDevStatus_Should_Skip_Unreported_Meter_Fields()
    {
        const string payload = """
        {"method":"getDevStatus","imei":"864536072949900","timestamp":1745456483,
         "slots":[1],"EMdata":[{"v":220.5}]}
        """;

        var message = _parser.Parse(payload);
        Assert.NotNull(message);

        var points = NewNormalizer().Normalize(message);

        Assert.True(points.ContainsKey("slot1_voltage"));
        Assert.False(points.ContainsKey("slot1_current"), "未上报的电流不应被补 0");
        Assert.False(points.ContainsKey("slot1_power"));
        Assert.False(points.ContainsKey("slot1_energy"));
    }

    // ─────────────────────────────────────────────────────────────
    // 事件报文归一化
    // ─────────────────────────────────────────────────────────────

    /// <summary>keyEvent：输出 <c>event</c> 标识与按键序号，供规则引擎按 key 命中。</summary>
    [Fact]
    public void NormalizeEvent_KeyEvent_Should_Emit_Event_And_Key_Index()
    {
        const string payload = """
        {"method":"keyEvent","imei":"864536072949900","key":2,"timestamp":1745456483}
        """;

        var message = _parser.Parse(payload);
        Assert.NotNull(message);

        var points = NewNormalizer().Normalize(message);

        Assert.Equal("keyEvent", points["event"]);
        Assert.Equal(2, points["event_key"]);
        Assert.Equal(Imei, points["imei"]);
    }

    /// <summary>delayEvent：输出位路号，并保留 frame_id 供与下发命令对账。</summary>
    [Fact]
    public void NormalizeEvent_DelayEvent_Should_Emit_SlotNum_And_FrameId()
    {
        const string payload = """
        {"method":"delayEvent","imei":"864536072949900","slotNum":3,
         "frameId":"1745456483900","timestamp":1745456483}
        """;

        var message = _parser.Parse(payload);
        Assert.NotNull(message);

        var points = NewNormalizer().Normalize(message);

        Assert.Equal(3, points["slot_num"]);
        Assert.Equal("1745456483900", points["frame_id"]);
    }

    /// <summary>
    /// recv485：透传帧无损落入 <c>rs485_hex</c>，并附字节长度与通道号。
    /// 决策 2 明确不建 485 专用表，数据完整性全靠这几个键。
    /// </summary>
    [Fact]
    public void NormalizeEvent_Recv485_Should_Emit_Rs485_Fields()
    {
        const string payload = """
        {"method":"recv485","imei":"864536072949900","num":1,
         "data":"0103040001000A","timestamp":1745456483}
        """;

        var message = _parser.Parse(payload);
        Assert.NotNull(message);

        var points = NewNormalizer().Normalize(message);

        Assert.Equal("0103040001000A", points["rs485_hex"]);
        Assert.Equal(7, points["rs485_len"]);
        Assert.Equal(1, points["rs485_port"]);
    }

    /// <summary>
    /// 固件新增的未知顶层字段必须原样透传，不得静默丢弃。
    /// 硬编码白名单会让固件迭代出的新字段无声消失，排障时无从追溯。
    /// </summary>
    [Fact]
    public void NormalizeEvent_Should_Passthrough_Unknown_Fields()
    {
        const string payload = """
        {"method":"connected","imei":"864536072949900","fwBuild":"20260801","timestamp":1745456483}
        """;

        var message = _parser.Parse(payload);
        Assert.NotNull(message);

        var points = NewNormalizer().Normalize(message);

        Assert.Equal("20260801", points["fwBuild"]);
    }

    /// <summary>报文体非法时只输出信封，绝不抛异常——上行线程崩了整条管道就哑了。</summary>
    [Fact]
    public void NormalizeToJson_Should_Never_Throw()
    {
        Assert.Equal("{}", NewNormalizer().NormalizeToJson(null));
    }

    // ─────────────────────────────────────────────────────────────
    // 验收 #4 数据层：OccurredAt 取值与回退
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 设备时间戳落在合理区间 <c>[ReceivedAt-24h, ReceivedAt+5min]</c> 内时优先采用。
    /// 事件的业务时间应是「设备上发生的时刻」，而非「平台收到的时刻」。
    /// </summary>
    [Fact]
    public void ResolveOccurredAt_Should_Prefer_Device_Timestamp_When_Reasonable()
    {
        var receivedAt = new DateTime(2026, 8, 3, 12, 0, 0, DateTimeKind.Utc);
        var deviceTs = receivedAt.AddSeconds(-30);

        var occurredAt = AnShengDeviceEvent.ResolveOccurredAt(deviceTs, receivedAt, out var usedFallback);

        Assert.Equal(deviceTs, occurredAt);
        Assert.False(usedFallback);
    }

    /// <summary>
    /// 设备时间戳越界（未校时的设备常年停在出厂时间，或超前数年）时回退到 ReceivedAt。
    ///
    /// 【为什么必须回退】OccurredAt 是事件表的主查询维度，也是 DataRule 的时间基准。
    /// 一条 2000 年的事件会永远排在时间线最前，且落在任何「近 N 小时」窗口之外，等同于丢失。
    /// </summary>
    [Theory]
    [InlineData(-25 * 60)]  // 落后 25 小时，超出 24h 下界
    [InlineData(6)]         // 超前 6 分钟，超出 5min 上界
    public void ResolveOccurredAt_Should_Fallback_When_Device_Timestamp_Out_Of_Range(int offsetMinutes)
    {
        var receivedAt = new DateTime(2026, 8, 3, 12, 0, 0, DateTimeKind.Utc);
        var deviceTs = receivedAt.AddMinutes(offsetMinutes);

        var occurredAt = AnShengDeviceEvent.ResolveOccurredAt(deviceTs, receivedAt, out var usedFallback);

        Assert.Equal(receivedAt, occurredAt);
        Assert.True(usedFallback);
    }

    /// <summary>报文未带时间戳时回退到 ReceivedAt 并标记 fallback。</summary>
    [Fact]
    public void ResolveOccurredAt_Should_Fallback_When_Device_Timestamp_Missing()
    {
        var receivedAt = new DateTime(2026, 8, 3, 12, 0, 0, DateTimeKind.Utc);

        var occurredAt = AnShengDeviceEvent.ResolveOccurredAt(null, receivedAt, out var usedFallback);

        Assert.Equal(receivedAt, occurredAt);
        Assert.True(usedFallback);
    }

    /// <summary>边界值必须<b>包含</b>在有效区间内（闭区间），避免刚好卡点的事件被误回退。</summary>
    [Theory]
    [InlineData(-24 * 60)]
    [InlineData(5)]
    public void ResolveOccurredAt_Boundaries_Should_Be_Inclusive(int offsetMinutes)
    {
        var receivedAt = new DateTime(2026, 8, 3, 12, 0, 0, DateTimeKind.Utc);
        var deviceTs = receivedAt.AddMinutes(offsetMinutes);

        var occurredAt = AnShengDeviceEvent.ResolveOccurredAt(deviceTs, receivedAt, out var usedFallback);

        Assert.Equal(deviceTs, occurredAt);
        Assert.False(usedFallback);
    }

    // ─────────────────────────────────────────────────────────────
    // 验收 #5 数据层：离线去抖
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 验收 #5 核心：<c>close</c> 起窗后，窗口内收到 <c>connected</c> ⇒ <b>不得</b>置离线。
    /// 4G 设备基站切换会导致秒级断连，不去抖会在页面上刷出大量假掉线告警。
    /// </summary>
    [Fact]
    public async Task Debouncer_Should_Not_Set_Offline_When_Connected_Arrives_Within_Window()
    {
        var discovery = new RecordingDiscoveryService();
        var debouncer = NewDebouncer(discovery, windowSeconds: 2);

        debouncer.Arm(Imei, "TEST");
        Assert.True(debouncer.IsArmed(Imei));

        debouncer.Cancel(Imei);
        Assert.False(debouncer.IsArmed(Imei));

        // 等到远超窗口的时刻再断言，确保「延迟置离线」也不会偷偷发生
        await Task.Delay(TimeSpan.FromSeconds(3));

        Assert.Empty(discovery.OfflineCalls);
    }

    /// <summary>窗口到期且无人撤销 ⇒ 必须真的置离线，且透传租户码。</summary>
    [Fact]
    public async Task Debouncer_Should_Set_Offline_When_Window_Expires()
    {
        var discovery = new RecordingDiscoveryService();
        var debouncer = NewDebouncer(discovery, windowSeconds: 1);

        debouncer.Arm(Imei, "TEST");

        var fired = await discovery.WaitForOfflineAsync(TimeSpan.FromSeconds(5));

        Assert.True(fired, "去抖窗口到期后应回调 OnDeviceOfflineAsync");
        Assert.Single(discovery.OfflineCalls);
        Assert.Equal((Imei, "TEST"), discovery.OfflineCalls[0]);
        Assert.False(debouncer.IsArmed(Imei), "置离线后应摘除窗口");
    }

    /// <summary>
    /// 连续多次 <c>close</c> 只应产生<b>一次</b>置离线：旧窗口被新窗口原子替换。
    /// 若实现让每个窗口都独立到期，设备会被反复置离线，产生重复告警。
    /// </summary>
    [Fact]
    public async Task Debouncer_Repeated_Arm_Should_Only_Fire_Once()
    {
        var discovery = new RecordingDiscoveryService();
        var debouncer = NewDebouncer(discovery, windowSeconds: 1);

        debouncer.Arm(Imei, "TEST");
        debouncer.Arm(Imei, "TEST");
        debouncer.Arm(Imei, "TEST");

        await Task.Delay(TimeSpan.FromSeconds(3));

        Assert.Single(discovery.OfflineCalls);
    }

    /// <summary>去抖窗口按 IMEI 隔离，一台设备的 connected 不得撤销另一台的离线判定。</summary>
    [Fact]
    public async Task Debouncer_Windows_Should_Be_Isolated_Per_Imei()
    {
        const string other = "864536072949901";
        var discovery = new RecordingDiscoveryService();
        var debouncer = NewDebouncer(discovery, windowSeconds: 1);

        debouncer.Arm(Imei, "TEST");
        debouncer.Arm(other, "TEST");
        debouncer.Cancel(other);

        await Task.Delay(TimeSpan.FromSeconds(3));

        Assert.Single(discovery.OfflineCalls);
        Assert.Equal(Imei, discovery.OfflineCalls[0].Imei);
    }

    /// <summary>配置为 0 / 负数时窗口应被归一化为 1 秒，而不是「立即到期」使去抖形同虚设。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void EffectiveCloseDebounceSeconds_Should_Clamp_To_At_Least_One(int configured)
    {
        var options = new AnShengEventOptions { CloseDebounceSeconds = configured };

        Assert.Equal(1, options.EffectiveCloseDebounceSeconds);
    }

    /// <summary><c>ClearAll</c> 必须撤销全部在途窗口，供测试隔离使用。</summary>
    [Fact]
    public async Task Debouncer_ClearAll_Should_Cancel_All_Windows()
    {
        var discovery = new RecordingDiscoveryService();
        var debouncer = NewDebouncer(discovery, windowSeconds: 1);

        debouncer.Arm(Imei, "TEST");
        debouncer.Arm("864536072949901", "TEST");
        debouncer.ClearAll();

        await Task.Delay(TimeSpan.FromSeconds(3));

        Assert.Empty(discovery.OfflineCalls);
    }

    // ─────────────────────────────────────────────────────────────
    // 辅助
    // ─────────────────────────────────────────────────────────────

    private static AnShengOfflineDebouncer NewDebouncer(
        IAnShengDiscoveryService discovery,
        int windowSeconds)
        => new(
            Options.Create(new AnShengEventOptions { CloseDebounceSeconds = windowSeconds }),
            discovery,
            NullLogger<AnShengOfflineDebouncer>.Instance);

    private static string DevStatusPayload() => """
    {"method":"getDevStatus","imei":"864536072949900","result":"success","timestamp":1745456483,
     "netType":"4G","iccid":"89860000000000000000","signal":25,"temperature":36.5,
     "slotAmount":2,"slots":[1,0],
     "EMdata":[{"v":220.5,"c":1.2,"p":264.6,"e":10.5,"pf":0.98},{"v":221.0,"c":0,"p":0,"e":0,"pf":0}]}
    """;

    /// <summary>
    /// 录制型发现服务替身：只记录 <c>OnDeviceOfflineAsync</c> 的调用，供去抖用例断言。
    /// 其余成员抛异常——去抖器不应触碰它们，一旦触碰应立即暴露而非静默通过。
    /// </summary>
    private sealed class RecordingDiscoveryService : IAnShengDiscoveryService
    {
        private readonly ConcurrentQueue<(string Imei, string? AppCode)> _offline = new();
        private readonly SemaphoreSlim _signal = new(0);

        public IReadOnlyList<(string Imei, string? AppCode)> OfflineCalls => _offline.ToArray();

        public async Task<bool> WaitForOfflineAsync(TimeSpan timeout)
            => await _signal.WaitAsync(timeout);

        public Task OnDeviceOfflineAsync(string imei, string? appCode, CancellationToken ct = default)
        {
            _offline.Enqueue((imei, appCode));
            _signal.Release();
            return Task.CompletedTask;
        }

        public Task OnDeviceOnlineAsync(
            string imei, string? model, string? netType, string? appCode, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<AnShengClaimResult> ClaimAsync(AnShengClaimCommand command, CancellationToken ct = default)
            => throw new NotSupportedException("去抖器不应调用 ClaimAsync");
    }
}
