using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.Infrastructure.Protocol.AnSheng;
using IoTPlatform.IntegrationTests.Infrastructure;
using IoTPlatform.IntegrationTests.Infrastructure.Auth;
using IoTPlatform.Models;
using IoTPlatform.Services;
using IoTPlatform.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IoTPlatform.IntegrationTests.AnSheng;

/// <summary>
/// T7-5 只读端点 <c>GET /api/v1/ansheng/commands/{commandId}</c> 的<b>安全红线</b>验收，
/// 外加 T7-4「应答回填」在<b>下行接缝已实装</b>之后的端到端复验。
///
/// ═══════════════════════════════════════════════════════════════════════
/// 【为什么这条必须是集成测试，单测替代不了】
/// ═══════════════════════════════════════════════════════════════════════
/// 单测 <c>AnShengSecretMaskerTests</c> 已经锁死「给定 method + JSON，掩码函数会不会打码」。
/// 但口令泄漏从来不是「掩码函数写错了」，而是<b>整条链路上少调了一次</b>：
///   · 下发侧忘了用 <c>MaskRequestJson</c> 落库 → 明文进 <c>RequestJson</c> 列；
///   · 回填侧忘了用 <c>MaskResponseJson</c>   → 明文进 <c>ResponseJson</c> 列（QA Round 1 · P0-3）；
///   · 掩码集按<b>设备自称的 method</b> 取而不是我方下发的 method → 设备谎报即可绕过；
///   · 读取侧直接 <c>Adapt(record)</c> 返回 → 存量明文行原样外泄。
/// 这四处任意一处漏掉，单测<b>全绿</b>，而 <c>GET /commands/{id}</c> 照样吐明文口令。
/// 因此本用例的判据只有一条、且是黑盒的：<b>HTTP 响应体全文不得出现明文口令字面量</b>。
///
/// ═══════════════════════════════════════════════════════════════════════
/// 【为什么同时断言「host/clientId 仍在」】
/// ═══════════════════════════════════════════════════════════════════════
/// 「把整个 mqttParams 打成 *** 」也能让「不含明文」通过，但那等于把留痕废掉——
/// 排障时既看不到连的哪个 broker，也看不到 clientId 撞没撞。掩码必须<b>精确到子字段</b>
/// （T7 决策 D3）。只断言「没有明文」而不断言「该留的还在」，会把过度掩码放行。
/// </summary>
[Collection(SharedTestConstants.CollectionName)]
public sealed class AnShengCommandRecordEndpointTests : IntegrationTestBase
{
    private const string CommandUrlTemplate = "/api/v1/ansheng/{0}/command";
    private const string RecordUrlTemplate = "/api/v1/ansheng/commands/{0}";

    /// <summary>
    /// 明文口令字面量。刻意取一个<b>绝不会自然出现</b>在任何模板/默认值里的串，
    /// 这样「响应体不含它」才是强证据而不是巧合。
    /// </summary>
    private const string PlaintextPassword = "QA-R3-PLAINTEXT-PWD-9f3a7c";

    /// <summary>排障必需、绝不该被打掉的非敏感字段值。</summary>
    private const string BrokerHost = "mqtt.qa-round3.example.com";
    private const string ClientId = "qa-round3-client";

    public AnShengCommandRecordEndpointTests(DatabaseFixture fixture) : base(fixture)
    {
    }

