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
    public const string CUSTOMER_ADMIN = "customer_admin";
    public const string USER = "user";

    /// <summary>
    /// 判断给定角色是否为超级管理员（统一超管判定信号）。
    /// 与 PermissionFilter / GetEffectivePermissionsAsync 共用，避免两处逻辑漂移。
    /// </summary>
    public static bool IsSuperAdmin(string? role)
    {
        return role == SUPER_ADMIN;
    }

    /// <summary>
    /// 获取角色所有权限
    /// </summary>
    public static List<string> GetRolePermissions(string roleCode)
    {
        return roleCode.ToLower() switch
        {
            SUPER_ADMIN => GetSuperAdminPermissions(),
            ADMIN => GetAdminPermissions(),
            CUSTOMER_ADMIN => GetCustomerAdminPermissions(),
            OPERATOR => GetOperatorPermissions(),
            CHEF => GetChefPermissions(),
            STAFF => GetStaffPermissions(),
            USER => GetUserPermissions(),
            _ => new List<string>()
        };
    }

    /// <summary>
    /// 超级管理员权限（全部权限）。
    /// 直接复用 Permissions.All 作为唯一事实来源，避免与 Permissions.All 手工副本漂移
    /// （历史上曾因此漏授 5 个权限：区域增改删、网关配置管理、隧道配置管理）。
    /// </summary>
    public static List<string> GetSuperAdminPermissions()
    {
        return Permissions.All;
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
        // 租户设备配额需求 R01：仅超级管理员可创建客户/租户。
        // customer_admin 复用本方法，故一并收权；Permissions.All 为形如 new List 的属性，
        // 每次调用返回新实例，Remove 不会污染超管权限（常量本身亦保留）。
        permissions.Remove(Permissions.CREATE_CUSTOMERS);
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
            Permissions.UPDATE_WORK_ORDERS,
            // 运维主管有指令查看和下发权限
            Permissions.VIEW_DEVICE_COMMANDS,
            Permissions.SEND_DEVICE_COMMANDS,
            Permissions.CANCEL_DEVICE_COMMANDS,
            // 日志管理（登录/操作日志）按 AppCode 区分租户，补查看权限
            Permissions.VIEW_LOGS
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
            // 告警中心按 AppCode 区分租户；厨师长负责厨房设备监控，需能查看本租户告警。
            Permissions.VIEW_ALERT_CENTER,
            Permissions.VIEW_WORK_ORDERS,
            Permissions.UPDATE_WORK_ORDERS,
            // 协议管理按 AppCode 区分租户；补完整管理权限让第二租户能管理本租户协议。
            Permissions.MANAGE_PROTOCOLS,
            Permissions.VIEW_PROTOCOL_CONFIG,
            Permissions.MANAGE_PROTOCOL_CONFIG,
            Permissions.VIEW_PROTOCOL_GATEWAY,
            Permissions.MANAGE_PROTOCOL_GATEWAY,
            // 字典管理按租户区分记录；补完整权限让第二租户能维护本租户字典。
            Permissions.VIEW_DICTIONARY,
            Permissions.CREATE_DICTIONARY,
            Permissions.UPDATE_DICTIONARY,
            Permissions.DELETE_DICTIONARY,
            // 日志管理（登录/操作日志）按 AppCode 区分租户，补查看权限
            Permissions.VIEW_LOGS
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
            Permissions.VIEW_MONITORING,
            // 告警/工单按 AppCode 区分租户，补只读查看权限，让第二租户（chef 等 staff 角色）能看到本租户数据。
            Permissions.VIEW_ALERT_CENTER,
            Permissions.VIEW_WORK_ORDERS,
            // 协议管理按 AppCode 区分租户；补完整管理权限让第二租户能管理本租户协议。
            Permissions.MANAGE_PROTOCOLS,
            Permissions.VIEW_PROTOCOL_CONFIG,
            Permissions.MANAGE_PROTOCOL_CONFIG,
            Permissions.VIEW_PROTOCOL_GATEWAY,
            Permissions.MANAGE_PROTOCOL_GATEWAY,
            // 字典管理按租户区分记录；补完整权限让第二租户能维护本租户字典。
            Permissions.VIEW_DICTIONARY,
            Permissions.CREATE_DICTIONARY,
            Permissions.UPDATE_DICTIONARY,
            Permissions.DELETE_DICTIONARY,
            // 日志管理（登录/操作日志）按 AppCode 区分租户，补查看权限
            Permissions.VIEW_LOGS
        };
    }

    /// <summary>
    /// 客户管理员权限（与租户管理员 admin 同级，含日志查看）
    /// </summary>
    public static List<string> GetCustomerAdminPermissions()
    {
        // 客户管理员职责等同租户管理员，复用 admin 权限集（已含 VIEW_LOGS）。
        return GetAdminPermissions();
    }

    /// <summary>
    /// 普通用户权限（基础查看 + 日志查看）
    /// </summary>
    public static List<string> GetUserPermissions()
    {
        return new List<string>
        {
            Permissions.VIEW_DASHBOARD,
            Permissions.VIEW_MONITORING,
            Permissions.VIEW_ALERT_CENTER,
            Permissions.VIEW_WORK_ORDERS,
            // 日志管理（登录/操作日志）按 AppCode 区分租户，补查看权限
            Permissions.VIEW_LOGS
        };
    }
}
