using System.Net;
using System.Net.Http;
using FluentAssertions;
using IoTPlatform.Configuration;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Helpers;
using IoTPlatform.IntegrationTests.Infrastructure;
using IoTPlatform.IntegrationTests.Infrastructure.Auth;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IoTPlatform.IntegrationTests.LoginLogScenarios;

/// <summary>
/// 「租户登录日志可见性」修复的集成验收（对应 P0 / P1）。
///
/// 真实 TestServer + 一次性 MySQL schema，覆盖：
///   · 授权（P1）：全部非超管租户角色（admin/operator/customer_admin/user）调
///     <c>GET /api/v1/logs/login</c> 不再 403，返回 200；超管同样 200。
///   · 租户隔离（P0 落 AppCode 的下游效果）：租户身份只看到自身 AppCode 的登录日志；
///     超管（无 AppCode claim）能看到跨租户 + 历史 null 行的全部登录日志。
///   · 回归护栏：一个「无 view_logs 权限」的自定义角色必须被 403，证明过滤器确实在工作
///     （不是无脑 200）。
///
/// 【共享工厂约束】本集合共用一个 TestWebAppFactory，其全局租户过滤器被冻结为单一 AppCode（TEST）。
/// 因此本类用「在库中插入不同 AppCode 的 login_logs 行 + 不同身份查询」的方式验证隔离：
/// 租户身份（claim AppCode=TEST）受全局过滤器 + 显式 AppCode 过滤双重约束，必然看不到
/// AppCode=TENANT_A / null 的行；超管（claim AppCode=null）跳过过滤器，看到全部。
/// 跨租户过滤的底层逻辑另外由 IoTPlatform.AnSheng.Tests.LoginLogAppCodeQueryFilterTests 单测覆盖。
/// </summary>
[Collection(SharedTestConstants.CollectionName)]
public sealed class LoginLogTenantTests : IntegrationTestBase
{
    private const string LoginUrl = "/api/v1/logs/login";
    private const string TenantAAppCode = "TENANT_A_QA";

    /// <summary>本用例造的登录日志固定 AppCode，便于断言与清库区分。</summary>
    private const string SeededTestAppCode = "TEST";

    public LoginLogTenantTests(DatabaseFixture fixture) : base(fixture)
    {
    }

