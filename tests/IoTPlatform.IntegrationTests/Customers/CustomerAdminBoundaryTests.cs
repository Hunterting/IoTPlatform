using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using IoTPlatform.Configuration;
using IoTPlatform.Helpers;
using IoTPlatform.IntegrationTests.Infrastructure;
using IoTPlatform.IntegrationTests.Infrastructure.Auth;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;
using CustomerDto = IoTPlatform.Controllers.CustomerDto;
using CreateCustomerRequest = IoTPlatform.Controllers.CreateCustomerRequest;
using UpdateCustomerRequest = IoTPlatform.Controllers.UpdateCustomerRequest;

namespace IoTPlatform.IntegrationTests.Customers;

/// <summary>
/// 「租户 admin 客户管理权限边界」增量修复的集成验收（AC-01 ~ AC-13）。
///
/// 【被测改动】Controllers/CustomersController.cs
///   · IsAdminLike() = role ∈ { super_admin, admin }，取代原先「仅 super_admin」的短路；
///   · GetCustomers / GetAccessibleCustomers 的租户过滤改为 !IsAdminLike()；
///   · GetCustomer / UpdateCustomer / DeleteCustomer 的归属校验改为 !IsAdminLike() &amp;&amp; AppCode 不等
///     （DeleteCustomer 的归属校验是本次新增，修复了原越权删除漏洞）；
///   · CreateCustomer 引入 effectiveAppCode：admin/超管采信 request.AppCode（每客户独立 AppCode），
///     普通角色若伪造他租户 AppCode → 403，否则强制用自身 claim（不采信前端）。
///
/// 【本平台的响应口径】业务成败一律 HTTP 200 + ApiResponse.Code（见 IntegrationTestBase.ReadAsync 注释）。
///   因此 403 / 400 / 404 全部断言包体 Code，而不是 HTTP 状态码。
///
/// 【为什么额外播种角色】
///   PermissionAuthorizeAttribute 对非超管角色调用 IPermissionService.GetEffectivePermissionsAsync，
///   其单一事实来源是 DB Role.Permissions。SeedData 只播种了 admin（Permissions="[]" → 回退静态表，
///   静态表 admin 含 CRUD_CUSTOMERS），而静态表 staff <b>不含 view_customers</b>。
///   为对齐真机基线（tenantB staff 仅 view_customers / view_projects / view_devices 3 权限），
///   本类在 OnInitializedAsync 显式播种：
///     · staff            → 仅 3 个 view_* 权限（真实基线，天然无写权限）；
///     · qa_admincust_writer → 拥有 4 个 customers 权限的<b>普通</b>角色。
///   后者是关键：staff 会在方法级 PermissionAuthorize 被拦（Code 403「权限不足」），
///   根本走不到控制器里新增的归属校验。要真正证伪「归属校验生效」，
///   必须用一个<b>拥有写权限但不是 admin</b>的普通角色，把请求送进控制器分支。
///
/// 【造数与清理】
///   每个用例自带唯一前缀 qa_ac_{guid10}，本类造的 Customer 的 Code/AppCode 全部带该前缀；
///   DisposeAsync 显式按前缀删除（Respawn 会在下一用例兜底），绝不污染共享测试 schema。
/// </summary>
[Collection(SharedTestConstants.CollectionName)]
public sealed class CustomerAdminBoundaryTests : IntegrationTestBase
{
    private const string CustomersUrl = "/api/v1/customers";

    /// <summary>真机基线中的普通只读角色。</summary>
    private const string StaffRole = "staff";

    /// <summary>
    /// 测试专用「普通角色 + 客户写权限」。非 admin ⇒ IsAdminLike() 为 false，
    /// 但能穿过方法级 PermissionAuthorize，用于精确验证控制器内的归属校验分支。
    /// </summary>
    private const string TenantWriterRole = "qa_admincust_writer";

    private readonly string _prefix = "qa_ac_" + Guid.NewGuid().ToString("N")[..10];

    private long _otherCustomerAId;
    private long _otherCustomerBId;

