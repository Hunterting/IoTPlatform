using System.Security.Claims;

namespace IoTPlatform.Services;

/// <summary>
/// 授权范围模式（设计 §3.2 / §3.3）。
/// </summary>
public enum CustomerScopeMode
{
    /// <summary>超管：全部客户，AppCodes 为空表示不过滤。</summary>
    All,

    /// <summary>admin：授权客户集合，每次请求从 user_customers 读取（DB 为准）。</summary>
    Multi,

    /// <summary>下级（含 customer_admin）：单客户，只读 JWT claim，不查授权表。</summary>
    Single
}

/// <summary>
/// 解析出的客户范围值对象（设计 §3.2 / §3.3）。
/// </summary>
/// <param name="Mode">范围模式。</param>
/// <param name="CustomerIds">可达客户 ID 集合；All 时为空（表示不过滤），Single 时最多一个。</param>
/// <param name="AppCodes">可达 AppCode 集合；All 时为空（表示不过滤）。</param>
public sealed record CustomerScope(
    CustomerScopeMode Mode,
    IReadOnlyList<long> CustomerIds,
    IReadOnlyList<string> AppCodes)
{
    /// <summary>设计别名：与 <see cref="Mode"/> 同义，便于调用方按设计文档中的字段名读取。</summary>
    public CustomerScopeMode ScopeMode => Mode;

    /// <summary>是否超管范围（由 Mode 派生，避免与 Mode 产生二义状态）。</summary>
    public bool IsSuperAdmin => Mode == CustomerScopeMode.All;
}

/// <summary>
/// 客户访问范围服务：越权判定的唯一入口（设计 §1.2 P-C / §7.2-4）。
///
/// <para><b>三分支语义</b>：</para>
/// <list type="number">
///   <item>All（超管）：短路放行，AppCodes 返回空表示不过滤。</item>
///   <item>Multi（admin）：<b>每次请求</b>读 user_customers；JWT 的 ManagedCustomerIds 仅作信号/展示，
///         绝不作为授权判定依据；授权变更即时生效。</item>
///   <item>Single（下级，含 customer_admin）：从 JWT claim 读单个 CustomerId/AppCode，
///         绝不查 user_customers，零回归。</item>
/// </list>
/// </summary>
public interface ICustomerScopeService
{
    /// <summary>
    /// 按 principal 的角色分叉解析客户范围。
    /// </summary>
    /// <param name="principal">当前请求的 ClaimsPrincipal。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>客户范围值对象；空集合的 403 语义由调用方处理。</returns>
    Task<CustomerScope> ResolveAsync(ClaimsPrincipal principal, CancellationToken ct = default);

    /// <summary>
    /// 判定 principal 是否可访问指定客户（Multi 分支走 DB Exists，Single 分支比对 claim 单值，All 恒 true）。
    /// </summary>
    /// <param name="principal">当前请求的 ClaimsPrincipal。</param>
    /// <param name="customerId">目标客户 ID。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>可访问返回 true，否则 false。</returns>
    Task<bool> CanAccessCustomerAsync(ClaimsPrincipal principal, long customerId, CancellationToken ct = default);

    /// <summary>
    /// 获取 principal 可达的 AppCode 集合，供区域/设备等按租户取数链路使用。
    /// All 返回空集合（表示不过滤）；Multi 每次从 DB 集合反查；Single 返回 claim 单值。
    /// </summary>
    /// <param name="principal">当前请求的 ClaimsPrincipal。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>AppCode 只读列表；无可达租户时为空列表。</returns>
    Task<IReadOnlyList<string>> GetAppCodesAsync(ClaimsPrincipal principal, CancellationToken ct = default);

    // ── 授权缺口统一修复新增（设计 §3.2 A；点查一律服务端派生，禁止采信客户端可控归属）──────

    /// <summary>
    /// 由 AppCode 反查客户 ID（AppCode↔Customer 1:1 不变，设计 §7.2-7）。
    /// 点查使用 IgnoreQueryFilters()，不受启动期冻结的租户全局过滤器影响。
    /// </summary>
    /// <param name="appCode">待解析的 AppCode（来自已校验的目标或 claim，禁止直接取请求头）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>客户 ID；AppCode 为空或不存在时返回 null。</returns>
    Task<long?> ResolveCustomerIdByAppCodeAsync(string appCode, CancellationToken ct = default);

    /// <summary>
    /// 由客户 ID 反查 AppCode（M1/M2/M9/M10 归属派生入口）。
    /// 调用方必须先通过 <see cref="CanAccessCustomerAsync"/> 校验目标归属，再取返回的 AppCode 落库。
    /// </summary>
    /// <param name="customerId">已授权的目标客户 ID。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>AppCode；客户不存在时返回 null。</returns>
    Task<string?> ResolveAppCodeByCustomerIdAsync(long customerId, CancellationToken ct = default);

    /// <summary>
    /// 由设备 ID 反查其归属客户 ID（M4 间接归属）。
    /// 设备仅带 AppCode，先 IgnoreQueryFilters 点查设备 AppCode，再按 1:1 不变映射反查客户。
    /// </summary>
    /// <param name="deviceId">设备 ID。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>客户 ID；设备不存在或归属缺失时返回 null。</returns>
    Task<long?> ResolveCustomerIdByDeviceIdAsync(long deviceId, CancellationToken ct = default);

    /// <summary>
    /// 由项目 ID 反查其归属客户 ID（M9 合同上传用）。
    /// 项目表自带 CustomerId 权威列，IgnoreQueryFilters 点查后原值返回。
    /// </summary>
    /// <param name="projectId">项目 ID。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>客户 ID；项目不存在时返回 null。</returns>
    Task<long?> ResolveCustomerIdByProjectIdAsync(long projectId, CancellationToken ct = default);

    /// <summary>
    /// 判定一行是否属于"有意的平台级共享数据"（字典/配置类：AppCode 为空或 IsSystem=true，设计 §3.5）。
    /// <b>绝不可泛化</b>：附件/角色/受控设备的 AppCode=null 是缺陷（无归属），必须 fail-closed。
    /// </summary>
    /// <param name="appCode">实体的 AppCode。</param>
    /// <param name="isSystem">实体的 IsSystem 标记（无该列时传 null）。</param>
    /// <returns>共享行返回 true；否则 false。</returns>
    bool IsSharedSystemRow(string? appCode, bool? isSystem);

    /// <summary>
    /// 读侧共享行判据：在 <see cref="IsSharedSystemRow"/> 基础上额外认可字符串 AppCode="system"
    /// （与 QueryScopeHelper.BuildSharedPredicate 同源）。<b>仅用于读</b>——写侧仍走严格判据，
    /// 避免把 "system" 行泛化为任何角色可写（设计 §3.5 铁律）。
    /// </summary>
    bool IsSharedReadRow(string? appCode, bool? isSystem)
        => IsSharedSystemRow(appCode, isSystem)
           || string.Equals(appCode, "system", StringComparison.OrdinalIgnoreCase);
}
