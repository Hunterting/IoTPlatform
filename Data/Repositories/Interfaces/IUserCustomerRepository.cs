namespace IoTPlatform.Data.Repositories.Interfaces;

/// <summary>
/// 用户-客户授权仓储（多客户授权模型 T02，设计 §3.2 / §3.3）。
///
/// <para>承载 admin ↔ 客户 N:N 授权集合的读写。索引依赖（T01 已建）：</para>
/// <list type="bullet">
///   <item><c>uk_user_customer (UserId, CustomerId)</c>：按 admin 查集合走其 UserId 前缀；ExistsAsync 亦命中该索引。</item>
///   <item><c>idx_customer_id</c>：按客户反查 admin。</item>
/// </list>
///
/// <para><b>安全边界</b>：本仓储是 admin 授权集合的唯一事实来源。任何越权判定都必须经此读取 DB，
/// 不得直接使用 JWT 中的 ManagedCustomerIds claim（"claim 是快照，DB 是权威"，设计 §5.2）。</para>
/// </summary>
public interface IUserCustomerRepository
{
    /// <summary>
    /// 查询指定 admin 当前获授权的全部客户 ID（按 ID 升序，已去重）。
    /// </summary>
    /// <param name="userId">被授权的 admin 用户 ID。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>客户 ID 只读列表；无授权时为空列表（永不返回 null）。</returns>
    Task<IReadOnlyList<long>> GetCustomerIdsAsync(long userId, CancellationToken ct = default);

    /// <summary>
    /// 批量查询多个 admin 的授权客户 ID（一次 SQL，无 N+1）。
    /// </summary>
    /// <param name="userIds">待查询的用户 ID 集合；null/空集合时返回空字典。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>以用户 ID 为键、授权客户 ID 列表为值的只读字典；无授权的 user 不出现在字典中。</returns>
    Task<IReadOnlyDictionary<long, List<long>>> GetCustomerIdsAsync(
        IEnumerable<long> userIds,
        CancellationToken ct = default);

    /// <summary>
    /// 按客户反查持有该授权的 admin ID 列表（走 idx_customer_id）。
    /// </summary>
    /// <param name="customerId">客户 ID。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>admin 用户 ID 只读列表；无授权时为空列表（永不返回 null）。</returns>
    Task<IReadOnlyList<long>> GetAdminIdsByCustomerIdAsync(long customerId, CancellationToken ct = default);

    /// <summary>
    /// 判定指定 admin 是否持有某客户的授权（走 uk_user_customer 前缀，单条 EXISTS）。
    /// 每次调用都查 DB，用于"分配变更即时生效"。
    /// </summary>
    /// <param name="userId">admin 用户 ID。</param>
    /// <param name="customerId">客户 ID。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>存在有效授权返回 true，否则 false。</returns>
    Task<bool> ExistsAsync(long userId, long customerId, CancellationToken ct = default);

    /// <summary>
    /// 全量同步指定 admin 的授权集合：单事务内删除差集、插入并集，幂等且跳过重复键。
    /// 只有超管经分配 API 调用（设计 §7.2-9）。
    /// </summary>
    /// <param name="userId">被授权的 admin 用户 ID。</param>
    /// <param name="customerIds">目标客户 ID 全量集合；null/空表示撤销全部授权。</param>
    /// <param name="grantedByUserId">授予人（超管）用户 ID，可为空；写入审计列 GrantedByUserId。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>完成的任务。</returns>
    Task SyncAsync(
        long userId,
        IReadOnlyCollection<long> customerIds,
        long? grantedByUserId,
        CancellationToken ct = default);
}
