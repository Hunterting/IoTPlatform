using IoTPlatform.Configuration;
using IoTPlatform.Services;

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
    /// 授权客户ID集合快照（仅 admin/Multi 分支非空；claim 是快照，DB 才是授权依据）。
    /// </summary>
    IReadOnlyList<long>? ManagedCustomerIds { get; }

    /// <summary>
    /// 客户范围模式：All=超管、Multi=admin 集合、Single=下级单客户。
    /// </summary>
    CustomerScopeMode ScopeMode { get; }

    /// <summary>
    /// 设置租户信息（新增两个可选尾参，原 4 参调用点零改动）。
    /// </summary>
    void SetTenant(
        string appCode,
        long? customerId = null,
        long? userId = null,
        string? role = null,
        IReadOnlyList<long>? managedCustomerIds = null,
        CustomerScopeMode scopeMode = CustomerScopeMode.Single);

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

    public IReadOnlyList<long>? ManagedCustomerIds { get; private set; }

    public CustomerScopeMode ScopeMode { get; private set; } = CustomerScopeMode.Single;

    public void SetTenant(
        string appCode,
        long? customerId = null,
        long? userId = null,
        string? role = null,
        IReadOnlyList<long>? managedCustomerIds = null,
        CustomerScopeMode scopeMode = CustomerScopeMode.Single)
    {
        AppCode = appCode ?? throw new ArgumentNullException(nameof(appCode));
        CustomerId = customerId;
        UserId = userId;
        Role = role;
        ManagedCustomerIds = managedCustomerIds;
        ScopeMode = scopeMode;
    }

    public void Reset()
    {
        AppCode = string.Empty;
        CustomerId = null;
        UserId = null;
        Role = null;
        ManagedCustomerIds = null;
        ScopeMode = CustomerScopeMode.Single;
    }
}
