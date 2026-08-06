using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using IoTPlatform.Data;
using IoTPlatform.Infrastructure.Protocol.Adapters;
using Xunit;

namespace IoTPlatform.AnSheng.Tests;

/// <summary>
/// 存量协议配置归一化器（<see cref="ProtocolConfigNormalizer"/>）验收。
///
/// 【守护的是哪个故障】
/// 旧版协议管理页的通用表单写入的是「小写键 + 字符串值」，例如 <c>{"host":"1.2.3.4","port":"502"}</c>。
/// 后端给适配器注入大小写不敏感反序列化后，键名问题解决了，但<b>值类型</b>问题没解决：
/// <c>"port":"502"</c> 绑定到 <c>int Port</c> 会直接抛 <see cref="JsonException"/>。
///
/// 【当前的双层防御 —— 读这个类之前必须先理解】
/// 后来 <see cref="ProtocolJsonOptions.CaseInsensitive"/> 又追加了
/// <see cref="JsonNumberHandling.AllowReadingFromString"/>，作为<b>运行时兜底</b>：
/// 现在字符串数值在适配器侧<b>已经不抛了</b>，会被直接读成数字。
/// 因此本类不能再无条件断言「存量原文会抛异常」—— 那个前提只在<b>没有该兜底</b>时成立。
///
/// 两层防御的分工（<see cref="ProtocolJsonOptions"/> 的注释里写得很清楚，此处与之对齐）：
///   · 运行时兜底（AllowReadingFromString）：保证「SQL 从未执行过的环境」也连得上，但数据依然是脏的；
///   · 数据清洗（本归一化器 + <c>scripts/normalize_protocol_config_keys.sql</c>）：把库里的数据真正洗干净。
/// 两者<b>不互斥、都要保留</b>。
///
/// 所以本类的断言范式相应调整为三条腿：
///   1. <see cref="StrictOptions"/>（不含兜底）下证明存量原文<b>确实会抛</b> —— 保留「为什么要迁移」的立论；
///   2. 归一化后的文本在 <see cref="StrictOptions"/> 下<b>也能干净绑定</b> —— 证明迁移后不再依赖兜底；
///   3. 归一化后的文本在真实 <see cref="AdapterOptions"/> 下值正确 —— 证明没有把线上行为改坏。
/// </summary>
public class ProtocolConfigNormalizerTests
{
    /// <summary>与各适配器实际使用的反序列化选项完全一致（internal，靠 InternalsVisibleTo 直接引用真身）。</summary>
    private static readonly JsonSerializerOptions AdapterOptions = ProtocolJsonOptions.CaseInsensitive;

    /// <summary>
    /// <b>不含</b> <see cref="JsonNumberHandling.AllowReadingFromString"/> 兜底的严格选项。
    /// 用途：证明「若哪天运行时兜底被移除，数据清洗就是唯一防线」，以及验证归一化产出的
    /// 数据是<b>真正干净</b>的（不靠兜底也能绑定），而不是「脏数据 + 宽松解析」凑合能跑。
    /// </summary>
    private static readonly JsonSerializerOptions StrictOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // ─────────────────────────────────────────────────────────────
    // 组 1：核心回归 —— 字符串数值
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 元断言（严格选项下）：证明存量原文确实是坏数据 —— 没有运行时兜底就会抛。
    /// 这条是整个迁移任务的立论基础：兜底可以被移除、可以被误用到别处，数据脏就是脏。
    /// </summary>
    [Theory]
    [InlineData("{\"host\":\"1.2.3.4\",\"port\":\"502\"}")]
    [InlineData("{\"port\":\"502\",\"Port\":5502}")]
    public void LegacyStringPort_UnderStrictOptions_ThrowsJsonException(string legacyJson)
    {
        Assert.ThrowsAny<JsonException>(
            () => JsonSerializer.Deserialize<ModbusTcpOptions>(legacyJson, StrictOptions));
    }

    /// <summary>
    /// 现状锁定：当前共享 options <b>带</b> AllowReadingFromString 兜底，所以存量原文<b>不再抛</b>。
    /// 这条不是在给脏数据背书，而是把「运行时兜底当前确实生效」钉死：
    /// 若哪天有人移除该兜底，这条会红，提醒他「归一化器/SQL 从兜底升级成唯一防线，必须先确认已执行」。
    /// </summary>
    [Theory]
    // 单个小写字符串键：兜底把 "502" 读成 502
    [InlineData("{\"host\":\"1.2.3.4\",\"port\":\"502\"}", 502)]
    // 新旧键并存：大小写不敏感下两个键都映射到 Port，System.Text.Json 是「后出现者覆盖」，
    // 所以结果是 5502 而不是 502。这正是脏数据的隐蔽之处 —— 不抛异常，但取哪个值取决于键顺序。
    // 归一化器则用确定性规则（规范键优先）消除这种顺序依赖，见 DuplicateLegacyAndCanonicalKey_* 用例。
    [InlineData("{\"port\":\"502\",\"Port\":5502}", 5502)]
    public void LegacyStringPort_UnderCurrentAdapterOptions_IsToleratedByRuntimeFallback(
        string legacyJson, int expectedPort)
    {
        Assert.True(
            AdapterOptions.NumberHandling.HasFlag(JsonNumberHandling.AllowReadingFromString),
            "共享 options 应带 AllowReadingFromString 运行时兜底；若已移除，请确认数据清洗（归一化器/SQL）已在目标环境执行。");

        var options = JsonSerializer.Deserialize<ModbusTcpOptions>(legacyJson, AdapterOptions);

        Assert.NotNull(options);
        Assert.Equal(expectedPort, options!.Port);
    }

