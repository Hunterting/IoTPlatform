using System.Security.Claims;
using IoTPlatform.Configuration;
using IoTPlatform.Services;

namespace IoTPlatform.Infrastructure.Tenant;

/// <summary>
/// 租户上下文访问器接口
/// </summary>
public interface ITenantContextAccessor
{
    /// <summary>
    /// 获取当前租户上下文
    /// </summary>
    ITenantContext Current { get; }

    /// <summary>
    /// 从当前HTTP上下文初始化租户信息
    /// </summary>
    void InitializeFromHttpContext(HttpContext httpContext);
}

/// <summary>
/// 租户上下文访问器实现
/// 基于 HttpContext 提取租户信息
/// </summary>
public class TenantContextAccessor : ITenantContextAccessor
{
    private readonly TenantContext _tenantContext;

    public TenantContextAccessor()
    {
        _tenantContext = new TenantContext();
    }

    /// <inheritdoc />
    public ITenantContext Current => _tenantContext;

    /// <inheritdoc />
    public void InitializeFromHttpContext(HttpContext httpContext)
    {
        TenantContextClaimLoader.Load(_tenantContext, httpContext);
    }
}

/// <summary>
/// Scoped 版本的租户上下文访问器
/// 每次请求创建一个新实例
/// </summary>
public class ScopedTenantContextAccessor : ITenantContextAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private TenantContext? _tenantContext;

    public ScopedTenantContextAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public ITenantContext Current
    {
        get
        {
            if (_tenantContext == null)
            {
                _tenantContext = new TenantContext();
                InitializeFromHttpContext(_httpContextAccessor.HttpContext);
            }
            return _tenantContext;
        }
    }

    public void InitializeFromHttpContext(HttpContext httpContext)
    {
        // 原实现在上下文尚未延迟创建时全部走 null 条件运算（即 no-op），此处保持一致。
        if (_tenantContext == null)
        {
            return;
        }

        TenantContextClaimLoader.Load(_tenantContext, httpContext);
    }
}

/// <summary>
/// 两个 TenantContextAccessor 共用的 claim 解析（设计 §5.3）：
/// 下级路径与改造前逐字等价——AppCode 为空仍 Reset，进而由 TenantFilter 返回 403；
/// admin 的 AppCode 按设计留空，不能 Reset（否则集合与 ScopeMode 丢失）；
/// 超管同样无单值 AppCode，需保留 All 模式。
/// </summary>
internal static class TenantContextClaimLoader
{
    internal static void Load(ITenantContext context, HttpContext? httpContext)
    {
        if (httpContext == null || httpContext.User == null)
        {
            context.Reset();
            return;
        }

        var user = httpContext.User;

        // 获取 AppCode
        var appCode = user.FindFirst("AppCode")?.Value ?? string.Empty;

        // 获取 CustomerId
        long? customerId = null;
        var customerIdClaim = user.FindFirst("CustomerId")?.Value;
        if (!string.IsNullOrEmpty(customerIdClaim) && long.TryParse(customerIdClaim, out var parsedCustomerId))
        {
            customerId = parsedCustomerId;
        }

        // 获取授权客户ID集合（仅 admin 持有；claim 是快照，授权判定以 DB 为准）
        IReadOnlyList<long>? managedCustomerIds = null;
        var managedClaim = user.FindFirst(CustomerScopeConstants.ClaimManagedCustomerIds)?.Value;
        if (!string.IsNullOrEmpty(managedClaim))
        {
            var parsedIds = new List<long>();
            foreach (var part in managedClaim!
                .Split(CustomerScopeConstants.ManagedCustomerIdsSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                if (long.TryParse(part.Trim(), out var parsedId))
                {
                    parsedIds.Add(parsedId);
                }
            }

            if (parsedIds.Count > 0)
            {
                managedCustomerIds = parsedIds;
            }
        }

        // 获取 UserId
        long? userId = null;
        var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(userIdClaim) && long.TryParse(userIdClaim, out var parsedUserId))
        {
            userId = parsedUserId;
        }

        // 获取 Role
        var role = user.FindFirst(ClaimTypes.Role)?.Value;

        // 按角色置范围模式：admin→Multi、超管→All、其他（含 customer_admin）→Single
        var scopeMode = role == Roles.ADMIN
            ? CustomerScopeMode.Multi
            : (Roles.IsSuperAdmin(role) ? CustomerScopeMode.All : CustomerScopeMode.Single);

        // 设置租户上下文：下级空 AppCode 仍 Reset（403 语义不变）；
        // admin / 超管即使无单值 AppCode 也保留上下文，否则 ScopeMode 与集合快照会丢失。
        if (!string.IsNullOrEmpty(appCode) || scopeMode != CustomerScopeMode.Single)
        {
            context.SetTenant(appCode, customerId, userId, role, managedCustomerIds, scopeMode);
        }
        else
        {
            context.Reset();
        }
    }
}
