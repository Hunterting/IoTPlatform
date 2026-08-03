using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using IoTPlatform.Infrastructure.Protocol.AnSheng;
using IoTPlatform.Infrastructure.Protocol.Adapters;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Diagnostics;
using Xunit;

namespace IoTPlatform.AnSheng.Tests;

/// <summary>
/// T4 止血验收：<see cref="AnShengMqttProtocolAdapter"/> 下行分支「默认拒绝、显式放行」。
///
/// 背景：历史实现在 method 未命中 <see cref="AnShengCommandCatalog"/> 时<b>无条件</b>走
/// <c>BuildLegacyCommand</c> 兜底并真实 publish，导致任意协议外方法（含前端臆造的伪命令）
/// 都会被外发到现网设备。T4 引入 <c>LegacyMethodWhitelist</c> 阻断该路径。
///
/// 本测试类通过反射注入 fake <see cref="IMqttClient"/>（<c>IsConnected == true</c>）
/// 真实驱动 <c>SendCommandAsync</c>，覆盖三条分支的后两条：
///   · 用例 A：白名单命中 → 走 Legacy 报文（<c>param</c> 包裹）并抵达 publish；
///   · 用例 B：协议外方法 → 抛 <see cref="NotSupportedException"/> 且 publish <b>零次</b>调用。
/// 不修改任何生产代码。
/// </summary>
public class AnShengLegacyWhitelistTests
{
    private const string TestImei = "864536072949900";

    // ─────────────────────────────────────────────────────────────
    // 测试脚手架
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 构造一个「已连接」的适配器：反射注入 fake MQTT 客户端与配置项，
    /// 使 <c>SendCommandAsync</c> 能越过连接性前置校验、真正执行分支选择逻辑。
    /// </summary>
    private static (AnShengMqttProtocolAdapter Adapter, FakeMqttClient Client) CreateConnectedAdapter()
    {
        var adapter = new AnShengMqttProtocolAdapter(configId: 1);
        var client = new FakeMqttClient();

        SetPrivateField(adapter, "_mqttClient", client);
        SetPrivateField(adapter, "_options", new AnShengMqttProtocolOptions());

        return (adapter, client);
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.True(field != null, $"私有字段 {fieldName} 不存在，测试脚手架需同步更新");
        field!.SetValue(target, value);
    }

