using System.Text.Json;
using System.Text.Json.Serialization;
using IoTPlatform.Infrastructure.Protocol.Adapters;
using Xunit;

namespace IoTPlatform.AnSheng.Tests;

/// <summary>
/// 协议连接配置「大小写不敏感绑定」回归验收。
///
/// 【守护的是哪个故障】
/// 协议连接配置（<c>ConnectionString</c> 字段里的那段 JSON）在入库时其键名<b>原样透传</b>，
/// 后端没有施加任何命名策略；而前端通用 MQTT 表单提交的是小写键 <c>host</c> / <c>port</c>。
/// 适配器侧读出后要反序列化成 PascalCase 的 <see cref="MqttProtocolOptions"/> /
/// <see cref="AnShengMqttProtocolOptions"/>，而 System.Text.Json <b>默认大小写敏感</b>。
///
/// 于是产生了一个典型的生产级<b>静默故障</b>：
///   小写键匹配不上 <c>Host</c>/<c>Port</c> → 反序列化<b>不报错</b>，字段保持 C# 初始化器给的默认值
///   → 适配器兴高采烈地去连 <c>localhost:1883</c> → 用户配的 broker 从没被使用过，日志里也没有任何线索。
///
/// 修复方式是给两个适配器的 <c>Deserialize</c> 传入共享的
/// <see cref="ProtocolJsonOptions.CaseInsensitive"/>（<c>PropertyNameCaseInsensitive = true</c>），
/// 小写键与存量 PascalCase 键同时可绑定，因此<b>存量数据无需迁移</b>。
///
/// 【为什么这些断言长这样】
/// 本类里每个「小写键」用例都<b>额外显式断言结果不等于默认值</b>（localhost / 1883）。
/// 这不是冗余：绑定失败时的表现恰恰是「悄悄等于默认值」，只断言 <c>Equal(期望值)</c> 固然也能挂，
/// 但把 <c>NotEqual(默认值)</c> 写出来，是让后来者一眼看懂这个测试在防什么样的回归。
/// </summary>
public class ProtocolJsonOptionsBindingTests
{
    /// <summary>前端表单里用户真实填写的 broker，刻意与默认值完全不同。</summary>
    private const string ExpectedHost = "10.0.0.5";

    /// <summary>刻意选 8883（MQTT over TLS 常用端口），与默认 1883 不同。</summary>
    private const int ExpectedPort = 8883;

    // ─────────────────────────────────────────────────────────────
    // 组 0：共享选项本身的配置
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 共享单例必须真的开启了大小写不敏感。
    /// 这条是「元断言」：如果哪天有人把这个开关关掉，下面所有用例会一起红，
    /// 但这条会直接指出根因在选项本身，而不是在某个 DTO。
    /// </summary>
    [Fact]
    public void CaseInsensitiveOptions_HasPropertyNameCaseInsensitiveEnabled()
    {
        Assert.NotNull(ProtocolJsonOptions.CaseInsensitive);
        Assert.True(
            ProtocolJsonOptions.CaseInsensitive.PropertyNameCaseInsensitive,
            "ProtocolJsonOptions.CaseInsensitive 必须开启 PropertyNameCaseInsensitive，否则小写键配置会静默回落默认值。");
    }

    // ─────────────────────────────────────────────────────────────
    // 组 1：用例 A —— 核心回归（通用 MQTT，小写键）
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 用例 A：前端通用 MQTT 表单写入的小写 <c>host</c>/<c>port</c> 必须能绑定。
    /// 这是本次修复的核心断言 —— 修复前此用例会得到 localhost:1883。
    /// </summary>
    [Fact]
    public void MqttProtocolOptions_LowerCaseKeys_BindsToHostAndPort()
    {
        // Arrange：前端通用 MQTT 表单实际落库的形态
        const string json = """{"host":"10.0.0.5","port":8883}""";

        // Act
        var options = JsonSerializer.Deserialize<MqttProtocolOptions>(json, ProtocolJsonOptions.CaseInsensitive);

        // Assert
        Assert.NotNull(options);
        Assert.Equal(ExpectedHost, options!.Host);
        Assert.Equal(ExpectedPort, options.Port);

        // 关键：证明拿到的不是「静默回落」的默认值
        Assert.NotEqual("localhost", options.Host);
        Assert.NotEqual(1883, options.Port);
    }

