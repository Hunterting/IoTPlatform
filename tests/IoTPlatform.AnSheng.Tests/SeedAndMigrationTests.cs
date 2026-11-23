using IoTPlatform.Configuration;
using IoTPlatform.Data;
using IoTPlatform.Data.SeedData;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IoTPlatform.AnSheng.Tests;

/// <summary>
/// 多客户授权模型 T05.1 —— 种子与迁移回填一致性（设计 §5.6 / §6 T01.2、T01.3 / §8 清单 3e）。
///
/// <para>【运行时验证】真实执行 <see cref="SeedUsers.InitializeAsync"/> 于 InMemory 库，断言
/// manager（Id=2）单值列已清空、user_customers 含 (2,1)/(2,2)/(2,3)、重复执行不产生重复行。</para>
///
/// <para>【结构验证】从 EF 模型元数据断言 uk_user_customer 组合唯一、customer_id 单列索引非唯一
/// （D6：同一客户可授予多个 admin）。</para>
///
/// <para>【迁移 SQL 契约】迁移在单元测试里无法真正跑 MySQL，改为读取迁移源码断言回填/清空/RESTRICT
/// 四段关键 SQL 均存在；真机执行由 QA 清单承担。</para>
/// </summary>
public sealed class SeedAndMigrationTests : IDisposable
{
    private readonly AppDbContext _db;

    public SeedAndMigrationTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"seed-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

