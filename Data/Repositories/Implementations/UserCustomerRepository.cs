using IoTPlatform.Configuration;
using IoTPlatform.Data.Repositories.Interfaces;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;

namespace IoTPlatform.Data.Repositories.Implementations;

/// <summary>
/// 用户-客户授权仓储实现（设计 §3.2 / §6 T02.1）。
///
/// <para>【索引路径】按 admin 查走 <c>uk_user_customer (UserId, CustomerId)</c> 的 UserId 前缀；
/// 按客户反查走 <c>idx_customer_id</c>；Exists 走组合唯一索引前缀。均为单表查询，无 JOIN、无 N+1。</para>
///
/// <para>【租户过滤】UserCustomer 不实现 IHasAppCode，AppDbContext 全局过滤器不覆盖它；
/// 这里仍显式 <c>IgnoreQueryFilters()</c>，确保启动期模型被冻结在任意租户时授权集合都不被误过滤
/// （设计 §5.5 / §7.2-8）。</para>
///
/// <para>【事务铁律】项目启用了 EnableRetryOnFailure（Program.cs），用户事务必须包在
/// <c>Database.CreateExecutionStrategy().ExecuteAsync(...)</c> 中，否则 BeginTransactionAsync
/// 会抛 "does not support user-initiated transactions"。范式参照 AnShengDiscoveryService.ClaimAsync。</para>
/// </summary>
public class UserCustomerRepository : IUserCustomerRepository
{
    private readonly AppDbContext _db;

    public UserCustomerRepository(AppDbContext context)
    {
        _db = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<long>> GetCustomerIdsAsync(long userId, CancellationToken ct = default)
    {
        if (userId <= 0)
        {
            return Array.Empty<long>();
        }

        var ids = await _db.UserCustomers
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(uc => uc.UserId == userId)
            .OrderBy(uc => uc.CustomerId)
            .Select(uc => uc.CustomerId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return ids;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<long, List<long>>> GetCustomerIdsAsync(
        IEnumerable<long> userIds,
        CancellationToken ct = default)
    {
        var ids = userIds?.Where(id => id > 0).Distinct().ToList() ?? new List<long>();
        if (ids.Count == 0)
        {
            return new Dictionary<long, List<long>>();
        }

        // 一次 SQL 取扁平 (UserId, CustomerId) 对，再在内存按 UserId 分组。
        // 避免 GroupBy + ToDictionary 的 EF Core SQL 翻译差异，同时彻底消除 N+1。
        var rows = await _db.UserCustomers
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(uc => ids.Contains(uc.UserId))
            .Select(uc => new { uc.UserId, uc.CustomerId })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var result = new Dictionary<long, List<long>>();
        foreach (var row in rows)
        {
            if (!result.TryGetValue(row.UserId, out var list))
            {
                list = new List<long>();
                result[row.UserId] = list;
            }
            list.Add(row.CustomerId);
        }

        foreach (var list in result.Values)
        {
            list.Sort();
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<long>> GetAdminIdsByCustomerIdAsync(
        long customerId,
        CancellationToken ct = default)
    {
        if (customerId <= 0)
        {
            return Array.Empty<long>();
        }

        var ids = await _db.UserCustomers
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(uc => uc.CustomerId == customerId)
            .OrderBy(uc => uc.UserId)
            .Select(uc => uc.UserId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return ids;
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(long userId, long customerId, CancellationToken ct = default)
    {
        if (userId <= 0 || customerId <= 0)
        {
            return false;
        }

        return await _db.UserCustomers
            .AsNoTracking()
            .IgnoreQueryFilters()
            .AnyAsync(uc => uc.UserId == userId && uc.CustomerId == customerId, ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SyncAsync(
        long userId,
        IReadOnlyCollection<long> customerIds,
        long? grantedByUserId,
        CancellationToken ct = default)
    {
        if (userId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(userId), "用户 ID 必须为正数。");
        }

        // 归一化：过滤非正 ID + 去重（保留传入顺序），从源头保证不产生重复键。
        var desired = new List<long>();
        var desiredSet = new HashSet<long>();
        if (customerIds != null)
        {
            foreach (var customerId in customerIds)
            {
                if (customerId > 0 && desiredSet.Add(customerId))
                {
                    desired.Add(customerId);
                }
            }
        }

        // 事务整体包在重试单元内：瞬时故障由执行策略整体重放 lambda（查询 → 删差集 → 插并集 → 提交）。
        await _db.Database.CreateExecutionStrategy().ExecuteAsync(async token =>
        {
            // 清除遗留跟踪项，避免同一 Scoped DbContext 中先前跟踪的 UserCustomer 实体
            // 影响本事务的删除/插入状态（项目既有范式见 DeviceService / AnShengDiscoveryService）。
            _db.ChangeTracker.Clear();

            await using var transaction = await _db.Database.BeginTransactionAsync(token).ConfigureAwait(false);
            try
            {
                var rows = await _db.UserCustomers
                    .IgnoreQueryFilters()
                    .Where(uc => uc.UserId == userId)
                    .ToListAsync(token)
                    .ConfigureAwait(false);

                var existingIds = new HashSet<long>(rows.Select(uc => uc.CustomerId));
                var toRemove = rows.Where(uc => !desiredSet.Contains(uc.CustomerId)).ToList();
                var toAdd = desired.Where(customerId => !existingIds.Contains(customerId)).ToList();

                if (toRemove.Count > 0)
                {
                    _db.UserCustomers.RemoveRange(toRemove);
                }

                if (toAdd.Count > 0)
                {
                    var now = DateTime.UtcNow;
                    _db.UserCustomers.AddRange(toAdd.Select(customerId => new UserCustomer
                    {
                        UserId = userId,
                        CustomerId = customerId,
                        GrantedByUserId = grantedByUserId,
                        Source = CustomerScopeConstants.SourceManual,
                        CreatedAt = now,
                        UpdatedAt = now,
                        RevokedAt = null
                    }));
                }

                // 幂等：目标集合与现状一致时零行变更，仍正常提交空事务。
                if (toRemove.Count > 0 || toAdd.Count > 0)
                {
                    await _db.SaveChangesAsync(token).ConfigureAwait(false);
                }

                await transaction.CommitAsync(token).ConfigureAwait(false);
            }
            catch
            {
                // 回滚失败不得覆盖原始异常（例如取消令牌已触发）。
                try
                {
                    await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // 有意吞掉回滚异常，保留业务/数据库原始异常向外抛出。
                }
                throw;
            }
        }, ct).ConfigureAwait(false);
    }
}