    /// <summary>
    /// 大小写不敏感不能只对 host/port 生效 —— 凭据与客户端前缀同样来自前端小写键，
    /// 绑定失败会导致「连上了但认证失败」或「clientId 撞车被 broker 踢」这类同样难查的问题。
    /// </summary>
    [Fact]
    public void MqttProtocolOptions_LowerCaseKeys_BindsCredentialsAndClientPrefix()
    {
        const string json = """
            {"host":"10.0.0.5","port":8883,"username":"iot","password":"s3cret","clientIdPrefix":"web_form","cleanSession":false,"qosLevel":2}
            """;

        var options = JsonSerializer.Deserialize<MqttProtocolOptions>(json, ProtocolJsonOptions.CaseInsensitive);

        Assert.NotNull(options);
        Assert.Equal("iot", options!.Username);
        Assert.Equal("s3cret", options.Password);
        Assert.Equal("web_form", options.ClientIdPrefix);
        Assert.False(options.CleanSession);   // 默认 true，能翻成 false 才说明真绑上了
        Assert.Equal(2, options.QosLevel);    // 默认 1
    }

    // ─────────────────────────────────────────────────────────────
    // 组 2：用例 B —— 存量 PascalCase 数据兼容（免迁移的凭据）
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 用例 B：存量库里已有的 PascalCase 配置必须<b>照旧</b>绑定成功。
    /// 这是「修复不得引入新回归」的护栏 —— 若为了兼容小写而改成强制 camelCase 命名策略，
    /// 这条会立刻红。存量数据无需迁移，正是靠大小写不敏感（而非命名策略）实现的。
    /// </summary>
    [Fact]
    public void MqttProtocolOptions_PascalCaseKeys_StillBinds()
    {
        const string json = """{"Host":"10.0.0.5","Port":8883}""";

        var options = JsonSerializer.Deserialize<MqttProtocolOptions>(json, ProtocolJsonOptions.CaseInsensitive);

        Assert.NotNull(options);
        Assert.Equal(ExpectedHost, options!.Host);
        Assert.Equal(ExpectedPort, options.Port);
        Assert.NotEqual("localhost", options.Host);
        Assert.NotEqual(1883, options.Port);
    }

    /// <summary>
    /// 手写配置 / 第三方导入常出现混合大小写，一并覆盖，确保不是只支持「全小写」和「全 Pascal」两种形态。
    /// </summary>
    [Theory]
    [InlineData("""{"host":"10.0.0.5","Port":8883}""")]
    [InlineData("""{"Host":"10.0.0.5","port":8883}""")]
    [InlineData("""{"HOST":"10.0.0.5","PORT":8883}""")]
    public void MqttProtocolOptions_MixedCaseKeys_Bind(string json)
    {
        var options = JsonSerializer.Deserialize<MqttProtocolOptions>(json, ProtocolJsonOptions.CaseInsensitive);

        Assert.NotNull(options);
        Assert.Equal(ExpectedHost, options!.Host);
        Assert.Equal(ExpectedPort, options.Port);
    }

    // ─────────────────────────────────────────────────────────────
    // 组 3：用例 C —— 安圣适配器同样受益且不受损
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 用例 C：安圣 MQTT 配置在小写与 PascalCase 两种键形态下都要绑定成功。
    /// 安圣现网数据是 PascalCase，本用例的 PascalCase 分支就是「安圣不被这次修复波及」的证明。
    /// </summary>
    [Theory]
    [InlineData("""{"host":"broker.x","port":1234}""")]
    [InlineData("""{"Host":"broker.x","Port":1234}""")]
    public void AnShengMqttProtocolOptions_EitherCasing_BindsToHostAndPort(string json)
    {
        var options = JsonSerializer.Deserialize<AnShengMqttProtocolOptions>(json, ProtocolJsonOptions.CaseInsensitive);

        Assert.NotNull(options);
        Assert.Equal("broker.x", options!.Host);
        Assert.Equal(1234, options.Port);

        // 同样显式排除静默回落
        Assert.NotEqual("localhost", options.Host);
        Assert.NotEqual(1883, options.Port);
    }

