using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using IoTPlatform.Infrastructure.Protocol.AnSheng;
using Xunit;

namespace IoTPlatform.AnSheng.Tests;

/// <summary>
/// 安圣二开协议「一致性交叉验证」测试（QA 独立编写）。
///
/// 与 <see cref="AnShengProtocolTests"/> 的区别：
///   本文件的全部期望值均<b>逐字抄自官方协议原文 asopen.md</b>（注释标注原文行号），
///   而非依据实现反推，用于识别「实现与测试一致地误读协议」这一类缺陷。
///
/// 报文样例来源：asopen.md 各小节的「命令示例」「应答示例」代码块。
/// </summary>
public class AnShengProtocolConformanceTests
{
    private readonly AnShengCommandBuilder _builder = new();
    private readonly AnShengMessageParser _parser = new();

    private const string Imei = "864536072949900";

    // ─────────────────────────────────────────────────────────────
    // A. 命令目录 ↔ 协议原文逐条比对
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// asopen.md 中全部 36 个方法小节（`## 标题（method）`），按原文出现顺序，
    /// 每项为 (method, 原文行号)。非方法小节（通用命令 / 定时任务说明等）不计入。
    /// </summary>
    public static readonly (string Method, int DocLine)[] DocMethods =
    {
        ("getDevInfo", 189), ("getDevStatus", 291), ("connected", 585), ("keyEvent", 649),
        ("getKeyConfig", 715), ("setKeyConfig", 813), ("reboot", 917), ("getAutoReport", 1007),
        ("setAutoReport", 1125), ("getMqtt", 1285), ("setMqtt", 1471), ("action", 1705),
        ("actions", 1811), ("getDelayTasks", 1917), ("startDelayTask", 2047), ("stopDelayTask", 2155),
        ("delayEvent", 2251), ("getEMRealtime", 2331), ("getCalParams", 2469), ("setCalParams", 2579),
        ("resetCalParams", 2709), ("autoCal", 2819), ("getTimeTasks", 2959), ("setTimeTasks", 3197),
        ("getSlotTimeTasks", 3583), ("setSlotTimeTasks", 3797), ("timeEvent", 4119),
        ("getEMStatistics", 4251), ("clearEMStatistics", 4447), ("getLogs", 4541), ("send485", 4733),
        ("recv485", 4835), ("setTime", 4925), ("getSimCheck", 5019), ("setSimCheck", 5123),
        ("simCheck", 5237)
    };

    /// <summary>目录收录的 method 集合必须与协议原文 36 个小节逐字符一致（大小写敏感）。</summary>
    [Fact]
    public void Catalog_Methods_Should_Match_Doc_Sections_Char_By_Char()
    {
        var expected = DocMethods.Select(m => m.Method).OrderBy(m => m, StringComparer.Ordinal).ToArray();
        var actual = AnShengCommandCatalog.Commands.Keys.OrderBy(m => m, StringComparer.Ordinal).ToArray();

        Assert.Equal(expected, actual);
    }

    /// <summary>协议原文中的每个 method 都能被目录命中（防止拼写偏差）。</summary>
    [Theory]
    [MemberData(nameof(DocMethodNames))]
    public void Every_Doc_Method_Should_Exist_In_Catalog(string method)
    {
        Assert.True(AnShengCommandCatalog.Contains(method),
            $"协议原文方法 {method} 未收录进目录");
    }