    // ══════════════════════════════════════════════════════════════════
    // 下行方向：setMqtt 的 password 藏在 mqttParams 对象内部
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// <c>setMqtt</c> 下发后，<c>GET /commands/{id}</c> 返回的 <c>requestJson</c>
    /// 必须已掩码：不含明文口令，但保留 host / clientId。
    /// </summary>
    [Fact(DisplayName = "T7-5 GET /commands/{id} → setMqtt 的 password 永不明文（且不过度掩码）")]
    public async Task GetCommandRecord_SetMqtt_MasksPasswordButKeepsDiagnosticFields()
    {
        // Arrange
        await SeedProfileAsync(Seed.DeviceId, Seed.Imei, AnShengDeviceKind.Speaker4G);
        var client = Client.AsAdmin();

        var request = new AnShengCommandRequest
        {
            Method = "setMqtt",
            Parameters = new Dictionary<string, object?>
            {
                ["mqttParams"] = new Dictionary<string, object?>
                {
                    ["host"] = BrokerHost,
                    ["port"] = 1883,
                    ["clientId"] = ClientId,
                    ["username"] = "qa-round3",
                    ["password"] = PlaintextPassword
                },
                ["reboot"] = false
            }
        };

        // Act 1 —— 下发
        var sendResponse = await client.PostAsJsonAsync(
            string.Format(CommandUrlTemplate, Seed.DeviceId), request);

        var sendBody = await sendResponse.Content.ReadAsStringAsync();
        sendResponse.StatusCode.Should().Be(
            HttpStatusCode.OK,
            $"setMqtt 属 GroupMqtt（4 个品类全支持）且参数合法，不该被 Guard 拒绝。实际响应：{Truncate(sendBody)}");

        var commandId = ReadCommandId(sendBody);
        commandId.Should().NotBeNullOrWhiteSpace();

        // Act 2 —— 回读命令记录
        var recordResponse = await client.GetAsync(string.Format(RecordUrlTemplate, commandId));
        var recordBody = await recordResponse.Content.ReadAsStringAsync();

        // Assert —— 端点可用
        recordResponse.StatusCode.Should().Be(
            HttpStatusCode.OK,
            $"T7-5 要求暴露 GET /commands/{{commandId}}；404 说明端点没挂上。实际响应：{Truncate(recordBody)}");

        // Assert —— ★安全红线★ 整个响应体不得出现明文口令
        recordBody.Should().NotContain(
            PlaintextPassword,
            "命令记录端点返回明文口令即为安全事故（T7 决策 D3）：" +
            "要么落库时没走 MaskRequestJson，要么读取时没做二次掩码");

        using var doc = JsonDocument.Parse(recordBody);
        var data = doc.RootElement.GetProperty("data");

        TryGetPropertyIgnoreCase(data, "requestJson", out var requestJson)
            .Should().BeTrue("命令记录必须带 requestJson，否则排障无从下手");

        var requestJsonText = requestJson.GetString() ?? string.Empty;
        requestJsonText.Should().NotBeNullOrWhiteSpace(
            "下发参数必须留痕；空 requestJson 说明 AnShengCommandRecord 没写参数快照");

        requestJsonText.Should().Contain(
            AnShengSecretMasker.Mask,
            "password 必须被替换成固定掩码字面量 ***，便于日志检索时一眼确认已脱敏");

        // Assert —— 不过度掩码：排障必需字段必须原样保留
        requestJsonText.Should().Contain(
            BrokerHost, "host 是非敏感字段，打掉它等于把留痕废掉（掩码必须精确到子字段）");
        requestJsonText.Should().Contain(
            ClientId, "clientId 是非敏感字段，排查「客户端撞号」全靠它");

        // Assert —— 库里存的本身就是掩码后的值（不是只在出口处打码）
        var stored = await QueryDbAsync(db => db.AnShengCommandRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.CommandId == commandId));

        stored.Should().NotBeNull("命令必须落库");
        (stored!.RequestJson ?? string.Empty).Should().NotContain(
            PlaintextPassword,
            "明文口令绝不能落库——只在 API 出口掩码，等于把明文留在了数据库备份、慢日志和 DBA 视野里");