    /// <summary>
    /// 基线之上，插入 3 条不同归属的登录日志：
    ///   · AppCode=TEST      （本租户，应被租户身份看到）
    ///   · AppCode=TENANT_A  （他租户，应被隔离）
    ///   · AppCode=null      （修复前的历史脏数据，修复后按 AppCode 查询应不可见）
    /// </summary>
    protected override async Task OnInitializedAsync()
    {
        await ExecuteDbAsync(async db =>
        {
            db.LoginLogs.Add(new LoginLog
            {
                UserId = 901,
                Email = "owner@test.local",
                UserName = "本租户用户",
                Status = "success",
                AppCode = SeededTestAppCode,
                LoginTime = DateTime.UtcNow
            });
            db.LoginLogs.Add(new LoginLog
            {
                UserId = 902,
                Email = "other@tenant-a.local",
                UserName = "他租户用户",
                Status = "success",
                AppCode = TenantAAppCode,
                LoginTime = DateTime.UtcNow
            });
            db.LoginLogs.Add(new LoginLog
            {
                UserId = 903,
                Email = "orphan@null.local",
                UserName = "历史脏数据",
                Status = "success",
                AppCode = null,
                LoginTime = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        });
    }

    // ───────────────────────────── 授权（P1） ─────────────────────────────

    [Theory(DisplayName = "C-01 非超管租户角色 GET /logs/login → 200（修复前为 403）")]
    [InlineData(Roles.ADMIN)]
    [InlineData(Roles.OPERATOR)]
    [InlineData(Roles.CUSTOMER_ADMIN)]
    [InlineData(Roles.USER)]
    public async Task NonSuperAdminRole_CanViewLoginLogs_Returns200(string role)
    {
        // Arrange
        var client = Client.AsRole(role, SharedTestConstants.AppCode);

        // Act
        var response = await client.GetAsync(LoginUrl);

        // Assert：业务成败看包体 Code，不看 HTTP 状态码
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "已认证请求即便越权也走 PermissionAuthorize 的 200+Code403，不会是裸 HTTP 403");
        var body = await ReadAsync<ApiResponse<PagedResponse<LoginLogDto>>>(response);
        body.Should().NotBeNull();
        body!.Code.Should().Be(200,
            $"角色 {role} 现应在静态权限集持有 view_logs，应当被放行；实际：{body.Message}");
    }

    [Fact(DisplayName = "C-02 超管 GET /logs/login → 200")]
    public async Task SuperAdmin_CanViewLoginLogs_Returns200()
    {
        // Arrange
        var client = Client.AsSuperAdmin();

        // Act
        var response = await client.GetAsync(LoginUrl);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadAsync<ApiResponse<PagedResponse<LoginLogDto>>>(response);
        body.Should().NotBeNull();
        body!.Code.Should().Be(200, $"超管应短路放行；实际：{body.Message}");
    }

    [Fact(DisplayName = "C-03 无 view_logs 的自定义角色 GET /logs/login → 403（过滤器确在工作）")]
    public async Task CustomRole_WithoutViewLogs_Returns403()
    {
        // Arrange：播种一个明确不含 view_logs 的角色，证明 200 是真实授权结果而非无条件放行
        const string noViewRole = "qa_logs_no_view";
        await ExecuteDbAsync(async db =>
        {
            db.Set<Role>().Add(new Role
            {
                Code = noViewRole,
                Name = "QA-无日志查看权限",
                Description = "仅 view_dashboard，刻意不含 view_logs",
                Permissions = "[\"view_dashboard\"]",
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

        var client = Client.AsRole(noViewRole, SharedTestConstants.AppCode);

        // Act
        var response = await client.GetAsync(LoginUrl);

        // Assert：已认证但缺权限 → HTTP 200 + 包体 Code 403
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadAsync<ApiResponse<PagedResponse<LoginLogDto>>>(response);
        body.Should().NotBeNull();
        body!.Code.Should().Be(403, "缺少 view_logs 必须被拒绝");
        body.Message.Should().Be("权限不足");
    }

    // ───────────────────────────── 隔离（P0 下游） ─────────────────────────────

    [Fact(DisplayName = "C-04 租户身份只看到自身 AppCode 登录日志，看不到他租户/null 行")]
    public async Task TenantIdentity_SeesOnlyOwnAppCodeLogs()
    {
        // Arrange：租户 admin（claim AppCode=TEST）
        var client = Client.AsAdmin();

        // Act
        var response = await client.GetAsync(LoginUrl);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadAsync<ApiResponse<PagedResponse<LoginLogDto>>>(response);
        body.Should().NotBeNull();
        body!.Code.Should().Be(200);

        var items = body.Data!.Items;

        // Assert：能看到本租户 TEST 行（含历史修复前可能落库的 TEST 行，至少 1 条）
        items.Should().Contain(l => l.AppCode == SeededTestAppCode,
            "本租户登录日志必须可见（P0 落 AppCode 后按 AppCode 查询命中）");

        // Assert：隔离生效——他租户与 null 行不得出现
        items.Should().NotContain(l => l.AppCode == TenantAAppCode,
            "租户之间的登录日志必须隔离，他租户行不得泄露");
        items.Should().NotContain(l => l.AppCode == null,
            "历史 null AppCode 行（修复前）对带 AppCode 的租户查询必须不可见");
    }

    [Fact(DisplayName = "C-05 超管（无 AppCode claim）看到跨租户 + null 全部登录日志")]
    public async Task SuperAdmin_SeesAllTenantsAndOrphanLogs()
    {
        // Arrange：超管，且关键——不签发 AppCode claim（传递 null）
        var client = Client.AsRole(Roles.SUPER_ADMIN, null);

        // Act
        var response = await client.GetAsync(LoginUrl);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadAsync<ApiResponse<PagedResponse<LoginLogDto>>>(response);
        body.Should().NotBeNull();
        body!.Code.Should().Be(200);

        var items = body.Data!.Items;

        // Assert：超管跨租户可见——本租户、他租户、历史 null 行都应出现
        items.Should().Contain(l => l.AppCode == SeededTestAppCode, "超管应能看本租户日志");
        items.Should().Contain(l => l.AppCode == TenantAAppCode,
            "超管应能跨租户查看他租户的登录日志");
        items.Should().Contain(l => l.AppCode == null,
            "超管应能查看历史未落 AppCode 的登录日志");
    }
}
