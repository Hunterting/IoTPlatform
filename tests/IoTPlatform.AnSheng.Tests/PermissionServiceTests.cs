using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using IoTPlatform.Configuration;
using IoTPlatform.Data.Repositories.Interfaces;
using IoTPlatform.Models;
using IoTPlatform.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IoTPlatform.AnSheng.Tests;

/// <summary>
/// 针对「自定义角色 Code 权限真正生效」改动的<b>单元测试</b>：
/// 验证 <see cref="PermissionService.GetEffectivePermissionsAsync"/> 以 DB <c>Role.Permissions</c>
/// 为单一事实来源的三段回退逻辑（超管→DB JSON→静态表）。
///
/// 不依赖 MySQL：用内存替身 <see cref="FakeRoleRepository"/> 模拟 <see cref="IRoleRepository.GetByCodeAsync"/>。
/// （Moq 未列入本工程依赖且本机无缓存，按任务「用 MOQ 等模拟」要求以手写替身等价替代。）
/// </summary>
public sealed class PermissionServiceTests
{
    /// <summary>
    /// 手写 IRoleRepository 替身：仅 <see cref="GetByCodeAsync"/> 按种子返回，其余成员返回无害默认值。
    /// 关键契约：<c>GetByCodeAsync(code, null)</c> 在 appCode 为空时不按租户过滤（与生产 Repository.ApplyFilters 一致），
    /// 因此这里对 AppCode 不做任何过滤，只按 Code 命中。
    /// </summary>
    private sealed class FakeRoleRepository : IRoleRepository
    {
        private readonly Dictionary<string, Role?> _byCode =
            new(StringComparer.OrdinalIgnoreCase);

        public void Seed(string code, string? permissionsJson)
        {
            _byCode[code] = permissionsJson is null
                ? null
                : new Role { Code = code, Permissions = permissionsJson };
        }

        public Task<Role?> GetByCodeAsync(string code, string? appCode = null) =>
            Task.FromResult(_byCode.TryGetValue(code, out var role) ? role : null);

        public Task<IEnumerable<Role>> GetSystemRolesAsync() =>
            Task.FromResult(Enumerable.Empty<Role>());

        public Task<bool> CodeExistsAsync(string code, string? appCode = null, long? excludeRoleId = null) =>
            Task.FromResult(_byCode.ContainsKey(code));

        public Task<IEnumerable<Role>> GetByPermissionAsync(string permission, string? appCode = null) =>
            Task.FromResult(Enumerable.Empty<Role>());

        public Task UpdatePermissionsAsync(long roleId, List<string> permissions) =>
            Task.CompletedTask;

        public Task<Role?> GetByIdAsync(long id, string[]? includeProperties = null, string? appCode = null, List<long>? allowedAreaIds = null) =>
            Task.FromResult<Role?>(null);

        public Task<IEnumerable<Role>> GetAllAsync(string[]? includeProperties = null, string? appCode = null, List<long>? allowedAreaIds = null) =>
            Task.FromResult(Enumerable.Empty<Role>());

        public Task<IEnumerable<Role>> GetAsync(Expression<Func<Role, bool>> predicate, string[]? includeProperties = null, string? appCode = null, List<long>? allowedAreaIds = null) =>
            Task.FromResult(Enumerable.Empty<Role>());

        public Task<(IEnumerable<Role> Items, int TotalCount, int TotalPages)> GetPagedAsync(
            int pageNumber = 1, int pageSize = 10, Expression<Func<Role, bool>>? predicate = null,
            Expression<Func<Role, object>>? orderBy = null, bool isDescending = false,
            string[]? includeProperties = null, string? appCode = null, List<long>? allowedAreaIds = null) =>
            Task.FromResult((Enumerable.Empty<Role>(), 0, 0));

        public IQueryable<Role> GetQueryable(string? appCode = null, List<long>? allowedAreaIds = null) =>
            new List<Role>().AsQueryable();

        public Task<Role> AddAsync(Role entity) => Task.FromResult(entity);

        public Task AddRangeAsync(IEnumerable<Role> entities) => Task.CompletedTask;

        public Task UpdateAsync(Role entity) => Task.CompletedTask;

        public Task UpdateRangeAsync(IEnumerable<Role> entities) => Task.CompletedTask;

        public Task DeleteAsync(Role entity) => Task.CompletedTask;

        public Task<bool> DeleteByIdAsync(long id, string? appCode = null, List<long>? allowedAreaIds = null) =>
            Task.FromResult(false);

        public Task DeleteRangeAsync(IEnumerable<Role> entities) => Task.CompletedTask;

        public Task<bool> ExistsAsync(Expression<Func<Role, bool>> predicate, string? appCode = null, List<long>? allowedAreaIds = null) =>
            Task.FromResult(false);

        public Task<int> CountAsync(Expression<Func<Role, bool>>? predicate = null, string? appCode = null, List<long>? allowedAreaIds = null) =>
            Task.FromResult(0);
    }

    private static PermissionService CreateService(FakeRoleRepository repo) =>
        new(repo, NullLogger<PermissionService>.Instance);