    /// <summary>
    /// 迁移的真正价值：归一化后的文本<b>不依赖任何兜底</b>也能干净绑定。
    /// 这是「数据洗干净了」与「靠宽松解析凑合能跑」的分水岭。
    /// </summary>
    [Theory]
    [InlineData("modbustcp", "{\"host\":\"1.2.3.4\",\"port\":\"502\"}")]
    [InlineData("modbustcp", "{\"port\":\"502\",\"Port\":5502}")]
    public void AfterNormalize_BindsCleanlyEvenWithoutRuntimeFallback(string type, string legacyJson)
    {
        var normalized = ProtocolConfigNormalizer.Normalize(type, legacyJson);

        var exception = Record.Exception(
            () => JsonSerializer.Deserialize<ModbusTcpOptions>(normalized!, StrictOptions));

        Assert.Null(exception);
    }

    /// <summary>
    /// 归一化后，同一份存量文本必须能无异常绑定，且 Host/Port 是用户真实配置值（不是默认值）。
    /// </summary>
    [Fact]
    public void ModbusTcp_LegacyStringPort_AfterNormalize_DeserializesWithoutThrowing()
    {
        var normalized = ProtocolConfigNormalizer.Normalize("modbustcp", "{\"host\":\"1.2.3.4\",\"port\":\"502\"}");

        var options = JsonSerializer.Deserialize<ModbusTcpOptions>(normalized!, AdapterOptions);

        Assert.NotNull(options);
        Assert.Equal("1.2.3.4", options!.Host);
        Assert.Equal(502, options.Port);
        Assert.NotEqual("localhost", options.Host);
    }

    /// <summary>
    /// 字符串 <c>"502"</c> 必须变成 JSON 数字 502，而不是仍然带引号的 <c>"502"</c>。
    /// </summary>
    [Fact]
    public void StringPort_IsCoercedToJsonNumber()
    {
        var normalized = ProtocolConfigNormalizer.Normalize("modbustcp", "{\"port\":\"502\"}");

        var node = JsonNode.Parse(normalized!)!.AsObject();
        Assert.True(node.ContainsKey("Port"));
        Assert.Equal(JsonValueKind.Number, node["Port"]!.GetValueKind());
        Assert.Equal(502, node["Port"]!.GetValue<int>());
    }

    /// <summary>
    /// 新旧键并存（QA 实证的第二种爆炸姿势）：规范键 <c>Port</c> 胜出，小写 <c>port</c> 被删除。
    /// </summary>
    [Fact]
    public void DuplicateLegacyAndCanonicalKey_CanonicalValueWins_AndLegacyKeyRemoved()
    {
        var normalized = ProtocolConfigNormalizer.Normalize("modbustcp", "{\"port\":\"502\",\"Port\":5502}");

        var node = JsonNode.Parse(normalized!)!.AsObject();
        Assert.False(node.ContainsKey("port"));
        Assert.Equal(5502, node["Port"]!.GetValue<int>());

        var options = JsonSerializer.Deserialize<ModbusTcpOptions>(normalized!, AdapterOptions);
        Assert.Equal(5502, options!.Port);
    }

    // ─────────────────────────────────────────────────────────────
    // 组 1b：JSON null 语义（规则 0 + 规则 3 的 null-aware 修订）
    //
    // 前提事实（QA 已在 ExplicitJsonNull_OverridesPropertyInitializerDefault_NotIgnored 实测）：
    // System.Text.Json 遇到显式 null 会<b>覆盖</b>属性初始化器默认值 ——
    // `public string Host { get; set; } = "localhost"` 碰上 {"Host":null} 绑定结果是 null。
    // 所以保留 null 会产出「比默认值更坏」的结果，必须删键。
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 元断言：先证明「显式 null 确实会覆盖属性初始化器默认值」，
    /// 否则下面所有「null 必须删键」的用例就失去了立论基础。
    /// </summary>
    [Fact]
    public void ExplicitJsonNull_OverridesPropertyInitializerDefault_Premise()
    {
        var options = JsonSerializer.Deserialize<ModbusTcpOptions>("{\"Host\":null}", AdapterOptions);

        Assert.NotNull(options);
        Assert.Null(options!.Host);                 // 不是 "localhost"！
        Assert.NotEqual("localhost", options.Host); // 显式写出来：null 比默认值更坏
    }

    /// <summary>
    /// 期望行为矩阵（team-lead 下发）逐条断言。
    /// 核心是第 3、5 行的 <b>rescue</b>：规范键为 null 时，同名历史小写键的真实值必须被救回来，
    /// 而不是「手上有真实值却丢掉、换成连默认值都不如的 null」。
    /// </summary>
    [Theory]
    // 规范键非 null → 照旧优先
    [InlineData("modbustcp", "{\"host\":\"y\",\"Host\":\"x\"}", "{\"Host\":\"x\"}")]
    // 与键顺序无关
    [InlineData("modbustcp", "{\"Host\":\"x\",\"host\":\"y\"}", "{\"Host\":\"x\"}")]
    // rescue：规范键为 null，小写键真实值救回
    [InlineData("modbustcp", "{\"host\":\"x\",\"Host\":null}", "{\"Host\":\"x\"}")]
    // 纯 null → 删键，DTO 默认值生效
    [InlineData("modbustcp", "{\"Host\":null}", "{}")]
    // 数值属性同样 rescue（且顺带 coerce 成数字）
    [InlineData("modbustcp", "{\"port\":\"502\",\"Port\":null}", "{\"Port\":502}")]
    // 可空属性删 null 同样无害
    [InlineData("opcua", "{\"CertificatePath\":null}", "{}")]
    public void JsonNull_BehaviorMatrix(string type, string input, string expected)
    {
        var normalized = ProtocolConfigNormalizer.Normalize(type, input);

        Assert.True(
            JsonNode.DeepEquals(JsonNode.Parse(expected), JsonNode.Parse(normalized!)),
            $"输入 {input} 期望 {expected}，实际 {normalized}");
    }

