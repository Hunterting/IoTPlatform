// B4 QA 第 1 轮 4 个缺陷的定向回归测试（P0 命令记录越集、共享字典/角色详情、角色列表、
// CustomerId=NULL + 授权 AppCode 的 Multi 列表）。
//
// 核心断言：越集 C4（CustomerId=8 / AppCode=customer_004）在所有路径均不可见，
// 同时授权内 NULL CustomerId 行、共享 system 行、自有授权客户的命令记录仍可见。

using System;
using System.Linq;
using System.Threading.Tasks;
using IoTPlatform.Configuration;
using IoTPlatform.Data;
using IoTPlatform.Data.Repositories.Implementations;
using IoTPlatform.Helpers;
using IoTPlatform.Models;
using IoTPlatform.Services;
using IoTPlatform.Services.Guards;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace IoTPlatform.AnSheng.Tests;

public sealed class PatternM5_M6_QAFixRegressionTests : IDisposable
{
    private const long AdminId = 10;
    private const string CommandIdOwn = "cmd-c1-0001";
    private readonly AppDbContext _db;
    private readonly ICustomerScopeService _scope;
    private readonly DetailScopeGuard _guard;

    public PatternM5_M6_QAFixRegressionTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"b4-qafix-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

        foreach (var id in new long[] { 1, 2, 3, 4, 8 })
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

        // ① 真实命令记录只在 anshengcommandrecords。
        _db.AnShengCommandRecords.Add(new AnShengCommandRecord
        {
            CommandId = CommandIdOwn,
            AppCode = "customer_001",
            Imei = "IMEI-OWN",
            Method = "getCalParams",
            RequestJson = "{}",
            ResponseJson = "{}",
            IssuedAt = DateTime.UtcNow
        });

