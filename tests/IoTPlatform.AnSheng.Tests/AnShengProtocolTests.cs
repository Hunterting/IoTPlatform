using System.Diagnostics;
using System.Text.Json;
using IoTPlatform.Infrastructure.Protocol.AnSheng;
using Xunit;

namespace IoTPlatform.AnSheng.Tests;

/// <summary>
/// 安圣二开协议 Phase 1（T1 命令目录 / T2 报文结构 / T3 伪命令清理）验收测试。
/// 每个 Fact 对应任务书中的一条验收标准。
/// </summary>
public class AnShengProtocolTests
{
    private readonly AnShengCommandBuilder _builder = new();
    private readonly AnShengMessageParser _parser = new();

    // ─────────────────────────────────────────────────────────────
    // 验收 1：命令目录收录 36 个方法
    // ─────────────────────────────────────────────────────────────

    /// <summary>目录必须恰好收录 asopen.md 中的 36 个 <c>##</c> 方法小节。</summary>
    [Fact]
    public void Catalog_Should_Contain_Exactly_36_Methods()
    {
        Assert.Equal(36, AnShengCommandCatalog.Count);
        Assert.Equal(36, AnShengCommandCatalog.Commands.Count);
    }

    /// <summary>目录中每条命令的 method 唯一，且都填写了文档锚点。</summary>
    [Fact]
    public void Catalog_Methods_Should_Be_Unique_And_Documented()
    {
        var methods = AnShengCommandCatalog.Commands.Values.Select(c => c.Method).ToList();
        Assert.Equal(methods.Count, methods.Distinct(StringComparer.Ordinal).Count());
        Assert.All(AnShengCommandCatalog.Commands.Values, c =>
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Method));
            Assert.False(string.IsNullOrWhiteSpace(c.Title));
            Assert.False(string.IsNullOrWhiteSpace(c.DocAnchor));
        });
    }

    // ─────────────────────────────────────────────────────────────
    // 验收 2：事件方法共 6 个
    // ─────────────────────────────────────────────────────────────

    /// <summary>事件集合应为 connected/keyEvent/delayEvent/timeEvent/recv485/close 共 6 个。</summary>
    [Fact]
    public void Catalog_Should_Contain_Exactly_6_Events()
    {
        Assert.Equal(6, AnShengCommandCatalog.EventMethods.Count);

        foreach (var evt in new[] { "connected", "keyEvent", "delayEvent", "timeEvent", "recv485", "close" })
        {
            Assert.True(AnShengCommandCatalog.IsEvent(evt), $"{evt} 应被识别为事件");
        }

        Assert.False(AnShengCommandCatalog.IsEvent("action"));
        Assert.False(AnShengCommandCatalog.IsEvent("getDevStatus"));
    }

    /// <summary>事件不可由平台下发。</summary>
    [Fact]
    public void BuildCommand_Should_Reject_Event_Methods()
    {
        var ex = Assert.Throws<NotSupportedException>(
            () => _builder.BuildCommand("864536072949900", "keyEvent", null, AnShengDeviceKind.Switch4G));
        Assert.Contains("keyEvent", ex.Message);
    }

    // ─────────────────────────────────────────────────────────────
    // 验收 3：能力矩阵 IsSupported 判定正确
    // ─────────────────────────────────────────────────────────────

    /// <summary>G1 通用命令四款设备全部支持。</summary>
    [Theory]
    [InlineData(AnShengDeviceKind.Speaker4G)]
    [InlineData(AnShengDeviceKind.Switch4G)]
    [InlineData(AnShengDeviceKind.SpeakerWiFi)]
    [InlineData(AnShengDeviceKind.SwitchWiFi)]
    public void Group1_Common_Commands_Supported_By_All_Kinds(AnShengDeviceKind kind)
    {
        foreach (var method in new[] { "getDevInfo", "getDevStatus", "reboot", "getKeyConfig", "setKeyConfig" })
        {
            Assert.True(AnShengCommandCatalog.IsSupported(method, kind), $"{kind} 应支持 {method}");
        }
    }

    /// <summary>G3 开关动作仅开关款支持，喇叭款不支持。</summary>
    [Fact]
    public void Group3_Switch_Actions_Only_For_Switch_Kinds()
    {
        foreach (var method in new[] { "action", "actions", "startDelayTask", "stopDelayTask", "getEMRealtime" })
        {
            Assert.True(AnShengCommandCatalog.IsSupported(method, AnShengDeviceKind.Switch4G));
            Assert.True(AnShengCommandCatalog.IsSupported(method, AnShengDeviceKind.SwitchWiFi));
            Assert.False(AnShengCommandCatalog.IsSupported(method, AnShengDeviceKind.Speaker4G));
            Assert.False(AnShengCommandCatalog.IsSupported(method, AnShengDeviceKind.SpeakerWiFi));
        }
    }

    /// <summary>G4 定时任务/电量统计/日志/RS485 仅 4G 开关支持。</summary>
    [Fact]
    public void Group4_Commands_Only_For_Switch4G()
    {
        foreach (var method in new[] { "getTimeTasks", "setTimeTasks", "getEMStatistics", "getLogs", "send485" })
        {
            Assert.True(AnShengCommandCatalog.IsSupported(method, AnShengDeviceKind.Switch4G), method);
            Assert.False(AnShengCommandCatalog.IsSupported(method, AnShengDeviceKind.SwitchWiFi), method);
            Assert.False(AnShengCommandCatalog.IsSupported(method, AnShengDeviceKind.Speaker4G), method);
            Assert.False(AnShengCommandCatalog.IsSupported(method, AnShengDeviceKind.SpeakerWiFi), method);
        }
    }

    /// <summary>G5 对时/物联卡仅 4G 款支持。</summary>
    [Fact]
    public void Group5_Commands_Only_For_4G_Kinds()
    {
        foreach (var method in new[] { "setTime", "getSimCheck", "setSimCheck" })
        {
            Assert.True(AnShengCommandCatalog.IsSupported(method, AnShengDeviceKind.Speaker4G), method);
            Assert.True(AnShengCommandCatalog.IsSupported(method, AnShengDeviceKind.Switch4G), method);
            Assert.False(AnShengCommandCatalog.IsSupported(method, AnShengDeviceKind.SpeakerWiFi), method);
            Assert.False(AnShengCommandCatalog.IsSupported(method, AnShengDeviceKind.SwitchWiFi), method);
        }
    }

    /// <summary>不支持的品类下发时应抛 <see cref="NotSupportedException"/>。</summary>
    [Fact]
    public void BuildCommand_Should_Reject_Unsupported_Kind()
    {
        Assert.Throws<NotSupportedException>(
            () => _builder.BuildAction("864536072949900", 1, "on", null, AnShengDeviceKind.Speaker4G));
    }

    // ─────────────────────────────────────────────────────────────
    // 验收 4：报文不含 param 包裹，参数平铺；timestamp 为 10 位秒级 int
    // ─────────────────────────────────────────────────────────────

    /// <summary>4G 报文：参数平铺、无 param、timestamp 为 10 位秒级整数。</summary>
    [Fact]
    public void Build_4G_Command_Should_Be_Flat_With_Second_Level_Int_Timestamp()
    {
        var (frameId, payload) = _builder.BuildAction("864536072949900", 1, "on", null, AnShengDeviceKind.Switch4G);

        Assert.DoesNotContain("\"param\"", payload);

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;

        Assert.Equal("action", root.GetProperty("method").GetString());
        Assert.Equal("864536072949900", root.GetProperty("imei").GetString());
        Assert.Equal(1, root.GetProperty("slotNum").GetInt32());          // 平铺
        Assert.Equal("on", root.GetProperty("action").GetString());       // 平铺
        Assert.Equal(frameId, root.GetProperty("frameId").GetString());

        var ts = root.GetProperty("timestamp");
        Assert.Equal(JsonValueKind.Number, ts.ValueKind);                 // int，不是字符串
        var seconds = ts.GetInt64();
        Assert.Equal(10, seconds.ToString().Length);                      // 10 位 = 秒级
        Assert.InRange(seconds, DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 5,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 5);
    }

    /// <summary>报文必须为压缩 JSON（无换行、无缩进空格）。</summary>
    [Fact]
    public void Build_Command_Should_Be_Minified_Json()
    {
        var (_, payload) = _builder.BuildGetDevInfo("864536072949900", AnShengDeviceKind.Switch4G);

        Assert.DoesNotContain("\n", payload);
        Assert.DoesNotContain("\r", payload);
        Assert.DoesNotContain(": ", payload);
        Assert.DoesNotContain(", ", payload);
    }

    // ─────────────────────────────────────────────────────────────
    // 验收 5：WiFi 款完全不注入 timestamp
    // ─────────────────────────────────────────────────────────────

    /// <summary>WiFi 款报文中必须完全没有 timestamp 字段。</summary>
    [Theory]
    [InlineData(AnShengDeviceKind.SpeakerWiFi)]
    [InlineData(AnShengDeviceKind.SwitchWiFi)]
    public void Build_WiFi_Command_Should_Omit_Timestamp(AnShengDeviceKind kind)
    {
        var (_, payload) = _builder.BuildGetDevStatus("864536072949900", null, kind);

        Assert.DoesNotContain("timestamp", payload);

        using var doc = JsonDocument.Parse(payload);
        Assert.False(doc.RootElement.TryGetProperty("timestamp", out _));
    }

    /// <summary>品类未知时保守处理：不注入 timestamp。</summary>
    [Fact]
    public void Build_Unknown_Kind_Should_Omit_Timestamp()
    {
        var (_, payload) = _builder.BuildGetDevInfo("864536072949900");
        Assert.DoesNotContain("timestamp", payload);
    }

    /// <summary><c>setTime</c> 的 timestamp 是业务参数，WiFi 也应携带（但 WiFi 不支持该命令）。</summary>
    [Fact]
    public void BuildSetTime_Should_Carry_Second_Level_Timestamp_Param()
    {
        var when = new DateTime(2025, 4, 23, 8, 25, 59, DateTimeKind.Utc);
        var (_, payload) = _builder.BuildSetTime("864536072949900", when, AnShengDeviceKind.Switch4G);

        using var doc = JsonDocument.Parse(payload);
        Assert.Equal(new DateTimeOffset(when).ToUnixTimeSeconds(),
            doc.RootElement.GetProperty("timestamp").GetInt64());
    }

    // ─────────────────────────────────────────────────────────────
    // 验收 6：frameId 为 16 位唯一串
    // ─────────────────────────────────────────────────────────────

    /// <summary>frameId 长度恒为 16，且大批量生成不重复。</summary>
    [Fact]
    public void FrameId_Should_Be_16_Chars_And_Unique()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < 20000; i++)
        {
            var id = AnShengCommandBuilder.NewFrameId();
            Assert.Equal(16, id.Length);
            Assert.True(ids.Add(id), $"frameId 重复: {id}");
        }
    }

    /// <summary>报文中的 frameId 与返回值一致且为 16 位。</summary>
    [Fact]
    public void Build_Command_FrameId_Should_Match_Payload()
    {
        var (frameId, payload) = _builder.BuildReboot("864536072949900", AnShengDeviceKind.Switch4G);
        Assert.Equal(16, frameId.Length);

        using var doc = JsonDocument.Parse(payload);
        Assert.Equal(frameId, doc.RootElement.GetProperty("frameId").GetString());
    }

    // ─────────────────────────────────────────────────────────────
    // 验收 7：解析器兼容 4 种 timestamp 形态
    // ─────────────────────────────────────────────────────────────

    /// <summary>秒级 / 毫秒级 / 字符串数字 / 缺失 四种 timestamp 都能正确解析。</summary>
    [Fact]
    public void Parser_Should_Handle_Four_Timestamp_Forms()
    {
        const long seconds = 1745396759L;

        // 1) 秒级 int
        var sec = _parser.Parse($"{{\"method\":\"getDevStatus\",\"imei\":\"1\",\"timestamp\":{seconds}}}");
        Assert.NotNull(sec);
        Assert.Equal(seconds, sec!.RawTimestamp);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime, sec.TimestampUtc);

        // 2) 毫秒级 long（归一化为秒）
        var ms = _parser.Parse($"{{\"method\":\"getDevStatus\",\"imei\":\"1\",\"timestamp\":{seconds * 1000}}}");
        Assert.NotNull(ms);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime, ms!.TimestampUtc);

        // 3) 字符串数字
        var str = _parser.Parse($"{{\"method\":\"getDevStatus\",\"imei\":\"1\",\"timestamp\":\"{seconds}\"}}");
        Assert.NotNull(str);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime, str!.TimestampUtc);

        // 4) 缺失（WiFi 款）
        var none = _parser.Parse("{\"method\":\"getDevStatus\",\"imei\":\"1\"}");
        Assert.NotNull(none);
        Assert.Null(none!.RawTimestamp);
        Assert.Null(none.TimestampUtc);
    }

    /// <summary>解析器应能读取平铺在顶层的业务字段（二开协议）。</summary>
    [Fact]
    public void Parser_Should_Read_Flat_Body_For_Open_Protocol()
    {
        const string json = "{\"method\":\"getDevStatus\",\"result\":\"ok\",\"imei\":\"864536072949900\","
                            + "\"netType\":\"4G\",\"signal\":22,\"temperature\":\"32.4\",\"slots\":[1,0],"
                            + "\"EMdata\":[{\"v\":220.1,\"c\":1.2,\"p\":264.0,\"e\":3.5}],\"timestamp\":1745396759}";

        var message = _parser.Parse(json);
        Assert.NotNull(message);
        Assert.True(message!.IsOk);

        var status = _parser.ParseDevStatus(message);
        Assert.NotNull(status);
        Assert.Equal("4G", status!.NetType);
        Assert.Equal(22, status.Signal);
        Assert.Equal(32.4, status.Temperature!.Value, 3);     // 字符串数字容错
        Assert.Equal(2, status.SlotCount);
        Assert.Single(status.EmData!);
        Assert.Equal(220.1, status.EmData![0].V!.Value, 3);
    }

    // ─────────────────────────────────────────────────────────────
    // 验收 8：离线判定只看 method == "close"
    // ─────────────────────────────────────────────────────────────

    /// <summary>will 报文 <c>{"imei":"X","method":"close"}</c> 应判定为离线。</summary>
    [Fact]
    public void Will_Message_Should_Be_Detected_By_Method_Close()
    {
        var will = _parser.Parse("{\"imei\":\"864536072949900\",\"method\":\"close\"}");
        Assert.NotNull(will);
        Assert.True(AnShengMessageParser.IsWillMessage(will));
        Assert.Equal(AnShengMessageCategory.Close, _parser.GetCategory(will!));
    }

    /// <summary>其他事件（如 keyEvent）不得被误判为离线。</summary>
    [Fact]
    public void Non_Close_Events_Should_Not_Be_Will()
    {
        foreach (var method in new[] { "keyEvent", "connected", "delayEvent", "timeEvent", "recv485" })
        {
            var msg = _parser.Parse($"{{\"imei\":\"864536072949900\",\"method\":\"{method}\"}}");
            Assert.NotNull(msg);
            Assert.False(AnShengMessageParser.IsWillMessage(msg), $"{method} 不应判定为离线");
            Assert.Equal(AnShengMessageCategory.Event, _parser.GetCategory(msg!));
        }
    }

    /// <summary>离线判定不依赖主题——同一主题下 close 与 keyEvent 结果必须不同。</summary>
    [Fact]
    public void Will_Detection_Should_Not_Depend_On_Topic()
    {
        const string topic = "/iot/server/iot-board/864536072949900";
        Assert.Equal("864536072949900", AnShengMessageParser.ExtractImeiFromTopic(topic));

        var close = _parser.Parse("{\"imei\":\"864536072949900\",\"method\":\"close\"}");
        var key = _parser.Parse("{\"imei\":\"864536072949900\",\"method\":\"keyEvent\",\"keyNum\":1}");

        Assert.True(AnShengMessageParser.IsWillMessage(close));
        Assert.False(AnShengMessageParser.IsWillMessage(key));
    }

    // ─────────────────────────────────────────────────────────────
    // 验收 9：伪命令已彻底移除
    // ─────────────────────────────────────────────────────────────

    /// <summary>四个臆造方法不得存在于命令目录中，且不可被构建。</summary>
    [Theory]
    [InlineData("setSwitch")]
    [InlineData("getSwitchStatus")]
    [InlineData("setSwitchConfig")]
    [InlineData("getSwitchConfig")]
    public void Pseudo_Commands_Should_Not_Exist(string method)
    {
        Assert.False(AnShengCommandCatalog.Contains(method), $"{method} 不应存在于目录中");
        Assert.False(AnShengCommandCatalog.TryGet(method, out _));

        var ex = Assert.Throws<ArgumentException>(
            () => _builder.BuildCommand("864536072949900", method, null, AnShengDeviceKind.Switch4G));
        Assert.Contains(method, ex.Message);
    }

    // ─────────────────────────────────────────────────────────────
    // 验收 10：Legacy 充电桩协议族保持不变
    // ─────────────────────────────────────────────────────────────

    /// <summary>Legacy orderStart 仍使用 <c>param</c> 包裹与毫秒字符串 timestamp。</summary>
    [Fact]
    public void Legacy_Charging_Pile_Commands_Should_Keep_Param_Wrapper()
    {
#pragma warning disable CS0618
        var (frameId, payload) = _builder.BuildOrderStart("864536072949900", "SN20250423001", 1, 3600);
#pragma warning restore CS0618

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;

        Assert.Equal("orderStart", root.GetProperty("method").GetString());
        Assert.Equal(frameId, root.GetProperty("frameId").GetString());

        var param = root.GetProperty("param");                        // param 包裹保留
        Assert.Equal("SN20250423001", param.GetProperty("sn").GetString());
        Assert.Equal(1, param.GetProperty("order").GetInt32());
        Assert.Equal(3600, param.GetProperty("limit").GetInt32());

        var ts = root.GetProperty("timestamp");
        Assert.Equal(JsonValueKind.String, ts.ValueKind);             // 毫秒字符串
        Assert.Equal(13, ts.GetString()!.Length);
    }

    /// <summary>Legacy 订单报文（param 内）仍能被解析与标准化。</summary>
    [Fact]
    public void Legacy_Order_Message_Should_Still_Parse()
    {
        const string json = "{\"method\":\"orderUp\",\"imei\":\"864536072949900\",\"frameId\":\"abc\","
                            + "\"timestamp\":\"1745396759000\",\"param\":{\"sn\":\"SN001\",\"order\":1,"
                            + "\"p\":264.0,\"e\":3.5,\"timing\":1200,\"limit\":3600}}";

        var message = _parser.Parse(json);
        Assert.NotNull(message);
        Assert.Equal(AnShengMessageCategory.OrderUp, _parser.GetCategory(message!));

        var order = _parser.ParseOrderData(message!);
        Assert.NotNull(order);
        Assert.Equal("SN001", order!.Sn);
        Assert.Equal(1, order.Order);
        Assert.Equal(264.0, order.P!.Value, 3);
        Assert.Equal(1200, order.Timing);

        var normalized = _parser.NormalizeForSensorData(message!, "/iot/server/iot-board/864536072949900");
        using var doc = JsonDocument.Parse(normalized);
        Assert.Equal("SN001", doc.RootElement.GetProperty("sn").GetString());

        // raw_timestamp 保留设备原样上报值（此处为毫秒），timestamp_utc 为归一化结果
        Assert.Equal(1745396759000L, doc.RootElement.GetProperty("raw_timestamp").GetInt64());
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1745396759L).UtcDateTime,
            doc.RootElement.GetProperty("timestamp_utc").GetDateTime());
    }

    /// <summary>Legacy 充电桩 getDevStatus（param 内）不因二开改造而失效。</summary>
    [Fact]
    public void Legacy_DevStatus_In_Param_Should_Still_Parse()
    {
        const string json = "{\"method\":\"getDevStatus\",\"imei\":\"864536072949900\","
                            + "\"param\":{\"netType\":\"4G\",\"signal\":18,"
                            + "\"EMdata\":[{\"v\":220.0,\"c\":0.5,\"p\":110.0,\"e\":1.25}]}}";

        var message = _parser.Parse(json);
        Assert.NotNull(message);

        var status = _parser.ParseDevStatus(message!);
        Assert.NotNull(status);
        Assert.Equal("4G", status!.NetType);
        Assert.Equal(18, status.Signal);
        Assert.Equal(1, status.SlotCount);
    }

    // ─────────────────────────────────────────────────────────────
    // 附加：限流 / 品类识别 / 固件版本
    // ─────────────────────────────────────────────────────────────

    /// <summary>同一 IMEI 连续下发必须间隔 ≥100ms。</summary>
    [Fact]
    public async Task Throttle_Should_Enforce_100ms_Per_Imei()
    {
        using var throttle = new AnShengCommandThrottle();
        const string imei = "864536072949900";

        await throttle.WaitTurnAsync(imei);
        var sw = Stopwatch.StartNew();
        await throttle.WaitTurnAsync(imei);
        await throttle.WaitTurnAsync(imei);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds >= 190,
            $"两次间隔应 ≥200ms（容差 10ms），实际 {sw.ElapsedMilliseconds}ms");
    }

    /// <summary>不同 IMEI 之间互不限流。</summary>
    [Fact]
    public async Task Throttle_Should_Not_Block_Different_Imei()
    {
        using var throttle = new AnShengCommandThrottle();

        var sw = Stopwatch.StartNew();
        await throttle.WaitTurnAsync("864536072949901");
        await throttle.WaitTurnAsync("864536072949902");
        await throttle.WaitTurnAsync("864536072949903");
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 50, $"不同 IMEI 不应互相等待，实际 {sw.ElapsedMilliseconds}ms");
    }

    /// <summary>按 netType + model 正确识别四种品类。</summary>
    [Theory]
    [InlineData("4G", "SWITCH-EC618X-R24-O-V4.0.8", AnShengDeviceKind.Switch4G)]
    [InlineData("WiFi", "SWITCH-ESP32-R10-O-V2.1.0", AnShengDeviceKind.SwitchWiFi)]
    [InlineData("4G", "SPEAKER-EC618X-R24-O-V4.0.8", AnShengDeviceKind.Speaker4G)]
    [InlineData("WiFi", "SPEAKER-ESP32-R10-O-V2.1.0", AnShengDeviceKind.SpeakerWiFi)]
    public void DeviceKindResolver_Should_Resolve_All_Four_Kinds(
        string netType, string version, AnShengDeviceKind expected)
    {
        Assert.Equal(expected, AnShengDeviceKindResolver.Resolve(netType, version, null));
    }

    /// <summary>仅 4G 款支持 timestamp 注入。</summary>
    [Fact]
    public void Only_4G_Kinds_Support_Timestamp()
    {
        Assert.True(AnShengDeviceKind.Speaker4G.SupportsTimestamp());
        Assert.True(AnShengDeviceKind.Switch4G.SupportsTimestamp());
        Assert.False(AnShengDeviceKind.SpeakerWiFi.SupportsTimestamp());
        Assert.False(AnShengDeviceKind.SwitchWiFi.SupportsTimestamp());
        Assert.False(AnShengDeviceKind.Unknown.SupportsTimestamp());
    }

    /// <summary>固件版本能从形如 <c>SWITCH-EC618X-R24-O-V4.0.8</c> 的串中解析并比较。</summary>
    [Fact]
    public void FirmwareVersion_Should_Parse_And_Compare()
    {
        Assert.True(AnShengFirmwareVersion.TryParse("SWITCH-EC618X-R24-O-V4.0.8", out var v408));
        Assert.True(AnShengFirmwareVersion.TryParse("SWITCH-EC618X-R24-O-V4.0.20", out var v4020));

        Assert.NotNull(v408);
        Assert.NotNull(v4020);
        Assert.True(v4020! > v408!);
        Assert.True(AnShengFirmwareVersion.Satisfies("SWITCH-EC618X-R24-O-V4.0.20", "4.0.20"));
        Assert.False(AnShengFirmwareVersion.Satisfies("SWITCH-EC618X-R24-O-V4.0.8", "4.0.20"));
        Assert.True(AnShengFirmwareVersion.Satisfies(null, "4.0.20"));   // 版本未知时不拦截
    }

    /// <summary><c>getDevStatus</c> 的 <c>q</c> 参数要求固件 ≥ 4.0.20。</summary>
    [Fact]
    public void GetDevStatus_Q_Param_Should_Require_Firmware_4_0_20()
    {
        var spec = AnShengCommandCatalog.Get("getDevStatus")!;
        var parameters = new Dictionary<string, object?> { ["q"] = "slots" };

        Assert.False(spec.ValidateParams(parameters, "SWITCH-EC618X-R24-O-V4.0.8").IsValid);
        Assert.True(spec.ValidateParams(parameters, "SWITCH-EC618X-R24-O-V4.0.20").IsValid);
    }

    /// <summary>必填参数缺失时校验失败。</summary>
    [Fact]
    public void Required_Params_Should_Be_Validated()
    {
        var spec = AnShengCommandCatalog.Get("action")!;

        Assert.False(spec.ValidateParams(null).IsValid);
        Assert.False(spec.ValidateParams(new Dictionary<string, object?> { ["slotNum"] = 1 }).IsValid);
        Assert.True(spec.ValidateParams(new Dictionary<string, object?>
        {
            ["slotNum"] = 1,
            ["action"] = "on"
        }).IsValid);

        // 枚举值校验
        Assert.False(spec.ValidateParams(new Dictionary<string, object?>
        {
            ["slotNum"] = 1,
            ["action"] = "flip"
        }).IsValid);
    }

    /// <summary>slot 从 1 开始，0 表示全部——两者都应通过校验。</summary>
    [Fact]
    public void SlotNum_Zero_Means_All_Slots()
    {
        var spec = AnShengCommandCatalog.Get("action")!;

        Assert.True(spec.ValidateParams(new Dictionary<string, object?>
        {
            ["slotNum"] = 0,
            ["action"] = "off"
        }).IsValid);

        var (_, payload) = _builder.BuildAction("864536072949900", 0, "off", null, AnShengDeviceKind.Switch4G);
        using var doc = JsonDocument.Parse(payload);
        Assert.Equal(0, doc.RootElement.GetProperty("slotNum").GetInt32());
    }

    /// <summary><see cref="AnShengCommandCatalog.ListFor"/> 返回的命令全部被该品类支持。</summary>
    [Theory]
    [InlineData(AnShengDeviceKind.Speaker4G)]
    [InlineData(AnShengDeviceKind.Switch4G)]
    [InlineData(AnShengDeviceKind.SpeakerWiFi)]
    [InlineData(AnShengDeviceKind.SwitchWiFi)]
    public void ListFor_Should_Return_Only_Supported_Commands(AnShengDeviceKind kind)
    {
        var commands = AnShengCommandCatalog.ListFor(kind);
        Assert.NotEmpty(commands);
        Assert.All(commands, c => Assert.True(c.IsSupportedBy(kind)));
    }
}
