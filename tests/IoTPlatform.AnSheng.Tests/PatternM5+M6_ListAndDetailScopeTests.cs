// B4 / M5 + M6：列表范围下推、详情归属校验与 PII 裁剪回归测试。
//
// 【核心命题】admin 的 AppCode claim 为空，旧链路把 null 传给 Service/Repository 即退化为全量查询；
// 详情端点则只凭 role/appCode 软判据，admin 可直读任意客户记录。修复后：
//   · 列表：Multi 分支把 DB 授权客户集合（及反查 AppCodes）真正下推到 SQL；
//   · 详情：先 IgnoreQueryFilters 点查归属，越权 HTTP 200 + ApiResponse.Code=403；
//   · PII：UserDto 绝不暴露 Password / PasswordHash。
// 【零回归】超管 All 仍全量，下级 Single 仍按 claim 单值取数。

using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using IoTPlatform.Configuration;
using IoTPlatform.Controllers;
using IoTPlatform.Data;
using IoTPlatform.Data.Repositories.Implementations;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Helpers;
using IoTPlatform.Models;
using IoTPlatform.Services;
using IoTPlatform.Services.Guards;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IoTPlatform.AnSheng.Tests;

/// <summary>B4 查询端点范围下推 / 详情归属 / PII 测试（设计 §2.6、§2.7、§5.2、§6.6）。</summary>
public sealed class PatternM5_M6_ListAndDetailScopeTests : IDisposable
{
    private const long AdminId = 10;
    private readonly AppDbContext _db;
    private readonly CustomerScopeService _scope;
    private readonly DetailScopeGuard _detailGuard;
    private readonly UserService _userService;
    private readonly LogService _logService;

    public PatternM5_M6_ListAndDetailScopeTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"b4-m5m6-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

        foreach (var id in new long[] { 1, 2, 3 })
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

        foreach (var id in new long[] { 101, 102, 103 })
        {
            _db.Users.Add(new User
            {
                Id = id,
                Username = $"user{id}",
                Password = "$2a$11$hashed-value-must-never-be-returned",
                Name = $"用户{id}",
                FullName = $"用户{id}",
                Email = $"user{id}@example.local",
                Role = Roles.OPERATOR,
                CustomerId = id - 100,
                AppCode = $"customer_{id - 100:000}"
            });
        }

        _db.OperationLogs.Add(new OperationLog
        {
            Id = 201,
            UserId = 101,
            UserName = "user101",
            Operation = "query",
            Action = "query",
            Time = DateTime.UtcNow,
            Status = "success",
            AppCode = "customer_001"
        });
        _db.OperationLogs.Add(new OperationLog
        {
            Id = 202,
            UserId = 103,
            UserName = "user103",
            Operation = "query",
            Action = "query",
            Time = DateTime.UtcNow,
            Status = "success",
            AppCode = "customer_003"
        });
        _db.SaveChanges();

        var grants = new UserCustomerRepository(_db);
        grants.SyncAsync(AdminId, new long[] { 1, 2 }, 1).GetAwaiter().GetResult();
        _db.ChangeTracker.Clear();

