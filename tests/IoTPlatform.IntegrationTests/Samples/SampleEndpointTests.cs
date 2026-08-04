using System.Net;
using System.Net.Http;
using FluentAssertions;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Helpers;
using IoTPlatform.IntegrationTests.Infrastructure;
using IoTPlatform.IntegrationTests.Infrastructure.Auth;
using Xunit;

namespace IoTPlatform.IntegrationTests.Samples;

/// <summary>
/// 示例一：HTTP 端点 + 认证 + 真实数据库读取（架构方案 §5-S5）。
///
/// 【这个示例证明什么】
///   1. TestServer 能在一次性 MySQL schema 上完成迁移建表；
///   2. SeedData 播种的数据能被真实 EF 查询读到；
///   3. TestAuthHandler 的「带头 = 已认证 / 不带头 = 匿名」两条分支都生效；
///   4. 断言落在 ApiResponse.Code 而不是 HTTP 状态码（本平台统一 200 + 包体状态）。
///
/// 【它刻意不做什么】
///   不覆盖 T5–T14 的任何业务规则。新增业务用例请另建文件，本文件仅作模板。
/// </summary>
[Collection(SharedTestConstants.CollectionName)]
public sealed class SampleEndpointTests : IntegrationTestBase
{
    private const string DiscoveredUrl = "/api/v1/ansheng/discovered";

    public SampleEndpointTests(DatabaseFixture fixture) : base(fixture)
    {
    }

    [Fact(DisplayName = "示例-01 匿名请求已发现设备列表 → HTTP 401（裸状态码，无 ApiResponse 包体）")]
    public async Task GetDiscoveredDevices_WithoutIdentity_ReturnsBareHttp401()
    {
        // Arrange：清掉全部 X-Test-* 头，模拟未登录
        var client = Client.AsAnonymous();

        // Act
        var response = await client.GetAsync(DiscoveredUrl);

        // Assert
        //
        // 【实测结论，与直觉相反，务必看完】
        //   PermissionAuthorizeAttribute 继承自 AuthorizeAttribute，因此它同时是一个「授权标记」。
        //   标准 AuthorizationMiddleware 会在 MVC 过滤器管道**之前**做策略评估：
        //   匿名请求直接触发 Challenge → 认证方案返回裸 HTTP 401，空包体。
        //   过滤器里的 `context.Result = new JsonResult(ApiResponse.Unauthorized(...))`
        //   在「匿名」这条路径上**根本不会被执行**——它是事实上的死代码。
        //
        //   所以：
        //     · 未认证 ⇒ HTTP 401 + 空包体（本用例）
        //     · 已认证但权限不足 ⇒ HTTP 200 + Code 403（此时过滤器才真正跑到）
        //   生产用 JwtBearer 时行为完全一致，TestAuthHandler 只是忠实复刻了它。
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "匿名请求被 AuthorizationMiddleware 在 MVC 过滤器之前挑战，返回裸 401");

        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().BeEmpty("Challenge 不产出 ApiResponse 包体；断言 Code 会因空包体反序列化失败");
    }

    [Fact(DisplayName = "示例-02 管理员请求已发现设备列表 → 业务码 200 且能读到播种的 IMEI")]
    public async Task GetDiscoveredDevices_AsAdmin_ReturnsSeededDevice()
    {
        // Arrange：admin 角色 + AppCode=TEST，与 SeedData 播种的租户一致
        var client = Client.AsAdmin();

        // Act
        var response = await client.GetAsync(DiscoveredUrl);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await ReadAsync<ApiResponse<DiscoveredDeviceListResponse>>(response);
        body.Should().NotBeNull();
        body!.Code.Should().Be(200, "admin 拥有 VIEW_DEVICES 权限，应当放行");
        body.Data.Should().NotBeNull();

        body.Data!.Total.Should().Be(1, "基线只播种了 1 台已发现设备");
        body.Data.Items.Should().ContainSingle()
            .Which.Imei.Should().Be(Seed.Imei, "读到的必须是本用例刚播种的那台设备");

        // 顺带验证真实落库：绕过 HTTP 直接查库，两边必须一致
        var dbCount = await QueryDbAsync(async db =>
            await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .CountAsync(db.DiscoveredAnShengDevices));
        dbCount.Should().Be(1, "HTTP 侧的 Total 必须与库里实际行数一致");
    }

    [Fact(DisplayName = "示例-03 超级管理员绕过权限校验，但仍受 AppCode 数据过滤")]
    public async Task GetDiscoveredDevices_AsSuperAdmin_StillCarriesAppCode()
    {
        // Arrange：super_admin 在 PermissionAuthorizeAttribute 里直接放行，
        //          但 AnShengController 仍会读 AppCode claim 做数据过滤（用户决策 3）。
        var client = Client.AsSuperAdmin();

        // Act
        var response = await client.GetAsync(DiscoveredUrl);

        // Assert
        var body = await ReadAsync<ApiResponse<DiscoveredDeviceListResponse>>(response);
        body.Should().NotBeNull();
        body!.Code.Should().Be(200);
        body.Data!.Items.Should().ContainSingle().Which.Imei.Should().Be(Seed.Imei);
    }

    [Fact(DisplayName = "示例-04 静态状态清理器可用（清理入口失效时会失败告警）")]
    public void StaticStateResetter_CanClearAllKnownStaticState()
    {
        // 这条是「脚手架自检」：一旦生产侧的清理入口（如 AnShengMqttProtocolAdapter.ClearDeviceKinds）
        // 失效或抛异常，这里会红，从而避免静态污染以「随机幽灵失败」的形式渗进业务用例。
        // 注意它只是「不报错」级别的弱断言；真正的「确实清空了」由 QA-01 证伪用例负责。
        // （T7-3 已删除 AnShengCommandService.FrameIdCommandIdMap，反射清理路径随之下线。）
        StaticStateResetter.Verify().Should().BeTrue(
            $"静态状态必须可清理，否则用例之间会串扰。诊断：{StaticStateResetter.LastError}");
    }
}
