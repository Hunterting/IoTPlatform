using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using IoTPlatform.Configuration;
using IoTPlatform.Controllers;
using IoTPlatform.Data;
using IoTPlatform.Data.Repositories.Implementations;
using IoTPlatform.Data.Repositories.Interfaces;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.Helpers;
using IoTPlatform.Infrastructure.JWT;
using IoTPlatform.Models;
using IoTPlatform.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;
using UserResponseDto = IoTPlatform.DTOs.Responses.UserDto;

namespace IoTPlatform.AnSheng.Tests;

/// <summary>
/// 多客户授权模型 T05.1 —— 端到端隔离集成测试（设计 §8 清单 1~6 / PRD G1、G2、G3）。
///
/// <para>【与 HTTP 集成测试的区别】这里用真实 DbContext + 真实仓储 + 真实服务 + 真实控制器直接串联，
/// 跳过权限过滤器/中间件（那部分由 IntegrationTests 的 CustomerAdminBoundaryTests 覆盖），
/// 因而能在零外部依赖的前提下验证「控制器调用 ICustomerScopeService 收口」这一核心链路。</para>
///
/// <para>【覆盖】两 admin 批次互不可见、越权建户 403、下级零回归、同客户多 admin、
/// 授权变更对旧 token 立竿见影、DeleteCustomer 的 user_customers RESTRICT 前置。</para>
///
/// <para>【响应口径】业务成败一律断言 ApiResponse.Code（HTTP 恒 200），不使用 HTTP 403 状态码。</para>
/// </summary>
public sealed class MultiCustomerIsolationTests : IDisposable
{
    private const string SuperEmail = "super@x.local";
    private const string AdminAEmail = "adminA@x.local";
    private const string AdminBEmail = "adminB@x.local";

    private readonly AppDbContext _db;
    private readonly IUserCustomerRepository _grants;
    private readonly ICustomerScopeService _scope;
    private readonly CustomersController _customers;
    private readonly AdminCustomerGrantsController _grantApi;
    private readonly UsersController _users;

