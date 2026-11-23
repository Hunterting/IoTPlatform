using System.Security.Claims;
using IoTPlatform.Configuration;
using IoTPlatform.Controllers;
using IoTPlatform.Data;
using IoTPlatform.Data.Repositories.Implementations;
using IoTPlatform.Data.Repositories.Interfaces;
using IoTPlatform.Helpers;
using IoTPlatform.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IoTPlatform.AnSheng.Tests;

/// <summary>
/// 多客户授权模型 T05.1 —— 分配 API 与授权仓储验收（设计 §6 T04.1 / §8 清单 4、7）。
///
/// <para>【覆盖】幂等全量替换、上限 50、空集合 400、非超管 403、目标非 admin 400、
/// 同一客户多 admin（D6）、按客户反查 admin、重复分配不产生重复行。</para>
///
/// <para>【响应口径】全站 HTTP 200 + ApiResponse.Code；本类直接调用控制器，
/// 断言落在 <c>Code</c> 上（不是 HTTP 状态码，也不是中文文案）。</para>
///
/// <para>【零外部依赖】EF InMemory + 真实 <see cref="UserCustomerRepository"/>；
/// 忽略 TransactionIgnoredWarning 后，SyncAsync 的单事务删差集/插并集路径可被真实执行。</para>
/// </summary>
public sealed class UserCustomerGrantTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IUserCustomerRepository _grants;
    private readonly AdminCustomerGrantsController _sut;

    public UserCustomerGrantTests()
    {
        _db = CustomerScopeServiceTests.CreateDb($"grants-{Guid.NewGuid():N}");
        _grants = new UserCustomerRepository(_db);
        _sut = new AdminCustomerGrantsController(_db, _grants)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = Principal(1, Roles.SUPER_ADMIN) }
            }
        };

        // 客户 1..5 + 两个 admin（10/11）+ 一个 chef（20，不得作为分配目标）。
        for (long id = 1; id <= 5; id++)
        {
            _db.Customers.Add(new Customer
            {
                Id = id,
                Name = $"客户{id}",
                Code = $"C{id:000}",
                AppCode = $"customer_{id:000}",
                Status = "active",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }
        SeedUser(10, "adminA@x.local", Roles.ADMIN);
        SeedUser(11, "adminB@x.local", Roles.ADMIN);
        SeedUser(20, "chef@x.local", Roles.CHEF);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
    }

    public void Dispose() => _db.Dispose();

    // ───────────────────────────── 分配主_path ─────────────────────────────

    [Fact(DisplayName = "GRANT-01 超管分配 [1,2] → Code 200 且 DB 真实持有集合")]
    public async Task Assign_SuperAdmin_PersistsGrants()
    {
        var response = Unwrap(await _sut.Assign(10, new AssignManagedCustomersRequest(new long[] { 1, 2 })));

        Assert.Equal(200, response.Code);
        Assert.NotNull(response.Data);
        Assert.Equal(10, response.Data!.UserId);
        Assert.Equal(new long[] { 1, 2 }, response.Data.CustomerIds);
        Assert.Equal(new long[] { 1, 2 }, await _grants.GetCustomerIdsAsync(10));
    }

    [Fact(DisplayName = "GRANT-02 重复分配同一集合 → 幂等，user_customers 无重复行")]
    public async Task Assign_SamePayloadTwice_IsIdempotent()
    {
        var first = Unwrap(await _sut.Assign(10, new AssignManagedCustomersRequest(new long[] { 1, 2 })));
        var second = Unwrap(await _sut.Assign(10, new AssignManagedCustomersRequest(new long[] { 1, 2 })));

        Assert.Equal(200, first.Code);
        Assert.Equal(200, second.Code);
        Assert.Equal(new long[] { 1, 2 }, await _grants.GetCustomerIdsAsync(10));
        Assert.Equal(2, await _db.UserCustomers.AsNoTracking().IgnoreQueryFilters()
            .CountAsync(uc => uc.UserId == 10));
    }

    [Fact(DisplayName = "GRANT-03 全量替换 [1,2]→[2,3] → 删差集插并集，审计列写入授予人")]
    public async Task Assign_ReplacesSet_RemovesAndAdds()
    {
        await _sut.Assign(10, new AssignManagedCustomersRequest(new long[] { 1, 2 }));

        var response = Unwrap(await _sut.Assign(10, new AssignManagedCustomersRequest(new long[] { 2, 3 })));

        Assert.Equal(200, response.Code);
        Assert.Equal(new long[] { 2, 3 }, await _grants.GetCustomerIdsAsync(10));
        Assert.False(await _grants.ExistsAsync(10, 1));
        Assert.True(await _grants.ExistsAsync(10, 2));
        Assert.True(await _grants.ExistsAsync(10, 3));
        var row = await _db.UserCustomers.AsNoTracking().IgnoreQueryFilters()
            .FirstAsync(uc => uc.UserId == 10 && uc.CustomerId == 3);
        Assert.Equal(1, row.GrantedByUserId);
        Assert.Equal(CustomerScopeConstants.SourceManual, row.Source);
        Assert.Null(row.RevokedAt);
    }

    [Fact(DisplayName = "GRANT-04 输入含重复 ID → Code 400，不落库")]
    public async Task Assign_DuplicateIds_Returns400()
    {
        var response = Unwrap(await _sut.Assign(10, new AssignManagedCustomersRequest(new long[] { 1, 1, 2 })));

        Assert.Equal(400, response.Code);
        Assert.Empty(await _grants.GetCustomerIdsAsync(10));
    }

    // ───────────────────────────── 权限与边界（§8 清单 7） ─────────────────────────────

    [Fact(DisplayName = "GRANT-05 非超管分配 → Code 403 且零写入")]
    public async Task Assign_NonSuperAdmin_Returns403()
    {
        _sut.ControllerContext.HttpContext.User = Principal(10, Roles.ADMIN);

        var response = Unwrap(await _sut.Assign(11, new AssignManagedCustomersRequest(new long[] { 1 })));

        Assert.Equal(403, response.Code);
        Assert.Empty(await _grants.GetCustomerIdsAsync(11));
    }

    [Fact(DisplayName = "GRANT-06 空集合 → Code 400（集合非空校验）")]
    public async Task Assign_EmptySet_Returns400()
    {
        var response = Unwrap(await _sut.Assign(10,
            new AssignManagedCustomersRequest(Array.Empty<long>())));

        Assert.Equal(400, response.Code);
    }

    [Fact(DisplayName = "GRANT-07 超过 50 上限 → Code 400 且零写入")]
    public async Task Assign_OverMaxLimit_Returns400()
    {
        // 上限校验发生在客户存在性校验之前，故无需真的播种 51 个客户。
        var ids = Enumerable.Range(1, CustomerScopeConstants.MaxManagedCustomersPerAdmin + 1)
            .Select(i => (long)i).ToArray();

        var response = Unwrap(await _sut.Assign(10, new AssignManagedCustomersRequest(ids)));

        Assert.Equal(400, response.Code);
        Assert.Empty(await _grants.GetCustomerIdsAsync(10));
    }

    [Fact(DisplayName = "GRANT-08 分配目标非 admin → Code 400（下级不得成为集合型用户）")]
    public async Task Assign_NonAdminTarget_Returns400()
    {
        var response = Unwrap(await _sut.Assign(20, new AssignManagedCustomersRequest(new long[] { 1 })));

        Assert.Equal(400, response.Code);
        Assert.Empty(await _grants.GetCustomerIdsAsync(20));
    }

    [Fact(DisplayName = "GRANT-09 授权集合含不存在客户 → Code 400，不留悬空授权")]
    public async Task Assign_UnknownCustomer_Returns400()
    {
        var response = Unwrap(await _sut.Assign(10, new AssignManagedCustomersRequest(new long[] { 1, 999 })));

        Assert.Equal(400, response.Code);
        Assert.Empty(await _grants.GetCustomerIdsAsync(10));
    }

    // ───────────────────────────── 多对多与反查（D6 / §8 清单 4） ─────────────────────────────

    [Fact(DisplayName = "GRANT-10 同一客户授予两个 admin → 各自持有，按客户反查含两者")]
    public async Task Assign_SameCustomerToTwoAdmins_IsManyToMany()
    {
        await _sut.Assign(10, new AssignManagedCustomersRequest(new long[] { 1 }));
        await _sut.Assign(11, new AssignManagedCustomersRequest(new long[] { 1 }));

        Assert.Equal(new long[] { 1 }, await _grants.GetCustomerIdsAsync(10));
        Assert.Equal(new long[] { 1 }, await _grants.GetCustomerIdsAsync(11));
        Assert.Equal(new long[] { 10, 11 }, await _grants.GetAdminIdsByCustomerIdAsync(1));
        Assert.Equal(2, await _db.UserCustomers.AsNoTracking().IgnoreQueryFilters()
            .CountAsync(uc => uc.CustomerId == 1));
    }

    [Fact(DisplayName = "GRANT-11 批量查询多个 admin 的授权集合 → 一次取回，无 N+1")]
    public async Task GetCustomerIds_MultipleUsers_ReturnsGroupedResult()
    {
        await _grants.SyncAsync(10, new long[] { 1, 2 }, 1);
        await _grants.SyncAsync(11, new long[] { 3 }, 1);

        var map = await _grants.GetCustomerIdsAsync(new long[] { 10, 11, 12 });

        Assert.Equal(new long[] { 1, 2 }, map[10]);
        Assert.Equal(new long[] { 3 }, map[11]);
        Assert.False(map.ContainsKey(12));
    }

    [Fact(DisplayName = "GRANT-12 GET 回显：超管可读，非超管 Code 403")]
    public async Task Get_Grants_OnlySuperAdmin()
    {
        await _grants.SyncAsync(10, new long[] { 1, 3 }, 1);

        var ok = Unwrap(await _sut.Get(10));
        Assert.Equal(200, ok.Code);
        Assert.Equal(new long[] { 1, 3 }, ok.Data!.CustomerIds);

        _sut.ControllerContext.HttpContext.User = Principal(10, Roles.ADMIN);
        var forbidden = Unwrap(await _sut.Get(10));
        Assert.Equal(403, forbidden.Code);
    }

    // ───────────────────────────── 夹具 ─────────────────────────────

    private void SeedUser(long id, string email, string role) => _db.Users.Add(new User
    {
        Id = id,
        Username = $"u{id}",
        Email = email,
        Password = "!not-a-real-hash!",
        FullName = $"用户{id}",
        Role = role,
        Status = "active",
        IsActive = true,
        IsSuperAdmin = false,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    });

    private static ClaimsPrincipal Principal(long userId, string role)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, role)
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private static ApiResponse<ManagedCustomerGrantDto> Unwrap(ActionResult<ApiResponse<ManagedCustomerGrantDto>> result)
    {
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsType<ApiResponse<ManagedCustomerGrantDto>>(ok.Value);
    }
}