    /// <summary>
    /// 安圣配置里未出现在 JSON 中的字段，必须保留各自的默认值 —— 大小写不敏感只影响<b>键匹配</b>，
    /// 不应该顺带把没提到的字段清成 null/0。这是对「修复副作用」的兜底检查。
    /// </summary>
    [Fact]
    public void AnShengMqttProtocolOptions_UnspecifiedFields_KeepDefaults()
    {
        const string json = """{"host":"broker.x","port":1234}""";

        var options = JsonSerializer.Deserialize<AnShengMqttProtocolOptions>(json, ProtocolJsonOptions.CaseInsensitive);

        Assert.NotNull(options);
        Assert.Equal("iot_platform_ansheng", options!.ClientIdPrefix);
        Assert.Equal(100, options.CommandMinIntervalMs);
        Assert.Equal("/iot/server/iot-board/+", options.PublishTopicPattern);
        Assert.Equal("/iot/client/iot-board/{imei}", options.SubscribeTopicTemplate);
        Assert.NotNull(options.DefaultAutoReport);
    }

    /// <summary>
    /// 大小写不敏感必须<b>递归</b>作用到嵌套对象。安圣的 <c>DefaultAutoReport</c> 是嵌套 DTO，
    /// 若只有顶层生效，自动上报间隔会静默沿用默认 60s/300s，属于同一类静默故障。
    /// </summary>
    [Fact]
    public void AnShengMqttProtocolOptions_NestedObjectLowerCaseKeys_Bind()
    {
        const string json = """
            {"host":"broker.x","port":1234,"defaultAutoReport":{"getDevStatusSec":15,"orderUpSec":90,"rs485Sec":30}}
            """;

        var options = JsonSerializer.Deserialize<AnShengMqttProtocolOptions>(json, ProtocolJsonOptions.CaseInsensitive);

        Assert.NotNull(options);
        Assert.NotNull(options!.DefaultAutoReport);
        Assert.Equal(15, options.DefaultAutoReport.GetDevStatusSec);   // 默认 60
        Assert.Equal(90, options.DefaultAutoReport.OrderUpSec);        // 默认 300
        Assert.Equal(30, options.DefaultAutoReport.Rs485Sec);          // 默认 0
    }

    // ─────────────────────────────────────────────────────────────
    // 组 4：故障特征刻画（证明上面的用例不是「本来就会过」）
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 刻画修复<b>之前</b>的真实行为：用 System.Text.Json 的默认（大小写敏感）选项解析小写键，
    /// 不抛异常、不报错，而是安静地回落到 localhost:1883。
    ///
    /// 【这条测试的意义】
    /// 它证明用例 A 的通过<b>确实归功于 <see cref="ProtocolJsonOptions.CaseInsensitive"/></b>，
    /// 而不是因为 System.Text.Json 本来就宽容 —— 没有这条，用例 A 有沦为「假阳性绿灯」的风险。
    /// 同时它把「静默失败」这个故障形态固化成可执行文档。
    /// </summary>
    [Fact]
    public void DefaultOptions_LowerCaseKeys_SilentlyFallBackToDefaults_DocumentsTheBug()
    {
        const string json = """{"host":"10.0.0.5","port":8883}""";

        // 刻意使用默认选项（= 修复前适配器的行为）
        var options = JsonSerializer.Deserialize<MqttProtocolOptions>(json);

        Assert.NotNull(options);
        Assert.Equal("localhost", options!.Host);  // 用户配的 10.0.0.5 被无声丢弃
        Assert.Equal(1883, options.Port);          // 用户配的 8883 被无声丢弃
    }

    // ═════════════════════════════════════════════════════════════
    // 组 5：Modbus TCP —— 同一故障在非 MQTT 协议上的复现与修复验收
    //
    // MQTT 之外的三个适配器（ModbusTCP / ModbusRTU / OPC UA）此前同样使用默认
    // 大小写敏感选项，属于同一个故障家族。本组及组 6、组 7 是这三处修复的验收。
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// 小写 <c>host</c>/<c>port</c> 必须绑定到 <see cref="ModbusTcpOptions"/>。
    /// 修复前：静默回落 localhost:502 —— 适配器会去连一个根本不存在的本机 Modbus 从站，
    /// 表现为「配置明明填对了却一直连不上」，且日志打印的也是 localhost，极难定位。
    /// </summary>
    [Fact]
    public void ModbusTcpOptions_LowerCaseKeys_BindsToHostAndPort()
    {
        // Arrange
        const string json = """{"host":"10.0.0.9","port":5502}""";

        // Act
        var options = JsonSerializer.Deserialize<ModbusTcpOptions>(json, ProtocolJsonOptions.CaseInsensitive);

        // Assert
        Assert.NotNull(options);
        Assert.Equal("10.0.0.9", options!.Host);
        Assert.Equal(5502, options.Port);

        // 关键：证明拿到的不是「静默回落」的默认值
        Assert.NotEqual("localhost", options.Host);
        Assert.NotEqual(502, options.Port);
    }

