using IoTPlatform.Configuration;

namespace IoTPlatform.Services;

/// <summary>
/// 权限服务接口
/// </summary>
public interface IPermissionService
{
    /// <summary>
    /// 获取指定角色的所有权限
    /// </summary>
    List<string> GetRolePermissions(string roleCode);

    /// <summary>
    /// 获取所有角色及其权限
    /// </summary>
    Dictionary<string, List<string>> GetAllRolePermissions();

    /// <summary>
    /// 获取所有权限常量列表
    /// </summary>
    List<string> GetAllPermissionCodes();

    /// <summary>
    /// 获取权限常量及其描述
    /// </summary>
    Dictionary<string, string> GetPermissionDescriptions();

    /// <summary>
    /// 检查角色是否拥有指定权限
    /// </summary>
    bool HasPermission(string roleCode, string permissionCode);
}

/// <summary>
/// 权限服务实现
/// </summary>
public class PermissionService : IPermissionService
{
    /// <inheritdoc />
    public List<string> GetRolePermissions(string roleCode)
    {
        return Roles.GetRolePermissions(roleCode);
    }

    /// <inheritdoc />
    public Dictionary<string, List<string>> GetAllRolePermissions()
    {
        return new Dictionary<string, List<string>>
        {
            { Roles.SUPER_ADMIN, Roles.GetSuperAdminPermissions() },
            { Roles.ADMIN, Roles.GetAdminPermissions() },
            { Roles.OPERATOR, Roles.GetOperatorPermissions() },
            { Roles.CHEF, Roles.GetChefPermissions() },
            { Roles.STAFF, Roles.GetStaffPermissions() }
        };
    }

    /// <inheritdoc />
    public List<string> GetAllPermissionCodes()
    {
        return new List<string>
        {
            // 工作台
            Permissions.VIEW_DASHBOARD,
            // 客户管理
            Permissions.VIEW_CUSTOMERS,
            Permissions.CREATE_CUSTOMERS,
            Permissions.UPDATE_CUSTOMERS,
            Permissions.DELETE_CUSTOMERS,
            // 设备管理
            Permissions.VIEW_DEVICES,
            Permissions.CREATE_DEVICES,
            Permissions.UPDATE_DEVICES,
            Permissions.DELETE_DEVICES,
            // 项目管理
            Permissions.VIEW_PROJECTS,
            Permissions.MANAGE_PROJECTS,
            // 区域管理
            Permissions.VIEW_AREAS,
            // 告警中心
            Permissions.VIEW_ALERT_CENTER,
            Permissions.CREATE_ALERTS,
            Permissions.UPDATE_ALERTS,
            Permissions.DELETE_ALERTS,
            // 实时监控
            Permissions.VIEW_MONITORING,
            // 智能分析
            Permissions.VIEW_ANALYTICS,
            // 空气质量
            Permissions.VIEW_AIR_QUALITY,
            // 环境监测
            Permissions.VIEW_ENVIRONMENT_MONITORING,
            // 档案管理
            Permissions.VIEW_ARCHIVES,
            Permissions.CREATE_ARCHIVES,
            Permissions.UPDATE_ARCHIVES,
            Permissions.DELETE_ARCHIVES,
            // 工单管理
            Permissions.VIEW_WORK_ORDERS,
            Permissions.CREATE_WORK_ORDERS,
            Permissions.UPDATE_WORK_ORDERS,
            Permissions.DELETE_WORK_ORDERS,
            // 日志管理
            Permissions.VIEW_LOGS,
            // 用户管理
            Permissions.VIEW_USERS,
            Permissions.CREATE_USERS,
            Permissions.UPDATE_USERS,
            Permissions.DELETE_USERS,
            // 角色管理
            Permissions.VIEW_ROLES,
            Permissions.CREATE_ROLES,
            Permissions.UPDATE_ROLES,
            Permissions.DELETE_ROLES,
            // 系统设置
            Permissions.VIEW_SETTINGS,
            Permissions.UPDATE_SETTINGS,
            // API配置
            Permissions.VIEW_API_CONFIG,
            Permissions.UPDATE_API_CONFIG,
            // 字典管理
            Permissions.VIEW_DICTIONARY,
            Permissions.CREATE_DICTIONARY,
            Permissions.UPDATE_DICTIONARY,
            Permissions.DELETE_DICTIONARY,
            // 数据采集
            Permissions.VIEW_DATA_COLLECTION,
            Permissions.MANAGE_PROTOCOLS,
            Permissions.MANAGE_RULES,
            Permissions.EXPORT_DATA,
            // 协议与接入管理
            Permissions.VIEW_PROTOCOL_CONFIG,
            Permissions.MANAGE_PROTOCOL_CONFIG,
            Permissions.VIEW_PROTOCOL_GATEWAY,
            Permissions.MANAGE_PROTOCOL_GATEWAY,
            Permissions.VIEW_NETWORK_TUNNEL,
            Permissions.MANAGE_NETWORK_TUNNEL,
            Permissions.VIEW_PLUGIN_SYSTEM,
            Permissions.MANAGE_PLUGIN_SYSTEM,
            // 数据处理
            Permissions.VIEW_DATA_CENTER,
            Permissions.MANAGE_DATA_CENTER,
            Permissions.VIEW_RULE_ENGINE,
            Permissions.MANAGE_RULE_ENGINE,
            Permissions.VIEW_DATA_TRANSFORM,
            Permissions.MANAGE_DATA_TRANSFORM,
            Permissions.VIEW_DATABASE_CONFIG,
            Permissions.MANAGE_DATABASE_CONFIG,
            Permissions.VIEW_DATA_EXPORT,
            Permissions.PERFORM_DATA_EXPORT
        };
    }