    /// <summary>反射读取适配器内部的 Legacy 白名单，避免为测试改动生产代码可见性。</summary>
    private static HashSet<string> ReadWhitelist()
    {
        var field = typeof(AnShengMqttProtocolAdapter)
            .GetField("LegacyMethodWhitelist", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.True(field != null, "未找到 LegacyMethodWhitelist 字段——T4 白名单加固可能已被回退");

        var value = Assert.IsType<HashSet<string>>(field!.GetValue(null));
        return value;
    }

    // ─────────────────────────────────────────────────────────────
    // 用例 A：白名单命中 → Legacy 报文（param 包裹）放行
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 白名单成员 <c>orderStart</c> 未登记于二开目录，必须走 Legacy 分支：
    /// 报文含 <c>param</c> 包裹、毫秒字符串 <c>timestamp</c>，并真实抵达 publish。
    /// </summary>
    [Fact]
    public async Task Whitelisted_Legacy_Method_Should_Be_Published_With_Param_Wrapper()
    {
        // Arrange
        Assert.False(AnShengCommandCatalog.Contains("orderStart"),
            "前置条件：orderStart 不应在二开目录中，否则本用例走的是目录分支而非白名单分支");

        var (adapter, client) = CreateConnectedAdapter();

        // Act
        var frameId = await adapter.SendCommandAsync(
            deviceId: 1,
            serialNumber: TestImei,
            commandType: "orderStart",
            parameters: "{\"sn\":\"SN20250423001\",\"order\":1}");

        // Assert：确实 publish 了一条，且报文为 Legacy 结构
        Assert.Equal(1, client.PublishCount);
        Assert.NotNull(client.LastPayload);

        using var doc = JsonDocument.Parse(client.LastPayload!);
        var root = doc.RootElement;

        Assert.Equal("orderStart", root.GetProperty("method").GetString());
        Assert.Equal(TestImei, root.GetProperty("imei").GetString());
        Assert.Equal(frameId, root.GetProperty("frameId").GetString());

        // Legacy 特征 1：参数被 param 包裹（二开协议为顶层平铺）
        Assert.True(root.TryGetProperty("param", out var param), "Legacy 报文必须保留 param 包裹");
        Assert.Equal("SN20250423001", param.GetProperty("sn").GetString());
        Assert.Equal(1, param.GetProperty("order").GetInt32());
        Assert.False(root.TryGetProperty("sn", out _), "Legacy 报文不应把参数平铺到顶层");

        // Legacy 特征 2：timestamp 为毫秒字符串（二开为秒级 int 且仅 4G 注入）
        var ts = root.GetProperty("timestamp");
        Assert.Equal(JsonValueKind.String, ts.ValueKind);
        Assert.True(long.TryParse(ts.GetString(), out var ms) && ms > 1_000_000_000_000L,
            "Legacy timestamp 应为毫秒字符串");
    }

    /// <summary>白名单三个成员均可放行下发，行为与 T4 之前一致（回归保护）。</summary>
    [Theory]
    [InlineData("orderStart")]
    [InlineData("orderEnd")]
    [InlineData("orderUp")]
    public async Task All_Whitelisted_Methods_Should_Pass_Through(string method)
    {
        var (adapter, client) = CreateConnectedAdapter();

        var frameId = await adapter.SendCommandAsync(1, TestImei, method, "{\"sn\":\"SN001\"}");

        Assert.Equal(1, client.PublishCount);
        Assert.False(string.IsNullOrWhiteSpace(frameId));

        using var doc = JsonDocument.Parse(client.LastPayload!);
        Assert.Equal(method, doc.RootElement.GetProperty("method").GetString());
        Assert.True(doc.RootElement.TryGetProperty("param", out _));
    }

    // ─────────────────────────────────────────────────────────────
    // 用例 B：协议外方法 → NotSupportedException，且绝不 publish
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 既不在二开目录、也不在 Legacy 白名单的方法（含已删除的 4 个伪命令与任意伪造串），
    /// 必须在 publish <b>之前</b>抛出 <see cref="NotSupportedException"/>，
    /// 且底层 MQTT 客户端零调用——这是 T4 止血的核心断言。
    /// </summary>
    [Theory]
    [InlineData("setSwitch")]
    [InlineData("getSwitchStatus")]
    [InlineData("setSwitchConfig")]
    [InlineData("getSwitchConfig")]
    [InlineData("totallyBogusMethod_9f3a")]
    [InlineData("")]
    public async Task Non_Whitelisted_Method_Should_Throw_Before_Publish(string method)
    {
        // Arrange
        Assert.False(AnShengCommandCatalog.Contains(method), $"{method} 不应在二开目录中");
        Assert.DoesNotContain(method, ReadWhitelist());

        var (adapter, client) = CreateConnectedAdapter();

        // Act + Assert
        var ex = await Assert.ThrowsAsync<NotSupportedException>(
            () => adapter.SendCommandAsync(1, TestImei, method, "{\"switch\":1,\"on\":true}"));

        Assert.Contains("禁止下发", ex.Message);

        // 最关键的一条：绝不能有任何报文外发到现网设备
        Assert.Equal(0, client.PublishCount);
        Assert.Null(client.LastPayload);
    }

    /// <summary>大小写敏感（Ordinal）：<c>OrderStart</c> 不等于 <c>orderStart</c>，同样必须被拒绝。</summary>
    [Fact]
    public async Task Whitelist_Should_Be_Case_Sensitive()
    {
        var (adapter, client) = CreateConnectedAdapter();

        await Assert.ThrowsAsync<NotSupportedException>(
            () => adapter.SendCommandAsync(1, TestImei, "OrderStart", null!));

        Assert.Equal(0, client.PublishCount);
    }

    // ─────────────────────────────────────────────────────────────
    // 白名单成员约束（防止后续任务误把伪命令加回来）
    // ─────────────────────────────────────────────────────────────

    /// <summary>白名单必须恰好为 orderStart / orderEnd / orderUp 三个，且使用 Ordinal 比较器。</summary>
    [Fact]
    public void Whitelist_Should_Contain_Exactly_Three_Legacy_Methods()
    {
        var whitelist = ReadWhitelist();

        Assert.Equal(3, whitelist.Count);
        Assert.Equal(
            new[] { "orderEnd", "orderStart", "orderUp" },
            whitelist.OrderBy(m => m, StringComparer.Ordinal).ToArray());
        Assert.Same(StringComparer.Ordinal, whitelist.Comparer);
    }

    /// <summary>四个已删除的伪命令永远不得出现在白名单中（回归护栏）。</summary>
    [Theory]
    [InlineData("setSwitch")]
    [InlineData("getSwitchStatus")]
    [InlineData("setSwitchConfig")]
    [InlineData("getSwitchConfig")]
    public void Pseudo_Commands_Must_Never_Be_Whitelisted(string pseudoMethod)
    {
        Assert.DoesNotContain(pseudoMethod, ReadWhitelist());
        Assert.False(AnShengCommandCatalog.Contains(pseudoMethod));
    }

    /// <summary>
    /// 白名单不得与二开目录重复登记——重复项说明该方法应走目录分支，
    /// 留在白名单里会造成「同名两种报文结构」的歧义。
    /// </summary>
    [Fact]
    public void Whitelist_Should_Not_Overlap_With_Catalog()
    {
        var overlapped = ReadWhitelist().Where(AnShengCommandCatalog.Contains).ToArray();

        Assert.True(overlapped.Length == 0,
            $"白名单与二开目录重复登记：{string.Join(", ", overlapped)}");
    }

    // ─────────────────────────────────────────────────────────────
    // 目录分支回归：确保白名单加固没有影响已登记方法
    // ─────────────────────────────────────────────────────────────

    /// <summary>已登记于目录的方法（如 <c>getDevStatus</c>）仍走二开报文：参数平铺、无 param 包裹。</summary>
    [Fact]
    public async Task Catalog_Method_Should_Still_Use_Flat_OpenProtocol_Payload()
    {
        Assert.True(AnShengCommandCatalog.Contains("getDevStatus"));

        var (adapter, client) = CreateConnectedAdapter();

        await adapter.SendCommandAsync(1, TestImei, "getDevStatus", "{\"q\":\"slots\"}");

        Assert.Equal(1, client.PublishCount);
        using var doc = JsonDocument.Parse(client.LastPayload!);
        var root = doc.RootElement;

        Assert.Equal("getDevStatus", root.GetProperty("method").GetString());
        Assert.False(root.TryGetProperty("param", out _), "二开协议报文不应有 param 包裹");
        Assert.Equal("slots", root.GetProperty("q").GetString());
    }

    // ─────────────────────────────────────────────────────────────
    // Fake MQTT 客户端
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 最小化 <see cref="IMqttClient"/> 替身：只关心 <c>IsConnected</c> 与 <c>PublishAsync</c>，
    /// 记录调用次数与最后一次载荷，其余成员均不应被下发路径触达（触达即抛错暴露问题）。
    /// </summary>
    private sealed class FakeMqttClient : IMqttClient
    {
        public int PublishCount { get; private set; }
        public string? LastPayload { get; private set; }
        public string? LastTopic { get; private set; }

        public bool IsConnected => true;
        public MqttClientOptions Options { get; } = new MqttClientOptionsBuilder()
            .WithTcpServer("localhost", 1883)
            .Build();

        public event Func<MqttApplicationMessageReceivedEventArgs, Task>? ApplicationMessageReceivedAsync;
        public event Func<MqttClientConnectedEventArgs, Task>? ConnectedAsync;
        public event Func<MqttClientConnectingEventArgs, Task>? ConnectingAsync;
        public event Func<MqttClientDisconnectedEventArgs, Task>? DisconnectedAsync;
        public event Func<InspectMqttPacketEventArgs, Task>? InspectPacketAsync;

        public Task<MqttClientPublishResult> PublishAsync(
            MqttApplicationMessage applicationMessage, CancellationToken cancellationToken = default)
        {
            PublishCount++;
            LastTopic = applicationMessage.Topic;
            LastPayload = applicationMessage.PayloadSegment.Array == null
                ? string.Empty
                : Encoding.UTF8.GetString(
                    applicationMessage.PayloadSegment.Array,
                    applicationMessage.PayloadSegment.Offset,
                    applicationMessage.PayloadSegment.Count);

            // MqttClientPublishResult 无公开构造函数；测试只关心报文内容，创建未初始化实例满足签名即可
            var result = (MqttClientPublishResult)RuntimeHelpers
                .GetUninitializedObject(typeof(MqttClientPublishResult));
            return Task.FromResult(result);
        }

        public Task<MqttClientConnectResult> ConnectAsync(
            MqttClientOptions options, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("测试替身：下发路径不应触发 ConnectAsync");

        public Task DisconnectAsync(
            MqttClientDisconnectOptions options, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task PingAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SendExtendedAuthenticationExchangeDataAsync(
            MqttExtendedAuthenticationExchangeData data, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<MqttClientSubscribeResult> SubscribeAsync(
            MqttClientSubscribeOptions options, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("测试替身：下发路径不应触发 SubscribeAsync");

        public Task<MqttClientUnsubscribeResult> UnsubscribeAsync(
            MqttClientUnsubscribeOptions options, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("测试替身：下发路径不应触发 UnsubscribeAsync");

        public void Dispose()
        {
            // 显式引用事件字段，避免 CS0067「从未使用」警告
            _ = ApplicationMessageReceivedAsync;
            _ = ConnectedAsync;
            _ = ConnectingAsync;
            _ = DisconnectedAsync;
            _ = InspectPacketAsync;
        }
    }
}