    /// <summary>
    /// 存量 PascalCase 的 Modbus TCP 配置必须照旧绑定。
    /// 护栏用途：若有人为了「统一命名」给这些 DTO 挂上强制 camelCase 命名策略，这条会立刻红。
    /// </summary>
    [Fact]
    public void ModbusTcpOptions_PascalCaseKeys_StillBinds()
    {
        const string json = """{"Host":"10.0.0.9","Port":5502}""";

        var options = JsonSerializer.Deserialize<ModbusTcpOptions>(json, ProtocolJsonOptions.CaseInsensitive);

        Assert.NotNull(options);
        Assert.Equal("10.0.0.9", options!.Host);
        Assert.Equal(5502, options.Port);
        Assert.NotEqual("localhost", options.Host);
        Assert.NotEqual(502, options.Port);
    }

    // ═════════════════════════════════════════════════════════════
    // 组 6：Modbus RTU（串口参数，绑定失败的后果比 TCP 更隐蔽）
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// 小写 <c>portName</c>/<c>baudRate</c> 必须绑定到 <see cref="ModbusRtuOptions"/>。
    /// 修复前：静默回落 COM1 / 9600。串口场景下这尤其致命 ——
    /// 打开错误的串口可能「成功」（机器上恰好存在 COM1），于是变成「连上了但一个字节也读不到」，
    /// 或波特率不匹配导致收到一堆乱码，排查方向会被完全带偏。
    /// </summary>
    [Fact]
    public void ModbusRtuOptions_LowerCaseKeys_BindsToPortNameAndBaudRate()
    {
        // Arrange
        const string json = """{"portName":"COM3","baudRate":19200}""";

        // Act
        var options = JsonSerializer.Deserialize<ModbusRtuOptions>(json, ProtocolJsonOptions.CaseInsensitive);

        // Assert
        Assert.NotNull(options);
        Assert.Equal("COM3", options!.PortName);
        Assert.Equal(19200, options.BaudRate);

        // 关键：证明拿到的不是「静默回落」的默认值
        Assert.NotEqual("COM1", options.PortName);
        Assert.NotEqual(9600, options.BaudRate);
    }

    /// <summary>存量 PascalCase 的 Modbus RTU 配置必须照旧绑定（命名策略护栏）。</summary>
    [Fact]
    public void ModbusRtuOptions_PascalCaseKeys_StillBinds()
    {
        const string json = """{"PortName":"COM3","BaudRate":19200}""";

        var options = JsonSerializer.Deserialize<ModbusRtuOptions>(json, ProtocolJsonOptions.CaseInsensitive);

        Assert.NotNull(options);
        Assert.Equal("COM3", options!.PortName);
        Assert.Equal(19200, options.BaudRate);
        Assert.NotEqual("COM1", options.PortName);
        Assert.NotEqual(9600, options.BaudRate);
    }

    /// <summary>
    /// RTU 未在 JSON 中出现的串口参数必须保留默认值 —— 大小写不敏感只影响<b>键匹配</b>，
    /// 不应顺带把没提到的字段清成 null/0（0 数据位 / 空校验位会直接让 SerialPort 构造失败）。
    /// </summary>
    [Fact]
    public void ModbusRtuOptions_UnspecifiedFields_KeepDefaults()
    {
        const string json = """{"portName":"COM3","baudRate":19200}""";

        var options = JsonSerializer.Deserialize<ModbusRtuOptions>(json, ProtocolJsonOptions.CaseInsensitive);

        Assert.NotNull(options);
        Assert.Equal(8, options!.DataBits);
        Assert.Equal("1", options.StopBits);
        Assert.Equal("None", options.Parity);
        Assert.Equal(3000, options.TimeoutMs);
    }

