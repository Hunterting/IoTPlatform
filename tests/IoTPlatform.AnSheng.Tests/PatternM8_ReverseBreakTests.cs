// B5 / M8：反向功能断点（唯一行为放宽批次）。
//
// 【核心命题】manager(admin) 自身 AppCode=null，旧判据 `role != SUPER_ADMIN && entity.AppCode != appCode`
// 恒真 → 对授权客户 C1/C2/C3 的区域/用户/角色改删一律 403。现在按 scope.Mode 三分支放行，
// 集合外客户仍 403，下级 Single 零回归，超管 All 全量不回退。
// 【写侧冻结】三个 Update DTO 均无 CustomerId/AppCode，断言归属列不被改写。

using System;
using System.Linq;
using System.Threading.Tasks;
using IoTPlatform.Configuration;
using IoTPlatform.Controllers;
using IoTPlatform.Data;
using IoTPlatform.Data.Repositories.Implementations;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.Helpers;
using IoTPlatform.Models;
using IoTPlatform.Services;
using IoTPlatform.Services.Guards;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IoTPlatform.AnSheng.Tests;

/// <summary>B5 批次 M8 反向断点模式测试（设计 §2.9 / §6.7）。</summary>
public sealed class PatternM8_ReverseBreakTests : IDisposable
{
    private const long AdminId = 10;
    private const long CustomerOne = 1;
    private const long CustomerTwo = 2;
    private const long CustomerThree = 3;
    private const long CustomerFour = 4; // 集合外客户

    private const string AppOne = "customer_001";
    private const string AppTwo = "customer_002";
    private const string AppThree = "customer_003";
    private const string AppFour = "customer_004";

    private readonly AppDbContext _db;
    private readonly CustomerScopeService _scope;
    private readonly AreasController _areas;
    private readonly UsersController _users;
    private readonly RolesController _roles;
    private readonly AreaService _areaService;
    private readonly UserService _userService;
    private readonly RoleService _roleService;

    public PatternM8_ReverseBreakTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"b5-m8-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