        // Assert —— 真正发给设备的报文必须是<b>明文</b>（掩码绝不能污染下行）
        Adapter.Sent.Should().HaveCount(1, "命令必须真的出网");
        Adapter.Sent.Single().Parameters.Should().Contain(
            PlaintextPassword,
            "下发报文必须携带真口令：若这里也变成 ***，设备会拿着字面量 *** 去连 broker，" +
            "这正是 AnShengSecretMasker「绝不原地修改入参」那段注释所防的事故");
    }

    // ══════════════════════════════════════════════════════════════════
    // 上行方向：getMqtt 无参数，口令只出现在设备应答里
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// <c>getMqtt</c> 的设备应答里带明文口令时，回填进 <c>ResponseJson</c> 前必须掩码；
    /// 同时复验 T7-4 应答闭环在「适配器已实装 <c>IAnShengDownlinkPort</c>」之后仍然成立
    /// （<c>Status=Succeeded</c> + <c>CompletedAt</c> + 在途表摘除）。
    ///
    /// <c>getMqtt</c> 是<b>无参命令</b>，敏感字段只能来自 <c>ResponseSecretFields</c>——
    /// 这正是 Round 1 · P0-2 的原始缺陷面，必须端到端守住，而不能只靠单测。
    /// </summary>
    [Fact(DisplayName = "T7-5/T7-4 getMqtt 应答口令掩码 + 下行接缝下应答回填闭环")]
    public async Task GetCommandRecord_GetMqttReply_MasksResponsePasswordAndClosesLoop()
    {
        // Arrange
        await SeedProfileAsync(Seed.DeviceId, Seed.Imei, AnShengDeviceKind.Speaker4G);
        var client = Client.AsAdmin();

        var store = Fixture.Factory.Services.GetRequiredService<IAnShengPendingCommandStore>();

        // 上行管道是 Singleton，构造时订阅静态总线；这里取出来只为拿到 DrainAsync 这个
        // 精确完成信号（替代 Thread.Sleep），实例与 Program.cs 强制解析的是同一个。
        var pipeline = Fixture.Factory.Services.GetRequiredService<AnShengUplinkPipeline>();

        // Act 1 —— 下发 getMqtt（无参）
        var sendResponse = await client.PostAsJsonAsync(
            string.Format(CommandUrlTemplate, Seed.DeviceId),
            new AnShengCommandRequest { Method = "getMqtt" });

        var sendBody = await sendResponse.Content.ReadAsStringAsync();
        sendResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"实际响应：{Truncate(sendBody)}");

        var commandId = ReadCommandId(sendBody);

        Adapter.Sent.Should().HaveCount(1, "命令必须真的出网");
        var frameId = Adapter.Sent.Single().FrameId;
        frameId.Should().NotBeNullOrWhiteSpace(
            "下行接缝要求 frameId 由服务侧预先生成并登记；空 frameId 意味着应答永远关联不上");

        store.Count.Should().Be(
            1, "先登记后下发（硬约束 N1）：适配器实装 IAnShengDownlinkPort 后，下发返回时条目必须已在表里");

        // Act 2 —— 设备回一条<b>带明文口令</b>的 getMqtt 应答，frameId 与下发严格一致
        var processedBefore = pipeline.ProcessedCount;
        Adapter.RaiseAnShengUplink(Seed.Imei, "getMqtt", BuildGetMqttReply(Seed.Imei, frameId));

        var drained = await pipeline.DrainAsync(TimeSpan.FromSeconds(15));
        drained.Should().BeTrue("上行管道必须在超时前处理完毕，否则后续断言读到的是中间态");
        pipeline.ProcessedCount.Should().BeGreaterThan(
            processedBefore, "上行报文必须真的被管道处理过；没增长说明总线订阅没生效");

        // Act 3 —— 回读命令记录
        var recordResponse = await client.GetAsync(string.Format(RecordUrlTemplate, commandId));
        var recordBody = await recordResponse.Content.ReadAsStringAsync();

        recordResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"实际响应：{Truncate(recordBody)}");

        // Assert —— ★安全红线★
        recordBody.Should().NotContain(
            PlaintextPassword,
            "getMqtt 是无参命令，敏感字段只能来自 spec.ResponseSecretFields；" +
            "这里出现明文即说明 SecretFieldNames 又退回到「只看 Params」（Round 1 · P0-2 回归）");

        using var doc = JsonDocument.Parse(recordBody);
        var data = doc.RootElement.GetProperty("data");

        TryGetPropertyIgnoreCase(data, "responseJson", out var responseJson)
            .Should().BeTrue("应答留痕必须回填到 responseJson");

        var responseJsonText = responseJson.GetString() ?? string.Empty;
        responseJsonText.Should().NotBeNullOrWhiteSpace(
            "ResponseJson 为空说明应答根本没回填（Round 1 · P0-3 的原始症状）");
        responseJsonText.Should().Contain(
            AnShengSecretMasker.Mask, "应答里的 password 必须被替换成 ***");
        responseJsonText.Should().Contain(
            BrokerHost, "应答中的 host 是排障必需信息，不应被一并打掉");

        // Assert —— T7-4 闭环仍然成立
        var stored = await QueryDbAsync(db => db.AnShengCommandRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.CommandId == commandId));

        stored.Should().NotBeNull();
        stored!.Status.Should().Be(
            AnShengCommandStatus.Succeeded,
            "设备回了 result=ok，终态必须是 Succeeded；停在 Sent 说明应答没关联上在途条目");
        stored.CompletedAt.Should().NotBeNull("终态必须带完成时刻");
        stored.DurationMs.Should().NotBeNull("闭环必须能算出端到端时延");
        (stored.ResponseJson ?? string.Empty).Should().NotContain(
            PlaintextPassword, "明文口令绝不能落库");

        store.Count.Should().Be(
            0, "应答已关联，在途条目必须被摘除；留着就是内存泄漏 + 后续同 frameId 误判");
    }

    // ══════════════════════════════════════════════════════════════════
    // 契约：查不到的 commandId
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 未知 <c>commandId</c> 必须以<b>业务码 404</b> 回应，而不是 500，也不是 200+空对象。
    ///
    /// 【为什么断言 HTTP 200 而不是 HTTP 404】
    ///   全站 <c>ApiResponse</c> 约定「业务失败不改 HTTP 码」——失败语义只走响应体的
    ///   <c>code</c> 字段（见 <c>AnShengClaimTests</c> 同款断言）。这里跟着全站口径走，
    ///   否则本端点会成为全站唯一一个「404 走 HTTP 层」的特例，前端得为它写分支。
    ///   真正要守的是：<c>code=404</c> 且 <c>data=null</c>，而不是 <c>code=500</c>（异常泄漏）。
    /// </summary>
    [Fact(DisplayName = "T7-5 GET /commands/{id} 未知 ID → 业务码 404 且 data 为 null（不是 500）")]
    public async Task GetCommandRecord_UnknownId_ReturnsBusinessNotFound()
    {
        var client = Client.AsAdmin();

        var response = await client.GetAsync(
            string.Format(RecordUrlTemplate, Guid.NewGuid().ToString("N")));

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(
            HttpStatusCode.OK,
            $"全站 ApiResponse 约定：业务失败不改 HTTP 码。实际响应：{Truncate(body)}");

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.GetProperty("code").GetInt32().Should().Be(
            404,
            "查不到必须是业务码 404；500 说明查询把异常泄漏成了服务器错误，" +
            $"200 会让前端把「不存在」渲染成空白详情页。实际响应：{Truncate(body)}");

        root.GetProperty("data").ValueKind.Should().Be(
            JsonValueKind.Null, "查不到时不得返回半成品对象");
    }

    // ══════════════════════════════════════════════════════════════════
    // Helpers
    // ══════════════════════════════════════════════════════════════════

    /// <summary>构造 getMqtt 应答：口令<b>明文</b>嵌在 mqttParams 内部，frameId 与下发一致。</summary>
    private static string BuildGetMqttReply(string imei, string frameId) =>
        JsonSerializer.Serialize(new
        {
            method = "getMqtt",
            result = "ok",
            imei,
            frameId,
            mqttParams = new
            {
                host = BrokerHost,
                port = 1883,
                clientId = ClientId,
                username = "qa-round3",
                password = PlaintextPassword
            }
        });

    /// <summary>从下发响应里取 commandId。</summary>
    private static string ReadCommandId(string sendBody)
    {
        using var doc = JsonDocument.Parse(sendBody);
        return doc.RootElement.GetProperty("data").GetProperty("commandId").GetString()!;
    }

    /// <summary>插入设备能力档案（D7：品类显式落档，避免 Guard 走降级放行掩盖问题）。</summary>
    private Task SeedProfileAsync(long deviceId, string imei, AnShengDeviceKind kind)
        => ExecuteDbAsync(async db =>
        {
            db.AnShengDeviceProfiles.Add(new AnShengDeviceProfile
            {
                // AppCode 必须显式赋值：播种走 DI 作用域直连 DbContext，
                // 此时 TenantContext 为空，全局过滤器不会代填。
                AppCode = SharedTestConstants.AppCode,
                Imei = imei,
                DeviceId = deviceId,
                Kind = kind,
                KindSource = AnShengKindSource.Manual,
                SlotAmount = 1,
                Version = "V4.0.20",
                ProbeStatus = AnShengProbeStatus.Probed
            });
            await db.SaveChangesAsync();
        });

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        foreach (var prop in element.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string Truncate(string s) => s.Length <= 800 ? s : s[..800] + "…";
}
