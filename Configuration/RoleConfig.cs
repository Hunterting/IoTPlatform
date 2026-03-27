namespace IoTPlatform.Configuration;

/// <summary>
/// 角色常量定义
/// </summary>
public static class Roles
{
    public const string SUPER_ADMIN = "super_admin";
    public const string ADMIN = "admin";
    public const string OPERATOR = "operator";
    public const string CHEF = "chef";
    public const string STAFF = "staff";

    /// <summary>
    /// 获取角色所有权限
    /// </summary>
    public static List<string> GetRolePermissions(string roleCode)
    {
        return roleCode.ToLower() switch
        {
            SUPER_ADMIN => GetSuperAdminPermissions(),
            ADMIN => GetAdminPermissions(),
            OPERATOR => GetOperatorPermissions(),
            CHEF => GetChefPermissions(),
            STAFF => GetStaffPermissions(),
            _ => new List<string>()
        };
    }

    /// <summary>
    /// 超级管理员权限（全部权限）
    /// </summary>
    public static List<string> GetSuperAdminPermissions()
    {
        return new List<string>
        {
            // 工作台
            Permissions.VIEW_DASHBOARD,
            // 客户
            Permissions.VIEW_CUSTOMERS,
            Permissions.CREATE_CUSTOMERS,
            Permissions.UPDATE_CUSTOMERS,
            Permissions.DELETE_CUSTOMERS,
            // 设备
            Permissions.VIEW_DEVICES,
            Permissions.CREATE_DEVICES,
            Permissions.UPDATE_DEVICES,
            Permissions.DELETE_DEVICES,
            // 区域
            Permissions.VIEW_AREAS,
            // 告警
            Permissions.VIEW_ALERT_CENTER,
            Permissions.CREATE_ALERTS,
            Permissions.UPDATE_ALERTS,
            Permissions.DELETE_ALERTS,
            // 监控 & 分析
            Permissions.VIEW_MONITORING,
            Permissions.VIEW_ANALYTICS,
            // 空气质量
            Permissions.VIEW_AIR_QUALITY,
            // 环境监测
            Permissions.VIEW_ENVIRONMENT_MONITORING,
            // 档案
            Permissions.VIEW_ARCHIVES,
            Permissions.CREATE_ARCHIVES,
            Permissions.UPDATE_ARCHIVES,
            Permissions.DELETE_ARCHIVES,
            // 工单
            Permissions.VIEW_WORK_ORDERS,
            Permissions.CREATE_WORK_ORDERS,
            Permissions.UPDATE_WORK_ORDERS,
            Permissions.DELETE_WORK_ORDERS,
            // 日志
            Permissions.VIEW_LOGS,
            // 用户
            Permissions.VIEW_USERS,
            Permissions.CREATE_USERS,
            Permissions.UPDATE_USERS,
            Permissions.DELETE_USERS,
            // 角色
            Permissions.VIEW_ROLES,
            Permissions.CREATE_ROLES,
            Permissions.UPDATE_ROLES,
            Permissions.DELETE_ROLES,
            // 设置
            Permissions.VIEW_SETTINGS,
            Permissions.UPDATE_SETTINGS,
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

    /// <summary>
    /// 租户管理员权限
    /// </summary>
    public static List<string> GetAdminPermissions()
    {
        var permissions = GetSuperAdminPermissions();
        // 移除API配置权限
        permissions.Remove(Permissions.VIEW_API_CONFIG);
        permissions.Remove(Permissions.UPDATE_API_CONFIG);
        return permissions;
    }

    /// <summary>
    /// 运维主管权限
    /// </summary>
    public static List<string> GetOperatorPermissions()
    {
        return new List<string>
        {
            Permissions.VIEW_DASHBOARD,
            Permissions.VIEW_DEVICES,
            Permissions.UPDATE_DEVICES,
            Permissions.VIEW_AREAS,
            Permissions.VIEW_ALERT_CENTER,
            Permissions.UPDATE_ALERTS,
            Permissions.VIEW_MONITORING,
            Permissions.VIEW_ANALYTICS,
            Permissions.VIEW_AIR_QUALITY,
            Permissions.VIEW_ENVIRONMENT_MONITORING,
            Permissions.VIEW_ARCHIVES,
            Permissions.VIEW_WORK_ORDERS,
            Permissions.CREATE_WORK_ORDERS,
            Permissions.UPDATE_WORK_ORDERS
        };
    }

    /// <summary>
    /// 厨师长权限
    /// </summary>
    public static List<string> GetChefPermissions()
    {
        return new List<string>
        {
            Permissions.VIEW_DASHBOARD,
            Permissions.VIEW_DEVICES,
            Permissions.VIEW_MONITORING,
            Permissions.VIEW_WORK_ORDERS,
            Permissions.UPDATE_WORK_ORDERS
        };
    }

    /// <summary>
    /// 普通员工权限
    /// </summary>
    public static List<string> GetStaffPermissions()
    {
        return new List<string>
        {
            Permissions.VIEW_DASHBOARD,
            Permissions.VIEW_MONITORING
        };
    }
}