    // ═════════════════════════════════════════════════════════════
    // 组 7：OPC UA
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// 小写 <c>endpointUrl</c> 必须绑定到 <see cref="OpcUaOptions"/>。
    /// 修复前：静默回落 <c>opc.tcp://localhost:4840</c>。
    /// </summary>
    [Fact]
    public void OpcUaOptions_LowerCaseKeys_BindsToEndpointUrl()
    {
        // Arrange
        const string json = """{"endpointUrl":"opc.tcp://10.0.0.7:4840"}""";

        // Act
        var options = JsonSerializer.Deserialize<OpcUaOptions>(json, ProtocolJsonOptions.CaseInsensitive);

        // Assert
        Assert.NotNull(options);
        Assert.Equal("opc.tcp://10.0.0.7:4840", options!.EndpointUrl);

        // 关键：证明拿到的不是「静默回落」的默认值
        Assert.NotEqual("opc.tcp://localhost:4840", options.EndpointUrl);
    }

    /// <summary>存量 PascalCase 的 OPC UA 配置必须照旧绑定（命名策略护栏）。</summary>
    [Fact]
    public void OpcUaOptions_PascalCaseKeys_StillBinds()
    {
        const string json = """{"EndpointUrl":"opc.tcp://10.0.0.7:4840"}""";

        var options = JsonSerializer.Deserialize<OpcUaOptions>(json, ProtocolJsonOptions.CaseInsensitive);

        Assert.NotNull(options);
        Assert.Equal("opc.tcp://10.0.0.7:4840", options!.EndpointUrl);
        Assert.NotEqual("opc.tcp://localhost:4840", options.EndpointUrl);
    }

    // ═════════════════════════════════════════════════════════════
    // 组 8：故障特征刻画 —— 证明组 5~7 的绿灯确实归功于本次修复
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// 用默认（大小写敏感）选项解析小写键，三个协议一律静默回落默认值、且<b>不抛异常</b>。
    ///
    /// 【这条测试的意义】
    /// 与 <see cref="DefaultOptions_LowerCaseKeys_SilentlyFallBackToDefaults_DocumentsTheBug"/> 同理：
    /// 没有它，组 5~7 有沦为「假阳性绿灯」的风险 —— 无法区分「修复生效」与「System.Text.Json 本来就宽容」。
    /// </summary>
    [Fact]
    public void DefaultOptions_LowerCaseKeys_AllThreeProtocolsSilentlyFallBack_DocumentsTheBug()
    {
        // 刻意使用默认选项（= 修复前三个适配器的行为）
        var tcp = JsonSerializer.Deserialize<ModbusTcpOptions>("""{"host":"10.0.0.9","port":5502}""");
        var rtu = JsonSerializer.Deserialize<ModbusRtuOptions>("""{"portName":"COM3","baudRate":19200}""");
        var opc = JsonSerializer.Deserialize<OpcUaOptions>("""{"endpointUrl":"opc.tcp://10.0.0.7:4840"}""");

        Assert.NotNull(tcp);
        Assert.Equal("localhost", tcp!.Host);   // 用户配的 10.0.0.9 被无声丢弃
        Assert.Equal(502, tcp.Port);

        Assert.NotNull(rtu);
        Assert.Equal("COM1", rtu!.PortName);    // 用户配的 COM3 被无声丢弃
        Assert.Equal(9600, rtu.BaudRate);

        Assert.NotNull(opc);
        Assert.Equal("opc.tcp://localhost:4840", opc!.EndpointUrl);
    }

    // ═════════════════════════════════════════════════════════════
    // 组 9：方案 B 落地后的数值绑定契约 —— 数字字段可从字符串读取，但非法字符串仍硬失败
    //
    // 此前的组 9 守护「大小写不敏感 ≠ 类型宽容」：字符串型数字会抛 JsonException。
    // 方案 B（用户已批准 A+B 并行）采纳后，共享单例追加了 NumberHandling = AllowReadingFromString，
    // 契约<b>有意反转</b>——本组改为守护新契约：字符串数字被救回（存量倒挂：修复前静默回落、
    // 修复后连接失败且不需用户编辑就触发），但垃圾值仍被暴露。
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// 共享单例在开启 <c>NumberHandling = AllowReadingFromString</c> 后，<b>会</b>把 JSON 字符串形态的数字字段
    /// 绑成目标数值类型——这正是方案 B 要兜底的存量遗留问题
    /// （旧前端 <c>&lt;input type="number"&gt;</c> 未经 parseInt，把 <c>e.target.value</c> 字符串直接写进 config）。
    /// 这条断言原本会因「加上 AllowReadingFromString 会变红」而被设计为护栏；方案 B 落地后，它转为守护
    /// 「B 确实在生效」的正向契约。契约反转理由：存量数据倒挂——修复前这些行是「静默回落 localhost:502」
    /// （连错目标但连得上），修复后变成「连接失败」，且<b>不需要用户编辑就触发</b>（键就躺在 DB 里）；
    /// 用户已明确批准 A（SQL 清洗）+ B（运行时兜底）并行。
    /// </summary>
    [Fact]
    public void CaseInsensitiveOptions_CoercesStringTypedPort_LegacyDataRescue()
    {
        // 字符串型端口：键名匹配上，且值被从字符串解析为 int
        const string stringPortJson = """{"Host":"10.0.0.9","Port":"5502"}""";

        var options = JsonSerializer.Deserialize<ModbusTcpOptions>(stringPortJson, ProtocolJsonOptions.CaseInsensitive);

        Assert.NotNull(options);
        Assert.Equal(5502, options!.Port);   // 方案 B 前这里会抛 JsonException

        // 对照组：同样的值以 JSON number 形态落库依然正常（B 没有破坏原有正常路径）
        var ok = JsonSerializer.Deserialize<ModbusTcpOptions>(
            """{"Host":"10.0.0.9","Port":5502}""", ProtocolJsonOptions.CaseInsensitive);

        Assert.NotNull(ok);
        Assert.Equal(5502, ok!.Port);
    }