    public MultiCustomerIsolationTests()
    {
        _db = CustomerScopeServiceTests.CreateDb($"isolation-{Guid.NewGuid():N}");

        for (long id = 1; id <= 4; id++)
        {
            _db.Customers.Add(new Customer
            {
                Id = id,
                Name = $"客户{id}",
                Code = $"C{id:000}",
                AppCode = $"customer_{id:000}",
                Status = "active",
                MaxDeviceCount = 100,
                MaxUserCount = 100,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }
        SeedUser(1, SuperEmail, Roles.SUPER_ADMIN, isSuperAdmin: true);
        SeedUser(10, AdminAEmail, Roles.ADMIN);
        SeedUser(11, AdminBEmail, Roles.ADMIN);
        SeedUser(20, "chef@x.local", Roles.CHEF, customerId: 1, appCode: "customer_001");
        _db.SaveChanges();

        _grants = new UserCustomerRepository(_db);
        var customerRepo = new CustomerRepository(_db);
        _scope = new CustomerScopeService(_grants, customerRepo);

        _customers = new CustomersController(_db, _scope)
        {
            ControllerContext = ControllerContextFor(AdminAPrincipal())
        };
        _grantApi = new AdminCustomerGrantsController(_db, _grants)
        {
            ControllerContext = ControllerContextFor(SuperPrincipal())
        };
        var userService = new UserService(
            new UserRepository(_db),
            customerRepo,
            new LogRepository(_db),
            new WorkOrderRepository(_db),
            new UnitOfWork(_db));
        _users = new UsersController(userService, _scope)
        {
            ControllerContext = ControllerContextFor(AdminAPrincipal())
        };

        // 基线：A→[1,2]，B→[3]（§8 清单 1a）。
        _grants.SyncAsync(10, new long[] { 1, 2 }, 1).GetAwaiter().GetResult();
        _grants.SyncAsync(11, new long[] { 3 }, 1).GetAwaiter().GetResult();
        _db.ChangeTracker.Clear();
    }

    public void Dispose() => _db.Dispose();

    // ───────────────────────────── G1：两 admin 互不可见 ─────────────────────────────

    [Fact(DisplayName = "ISO-01 两 admin 不同批次互不可见；越权详情 403；accessible 同语义")]
    public async Task TwoAdmins_DifferentBatches_AreInvisibleToEachOther()
    {
        _customers.ControllerContext.HttpContext.User = AdminAPrincipal();
        var aList = ListEnvelope(await _customers.GetCustomers(pageSize: 50));
        Assert.Equal(200, aList.Code);
        Assert.Equal(new long[] { 1, 2 }, aList.Data!.Items.Select(c => c.Id).OrderBy(id => id));
        Assert.Equal(2, aList.Data.TotalCount);

        _customers.ControllerContext.HttpContext.User = AdminBPrincipal();
        var bList = ListEnvelope(await _customers.GetCustomers(pageSize: 50));
        Assert.Equal(new long[] { 3 }, bList.Data!.Items.Select(c => c.Id));

        _customers.ControllerContext.HttpContext.User = AdminAPrincipal();
        Assert.Equal(403, CodeOnly(await _customers.GetCustomer(3)));

        _customers.ControllerContext.HttpContext.User = AdminBPrincipal();
        Assert.Equal(403, CodeOnly(await _customers.GetCustomer(1)));

        _customers.ControllerContext.HttpContext.User = AdminAPrincipal();
        var accessible = UnwrapGeneric<List<CustomerDto>>(await _customers.GetAccessibleCustomers());
        Assert.Equal(200, accessible.Code);
        Assert.Equal(new long[] { 1, 2 }, accessible.Data!.Select(c => c.Id).OrderBy(id => id));

        // 超管仍看全量（D2），三分支语义不被 admin 改造波及。
        _customers.ControllerContext.HttpContext.User = SuperPrincipal();
        var all = ListEnvelope(await _customers.GetCustomers(pageSize: 50));
        Assert.Equal(4, all.Data!.TotalCount);
    }

    // ───────────────────────────── G2：越权建户 ─────────────────────────────

    [Fact(DisplayName = "ISO-02 admin 授权内建户成功且 AppCode=customer.AppCode，不自动扩大授权集合（D9）")]
    public async Task Admin_CreateUser_InGrant_SucceedsWithoutExpandingGrants()
    {
        _users.ControllerContext.HttpContext.User = AdminAPrincipal();
        var request = NewUserRequest("newchef@x.local", 1);

        var response = UnwrapGeneric<UserResponseDto>(await _users.CreateUser(request));

        Assert.Equal(200, response.Code);
        Assert.NotNull(response.Data);
        Assert.Equal("customer_001", response.Data!.AppCode);
        var stored = await _db.Users.AsNoTracking().IgnoreQueryFilters()
            .FirstAsync(u => u.Email == "newchef@x.local");
        Assert.Equal(1, stored.CustomerId);
        Assert.Equal("customer_001", stored.AppCode);
        // 建户不得顺带写 user_customers（D9）：授权集合仍与基线一致。
        Assert.Equal(new long[] { 1, 2 }, await _grants.GetCustomerIdsAsync(10));
    }

    [Fact(DisplayName = "ISO-03 admin 授权外建户 → Code 403 且零 INSERT")]
    public async Task Admin_CreateUser_OutsideGrant_Returns403_NoInsert()
    {
        _users.ControllerContext.HttpContext.User = AdminAPrincipal();
        var request = NewUserRequest("bad@x.local", 3);

        var response = UnwrapGeneric<UserResponseDto>(await _users.CreateUser(request));

        Assert.Equal(403, response.Code);
        Assert.False(await _db.Users.AsNoTracking().IgnoreQueryFilters()
            .AnyAsync(u => u.Email == "bad@x.local"));
    }

    // ───────────────────────────── G3：下级零回归 ─────────────────────────────

    [Fact(DisplayName = "ISO-04 下级 Single：客户列表仅自身一条，越权详情/建户判定逐字不变")]
    public async Task Subordinate_ZeroRegression_StaysSingleTenant()
    {
        var subordinate = SubordinatePrincipal();
        var scope = await _scope.ResolveAsync(subordinate);
        Assert.Equal(CustomerScopeMode.Single, scope.Mode);
        Assert.Equal(new long[] { 1 }, scope.CustomerIds);
        Assert.Equal(new[] { "customer_001" }, scope.AppCodes);
        Assert.True(await _scope.CanAccessCustomerAsync(subordinate, 1));
        Assert.False(await _scope.CanAccessCustomerAsync(subordinate, 2));

        _customers.ControllerContext.HttpContext.User = subordinate;
        var list = ListEnvelope(await _customers.GetCustomers(pageSize: 50));
        Assert.Equal(200, list.Code);
        Assert.Equal(new long[] { 1 }, list.Data!.Items.Select(c => c.Id));
        Assert.Equal(1, list.Data.TotalCount);
    }

    [Fact(DisplayName = "ISO-05 JWT 形态：admin 签 ManagedCustomerIds 且无单值；下级单值逐字段保留")]
    public void JwtShape_Admin集合Vs下级单值()
    {
        var jwt = new JwtHelper(CreateJwtConfiguration());
        var adminToken = jwt.GenerateToken(10, AdminAEmail, Roles.ADMIN, null, null, "1,2,3", "1,2");
        var chefToken = jwt.GenerateToken(20, "chef@x.local", Roles.CHEF, 1, "customer_001", "1");

        var adminClaims = ReadToken(adminToken).Claims;
        Assert.Equal("1,2", adminClaims.First(c => c.Type == CustomerScopeConstants.ClaimManagedCustomerIds).Value);
        Assert.DoesNotContain(adminClaims, c => c.Type == "AppCode");
        Assert.DoesNotContain(adminClaims, c => c.Type == "CustomerId");
        Assert.Equal(new long[] { 1, 2 }, jwt.GetManagedCustomerIdsFromToken(adminToken));

        var chefClaims = ReadToken(chefToken).Claims;
        Assert.DoesNotContain(chefClaims, c => c.Type == CustomerScopeConstants.ClaimManagedCustomerIds);
        Assert.Equal("1", chefClaims.First(c => c.Type == "CustomerId").Value);
        Assert.Equal("customer_001", chefClaims.First(c => c.Type == "AppCode").Value);
        Assert.Null(jwt.GetManagedCustomerIdsFromToken(chefToken));
    }

    // ───────────────────────────── D6 / P-E / RESTRICT ─────────────────────────────

    [Fact(DisplayName = "ISO-06 同客户多 admin：B 也拿到客户 1 后两者都可见，反查含 10 与 11")]
    public async Task SameCustomer_MultipleAdmins_BothCanSeeIt()
    {
        var assign = UnwrapGeneric<ManagedCustomerGrantDto>(
            await _grantApi.Assign(11, new AssignManagedCustomersRequest(new long[] { 1, 3 })));
        Assert.Equal(200, assign.Code);
        Assert.Equal(new long[] { 10, 11 }, await _grants.GetAdminIdsByCustomerIdAsync(1));

        _customers.ControllerContext.HttpContext.User = AdminAPrincipal();
        var a = ListEnvelope(await _customers.GetCustomers(pageSize: 50));
        Assert.Contains(1L, a.Data!.Items.Select(c => c.Id));

        _customers.ControllerContext.HttpContext.User = AdminBPrincipal();
        var b = ListEnvelope(await _customers.GetCustomers(pageSize: 50));
        Assert.Equal(new long[] { 1, 3 }, b.Data!.Items.Select(c => c.Id).OrderBy(id => id));
    }

    [Fact(DisplayName = "ISO-07 分配变更即时生效：旧 token（claim 仍是 [1,2]）下次请求看到新增/移除")]
    public async Task GrantChange_TakesEffectWithStaleToken()
    {
        // 旧 token 快照恒为 "1,2"，后续请求不再重新登录。
        var staleAdminA = AdminAPrincipal();

        var added = UnwrapGeneric<ManagedCustomerGrantDto>(
            await _grantApi.Assign(10, new AssignManagedCustomersRequest(new long[] { 1, 2, 4 })));
        Assert.Equal(200, added.Code);

        _customers.ControllerContext.HttpContext.User = staleAdminA;
        var afterAdd = ListEnvelope(await _customers.GetCustomers(pageSize: 50));
        Assert.Equal(new long[] { 1, 2, 4 }, afterAdd.Data!.Items.Select(c => c.Id).OrderBy(id => id));

        var removed = UnwrapGeneric<ManagedCustomerGrantDto>(
            await _grantApi.Assign(10, new AssignManagedCustomersRequest(new long[] { 1, 2 })));
        Assert.Equal(200, removed.Code);

        _customers.ControllerContext.HttpContext.User = staleAdminA;
        Assert.Equal(403, CodeOnly(await _customers.GetCustomer(4)));
    }

    [Fact(DisplayName = "ISO-08 DeleteCustomer：被 user_customers 引用 → Code 400；无引用客户可正常删除")]
    public async Task DeleteCustomer_GrantReference_IsRestricted()
    {
        _customers.ControllerContext.HttpContext.User = SuperPrincipal();

        var blockedCode = PlainCode(await _customers.DeleteCustomer(1));
        Assert.Equal(400, blockedCode);
        Assert.True(await _db.Customers.AsNoTracking().IgnoreQueryFilters().AnyAsync(c => c.Id == 1));

        // 客户 4 无授权引用、无设备/用户/项目关联，删除应成功。
        var okCode = PlainCode(await _customers.DeleteCustomer(4));
        Assert.Equal(200, okCode);
        Assert.False(await _db.Customers.AsNoTracking().IgnoreQueryFilters().AnyAsync(c => c.Id == 4));
    }

    // ───────────────────────────── 夹具 ─────────────────────────────

    private void SeedUser(long id, string email, string role, bool isSuperAdmin = false,
        long? customerId = null, string? appCode = null) => _db.Users.Add(new User
    {
        Id = id,
        Username = $"u{id}",
        Email = email,
        Password = "!not-a-real-hash!",
        FullName = $"用户{id}",
        Role = role,
        Status = "active",
        IsActive = true,
        IsSuperAdmin = isSuperAdmin,
        CustomerId = customerId,
        AppCode = appCode,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    });

    private static ClaimsPrincipal SuperPrincipal() => Principal(1, Roles.SUPER_ADMIN);

    /// <summary>A 的 token 快照固定为 "1,2"——用于验证 claim 不是授权依据。</summary>
    private static ClaimsPrincipal AdminAPrincipal() => Principal(10, Roles.ADMIN, managedClaim: "1,2");

    private static ClaimsPrincipal AdminBPrincipal() => Principal(11, Roles.ADMIN, managedClaim: "3");

    private static ClaimsPrincipal SubordinatePrincipal() => Principal(
        20, Roles.CHEF, customerId: 1, appCode: "customer_001");

    private static ClaimsPrincipal Principal(long userId, string role, long? customerId = null,
        string? appCode = null, string? managedClaim = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Role, role)
        };
        if (customerId.HasValue) claims.Add(new Claim("CustomerId", customerId.Value.ToString()));
        if (!string.IsNullOrEmpty(appCode)) claims.Add(new Claim("AppCode", appCode));
        if (!string.IsNullOrEmpty(managedClaim))
        {
            claims.Add(new Claim(CustomerScopeConstants.ClaimManagedCustomerIds, managedClaim));
        }
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private static ControllerContext ControllerContextFor(ClaimsPrincipal principal) => new()
    {
        HttpContext = new DefaultHttpContext { User = principal }
    };

    private static CreateUserRequest NewUserRequest(string email, long customerId) => new()
    {
        Name = "测试新建用户",
        Email = email,
        Password = "abc123",
        Role = Roles.CHEF,
        CustomerId = customerId,
        IsActive = true
    };

    private static ApiResponse<PagedResponse<CustomerDto>> ListEnvelope(
        ActionResult<ApiResponse<PagedResponse<CustomerDto>>> result) => UnwrapGeneric(result);

    // 控制器既可能 return ApiResponse<T>（落到 ActionResult<T>.Value），
    // 也可能 return Ok(ApiResponse<T>)（落到 ActionResult<T>.Result = OkObjectResult）；两种都要兼容。
    private static object? UnwrapValue<T>(ActionResult<T> result) =>
        result.Result is OkObjectResult ok ? ok.Value : result.Value;

    private static ApiResponse<T> UnwrapGeneric<T>(ActionResult<ApiResponse<T>> result) =>
        Assert.IsType<ApiResponse<T>>(UnwrapValue(result));

    /// <summary>403 分支由 ApiResponse.Forbidden 产生非泛型包体，只需读机器可读的 Code。</summary>
    private static int CodeOnly<T>(ActionResult<ApiResponse<T>> result) => UnwrapValue(result) switch
    {
        ApiResponse<T> typed => typed.Code,
        ApiResponse<object> plain => plain.Code,
        var other => throw new Xunit.Sdk.XunitException($"未知响应形态：{other?.GetType().FullName ?? "null"}")
    };

    /// <summary>DeleteCustomer 的非泛型 ApiResponse 在 ActionResult 里以 ApiResponse&lt;object&gt; 形态承载，
    /// 这里只取机器可读的 Code，不做具体类型断言。</summary>
    private static int PlainCode(ActionResult<ApiResponse> result) => UnwrapValue(result) switch
    {
        ApiResponse plain => plain.Code,
        ApiResponse<object> generic => generic.Code,
        var other => throw new Xunit.Sdk.XunitException($"未知响应形态：{other?.GetType().FullName ?? "null"}")
    };

    private static JwtSecurityToken ReadToken(string token) =>
        new JwtSecurityTokenHandler().ReadJwtToken(token);

    private static IConfiguration CreateJwtConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:SecretKey"] = "TEST_ONLY_FakeJwtSecret_NotForProduction_0123456789abcdef",
                ["Jwt:Issuer"] = "IoTPlatform",
                ["Jwt:Audience"] = "IoTPlatform",
                ["Jwt:ExpirationMinutes"] = "1440"
            })
            .Build();
}
