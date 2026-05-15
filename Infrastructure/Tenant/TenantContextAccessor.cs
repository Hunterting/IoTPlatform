using System.Security.Claims;

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
        if (httpContext == null || httpContext.User == null)
        {
            _tenantContext.Reset();
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

        // 获取 UserId
        long? userId = null;
        var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(userIdClaim) && long.TryParse(userIdClaim, out var parsedUserId))
        {
            userId = parsedUserId;
        }

        // 获取 Role
        var role = user.FindFirst(ClaimTypes.Role)?.Value;

        // 设置租户上下文
        if (!string.IsNullOrEmpty(appCode))
        {
            _tenantContext.SetTenant(appCode, customerId, userId, role);
        }
        else
        {
            _tenantContext.Reset();
        }
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
        if (httpContext == null || httpContext.User == null)
        {
            _tenantContext?.Reset();
            return;
        }

        var user = httpContext.User;

        var appCode = user.FindFirst("AppCode")?.Value ?? string.Empty;
        long? customerId = null;
        var customerIdClaim = user.FindFirst("CustomerId")?.Value;
        if (!string.IsNullOrEmpty(customerIdClaim) && long.TryParse(customerIdClaim, out var parsedCustomerId))
        {
            customerId = parsedCustomerId;
        }

        long? userId = null;
        var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(userIdClaim) && long.TryParse(userIdClaim, out var parsedUserId))
        {
            userId = parsedUserId;
        }

        var role = user.FindFirst(ClaimTypes.Role)?.Value;

        if (!string.IsNullOrEmpty(appCode))
        {
            _tenantContext?.SetTenant(appCode, customerId, userId, role);
        }
        else
        {
            _tenantContext?.Reset();
        }
    }
}