    /// <inheritdoc />
    public Dictionary<string, string> GetPermissionDescriptions()
    {
        return new Dictionary<string, string>
        {
            // 工作台
            { Permissions.VIEW_DASHBOARD, "查看工作台" },
            // 客户管理
            { Permissions.VIEW_CUSTOMERS, "查看客户" },
            { Permissions.CREATE_CUSTOMERS, "创建客户" },
            { Permissions.UPDATE_CUSTOMERS, "更新客户" },
            { Permissions.DELETE_CUSTOMERS, "删除客户" },
            // 设备管理
            { Permissions.VIEW_DEVICES, "查看设备" },
            { Permissions.CREATE_DEVICES, "创建设备" },
            { Permissions.UPDATE_DEVICES, "更新设备" },
            { Permissions.DELETE_DEVICES, "删除设备" },
            // 项目管理
            { Permissions.VIEW_PROJECTS, "查看项目" },
            { Permissions.MANAGE_PROJECTS, "管理项目" },
            // 区域管理
            { Permissions.VIEW_AREAS, "查看区域" },
            // 告警中心
            { Permissions.VIEW_ALERT_CENTER, "查看告警中心" },
            { Permissions.CREATE_ALERTS, "创建告警" },
            { Permissions.UPDATE_ALERTS, "更新告警" },
            { Permissions.DELETE_ALERTS, "删除告警" },
            // 实时监控
            { Permissions.VIEW_MONITORING, "查看实时监控" },
            // 智能分析
            { Permissions.VIEW_ANALYTICS, "查看智能分析" },
            // 空气质量
            { Permissions.VIEW_AIR_QUALITY, "查看空气质量" },
            // 环境监测
            { Permissions.VIEW_ENVIRONMENT_MONITORING, "查看环境监测" },
            // 档案管理
            { Permissions.VIEW_ARCHIVES, "查看档案" },
            { Permissions.CREATE_ARCHIVES, "创建档案" },
            { Permissions.UPDATE_ARCHIVES, "更新档案" },
            { Permissions.DELETE_ARCHIVES, "删除档案" },
            // 工单管理
            { Permissions.VIEW_WORK_ORDERS, "查看工单" },
            { Permissions.CREATE_WORK_ORDERS, "创建工单" },
            { Permissions.UPDATE_WORK_ORDERS, "更新工单" },
            { Permissions.DELETE_WORK_ORDERS, "删除工单" },
            // 日志管理
            { Permissions.VIEW_LOGS, "查看日志" },
            // 用户管理
            { Permissions.VIEW_USERS, "查看用户" },
            { Permissions.CREATE_USERS, "创建用户" },
            { Permissions.UPDATE_USERS, "更新用户" },
            { Permissions.DELETE_USERS, "删除用户" },
            // 角色管理
            { Permissions.VIEW_ROLES, "查看角色" },
            { Permissions.CREATE_ROLES, "创建角色" },
            { Permissions.UPDATE_ROLES, "更新角色" },
            { Permissions.DELETE_ROLES, "删除角色" },
            // 系统设置
            { Permissions.VIEW_SETTINGS, "查看设置" },
            { Permissions.UPDATE_SETTINGS, "更新设置" },
            // API配置
            { Permissions.VIEW_API_CONFIG, "查看API配置" },
            { Permissions.UPDATE_API_CONFIG, "更新API配置" },
            // 字典管理
            { Permissions.VIEW_DICTIONARY, "查看字典" },
            { Permissions.CREATE_DICTIONARY, "创建字典" },
            { Permissions.UPDATE_DICTIONARY, "更新字典" },
            { Permissions.DELETE_DICTIONARY, "删除字典" },
            // 数据采集
            { Permissions.VIEW_DATA_COLLECTION, "查看数据采集" },
            { Permissions.MANAGE_PROTOCOLS, "管理协议" },
            { Permissions.MANAGE_RULES, "管理规则" },
            { Permissions.EXPORT_DATA, "导出数据" },
            // 协议与接入管理
            { Permissions.VIEW_PROTOCOL_CONFIG, "查看协议配置" },
            { Permissions.MANAGE_PROTOCOL_CONFIG, "管理协议配置" },
            { Permissions.VIEW_PROTOCOL_GATEWAY, "查看协议网关" },
            { Permissions.MANAGE_PROTOCOL_GATEWAY, "管理协议网关" },
            { Permissions.VIEW_NETWORK_TUNNEL, "查看网络隧道" },
            { Permissions.MANAGE_NETWORK_TUNNEL, "管理网络隧道" },
            { Permissions.VIEW_PLUGIN_SYSTEM, "查看插件系统" },
            { Permissions.MANAGE_PLUGIN_SYSTEM, "管理插件系统" },
            // 数据处理
            { Permissions.VIEW_DATA_CENTER, "查看数据中心" },
            { Permissions.MANAGE_DATA_CENTER, "管理数据中心" },
            { Permissions.VIEW_RULE_ENGINE, "查看规则引擎" },
            { Permissions.MANAGE_RULE_ENGINE, "管理规则引擎" },
            { Permissions.VIEW_DATA_TRANSFORM, "查看数据转换" },
            { Permissions.MANAGE_DATA_TRANSFORM, "管理数据转换" },
            { Permissions.VIEW_DATABASE_CONFIG, "查看数据库配置" },
            { Permissions.MANAGE_DATABASE_CONFIG, "管理数据库配置" },
            { Permissions.VIEW_DATA_EXPORT, "查看数据导出" },
            { Permissions.PERFORM_DATA_EXPORT, "执行数据导出" }
        };
    }

    /// <inheritdoc />
    public bool HasPermission(string roleCode, string permissionCode)
    {
        var permissions = GetRolePermissions(roleCode);
        return permissions.Contains(permissionCode);
    }
}
