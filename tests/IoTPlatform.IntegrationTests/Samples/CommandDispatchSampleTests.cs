using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using FluentAssertions;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Helpers;
using IoTPlatform.IntegrationTests.Infrastructure;
using IoTPlatform.IntegrationTests.Infrastructure.Auth;
using IoTPlatform.IntegrationTests.Infrastructure.Mqtt;
using Xunit;

namespace IoTPlatform.IntegrationTests.Samples;

/// <summary>
/// 示例二：指令下发链路（架构方案 §5-S5）。
///
/// 【这个示例证明什么】
///   1. <c>IProtocolAdapterFactory</c> 是 MQTT 侧唯一有效的 DI 接缝——
///      替换它之后，<c>AnShengCommandService</c> 全程走真实逻辑，只有最后一跳落到录制替身；
///   2. 「发了几条、发了什么」可被精确断言，无需真实 broker；
///   3. 目录外方法会在服务层被挡下，一条都不发（下发链路的护栏）。
///
/// 【为什么用 getDevStatus】
///   它在 <c>AnShengCommandCatalog</c> 里属 GroupCommon，对所有设备型号都支持，
///   因此不依赖 <c>AnShengMqttProtocolAdapter.RegisterDeviceKind</c> 的预置状态，
///   与 <c>StaticStateResetter</c> 的清理动作天然解耦。
/// </summary>
[Collection(SharedTestConstants.CollectionName)]
public sealed class CommandDispatchSampleTests : IntegrationTestBase
{
    public CommandDispatchSampleTests(DatabaseFixture fixture) : base(fixture)
    {
    }

    private string CommandUrl => $"/api/v1/ansheng/{Seed.DeviceId}/command";

    [Fact(DisplayName = "示例-05 下发目录内命令 → 适配器恰好收到 1 条，且参数透传正确")]
    public async Task SendCommand_WithCatalogMethod_ReachesAdapterExactlyOnce()
    {
        // Arrange
        var client = Client.AsAdmin();
        var request = new AnShengCommandRequest
        {
            Method = "getDevStatus",
            Parameters = new Dictionary<string, object?> { ["q"] = "slots" }
        };

        Adapter.Sent.Should().BeEmpty("每个用例开始前 IntegrationTestBase 都会 Reset 适配器");

        // Act
        var response = await client.PostAsJsonAsync(CommandUrl, request);

        // Assert —— HTTP 层
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await ReadAsync<ApiResponse<AnShengCommandResponse>>(response);
        body.Should().NotBeNull();
        body!.Code.Should().Be(200, $"下发应当成功，实际返回：{body.Message}");
        body.Data.Should().NotBeNull();
        body.Data!.Success.Should().BeTrue();
        body.Data.FrameId.Should().NotBeNullOrWhiteSpace("服务层必须回填 frameId 以便关联上行回包");

        // Assert —— 适配器层（下发链路的真正锚点）
        Adapter.Sent.Should().ContainSingle("一次 HTTP 调用只应产生一次下发");

        var sent = Adapter.Sent[0];
        sent.DeviceId.Should().Be(Seed.DeviceId);
        sent.SerialNumber.Should().Be(Seed.Imei, "下行主题按 IMEI 寻址，序列号必须原样透传");
        sent.CommandType.Should().Be("getDevStatus");
        sent.Parameters.Should().Contain("slots", "参数 JSON 应包含用例传入的 q=slots");
        sent.FrameId.Should().Be(body.Data.FrameId, "HTTP 回包里的 frameId 必须与实际下发的一致");
        sent.FrameId.Should().HaveLength(16, "安圣 frameId 固定 16 位十六进制");
    }