    /// <summary>超管短路：直接返回 <see cref="Permissions.All"/>（全量集合）。</summary>
    [Fact(DisplayName = "PS-01 超管 → 返回 Permissions.All 全量集合")]
    public async Task SuperAdmin_ReturnsPermissionsAll()
    {
        var svc = CreateService(new FakeRoleRepository());

        var result = await svc.GetEffectivePermissionsAsync(Roles.SUPER_ADMIN);

        AssertSetEqual(Permissions.All, result);
    }

    /// <summary>
    /// 内置角色（staff）且无 DB 记录 → 回退到静态表 <see cref="Roles.GetStaffPermissions"/>，
    /// 证明 5 个内置角色零回归（不读 DB 也能拿到正确权限）。
    /// </summary>
    [Fact(DisplayName = "PS-02 内置角色 staff(DB 无记录) → 回退静态 staff 权限")]
    public async Task BuiltInRole_NoDbRecord_ReturnsStaticPermissions()
    {
        var repo = new FakeRoleRepository();
        repo.Seed(Roles.STAFF, null); // DB 查不到 → 回退静态
        var svc = CreateService(repo);

        var result = await svc.GetEffectivePermissionsAsync(Roles.STAFF);

        AssertSetEqual(Roles.GetStaffPermissions(), result);
    }

    /// <summary>
    /// 自定义 Code 且 DB <c>Permissions</c> 为 JSON → 返回该集合（单一事实来源生效）。
    /// 这里故意用 DB 里写的值，而非任何静态表，证明「自定义 Code 权限真正生效」。
    /// </summary>
    [Fact(DisplayName = "PS-03 自定义 Code(DB Permissions=view_dashboard,view_billing) → 返回该集合")]
    public async Task CustomRole_DbPermissions_ReturnedAsIs()
    {
        var repo = new FakeRoleRepository();
        repo.Seed("custom_viewer", "[\"view_dashboard\",\"view_billing\"]");
        var svc = CreateService(repo);

        var result = await svc.GetEffectivePermissionsAsync("custom_viewer");

        Assert.Equal(2, result.Count);
        Assert.Contains("view_dashboard", result);
        Assert.Contains("view_billing", result);
    }

    /// <summary>
    /// 自定义 Code 但 DB 无记录 → 回退静态表；自定义 Code 不在内置 5 角色中 → 空列表。
    /// </summary>
    [Fact(DisplayName = "PS-04 自定义 Code(DB 无记录) → 回退静态 → 空列表")]
    public async Task CustomRole_NoDbRecord_ReturnsEmpty()
    {
        var repo = new FakeRoleRepository();
        repo.Seed("ghost_role", null);
        var svc = CreateService(repo);

        var result = await svc.GetEffectivePermissionsAsync("ghost_role");

        Assert.Empty(result);
    }

    /// <summary>
    /// 自定义 Code 但 DB <c>Permissions</c> 为非法 JSON → catch 兜底回退静态 → 空列表（不抛异常）。
    /// </summary>
    [Fact(DisplayName = "PS-05 自定义 Code(DB Permissions 非法 JSON) → catch 兜底 → 空列表")]
    public async Task CustomRole_InvalidJson_ReturnsEmpty()
    {
        var repo = new FakeRoleRepository();
        repo.Seed("bad_json_role", "{这不是合法JSON");
        var svc = CreateService(repo);

        var result = await svc.GetEffectivePermissionsAsync("bad_json_role");

        Assert.Empty(result);
    }

    /// <summary>
    /// 自定义 Code 的 DB <c>Permissions</c> 含重复项 → 经 <c>.Distinct()</c> 去重返回。
    /// </summary>
    [Fact(DisplayName = "PS-06 自定义 Code(DB Permissions 含重复) → Distinct 去重")]
    public async Task CustomRole_DbPermissions_DuplicatesDeduplicated()
    {
        var repo = new FakeRoleRepository();
        repo.Seed("dup_role", "[\"view_dashboard\",\"view_dashboard\",\"view_billing\"]");
        var svc = CreateService(repo);

        var result = await svc.GetEffectivePermissionsAsync("dup_role");

        Assert.Equal(2, result.Count);
        Assert.Contains("view_dashboard", result);
        Assert.Contains("view_billing", result);
    }

    /// <summary>
    /// 自定义 Code 的 DB <c>Permissions</c> 为合法但空的 JSON <c>[]</c> →
    /// perms.Count == 0 不进入返回分支 → 回退静态 → 自定义 Code 返回空列表。
    /// </summary>
    [Fact(DisplayName = "PS-07 自定义 Code(DB Permissions=[]) → 空集合 → 回退静态 → 空列表")]
    public async Task CustomRole_EmptyJsonArray_ReturnsEmpty()
    {
        var repo = new FakeRoleRepository();
        repo.Seed("empty_arr_role", "[]");
        var svc = CreateService(repo);

        var result = await svc.GetEffectivePermissionsAsync("empty_arr_role");

        Assert.Empty(result);
    }

    /// <summary>
    /// 顺序无关的集合相等断言：先比数量，再逐一确认包含。
    /// </summary>
    private static void AssertSetEqual(List<string> expected, List<string> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        foreach (var item in expected)
        {
            Assert.Contains(item, actual);
        }
    }
}
