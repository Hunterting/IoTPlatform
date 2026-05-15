using IoTPlatform.Configuration;

namespace IoTPlatform.Infrastructure.Tenant;

/// <summary>
/// 租户上下文接口
/// </summary>
public interface ITenantContext
{
    /// <summary>
    /// 当前租户的应用代码
    /// </summary>
    string AppCode { get; }

    /// <summary>
    /// 当前租户的客户ID
    /// </summary>
    long? CustomerId { get; }

    /// <summary>
    /// 当前用户ID
    /// </summary>
    long? UserId { get; }

    /// <summary>
    /// 当前用户角色
    /// </summary>
    string? Role { get; }

    /// <summary>
    /// 是否为超级管理员
    /// </summary>
    bool IsSuperAdmin { get; }

    /// <summary>
    /// 设置租户信息
    /// </summary>
    void SetTenant(string appCode, long? customerId = null, long? userId = null, string? role = null);

    /// <summary>
    /// 重置租户上下文
    /// </summary>
    void Reset();
}

/// <summary>
/// 租户上下文实现
/// </summary>
public class TenantContext : ITenantContext
{
    public string AppCode { get; private set; } = string.Empty;

    public long? CustomerId { get; private set; }

    public long? UserId { get; private set; }

    public string? Role { get; private set; }

    public bool IsSuperAdmin => Role == Roles.SUPER_ADMIN;

    public void SetTenant(string appCode, long? customerId = null, long? userId = null, string? role = null)
    {
        AppCode = appCode ?? throw new ArgumentNullException(nameof(appCode));
        CustomerId = customerId;
        UserId = userId;
        Role = role;
    }

    public void Reset()
    {
        AppCode = string.Empty;
        CustomerId = null;
        UserId = null;
        Role = null;
    }
}
