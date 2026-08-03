using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using IoTPlatform.Data;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Helpers;
using IoTPlatform.Infrastructure.Protocol.AnSheng;
using IoTPlatform.IntegrationTests.Infrastructure;
using IoTPlatform.IntegrationTests.Infrastructure.Auth;
using IoTPlatform.IntegrationTests.Infrastructure.Mqtt;
using IoTPlatform.IntegrationTests.Seed;
using IoTPlatform.Models;
using Xunit;

namespace IoTPlatform.IntegrationTests.AnSheng;

/// <summary>
/// 验收 #2：认领 4G 四路开关的端到端链路。
///
/// 【本用例覆盖什么】
///   1. <c>POST /api/v1/ansheng/claim</c> 触发「探测 → 判品类 → 建档 → 转 Device」全链路；
///   2. 录制型适配器按 <c>getDevInfo → getDevStatus</c> 顺序下发两条指令，且<b>不</b>发 setAutoReport
///      （用例显式传 <c>GetDevStatusSec = 0</c>，避开 fire-and-forget 的竞态）；
///   3. 替身以同步上行应答驱动 <c>AnShengProbeService</c> 的 (imei, method) 关联；
///   4. 探测结论落库到 <c>ansheng_device_profiles</c>，且关键能力字段正确；
///   5. 设备 <c>Category</c> 由品类派生为 <c>"4G开关"</c>，<b>绝不</b>写死产品名「安圣充电桩」
///      （决策 Q8）——
///      <c>Category == Kind.ToDisplayName()</c> 是全局唯一权威来源，本断言即是该约束的护栏。
///
/// 【为什么另起一条 discovered 记录而不是复用 Seed.DiscoveredDeviceId】
///   基线 SeedData 把主测试 <c>Device</c> 与 <c>DiscoveredAnShengDevice</c> 设成同一 IMEI，
///   认领流程步骤 4（IMEI 冲突拦截）会因此把认领判成 AlreadyClaimed。
///   故本例为认领单独插入一台 IMEI 不冲突的待认领设备，避免碰基线、避免被误判为已认领。
/// </summary>
[Collection(SharedTestConstants.CollectionName)]
public sealed class AnShengClaimTests : IntegrationTestBase
{
    public AnShengClaimTests(DatabaseFixture fixture) : base(fixture)
    {
    }

    private static string ClaimUrl => "/api/v1/ansheng/claim";

