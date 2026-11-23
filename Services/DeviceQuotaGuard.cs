using IoTPlatform.Data;
using Microsoft.EntityFrameworkCore;

namespace IoTPlatform.Services;

/// <summary>
/// 租户设备数量配额守卫（纯静态、无状态，与 <see cref="DeviceRegistrationGuard"/> 同范式）。
///
/// 【职责】把「按目标租户统计设备数 + 与 MaxDeviceCount 上限比较」收敛为单一入口，
/// 供「直接新增」（<see cref="DeviceService.CreateDeviceAsync"/>）与「安圣认领」
/// （<see cref="AnShengDiscoveryService.ClaimAsync"/>）两个建档入口共用，杜绝口径漂移。
///
/// 【统计口径 · 强约束】
///   AppDbContext 的全局租户过滤器在「超管 / AppCode 为空」时根本不注册
///   （Data/AppDbContext.cs:171-204），裸 CountAsync() 会数全表。因此这里：
///     ① 一律显式 Where(d =&gt; d.AppCode == effectiveAppCode)；
///     ② 一律 IgnoreQueryFilters()，避免过滤器冻结在另一个租户上导致少算/误放行。
///   不复用 CustomerRepository.GetCustomerStatsAsync（它按 Project.CustomerId 统计，口径不同）。
///
/// 【并发】ReadLockedAsync 必须对目标租户的 customers 行取 MySQL FOR UPDATE 排他锁，
/// 且调用方已把本查询与后续的 Devices.Add 包进同一事务（CreateExecutionStrategy 之内）。
///
/// 【fail-closed】effectiveAppCode 为空或找不到 Customer 行时返回
/// <see cref="QuotaSnapshot.ConfigMissing"/> = true，由调用方以 400 拒绝，绝不按「0=不限」放行。
/// </summary>
public static class DeviceQuotaGuard
{
    /// <summary>超限文案模板（唯一出口；X、Y 均为真实数字）。</summary>
    public const string MESSAGE_TEMPLATE = "已超出设备数量上限（当前 {0}/上限 {1}）";

    /// <summary>
    /// 非事务版本：用于认领路径的「早检」（探测之前），不做 FOR UPDATE。
    /// </summary>
    /// <param name="db">作用域内的数据库上下文。</param>
    /// <param name="effectiveAppCode">目标租户 AppCode（超管代建也用目标租户，不用调用者自身）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>配额快照；配置缺失时 <see cref="QuotaSnapshot.ConfigMissing"/> = true。</returns>
    public static async Task<QuotaSnapshot> ReadAsync(
        AppDbContext db,
        string effectiveAppCode,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(effectiveAppCode))
        {
            return new QuotaSnapshot(0, 0, ConfigMissing: true);
        }

        var customer = await db.Customers
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(c => c.AppCode == effectiveAppCode)
            .Select(c => new { c.MaxDeviceCount })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (customer is null)
        {
            return new QuotaSnapshot(0, 0, ConfigMissing: true);
        }

        // 绝不可裸 CountAsync()：显式 Where 才是唯一可靠的租户口径。
        var current = await db.Devices
            .AsNoTracking()
            .IgnoreQueryFilters()
            .CountAsync(d => d.AppCode == effectiveAppCode, ct)
            .ConfigureAwait(false);

        return new QuotaSnapshot(current, customer.MaxDeviceCount);
    }

    /// <summary>
    /// 事务内版本：调用方已在 CreateExecutionStrategy 事务中。
    /// 先对目标租户 customer 行加排他锁（MySQL SELECT ... FOR UPDATE），再统计设备数，
    /// 闭合 count→insert 并发窗口；不同租户锁不同行，互不阻塞。
    /// </summary>
    /// <param name="db">已处于用户事务中的数据库上下文。</param>
    /// <param name="effectiveAppCode">目标租户 AppCode。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>配额快照；未命中 Customer 行时 <see cref="QuotaSnapshot.ConfigMissing"/> = true。</returns>
    public static async Task<QuotaSnapshot> ReadLockedAsync(
        AppDbContext db,
        string effectiveAppCode,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(effectiveAppCode))
        {
            return new QuotaSnapshot(0, 0, ConfigMissing: true);
        }

        // AsTracking：被锁定的实体保持在 ChangeTracker 中，锁一直持有到 Commit/Rollback。
        // 调用方须保证本查询发生在其 ChangeTracker.Clear() 之后（DeviceService.cs:488）。
        var locked = await db.Customers
            .FromSqlRaw("SELECT * FROM customers WHERE AppCode = {0} FOR UPDATE", effectiveAppCode)
            .IgnoreQueryFilters()
            .AsTracking()
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (locked is null)
        {
            return new QuotaSnapshot(0, 0, ConfigMissing: true);
        }

        var current = await db.Devices
            .AsNoTracking()
            .IgnoreQueryFilters()
            .CountAsync(d => d.AppCode == effectiveAppCode, ct)
            .ConfigureAwait(false);

        return new QuotaSnapshot(current, locked.MaxDeviceCount);
    }

    /// <summary>
    /// 生成全站统一的超限文案。两个建档入口不得自行拼串。
    /// </summary>
    /// <param name="current">该租户当前设备数（真实值）。</param>
    /// <param name="limit">该租户设备上限（真实值）。</param>
    /// <returns>形如「已超出设备数量上限（当前 3/上限 1）」的文案。</returns>
    public static string BuildExceededMessage(int current, int limit)
        => string.Format(MESSAGE_TEMPLATE, current, limit);
}

/// <summary>
/// 配额快照：三态判定（配置存在且不限 / 配置存在且超标 / 配置缺失）。
/// </summary>
/// <param name="Current">目标租户当前设备数。</param>
/// <param name="Limit">目标租户 MaxDeviceCount。</param>
/// <param name="ConfigMissing">目标租户 Customer 行缺失（fail-closed，不放行）。</param>
public sealed record QuotaSnapshot(int Current, int Limit, bool ConfigMissing = false)
{
    /// <summary>配置存在且上限为 0 → 不限量。</summary>
    public bool IsUnlimited => !ConfigMissing && Limit == 0;

    /// <summary>配置存在、上限 &gt; 0 且当前数已达/超过上限（含改小上限后的存量超标）→ 拒绝新增。</summary>
    public bool Exceeded => !ConfigMissing && !IsUnlimited && Current >= Limit;

    /// <summary>不放行的统一判定：配置缺失或超限。</summary>
    public bool ShouldReject => ConfigMissing || Exceeded;
}