    /// <summary>
    /// 端到端：rescue 后必须真的能绑定出真实值，既不是 null 也不是 DTO 默认值。
    /// 这是整条 null 规则存在的意义 —— 光看 JSON 文本对不算数，要能喂进适配器 DTO。
    /// </summary>
    [Fact]
    public void RescuedValue_DeserializesToRealValue_NotNullNorDefault()
    {
        var normalized = ProtocolConfigNormalizer.Normalize("modbustcp", "{\"host\":\"1.2.3.4\",\"Host\":null}");

        var options = JsonSerializer.Deserialize<ModbusTcpOptions>(normalized!, AdapterOptions);

        Assert.Equal("1.2.3.4", options!.Host);
        Assert.NotNull(options.Host);
        Assert.NotEqual("localhost", options.Host);
    }

    /// <summary>纯 null 删键后，DTO 的属性初始化器默认值必须真的生效。</summary>
    [Fact]
    public void NullOnlyCanonicalKey_IsRemoved_SoInitializerDefaultApplies()
    {
        var normalized = ProtocolConfigNormalizer.Normalize("modbustcp", "{\"Host\":null,\"Port\":null}");

        var options = JsonSerializer.Deserialize<ModbusTcpOptions>(normalized!, AdapterOptions);

        Assert.Equal("localhost", options!.Host); // 初始化器默认值回来了
        Assert.Equal(502, options.Port);
    }

    /// <summary>
    /// 规则 0 作用于<b>全部</b>属性，不只数值属性：未知键上的 null 也要删。
    /// （未知键的 null 虽不致命，但留着毫无价值，且会让前后端归一结果不一致。）
    /// </summary>
    [Fact]
    public void JsonNull_OnUnknownKey_IsAlsoRemoved()
    {
        var normalized = ProtocolConfigNormalizer.Normalize(
            "modbustcp", "{\"host\":\"1.2.3.4\",\"customFlag\":null,\"keep\":\"me\"}");

        var node = JsonNode.Parse(normalized!)!.AsObject();
        Assert.False(node.ContainsKey("customFlag"));
        Assert.Equal("me", node["keep"]!.GetValue<string>());
        Assert.Equal("1.2.3.4", node["Host"]!.GetValue<string>());
    }

    /// <summary>
    /// 边界：空串对 <c>string</c> 属性是<b>合法值</b>，绝不能跟着 null 一起被删。
    /// 空串删键只作用于数值属性（见 <see cref="NumericProperty_BlankOrNull_IsRemovedSoDefaultApplies"/>）。
    /// </summary>
    [Fact]
    public void EmptyString_OnStringProperty_IsPreserved()
    {
        var normalized = ProtocolConfigNormalizer.Normalize("modbustcp", "{\"host\":\"\"}");

        var node = JsonNode.Parse(normalized!)!.AsObject();
        Assert.True(node.ContainsKey("Host"));
        Assert.Equal("", node["Host"]!.GetValue<string>());
    }

    /// <summary>rescue 之后仍必须幂等：第二遍不能再变。</summary>
    [Theory]
    [InlineData("modbustcp", "{\"host\":\"x\",\"Host\":null}")]
    [InlineData("modbustcp", "{\"port\":\"502\",\"Port\":null}")]
    [InlineData("mqtt", "{\"qoslevel\":\"2\",\"QosLevel\":null}")]
    public void JsonNullRescue_IsIdempotent(string type, string legacy)
    {
        var once = ProtocolConfigNormalizer.Normalize(type, legacy);
        var twice = ProtocolConfigNormalizer.Normalize(type, once);

        Assert.Equal(once, twice);
    }

    // ─────────────────────────────────────────────────────────────
    // 组 2：各协议的键重映射
    // ─────────────────────────────────────────────────────────────

    /// <summary>MQTT：全套小写键必须映射到 <c>MqttProtocolOptions</c> 的 PascalCase 属性名。</summary>
    [Fact]
    public void Mqtt_LowercaseKeys_AreRemappedToPascalCase()
    {
        const string legacy = """
            {"host":"10.0.0.5","port":"8883","username":"iot","password":"p@ss",
             "clientidprefix":"plant_a","cleansession":false,"qoslevel":"2"}
            """;

        var normalized = ProtocolConfigNormalizer.Normalize("mqtt", legacy);
        var node = JsonNode.Parse(normalized!)!.AsObject();

        Assert.Equal("10.0.0.5", node["Host"]!.GetValue<string>());
        Assert.Equal(8883, node["Port"]!.GetValue<int>());
        Assert.Equal("iot", node["Username"]!.GetValue<string>());
        Assert.Equal("p@ss", node["Password"]!.GetValue<string>());
        Assert.Equal("plant_a", node["ClientIdPrefix"]!.GetValue<string>());
        Assert.False(node["CleanSession"]!.GetValue<bool>());
        Assert.Equal(2, node["QosLevel"]!.GetValue<int>());

        // 旧键必须清干净
        foreach (var legacyKey in new[] { "host", "port", "username", "password", "clientidprefix", "cleansession", "qoslevel" })
        {
            Assert.False(node.ContainsKey(legacyKey), $"旧键 {legacyKey} 未被删除");
        }

        var options = JsonSerializer.Deserialize<MqttProtocolOptions>(normalized!, AdapterOptions);
        Assert.Equal("10.0.0.5", options!.Host);
        Assert.Equal(8883, options.Port);
        Assert.Equal(2, options.QosLevel);
        Assert.False(options.CleanSession);
    }