    [Fact(DisplayName = "验收#2 认领 4G 四路开关 → 探测两条指令、品类=Switch4G、Profile 落库、Category=4G开关")]
    public async Task Claim_4GSwitch_ProbesAndCreatesDeviceWithCorrectProfile()
    {
        // Arrange —— 插入一台 IMEI 不冲突的待认领 4G 设备
        var imei = SharedTestConstants.SecondaryImei;
        var discoveredId = await SeedUnduplicatedDeviceAsync(imei);

        // 替身对两条探测方法同步回包（4G 四路开关报文）
        Adapter
            .AutoReplyUplink("getDevInfo", BuildGetDevInfoPayload(imei))
            .AutoReplyUplink("getDevStatus", BuildGetDevStatusPayload(imei));

        var client = Client.AsAdmin();
        var request = new ClaimAnShengDeviceRequest
        {
            DiscoveredDeviceId = discoveredId,
            Name = "集成测试-4G四路开关",
            Kind = AnShengDeviceKind.Switch4G,
            // 0 ⇒ 跳过自动上报配置，避免认领成功后 fire-and-forget 下发 setAutoReport
            // 污染本例「恰好两条下发」的断言。
            GetDevStatusSec = 0
        };

        Adapter.Sent.Should().BeEmpty("每个用例开始前 IntegrationTestBase 都会 Reset 适配器");

        // Act
        var response = await client.PostAsJsonAsync(ClaimUrl, request);

        // Assert —— HTTP 层：业务成败看包体 Code，不看状态码
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await ReadAsync<ApiResponse<ClaimAnShengDeviceResponse>>(response);
        body.Should().NotBeNull();
        body!.Code.Should().Be(200, $"认领应当成功，实际：{body.Message}");
        body.Data.Should().NotBeNull();

        var data = body.Data!;
        data.Success.Should().BeTrue();
        data.ErrorCode.Should().BeNull("成功时不应有错误码");
        data.Kind.Should().Be(AnShengDeviceKind.Switch4G, "认领时人工指定 Switch4G");
        data.KindName.Should().Be("4G开关", "KindName 即写入 Device.Category 的值");

        // Assert —— 下发链路：恰好 getDevInfo + getDevStatus 两条，顺序固定
        var sentMethods = Adapter.Sent.Select(s => s.CommandType).ToArray();
        sentMethods.Should().Equal(
            new[] { "getDevInfo", "getDevStatus" },
            "先探能力信息、再探状态；且未发 setAutoReport");

        Adapter.Sent.Should().HaveCount(2, "认领探测只发两条指令");
        Adapter.Sent[0].SerialNumber.Should().Be(imei, "下行按 IMEI 寻址");
        Adapter.Sent[0].DeviceId.Should().Be(0L, "认领前数据库无 Device 行，deviceId 必须传 0");

        // Assert —— 设备已建，Category 由品类派生（决策 Q8 护栏）
        var deviceId = data.DeviceId;
        deviceId.Should().BeGreaterThan(0, "认领成功必须产出正式 Device");

        var claimedDevice = await QueryDbAsync(db => db.Devices
            .FirstOrDefaultAsync(d => d.Id == deviceId!.Value));
        claimedDevice.Should().NotBeNull();
        claimedDevice!.Category.Should().Be("4G开关", "Category 必须等于 Kind.ToDisplayName()");
        claimedDevice.Category.Should().NotBe("安圣充电桩", "决策 Q8：严禁写死产品名");
        claimedDevice.SerialNumber.Should().Be(imei);

        // Assert —— 能力档案落库且关键字段正确
        data.ProfileId.Should().BeGreaterThan(0, "认领成功必须产出 Profile");
        var profile = await QueryDbAsync(db => db.AnShengDeviceProfiles
            .FirstOrDefaultAsync(p => p.Imei == imei));
        profile.Should().NotBeNull();
        profile!.Id.Should().Be(data.ProfileId!.Value);
        profile.DeviceId.Should().Be(deviceId!.Value, "档案回写 DeviceId");
        profile.Kind.Should().Be(AnShengDeviceKind.Switch4G);
        profile.KindSource.Should().Be(AnShengKindSource.Manual, "人工指定品类，来源为 Manual");
        profile.SlotAmount.Should().Be(4, "开关款插槽数=4");
        profile.Version.Should().Be("SWITCH-EC618X-R24-O-V4.0.8", "版本号来自 getDevInfo");
        profile.NetType.Should().Be("4G", "联网类型来自 getDevInfo/getDevStatus");
        profile.ProbeStatus.Should().Be(AnShengProbeStatus.Probed, "探测成功");
        profile.Iccid.Should().NotBeNullOrWhiteSpace("4G 款应带 ICCID");

        // Assert —— 待认领池状态同步回写
        var discovered = await QueryDbAsync(db => db.DiscoveredAnShengDevices
            .FirstOrDefaultAsync(d => d.Id == discoveredId));
        discovered.Should().NotBeNull();
        discovered!.IsClaimed.Should().BeTrue();
        discovered.ClaimedDeviceId.Should().Be(deviceId.Value);
        discovered.ProbeStatus.Should().Be(AnShengProbeStatus.Probed);
    }

