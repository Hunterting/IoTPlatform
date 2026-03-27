namespace IoTPlatform.Configuration;

/// <summary>
/// 权限常量定义
/// </summary>
public static class Permissions
{
    // 工作台
    public const string VIEW_DASHBOARD = "view_dashboard";

    // 客户管理
    public const string VIEW_CUSTOMERS = "view_customers";
    public const string CREATE_CUSTOMERS = "create_customers";
    public const string UPDATE_CUSTOMERS = "update_customers";
    public const string DELETE_CUSTOMERS = "delete_customers";

    // 设备管理
    public const string VIEW_DEVICES = "view_devices";
    public const string CREATE_DEVICES = "create_devices";
    public const string UPDATE_DEVICES = "update_devices";
    public const string DELETE_DEVICES = "delete_devices";

    // 区域管理
    public const string VIEW_AREAS = "view_areas";

    // 告警中心
    public const string VIEW_ALERT_CENTER = "view_alert_center";
    public const string CREATE_ALERTS = "create_alerts";
    public const string UPDATE_ALERTS = "update_alerts";
    public const string DELETE_ALERTS = "delete_alerts";

    // 实时监控
    public const string VIEW_MONITORING = "view_monitoring";

    // 智能分析
    public const string VIEW_ANALYTICS = "view_analytics";

    // 空气质量
    public const string VIEW_AIR_QUALITY = "view_air_quality";

    // 环境监测
    public const string VIEW_ENVIRONMENT_MONITORING = "view_environment_monitoring";

    // 档案管理
    public const string VIEW_ARCHIVES = "view_archives";
    public const string CREATE_ARCHIVES = "create_archives";
    public const string UPDATE_ARCHIVES = "update_archives";
    public const string DELETE_ARCHIVES = "delete_archives";

    // 工单管理
    public const string VIEW_WORK_ORDERS = "view_work_orders";
    public const string CREATE_WORK_ORDERS = "create_work_orders";
    public const string UPDATE_WORK_ORDERS = "update_work_orders";
    public const string DELETE_WORK_ORDERS = "delete_work_orders";

    // 日志管理
    public const string VIEW_LOGS = "view_logs";

    // 用户管理
    public const string VIEW_USERS = "view_users";
    public const string CREATE_USERS = "create_users";
    public const string UPDATE_USERS = "update_users";
    public const string DELETE_USERS = "delete_users";

    // 角色管理
    public const string VIEW_ROLES = "view_roles";
    public const string CREATE_ROLES = "create_roles";
    public const string UPDATE_ROLES = "update_roles";
    public const string DELETE_ROLES = "delete_roles";

    // 系统设置
    public const string VIEW_SETTINGS = "view_settings";
    public const string UPDATE_SETTINGS = "update_settings";

    // API配置（仅超级管理员）
    public const string VIEW_API_CONFIG = "view_api_config";
    public const string UPDATE_API_CONFIG = "update_api_config";

    // 字典管理
    public const string VIEW_DICTIONARY = "view_dictionary";
    public const string CREATE_DICTIONARY = "create_dictionary";
    public const string UPDATE_DICTIONARY = "update_dictionary";
    public const string DELETE_DICTIONARY = "delete_dictionary";

    // 数据采集
    public const string VIEW_DATA_COLLECTION = "view_data_collection";
    public const string MANAGE_PROTOCOLS = "manage_protocols";
    public const string MANAGE_RULES = "manage_rules";
    public const string EXPORT_DATA = "export_data";

    // 协议与接入管理
    public const string VIEW_PROTOCOL_CONFIG = "view_protocol_config";
    public const string MANAGE_PROTOCOL_CONFIG = "manage_protocol_config";
    public const string VIEW_PROTOCOL_GATEWAY = "view_protocol_gateway";
    public const string MANAGE_PROTOCOL_GATEWAY = "manage_protocol_gateway";
    public const string VIEW_NETWORK_TUNNEL = "view_network_tunnel";
    public const string MANAGE_NETWORK_TUNNEL = "manage_network_tunnel";
    public const string VIEW_PLUGIN_SYSTEM = "view_plugin_system";
    public const string MANAGE_PLUGIN_SYSTEM = "manage_plugin_system";

    // 数据处理
    public const string VIEW_DATA_CENTER = "view_data_center";
    public const string MANAGE_DATA_CENTER = "manage_data_center";
    public const string VIEW_RULE_ENGINE = "view_rule_engine";
    public const string MANAGE_RULE_ENGINE = "manage_rule_engine";
    public const string VIEW_DATA_TRANSFORM = "view_data_transform";
    public const string MANAGE_DATA_TRANSFORM = "manage_data_transform";
    public const string VIEW_DATABASE_CONFIG = "view_database_config";
    public const string MANAGE_DATABASE_CONFIG = "manage_database_config";
    public const string VIEW_DATA_EXPORT = "view_data_export";
    public const string PERFORM_DATA_EXPORT = "perform_data_export";
}