    public static TheoryData<string> DocMethodNames
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var (method, _) in DocMethods) data.Add(method);
            return data;
        }
    }

    /// <summary>
    /// 品类支持矩阵逐格核对：协议原文共 5 张表（行号 183 / 1277 / 1699 / 2939 / 4913），
    /// 列顺序固定为 4G喇叭 | 4G开关 | WiFi喇叭 | WiFi开关。
    /// 下面按「小节所属分组」展开成 36 × 4 = 144 格逐一断言。
    /// </summary>
    [Theory]
    [MemberData(nameof(SupportMatrixCells))]
    public void Support_Matrix_Should_Match_Doc_Tables(
        string method, AnShengDeviceKind kind, bool expected, int docTableLine)
    {
        var actual = AnShengCommandCatalog.IsSupported(method, kind);
        Assert.True(expected == actual,
            $"{method} × {kind.ToDisplayName()}：协议原文（asopen.md:{docTableLine}）为 " +
            $"{(expected ? "√" : "×")}，实现为 {(actual ? "√" : "×")}");
    }

    public static TheoryData<string, AnShengDeviceKind, bool, int> SupportMatrixCells
    {
        get
        {
            // (分组表行号, 该表 4 列取值, 该组下属方法)
            var groups = new (int DocLine, bool[] Row, string[] Methods)[]
            {
                // asopen.md:183 → | √ | √ | √ | √ |
                (183, new[] { true, true, true, true },
                    new[] { "getDevInfo", "getDevStatus", "connected", "keyEvent", "getKeyConfig",
                            "setKeyConfig", "reboot", "getAutoReport", "setAutoReport" }),
                // asopen.md:1277 → | √ | √ | √ | √ |
                (1277, new[] { true, true, true, true },
                    new[] { "getMqtt", "setMqtt" }),
                // asopen.md:1699 → | × | √ | × | √ |
                (1699, new[] { false, true, false, true },
                    new[] { "action", "actions", "getDelayTasks", "startDelayTask", "stopDelayTask",
                            "delayEvent", "getEMRealtime", "getCalParams", "setCalParams",
                            "resetCalParams", "autoCal" }),
                // asopen.md:2939 → | × | √ | × | × |
                (2939, new[] { false, true, false, false },
                    new[] { "getTimeTasks", "setTimeTasks", "getSlotTimeTasks", "setSlotTimeTasks",
                            "timeEvent", "getEMStatistics", "clearEMStatistics", "getLogs",
                            "send485", "recv485" }),
                // asopen.md:4917 → | √ | √ | × | × |
                (4917, new[] { true, true, false, false },
                    new[] { "setTime", "getSimCheck", "setSimCheck", "simCheck" })
            };

            var kinds = new[]
            {
                AnShengDeviceKind.Speaker4G, AnShengDeviceKind.Switch4G,
                AnShengDeviceKind.SpeakerWiFi, AnShengDeviceKind.SwitchWiFi
            };

            var data = new TheoryData<string, AnShengDeviceKind, bool, int>();
            foreach (var (docLine, row, methods) in groups)
            {
                foreach (var method in methods)
                {
                    for (var i = 0; i < kinds.Length; i++)
                    {
                        data.Add(method, kinds[i], row[i], docLine);
                    }
                }
            }

            return data;
        }
    }

    /// <summary>
    /// 上行事件集合核对。协议中「命令参数：无 / 命令示例：无」的小节才是纯事件：
    /// connected(585) / keyEvent(649) / delayEvent(2251) / timeEvent(4119) / recv485(4835)，
    /// 加上 MQTT 遗嘱 close（asopen.md:27 的 will 报文），共 6 个。
    /// </summary>
    [Fact]
    public void Event_Methods_Should_Be_The_Five_Uplink_Sections_Plus_Close()
    {
        var expected = new[] { "close", "connected", "delayEvent", "keyEvent", "recv485", "timeEvent" };
        var actual = AnShengCommandCatalog.EventMethods.OrderBy(m => m, StringComparer.Ordinal).ToArray();

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// simCheck（asopen.md:5237）有「命令参数 method 是」与「命令示例」，是<b>可下发命令</b>而非事件。
    /// </summary>
    [Fact]
    public void SimCheck_Should_Be_Downlink_Command_Not_Event()
    {
        var spec = AnShengCommandCatalog.Get("simCheck");
        Assert.NotNull(spec);
        Assert.False(spec!.IsEvent, "simCheck 在 asopen.md:5247-5271 有命令参数与命令示例，应为可下发命令");
        Assert.Equal(AnShengCommandDirection.Downlink, spec.Direction);
        Assert.False(AnShengCommandCatalog.IsEvent("simCheck"));
    }

    /// <summary>delayEvent 虽携带 frameId（asopen.md:2291），但仍是上行事件，平台不可下发。</summary>
    [Fact]
    public void DelayEvent_Has_FrameId_But_Is_Still_An_Event()
    {
        var spec = AnShengCommandCatalog.Get("delayEvent");
        Assert.NotNull(spec);
        Assert.True(spec!.IsEvent);
        Assert.Throws<NotSupportedException>(
            () => _builder.BuildCommand(Imei, "delayEvent", null, AnShengDeviceKind.Switch4G));
    }

    /// <summary>
    /// 「测试中」标记核对：协议标题带（测试中）的仅 4 个小节 —
    /// getAutoReport(1007) / setAutoReport(1125) / send485(4733) / recv485(4835)。
    /// </summary>
    [Fact]
    public void Beta_Flag_Should_Match_Doc_Titles()
    {
        var expected = new[] { "getAutoReport", "recv485", "send485", "setAutoReport" };
        var actual = AnShengCommandCatalog.ListAll()
            .Where(s => s.IsBeta)
            .Select(s => s.Method)
            .OrderBy(m => m, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// 必填参数逐条核对（「命令参数」表中「必须 = 是」的行，不含公共字段 method/frameId）。
    /// 期望值抄自协议原文对应表格。
    /// </summary>
    [Theory]
    // getDevInfo(199-209)：无业务参数
    [InlineData("getDevInfo", "")]
    // getDevStatus(305-313)：q 为「否」
    [InlineData("getDevStatus", "")]
    // setKeyConfig(827-837)：mode 是、uploadEnable 是
    [InlineData("setKeyConfig", "mode,uploadEnable")]
    // reboot(931-937)：无
    [InlineData("reboot", "")]
    // setAutoReport(1139-1159)：getDevStatusSec/orderUpSec/rs485Sec/rs485BaudRate 是
    [InlineData("setAutoReport", "getDevStatusSec,orderUpSec,rs485BaudRate,rs485Sec")]
    // setMqtt(1485-1495)：mqttParams 是、reboot 否
    [InlineData("setMqtt", "mqttParams")]
    // action(1719-1731)：slotNum 是、action 是、hasStopDelayTask 否
    [InlineData("action", "action,slotNum")]
    // actions(1825-1837)：slotNums 是、action 是
    [InlineData("actions", "action,slotNums")]
    // startDelayTask(2061-2077)：slotNum/enable/sAction/eAction/secs 全部为「是」
    [InlineData("startDelayTask", "eAction,enable,sAction,secs,slotNum")]
    // stopDelayTask(2169-2177)：slotNum 是
    [InlineData("stopDelayTask", "slotNum")]
    // setCalParams(2593-2601)：calParams 是
    [InlineData("setCalParams", "calParams")]
    // autoCal(2835-2843)：power 是
    [InlineData("autoCal", "power")]
    // setTimeTasks(3211 附近)：tasks 是
    [InlineData("setTimeTasks", "tasks")]
    // getEMStatistics(4265-4273)：q 否
    [InlineData("getEMStatistics", "")]
    // clearEMStatistics(4461-4469)：slotNum 否
    [InlineData("clearEMStatistics", "")]
    // getLogs(4555-4563)：num 否
    [InlineData("getLogs", "")]
    // send485(4747-4759)：dataArray 是（baudRate 否；sendWaitMs 行缺「必须」列，按可选处理）
    [InlineData("send485", "dataArray")]
    // setTime(4939-4947)：timestamp 是
    [InlineData("setTime", "timestamp")]
    // setSimCheck(5137-5149)：enabled/leftDays/dataBalance 全部为「是」
    [InlineData("setSimCheck", "dataBalance,enabled,leftDays")]
    // simCheck(5251-5257)：无业务参数
    [InlineData("simCheck", "")]
    public void Required_Params_Should_Match_Doc_Tables(string method, string expectedCsv)
    {
        var spec = AnShengCommandCatalog.Get(method);
        Assert.NotNull(spec);

        var expected = expectedCsv.Length == 0
            ? Array.Empty<string>()
            : expectedCsv.Split(',');

        var actual = spec!.Params
            .Where(p => p.Required)
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// 可选参数核对：协议中「必须 = 否」的行必须被目录收录，否则调用方无法下发该字段。
    /// </summary>
    [Theory]
    [InlineData("getDevStatus", "q")]                      // asopen.md:311
    [InlineData("action", "hasStopDelayTask")]             // asopen.md:1729
    [InlineData("actions", "hasStopDelayTask")]            // asopen.md:1835
    [InlineData("setMqtt", "reboot")]                      // asopen.md:1493
    [InlineData("setAutoReport", "getDevStatusQ")]         // asopen.md:1147
    [InlineData("setAutoReport", "rs485SendWaitMs")]       // asopen.md:1155
    [InlineData("setAutoReport", "rs485Array")]            // asopen.md:1157
    [InlineData("getEMStatistics", "q")]                   // asopen.md:4271
    [InlineData("clearEMStatistics", "slotNum")]           // asopen.md:4467
    [InlineData("getLogs", "num")]                         // asopen.md:4561
    [InlineData("send485", "baudRate")]                    // asopen.md:4753
    [InlineData("send485", "sendWaitMs")]                  // asopen.md:4755
    [InlineData("setSlotTimeTasks", "loopTimeTasks")]      // asopen.md:3817
    [InlineData("setSlotTimeTasks", "timeTasks")]          // asopen.md:3819
    public void Optional_Params_Should_Be_Present_In_Catalog(string method, string paramName)
    {
        var spec = AnShengCommandCatalog.Get(method);
        Assert.NotNull(spec);
        Assert.True(spec!.Params.Any(p => string.Equals(p.Name, paramName, StringComparison.Ordinal)),
            $"{method} 缺少协议规定的可选参数 {paramName}");
    }

    /// <summary>
    /// 枚举取值核对：action/actions 的 action 仅 on/off/toggle（asopen.md:1727、1833），
    /// startDelayTask 的 sAction 额外允许 none（asopen.md:2071），eAction 不允许 none（asopen.md:2073）。
    /// </summary>
    [Fact]
    public void Action_Enum_Values_Should_Match_Doc()
    {
        var action = AnShengCommandCatalog.Get("action")!.Params.First(p => p.Name == "action");
        Assert.Equal(new[] { "on", "off", "toggle" }, action.AllowedValues);

        var start = AnShengCommandCatalog.Get("startDelayTask")!;
        var sAction = start.Params.First(p => p.Name == "sAction");
        var eAction = start.Params.First(p => p.Name == "eAction");

        Assert.Equal(new[] { "on", "off", "toggle", "none" }, sAction.AllowedValues);
        Assert.Equal(new[] { "on", "off", "toggle" }, eAction.AllowedValues);
        Assert.DoesNotContain("none", eAction.AllowedValues!);
    }

    /// <summary>setKeyConfig 的 mode 取值范围 0-2（asopen.md:833）。</summary>
    [Fact]
    public void SetKeyConfig_Mode_Range_Should_Be_0_To_2()
    {
        var spec = AnShengCommandCatalog.Get("setKeyConfig")!;
        var mode = spec.Params.First(p => p.Name == "mode");

        Assert.Equal(0d, mode.Minimum);
        Assert.Equal(2d, mode.Maximum);

        Assert.True(spec.ValidateParams(Params(("mode", 2), ("uploadEnable", true))).IsValid);
        Assert.False(spec.ValidateParams(Params(("mode", 3), ("uploadEnable", true))).IsValid);
    }

    // ─────────────────────────────────────────────────────────────
    // B. 下行命令回放：协议原文「命令示例」 vs Builder 输出
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 用协议原文的命令示例逐字段比对 Builder 输出。
    /// 动态字段（frameId / timestamp）与 imei 不参与比对：
    ///   - frameId 由平台生成；
    ///   - timestamp 为 4G 款注入的当前时钟；
    ///   - 原文示例未带 imei，实际下发需带（asopen.md:44「设备发布的应答中，都会带有设备实际的imei」）。
    /// </summary>
    [Theory]
    // asopen.md:217-223
    [InlineData("getDevInfo", "{\"method\":\"getDevInfo\",\"frameId\":\"1745396239780\"}")]
    // asopen.md:321-327
    [InlineData("getDevStatus", "{\"method\":\"getDevStatus\",\"frameId\":\"1745396239780\"}")]
    // asopen.md:743-749
    [InlineData("getKeyConfig", "{\"method\":\"getKeyConfig\",\"frameId\":\"1745396239780\"}")]
    // asopen.md:945-951
    [InlineData("reboot", "{\"method\":\"reboot\",\"frameId\":\"1745396239780\"}")]
    // asopen.md:1035-1041
    [InlineData("getAutoReport", "{\"method\":\"getAutoReport\",\"frameId\":\"1745396239780\"}")]
    // asopen.md:1313-1319
    [InlineData("getMqtt", "{\"method\":\"getMqtt\",\"frameId\":\"1745396239780\"}")]
    // asopen.md:1945-1951
    [InlineData("getDelayTasks", "{\"method\":\"getDelayTasks\",\"frameId\":\"1745396239780\"}")]
    // asopen.md:2359-2365
    [InlineData("getEMRealtime", "{\"method\":\"getEMRealtime\",\"frameId\":\"1745396239780\"}")]
    // asopen.md:2497-2503
    [InlineData("getCalParams", "{\"method\":\"getCalParams\",\"frameId\":\"1745396239780\"}")]
    // asopen.md:2737-2743
    [InlineData("resetCalParams", "{\"method\":\"resetCalParams\",\"frameId\":\"1745396239780\"}")]
    // asopen.md:2987-2993
    [InlineData("getTimeTasks", "{\"method\":\"getTimeTasks\",\"frameId\":\"1745396239780\"}")]
    // asopen.md:5049-5055
    [InlineData("getSimCheck", "{\"method\":\"getSimCheck\",\"frameId\":\"1745396239780\"}")]
    // asopen.md:5265-5271
    [InlineData("simCheck", "{\"method\":\"simCheck\",\"frameId\":\"1745456483900\"}")]
    public void Parameterless_Commands_Should_Match_Doc_Examples(string method, string docExample)
    {
        var expected = FieldsOf(docExample);
        var (_, payload) = _builder.BuildCommand(Imei, method, null, AnShengDeviceKind.Switch4G);
        var actual = FieldsOf(payload);

        AssertBusinessFieldsEqual(expected, actual, method);
    }

    /// <summary>action 命令回放（asopen.md:1739-1751）。</summary>
    [Fact]
    public void Action_Command_Should_Match_Doc_Example()
    {
        const string doc = """
                           {
                             "method": "action",
                             "slotNum": 1,
                             "action": "on",
                             "hasStopDelayTask": false,
                             "frameId": "1745396239780"
                           }
                           """;

        var (_, payload) = _builder.BuildAction(Imei, 1, "on", false, AnShengDeviceKind.Switch4G);
        AssertBusinessFieldsEqual(FieldsOf(doc), FieldsOf(payload), "action");
    }

    /// <summary>actions 命令回放（asopen.md:1845-1857），slotNums 为数组。</summary>
    [Fact]
    public void Actions_Command_Should_Match_Doc_Example()
    {
        const string doc = """
                           {
                             "method": "actions",
                             "slotNums": [1,3,4],
                             "action": "on",
                             "hasStopDelayTask": false,
                             "frameId": "1745396239780"
                           }
                           """;

        var (_, payload) = _builder.BuildActions(Imei, new[] { 1, 3, 4 }, "on", false, AnShengDeviceKind.Switch4G);
        AssertBusinessFieldsEqual(FieldsOf(doc), FieldsOf(payload), "actions");
    }

    /// <summary>
    /// startDelayTask 命令回放（asopen.md:2085-2099）。
    /// 注意原文示例未带 enable，但参数表（asopen.md:2069）标注 enable 为必填 —— 以参数表为准。
    /// </summary>
    [Fact]
    public void StartDelayTask_Command_Should_Match_Doc_Example()
    {
        const string doc = """
                           {
                             "method": "startDelayTask",
                             "slotNum": 1,
                             "sAction": "none",
                             "secs": 100,
                             "eAction": "toggle",
                             "frameId": "1745396239780"
                           }
                           """;

        var (_, payload) = _builder.BuildStartDelayTask(
            Imei, 1, true, "none", "toggle", 100, AnShengDeviceKind.Switch4G);

        var expected = FieldsOf(doc);
        var actual = FieldsOf(payload);

        // 原文示例中出现的每个业务字段都必须一致
        AssertBusinessFieldsSubset(expected, actual, "startDelayTask");
        // 参数表要求的 enable 必须存在
        Assert.True(actual.ContainsKey("enable"), "startDelayTask 缺少必填参数 enable（asopen.md:2069）");
    }

    /// <summary>setKeyConfig 命令回放（asopen.md:845-855）。</summary>
    [Fact]
    public void SetKeyConfig_Command_Should_Match_Doc_Example()
    {
        const string doc = """
                           {
                             "method": "setKeyConfig",
                             "mode": 1,
                             "uploadEnable": true,
                             "frameId": "1745396239780"
                           }
                           """;

        var (_, payload) = _builder.BuildSetKeyConfig(Imei, 1, true, AnShengDeviceKind.Switch4G);
        AssertBusinessFieldsEqual(FieldsOf(doc), FieldsOf(payload), "setKeyConfig");
    }

    /// <summary>
    /// setTime 命令回放（asopen.md:4955-4963）：timestamp 是<b>业务参数</b>，
    /// 必须等于调用方指定的目标时间，不得被当前时钟覆盖。
    /// </summary>
    [Fact]
    public void SetTime_Command_Should_Carry_Caller_Timestamp_Not_Current_Clock()
    {
        // 原文示例 timestamp = 1745456483
        const long docTimestamp = 1745456483;
        var target = DateTimeOffset.FromUnixTimeSeconds(docTimestamp).UtcDateTime;

        var (_, payload) = _builder.BuildSetTime(Imei, target, AnShengDeviceKind.Switch4G);
        var fields = FieldsOf(payload);

        Assert.Equal(JsonValueKind.Number, fields["timestamp"].ValueKind);
        Assert.Equal(docTimestamp, fields["timestamp"].GetInt64());
    }

    /// <summary>setAutoReport 命令回放（asopen.md:1167-1187）。</summary>
    [Fact]
    public void SetAutoReport_Command_Should_Match_Doc_Example()
    {
        const string doc = """
                           {
                             "method": "setAutoReport",
                             "getDevStatusSec": 600,
                             "getDevStatusQ": "slots,EMdata",
                             "orderUpSec": 0,
                             "rs485Sec": 200,
                             "rs485BaudRate": 115200,
                             "rs485SendWaitMs": 300,
                             "rs485Array": ["3837313131","3a4d558921"],
                             "frameId": "1745396239780"
                           }
                           """;

        var (_, payload) = _builder.BuildSetAutoReport(
            Imei,
            getDevStatusSec: 600,
            orderUpSec: 0,
            rs485Sec: 200,
            rs485BaudRate: 115200,
            getDevStatusQ: "slots,EMdata",
            rs485SendWaitMs: 300,
            rs485Array: new[] { "3837313131", "3a4d558921" },
            kind: AnShengDeviceKind.Switch4G);

        AssertBusinessFieldsEqual(FieldsOf(doc), FieldsOf(payload), "setAutoReport");
    }

    /// <summary>
    /// 通用 BuildCommand 通道回放：协议原文带业务参数的命令示例。
    /// </summary>
    [Theory]
    // asopen.md:2185-2191
    [InlineData("stopDelayTask", "{\"method\":\"stopDelayTask\",\"slotNum\":1,\"frameId\":\"x\"}")]
    // asopen.md:2851-2859
    [InlineData("autoCal", "{\"method\":\"autoCal\",\"power\":500,\"frameId\":\"x\"}")]
    // asopen.md:4281-4289
    [InlineData("getEMStatistics", "{\"method\":\"getEMStatistics\",\"q\":\"all\",\"frameId\":\"x\"}")]
    // asopen.md:4477-4485
    [InlineData("clearEMStatistics", "{\"method\":\"clearEMStatistics\",\"slotNum\":1,\"frameId\":\"x\"}")]
    // asopen.md:4571-4579
    [InlineData("getLogs", "{\"method\":\"getLogs\",\"num\":10,\"frameId\":\"x\"}")]
    // asopen.md:5157-5167
    [InlineData("setSimCheck",
        "{\"method\":\"setSimCheck\",\"enabled\":true,\"leftDays\":0,\"dataBalance\":0,\"frameId\":\"x\"}")]
    public void Doc_Example_Params_Should_Round_Trip_Through_BuildCommand(string method, string docExample)
    {
        var expected = FieldsOf(docExample);

        // 把原文示例的业务字段原样喂回 Builder
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in expected)
        {
            if (key is "method" or "frameId" or "imei" or "timestamp") continue;
            parameters[key] = value;
        }

        var (_, payload) = _builder.BuildCommand(Imei, method, parameters, AnShengDeviceKind.Switch4G);
        AssertBusinessFieldsEqual(expected, FieldsOf(payload), method);
    }

    /// <summary>send485 命令回放（asopen.md:4767-4779），dataArray 为十六进制字符串数组。</summary>
    [Fact]
    public void Send485_Command_Should_Match_Doc_Example()
    {
        const string doc = """
                           {
                             "method": "send485",
                             "baudRate": 115200,
                             "sendWaitMs": 300,
                             "dataArray": ["343830303133","234345287283"],
                             "frameId": "1745396239780"
                           }
                           """;

        var expected = FieldsOf(doc);
        var (_, payload) = _builder.BuildCommand(Imei, "send485", new Dictionary<string, object?>
        {
            ["baudRate"] = 115200,
            ["sendWaitMs"] = 300,
            ["dataArray"] = new[] { "343830303133", "234345287283" }
        }, AnShengDeviceKind.Switch4G);

        AssertBusinessFieldsEqual(expected, FieldsOf(payload), "send485");
    }

    /// <summary>setCalParams 命令回放（asopen.md:2621-2633），calParams 为嵌套对象。</summary>
    [Fact]
    public void SetCalParams_Command_Should_Match_Doc_Example()
    {
        const string doc = """
                           {
                             "method": "setCalParams",
                             "calParams": { "RL": 0.24 },
                             "frameId": "1745396239780"
                           }
                           """;

        var expected = FieldsOf(doc);
        var (_, payload) = _builder.BuildCommand(Imei, "setCalParams", new Dictionary<string, object?>
        {
            ["calParams"] = new Dictionary<string, object?> { ["RL"] = 0.24 }
        }, AnShengDeviceKind.Switch4G);

        AssertBusinessFieldsEqual(expected, FieldsOf(payload), "setCalParams");
    }

    // ─────────────────────────────────────────────────────────────
    // C. 上行应答/事件回放：协议原文「应答示例」 → Parser
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// getDevStatus 完整应答回放（asopen.md:473-571）。
    /// 该样例是<b>真实设备抓包</b>：temperature/voltage/current/power/totalKwh 均以<b>字符串</b>上报，
    /// 与参数表声明的 float/double 不一致，解析器必须能宽松兼容。
    /// </summary>
    [Fact]
    public void GetDevStatus_Doc_Response_Should_Parse_All_Fields()
    {
        const string doc = """
                           {
                             "imei": "864536072949900",
                             "gps": "113.2170916,023.4001628",
                             "method": "getDevStatus",
                             "timestamp": 1745398605,
                             "signal": 25,
                             "result": "ok",
                             "model": "Air780E",
                             "EMdata": [
                               { "c": 0.067, "v": 239.0090179, "p": 2.9530001, "e": 0 }
                             ],
                             "slots": [ 0 ],
                             "netType": "4G",
                             "iccid": "898608481024C0310590",
                             "tasks": [
                               {
                                 "chargeFullStop": false,
                                 "pullOutStopStartSec": 0,
                                 "timeSec": 23596,
                                 "voltage": "239.009",
                                 "slotNum": 1,
                                 "totalKwh": "0.000",
                                 "pullOutStop": false,
                                 "pullOutStopPower": 5,
                                 "chargeFullStopPower": 10,
                                 "remark": "QQO20250417092634",
                                 "chargeFullStopStartSec": 0,
                                 "closeReason": "CLOSED",
                                 "current": "0.067",
                                 "type": "TIME",
                                 "power": "2.953",
                                 "status": "idle",
                                 "chargeFullStopSec": 60,
                                 "maxPower": 2000,
                                 "totalSec": 23596,
                                 "ps": [1.8430001,1.3590001,1.868],
                                 "cs": [0.045,0.019,0.026],
                                 "vs": [237.0470123,235.8450165,237.0630188]
                               }
                             ],
                             "temperature": "32.4",
                             "frameId": "1745398603262"
                           }
                           """;

        var message = _parser.Parse(doc);
        Assert.NotNull(message);
        Assert.Equal("getDevStatus", message!.Method);
        Assert.Equal("864536072949900", message.Imei);
        Assert.Equal("1745398603262", message.FrameId);
        Assert.True(message.IsOk);
        Assert.Equal(1745398605L, message.RawTimestamp);

        var status = _parser.ParseDevStatus(message);
        Assert.NotNull(status);

        // 顶层字段
        Assert.Equal("4G", status!.NetType);
        Assert.Equal("898608481024C0310590", status.Iccid);
        Assert.Equal("113.2170916,023.4001628", status.Gps);
        Assert.Equal(25, status.Signal);
        Assert.Equal("Air780E", status.Model);
        // 字符串形态温度必须被兼容
        Assert.NotNull(status.Temperature);
        Assert.Equal(32.4, status.Temperature!.Value, 3);

        // slots
        Assert.NotNull(status.Slots);
        Assert.Single(status.Slots!);
        Assert.Equal(0, status.Slots![0]);
        Assert.Equal(1, status.SlotCount);

        // EMdata
        Assert.NotNull(status.EmData);
        var em = Assert.Single(status.EmData!);
        Assert.Equal(0.067, em.C!.Value, 4);
        Assert.Equal(239.0090179, em.V!.Value, 6);
        Assert.Equal(2.9530001, em.P!.Value, 6);
        Assert.Equal(0, em.E!.Value, 6);

        // tasks —— 字符串数值必须被宽松解析为 double
        Assert.NotNull(status.Tasks);
        var task = Assert.Single(status.Tasks!);
        Assert.Equal(1, task.SlotNum);
        Assert.Equal("TIME", task.Type);
        Assert.Equal("idle", task.Status);
        Assert.False(task.IsWorking);
        Assert.Equal(23596, task.TimeSec);
        Assert.Equal(23596, task.TotalSec);
        Assert.Equal(239.009, task.Voltage!.Value, 3);
        Assert.Equal(0.067, task.Current!.Value, 3);
        Assert.Equal(2.953, task.Power!.Value, 3);
        Assert.Equal(0.0, task.TotalKwh!.Value, 3);
        Assert.Equal("CLOSED", task.CloseReason);
        Assert.Equal("QQO20250417092634", task.Remark);
        Assert.Equal(2000, task.MaxPower);
        Assert.Equal(60, task.ChargeFullStopSec);
        Assert.Equal(10, task.ChargeFullStopPower);
        Assert.Equal(5, task.PullOutStopPower);
        Assert.False(task.ChargeFullStop);
        Assert.False(task.PullOutStop);

        // 多相电数组
        Assert.Equal(3, task.Ps!.Count);
        Assert.Equal(3, task.Cs!.Count);
        Assert.Equal(3, task.Vs!.Count);
        Assert.Equal(237.0470123, task.Vs![0], 6);
    }

    /// <summary>
    /// 文档参数表（asopen.md:401）写作 <c>chageFullStopSec</c>（缺 r），
    /// 应答示例（asopen.md:551）写作 <c>chargeFullStopSec</c>。两种拼写都必须能取到值。
    /// </summary>
    [Theory]
    [InlineData("chargeFullStopSec")] // 示例拼写
    [InlineData("chageFullStopSec")]  // 参数表拼写
    public void ChargeFullStopSec_Both_Spellings_Should_Be_Readable(string fieldName)
    {
        var json = $$"""
                     {
                       "method": "getDevStatus",
                       "imei": "864536072949900",
                       "result": "ok",
                       "tasks": [ { "slotNum": 1, "{{fieldName}}": 60 } ]
                     }
                     """;

        var message = _parser.Parse(json);
        Assert.NotNull(message);
        var status = _parser.ParseDevStatus(message!);
        var task = Assert.Single(status!.Tasks!);

        var effective = task.ChargeFullStopSec ?? task.ChargeFullStopSecTypo;
        Assert.Equal(60, effective);
    }

    /// <summary>getDevInfo 应答回放（asopen.md:259-277）。</summary>
    [Fact]
    public void GetDevInfo_Doc_Response_Should_Parse()
    {
        const string doc = """
                           {
                             "method": "getDevInfo",
                             "result": "ok",
                             "version": "SWITCH-EC618X-R24-O-V4.0.8",
                             "slotAmount": 1,
                             "phaseAmount": 1,
                             "imei": "1745396239780",
                             "frameId": "1745396239780",
                             "timestamp": 1745396759
                           }
                           """;

        var message = _parser.Parse(doc);
        Assert.NotNull(message);

        var info = _parser.ParseDevInfo(message!);
        Assert.NotNull(info);
        Assert.Equal("SWITCH-EC618X-R24-O-V4.0.8", info!.Version);
        Assert.Equal(1, info.SlotAmount);
        Assert.Equal(1, info.PhaseAmount);

        // 该版本号可推断出品类：4G 开关
        var kind = AnShengDeviceKindResolver.Resolve("4G", info.Version, "Air780E");
        Assert.Equal(AnShengDeviceKind.Switch4G, kind);
    }

    /// <summary>getEMRealtime 应答回放（asopen.md:2421-2455），数据在 <c>data</c> 数组内。</summary>
    [Fact]
    public void GetEMRealtime_Doc_Response_Should_Parse()
    {
        const string doc = """
                           {
                             "method": "getEMRealtime",
                             "result": "ok",
                             "data": [
                               {
                                 "v": 237.1000061,
                                 "vs": [237.3490143,236.4700165,237.4820099],
                                 "c": 0.091,
                                 "cs": [0.046,0.019,0.026],
                                 "p": 4.263,
                                 "ps": [1.784,1.064,1.4150001],
                                 "e":0
                               }
                             ],
                             "imei": "1745396239780",
                             "frameId": "1745396239780",
                             "timestamp": 1745396759
                           }
                           """;

        var message = _parser.Parse(doc);
        Assert.NotNull(message);
        Assert.Equal("getEMRealtime", message!.Method);
        Assert.True(message.IsOk);
        Assert.Equal(AnShengMessageCategory.CommandResponse, _parser.GetCategory(message));

        // data 数组当前无强类型模型，验证标准化输出至少完整保留原始数据
        var normalized = _parser.NormalizeForSensorData(message, "/iot/server/iot-board/1745396239780");
        using var document = JsonDocument.Parse(normalized);
        Assert.True(document.RootElement.TryGetProperty("data", out var data),
            "getEMRealtime 的 data 数组在标准化输出中丢失");
        Assert.Equal(1, data.GetArrayLength());
        Assert.Equal(4.263, data[0].GetProperty("p").GetDouble(), 3);
    }

    /// <summary>connected 事件回放（asopen.md:627-635）：无 frameId、无 result。</summary>
    [Fact]
    public void Connected_Event_Doc_Response_Should_Parse()
    {
        const string doc = """
                           {
                             "method": "connected",
                             "imei": "1745396239780",
                             "timestamp": 1745396759
                           }
                           """;

        var message = _parser.Parse(doc);
        Assert.NotNull(message);
        Assert.Equal("connected", message!.Method);
        Assert.Equal("1745396239780", message.Imei);
        Assert.Null(message.FrameId);
        Assert.True(message.IsEvent);
        Assert.Equal(AnShengMessageCategory.Event, _parser.GetCategory(message));
        Assert.False(AnShengMessageParser.IsWillMessage(message));
    }

    /// <summary>keyEvent 事件回放（asopen.md:691-699）。</summary>
    [Fact]
    public void KeyEvent_Doc_Response_Should_Parse()
    {
        const string doc = """
                           {
                             "method": "keyEvent",
                             "imei": "1745396239780",
                             "timestamp": 1745396759
                           }
                           """;

        var message = _parser.Parse(doc);
        Assert.NotNull(message);
        Assert.Equal(AnShengMessageCategory.Event, _parser.GetCategory(message!));
        Assert.Equal(1745396759L, message!.RawTimestamp);
        Assert.NotNull(message.TimestampUtc);
        // 秒级时间戳必须被识别为 2025-04-23，而非 1970 年（毫秒误判）
        Assert.Equal(2025, message.TimestampUtc!.Value.Year);
    }

    /// <summary>delayEvent 事件回放（asopen.md:2301-2317）：带 slotNum / slots / frameId。</summary>
    [Fact]
    public void DelayEvent_Doc_Response_Should_Parse()
    {
        const string doc = """
                           {
                             "method": "delayEvent",
                             "result": "ok",
                             "slotNum": 1,
                             "slots": [0],
                             "imei": "1745396239780",
                             "frameId": "1745396239780",
                             "timestamp": 1745396759
                           }
                           """;

        var message = _parser.Parse(doc);
        Assert.NotNull(message);
        Assert.Equal(AnShengMessageCategory.Event, _parser.GetCategory(message!));
        Assert.Equal("1745396239780", message!.FrameId);

        // slotNum / slots 必须能从平铺报文取出
        using var document = JsonDocument.Parse(message.RawJson);
        Assert.Equal(1, document.RootElement.GetProperty("slotNum").GetInt32());
        Assert.Equal(1, document.RootElement.GetProperty("slots").GetArrayLength());
    }

    /// <summary>timeEvent 事件回放（asopen.md:4193-4237）：无 frameId，带 taskIndex 与 task 对象。</summary>
    [Fact]
    public void TimeEvent_Doc_Response_Should_Parse()
    {
        const string doc = """
                           {
                               "taskIndex": 1,
                               "timestamp": 1779346021,
                               "task": {
                                   "minute": 47,
                                   "enable": true,
                                   "uploadEnable": true,
                                   "id": "1779345917718",
                                   "weekDays": [1,4,5],
                                   "action": "toggle",
                                   "hour": 14
                               },
                               "slots": [1],
                               "imei": "863434084747622",
                               "slotNum": 1,
                               "method": "timeEvent"
                           }
                           """;

        var message = _parser.Parse(doc);
        Assert.NotNull(message);
        Assert.Equal("timeEvent", message!.Method);
        Assert.Equal("863434084747622", message.Imei);
        Assert.Null(message.FrameId);
        Assert.Equal(AnShengMessageCategory.Event, _parser.GetCategory(message));

        using var document = JsonDocument.Parse(message.RawJson);
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("taskIndex").GetInt32());
        Assert.Equal("toggle", root.GetProperty("task").GetProperty("action").GetString());
        Assert.True(root.GetProperty("task").GetProperty("uploadEnable").GetBoolean());
    }

    /// <summary>getKeyConfig 应答回放（asopen.md:781-797）。</summary>
    [Fact]
    public void GetKeyConfig_Doc_Response_Should_Parse()
    {
        const string doc = """
                           {
                             "method": "getKeyConfig",
                             "result": "ok",
                             "mode": 1,
                             "uploadEnable": true,
                             "imei": "1745396239780",
                             "frameId": "1745396239780",
                             "timestamp": 1745396759
                           }
                           """;

        var message = _parser.Parse(doc);
        Assert.NotNull(message);
        Assert.True(message!.IsOk);
        Assert.Equal(AnShengMessageCategory.CommandResponse, _parser.GetCategory(message));

        using var document = JsonDocument.Parse(message.RawJson);
        Assert.Equal(1, document.RootElement.GetProperty("mode").GetInt32());
        Assert.True(document.RootElement.GetProperty("uploadEnable").GetBoolean());
    }

    /// <summary>getDelayTasks 应答回放（asopen.md:2003-2033）。</summary>
    [Fact]
    public void GetDelayTasks_Doc_Response_Should_Parse()
    {
        const string doc = """
                           {
                             "method": "getDelayTasks",
                             "result": "ok",
                             "tasks": [
                               { "cnt": 7, "eAction": "toggle", "sAction": "none", "secs": 100, "enable": true }
                             ],
                             "imei": "1745396239780",
                             "frameId": "1745396239780",
                             "timestamp": 1745396759
                           }
                           """;

        var message = _parser.Parse(doc);
        Assert.NotNull(message);

        using var document = JsonDocument.Parse(message!.RawJson);
        var task = document.RootElement.GetProperty("tasks")[0];
        Assert.Equal(7, task.GetProperty("cnt").GetInt32());
        Assert.Equal("none", task.GetProperty("sAction").GetString());
        Assert.Equal(100, task.GetProperty("secs").GetInt32());
    }

    /// <summary>
    /// recv485 事件回放（asopen.md:4881-4897），以及协议注明的
    /// 「自动上报时 frameId 为空」（asopen.md:4869）两种形态。
    /// </summary>
    [Theory]
    [InlineData("\"frameId\": \"1745396239780\",", "1745396239780")]
    [InlineData("\"frameId\": \"\",", "")]
    [InlineData("", null)]
    public void Recv485_Doc_Response_Should_Parse_With_Or_Without_FrameId(
        string frameIdFragment, string? expectedFrameId)
    {
        var doc = $$"""
                    {
                      "method": "recv485",
                      "result": "ok",
                      "data": "343830303133",
                      "imei": "1745396239780",
                      "num": 1,
                      {{frameIdFragment}}
                      "timestamp": 1745396759
                    }
                    """;

        var message = _parser.Parse(doc);
        Assert.NotNull(message);
        Assert.Equal("recv485", message!.Method);
        Assert.Equal(expectedFrameId, message.FrameId);
        Assert.Equal(AnShengMessageCategory.Event, _parser.GetCategory(message));
    }

    /// <summary>
    /// MQTT 遗嘱回放：原文配置示例中的 will 内容（asopen.md:27 / 1421）为
    /// <c>{"imei":"%imei%","method":"close"}</c> 与 <c>{"method":"close","imei":"%imei%"}</c>，
    /// 字段顺序不同都必须判为离线。
    /// </summary>
    [Theory]
    [InlineData("{\"imei\":\"864536072949900\",\"method\": \"close\"}")]
    [InlineData("{\"method\":\"close\",\"imei\":\"864536072949900\"}")]
    public void Will_Doc_Payloads_Should_Be_Detected_As_Offline(string willPayload)
    {
        var message = _parser.Parse(willPayload);
        Assert.NotNull(message);
        Assert.True(AnShengMessageParser.IsWillMessage(message));
        Assert.Equal(AnShengMessageCategory.Close, _parser.GetCategory(message!));
        Assert.Equal("864536072949900", message!.Imei);
        // 遗嘱报文无 timestamp，不得因此解析失败
        Assert.Null(message.RawTimestamp);
    }

    /// <summary>
    /// 现网配置中 PublishTopicPattern 与 WillTopicPattern 均为 /iot/server/iot-board/+，
    /// 因此离线判定不得依赖主题：同一主题下 close 判离线、getDevStatus 不判离线。
    /// </summary>
    [Fact]
    public void Will_Detection_Must_Not_Depend_On_Topic_Under_Shared_Pattern()
    {
        const string topic = "/iot/server/iot-board/864536072949900";

        var will = _parser.Parse("{\"imei\":\"864536072949900\",\"method\":\"close\"}");
        var data = _parser.Parse("{\"imei\":\"864536072949900\",\"method\":\"getDevStatus\",\"result\":\"ok\"}");

        Assert.True(AnShengMessageParser.IsWillMessage(will));
        Assert.False(AnShengMessageParser.IsWillMessage(data));
        Assert.Equal("864536072949900", AnShengMessageParser.ExtractImeiFromTopic(topic));
    }

    // ─────────────────────────────────────────────────────────────
    // D. 边界与错误路径
    // ─────────────────────────────────────────────────────────────

    /// <summary>向不支持的品类下发命令必须被拒绝，且错误信息包含品类名与方法名。</summary>
    [Theory]
    [InlineData("action", AnShengDeviceKind.SpeakerWiFi)]      // G3 表 asopen.md:1699 WiFi喇叭 ×
    [InlineData("action", AnShengDeviceKind.Speaker4G)]        // G3 表 4G喇叭 ×
    [InlineData("getTimeTasks", AnShengDeviceKind.SwitchWiFi)] // G4 表 asopen.md:2939 WiFi开关 ×
    [InlineData("setTime", AnShengDeviceKind.SwitchWiFi)]      // G5 表 asopen.md:4917 WiFi开关 ×
    [InlineData("simCheck", AnShengDeviceKind.SpeakerWiFi)]    // G5 表 WiFi喇叭 ×
    public void Unsupported_Kind_Should_Be_Rejected_With_Readable_Message(
        string method, AnShengDeviceKind kind)
    {
        var ex = Assert.Throws<NotSupportedException>(
            () => _builder.BuildCommand(Imei, method, null, kind));

        Assert.Contains(method, ex.Message, StringComparison.Ordinal);
        Assert.Contains(kind.ToDisplayName(), ex.Message, StringComparison.Ordinal);
    }

    /// <summary>未知 method 必须被拒绝（防止下发协议外的伪命令）。</summary>
    [Theory]
    [InlineData("openDoor")]
    [InlineData("getdevstatus")]  // 大小写错误
    [InlineData("getDevstatus")]
    [InlineData("chargeStart")]
    [InlineData("")]
    public void Unknown_Or_Miscased_Method_Should_Be_Rejected(string method)
    {
        Assert.False(AnShengCommandCatalog.Contains(method));
        Assert.Throws<ArgumentException>(
            () => _builder.BuildCommand(Imei, method, null, AnShengDeviceKind.Switch4G));
    }

    /// <summary>空 IMEI 必须被拒绝。</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_Imei_Should_Be_Rejected(string imei)
    {
        Assert.Throws<ArgumentException>(
            () => _builder.BuildCommand(imei, "getDevInfo", null, AnShengDeviceKind.Switch4G));
    }

    /// <summary>必填参数缺失必须被校验拦截（逐条覆盖有必填项的命令）。</summary>
    [Theory]
    [InlineData("action", "slotNum")]
    [InlineData("action", "action")]
    [InlineData("actions", "slotNums")]
    [InlineData("startDelayTask", "secs")]
    [InlineData("startDelayTask", "eAction")]
    [InlineData("stopDelayTask", "slotNum")]
    [InlineData("setKeyConfig", "mode")]
    [InlineData("setKeyConfig", "uploadEnable")]
    [InlineData("setTime", "timestamp")]
    [InlineData("autoCal", "power")]
    [InlineData("setCalParams", "calParams")]
    [InlineData("setMqtt", "mqttParams")]
    [InlineData("setSimCheck", "enabled")]
    [InlineData("setSimCheck", "leftDays")]
    [InlineData("setSimCheck", "dataBalance")]
    [InlineData("send485", "dataArray")]
    [InlineData("setTimeTasks", "tasks")]
    public void Missing_Required_Param_Should_Fail_Validation(string method, string missingParam)
    {
        var spec = AnShengCommandCatalog.Get(method);
        Assert.NotNull(spec);

        var parameters = BuildValidParams(spec!);
        parameters.Remove(missingParam);

        var result = spec!.ValidateParams(parameters);
        Assert.False(result.IsValid, $"{method} 缺少必填参数 {missingParam} 时应校验失败");
        Assert.Contains(result.Errors, e => e.Contains(missingParam, StringComparison.Ordinal));
    }

    /// <summary>完整必填参数应通过校验（避免上一个测试因规格过严而误判通过）。</summary>
    [Theory]
    [InlineData("action")]
    [InlineData("actions")]
    [InlineData("startDelayTask")]
    [InlineData("stopDelayTask")]
    [InlineData("setKeyConfig")]
    [InlineData("setTime")]
    [InlineData("autoCal")]
    [InlineData("setCalParams")]
    [InlineData("setMqtt")]
    [InlineData("setSimCheck")]
    [InlineData("send485")]
    [InlineData("setTimeTasks")]
    public void Complete_Required_Params_Should_Pass_Validation(string method)
    {
        var spec = AnShengCommandCatalog.Get(method)!;
        var result = spec.ValidateParams(BuildValidParams(spec));
        Assert.True(result.IsValid, $"{method} 参数齐备却校验失败：{result}");
    }

    /// <summary>
    /// Object 型参数：生产路径（System.Text.Json 绑定 → JsonElement）必须通过校验。
    /// 这是 setMqtt.mqttParams 的真实到达形态。
    /// </summary>
    [Fact]
    public void Object_Param_As_JsonElement_Should_Pass_Validation()
    {
        var spec = AnShengCommandCatalog.Get("setMqtt")!;
        var objectParam = spec.Params.FirstOrDefault(p => p.Type == AnShengParamType.Object);
        Assert.NotNull(objectParam);

        var parameters = BuildValidParams(spec);
        parameters[objectParam!.Name] = JsonObjectElement("{\"host\":\"1.2.3.4\",\"port\":1883}");

        var result = spec.ValidateParams(parameters);
        Assert.True(result.IsValid, $"setMqtt.{objectParam.Name} 传 JsonElement(Object) 应通过：{result}");
    }

    /// <summary>
    /// 【已知健壮性缺口·P2】Object 型参数若由内部调用方直接传 Dictionary，
    /// 会被 AnShengParamSpec.MatchesType 的 `value is not IEnumerable` 判据误杀
    /// （Dictionary 实现了 IEnumerable）。
    /// HTTP 入口因 STJ 绑定为 JsonElement 而不受影响，故定级 P2 而非 P0。
    /// 本测试固化当前行为：一旦源码修复（改为接受 IDictionary），此测试会失败并提醒更新。
    /// </summary>
    [Fact]
    public void Object_Param_As_Dictionary_Is_Currently_Rejected_KnownGap()
    {
        var spec = AnShengCommandCatalog.Get("setMqtt")!;
        var objectParam = spec.Params.First(p => p.Type == AnShengParamType.Object);

        var parameters = BuildValidParams(spec);
        parameters[objectParam.Name] = new Dictionary<string, object?> { ["host"] = "1.2.3.4" };

        var result = spec.ValidateParams(parameters);
        Assert.False(
            result.IsValid,
            "若此断言失败，说明源码已支持 Dictionary 型 Object 参数（P2 缺口已修复），请同步更新本测试");
    }

    /// <summary>参数类型错误必须被拦截。</summary>
    [Theory]
    [InlineData("action", "slotNum", "not-a-number")]
    [InlineData("action", "action", 1)]
    [InlineData("actions", "slotNums", "1,2,3")]
    [InlineData("setKeyConfig", "uploadEnable", "true")]
    [InlineData("setKeyConfig", "mode", "1")]
    [InlineData("setTime", "timestamp", "1745456483")]
    public void Wrong_Param_Type_Should_Fail_Validation(string method, string param, object wrongValue)
    {
        var spec = AnShengCommandCatalog.Get(method)!;
        var parameters = BuildValidParams(spec);
        parameters[param] = wrongValue;

        var result = spec.ValidateParams(parameters);
        Assert.False(result.IsValid, $"{method}.{param} 类型错误应校验失败");
    }

    /// <summary>枚举取值非法必须被拦截（协议仅允许 on/off/toggle）。</summary>
    [Theory]
    [InlineData("ON")]
    [InlineData("On")]
    [InlineData("open")]
    [InlineData("none")]   // none 仅 sAction 允许
    [InlineData("")]
    public void Invalid_Action_Value_Should_Fail_Validation(string action)
    {
        var spec = AnShengCommandCatalog.Get("action")!;
        var result = spec.ValidateParams(Params(("slotNum", 1), ("action", action)));
        Assert.False(result.IsValid, $"action=\"{action}\" 不在协议枚举内，应校验失败");
    }

    /// <summary>slotNum 下界：协议规定从 1 开始，0 表示所有插槽，负数非法（asopen.md:1725）。</summary>
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(-1, false)]
    public void SlotNum_Lower_Bound_Should_Follow_Doc(int slotNum, bool expectValid)
    {
        var spec = AnShengCommandCatalog.Get("action")!;
        var result = spec.ValidateParams(Params(("slotNum", slotNum), ("action", "on")));
        Assert.Equal(expectValid, result.IsValid);
    }

    /// <summary>
    /// 【已知缺口 · 文档化】slotNum 上界与设备 slotAmount 的关系未被校验。
    /// 协议未给出上限，但设备 getDevInfo 会上报 slotAmount（asopen.md:243）；
    /// 当前规格层无设备上下文，越界值会被原样下发，由设备侧拒绝。
    /// </summary>
    [Fact]
    public void SlotNum_Upper_Bound_Against_SlotAmount_Is_Not_Validated_KnownGap()
    {
        var spec = AnShengCommandCatalog.Get("action")!;
        var result = spec.ValidateParams(Params(("slotNum", 99), ("action", "on")));

        // 记录当前行为：规格层不感知设备 slotAmount，故通过校验
        Assert.True(result.IsValid,
            "若此断言失败说明已补充 slotNum 上界校验，请同步更新本测试与 QA 报告中的已知缺口条目");
    }

    /// <summary>
    /// getDevStatus 的 q 参数要求固件 ≥ v4.0.20（asopen.md:311）。
    /// </summary>
    [Theory]
    [InlineData("SWITCH-EC618X-R24-O-V4.0.8", false)]
    [InlineData("SWITCH-EC618X-R24-O-V4.0.19", false)]
    [InlineData("SWITCH-EC618X-R24-O-V4.0.20", true)]
    [InlineData("SWITCH-EC618X-R24-O-V5.0.1", true)]
    public void GetDevStatus_Q_Should_Respect_Firmware_Threshold(string firmware, bool expectValid)
    {
        var spec = AnShengCommandCatalog.Get("getDevStatus")!;
        var result = spec.ValidateParams(Params(("q", "slots,EMdata")), firmware);
        Assert.Equal(expectValid, result.IsValid);
    }

    /// <summary>不传 q 时任何固件版本都应通过（低版本设备仍可取全量状态）。</summary>
    [Theory]
    [InlineData("SWITCH-EC618X-R24-O-V4.0.8")]
    [InlineData(null)]
    public void GetDevStatus_Without_Q_Should_Pass_On_Any_Firmware(string? firmware)
    {
        var spec = AnShengCommandCatalog.Get("getDevStatus")!;
        Assert.True(spec.ValidateParams(null, firmware).IsValid);
    }

    /// <summary>
    /// 【已知缺口 · 文档化】定时任务对象内的 uploadEnable 要求固件 ≥ v5.0.1
    /// （asopen.md:3083 / 3285 / 3469 / 3695 / 3873 / 4017 / 4185），
    /// 但 tasks/timeTasks 为整体数组参数，规格层未对数组子项做版本门槛校验。
    /// </summary>
    [Fact]
    public void TimeTask_UploadEnable_Firmware_Gate_Is_Not_Enforced_KnownGap()
    {
        var spec = AnShengCommandCatalog.Get("setSlotTimeTasks")!;
        var parameters = Params(("timeTasks", new object[]
        {
            new Dictionary<string, object?>
            {
                ["id"] = "1702288645833",
                ["enable"] = true,
                ["weekDays"] = new[] { 1, 2, 3 },
                ["hour"] = 10,
                ["minute"] = 30,
                ["action"] = "on",
                ["uploadEnable"] = true
            }
        }));

        // 低于 5.0.1 的固件也不会被拦截 —— 记录当前行为
        var result = spec.ValidateParams(parameters, "SWITCH-EC618X-R24-O-V4.0.8");
        Assert.True(result.IsValid,
            "若此断言失败说明已补充数组子项固件门槛校验，请同步更新 QA 报告中的已知缺口条目");
    }

    /// <summary>Parser 面对畸形输入必须安全降级返回 null，不得抛异常。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{")]
    [InlineData("{\"method\":}")]
    [InlineData("[]")]
    [InlineData("[1,2,3]")]
    [InlineData("{}")]                                   // 缺 method
    [InlineData("{\"imei\":\"864536072949900\"}")]       // 缺 method
    [InlineData("{\"method\":\"\"}")]                    // method 为空串
    [InlineData("null")]
    public void Parser_Should_Degrade_Safely_On_Malformed_Payload(string? payload)
    {
        var exception = Record.Exception(() =>
        {
            var message = _parser.Parse(payload);
            Assert.Null(message);
        });

        Assert.Null(exception);
    }

    /// <summary>字段类型异常（timestamp 为对象 / slots 为字符串）时不得抛出未捕获异常。</summary>
    [Theory]
    [InlineData("{\"method\":\"getDevStatus\",\"imei\":\"1\",\"timestamp\":{\"a\":1}}")]
    [InlineData("{\"method\":\"getDevStatus\",\"imei\":\"1\",\"slots\":\"0,1\"}")]
    [InlineData("{\"method\":\"getDevStatus\",\"imei\":\"1\",\"tasks\":\"none\"}")]
    [InlineData("{\"method\":\"getDevStatus\",\"imei\":\"1\",\"EMdata\":{}}")]
    [InlineData("{\"method\":\"getDevStatus\",\"imei\":\"1\",\"temperature\":\"N/A\"}")]
    public void Parser_Should_Not_Throw_On_Unexpected_Field_Shapes(string payload)
    {
        var exception = Record.Exception(() =>
        {
            var message = _parser.Parse(payload);
            if (message != null)
            {
                _parser.ParseDevStatus(message);
                _parser.NormalizeForSensorData(message, "/iot/server/iot-board/1");
            }
        });

        Assert.Null(exception);
    }

    /// <summary>设备回复「method unsupported」时必须被识别（asopen.md:112）。</summary>
    [Fact]
    public void Unsupported_Result_Should_Be_Recognized()
    {
        var message = _parser.Parse(
            "{\"method\":\"getTimeTasks\",\"result\":\"method unsupported\",\"imei\":\"1\"}");

        Assert.NotNull(message);
        Assert.True(message!.IsUnsupported);
        Assert.False(message.IsOk);
    }

    /// <summary>
    /// 节流器并发正确性：多线程同时对<b>同一 IMEI</b> 下发时必须串行且间隔 ≥ 100ms
    /// （asopen.md:169「每个命令之间最好间隔100ms，防止命令粘连」）。
    /// </summary>
    [Fact]
    public async Task Throttle_Should_Serialize_Concurrent_Sends_To_Same_Imei()
    {
        const int intervalMs = 100;
        const int commandCount = 5;

        using var throttle = new AnShengCommandThrottle(intervalMs);
        var timestamps = new ConcurrentBag<long>();
        var stopwatch = Stopwatch.StartNew();

        await Task.WhenAll(Enumerable.Range(0, commandCount).Select(async _ =>
        {
            await throttle.WaitTurnAsync(Imei);
            timestamps.Add(stopwatch.ElapsedMilliseconds);
        }));

        stopwatch.Stop();
        Assert.Equal(commandCount, timestamps.Count);

        // 首条不等待，其余各等待一个间隔 → 总耗时 ≥ (n-1) × interval（留 15ms 计时容差）
        Assert.True(stopwatch.ElapsedMilliseconds >= (commandCount - 1) * intervalMs - 15,
            $"同一 IMEI 并发下发未被串行化，总耗时仅 {stopwatch.ElapsedMilliseconds}ms");

        // 相邻两次放行的间隔不得小于阈值
        var ordered = timestamps.OrderBy(t => t).ToArray();
        for (var i = 1; i < ordered.Length; i++)
        {
            Assert.True(ordered[i] - ordered[i - 1] >= intervalMs - 15,
                $"第 {i} 次与第 {i - 1} 次下发间隔仅 {ordered[i] - ordered[i - 1]}ms");
        }
    }

    /// <summary>不同 IMEI 之间不得相互阻塞。</summary>
    [Fact]
    public async Task Throttle_Should_Not_Block_Across_Different_Imeis()
    {
        using var throttle = new AnShengCommandThrottle(200);
        var stopwatch = Stopwatch.StartNew();

        await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(i => throttle.WaitTurnAsync($"86453607294990{i}")));

        stopwatch.Stop();
        Assert.True(stopwatch.ElapsedMilliseconds < 200,
            $"不同 IMEI 被错误串行化，耗时 {stopwatch.ElapsedMilliseconds}ms");
    }

    // ─────────────────────────────────────────────────────────────
    // E. Legacy 充电桩链路回归（独立于工程师的 3 个用例）
    // ─────────────────────────────────────────────────────────────

    /// <summary>Legacy orderStart 必须保留 param 包裹与毫秒字符串 timestamp。</summary>
    [Fact]
    public void Legacy_OrderStart_Should_Keep_Param_Wrapper()
    {
#pragma warning disable CS0618
        var (frameId, payload) = _builder.BuildOrderStart(Imei, "SN20250417001", 2, 3600);
#pragma warning restore CS0618

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        Assert.Equal("orderStart", root.GetProperty("method").GetString());
        Assert.Equal(Imei, root.GetProperty("imei").GetString());
        Assert.Equal(frameId, root.GetProperty("frameId").GetString());

        // Legacy timestamp 为毫秒<b>字符串</b>
        var timestamp = root.GetProperty("timestamp");
        Assert.Equal(JsonValueKind.String, timestamp.ValueKind);
        Assert.True(long.Parse(timestamp.GetString()!) > 1_000_000_000_000L);

        // 业务参数在 param 内，不得平铺
        var param = root.GetProperty("param");
        Assert.Equal("SN20250417001", param.GetProperty("sn").GetString());
        Assert.Equal(2, param.GetProperty("order").GetInt32());
        Assert.Equal(3600, param.GetProperty("limit").GetInt32());
        Assert.False(root.TryGetProperty("sn", out _), "Legacy 参数不应平铺到顶层");
    }

    /// <summary>Legacy orderEnd 回归。</summary>
    [Fact]
    public void Legacy_OrderEnd_Should_Keep_Param_Wrapper()
    {
#pragma warning disable CS0618
        var (_, payload) = _builder.BuildOrderEnd(Imei, "SN20250417001", "manual");
#pragma warning restore CS0618

        using var document = JsonDocument.Parse(payload);
        var param = document.RootElement.GetProperty("param");

        Assert.Equal("orderEnd", document.RootElement.GetProperty("method").GetString());
        Assert.Equal("SN20250417001", param.GetProperty("sn").GetString());
        Assert.Equal("manual", param.GetProperty("reason").GetString());
    }

    /// <summary>Legacy orderUp 上报解析回归。</summary>
    [Fact]
    public void Legacy_OrderUp_Should_Still_Parse()
    {
        const string payload = """
                               {
                                 "method": "orderUp",
                                 "imei": "864536072949900",
                                 "frameId": "1745396239780",
                                 "timestamp": "1745396759000",
                                 "param": {
                                   "sn": "SN20250417001",
                                   "order": 1,
                                   "state": 1,
                                   "p": 1350.5,
                                   "e": 2.75,
                                   "timing": 3600,
                                   "limit": 7200
                                 }
                               }
                               """;

        var message = _parser.Parse(payload);
        Assert.NotNull(message);
        Assert.Equal(AnShengMessageCategory.OrderUp, _parser.GetCategory(message!));

        var order = _parser.ParseOrderData(message!);
        Assert.NotNull(order);
        Assert.Equal("SN20250417001", order!.Sn);
        Assert.Equal(1, order.Order);
        Assert.Equal(1350.5, order.P!.Value, 3);
        Assert.Equal(2.75, order.E!.Value, 3);
        Assert.Equal(3600, order.Timing);
        Assert.Equal(7200, order.Limit);

        // 毫秒字符串时间戳必须被归一化为正确年份
        Assert.NotNull(message!.TimestampUtc);
        Assert.Equal(2025, message.TimestampUtc!.Value.Year);
    }

    /// <summary>
    /// Legacy 充电桩 getDevStatus（param 包裹）经标准化后，
    /// 必须产出 DataCollectionService 认识的 total_power / total_energy 键，
    /// 分别映射 ElectricPower / ElectricKWh。
    /// </summary>
    [Fact]
    public void Legacy_DevStatus_Should_Emit_TotalPower_And_TotalEnergy_Keys()
    {
        const string payload = """
                               {
                                 "method": "getDevStatus",
                                 "imei": "864536072949900",
                                 "timestamp": "1745396759000",
                                 "param": {
                                   "netType": "4G",
                                   "signal": 25,
                                   "EMdata": [
                                     { "v": 220.0, "c": 5.0, "p": 1100.0, "e": 12.5 },
                                     { "v": 221.0, "c": 4.0, "p": 900.0,  "e": 7.5 }
                                   ]
                                 }
                               }
                               """;

        var message = _parser.Parse(payload);
        Assert.NotNull(message);

        var normalized = _parser.NormalizeForSensorData(message!, "/devtoser/pub/864536072949900");
        using var document = JsonDocument.Parse(normalized);
        var root = document.RootElement;

        // total_power → ElectricPower（DataCollectionService.cs:72）
        Assert.Equal(2000.0, root.GetProperty("total_power").GetDouble(), 3);
        // total_energy → ElectricKWh（DataCollectionService.cs:73）
        Assert.Equal(20.0, root.GetProperty("total_energy").GetDouble(), 3);
        Assert.Equal(2, root.GetProperty("slot_count").GetInt32());
        Assert.Equal(220.5, root.GetProperty("avg_voltage").GetDouble(), 3);
    }

    /// <summary>
    /// 二开协议（平铺）getDevStatus 也必须产出同样的 total_power / total_energy 键，
    /// 保证两套链路进入相同的采集映射。
    /// </summary>
    [Fact]
    public void Flat_DevStatus_Should_Emit_Same_Collection_Keys_As_Legacy()
    {
        const string payload = """
                               {
                                 "method": "getDevStatus",
                                 "imei": "864536072949900",
                                 "result": "ok",
                                 "netType": "4G",
                                 "signal": 25,
                                 "timestamp": 1745398605,
                                 "EMdata": [
                                   { "v": 220.0, "c": 5.0, "p": 1100.0, "e": 12.5 },
                                   { "v": 221.0, "c": 4.0, "p": 900.0,  "e": 7.5 }
                                 ]
                               }
                               """;

        var message = _parser.Parse(payload);
        Assert.NotNull(message);

        var normalized = _parser.NormalizeForSensorData(message!, "/iot/server/iot-board/864536072949900");
        using var document = JsonDocument.Parse(normalized);
        var root = document.RootElement;

        Assert.Equal(2000.0, root.GetProperty("total_power").GetDouble(), 3);
        Assert.Equal(20.0, root.GetProperty("total_energy").GetDouble(), 3);
        Assert.Equal("4G", root.GetProperty("net_type").GetString());
    }

    // ─────────────────────────────────────────────────────────────
    // 辅助方法
    // ─────────────────────────────────────────────────────────────

    private static Dictionary<string, object?> Params(params (string Key, object? Value)[] items)
    {
        var dictionary = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in items) dictionary[key] = value;
        return dictionary;
    }

    /// <summary>按规格生成一套合法的必填参数，用于「缺一项」类测试。</summary>
    private static Dictionary<string, object?> BuildValidParams(AnShengCommandSpec spec)
    {
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var param in spec.Params.Where(p => p.Required))
        {
            parameters[param.Name] = param.Type switch
            {
                AnShengParamType.String => param.AllowedValues is { Count: > 0 }
                    ? param.AllowedValues[0]
                    : "x",
                AnShengParamType.Int => (int)(param.Minimum ?? 1),
                AnShengParamType.Double => param.Minimum ?? 1.0,
                AnShengParamType.Bool => true,
                AnShengParamType.Array => new object[] { 1 },
                // 生产路径下 Dictionary<string, object?> 由 System.Text.Json 绑定，
                // 嵌套对象实际到达的是 JsonElement(ValueKind.Object)，此处必须与生产一致，
                // 否则测得的是助手函数的行为而非真实行为。
                AnShengParamType.Object => JsonObjectElement("{\"RL\":0.24}"),
                _ => "x"
            };
        }

        return parameters;
    }

    /// <summary>构造一个 JsonElement 对象值，模拟 ASP.NET Core System.Text.Json 的实际绑定结果。</summary>
    private static JsonElement JsonObjectElement(string json)
        => JsonDocument.Parse(json).RootElement.Clone();

    /// <summary>把 JSON 拆成顶层字段字典（值保留为 JsonElement 以便结构化比对）。</summary>
    private static Dictionary<string, JsonElement> FieldsOf(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.Ordinal);
    }

    /// <summary>比对业务字段（忽略 frameId / timestamp / imei 等动态或平台注入字段）。</summary>
    private static void AssertBusinessFieldsEqual(
        Dictionary<string, JsonElement> expected,
        Dictionary<string, JsonElement> actual,
        string method)
    {
        AssertBusinessFieldsSubset(expected, actual, method);

        var extra = actual.Keys
            .Where(k => k is not ("frameId" or "timestamp" or "imei"))
            .Where(k => !expected.ContainsKey(k))
            .ToArray();

        Assert.True(extra.Length == 0,
            $"{method} 下发报文出现协议示例中没有的字段：{string.Join(", ", extra)}");
    }

    /// <summary>断言协议示例中的每个业务字段都在实际报文中原样出现。</summary>
    private static void AssertBusinessFieldsSubset(
        Dictionary<string, JsonElement> expected,
        Dictionary<string, JsonElement> actual,
        string method)
    {
        foreach (var (key, value) in expected)
        {
            if (key is "frameId" or "timestamp" or "imei") continue;

            Assert.True(actual.ContainsKey(key), $"{method} 下发报文缺少协议字段 {key}");
            Assert.True(JsonEquals(value, actual[key]),
                $"{method}.{key} 与协议示例不一致：期望 {value.GetRawText()}，实际 {actual[key].GetRawText()}");
        }
    }

    private static bool JsonEquals(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != right.ValueKind)
        {
            // 数值类型统一按 double 比较（1 与 1.0 视为相等）
            if (left.ValueKind == JsonValueKind.Number && right.ValueKind == JsonValueKind.Number)
            {
                return Math.Abs(left.GetDouble() - right.GetDouble()) < 1e-9;
            }

            return false;
        }

        return left.ValueKind switch
        {
            JsonValueKind.Number => Math.Abs(left.GetDouble() - right.GetDouble()) < 1e-9,
            JsonValueKind.String => string.Equals(left.GetString(), right.GetString(), StringComparison.Ordinal),
            JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null => true,
            JsonValueKind.Array => left.GetArrayLength() == right.GetArrayLength()
                                   && left.EnumerateArray().Zip(right.EnumerateArray())
                                       .All(pair => JsonEquals(pair.First, pair.Second)),
            JsonValueKind.Object => left.EnumerateObject().Count() == right.EnumerateObject().Count()
                                    && left.EnumerateObject().All(p =>
                                        right.TryGetProperty(p.Name, out var other)
                                        && JsonEquals(p.Value, other)),
            _ => string.Equals(left.GetRawText(), right.GetRawText(), StringComparison.Ordinal)
        };
    }
}
