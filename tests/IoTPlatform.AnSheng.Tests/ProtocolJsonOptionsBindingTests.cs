using System.Text.Json;
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
}
