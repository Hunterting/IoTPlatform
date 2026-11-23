using System.Security.Claims;
using IoTPlatform.Configuration;
using IoTPlatform.Data;
using IoTPlatform.Data.Repositories.Interfaces;
using IoTPlatform.Models;
using IoTPlatform.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace IoTPlatform.AnSheng.Tests;

/// <summary>
/// 多客户授权模型 T05.1 —— <see cref="CustomerScopeService"/> 三分支单元测试（设计 §6 T02.2 / §8 清单 1、5、6）。
///
/// <para>【三分支】超管 All（空集合=不过滤）；admin Multi（DB 为准，claim 仅是快照）；
/// 下级 Single（只读 claim 单值，<b>零授权表访问</b>，下级零回归）。</para>
///
/// <para>【零 DB 访问证明】Single 分支的被测服务注入 <see cref="ThrowingUserCustomerRepository"/>：
/// 只要代码路径碰到 user_customers 就会抛异常。ResolveAsync / CanAccessCustomerAsync /
/// GetAppCodesAsync 三个入口全部覆盖。</para>
///
/// <para>【claim 快照不参与授权】Multi 分支故意让 claim 中的 ManagedCustomerIds 与 DB 事实相反，
/// 断言结果只随 DB 变化（"claim 是快照、DB 是权威"，设计 §5.2）。</para>
///
/// <para>全程 EF InMemory + 真实 <see cref="Data.Repositories.Implementations.UserCustomerRepository"/> /
/// <see cref="Data.Repositories.Implementations.CustomerRepository"/>，零外部依赖。</para>
/// </summary>
public sealed class CustomerScopeServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IUserCustomerRepository _grants;
    private readonly CustomerScopeService _sut;

    public CustomerScopeServiceTests()
    {
        _db = CreateDb($"scope-{Guid.NewGuid():N}");
        SeedCustomers(1, 2, 3, 4);
        _grants = new Data.Repositories.Implementations.UserCustomerRepository(_db);
        _sut = new CustomerScopeService(
            _grants,
            new Data.Repositories.Implementations.CustomerRepository(_db));
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
    }

    public void Dispose() => _db.Dispose();

    // ───────────────────────────── ResolveAsync 三分支 ─────────────────────────────

    [Fact(DisplayName = "SCOPE-01 超管 Resolve → All，CustomerIds/AppCodes 均为空（不过滤）")]
    public async Task Resolve_SuperAdmin_ReturnsAllWithEmptyCollections()
    {
        var principal = Principal(1, Roles.SUPER_ADMIN);

        var scope = await _sut.ResolveAsync(principal);

        Assert.Equal(CustomerScopeMode.All, scope.Mode);
        Assert.Equal(CustomerScopeMode.All, scope.ScopeMode);
        Assert.True(scope.IsSuperAdmin);
        Assert.Empty(scope.CustomerIds);
        Assert.Empty(scope.AppCodes);
    }

    [Fact(DisplayName = "SCOPE-02 admin Resolve → Multi，集合来自 user_customers 而非 claim 快照")]
    public async Task Resolve_Admin_ReadsDbGrantsNotClaim()
    {
        // 授权事实：admin 10 持有 [1,2]；claim 快照故意写成完全不同的 [3,4]。
        await _grants.SyncAsync(10, new long[] { 1, 2 }, grantedByUserId: 1);
        var principal = Principal(10, Roles.ADMIN, managedClaim: "3,4");

        var scope = await _sut.ResolveAsync(principal);

        Assert.Equal(CustomerScopeMode.Multi, scope.Mode);
        Assert.Equal(new long[] { 1, 2 }, scope.CustomerIds);
        Assert.Equal(new[] { "customer_001", "customer_002" }, scope.AppCodes);

        // 立即变更 DB（不刷新 claim）：下次解析立刻反映新集合。
        await _grants.SyncAsync(10, new long[] { 2, 3 }, grantedByUserId: 1);
        var refreshed = await _sut.ResolveAsync(principal);
        Assert.Equal(new long[] { 2, 3 }, refreshed.CustomerIds);
        Assert.Equal(new[] { "customer_002", "customer_003" }, refreshed.AppCodes);
    }

    [Fact(DisplayName = "SCOPE-03 admin 无授权 → Multi + 空集合（403 由调用方决定，服务只反映事实）")]
    public async Task Resolve_Admin_WithoutGrants_ReturnsEmptyMultiScope()
    {
        var principal = Principal(10, Roles.ADMIN, managedClaim: "1,2"); // claim 有值但 DB 无授权

        var scope = await _sut.ResolveAsync(principal);

        Assert.Equal(CustomerScopeMode.Multi, scope.Mode);
        Assert.Empty(scope.CustomerIds);
        Assert.Empty(scope.AppCodes);
    }

    [Fact(DisplayName = "SCOPE-04 下级 Resolve → Single，只读 claim 单值且零授权表访问")]
    public async Task Resolve_Subordinate_UsesClaimAndNeverTouchesGrantTable()
    {
        var throwingSut = new CustomerScopeService(
            new ThrowingUserCustomerRepository(),
            new Data.Repositories.Implementations.CustomerRepository(_db));
        var principal = Principal(20, Roles.CHEF, customerId: 1, appCode: "customer_001");

        var scope = await throwingSut.ResolveAsync(principal);

        Assert.Equal(CustomerScopeMode.Single, scope.Mode);
        Assert.Equal(new long[] { 1 }, scope.CustomerIds);
        Assert.Equal(new[] { "customer_001" }, scope.AppCodes);

        // 同一零访问服务再走两个入口，证明下级全链路都不碰 user_customers。
        Assert.True(await throwingSut.CanAccessCustomerAsync(principal, 1));
        Assert.False(await throwingSut.CanAccessCustomerAsync(principal, 2));
        Assert.Equal(new[] { "customer_001" }, await throwingSut.GetAppCodesAsync(principal));
    }

    [Fact(DisplayName = "SCOPE-05 customer_admin 同样走 Single（本轮不纳入集合分支，设计 R9）")]
    public async Task Resolve_CustomerAdmin_StillSingleScope()
    {
        var throwingSut = new CustomerScopeService(
            new ThrowingUserCustomerRepository(),
            new Data.Repositories.Implementations.CustomerRepository(_db));
        var principal = Principal(30, Roles.CUSTOMER_ADMIN, customerId: 2, appCode: "customer_002");

        var scope = await throwingSut.ResolveAsync(principal);

        Assert.Equal(CustomerScopeMode.Single, scope.Mode);
        Assert.Equal(new long[] { 2 }, scope.CustomerIds);
        Assert.Equal(new[] { "customer_002" }, scope.AppCodes);
    }

    // ───────────────────────────── CanAccessCustomerAsync ─────────────────────────────

    [Fact(DisplayName = "SCOPE-06 CanAccess 三分支：超管恒 true / admin 走 DB / 下级仅自身单值")]
    public async Task CanAccessCustomer_ThreeBranches()
    {
        await _grants.SyncAsync(10, new long[] { 1, 2 }, grantedByUserId: 1);

        Assert.True(await _sut.CanAccessCustomerAsync(Principal(1, Roles.SUPER_ADMIN), 999));
        Assert.True(await _sut.CanAccessCustomerAsync(Principal(10, Roles.ADMIN, managedClaim: "3"), 2));
        Assert.False(await _sut.CanAccessCustomerAsync(Principal(10, Roles.ADMIN, managedClaim: "1,2"), 3));
        Assert.True(await _sut.CanAccessCustomerAsync(
            Principal(20, Roles.CHEF, customerId: 1, appCode: "customer_001"), 1));
        Assert.False(await _sut.CanAccessCustomerAsync(
            Principal(20, Roles.CHEF, customerId: 1, appCode: "customer_001"), 2));
    }

    [Fact(DisplayName = "SCOPE-06b 存量下级无 CustomerId claim → CanAccess 回退 AppCode 比对，且零 user_customers 访问")]
    public async Task CanAccess_SubordinateWithoutCustomerIdClaim_FallsBackToAppCode()
    {
        // 存量库下级用户 CustomerId=NULL，JWT 只有 AppCode=customer_001（QA 真机解码确认）。
        var throwingSut = new CustomerScopeService(
            new ThrowingUserCustomerRepository(),
            new Data.Repositories.Implementations.CustomerRepository(_db));
        var principal = Principal(20, Roles.CHEF, appCode: "customer_001"); // 无 CustomerId claim

        Assert.True(await throwingSut.CanAccessCustomerAsync(principal, 1), "自身客户区域必须放行");
        Assert.False(await throwingSut.CanAccessCustomerAsync(principal, 2), "其它客户区域必须拒绝");
    }

    [Fact(DisplayName = "SCOPE-07 CanAccess 授权撤销立竿见影（旧 claim 快照不影响判定）")]
    public async Task CanAccessCustomer_GrantRevocation_TakesEffectImmediately()
    {
        await _grants.SyncAsync(10, new long[] { 1, 2 }, grantedByUserId: 1);
        var oldToken = Principal(10, Roles.ADMIN, managedClaim: "1,2");
        Assert.True(await _sut.CanAccessCustomerAsync(oldToken, 2));

        await _grants.SyncAsync(10, new long[] { 1 }, grantedByUserId: 1);

        Assert.False(await _sut.CanAccessCustomerAsync(oldToken, 2), "旧 token 的 claim 快照不得作为授权依据");
    }

    // ───────────────────────────── GetAppCodesAsync ─────────────────────────────

    [Fact(DisplayName = "SCOPE-08 GetAppCodes 三分支：All 空 / Multi 按 DB 集合反查 / Single claim 单值")]
    public async Task GetAppCodes_ThreeBranches()
    {
        await _grants.SyncAsync(10, new long[] { 1, 3 }, grantedByUserId: 1);

        Assert.Empty(await _sut.GetAppCodesAsync(Principal(1, Roles.SUPER_ADMIN)));
        Assert.Equal(new[] { "customer_001", "customer_003" },
            await _sut.GetAppCodesAsync(Principal(10, Roles.ADMIN, managedClaim: "2")));
        Assert.Equal(new[] { "customer_002" },
            await _sut.GetAppCodesAsync(Principal(20, Roles.OPERATOR, customerId: 2, appCode: "customer_002")));
    }

    // ───────────────────────────── 夹具 ─────────────────────────────

    /// <summary>
    /// InMemory 的 BeginTransactionAsync 是 no-op，但默认会把 TransactionIgnoredWarning 当异常抛出；
    /// 显式忽略该诊断事件后，真实 UserCustomerRepository.SyncAsync 的事务代码路径可被完整覆盖。
    /// </summary>
    internal static AppDbContext CreateDb(string name) => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(name)
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options);

    private void SeedCustomers(params long[] ids)
    {
        foreach (var id in ids)
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
                MaxAreaCount = 100,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }
    }

    private static ClaimsPrincipal Principal(
        long userId,
        string role,
        long? customerId = null,
        string? appCode = null,
        string? managedClaim = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Role, role)
        };
        if (customerId.HasValue)
        {
            claims.Add(new Claim("CustomerId", customerId.Value.ToString()));
        }
        if (!string.IsNullOrEmpty(appCode))
        {
            claims.Add(new Claim("AppCode", appCode));
        }
        if (!string.IsNullOrEmpty(managedClaim))
        {
            claims.Add(new Claim(CustomerScopeConstants.ClaimManagedCustomerIds, managedClaim));
        }
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    /// <summary>任何方法被调用即失败：用于证明 Single / All 分支没有触碰授权表。</summary>
    private sealed class ThrowingUserCustomerRepository : IUserCustomerRepository
    {
        private static InvalidOperationException Failure() =>
            new("下级/超管分支不应访问 user_customers 仓储");

        public Task<IReadOnlyList<long>> GetCustomerIdsAsync(long userId, CancellationToken ct = default) =>
            throw Failure();

        public Task<IReadOnlyDictionary<long, List<long>>> GetCustomerIdsAsync(
            IEnumerable<long> userIds, CancellationToken ct = default) => throw Failure();

        public Task<IReadOnlyList<long>> GetAdminIdsByCustomerIdAsync(long customerId, CancellationToken ct = default) =>
            throw Failure();

        public Task<bool> ExistsAsync(long userId, long customerId, CancellationToken ct = default) =>
            throw Failure();

        public Task SyncAsync(long userId, IReadOnlyCollection<long> customerIds, long? grantedByUserId,
            CancellationToken ct = default) => throw Failure();
    }
}
