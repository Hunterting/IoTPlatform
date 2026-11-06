using System.Net;
using System.Net.Http;
using FluentAssertions;
using IoTPlatform.Data;
using IoTPlatform.Helpers;
using IoTPlatform.IntegrationTests.Infrastructure;
using IoTPlatform.IntegrationTests.Infrastructure.Auth;
using IoTPlatform.Models;
using Xunit;

namespace IoTPlatform.IntegrationTests.Security;

/// <summary>
/// 针对「自定义角色 Code 权限真正生效」改动的<b>集成测试</b>。
///
/// 通过真实 TestServer + 一次性 MySQL schema，验证 <see cref="Filters.PermissionAuthorizeAttribute"/>
/// （现 <c>IAsyncAuthorizationFilter</c>）在请求链路里确实以 DB <c>Role.Permissions</c> 为单一事实来源：
///
///   · 自定义角色持有某权限 → 端点放行（200）；
///   · 自定义角色缺失某权限 → 端点拒绝（HTTP 200 + 包体 Code 403，本平台统一口径）；
///   · 超管 → 短路全通；
///   · 未知角色码（无 DB 记录、非内置）→ 回退空列表 → 拒绝。
///
/// 角色身份由 <see cref="AuthTestHelper.AsRole"/> 经 X-Test-Role 头注入，
/// <see cref="Filters.PermissionAuthorizeAttribute"/> 消费 <c>ClaimTypes.Role</c> 后调用
/// <c>IPermissionService.GetEffectivePermissionsAsync</c> 解析（跨租户 appCode=null 查全表）。
/// </summary>
[Collection(SharedTestConstants.CollectionName)]
public sealed class PermissionFilterTests : IntegrationTestBase
{
    /// <summary>档案管理列表：类级 <c>[PermissionAuthorize(VIEW_ARCHIVES)]</c>。</summary>
    private const string ArchivesUrl = "/api/v1/archives";

    /// <summary>已发现设备列表：类级 <c>[PermissionAuthorize(VIEW_DEVICES)]</c>（示例脚手架已验证可达）。</summary>
    private const string DiscoveredUrl = "/api/v1/ansheng/discovered";

    /// <summary>自定义角色码；其权限完全由 DB 决定，覆盖所有内置 5 角色之外的「自定义 Code」分支。</summary>
    private const string CustomViewerRole = "qa_custom_viewer";

    public PermissionFilterTests(DatabaseFixture fixture) : base(fixture)
    {
    }

    /// <summary>
    /// 在基线播种之后，向测试 schema 插入一个自定义角色，权限由 DB 的 Permissions JSON 决定。
    /// 注意：<see cref="PermissionService"/> 以 <c>GetByCodeAsync(code, null)</c> 查全表（不按 AppCode 过滤），
    /// 因此这里带 AppCode=TEST 也能被解析命中。
    /// </summary>
    protected override async Task OnInitializedAsync()
    {
        await ExecuteDbAsync(async db =>
        {
            db.Set<Role>().Add(new Role
            {
                Code = CustomViewerRole,
                Name = "QA 自定义查看角色",
                // 只授予 view_archives / view_areas，刻意不包含 view_devices
                Permissions = "[\"view_archives\",\"view_areas\"]",
                DataScope = "CUSTOM",
                AppCode = SharedTestConstants.AppCode,
                IsSystem = false,
                IsDefault = false,
                Status = "active",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        });
    }

    [Fact(DisplayName = "PF-01 自定义角色(DB 持有 view_archives) 访问授权端点 → 200")]
    public async Task CustomRole_GrantedPermission_Returns200()
    {
        // Arrange：持有 view_archives 的自定义角色
        var client = Client.AsRole(CustomViewerRole, SharedTestConstants.AppCode);

        // Act
        var response = await client.GetAsync(ArchivesUrl);

        // Assert：权限命中 → 过滤器放行，控制器返回业务 200（档案为空列表，但不影响判定）
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "自定义角色在 DB 中持有 view_archives，应当被放行");
        var body = await ReadAsync<ApiResponse<object>>(response);
        body.Should().NotBeNull();
        body!.Code.Should().Be(200, "授权端点应当返回业务成功码");
    }

    [Fact(DisplayName = "PF-02 自定义角色(缺失 view_devices) 访问需授权端点 → 403")]
    public async Task CustomRole_MissingPermission_Returns403()
    {
        // Arrange：自定义角色没有 view_devices
        var client = Client.AsRole(CustomViewerRole, SharedTestConstants.AppCode);

        // Act
        var response = await client.GetAsync(DiscoveredUrl);

        // Assert：已认证但权限不足 → HTTP 200 + 包体 Code 403（本平台统一口径）
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "越权分支走 PermissionAuthorize 的 JsonResult(Forbidden)，HTTP 仍是 200");
        var body = await ReadAsync<ApiResponse<object>>(response);
        body.Should().NotBeNull();
        body!.Code.Should().Be(403, "缺少 view_devices 应当被拒绝");
        body.Message.Should().Be("权限不足");
    }

    [Fact(DisplayName = "PF-03 超管 → 短路全通（即便端点要求 view_devices）")]
    public async Task SuperAdmin_BypassesAuthorization_Returns200()
    {
        // Arrange：超管（PermissionAuthorize 内 IsSuperAdmin 短路，不查 DB）
        var client = Client.AsSuperAdmin();

        // Act
        var response = await client.GetAsync(DiscoveredUrl);

        // Assert：超管绕过权限校验，直接放行
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadAsync<ApiResponse<object>>(response);
        body.Should().NotBeNull();
        body!.Code.Should().Be(200, "超管应短路放行，不应被权限过滤器拦截");
    }

    [Fact(DisplayName = "PF-04 未知角色码(无 DB 记录/非内置) → 空权限 → 403")]
    public async Task UnknownRole_NoPermissions_Returns403()
    {
        // Arrange：一个既无 DB 记录、又非内置 5 角色的码
        var client = Client.AsRole("qa_totally_unknown_role", SharedTestConstants.AppCode);

        // Act
        var response = await client.GetAsync(ArchivesUrl);

        // Assert：GetEffectivePermissionsAsync 回退静态表 → 未知码返回空列表 → 缺少 view_archives → 403
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadAsync<ApiResponse<object>>(response);
        body.Should().NotBeNull();
        body!.Code.Should().Be(403, "未知角色码权限为空，应当被拒绝");
    }
}