        foreach (var (id, appCode) in new[]
                 {
                     (CustomerOne, AppOne), (CustomerTwo, AppTwo),
                     (CustomerThree, AppThree), (CustomerFour, AppFour)
                 })
        {
            _db.Customers.Add(new Customer
            {
                Id = id,
                Name = $"客户{id}",
                Code = $"C{id:000}",
                AppCode = appCode,
                Status = "active",
                MaxDeviceCount = 100,
                MaxUserCount = 100,
                MaxAreaCount = 100,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        foreach (var (id, customer, appCode) in new[]
                 {
                     (101L, CustomerOne, AppOne), (102L, CustomerTwo, AppTwo),
                     (103L, CustomerThree, AppThree), (104L, CustomerFour, AppFour)
                 })
        {
            _db.Areas.Add(new Area
            {
                Id = id,
                Name = $"区域{id}",
                Type = "level1",
                CustomerId = customer,
                AppCode = appCode,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            _db.Users.Add(new User
            {
                Id = 200 + id,
                Username = $"user{id}",
                Password = "hash",
                Name = $"用户{id}",
                FullName = $"用户{id}",
                Email = $"user{id}@example.com",
                Role = "operator",
                CustomerId = customer,
                AppCode = appCode,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            _db.Roles.Add(new Role
            {
                Id = 300 + id,
                Code = $"role_{id}",
                Name = $"角色{id}",
                DataScope = "CUSTOM",
                AppCode = appCode,
                Permissions = "[]",
                IsSystem = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        // 脏数据：有 CustomerId 在授权集合内，但 AppCode 为空 —— 非超管必须 fail-closed。
        _db.Areas.Add(new Area
        {
            Id = 105, Name = "无归属区域", Type = "level1",
            CustomerId = CustomerOne, AppCode = null,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        _db.Roles.Add(new Role
        {
            Id = 505, Code = "role_dirty", Name = "无归属角色", DataScope = "CUSTOM",
            AppCode = null, Permissions = "[]", IsSystem = false,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });

        // 权限白名单的权威来源：DB 中 admin 角色的有效权限。
        _db.Roles.Add(new Role
        {
            Id = 900,
            Code = Roles.ADMIN,
            Name = "租户管理员",
            DataScope = "CUSTOM",
            AppCode = null,
            Permissions = System.Text.Json.JsonSerializer.Serialize(new[]
            {
                Permissions.VIEW_USERS, Permissions.UPDATE_USERS,
                Permissions.VIEW_ROLES, Permissions.UPDATE_ROLES
            }),
            IsSystem = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        _db.SaveChanges();

        var grants = new UserCustomerRepository(_db);
        grants.SyncAsync(AdminId, new[] { CustomerOne, CustomerTwo, CustomerThree }, 1)
            .GetAwaiter().GetResult();
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        var customers = new CustomerRepository(_db);
        var devices = new DeviceRepository(_db);
        var projects = new ProjectRepository(_db);
        var logs = new LogRepository(_db);
        var workOrders = new WorkOrderRepository(_db);
        var unitOfWork = new UnitOfWork(_db);
        var users = new UserRepository(_db);
        var roles = new RoleRepository(_db);

        _scope = new CustomerScopeService(grants, customers, devices, projects);

        _areaService = new AreaService(new AreaRepository(_db), customers, devices, unitOfWork);
        _userService = new UserService(users, customers, logs, workOrders, unitOfWork);
        _roleService = new RoleService(roles, users, customers, unitOfWork);

        _areas = new AreasController(_areaService, _scope);
        _users = new UsersController(_userService, _scope);
        _roles = new RolesController(
            _roleService, _scope, new PermissionService(roles, NullLogger<PermissionService>.Instance));
    }

    /// <inheritdoc />
    public void Dispose() => _db.Dispose();

    private static T Attach<T>(T controller, System.Security.Claims.ClaimsPrincipal principal)
        where T : ControllerBase
    {
        controller.ControllerContext = ScopePrincipals.ForgedHeaderContext(principal);
        return controller;
    }

    private static int CodeOf<T>(ActionResult<ApiResponse<T>> result)
    {
        if (result.Result is OkObjectResult ok)
        {
            if (ok.Value is ApiResponse<T> wrapped) return wrapped.Code;
            if (ok.Value is ApiResponse<object> plain) return plain.Code;
        }
        if (result.Value is ApiResponse<T> direct) return direct.Code;
        return result.Value is ApiResponse<object> directPlain ? directPlain.Code : -1;
    }

    private static int CodeOf(ActionResult<ApiResponse> result)
    {
        if (result.Result is OkObjectResult ok)
        {
            if (ok.Value is ApiResponse wrapped) return wrapped.Code;
            if (ok.Value is ApiResponse<object> plain) return plain.Code;
        }
        if (result.Value is ApiResponse direct) return direct.Code;
        return result.Value is ApiResponse<object> directPlain ? directPlain.Code : -1;
    }

    private void ClearTracker() => _db.ChangeTracker.Clear();

    private static UpdateAreaRequest AreaRequest(string name) => new() { Name = name, Description = "B5" };

    private static UpdateUserRequest UserRequest(string name) => new()
    {
        Name = name, Email = $"{name}@example.com", IsActive = true
    };

    private static UpdateRoleRequest RoleRequest(string name) => new()
    {
        Name = name, DataScope = "CUSTOM", Permissions = null
    };

    // ── 区域：反向断点修复 ─────────────────────────────────────────────────

    [Theory(DisplayName = "M8-01 admin 改授权 C1/C2/C3 区域 → 200 且归属不变")]
    [InlineData(101L, AppOne, CustomerOne)]
    [InlineData(102L, AppTwo, CustomerTwo)]
    [InlineData(103L, AppThree, CustomerThree)]
    public async Task Admin_UpdateArea_InGrant_Succeeds(long id, string appCode, long customerId)
    {
        Attach(_areas, ScopePrincipals.Multi(AdminId, CustomerOne, CustomerTwo, CustomerThree));
        var response = await _areas.UpdateArea(id, AreaRequest("授权改名"));

        Assert.Equal(200, CodeOf(response));
        ClearTracker();
        var stored = _db.Areas.IgnoreQueryFilters().Single(a => a.Id == id);
        Assert.Equal("授权改名", stored.Name);
        Assert.Equal(appCode, stored.AppCode);   // M3c：归属不可被请求改写
        Assert.Equal(customerId, stored.CustomerId);
    }

    [Fact(DisplayName = "M8-02 admin 改集合外 C4 区域 → 403 且字段不变")]
    public async Task Admin_UpdateArea_OutOfGrant_Forbidden()
    {
        Attach(_areas, ScopePrincipals.Multi(AdminId, CustomerOne, CustomerTwo, CustomerThree));
        var response = await _areas.UpdateArea(104, AreaRequest("越权改名"));

        Assert.Equal(403, CodeOf(response));
        ClearTracker();
        Assert.Equal("区域104", _db.Areas.IgnoreQueryFilters().Single(a => a.Id == 104).Name);
    }

    [Fact(DisplayName = "M8-03 admin 删授权内区域 → 200；删集合外 → 403 且实体仍在")]
    public async Task Admin_DeleteArea_InGrantSucceeds_OutOfGrantForbidden()
    {
        Attach(_areas, ScopePrincipals.Multi(AdminId, CustomerOne, CustomerTwo, CustomerThree));
        var allowed = await _areas.DeleteArea(102);
        Assert.Equal(200, CodeOf(allowed));

        var forbidden = await _areas.DeleteArea(104);
        Assert.Equal(403, CodeOf(forbidden));
        ClearTracker();
        Assert.Null(_db.Areas.IgnoreQueryFilters().SingleOrDefault(a => a.Id == 102));
        Assert.NotNull(_db.Areas.IgnoreQueryFilters().SingleOrDefault(a => a.Id == 104));
    }

    // ── 用户：反向断点修复 ─────────────────────────────────────────────────

    [Fact(DisplayName = "M8-04 admin 改 C2 用户 → 200；改 C4 用户 → 403 且字段不变")]
    public async Task Admin_UpdateUser_InGrantSucceeds_OutOfGrantForbidden()
    {
        Attach(_users, ScopePrincipals.Multi(AdminId, CustomerOne, CustomerTwo, CustomerThree));
        var allowed = await _users.UpdateUser(302, UserRequest("授权用户"));
        Assert.Equal(200, CodeOf(allowed));

        var forbidden = await _users.UpdateUser(304, UserRequest("越权用户"));
        Assert.Equal(403, CodeOf(forbidden));
        ClearTracker();
        Assert.Equal("授权用户", _db.Users.IgnoreQueryFilters().Single(u => u.Id == 302).FullName);
        var outside = _db.Users.IgnoreQueryFilters().Single(u => u.Id == 304);
        Assert.Equal("用户104", outside.FullName);
        Assert.Equal(CustomerFour, outside.CustomerId);
        Assert.Equal(AppFour, outside.AppCode);
    }

    [Fact(DisplayName = "M8-05 admin 删 C3 用户 → 200；删 C4 用户 → 403 且实体仍在")]
    public async Task Admin_DeleteUser_InGrantSucceeds_OutOfGrantForbidden()
    {
        Attach(_users, ScopePrincipals.Multi(AdminId, CustomerOne, CustomerTwo, CustomerThree));
        var allowed = await _users.DeleteUser(303);
        Assert.Equal(200, CodeOf(allowed));

        var forbidden = await _users.DeleteUser(304);
        Assert.Equal(403, CodeOf(forbidden));
        ClearTracker();
        Assert.Null(_db.Users.IgnoreQueryFilters().SingleOrDefault(u => u.Id == 303));
        Assert.NotNull(_db.Users.IgnoreQueryFilters().SingleOrDefault(u => u.Id == 304));
    }

    // ── 角色：反向断点修复（B3 QA 转交遗留） ───────────────────────────────

    [Fact(DisplayName = "M8-06 admin 改 C1 角色 → 200（RoleService appCode=null 误伤已解）")]
    public async Task Admin_UpdateRole_InGrant_Succeeds()
    {
        Attach(_roles, ScopePrincipals.Multi(AdminId, CustomerOne, CustomerTwo, CustomerThree));
        var response = await _roles.UpdateRole(401, RoleRequest("授权角色"));

        Assert.Equal(200, CodeOf(response));
        ClearTracker();
        var stored = _db.Roles.IgnoreQueryFilters().Single(r => r.Id == 401);
        Assert.Equal("授权角色", stored.Name);
        Assert.Equal(AppOne, stored.AppCode);
    }

    [Fact(DisplayName = "M8-07 admin 改 C4 角色 → 403；删 C2 角色 → 200；删 C4 角色 → 403")]
    public async Task Admin_Role_OutOfGrantForbidden_InGrantDeleteSucceeds()
    {
        Attach(_roles, ScopePrincipals.Multi(AdminId, CustomerOne, CustomerTwo, CustomerThree));
        var forbiddenUpdate = await _roles.UpdateRole(404, RoleRequest("越权角色"));
        Assert.Equal(403, CodeOf(forbiddenUpdate));

        var allowedDelete = await _roles.DeleteRole(402);
        Assert.Equal(200, CodeOf(allowedDelete));

        var forbiddenDelete = await _roles.DeleteRole(404);
        Assert.Equal(403, CodeOf(forbiddenDelete));
        ClearTracker();
        Assert.Equal("角色104", _db.Roles.IgnoreQueryFilters().Single(r => r.Id == 404).Name);
        Assert.Null(_db.Roles.IgnoreQueryFilters().SingleOrDefault(r => r.Id == 402));
    }

    // ── 零回归 / 防御 ──────────────────────────────────────────────────────

    [Fact(DisplayName = "M8-08 下级 Single 零回归：自身 200、他人 403（区域/用户/角色）")]
    public async Task Single_Update_OwnershipUnchanged()
    {
        var principal = ScopePrincipals.Single(CustomerOne, AppOne);

        Attach(_areas, principal);
        Assert.Equal(200, CodeOf(await _areas.UpdateArea(101, AreaRequest("下级改区域"))));
        Assert.Equal(403, CodeOf(await _areas.UpdateArea(104, AreaRequest("越权区域"))));

        Attach(_users, principal);
        Assert.Equal(200, CodeOf(await _users.UpdateUser(301, UserRequest("下级改用户"))));
        Assert.Equal(403, CodeOf(await _users.UpdateUser(304, UserRequest("越权用户"))));

        Attach(_roles, principal);
        Assert.Equal(200, CodeOf(await _roles.UpdateRole(401, RoleRequest("下级改角色"))));
        Assert.Equal(403, CodeOf(await _roles.UpdateRole(404, RoleRequest("越权角色"))));
    }

    [Fact(DisplayName = "M8-09 超管 All 全量不回退：集合外 C4 区域/用户/角色可改删")]
    public async Task SuperAdmin_All_ScopeUnchanged()
    {
        var principal = ScopePrincipals.All();

        Attach(_areas, principal);
        Assert.Equal(200, CodeOf(await _areas.UpdateArea(104, AreaRequest("超管改区域"))));
        Assert.Equal(200, CodeOf(await _areas.DeleteArea(104)));

        Attach(_users, principal);
        Assert.Equal(200, CodeOf(await _users.UpdateUser(304, UserRequest("超管改用户"))));
        Assert.Equal(200, CodeOf(await _users.DeleteUser(304)));

        Attach(_roles, principal);
        Assert.Equal(200, CodeOf(await _roles.UpdateRole(404, RoleRequest("超管改角色"))));
        Assert.Equal(200, CodeOf(await _roles.DeleteRole(404)));
    }

    [Fact(DisplayName = "M8-10 JWT 授权快照伪造不采信：DB 授权 1/2/3 仍只放行 C1/C2/C3")]
    public async Task ForgedManagedCustomerIdsClaim_UsesDatabaseGrant()
    {
        var principal = ScopePrincipals.Multi(AdminId, 999); // claim 快照与 DB 授权完全相反
        Attach(_areas, principal);
        Assert.Equal(200, CodeOf(await _areas.UpdateArea(103, AreaRequest("DB授权放行"))));
        Assert.Equal(403, CodeOf(await _areas.UpdateArea(104, AreaRequest("快照越权"))));
    }

    [Fact(DisplayName = "M8-11 Service fail-closed：未验证重载与旧签名 appCode=null 均 403 且零写库")]
    public async Task Service_UnverifiedOrLegacy_NullAppCode_FailClosed()
    {
        var scope = await _scope.ResolveAsync(ScopePrincipals.Multi(AdminId, CustomerOne));
        var request = AreaRequest("不得入库");

        await Assert.ThrowsAsync<ForbiddenAccessException>(
            () => _areaService.UpdateAreaAsync(101, request, null, scope, customerScopeVerified: false));
        await Assert.ThrowsAsync<ForbiddenAccessException>(
            () => _roleService.UpdateRoleAsync(401, RoleRequest("不得入库"), null, Roles.ADMIN));

        ClearTracker();
        Assert.Equal("区域101", _db.Areas.IgnoreQueryFilters().Single(a => a.Id == 101).Name);
        Assert.Equal("角色101", _db.Roles.IgnoreQueryFilters().Single(r => r.Id == 401).Name);
    }

    [Fact(DisplayName = "M8-13 无归属脏数据 fail-closed：admin 不得改 AppCode=null 的区域/角色")]
    public async Task Admin_Update_NullAppCodeEntity_FailClosed()
    {
        Attach(_areas, ScopePrincipals.Multi(AdminId, CustomerOne, CustomerTwo, CustomerThree));
        var area = await _areas.UpdateArea(105, AreaRequest("不得入库"));
        Assert.Equal(403, CodeOf(area));

        Attach(_roles, ScopePrincipals.Multi(AdminId, CustomerOne, CustomerTwo, CustomerThree));
        var role = await _roles.UpdateRole(505, RoleRequest("不得入库"));
        Assert.Equal(403, CodeOf(role));

        ClearTracker();
        Assert.Equal("无归属区域", _db.Areas.IgnoreQueryFilters().Single(a => a.Id == 105).Name);
        Assert.Equal("无归属角色", _db.Roles.IgnoreQueryFilters().Single(r => r.Id == 505).Name);
    }

    [Fact(DisplayName = "M8-12 M10 提权面不回退：admin 改角色不得授予超出自身的权限")]
    public async Task Admin_UpdateRole_PermissionWhitelistStillEnforced()
    {
        Attach(_roles, ScopePrincipals.Multi(AdminId, CustomerOne, CustomerTwo, CustomerThree));

        var illegal = await _roles.UpdateRole(401, new UpdateRoleRequest
        {
            Name = "提权角色", DataScope = "CUSTOM",
            Permissions = new System.Collections.Generic.List<string> { "__FORGED_PERMISSION__" }
        });
        Assert.Equal(403, CodeOf(illegal));

        var legal = await _roles.UpdateRole(401, new UpdateRoleRequest
        {
            Name = "合法角色", DataScope = "CUSTOM",
            Permissions = new System.Collections.Generic.List<string> { Permissions.UPDATE_ROLES }
        });
        Assert.Equal(200, CodeOf(legal));
        ClearTracker();
        Assert.Equal("合法角色", _db.Roles.IgnoreQueryFilters().Single(r => r.Id == 401).Name);
    }
}