    /// <summary>
    /// 验收 #4：设备探测失败时必须<b>显式报错而非静默成功</b>。
    ///
    /// 【为什么补这条】
    ///   T5 交付时 <c>ProbeFailed</c> 仅在 <c>AnShengDeviceProfileServiceTests</c> 里被断言过，
    ///   那是「档案实体字段」层面的单测，覆盖不到验收 #4 真正关心的<b>认领契约</b>：
    ///   错误码、包体 Code、以及「一行 Device 都不许落」的副作用边界。
    ///   本用例把这三件事钉在端到端链路上。
    ///
    /// 【怎么制造超时】
    ///   不登记任何 <c>AutoReplyUplink</c> ⇒ 替身收到下发后不回上行，
    ///   <c>AnShengProbeService</c> 等到 <c>AnSheng:Probe:TimeoutMs</c>（测试环境 200ms）后判失败。
    ///
    /// 【最关键的断言是"没有什么"】
    ///   探测失败最危险的退化不是报错，而是 Device 建了一半、档案却是空的——
    ///   那会让一台能力未知的设备混进正式设备列表。故本例重点断言 Device / AnShengDeviceConfig 均为 0 行。
    /// </summary>
    [Fact(DisplayName = "验收#4 探测超时 → 返回 PROBE_FAILED、ProbeStatus=ProbeFailed，且不创建任何设备行")]
    public async Task Claim_WhenProbeTimesOut_ShouldFailExplicitlyAndCreateNoDevice()
    {
        // Arrange —— 待认领设备存在，但「设备不吭声」：不登记任何自动上行应答
        var imei = SharedTestConstants.SecondaryImei;
        var discoveredId = await SeedUnduplicatedDeviceAsync(imei);

        var deviceCountBefore = await QueryDbAsync(db => db.Devices.CountAsync());

        var client = Client.AsAdmin();
        var request = new ClaimAnShengDeviceRequest
        {
            DiscoveredDeviceId = discoveredId,
            Name = "集成测试-探测超时设备",
            Kind = AnShengDeviceKind.Switch4G,
            GetDevStatusSec = 0
        };

        // Act
        var response = await client.PostAsJsonAsync(ClaimUrl, request);

        // Assert —— HTTP 恒 200，业务失败由包体 Code 承载（设计 §8.3）
        response.StatusCode.Should().Be(HttpStatusCode.OK, "全站 ApiResponse 约定：业务失败不改 HTTP 码");

        var body = await ReadAsync<ApiResponse<ClaimAnShengDeviceResponse>>(response);
        body.Should().NotBeNull();
        body!.Code.Should().Be(400, "探测失败按 BadRequest 承载");

        var data = body.Data;
        data.Should().NotBeNull("失败时仍需返回结构化载荷，前端据此展示「重试探测」");
        data!.Success.Should().BeFalse();
        data.ErrorCode.Should().Be("PROBE_FAILED", "机器可读错误码，前端分支依赖它");
        data.ProbeStatus.Should().Be(AnShengProbeStatus.ProbeFailed);
        data.DeviceId.Should().BeNull("认领失败不得产出 Device");

        // Assert —— ★ 副作用边界：一行都不许落
        var deviceCountAfter = await QueryDbAsync(db => db.Devices.CountAsync());
        deviceCountAfter.Should().Be(deviceCountBefore, "探测失败绝不能创建 Device 行");

        var claimedDevice = await QueryDbAsync(db => db.Devices
            .FirstOrDefaultAsync(d => d.SerialNumber == imei));
        claimedDevice.Should().BeNull("该 IMEI 不应出现在正式设备表");

        var autoReportConfig = await QueryDbAsync(db => db.Set<AnShengDeviceConfig>()
            .FirstOrDefaultAsync(c => c.Imei == imei));
        autoReportConfig.Should().BeNull("未认领成功就不该有自动上报配置");

        // Assert —— 待认领池保持未认领，且状态可观测
        var discovered = await QueryDbAsync(db => db.DiscoveredAnShengDevices
            .FirstOrDefaultAsync(d => d.Id == discoveredId));
        discovered.Should().NotBeNull();
        discovered!.IsClaimed.Should().BeFalse("探测失败不算认领");
        discovered.ClaimedDeviceId.Should().BeNull();
        discovered.ProbeStatus.Should().Be(AnShengProbeStatus.ProbeFailed);
        discovered.ProbeError.Should().NotBeNullOrWhiteSpace("必须留下可排查的错因");

        // Assert —— 档案行存在且标记失败（设计 §3.8 表格：profile 行存在且 ProbeStatus=ProbeFailed）
        var profile = await QueryDbAsync(db => db.AnShengDeviceProfiles
            .FirstOrDefaultAsync(p => p.Imei == imei));
        profile.Should().NotBeNull("探测失败也要留档，便于后续重试与排查");
        profile!.ProbeStatus.Should().Be(AnShengProbeStatus.ProbeFailed);
        profile.DeviceId.Should().BeNull("没有 Device 可挂");
    }

    /// <summary>
    /// 插入一台 IMEI 不冲突的待认领设备，返回其自增主键。
    /// 与基线主设备（SharedTestConstants.Imei）错开，避免认领流程的 IMEI 冲突拦截。
    /// </summary>
    private async Task<long> SeedUnduplicatedDeviceAsync(string imei)
    {
        long id = 0;
        await ExecuteDbAsync(async db =>
        {
            var now = DateTime.UtcNow;
            var discovered = new DiscoveredAnShengDevice
            {
                AppCode = SharedTestConstants.AppCode,
                Imei = imei,
                Model = "ANSHENG-TEST",
                NetType = "4G",
                DiscoveredAt = now,
                LastSeenAt = now,
                IsClaimed = false
            };
            db.DiscoveredAnShengDevices.Add(discovered);
            await db.SaveChangesAsync();
            id = discovered.Id;
        });
        return id;
    }

    /// <summary>构造 getDevInfo 应答：4G 四路开关，含 version / slotAmount / model / iccid。</summary>
    private static string BuildGetDevInfoPayload(string imei) =>
        JsonSerializer.Serialize(new
        {
            method = "getDevInfo",
            result = "ok",
            imei,
            frameId = "1111111111111111",
            version = "SWITCH-EC618X-R24-O-V4.0.8",
            slotAmount = 4,
            phaseAmount = 1,
            model = "Air780E",
            netType = "4G",
            iccid = "89860090100000000001"
        });

    /// <summary>构造 getDevStatus 应答：4G 四路开关，含 signal / temperature / slots / iccid。</summary>
    private static string BuildGetDevStatusPayload(string imei) =>
        JsonSerializer.Serialize(new
        {
            method = "getDevStatus",
            result = "ok",
            imei,
            frameId = "2222222222222222",
            netType = "4G",
            iccid = "89860090100000000001",
            signal = 24,
            temperature = "32.4",
            slotAmount = 4,
            slots = new[] { 0, 0, 0, 0 },
            model = "Air780E",
            version = "SWITCH-EC618X-R24-O-V4.0.8"
        });
}