    [Fact(DisplayName = "示例-06 下发协议外命令 → 适配器层「默认拒绝」，一条都不外发")]
    public async Task SendCommand_WithUnknownMethod_IsRejectedByAdapterGuard()
    {
        // Arrange：一个既不在 AnShengCommandCatalog、也不在 Legacy 白名单里的方法名
        var client = Client.AsAdmin();
        var request = new AnShengCommandRequest
        {
            Method = "definitelyNotAnAnShengMethod",
            Parameters = new Dictionary<string, object?>()
        };

        // Act
        var response = await client.PostAsJsonAsync(CommandUrl, request);

        // Assert
        //
        // 【护栏在哪一层，必须说清楚】
        //   服务层 AnShengCommandService 的目录校验包在
        //       if (AnShengCommandCatalog.TryGet(method, out var spec) && spec != null) { ... }
        //   里——方法不在目录中，整个校验块会被**跳过**，服务层并不拦截。
        //   真正的护栏在适配器层：AnShengMqttProtocolAdapter.SendCommandAsync 实行
        //   「默认拒绝、显式放行」，非目录且非 Legacy 白名单的方法直接抛 NotSupportedException。
        //   （该护栏由二开重构线第 1 步引入。）
        //
        //   所以 RecordingAnShengAdapter 必须复刻这条护栏，否则本用例会假绿。
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await ReadAsync<ApiResponse<AnShengCommandResponse>>(response);
        body.Should().NotBeNull();
        body!.Code.Should().NotBe(200, "协议外方法必须被拒绝");

        // 关键安全断言：一条报文都不能溜到设备侧
        Adapter.Sent.Should().BeEmpty("被拒绝的命令绝不能产生任何实际下发");
    }

    [Fact(DisplayName = "示例-09 Legacy 白名单内命令 → 放行（orderStart）")]
    public async Task SendCommand_WithLegacyWhitelistedMethod_IsDispatched()
    {
        // Arrange：orderStart 不在二开协议目录，但在 Legacy 充电桩白名单内 ⇒ 应放行
        RecordingAnShengAdapter.LegacyWhitelist.Should().Contain("orderStart");

        var client = Client.AsAdmin();
        var request = new AnShengCommandRequest
        {
            Method = "orderStart",
            Parameters = new Dictionary<string, object?> { ["slotNum"] = 1 }
        };

        // Act
        var response = await client.PostAsJsonAsync(CommandUrl, request);

        // Assert
        var body = await ReadAsync<ApiResponse<AnShengCommandResponse>>(response);
        body.Should().NotBeNull();
        body!.Code.Should().Be(200, $"Legacy 白名单内的方法应放行，实际：{body.Message}");

        Adapter.Sent.Should().ContainSingle()
            .Which.CommandType.Should().Be("orderStart");
    }

    [Fact(DisplayName = "示例-10 测试替身的 Legacy 白名单与生产同源（防止双份真相漂移）")]
    public void RecordingAdapter_MirrorsProductionLegacyWhitelist()
    {
        // 一旦生产把 LegacyMethodWhitelist 改名或改结构，反射失效、来源退化为 fallback，
        // 测试替身就可能与生产放行策略不一致 —— 这条哨兵会立刻变红。
        RecordingAnShengAdapter.WhitelistSource.Should().Be("reflection",
            "必须从生产字段反射读取白名单，而不是退回测试侧的手抄快照");

        RecordingAnShengAdapter.LegacyWhitelist.Should().NotBeEmpty();
    }

    [Fact(DisplayName = "示例-07 设备不存在 → 拒绝下发，适配器无副作用")]
    public async Task SendCommand_WithUnknownDevice_IsRejectedBeforeDispatch()
    {
        // Arrange：一个必然不存在的设备主键
        var client = Client.AsAdmin();
        var missingDeviceId = Seed.DeviceId + 999_999;
        var request = new AnShengCommandRequest
        {
            Method = "getDevStatus",
            Parameters = new Dictionary<string, object?> { ["q"] = "slots" }
        };

        // Act
        var response = await client.PostAsJsonAsync(
            $"/api/v1/ansheng/{missingDeviceId}/command", request);

        // Assert
        var body = await ReadAsync<ApiResponse<AnShengCommandResponse>>(response);
        body.Should().NotBeNull();
        body!.Code.Should().NotBe(200, "设备不存在时必须失败");
        Adapter.Sent.Should().BeEmpty();
    }

    [Fact(DisplayName = "示例-08 匿名下发 → HTTP 401，适配器无副作用")]
    public async Task SendCommand_WithoutIdentity_ReturnsBareHttp401AndDispatchesNothing()
    {
        // Arrange
        var client = Client.AsAnonymous();
        var request = new AnShengCommandRequest
        {
            Method = "getDevStatus",
            Parameters = new Dictionary<string, object?>()
        };

        // Act
        var response = await client.PostAsJsonAsync(CommandUrl, request);

        // Assert
        // 同 示例-01：匿名请求被 AuthorizationMiddleware 挑战，返回裸 401 空包体，
        // 不会进入 MVC 过滤器，更不会进入服务层。
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // 这条才是真正的安全护栏：未授权绝不能产生任何设备侧副作用。
        Adapter.Sent.Should().BeEmpty("未授权请求必须在进入服务层之前就被拦下");
    }
}
