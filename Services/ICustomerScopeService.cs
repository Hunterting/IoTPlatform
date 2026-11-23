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
}