    /// <summary>
    /// 正常数字（PascalCase 键 + JSON number）仍然是方案 B 下的首要路径，必须不受任何影响。
    /// 这条把「B 只救字符串数字、不动正常数字」显式固化下来，也是任务要求的
    /// <c>{"Port":502}</c> → <c>Port == 502</c> 回归断言。
    /// </summary>
    [Fact]
    public void CaseInsensitiveOptions_ScalarNumericPort_PascalCase_Binds()
    {
        const string json = """{"Port":502}""";

        var options = JsonSerializer.Deserialize<ModbusTcpOptions>(json, ProtocolJsonOptions.CaseInsensitive);

        Assert.NotNull(options);
        Assert.Equal(502, options!.Port);
    }

    /// <summary>
    /// <c>AllowReadingFromString</c> 放宽的是「字符串 → 数字」的<b>合法</b>转换；<b>非法</b>字符串（如
    /// <c>"abc"</c>）无法解析为数字，仍必须抛 <see cref="JsonException"/>，<b>不得</b>被静默吞掉。
    /// 这是方案 B 不能退让的底线：兜底是为了救「类型写串了」的存量脏数据，不是给「值本身非法」放行。
    /// </summary>
    [Fact]
    public void CaseInsensitiveOptions_IllegalStringValue_StillThrows()
    {
        const string badJson = """{"host":"10.0.0.9","port":"abc"}""";

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<ModbusTcpOptions>(badJson, ProtocolJsonOptions.CaseInsensitive));
    }

    // ═════════════════════════════════════════════════════════════
    // 组 10：存量小写数据的「类型」遗留问题，及方案 B 的兜底效果
    //
    // 方案 B 落地后，共享单例现已<b>放宽</b>数值类型（AllowReadingFromString）。本组把这个结论套到
    // <b>存量库数据</b>上：旧前端 <c>setConfigValue('port', e.target.value)</c> 直接把 input 的 string 写入，
    // 因此存量库里的小写键很可能长这样：<c>{"host":"10.0.0.9","port":"502"}</c>。
    // 这类数据在方案 B 落地前会「抛异常」，落地后被「救回」——这正是 B 要补的运行时兜底。
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// 存量小写 <c>port</c> 若为<b>字符串</b>值，方案 B 的 <c>AllowReadingFromString</c> 会把它<b>救回</b>——
    /// 不再抛异常，正确绑定为数值。这是 B 相对「纯前端补丁」的<b>唯一不可替代之处</b>：
    /// 该类配置从未经过任何前端代码路径，纯前端永远碰不到它。
    ///
    /// 同一份数据在「修复前」的大小写敏感默认选项下仍会静默回落默认值（见下方对照组），
    /// 与 B 的「正确绑定」形成对照——说明 B 真正改变了存量行的命运，而不是无中生有。
    /// </summary>
    [Fact]
    public void LegacyLowerCaseStringTypedPort_RescuedByAllowReadingFromString_NoLongerThrows()
    {
        // 旧前端写入形态：端口是字符串
        const string legacyJson = """{"host":"10.0.0.9","port":"502"}""";

        var options = JsonSerializer.Deserialize<ModbusTcpOptions>(legacyJson, ProtocolJsonOptions.CaseInsensitive);

        Assert.NotNull(options);
        Assert.Equal("10.0.0.9", options!.Host);
        Assert.Equal(502, options.Port);
        Assert.NotEqual("localhost", options.Host);

        // 对照：同一份存量数据在「修复前」的大小写敏感默认选项下不抛异常，而是静默回落默认值
        var silent = JsonSerializer.Deserialize<ModbusTcpOptions>(legacyJson);
        Assert.NotNull(silent);
        Assert.Equal("localhost", silent!.Host);
        Assert.Equal(502, silent.Port);
    }

    /// <summary>
    /// 存量小写键为<b>数字</b>时，是本次修复的纯收益场景：由静默回落变为正确绑定。
    /// 与上一条配对，说明「存量小写数据」并非一概有风险，只有<b>字符串型数字字段</b>才有。
    /// </summary>
    [Fact]
    public void LegacyLowerCaseNumericPort_BindsCorrectly_NoRisk()
    {
        const string legacyJson = """{"host":"10.0.0.9","port":502}""";

        var options = JsonSerializer.Deserialize<ModbusTcpOptions>(legacyJson, ProtocolJsonOptions.CaseInsensitive);

        Assert.NotNull(options);
        Assert.Equal("10.0.0.9", options!.Host);
        Assert.Equal(502, options.Port);
        Assert.NotEqual("localhost", options.Host);
    }

    /// <summary>
    /// 用户编辑存量小写配置并保存后，config 里会<b>同时</b>存在旧小写键与新 PascalCase 键。
    /// 前端 <c>{...f.config, Port: x}</c> 的展开顺序决定了 JSON 中<b>旧键在前、新键在后</b>
    /// （已用 Node 实测：<c>{"host":"10.0.0.9","port":"502","Port":5502}</c>）。
    ///
    /// 【关键结论】
    /// System.Text.Json 是<b>流式顺序处理</b>：读到第一个能匹配 <c>Port</c> 的键就尝试赋值。
    /// 因此旧的字符串键会<b>先</b>触发异常，后面那个正确的 number 键<b>根本没机会被读到</b>——
    /// 「新键在后所以安全」的直觉在这里<b>不成立</b>。
    /// </summary>
    [Fact]
    public void LegacyStringPort_FollowedByNewNumericPort_RescuedThenOverridden()
    {
        // 前端合并后的真实形态：旧 string 键在前，新 number 键在后
        const string mergedJson = """{"host":"10.0.0.9","port":"502","Port":5502}""";

        // 方案 B 下，旧字符串键 "502" 能被正常解析（不再抛异常），随后被后到的正确 number 键覆盖——
        // 用户新填的值胜出。原风险（STJ 流式处理：旧键先触异常、后面的键没机会读）已被方案 B 消除。
        var options = JsonSerializer.Deserialize<ModbusTcpOptions>(mergedJson, ProtocolJsonOptions.CaseInsensitive);

        Assert.NotNull(options);
        Assert.Equal(5502, options!.Port);   // 用户新填的 PascalCase 值胜出
    }

    /// <summary>
    /// 若新旧两个键<b>都是数字</b>，则不抛异常，且遵循「后者覆盖前者」——
    /// 用户新填的 PascalCase 值胜出，这是期望行为。
    /// 与上一条对比可见：重复键本身无害，<b>有害的是旧键的字符串类型</b>。
    /// </summary>
    [Fact]
    public void DuplicateCaseVariantKeys_BothNumeric_LastOccurrenceWins()
    {
        const string mergedJson = """{"host":"10.0.0.9","port":502,"Port":5502}""";

        var options = JsonSerializer.Deserialize<ModbusTcpOptions>(mergedJson, ProtocolJsonOptions.CaseInsensitive);

        Assert.NotNull(options);
        Assert.Equal(5502, options!.Port);   // 用户新填的值胜出
    }

    // ═════════════════════════════════════════════════════════════
    // 组 11：方案 B 的<b>正式回归断言</b>（运行时兜底行为守护）
    //
    // 决策阶段本组曾是「用局部等价选项做可行性验证」的探索性用例。方案 B 已落地——
    // <see cref="ProtocolJsonOptions.CaseInsensitive"/> 现在<b>真的</b>配置了
    // NumberHandling = AllowReadingFromString。因此本组转正为正式回归，
    // 直接断言共享单例的真实行为，删除了原先的 OptionBCandidate 局部副本（测试就该测真东西）。
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// 共享单例能救回「从未被编辑过」的存量字符串端口数据 —— 这是 B 相对其他方案的<b>唯一不可替代之处</b>：
    /// 该类配置不经过任何前端代码路径，纯前端补丁永远碰不到它。
    /// 即任务要求的 <c>{"port":"502"}</c> → <c>Port == 502</c> 回归断言。
    /// </summary>
    [Fact]
    public void CaseInsensitiveOptions_RescuesNeverEditedLegacyStringPort()
    {
        const string legacyJson = """{"host":"10.0.0.9","port":"502"}""";

        var options = JsonSerializer.Deserialize<ModbusTcpOptions>(legacyJson, ProtocolJsonOptions.CaseInsensitive);

        Assert.NotNull(options);
        Assert.Equal("10.0.0.9", options!.Host);
        Assert.Equal(502, options.Port);
        Assert.NotEqual("localhost", options.Host);
    }

    /// <summary>
    /// 新旧键共存（旧 string 键在前，新 number 键在后）不再是连接中断级故障：
    /// 旧字符串键 <c>"502"</c> 被正常解析，随后被后到的正确 number 键覆盖，用户新填的值胜出。
    /// </summary>
    [Fact]
    public void CaseInsensitiveOptions_MakesDuplicateLegacyAndNewKeysHarmless()
    {
        // 用户重填表单后的真实形态：旧 string 键在前，新 number 键在后
        const string mergedJson = """{"host":"10.0.0.9","port":"502","Port":5502}""";

        var options = JsonSerializer.Deserialize<ModbusTcpOptions>(mergedJson, ProtocolJsonOptions.CaseInsensitive);

        Assert.NotNull(options);
        Assert.Equal(5502, options!.Port);
    }

    /// <summary>
    /// JSON <c>null</c> 会<b>覆盖</b>掉 C# 属性初始化器给的默认值，而不是被忽略。
    /// 这条与「是否放宽数值类型」无关，属独立的边界刻画，本轮未发生任何改动，保留为回归守护。
    /// </summary>
    [Fact]
    public void ExplicitJsonNull_OverridesPropertyInitializerDefault_NotIgnored()
    {
        // 单独的 null：是否回落到初始化器的 "localhost"？
        var nullOnly = JsonSerializer.Deserialize<ModbusTcpOptions>(
            """{"Host":null}""", ProtocolJsonOptions.CaseInsensitive);

        Assert.NotNull(nullOnly);
        Assert.Null(nullOnly!.Host);                  // null 覆盖了初始化器，未回落 localhost
        Assert.NotEqual("localhost", nullOnly.Host);

        // 新旧键共存：小写键先绑上真值，随后 null 再把它覆盖掉
        var legacyThenNull = JsonSerializer.Deserialize<ModbusTcpOptions>(
            """{"host":"10.0.0.9","Host":null}""", ProtocolJsonOptions.CaseInsensitive);

        Assert.NotNull(legacyThenNull);
        Assert.Null(legacyThenNull!.Host);            // 旧键的 10.0.0.9 被 null 抹掉
    }

    /// <summary>
    /// 方案 B 的副作用边界（正式回归版）：<see cref="JsonNumberHandling.AllowReadingFromString"/> 只放宽
    /// <b>数字</b>字段，<b>不</b>放宽布尔字段。数字字符串被正确解析，而布尔字符串 <c>"true"</c> 仍抛
    /// <see cref="JsonException"/>。即任务要求的「布尔不被连带放松」断言——这条必须打在真实单例上才有意义。
    /// </summary>
    [Fact]
    public void CaseInsensitiveOptions_DoesNotRelaxBooleanFields_DocumentsScopeOfTheRelaxation()
    {
        // 数字字段：被放宽
        var numeric = JsonSerializer.Deserialize<ModbusTcpOptions>(
            """{"timeoutMs":"7000"}""", ProtocolJsonOptions.CaseInsensitive);
        Assert.NotNull(numeric);
        Assert.Equal(7000, numeric!.TimeoutMs);

        // 布尔字段：未被放宽，仍抛异常（以 OpcUaOptions.UsePollingMode 为例）
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<OpcUaOptions>("""{"usePollingMode":"true"}""", ProtocolJsonOptions.CaseInsensitive));
    }
}