        // SeedUsers 只负责用户与授权，客户由前置 seed 建好（customer_001/002/003 的 Id 为 1/2/3）。
        _db.Customers.AddRange(
            new Customer
            {
                Id = 1,
                Name = "海底捞智慧工厂有限公司",
                Code = "customer_001",
                AppCode = "customer_001",
                Status = "active",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new Customer
            {
                Id = 2,
                Name = "绿色能源有限公司",
                Code = "customer_002",
                AppCode = "customer_002",
                Status = "active",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new Customer
            {
                Id = 3,
                Name = "智慧建筑有限公司",
                Code = "customer_003",
                AppCode = "customer_003",
                Status = "active",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    [Fact(DisplayName = "SEED-01 种子执行后 manager(2) 单值清空且 user_customers 含 (2,1)/(2,2)/(2,3)，source=seed")]
    public async Task SeedUsers_ManagerSingleValuesClearedAndGrantSeeded()
    {
        var seeder = new SeedUsers(NullLogger<SeedUsers>.Instance);

        await seeder.InitializeAsync(_db);

        var manager = await _db.Users.AsNoTracking().IgnoreQueryFilters()
            .SingleAsync(u => u.Id == 2);
        Assert.Equal(Roles.ADMIN, manager.Role);
        Assert.Null(manager.CustomerId);
        Assert.Null(manager.AppCode);

        var grants = await _db.UserCustomers.AsNoTracking().IgnoreQueryFilters()
            .OrderBy(uc => uc.CustomerId)
            .ToListAsync();
        Assert.Equal(3, grants.Count);
        Assert.Equal(new long[] { 1, 2, 3 }, grants.Select(g => g.CustomerId).ToArray());
        Assert.All(grants, grant =>
        {
            Assert.Equal(2L, grant.UserId);
            Assert.Equal(CustomerScopeConstants.SourceSeed, grant.Source);
            Assert.Null(grant.RevokedAt);
        });

        // 下级与超管单值列逐字不动（零回归）；Id=3/4 的 Role 字符串显式落库，不再回落到默认角色。
        var superAdmin = await _db.Users.AsNoTracking().IgnoreQueryFilters().SingleAsync(u => u.Id == 1);
        Assert.True(superAdmin.IsSuperAdmin);
        Assert.Null(superAdmin.CustomerId);
        var operatorUser = await _db.Users.AsNoTracking().IgnoreQueryFilters().SingleAsync(u => u.Id == 3);
        Assert.Equal(Roles.OPERATOR, operatorUser.Role);
        var ordinaryUser = await _db.Users.AsNoTracking().IgnoreQueryFilters().SingleAsync(u => u.Id == 4);
        Assert.Equal(Roles.USER, ordinaryUser.Role);
        Assert.Equal(1L, ordinaryUser.CustomerId);
        Assert.Equal("customer_001", ordinaryUser.AppCode);
    }

    [Fact(DisplayName = "SEED-02 种子重复执行幂等：不新增用户、不产生重复授权行")]
    public async Task SeedUsers_SecondRun_IsIdempotent()
    {
        var seeder = new SeedUsers(NullLogger<SeedUsers>.Instance);

        await seeder.InitializeAsync(_db);
        await seeder.InitializeAsync(_db);

        Assert.Equal(4, await _db.Users.AsNoTracking().IgnoreQueryFilters().CountAsync());
        Assert.Equal(3, await _db.UserCustomers.AsNoTracking().IgnoreQueryFilters().CountAsync());
        foreach (var customerId in new long[] { 1, 2, 3 })
        {
            Assert.Equal(1, await _db.UserCustomers.AsNoTracking().IgnoreQueryFilters()
                .CountAsync(uc => uc.UserId == 2 && uc.CustomerId == customerId));
        }
    }

    [Fact(DisplayName = "SEED-03 模型元数据：组合唯一索引存在，customer_id 单列索引非唯一（D6 多对多）")]
    public void UserCustomer_Indexes_MatchManyToManyDesign()
    {
        var entity = _db.Model.FindEntityType(typeof(UserCustomer));
        Assert.NotNull(entity);

        var indexes = entity!.GetIndexes().ToList();

        var composite = indexes.SingleOrDefault(i => i.Properties.Select(p => p.Name)
            .SequenceEqual(new[] { "UserId", "CustomerId" }));
        Assert.NotNull(composite);
        Assert.True(composite!.IsUnique, "同一 admin 不得重复持有同一客户");

        var byCustomer = indexes.SingleOrDefault(i =>
            i.Properties.Count == 1 && i.Properties.Single().Name == "CustomerId");
        Assert.NotNull(byCustomer);
        Assert.False(byCustomer!.IsUnique, "同一客户必须允许授予多个 admin（D6）");
    }

    [Fact(DisplayName = "SEED-04 迁移 SQL 契约：建表 / 回填 / 清空单值 / RESTRICT 四段齐备")]
    public void Migration_Up_ContainsBackfillAndSingleValueWipe()
    {
        var migrationSource = File.ReadAllText(FindMigrationPath());

        Assert.Contains("CreateTable(", migrationSource, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("user_customers", migrationSource);
        Assert.Contains("'migration'", migrationSource, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Role = 'admin'", migrationSource, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NOT EXISTS", migrationSource, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CustomerId = NULL, AppCode = NULL", migrationSource, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("uk_user_customer", migrationSource, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ReferentialAction.Restrict", migrationSource, StringComparison.OrdinalIgnoreCase);

        // 种子侧：manager 的授权客户集合固定为 (2 → 1/2/3)，与回填函数的多客户快照一致。
        var seedSource = File.ReadAllText(Path.Combine(FindRepoRoot(), "Data", "SeedData", "SeedUsers.cs"));
        Assert.Contains("[2] = new long[] { 1, 2, 3 }", seedSource);
        Assert.Contains("SourceSeed", seedSource);
    }

    // ───────────────────────────── 夹具 ─────────────────────────────

    private static string FindMigrationPath()
    {
        var path = Path.Combine(FindRepoRoot(), "Migrations", "20261123025525_AddUserCustomers.cs");
        Assert.True(File.Exists(path), $"找不到迁移文件：{path}");
        return path;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "IoTPlatform.sln")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            $"从 {AppContext.BaseDirectory} 向上未找到 IoTPlatform.sln，无法定位迁移源码。");
    }
}