    /// <summary>MQTT：<c>endpoint</c> 按迁移约定统一改名为 <c>EndpointUrl</c>（DTO 未知成员，绑定时被忽略，不抛异常）。</summary>
    [Fact]
    public void Mqtt_EndpointKey_IsRenamedToEndpointUrl_AndStillBindsWithoutThrowing()
    {
        var normalized = ProtocolConfigNormalizer.Normalize(
            "mqtt", "{\"host\":\"10.0.0.5\",\"endpoint\":\"tcp://10.0.0.5:1883\"}");

        var node = JsonNode.Parse(normalized!)!.AsObject();
        Assert.False(node.ContainsKey("endpoint"));
        Assert.Equal("tcp://10.0.0.5:1883", node["EndpointUrl"]!.GetValue<string>());

        var options = JsonSerializer.Deserialize<MqttProtocolOptions>(normalized!, AdapterOptions);
        Assert.Equal("10.0.0.5", options!.Host);
    }

    /// <summary>安圣 MQTT 与通用 MQTT 同构，必须走同一套重映射。</summary>
    [Fact]
    public void AnShengMqtt_LowercaseKeys_AreRemapped()
    {
        var normalized = ProtocolConfigNormalizer.Normalize(
            "ansheng_mqtt", "{\"host\":\"10.0.0.9\",\"port\":\"1884\",\"qoslevel\":\"1\"}");

        var options = JsonSerializer.Deserialize<AnShengMqttProtocolOptions>(normalized!, AdapterOptions);

        Assert.Equal("10.0.0.9", options!.Host);
        Assert.Equal(1884, options.Port);
        Assert.Equal(1, options.QosLevel);
    }

    /// <summary>Modbus TCP：<c>host</c>/<c>port</c> → <c>Host</c>/<c>Port</c>。</summary>
    [Fact]
    public void ModbusTcp_LowercaseKeys_AreRemapped()
    {
        var normalized = ProtocolConfigNormalizer.Normalize(
            "modbustcp", "{\"host\":\"192.168.1.20\",\"port\":\"5020\",\"timeoutMs\":\"1500\"}");

        var node = JsonNode.Parse(normalized!)!.AsObject();
        Assert.Equal("192.168.1.20", node["Host"]!.GetValue<string>());
        Assert.Equal(5020, node["Port"]!.GetValue<int>());
        // timeoutMs 只是大小写差异，按 DTO 规范名归一为 TimeoutMs，同时字符串值被矫正成数字
        Assert.Equal(1500, node["TimeoutMs"]!.GetValue<int>());

        var options = JsonSerializer.Deserialize<ModbusTcpOptions>(normalized!, AdapterOptions);
        Assert.Equal(5020, options!.Port);
        Assert.Equal(1500, options.TimeoutMs);
    }

    /// <summary>Modbus RTU：<c>serialPort</c> → <c>PortName</c>、<c>baudRate</c> → <c>BaudRate</c>。</summary>
    [Fact]
    public void ModbusRtu_SerialPortAndBaudRate_AreRemapped()
    {
        var normalized = ProtocolConfigNormalizer.Normalize(
            "modbusrtu", "{\"serialPort\":\"COM3\",\"baudRate\":\"115200\",\"dataBits\":\"7\"}");

        var node = JsonNode.Parse(normalized!)!.AsObject();
        Assert.False(node.ContainsKey("serialPort"));
        Assert.False(node.ContainsKey("baudRate"));
        Assert.Equal("COM3", node["PortName"]!.GetValue<string>());
        Assert.Equal(115200, node["BaudRate"]!.GetValue<int>());
        Assert.Equal(7, node["DataBits"]!.GetValue<int>());

        var options = JsonSerializer.Deserialize<ModbusRtuOptions>(normalized!, AdapterOptions);
        Assert.Equal("COM3", options!.PortName);
        Assert.Equal(115200, options.BaudRate);
        Assert.Equal(7, options.DataBits);
        Assert.NotEqual("COM1", options.PortName);
    }

    /// <summary>OPC UA：<c>endpoint</c> → <c>EndpointUrl</c>，且 <c>timeoutMs</c> 字符串被矫正。</summary>
    [Fact]
    public void OpcUa_EndpointKey_IsRemappedToEndpointUrl()
    {
        var normalized = ProtocolConfigNormalizer.Normalize(
            "opcua", "{\"endpoint\":\"opc.tcp://10.0.0.7:4840\",\"timeoutMs\":\"12000\"}");

        var node = JsonNode.Parse(normalized!)!.AsObject();
        Assert.False(node.ContainsKey("endpoint"));
        Assert.Equal("opc.tcp://10.0.0.7:4840", node["EndpointUrl"]!.GetValue<string>());
        Assert.Equal(12000, node["TimeoutMs"]!.GetValue<int>());

        var options = JsonSerializer.Deserialize<OpcUaOptions>(normalized!, AdapterOptions);
        Assert.Equal("opc.tcp://10.0.0.7:4840", options!.EndpointUrl);
        Assert.Equal(12000, options.TimeoutMs);
        Assert.NotEqual("opc.tcp://localhost:4840", options.EndpointUrl);
    }

    // ─────────────────────────────────────────────────────────────
    // 组 3：协议类型写法容错
    // ─────────────────────────────────────────────────────────────

    /// <summary>Type 字段在库里大小写与分隔符都不统一，必须一律识别。</summary>
    [Theory]
    [InlineData("modbustcp")]
    [InlineData("ModbusTcp")]
    [InlineData("MODBUS_TCP")]
    [InlineData("modbus-tcp")]
    [InlineData(" Modbus Tcp ")]
    public void ProtocolType_IsMatchedCaseAndSeparatorInsensitively(string type)
    {
        var normalized = ProtocolConfigNormalizer.Normalize(type, "{\"host\":\"1.2.3.4\",\"port\":\"502\"}");

        var node = JsonNode.Parse(normalized!)!.AsObject();
        Assert.Equal("1.2.3.4", node["Host"]!.GetValue<string>());
        Assert.Equal(502, node["Port"]!.GetValue<int>());
    }