    private string OtherAppCodeA => $"{_prefix}_a";
    private string OtherAppCodeB => $"{_prefix}_b";
    private string OtherCodeA => $"{_prefix}_ca";
    private string OtherCodeB => $"{_prefix}_cb";
    private string FreshAppCode => $"{_prefix}_new";
    private string FreshCode => $"{_prefix}_cnew";
    private string FreshAppCode2 => $"{_prefix}_new2";
    private string FreshCode2 => $"{_prefix}_cnew2";
    private string WriterAppCode => $"{_prefix}_w";

    public CustomerAdminBoundaryTests(DatabaseFixture fixture) : base(fixture)
    {
    }

    /// <summary>
    /// 基线（1 条 AppCode=TEST 的客户）之上，追加 2 条「其他租户」客户 + 2 个测试角色。
    /// 因此每个用例的客户全集恒为 3 条，超管/admin 应看到 3 条，staff 应只看到 1 条。
    /// </summary>
    protected override async Task OnInitializedAsync()
    {
        await ExecuteDbAsync(async db =>
        {
            db.Set<Role>().Add(new Role
            {
                Code = StaffRole,
                Name = "QA-真机基线只读员工",
                Description = "对齐真机 tenantB staff：仅 view_customers / view_projects / view_devices",
                Permissions = """["view_customers","view_projects","view_devices"]""",
                DataScope = "CUSTOM",
                AppCode = SharedTestConstants.AppCode,
                IsSystem = false,
                IsDefault = false,
                Status = "active",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            db.Set<Role>().Add(new Role
            {
                Code = TenantWriterRole,
                Name = "QA-普通租户写角色",
                Description = "拥有客户 CRUD 权限但非 admin，用于穿透权限过滤器验证控制器归属校验",
                Permissions =
                    """["view_customers","create_customers","update_customers","delete_customers"]""",
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

        _otherCustomerAId = await AddCustomerAsync(OtherCodeA, OtherAppCodeA, "QA-他租户客户A");
        _otherCustomerBId = await AddCustomerAsync(OtherCodeB, OtherAppCodeB, "QA-他租户客户B");
    }

    /// <summary>按唯一前缀显式清理本用例造的客户与角色，避免污染共享测试库。</summary>
    public override async Task DisposeAsync()
    {
        try
        {
            await ExecuteDbAsync(async db =>
            {
                var staleCustomers = await db.Customers
                    .Where(c => c.Code.StartsWith(_prefix) || c.AppCode.StartsWith(_prefix))
                    .ToListAsync();
                if (staleCustomers.Count > 0)
                {
                    db.Customers.RemoveRange(staleCustomers);
                    await db.SaveChangesAsync();
                }

                var staleRoles = await db.Set<Role>()
                    .Where(r => r.Code == StaffRole || r.Code == TenantWriterRole)
                    .ToListAsync();
                if (staleRoles.Count > 0)
                {
                    db.Set<Role>().RemoveRange(staleRoles);
                    await db.SaveChangesAsync();
                }
            });
        }
        catch (Exception ex)
        {
            // 清理失败不应掩盖断言结果；下一用例的 Respawn 会兜底清库。
            Console.WriteLine($"[CustomerAdminBoundaryTests] 显式清理失败（Respawn 兜底）：{ex.Message}");
        }

        await base.DisposeAsync();
    }

    // ────────────────────────────── 列表可见性 ──────────────────────────────

    [Fact(DisplayName = "AC-01 超管 GET /customers → 看全部 3 条客户")]
    public async Task AC01_SuperAdmin_List_SeesAllCustomers()
    {
        // Arrange
        var client = Client.AsSuperAdmin();

        // Act
        var body = await ReadListAsync(await client.GetAsync($"{CustomersUrl}?pageSize=50"));

        // Assert
        body.Code.Should().Be(200, $"超管读取客户列表应成功，实际：{body.Message}");
        body.Data.Should().NotBeNull();
        body.Data!.TotalCount.Should().Be(3, "基线 1 条 + 本用例追加的 2 条他租户客户");
        body.Data.Items.Select(c => c.AppCode).Should().BeEquivalentTo(
            new[] { SharedTestConstants.AppCode, OtherAppCodeA, OtherAppCodeB },
            "超管不受 AppCode 过滤");
    }

    [Fact(DisplayName = "AC-02 admin GET /customers → 看全部 3 条（本轮核心：admin 等同超管）")]
    public async Task AC02_Admin_List_SeesAllCustomers()
    {
        // Arrange：admin 自身 claim 是 TEST，但新增的 IsAdminLike() 短路必须让它看到
        //          AppCode=qa_ac_* 的独立租户客户（旧实现 role != SUPER_ADMIN 会只看 TEST 1 条）。
        var client = Client.AsAdmin();

        // Act
        var body = await ReadListAsync(await client.GetAsync($"{CustomersUrl}?pageSize=50"));

        // Assert
        body.Code.Should().Be(200, $"admin 读取客户列表应成功，实际：{body.Message}");
        body.Data.Should().NotBeNull();
        body.Data!.TotalCount.Should().Be(3, "admin 等同超管，应看到全部 3 条而非仅自身 AppCode 的 1 条");
        body.Data.Items.Select(c => c.AppCode).Should().Contain(OtherAppCodeA)
            .And.Contain(OtherAppCodeB, "两个独立 AppCode 的他租户客户都必须可见");
    }

    [Fact(DisplayName = "AC-03 staff GET /customers → 只看自身 AppCode 1 条")]
    public async Task AC03_Staff_List_OnlyOwnTenant()
    {
        // Arrange：staff 拥有 view_customers（测试库播种），但非 admin ⇒ 走严格 AppCode 隔离
        var client = Client.AsRole(StaffRole, SharedTestConstants.AppCode);

        // Act
        var body = await ReadListAsync(await client.GetAsync($"{CustomersUrl}?pageSize=50"));

        // Assert
        body.Code.Should().Be(200, $"staff 读取客户列表应成功，实际：{body.Message}");
        body.Data.Should().NotBeNull();
        body.Data!.TotalCount.Should().Be(1, "普通角色严格按 AppCode 隔离");
        body.Data.Items.Should().ContainSingle()
            .Which.AppCode.Should().Be(SharedTestConstants.AppCode);
    }

    [Fact(DisplayName = "AC-14 admin GET /customers/accessible → 看全部 3 条（第 2 处独立短路）")]
    public async Task AC14_Admin_AccessibleList_SeesAllCustomers()
    {
        // Arrange：GetAccessibleCustomers 是本次改动的另一处独立短路（:115）。
        //          若只改了 GetCustomers 而漏改这里，AC-02 仍会绿、本用例会红。
        var client = Client.AsAdmin();

        // Act
        var response = await client.GetAsync($"{CustomersUrl}/accessible");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadAsync<ApiResponse<List<CustomerDto>>>(response);
        body.Should().NotBeNull();
        body!.Code.Should().Be(200, $"admin 读取可访问客户列表应成功，实际：{body.Message}");
        body.Data.Should().NotBeNull();
        body.Data!.Should().HaveCount(3, "admin 等同超管，应可访问全部租户");
        body.Data.Select(c => c.AppCode).Should().Contain(OtherAppCodeA)
            .And.Contain(OtherAppCodeB, "独立 AppCode 的他租户客户必须出现在可访问列表");
    }

    [Fact(DisplayName = "AC-15 staff GET /customers/accessible → 只看自身 AppCode 1 条")]
    public async Task AC15_Staff_AccessibleList_OnlyOwnTenant()
    {
        // Arrange
        var client = Client.AsRole(StaffRole, SharedTestConstants.AppCode);

        // Act
        var response = await client.GetAsync($"{CustomersUrl}/accessible");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadAsync<ApiResponse<List<CustomerDto>>>(response);
        body.Should().NotBeNull();
        body!.Code.Should().Be(200, $"staff 读取可访问客户列表应成功，实际：{body.Message}");
        body.Data.Should().NotBeNull();
        body.Data!.Should().ContainSingle()
            .Which.AppCode.Should().Be(SharedTestConstants.AppCode, "普通角色仍严格按 AppCode 隔离");
    }

    // ────────────────────────────── 详情归属 ──────────────────────────────

    [Fact(DisplayName = "AC-04 admin GET /customers/{他租户id} → 200（等同超管）")]
    public async Task AC04_Admin_Detail_OtherTenant_Returns200()
    {
        // Arrange
        var client = Client.AsAdmin();

        // Act
        var body = await ReadEnvelopeAsync(await client.GetAsync($"{CustomersUrl}/{_otherCustomerAId}"));

        // Assert
        body.Code.Should().Be(200, $"admin 应可访问任意租户客户详情，实际：{body.Message}");
    }

    [Fact(DisplayName = "AC-05 staff GET /customers/{他租户id} → 403")]
    public async Task AC05_Staff_Detail_OtherTenant_Returns403()
    {
        // Arrange
        var client = Client.AsRole(StaffRole, SharedTestConstants.AppCode);

        // Act
        var body = await ReadEnvelopeAsync(await client.GetAsync($"{CustomersUrl}/{_otherCustomerAId}"));

        // Assert：staff 有 view_customers，能穿过类级过滤器，被控制器归属校验挡下
        body.Code.Should().Be(403);
        body.Message.Should().Be("无权访问该客户", "403 必须来自 CustomersController 的归属校验");
    }

    // ────────────────────────────── 更新归属 ──────────────────────────────

    [Fact(DisplayName = "AC-06 admin PUT /customers/{他租户id} → 200 且真实落库")]
    public async Task AC06_Admin_Update_OtherTenant_Returns200()
    {
        // Arrange
        var client = Client.AsAdmin();
        const string newName = "QA-admin-跨租户改名";

        // Act
        var response = await client.PutAsJsonAsync(
            $"{CustomersUrl}/{_otherCustomerAId}", NewUpdateBody(newName));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadAsync<ApiResponse<CustomerDto>>(response);
        body.Should().NotBeNull();
        body!.Code.Should().Be(200, $"admin 应可修改任意租户客户，实际：{body.Message}");
        body.Data.Should().NotBeNull();
        body.Data!.AppCode.Should().Be(OtherAppCodeA, "更新不得改变客户归属");

        var storedName = await QueryDbAsync(db => db.Customers
            .Where(c => c.Id == _otherCustomerAId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync());
        storedName.Should().Be(newName, "改名必须真实落库，而不是只回显成功");
    }

    [Fact(DisplayName = "AC-07 staff PUT /customers/{他租户id} → 403 且未改动")]
    public async Task AC07_Staff_Update_OtherTenant_Returns403()
    {
        // Arrange
        var client = Client.AsRole(StaffRole, SharedTestConstants.AppCode);

        // Act
        var body = await ReadEnvelopeAsync(await client.PutAsJsonAsync(
            $"{CustomersUrl}/{_otherCustomerAId}", NewUpdateBody("QA-staff-越权改名")));

        // Assert
        body.Code.Should().Be(403, "staff 无 update_customers 权限且不得跨租户修改");

        var storedName = await QueryDbAsync(db => db.Customers
            .Where(c => c.Id == _otherCustomerAId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync());
        storedName.Should().Be("QA-他租户客户A", "越权请求不得产生任何落库副作用");
    }

    [Fact(DisplayName = "AC-07b 普通写角色 PUT 他租户 → 403「无权修改该客户」（精确打中归属校验）")]
    public async Task AC07b_TenantWriter_Update_OtherTenant_HitsOwnershipGuard()
    {
        // Arrange：该角色拥有 update_customers，能穿过方法级 PermissionAuthorize；
        //          若控制器 :301 的 !IsAdminLike() 归属校验丢失，这里会返回 200 → 用例变红。
        var client = Client.AsRole(TenantWriterRole, SharedTestConstants.AppCode);

        // Act
        var body = await ReadEnvelopeAsync(await client.PutAsJsonAsync(
            $"{CustomersUrl}/{_otherCustomerAId}", NewUpdateBody("QA-writer-越权改名")));

        // Assert
        body.Code.Should().Be(403);
        body.Message.Should().Be("无权修改该客户", "必须来自控制器归属校验，而非权限过滤器");
    }

    // ────────────────────────────── 删除归属（本轮新增校验） ──────────────────────────────

    [Fact(DisplayName = "AC-08 admin DELETE 他租户无关联客户 → 200 且真实删除")]
    public async Task AC08_Admin_Delete_OtherTenant_Returns200()
    {
        // Arrange：OtherCustomerB 无任何 Device(AppCode) / User(CustomerId) / Project(CustomerId) 关联
        var client = Client.AsAdmin();

        // Act
        var body = await ReadEnvelopeAsync(await client.DeleteAsync($"{CustomersUrl}/{_otherCustomerBId}"));

        // Assert
        body.Code.Should().Be(200, $"admin 应可删除任意租户的无关联客户，实际：{body.Message}");

        var stillExists = await QueryDbAsync(db => db.Customers.AnyAsync(c => c.Id == _otherCustomerBId));
        stillExists.Should().BeFalse("删除必须真实落库");
    }

    [Fact(DisplayName = "AC-09 staff DELETE 他租户客户 → 403 且客户仍在库")]
    public async Task AC09_Staff_Delete_OtherTenant_Returns403()
    {
        // Arrange
        var client = Client.AsRole(StaffRole, SharedTestConstants.AppCode);

        // Act
        var body = await ReadEnvelopeAsync(await client.DeleteAsync($"{CustomersUrl}/{_otherCustomerAId}"));

        // Assert
        body.Code.Should().Be(403);

        var stillExists = await QueryDbAsync(db => db.Customers.AnyAsync(c => c.Id == _otherCustomerAId));
        stillExists.Should().BeTrue("越权删除必须被拒绝，客户不得消失");
    }

    [Fact(DisplayName = "AC-09b 普通写角色 DELETE 他租户 → 403「无权删除该客户」（修复原越权漏洞）")]
    public async Task AC09b_TenantWriter_Delete_OtherTenant_HitsNewOwnershipGuard()
    {
        // Arrange：旧版 DeleteCustomer 完全没有归属校验，任何持有 delete_customers 的普通角色
        //          都能删掉他租户客户 → 旧代码本用例会返回 200 且客户消失。
        //          该角色有 delete_customers 权限，能穿过过滤器直达控制器 :360。
        var client = Client.AsRole(TenantWriterRole, SharedTestConstants.AppCode);

        // Act
        var body = await ReadEnvelopeAsync(await client.DeleteAsync($"{CustomersUrl}/{_otherCustomerAId}"));

        // Assert
        body.Code.Should().Be(403);
        body.Message.Should().Be("无权删除该客户", "必须命中本次新增的 DeleteCustomer 归属校验");

        var stillExists = await QueryDbAsync(db => db.Customers.AnyAsync(c => c.Id == _otherCustomerAId));
        stillExists.Should().BeTrue("归属校验必须在 Remove 之前生效，客户不得被越权删除");
    }

    // ────────────────────────────── 创建：每客户独立 AppCode ──────────────────────────────

    [Fact(DisplayName = "AC-10 超管 POST 两个全新 AppCode → 均 200 且各自独立落库")]
    public async Task AC10_Admin_Create_TwoDistinctAppCodes_PersistedIndependently()
    {
        // Arrange：配额需求 R01 后创建客户仅 super_admin 可达（admin 直调返回 403），
        //          故改用超管验证「AppCode 采信请求体、每客户独立」这一原有意图。
        //          若实现把 effectiveAppCode 强制成自身 claim，撞「system 已存在」→ 用例变红。
        var client = Client.AsSuperAdmin();

        // Act
        var first = await client.PostAsJsonAsync(
            CustomersUrl, NewCreateBody("QA-新租户1", FreshCode, FreshAppCode));
        var second = await client.PostAsJsonAsync(
            CustomersUrl, NewCreateBody("QA-新租户2", FreshCode2, FreshAppCode2));

        // Assert
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);

        var body1 = await ReadAsync<ApiResponse<CustomerDto>>(first);
        var body2 = await ReadAsync<ApiResponse<CustomerDto>>(second);
        body1.Should().NotBeNull();
        body2.Should().NotBeNull();
        body1!.Code.Should().Be(200, $"第一次创建应成功，实际：{body1.Message}");
        body2!.Code.Should().Be(200, $"第二次创建应成功，实际：{body2.Message}");
        body1.Data.Should().NotBeNull();
        body2.Data.Should().NotBeNull();
        body1.Data!.AppCode.Should().Be(FreshAppCode, "落库/返回必须采信 request.AppCode");
        body2.Data!.AppCode.Should().Be(FreshAppCode2, "每个客户拥有独立 AppCode");

        var stored = await QueryDbAsync(db => db.Customers
            .Where(c => c.AppCode == FreshAppCode || c.AppCode == FreshAppCode2)
            .Select(c => c.AppCode)
            .ToListAsync());
        stored.Should().BeEquivalentTo(new[] { FreshAppCode, FreshAppCode2 },
            "两个全新 AppCode 必须真实落库，互不相同");
    }

    [Fact(DisplayName = "AC-11 超管重复 AppCode → 400「客户代码或应用代码已存在」")]
    public async Task AC11_Admin_Create_DuplicateAppCode_Returns400()
    {
        // Arrange：R01 后仅超管可创建客户，改用超管验证唯一性分支。
        var client = Client.AsSuperAdmin();
        var first = await client.PostAsJsonAsync(
            CustomersUrl, NewCreateBody("QA-首次", FreshCode, FreshAppCode));
        var firstBody = await ReadAsync<ApiResponse<CustomerDto>>(first);
        firstBody.Should().NotBeNull();
        firstBody!.Code.Should().Be(200, "前置条件：首个客户必须创建成功");

        // Act：换一个不同的 Code，但复用同一个 AppCode ⇒ 命中 AppCode 唯一性分支
        var response = await client.PostAsJsonAsync(
            CustomersUrl, NewCreateBody("QA-重复", $"{_prefix}_cdup", FreshAppCode));

        // Assert
        var body = await ReadEnvelopeAsync(response);
        body.Code.Should().Be(400);
        body.Message.Should().Contain("已存在");
    }

    [Fact(DisplayName = "AC-12 超管空 AppCode → 400「应用代码不能为空」")]
    public async Task AC12_Admin_Create_EmptyAppCode_Returns400()
    {
        // Arrange：R01 后仅超管可创建客户，改用超管验证空 AppCode 分支。
        var client = Client.AsSuperAdmin();

        // Act
        var body = await ReadEnvelopeAsync(await client.PostAsJsonAsync(
            CustomersUrl, NewCreateBody("QA-空归属", $"{_prefix}_cempty", string.Empty)));

        // Assert
        body.Code.Should().Be(400);
        body.Message.Should().Be("应用代码不能为空");
    }

    [Fact(DisplayName = "AC-13 staff 带他租户 AppCode 创建 → 403（纵深防御）")]
    public async Task AC13_Staff_Create_WithForeignAppCode_Returns403()
    {
        // Arrange：staff 无 create_customers，方法级 PermissionAuthorize 先行拦截
        var client = Client.AsRole(StaffRole, SharedTestConstants.AppCode);

        // Act
        var body = await ReadEnvelopeAsync(await client.PostAsJsonAsync(
            CustomersUrl, NewCreateBody("QA-staff越权建", $"{_prefix}_cstaff", OtherAppCodeA)));

        // Assert
        body.Code.Should().Be(403, "普通角色不得为他租户创建客户");
    }

    [Fact(DisplayName = "AC-13b 普通写角色带他租户 AppCode 创建 → 403（R01 超管硬校验）")]
    public async Task AC13b_TenantWriter_Create_WithForeignAppCode_HitsGuard()
    {
        // Arrange：配额需求 R01 / 决策 1 后，即使该角色持有 create_customers，
        //          CreateCustomer 顶部 Roles.IsSuperAdmin() 硬校验仍先行拒绝（403），
        //          不得再期待命中旧的「无权为该租户创建客户」纵深防御分支。
        var client = Client.AsRole(TenantWriterRole, SharedTestConstants.AppCode);

        // Act
        var createCode = $"{_prefix}_cwriter";
        var body = await ReadEnvelopeAsync(await client.PostAsJsonAsync(
            CustomersUrl, NewCreateBody("QA-writer越权建", createCode, OtherAppCodeA)));

        // Assert
        body.Code.Should().Be(403, "R01：非超管不得创建客户，含绕过前端直调");
        body.Message.Should().Be("仅超级管理员可创建客户/租户。");

        var stored = await QueryDbAsync(db => db.Customers.AnyAsync(c => c.Code == createCode));
        stored.Should().BeFalse("被拒请求不得入库");
    }

    [Fact(DisplayName = "AC-13c 普通写角色空 AppCode 创建 → 403 且无入库（R01 超管硬校验）")]
    public async Task AC13c_TenantWriter_Create_EmptyAppCode_ForcedToOwnClaim()
    {
        // Arrange：R01 后普通写角色不再具备建客能力，即使请求体 AppCode 留空也不得建档。
        var client = Client.AsRole(TenantWriterRole, WriterAppCode);

        // Act
        var createCode = $"{_prefix}_cself";
        var body = await ReadEnvelopeAsync(await client.PostAsJsonAsync(
            CustomersUrl, NewCreateBody("QA-writer自建", createCode, string.Empty)));

        // Assert
        body.Code.Should().Be(403, "R01：非超管即使空 AppCode 也不得创建客户");
        body.Message.Should().Be("仅超级管理员可创建客户/租户。");

        var stored = await QueryDbAsync(db => db.Customers.AnyAsync(c => c.Code == createCode));
        stored.Should().BeFalse("被拒请求不得入库");
    }

    // ────────────────────────────── 辅助 ──────────────────────────────

    /// <summary>追加一条无关联数据的客户，返回 MySQL 自增主键。</summary>
    private Task<long> AddCustomerAsync(string code, string appCode, string name) =>
        QueryDbAsync(async db =>
        {
            var now = DateTime.UtcNow;
            var customer = new Customer
            {
                Name = name,
                Code = code,
                AppCode = appCode,
                ContactPerson = "QA-联系人",
                Status = "active",
                MaxDeviceCount = 100,
                MaxUserCount = 10,
                MaxAreaCount = 10,
                CreatedAt = now,
                UpdatedAt = now
            };

            db.Customers.Add(customer);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            return customer.Id;
        });

    private static CreateCustomerRequest NewCreateBody(string name, string code, string appCode) => new()
    {
        Name = name,
        Code = code,
        AppCode = appCode,
        Contact = "QA-联系人",
        Phone = "13800000000",
        Address = "QA-地址"
    };

    private static UpdateCustomerRequest NewUpdateBody(string name) => new()
    {
        Name = name,
        Contact = "QA-改",
        Phone = "13900000000",
        Address = "QA-改地址",
        Status = "active"
    };

    /// <summary>读取客户列表响应（强类型，供 AppCode 集合断言）。</summary>
    private async Task<ApiResponse<PagedResponse<CustomerDto>>> ReadListAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadAsync<ApiResponse<PagedResponse<CustomerDto>>>(response);
        body.Should().NotBeNull("客户列表必须返回可反序列化的 ApiResponse 包体");
        return body!;
    }

    /// <summary>读取通用响应包体（只关心 Code / Message，回避 CustomerDetailDto 的隐藏成员）。</summary>
    private async Task<ApiResponse<object>> ReadEnvelopeAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, "业务失败在本平台仍返回 HTTP 200 + 包体 Code");
        var body = await ReadAsync<ApiResponse<object>>(response);
        body.Should().NotBeNull("响应必须返回可反序列化的 ApiResponse 包体");
        return body!;
    }
}
