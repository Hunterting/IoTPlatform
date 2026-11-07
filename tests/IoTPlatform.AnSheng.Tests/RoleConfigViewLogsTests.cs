using IoTPlatform.Configuration;
using Xunit;

namespace IoTPlatform.AnSheng.Tests;

/// <summary>
/// 验收「P1 权限修复」：全部非超管租户角色（admin/operator/chef/staff/customer_admin/user）
/// 的静态权限集均已包含 <see cref="Permissions.VIEW_LOGS"/>；超管返回 <see cref="Permissions.All"/>（含 VIEW_LOGS）。
///
/// 纯单元测试，无 DB 依赖：直接调用 <see cref="Roles.GetRolePermissions(string)"/> 与各
/// <c>GetXxxPermissions()</c> 静态方法。对应本次修复的 Configuration/RoleConfig.cs。
///
/// 【为什么要存在】
///   本次修复前，这些角色在静态权限集里没有 view_logs，导致 LogsController 上的
///   [PermissionAuthorize(Permissions.VIEW_LOGS)] 对它们一律 403，登录日志彻底不可见。
///   本类把这些角色的静态权限快照钉死，防止未来回退。
/// </summary>
public sealed class RoleConfigViewLogsTests
{
    /// <summary>全部非超管内置角色码，必须与 IoTPlatform.Configuration.Roles 常量一致。</summary>
    public static readonly TheoryData<string> NonSuperAdminRoles = new()
    {
        Roles.ADMIN,
        Roles.OPERATOR,
        Roles.CHEF,
        Roles.STAFF,
        Roles.CUSTOMER_ADMIN,
        Roles.USER
    };

    [Theory(DisplayName = "A-01 非超管角色 GetRolePermissions 均含 view_logs")]
    [MemberData(nameof(NonSuperAdminRoles))]
    public void NonSuperAdminRole_Permissions_ContainViewLogs(string role)
    {
        // Act
        var perms = Roles.GetRolePermissions(role);

        // Assert：该角色必须能查看日志（P1 修复的核心）
        Assert.True(perms.Contains(Permissions.VIEW_LOGS),
            $"角色 {role} 必须持有 view_logs，否则 LogsController 会对其 403");
    }

    [Theory(DisplayName = "A-02 各 GetXxxPermissions() 工厂方法均含 view_logs")]
    [MemberData(nameof(NonSuperAdminRoles))]
    public void GetXxxPermissions_Factory_ContainViewLogs(string role)
    {
        // Act：逐一调用与各角色对应的工厂方法，断言都含 view_logs
        var perms = role.ToLower() switch
        {
            Roles.ADMIN => Roles.GetAdminPermissions(),
            Roles.CUSTOMER_ADMIN => Roles.GetCustomerAdminPermissions(),
            Roles.OPERATOR => Roles.GetOperatorPermissions(),
            Roles.CHEF => Roles.GetChefPermissions(),
            Roles.STAFF => Roles.GetStaffPermissions(),
            Roles.USER => Roles.GetUserPermissions(),
            _ => Roles.GetRolePermissions(role)
        };

        // Assert
        Assert.True(perms.Contains(Permissions.VIEW_LOGS),
            $"工厂方法 {role} 必须含 view_logs");
    }

    [Fact(DisplayName = "A-03 超管权限为 admin 的超集且含 view_logs")]
    public void SuperAdmin_Permissions_IsSupersetOfAdminAndContainsViewLogs()
    {
        // Act
        var perms = Roles.GetRolePermissions(Roles.SUPER_ADMIN);

        // Assert：超管是最高权限，必须至少覆盖 admin 的全部权限（GetAdminPermissions 由
        // GetSuperAdminPermissions 去除少量 API 配置权限得到，admin ⊆ super_admin 是既定契约）。
        var adminPerms = Roles.GetAdminPermissions();
        Assert.All(adminPerms, p => Assert.True(perms.Contains(p),
            $"超管权限集合应含 admin 的 {p}"));

        // 本次修复聚焦：超管必须能查看日志
        Assert.True(perms.Contains(Permissions.VIEW_LOGS),
            "超管必须包含 view_logs");
    }

    [Fact(DisplayName = "A-04 未知角色码回退空列表（不泄露 view_logs）")]
    public void UnknownRole_FallsBackToEmpty()
    {
        // Act
        var perms = Roles.GetRolePermissions("totally_unknown_role_code");

        // Assert：未知角色不应意外获得任何权限（含 view_logs）
        Assert.Empty(perms);
        Assert.False(perms.Contains(Permissions.VIEW_LOGS));
    }
}