    /// <summary>未知协议类型（http/tcp/bacnet 等）不在迁移范围内，必须原样返回，绝不改坏。</summary>
    [Theory]
    [InlineData("http")]
    [InlineData("bacnet")]
    [InlineData("")]
    [InlineData(null)]
    public void UnknownProtocolType_ReturnsInputUnchanged(string? type)
    {
        const string input = "{\"host\":\"1.2.3.4\",\"port\":\"502\"}";

        Assert.Equal(input, ProtocolConfigNormalizer.Normalize(type, input));
    }

    // ─────────────────────────────────────────────────────────────
    // 组 4：幂等性
    // ─────────────────────────────────────────────────────────────

    /// <summary>已经是 PascalCase + 正确类型的配置，归一化必须不改变其语义。</summary>
    [Theory]
    [InlineData("mqtt", "{\"Host\":\"10.0.0.5\",\"Port\":8883,\"QosLevel\":2,\"CleanSession\":true}")]
    [InlineData("modbustcp", "{\"Host\":\"1.2.3.4\",\"Port\":502,\"TimeoutMs\":3000}")]
    [InlineData("modbusrtu", "{\"PortName\":\"COM3\",\"BaudRate\":9600,\"Parity\":\"None\"}")]
    [InlineData("opcua", "{\"EndpointUrl\":\"opc.tcp://h:4840\",\"UsePollingMode\":true}")]
    public void AlreadyPascalCase_IsUnchanged(string type, string alreadyNormalized)
    {
        var once = ProtocolConfigNormalizer.Normalize(type, alreadyNormalized);

        Assert.True(
            JsonNode.DeepEquals(JsonNode.Parse(alreadyNormalized), JsonNode.Parse(once!)),
            $"已规范化的输入被改动了：{alreadyNormalized} → {once}");
    }

    /// <summary>归一化必须幂等：跑第二遍与第一遍结果<b>逐字符相同</b>（迁移脚本可安全重跑）。</summary>
    [Theory]
    [InlineData("mqtt", "{\"host\":\"10.0.0.5\",\"port\":\"8883\",\"qoslevel\":\"2\"}")]
    [InlineData("modbusrtu", "{\"serialPort\":\"COM3\",\"baudRate\":\"115200\"}")]
    [InlineData("opcua", "{\"endpoint\":\"opc.tcp://h:4840\",\"timeoutMs\":\"9000\"}")]
    public void Normalize_IsIdempotent(string type, string legacy)
    {
        var once = ProtocolConfigNormalizer.Normalize(type, legacy);
        var twice = ProtocolConfigNormalizer.Normalize(type, once);

        Assert.Equal(once, twice);
    }

    // ─────────────────────────────────────────────────────────────
    // 组 5：未知键 / 嵌套结构保留
    // ─────────────────────────────────────────────────────────────

    /// <summary>业务自定义扩展字段必须原样保留，不允许迁移顺手把人家的数据吃掉。</summary>
    [Fact]
    public void UnknownKeys_ArePreservedAsIs()
    {
        var normalized = ProtocolConfigNormalizer.Normalize(
            "modbustcp", "{\"host\":\"1.2.3.4\",\"customFlag\":\"keep-me\",\"vendor\":{\"id\":7}}");

        var node = JsonNode.Parse(normalized!)!.AsObject();
        Assert.Equal("keep-me", node["customFlag"]!.GetValue<string>());
        Assert.Equal(7, node["vendor"]!["id"]!.GetValue<int>());
        Assert.Equal("1.2.3.4", node["Host"]!.GetValue<string>());
    }

    /// <summary>嵌套数组（Devices/Registers）必须原封不动地深拷贝过去。</summary>
    [Fact]
    public void NestedArrays_ArePreserved()
    {
        const string legacy = """
            {"host":"1.2.3.4","port":"502",
             "Devices":[{"DeviceId":1,"SerialNumber":"SN-1","SlaveId":3,
                         "Registers":[{"Name":"temp","Address":100,"Count":2}]}]}
            """;

        var normalized = ProtocolConfigNormalizer.Normalize("modbustcp", legacy);
        var options = JsonSerializer.Deserialize<ModbusTcpOptions>(normalized!, AdapterOptions);

        Assert.Single(options!.Devices);
        Assert.Equal("SN-1", options.Devices[0].SerialNumber);
        Assert.Equal((byte)3, options.Devices[0].SlaveId);
        Assert.Single(options.Devices[0].Registers);
        Assert.Equal("temp", options.Devices[0].Registers[0].Name);
        Assert.Equal(100, options.Devices[0].Registers[0].Address);
    }

    /// <summary>中文等非 ASCII 值不应被转义成 <c>\uXXXX</c>，运维直接看库里的值要可读。</summary>
    [Fact]
    public void NonAsciiValues_AreNotEscaped()
    {
        var normalized = ProtocolConfigNormalizer.Normalize("modbustcp", "{\"host\":\"1.2.3.4\",\"note\":\"一号车间\"}");

        Assert.Contains("一号车间", normalized);
        Assert.DoesNotContain("\\u", normalized);
    }