        // ② 共享字典类型 / 平台级角色（AppCode="system", IsSystem=false）。
        _db.DictionaryTypeConfigs.Add(new DictionaryTypeConfig
        {
            Id = 321,
            Code = "SYSTEM_TYPE",
            Name = "共享类型",
            AppCode = "system",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        _db.Roles.Add(new Role { Id = 3, Code = "R_SYSTEM_3", Name = "平台角色3", AppCode = "system", IsSystem = false });
        _db.Roles.Add(new Role { Id = 5, Code = "R_SYSTEM_5", Name = "平台角色5", AppCode = "system", IsSystem = false });
        _db.Roles.Add(new Role { Id = 8, Code = "R_C4", Name = "越集角色", AppCode = "customer_004", IsSystem = false });

        // ④ CustomerId=NULL 但有授权 AppCode 的行 + 越集 C4 行。
        _db.Users.Add(new User { Id = 104, Username = "nullc1", Password = "x", Name = "空客户1", FullName = "空客户1", Email = "n1@x.local", Role = Roles.OPERATOR, CustomerId = null, AppCode = "customer_001" });
        _db.Users.Add(new User { Id = 105, Username = "nullc4", Password = "x", Name = "空客户4", FullName = "空客户4", Email = "n4@x.local", Role = Roles.OPERATOR, CustomerId = null, AppCode = "customer_004" });
        _db.WorkOrders.Add(new WorkOrder { Id = 2, OrderNo = "WO-NULL-C2", Title = "空客户工单2", Status = "pending", Type = "maintenance", Priority = "medium", CustomerId = null, AppCode = "customer_002" });
        _db.WorkOrders.Add(new WorkOrder { Id = 4, OrderNo = "WO-NULL-C4", Title = "空客户工单4", Status = "pending", Type = "maintenance", Priority = "medium", CustomerId = null, AppCode = "customer_004" });
        _db.SaveChanges();

        var grants = new UserCustomerRepository(_db);
        grants.SyncAsync(AdminId, new long[] { 1, 2, 3 }, 1).GetAwaiter().GetResult();
        _db.ChangeTracker.Clear();

        var customers = new CustomerRepository(_db);
        _scope = new CustomerScopeService(grants, customers, new DeviceRepository(_db), new ProjectRepository(_db));
        _guard = new DetailScopeGuard(_db, new CustomerScopeGuard(_scope), _scope);
    }

    public void Dispose() => _db.Dispose();

    // ① P0：AnShengCommandRecord 点查守卫（实体有 AppCode 无 CustomerId）。
    [Fact(DisplayName = "B4-QA-01 命令记录越集详情 403，自有授权 200，下级零回归")]
    public async Task CommandRecordDetail_OwnAllowed_CrossTenantDenied()
    {
        Assert.True(await _guard.CanReadByKeyAsync<AnShengCommandRecord>(
            ScopePrincipals.Multi(AdminId, 1, 2, 3), CommandIdOwn, "CommandId"));
        // DB 为准：另建一个只授权 C3 的 admin（claim 快照不能改变授权集合）。
        var grantsC3 = new UserCustomerRepository(_db);
        grantsC3.SyncAsync(AdminId, new long[] { 3 }, 1).GetAwaiter().GetResult();
        var scopeC3 = new CustomerScopeService(grantsC3, new CustomerRepository(_db));
        var guardC3 = new DetailScopeGuard(_db, new CustomerScopeGuard(scopeC3), scopeC3);
        Assert.False(await guardC3.CanReadByKeyAsync<AnShengCommandRecord>(
            ScopePrincipals.Multi(AdminId, 3), CommandIdOwn, "CommandId"));
        Assert.True(await _guard.CanReadByKeyAsync<AnShengCommandRecord>(
            ScopePrincipals.Single(1, "customer_001"), CommandIdOwn, "CommandId"));
        Assert.False(await _guard.CanReadByKeyAsync<AnShengCommandRecord>(
            ScopePrincipals.Single(2, "customer_002"), CommandIdOwn, "CommandId"));
        Assert.True(await _guard.CanReadByKeyAsync<AnShengCommandRecord>(
            ScopePrincipals.All(), CommandIdOwn, "CommandId"));
    }

    // ② 共享行读判据统一；写侧判据不放宽。
    [Fact(DisplayName = "B4-QA-02 system 共享行详情放行，写侧判据不泛化")]
    public async Task SharedSystemRows_Readable_ButWriteJudgementUnchanged()
    {
        Assert.True(_scope.IsSharedReadRow("system", false));
        Assert.False(_scope.IsSharedSystemRow("system", false)); // 写侧严格判据保持

        var admin = ScopePrincipals.Multi(AdminId, 1, 2, 3);
        Assert.True(await _guard.CanReadAsync<DictionaryTypeConfig>(admin, 321, allowShared: true));
        Assert.True(await _guard.CanReadAsync<Role>(admin, 3, allowShared: true));
        Assert.True(await _guard.CanReadAsync<Role>(admin, 5, allowShared: true));
        Assert.False(await _guard.CanReadAsync<Role>(admin, 8, allowShared: true)); // C4 私有角色仍 403
    }

    // ③ 角色列表保留 system 共享行，但 C4 私有角色不可见。
    [Fact(DisplayName = "B4-QA-03 manager 角色列表含 system 角色且不含 C4")]
    public async Task RoleList_KeepsSystemRows_AndHidesCrossTenant()
    {
        var service = new RoleService(
            new RoleRepository(_db), new UserRepository(_db), new CustomerRepository(_db), new UnitOfWork(_db));
        var scope = await _scope.ResolveAsync(ScopePrincipals.Multi(AdminId, 1, 2, 3));

        var page = await service.GetRolesAsync(1, 100, null, null, Roles.ADMIN, scope.CustomerIds, scope.AppCodes);

        Assert.Contains(page.Items, r => r.Id == 3);
        Assert.Contains(page.Items, r => r.Id == 5);
        Assert.DoesNotContain(page.Items, r => r.Id == 8);

        var all = await service.GetRolesAsync(1, 100, null, null, Roles.SUPER_ADMIN);
        Assert.Equal(3, all.TotalCount); // 超管全量不回退
    }

    // ④ Multi 列表 OR 条件覆盖 CustomerId=NULL + 授权 AppCode，且不泄漏 C4。
    [Fact(DisplayName = "B4-QA-04 NULL CustomerId 授权行可见，越集 C4 不可见")]
    public async Task MultiList_OrAppCode_IncludesNullCustomerRows_ButNotCrossTenant()
    {
        var scope = await _scope.ResolveAsync(ScopePrincipals.Multi(AdminId, 1, 2, 3));
        var customers = new CustomerRepository(_db);
        var userService = new UserService(
            new UserRepository(_db), customers, new LogRepository(_db),
            new WorkOrderRepository(_db), new UnitOfWork(_db));
        var guard = new CustomerScopeGuard(_scope);
        var workOrderService = new WorkOrderService(
            new WorkOrderRepository(_db), new DeviceRepository(_db), new AreaRepository(_db),
            new UnitOfWork(_db), guard);

        var users = await userService.GetUsersAsync(
            1, 100, null, null, Roles.ADMIN, scope.CustomerIds, scope.AppCodes);
        Assert.Contains(users.Items, u => u.Id == 104);
        Assert.DoesNotContain(users.Items, u => u.Id == 105);

        var orders = await workOrderService.GetWorkOrdersAsync(
            1, 100, null, null, null, null, null, scope.CustomerIds, scope.AppCodes);
        Assert.Contains(orders.Items, w => w.Id == 2);
        Assert.DoesNotContain(orders.Items, w => w.Id == 4);

        // 与详情判据同源：NULL CustomerId 的授权行详情也可见，C4 不可见。
        Assert.True(await guard.CanAccessOwnedEntityAsync(
            ScopePrincipals.Multi(AdminId, 1, 2, 3), null, "customer_001"));
        Assert.False(await guard.CanAccessOwnedEntityAsync(
            ScopePrincipals.Multi(AdminId, 1, 2, 3), null, "customer_004"));
    }
}