        var customers = new CustomerRepository(_db);
        _scope = new CustomerScopeService(grants, customers, new DeviceRepository(_db), new ProjectRepository(_db));
        _detailGuard = new DetailScopeGuard(_db, new CustomerScopeGuard(_scope), _scope);
        _userService = new UserService(
            new UserRepository(_db), customers, new LogRepository(_db),
            new WorkOrderRepository(_db), new UnitOfWork(_db));
        _logService = new LogService(new LogRepository(_db));
    }

    public void Dispose() => _db.Dispose();

    private UsersController UsersFor(System.Security.Claims.ClaimsPrincipal principal)
    {
        var provider = new ServiceCollection()
            .AddSingleton<IDetailScopeGuard>(_detailGuard)
            .BuildServiceProvider();
        return new UsersController(_userService, _scope, _detailGuard)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = principal, RequestServices = provider }
            }
        };
    }

    private LogsController LogsFor(System.Security.Claims.ClaimsPrincipal principal)
    {
        var provider = new ServiceCollection()
            .AddSingleton<IDetailScopeGuard>(_detailGuard)
            .BuildServiceProvider();
        return new LogsController(_logService, _scope, _detailGuard)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = principal, RequestServices = provider }
            }
        };
    }

    /// <summary>兼容控制器两种返回形态：Ok(ApiResponse) 与直接返回 ApiResponse。</summary>
    private static ApiResponse<T> EnvelopeOf<T>(ActionResult<ApiResponse<T>> action)
    {
        if (action.Result is OkObjectResult ok)
        {
            return Assert.IsType<ApiResponse<T>>(ok.Value);
        }
        return Assert.IsType<ApiResponse<T>>(action.Value);
    }

    private static int CodeOf<T>(ActionResult<ApiResponse<T>> action) => EnvelopeOf(action).Code;

    private static T DataOf<T>(ActionResult<ApiResponse<T>> action) => EnvelopeOf(action).Data!;

    // ── M5 列表范围下推 ─────────────────────────────────────────────────────

    [Fact(DisplayName = "B4-M5-01 admin 用户列表按 DB 授权集合下推，不再全量")]
    public async Task MultiScope_UserList_IsPushedDown()
    {
        var scope = await _scope.ResolveAsync(ScopePrincipals.Multi(AdminId, 1, 2));
        Assert.Equal(CustomerScopeMode.Multi, scope.Mode);

        var page = await _userService.GetUsersAsync(
            1, 50, null, null, Roles.ADMIN, scope.CustomerIds, scope.AppCodes);

        Assert.Equal(2, page.TotalCount);
        Assert.Equal(new long[] { 101, 102 }, page.Items.Select(u => u.Id).OrderBy(id => id));
    }

    [Fact(DisplayName = "B4-M5-02 超管列表零过滤，下级 claim 单值路径零回归")]
    public async Task AllAndSingle_UserList_KeepLegacyBehavior()
    {
        var all = await _userService.GetUsersAsync(1, 50, null, null, Roles.SUPER_ADMIN);
        Assert.Equal(3, all.TotalCount);

        var single = await _userService.GetUsersAsync(1, 50, null, "customer_002", Roles.OPERATOR);
        Assert.Equal(1, single.TotalCount);
        Assert.Equal(102, single.Items.Single().Id);
    }

    [Fact(DisplayName = "B4-M5-03 日志列表按授权 AppCodes 下推")]
    public async Task MultiScope_LogList_IsPushedDown()
    {
        var scope = await _scope.ResolveAsync(ScopePrincipals.Multi(AdminId, 1, 2));

        var page = await _logService.GetOperationLogsAsync(
            1, 50, null, null, null, null, null, null, Roles.ADMIN,
            scope.CustomerIds, scope.AppCodes);

        Assert.Equal(1, page.TotalCount);
        Assert.Equal(201, page.Items.Single().Id);
    }

    // ── M6 详情归属校验 ────────────────────────────────────────────────────

    [Fact(DisplayName = "B4-M6-01 admin 越权读用户详情返回业务 403，授权内与超管正常")]
    public async Task MultiScope_UserDetail_ForbiddenOutsideGrant()
    {
        var admin = UsersFor(ScopePrincipals.Multi(AdminId, 1, 2));

        Assert.Equal(403, CodeOf(await admin.GetUser(103))); // 客户3 不在授权集合
        Assert.Equal(200, CodeOf(await admin.GetUser(101))); // 客户1 在授权集合

        var superAdmin = UsersFor(ScopePrincipals.All());
        Assert.Equal(200, CodeOf(await superAdmin.GetUser(103)));

        var single = UsersFor(ScopePrincipals.Single(1, "customer_001"));
        Assert.Equal(403, CodeOf(await single.GetUser(102)));
    }

    [Fact(DisplayName = "B4-M6-02 admin 越权读操作日志详情返回业务 403")]
    public async Task MultiScope_LogDetail_ForbiddenOutsideGrant()
    {
        var admin = LogsFor(ScopePrincipals.Multi(AdminId, 1, 2));

        Assert.Equal(403, CodeOf(await admin.GetOperationLog(202)));
        Assert.Equal(200, CodeOf(await admin.GetOperationLog(201)));
    }

    // ── PII / DTO 裁剪 ─────────────────────────────────────────────────────

    [Fact(DisplayName = "B4-PII-01 用户 DTO 不含 Password/PasswordHash，出网 JSON 亦无哈希")]
    public async Task UserDto_DoesNotLeakPasswordFields()
    {
        var propertyNames = typeof(UserDto).GetProperties().Select(p => p.Name).ToArray();
        Assert.DoesNotContain("Password", propertyNames);
        Assert.DoesNotContain("PasswordHash", propertyNames);

        var admin = UsersFor(ScopePrincipals.Multi(AdminId, 1, 2));
        var dto = DataOf(await admin.GetUser(101));
        var json = JsonSerializer.Serialize(dto);
        Assert.DoesNotContain("hashed-value", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PasswordHash", json, StringComparison.OrdinalIgnoreCase);
    }
}