    // ─────────────────────────────────────────────────────────────
    // 组 6：数值属性的边界值
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 数值属性为空串或 JSON null 时同样会让适配器抛异常，归一化必须删除该键让 DTO 默认值生效。
    /// </summary>
    [Theory]
    [InlineData("{\"host\":\"1.2.3.4\",\"port\":\"\"}")]
    [InlineData("{\"host\":\"1.2.3.4\",\"port\":\"   \"}")]
    [InlineData("{\"host\":\"1.2.3.4\",\"Port\":null}")]
    public void NumericProperty_BlankOrNull_IsRemovedSoDefaultApplies(string legacy)
    {
        // 前置：原文确实会抛
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<ModbusTcpOptions>(legacy, AdapterOptions));

        var normalized = ProtocolConfigNormalizer.Normalize("modbustcp", legacy);

        var node = JsonNode.Parse(normalized!)!.AsObject();
        Assert.False(node.ContainsKey("Port"));
        Assert.False(node.ContainsKey("port"));

        var options = JsonSerializer.Deserialize<ModbusTcpOptions>(normalized!, AdapterOptions);
        Assert.Equal(502, options!.Port); // ModbusTcpOptions 的默认端口
        Assert.Equal("1.2.3.4", options.Host);
    }

    /// <summary>
    /// 锁定「什么样的字符串算合法整数」的边界 —— 这条契约同时约束 SQL 脚本的 REGEXP
    /// 与前端的解析实现，三处必须给出同样的答案，否则同一份数据在不同入口结果会分叉。
    /// </summary>
    /// <remarks>
    /// C# 侧用 <c>long.TryParse(trimmed, NumberStyles.Integer, InvariantCulture, ...)</c>，
    /// 而 <c>NumberStyles.Integer</c> = AllowLeadingWhite | AllowTrailingWhite | AllowLeadingSign，
    /// 即【带正号的 "+502" 也算合法整数】。这一点很容易被 SQL 的 <c>'^-?[0-9]+$'</c> 漏掉。
    /// </remarks>
    [Theory]
    [InlineData("502", true, 502L)]        // 常规
    [InlineData("  502  ", true, 502L)]    // 前后空白：Trim 后可解析
    [InlineData("+502", true, 502L)]       // 正号：NumberStyles.Integer 允许
    [InlineData("-1", true, -1L)]          // 负号
    [InlineData("0502", true, 502L)]       // 前导零
    [InlineData("50x2", false, 0L)]        // 非法字符
    [InlineData("5000.5", false, 0L)]      // 小数不猜测
    [InlineData("1e3", false, 0L)]         // 科学计数法不猜测
    public void NumericProperty_IntegerParsingBoundary_IsLockedForCrossStackParity(
        string rawPort, bool shouldCoerce, long expected)
    {
        var legacy = "{\"port\":\"" + rawPort + "\"}";

        var normalized = ProtocolConfigNormalizer.Normalize("modbustcp", legacy);

        var node = JsonNode.Parse(normalized!)!.AsObject();
        if (shouldCoerce)
        {
            Assert.Equal(JsonValueKind.Number, node["Port"]!.GetValueKind());
            Assert.Equal(expected, node["Port"]!.GetValue<long>());
        }
        else
        {
            // 不是合法整数就原样保留字符串，不做有损猜测。
            Assert.Equal(JsonValueKind.String, node["Port"]!.GetValueKind());
            Assert.Equal(rawPort, node["Port"]!.GetValue<string>());
        }
    }

    /// <summary>
    /// 记录一个【已知且刻意保留】的缺口：归一化用 <c>long.TryParse</c>，而所有数值 DTO 属性都是
    /// <c>int</c>。超出 Int32 范围的字符串会被"成功"矫正成 JSON 数字，但绑定时仍然抛异常。
    /// </summary>
    /// <remarks>
    /// 这条用例不是在为现状背书，而是把缺口钉死、防止它被静默改动：
    /// 归一化的承诺是"归一后能干净绑定"，这个输入违背了该承诺。
    /// 更麻烦的是 SQL 脚本阶段 3.2 只校验"是否仍为字符串"，矫正成数字后就查不出来了，
    /// 会给出"迁移干净"的假信号。缓解措施是 SQL 阶段 3.6 增加了 Int32 范围校验查询。
    /// 是否改成 int.TryParse（让超范围值保持字符串、从而被 3.2 抓到）需三端同步，已上报 team-lead。
    /// </remarks>
    [Fact]
    public void NumericProperty_OutOfInt32Range_IsCoercedButStillFailsToBind_KnownGap()
    {
        const string legacy = "{\"host\":\"1.2.3.4\",\"port\":\"3000000000\"}"; // > int.MaxValue

        var normalized = ProtocolConfigNormalizer.Normalize("modbustcp", legacy);

        // 归一化"成功"了：字符串被矫正成了 JSON 数字。
        var node = JsonNode.Parse(normalized!)!.AsObject();
        Assert.Equal(JsonValueKind.Number, node["Port"]!.GetValueKind());
        Assert.Equal(3000000000L, node["Port"]!.GetValue<long>());

        // 但绑定到 int Port 依然抛异常 —— 归一化并没有真正解决这一行数据。
        Assert.ThrowsAny<JsonException>(
            () => JsonSerializer.Deserialize<ModbusTcpOptions>(normalized!, AdapterOptions));
    }

    /// <summary>非整数格式的字符串不猜测：保持原值，留给人工排查（迁移不做有损改写）。</summary>
    [Fact]
    public void NumericProperty_NonIntegerString_IsLeftUntouched()
    {
        var normalized = ProtocolConfigNormalizer.Normalize("modbustcp", "{\"port\":\"50x2\"}");

        var node = JsonNode.Parse(normalized!)!.AsObject();
        Assert.Equal(JsonValueKind.String, node["Port"]!.GetValueKind());
        Assert.Equal("50x2", node["Port"]!.GetValue<string>());
    }

    /// <summary>已经是数字的值不许被动手脚。</summary>
    [Fact]
    public void NumericProperty_AlreadyNumber_IsPreserved()
    {
        var normalized = ProtocolConfigNormalizer.Normalize("modbustcp", "{\"host\":\"1.2.3.4\",\"port\":502}");

        var node = JsonNode.Parse(normalized!)!.AsObject();
        Assert.Equal(JsonValueKind.Number, node["Port"]!.GetValueKind());
        Assert.Equal(502, node["Port"]!.GetValue<int>());
    }

    /// <summary>非数值属性上的字符串不做任何类型矫正（例如 <c>StopBits</c> 就是字符串 "1"）。</summary>
    [Fact]
    public void NonNumericProperty_StringValue_IsNotCoerced()
    {
        var normalized = ProtocolConfigNormalizer.Normalize("modbusrtu", "{\"serialPort\":\"COM3\",\"StopBits\":\"1\"}");

        var node = JsonNode.Parse(normalized!)!.AsObject();
        Assert.Equal(JsonValueKind.String, node["StopBits"]!.GetValueKind());
        Assert.Equal("1", node["StopBits"]!.GetValue<string>());
    }

    // ─────────────────────────────────────────────────────────────
    // 组 7：防御性输入
    // ─────────────────────────────────────────────────────────────

    /// <summary>null / 空串 / 空白输入原样返回，不得凭空造出 "{}"。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NullOrBlankConfig_IsReturnedAsIs(string? configJson)
    {
        Assert.Equal(configJson, ProtocolConfigNormalizer.Normalize("mqtt", configJson));
    }

    /// <summary>非法 JSON / 非对象 JSON 原样返回，交人工处理，绝不吞掉原文。</summary>
    [Theory]
    [InlineData("not-a-json")]
    [InlineData("{\"host\":")]
    [InlineData("[1,2,3]")]
    [InlineData("\"just-a-string\"")]
    [InlineData("null")]
    public void InvalidOrNonObjectJson_IsReturnedAsIs(string configJson)
    {
        Assert.Equal(configJson, ProtocolConfigNormalizer.Normalize("mqtt", configJson));
    }

    /// <summary>空对象是合法输入，归一化后仍是空对象。</summary>
    [Fact]
    public void EmptyJsonObject_StaysEmptyObject()
    {
        var normalized = ProtocolConfigNormalizer.Normalize("mqtt", "{}");

        Assert.Equal("{}", normalized);
    }

    // ─────────────────────────────────────────────────────────────
    // 组 8：方案 A（SQL 清洗 / 本归一化器）与方案 B（运行时兜底
    //       AllowReadingFromString）的覆盖边界
    //
    // 【为什么需要这一组】
    // ProtocolJsonOptions 的注释称 B "只保证「SQL 从未执行过的环境」也不至于连不上"。
    // 实测下来这句话是**过度承诺**：B 走的是 System.Text.Json 的数字解析，
    // **不做 TRIM、不接受空串**；而归一化器（与前端、与 SQL 脚本）三处都先 TRIM。
    // 于是存在一类存量行——B 救不动、只有 A 能救。若无本组用例，
    // 「已经有 B 兜底了，SQL 可以不执行」会成为一个看似合理、实则会导致
    // 生产连接失败的决策。本组把 A ⊋ B 这个包含关系钉死。
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// A 与 B 的覆盖矩阵：<paramref name="bindsWithRuntimeFallbackAlone"/> 为 <c>false</c> 的行
    /// 就是「B 救不动、只有跑 SQL 才能修」的存量数据形态。
    /// 无论 B 是否救得动，经过归一化（A）之后都必须能干净绑定 —— 这是 A 的承诺。
    /// </summary>
    /// <remarks>
    /// 实测记录（.NET 8，AllowReadingFromString 对 <c>int</c> 字段）：
    /// <c>"502"/"+502"/"0502"/"-1"</c> 可绑定 —— 注意它比 JSON 数字文法更宽松，
    /// 前导正号与前导零都被接受，这与归一化器的 <c>NumberStyles.Integer</c> 恰好对齐（属巧合，故锁死）；
    /// 而带任何前后空白的数字串以及空串一律抛 <see cref="JsonException"/>。
    /// </remarks>
    [Theory]
    // 原始值        B 单独能否绑定   归一化后 Port 期望值
    [InlineData("502", true, 502)]
    [InlineData("+502", true, 502)]
    [InlineData("0502", true, 502)]
    [InlineData("-1", true, -1)]
    [InlineData("  502  ", false, 502)]
    [InlineData(" 502", false, 502)]
    [InlineData("502 ", false, 502)]
    [InlineData("", false, 502)]   // A 把空串键删除 → 回落 DTO 默认端口 502
    public void RuntimeFallbackB_IsNotASupersetOfMigrationA_CoverageMatrix(
        string rawPortValue,
        bool bindsWithRuntimeFallbackAlone,
        int expectedPortAfterMigration)
    {
        // 未迁移的存量行：键小写、值为字符串
        var legacyJson = "{\"host\":\"1.2.3.4\",\"port\":" + JsonSerializer.Serialize(rawPortValue) + "}";

        // ① 只有 B（SQL 从未执行过的环境）
        if (bindsWithRuntimeFallbackAlone)
        {
            var direct = JsonSerializer.Deserialize<ModbusTcpOptions>(legacyJson, AdapterOptions);
            Assert.NotNull(direct);
            Assert.Equal(expectedPortAfterMigration, direct!.Port);
        }
        else
        {
            // B 兜不住：这一行在跑 SQL 之前，运行时就是连不上的
            Assert.ThrowsAny<JsonException>(
                () => JsonSerializer.Deserialize<ModbusTcpOptions>(legacyJson, AdapterOptions));
        }

        // ② 跑过 A（归一化 / SQL 清洗）之后：全部必须干净绑定
        var normalized = ProtocolConfigNormalizer.Normalize("modbustcp", legacyJson);
        var afterMigration = JsonSerializer.Deserialize<ModbusTcpOptions>(normalized!, AdapterOptions);

        Assert.NotNull(afterMigration);
        Assert.Equal(expectedPortAfterMigration, afterMigration!.Port);
        Assert.Equal("1.2.3.4", afterMigration.Host);   // 迁移不得误伤同行的其它字段
    }

    /// <summary>
    /// 【根因护栏】运行时兜底（B）<b>并非</b>按 <see cref="NumberStyles.Integer"/> 解析 ——
    /// 这两套解析器在<b>空白</b>上的行为完全相反，本用例把这个区别钉死。
    /// </summary>
    /// <remarks>
    /// 【为什么需要这条】曾有人把「B 接受 <c>"+502"</c>/<c>"0502"</c>」归因为
    /// 「B 底层按 <c>NumberStyles.Integer</c> 解析」。这个归因是错的，而且危险：
    /// <c>NumberStyles.Integer</c> 按定义 = <c>AllowLeadingWhite | AllowTrailingWhite | AllowLeadingSign</c>，
    /// 若 B 真按它解析，<c>" 502"</c> 就必须被接受 —— 但实测 B 对<b>任何</b>带空白的值都抛异常。
    ///
    /// 危险之处在于：一旦有人相信「B 与归一化器用同一套规则」，就会顺势推出「B 等价于 A」，
    /// 从而推翻 <see cref="RuntimeFallbackB_IsNotASupersetOfMigrationA_CoverageMatrix"/> 建立的结论，
    /// 进而认为「已有 B，SQL 可以缓一缓」—— 而带空白/空串的存量行此刻正处于连接失败状态。
    ///
    /// 【真实情况】B 走的是 System.Text.Json 自己的 UTF-8 数字解析（不容任何空白，
    /// 但允许前导正负号与前导零）；归一化器走的是
    /// <c>long.TryParse(trimmed, NumberStyles.Integer, ...)</c>。
    /// 二者是<b>两套不同的解析器</b>，之所以在「<c>+502</c>/<c>0502</c>」上看起来一致，
    /// 是因为归一化器<b>先 Trim 过</b>——Trim 使 <c>NumberStyles.Integer</c> 的空白宽容性变成无关项，
    /// 剩下的「符号 + 前导零」子集才恰好重合。这就是「巧合性对齐」的准确含义：
    /// 对齐只发生在<b>已去空白的整数子集</b>上，超出该子集立刻分叉。
    /// </remarks>
    [Theory]
    [InlineData(" 502")]
    [InlineData("502 ")]
    [InlineData("  502  ")]
    [InlineData("\t502")]
    [InlineData("502\t")]
    public void RuntimeFallbackB_DoesNotFollowNumberStylesInteger_DivergesOnWhitespace(string rawPortValue)
    {
        // NumberStyles.Integer 含 AllowLeadingWhite/AllowTrailingWhite —— 这些值它全部接受
        Assert.True(
            long.TryParse(rawPortValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var viaNumberStyles),
            $"NumberStyles.Integer 应当接受带空白的 {JsonSerializer.Serialize(rawPortValue)}。");
        Assert.Equal(502, viaNumberStyles);

        // 而运行时兜底 B 对同样的值一律抛异常 —— 证明 B 不是按 NumberStyles.Integer 解析的
        var legacyJson = "{\"port\":" + JsonSerializer.Serialize(rawPortValue) + "}";
        Assert.ThrowsAny<JsonException>(
            () => JsonSerializer.Deserialize<ModbusTcpOptions>(legacyJson, AdapterOptions));

        // 归一化器（先 Trim 再按 NumberStyles.Integer 解析）则能救回来 —— 这正是 A 强于 B 之处
        var normalized = ProtocolConfigNormalizer.Normalize("modbustcp", legacyJson);
        var afterMigration = JsonSerializer.Deserialize<ModbusTcpOptions>(normalized!, AdapterOptions);
        Assert.NotNull(afterMigration);
        Assert.Equal(502, afterMigration!.Port);
    }

    /// <summary>
    /// 边界的另一侧，同样要防过度承诺：<b>A 也不是万能的</b>。
    /// 值本身就非法（<c>"50x2"</c>）、是小数（<c>"5000.5"</c>）、是科学计数法（<c>"1e3"</c>）、
    /// 或超出 Int32 范围（<c>"3000000000"</c>）时，A 与 B 都救不回来，必须人工介入。
    /// 这类行由 SQL 脚本阶段 3.2（仍为字符串）与阶段 3.6（超 Int32 范围）负责在 COMMIT 前暴露出来。
    /// </summary>
    [Theory]
    [InlineData("50x2")]
    [InlineData("5000.5")]
    [InlineData("1e3")]
    [InlineData("3000000000")]
    public void NeitherRuntimeFallbackNorMigration_RescuesGarbageOrOutOfRange(string rawPortValue)
    {
        var legacyJson = "{\"host\":\"1.2.3.4\",\"port\":" + JsonSerializer.Serialize(rawPortValue) + "}";

        // B 单独：抛异常
        Assert.ThrowsAny<JsonException>(
            () => JsonSerializer.Deserialize<ModbusTcpOptions>(legacyJson, AdapterOptions));

        // A 之后：依然抛异常（前三者保持字符串，最后一者被矫正成超范围数字）
        var normalized = ProtocolConfigNormalizer.Normalize("modbustcp", legacyJson);
        Assert.ThrowsAny<JsonException>(
            () => JsonSerializer.Deserialize<ModbusTcpOptions>(normalized!, AdapterOptions));
    }
}
