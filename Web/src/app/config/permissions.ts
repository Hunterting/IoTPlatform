export const PERMISSIONS = {
  // ── 工作台 ────────────────────────────────────────────────
  VIEW_DASHBOARD: 'view_dashboard',

  // ── 客户管理 ──────────────────────────────────────────────
  VIEW_CUSTOMERS: 'view_customers',
  CREATE_CUSTOMERS: 'create_customers',
  UPDATE_CUSTOMERS: 'update_customers',
  DELETE_CUSTOMERS: 'delete_customers',

  // ── 设备管理 ──────────────────────────────────────────────
  VIEW_DEVICES: 'view_devices',
  CREATE_DEVICES: 'create_devices',
  UPDATE_DEVICES: 'update_devices',
  DELETE_DEVICES: 'delete_devices',

  // ── 区域管理 ──────────────────────────────────────────────
  VIEW_AREAS: 'view_areas',

  // ── 告警中心 ──────────────────────────────────────────────
  VIEW_ALERT_CENTER: 'view_alert_center',
  CREATE_ALERTS: 'create_alerts',
  UPDATE_ALERTS: 'update_alerts',
  DELETE_ALERTS: 'delete_alerts',

  // ─ 实时监控 ──────────────────────────────────────────────
  VIEW_MONITORING: 'view_monitoring',

  // ── 智能分析 ──────────────────────────────────────────────
  VIEW_ANALYTICS: 'view_analytics',

  // ── 空气质量 ──────────────────────────────────────────────
  VIEW_AIR_QUALITY: 'view_air_quality',

  // ── 环境监测 ──────────────────────────────────────────────
  VIEW_ENVIRONMENT_MONITORING: 'view_environment_monitoring',

  // ── 档案管理 ──────────────────────────────────────────────
  VIEW_ARCHIVES: 'view_archives',
  CREATE_ARCHIVES: 'create_archives',
  UPDATE_ARCHIVES: 'update_archives',
  DELETE_ARCHIVES: 'delete_archives',

  // ── 工单管理 ──────────────────────────────────────────────
  VIEW_WORK_ORDERS: 'view_work_orders',
  CREATE_WORK_ORDERS: 'create_work_orders',
  UPDATE_WORK_ORDERS: 'update_work_orders',
  DELETE_WORK_ORDERS: 'delete_work_orders',

  // ── 日志管理 ──────────────────────────────────────────────
  VIEW_LOGS: 'view_logs',

  // ── 用户管理 ──────────────────────────────────────────────
  VIEW_USERS: 'view_users',
  CREATE_USERS: 'create_users',
  UPDATE_USERS: 'update_users',
  DELETE_USERS: 'delete_users',

  // ── 角色管理 ──────────────────────────────────────────────
  VIEW_ROLES: 'view_roles',
  CREATE_ROLES: 'create_roles',
  UPDATE_ROLES: 'update_roles',
  DELETE_ROLES: 'delete_roles',

  // ── 系统设置 ──────────────────────────────────────────────
  VIEW_SETTINGS: 'view_settings',
  UPDATE_SETTINGS: 'update_settings',

  // ── API 配置（仅超级管理员） ───────────────────────────────
  VIEW_API_CONFIG: 'view_api_config',
  UPDATE_API_CONFIG: 'update_api_config',

  // ── 字典管理 ──────────────────────────────────────────────
  VIEW_DICTIONARY: 'view_dictionary',
  CREATE_DICTIONARY: 'create_dictionary',
  UPDATE_DICTIONARY: 'update_dictionary',
  DELETE_DICTIONARY: 'delete_dictionary',

  // ── 数据采集 ──────────────────────────────────────────────
  VIEW_DATA_COLLECTION: 'view_data_collection',
  MANAGE_PROTOCOLS: 'manage_protocols',
  MANAGE_RULES: 'manage_rules',
  EXPORT_DATA: 'export_data',
  
  // ── 协议与接入管理 ────────────────────────────────────────
  VIEW_PROTOCOL_CONFIG: 'view_protocol_config',
  MANAGE_PROTOCOL_CONFIG: 'manage_protocol_config',
  VIEW_PROTOCOL_GATEWAY: 'view_protocol_gateway',
  MANAGE_PROTOCOL_GATEWAY: 'manage_protocol_gateway',
  VIEW_NETWORK_TUNNEL: 'view_network_tunnel',
  MANAGE_NETWORK_TUNNEL: 'manage_network_tunnel',
  VIEW_PLUGIN_SYSTEM: 'view_plugin_system',
  MANAGE_PLUGIN_SYSTEM: 'manage_plugin_system',
  
  // ── 数据处理 ──────────────────────────────────────────────
  VIEW_DATA_CENTER: 'view_data_center',
  MANAGE_DATA_CENTER: 'manage_data_center',
  VIEW_RULE_ENGINE: 'view_rule_engine',
  MANAGE_RULE_ENGINE: 'manage_rule_engine',
  VIEW_DATA_TRANSFORM: 'view_data_transform',
  MANAGE_DATA_TRANSFORM: 'manage_data_transform',
  VIEW_DATABASE_CONFIG: 'view_database_config',
  MANAGE_DATABASE_CONFIG: 'manage_database_config',
  VIEW_DATA_EXPORT: 'view_data_export',
  PERFORM_DATA_EXPORT: 'perform_data_export',
} as const;

export type Permission = typeof PERMISSIONS[keyof typeof PERMISSIONS];

export type DataScope = 'ALL' | 'CUSTOM';

export interface RoleDefinition {
  code: string;
  name: string;
  description: string;
  permissions: Permission[];
  dataScope: DataScope;
}

// ── 权限分组（用于角色编辑矩阵） ─────────────────────────────
export const PERMISSION_GROUPS = [
  {
    name: '工作台',
    permissions: [
      { code: PERMISSIONS.VIEW_DASHBOARD, name: '查看工作台' },
    ],
  },
  {
    name: '客户管理',
    permissions: [
      { code: PERMISSIONS.VIEW_CUSTOMERS, name: '查看列表' },
      { code: PERMISSIONS.CREATE_CUSTOMERS, name: '新增客户' },
      { code: PERMISSIONS.UPDATE_CUSTOMERS, name: '编辑客户' },
      { code: PERMISSIONS.DELETE_CUSTOMERS, name: '删除客户' },
    ],
  },
  {
    name: '设备管理',
    permissions: [
      { code: PERMISSIONS.VIEW_DEVICES, name: '查看列表' },
      { code: PERMISSIONS.CREATE_DEVICES, name: '新增设备' },
      { code: PERMISSIONS.UPDATE_DEVICES, name: '编辑设备' },
      { code: PERMISSIONS.DELETE_DEVICES, name: '删除设备' },
    ],
  },
  {
    name: '区域管理',
    permissions: [
      { code: PERMISSIONS.VIEW_AREAS, name: '查看区域' },
    ],
  },
  {
    name: '告警中心',
    permissions: [
      { code: PERMISSIONS.VIEW_ALERT_CENTER, name: '查看告警' },
      { code: PERMISSIONS.CREATE_ALERTS, name: '新增告警' },
      { code: PERMISSIONS.UPDATE_ALERTS, name: '处理告警' },
      { code: PERMISSIONS.DELETE_ALERTS, name: '删除告警' },
    ],
  },
  {
    name: '实时监控',
    permissions: [
      { code: PERMISSIONS.VIEW_MONITORING, name: '查看监控' },
    ],
  },
  {
    name: '智能分析',
    permissions: [
      { code: PERMISSIONS.VIEW_ANALYTICS, name: '查看报表' },
    ],
  },
  {
    name: '空气质量',
    permissions: [
      { code: PERMISSIONS.VIEW_AIR_QUALITY, name: '查看监测' },
    ],
  },
  {
    name: '环境监测',
    permissions: [
      { code: PERMISSIONS.VIEW_ENVIRONMENT_MONITORING, name: '查看监测' },
    ],
  },
  {
    name: '档案管理',
    permissions: [
      { code: PERMISSIONS.VIEW_ARCHIVES, name: '查看档案' },
      { code: PERMISSIONS.CREATE_ARCHIVES, name: '上传档案' },
      { code: PERMISSIONS.UPDATE_ARCHIVES, name: '编辑档案' },
      { code: PERMISSIONS.DELETE_ARCHIVES, name: '删除档案' },
    ],
  },
  {
    name: '工单管理',
    permissions: [
      { code: PERMISSIONS.VIEW_WORK_ORDERS, name: '查看工单' },
      { code: PERMISSIONS.CREATE_WORK_ORDERS, name: '创建工单' },
      { code: PERMISSIONS.UPDATE_WORK_ORDERS, name: '处理工单' },
      { code: PERMISSIONS.DELETE_WORK_ORDERS, name: '删除工单' },
    ],
  },
  {
    name: '日志管理',
    permissions: [
      { code: PERMISSIONS.VIEW_LOGS, name: '查看日志' },
    ],
  },
  {
    name: '用户管理',
    permissions: [
      { code: PERMISSIONS.VIEW_USERS, name: '查看用户' },
      { code: PERMISSIONS.CREATE_USERS, name: '新增用户' },
      { code: PERMISSIONS.UPDATE_USERS, name: '编辑用户' },
      { code: PERMISSIONS.DELETE_USERS, name: '删除用户' },
    ],
  },
  {
    name: '角色管理',
    permissions: [
      { code: PERMISSIONS.VIEW_ROLES, name: '查看角色' },
      { code: PERMISSIONS.CREATE_ROLES, name: '新增角色' },
      { code: PERMISSIONS.UPDATE_ROLES, name: '编辑角色' },
      { code: PERMISSIONS.DELETE_ROLES, name: '删除角色' },
    ],
  },
  {
    name: '系统设置',
    permissions: [
      { code: PERMISSIONS.VIEW_SETTINGS, name: '查看设置' },
      { code: PERMISSIONS.UPDATE_SETTINGS, name: '修改设置' },
    ],
  },
  {
    name: 'API 配置',
    permissions: [
      { code: PERMISSIONS.VIEW_API_CONFIG, name: '查看 API 配置' },
      { code: PERMISSIONS.UPDATE_API_CONFIG, name: '修改 API 配置' },
    ],
  },
  {
    name: '字典管理',
    permissions: [
      { code: PERMISSIONS.VIEW_DICTIONARY, name: '查看字典' },
      { code: PERMISSIONS.CREATE_DICTIONARY, name: '新增字典' },
      { code: PERMISSIONS.UPDATE_DICTIONARY, name: '编辑字典' },
      { code: PERMISSIONS.DELETE_DICTIONARY, name: '删除字典' },
    ],
  },
  {
    name: '数据采集',
    permissions: [
      { code: PERMISSIONS.VIEW_DATA_COLLECTION, name: '查看数据采集' },
      { code: PERMISSIONS.MANAGE_PROTOCOLS, name: '管理协议' },
      { code: PERMISSIONS.MANAGE_RULES, name: '管理规则' },
      { code: PERMISSIONS.EXPORT_DATA, name: '导出数据' },
    ],
  },
  {
    name: '协议与接入管理',
    permissions: [
      { code: PERMISSIONS.VIEW_PROTOCOL_CONFIG, name: '查看协议配置' },
      { code: PERMISSIONS.MANAGE_PROTOCOL_CONFIG, name: '管理协议配置' },
      { code: PERMISSIONS.VIEW_PROTOCOL_GATEWAY, name: '查看协议网关' },
      { code: PERMISSIONS.MANAGE_PROTOCOL_GATEWAY, name: '管理协议网关' },
      { code: PERMISSIONS.VIEW_NETWORK_TUNNEL, name: '查看网络隧道' },
      { code: PERMISSIONS.MANAGE_NETWORK_TUNNEL, name: '管理网络隧道' },
      { code: PERMISSIONS.VIEW_PLUGIN_SYSTEM, name: '查看插件系统' },
      { code: PERMISSIONS.MANAGE_PLUGIN_SYSTEM, name: '管理插件系统' },
    ],
  },
  {
    name: '数据处理',
    permissions: [
      { code: PERMISSIONS.VIEW_DATA_CENTER, name: '查看数据中心' },
      { code: PERMISSIONS.MANAGE_DATA_CENTER, name: '管理数据中心' },
      { code: PERMISSIONS.VIEW_RULE_ENGINE, name: '查看规则引擎' },
      { code: PERMISSIONS.MANAGE_RULE_ENGINE, name: '管理规则引擎' },
      { code: PERMISSIONS.VIEW_DATA_TRANSFORM, name: '查看数据转换' },
      { code: PERMISSIONS.MANAGE_DATA_TRANSFORM, name: '管理数据转换' },
      { code: PERMISSIONS.VIEW_DATABASE_CONFIG, name: '查看数据库配置' },
      { code: PERMISSIONS.MANAGE_DATABASE_CONFIG, name: '管理数据库配置' },
      { code: PERMISSIONS.VIEW_DATA_EXPORT, name: '查看数据导出' },
      { code: PERMISSIONS.PERFORM_DATA_EXPORT, name: '执行数据导出' },
    ],
  },
];

// ── 默认角色定义 ──────────────────────────────────────────────
// 规则：
//   super_admin → Object.values(PERMISSIONS) 动态获取，新增权限自动继承
//   其他角色    → 显式列，需手动授权才可访问新模块
export const DEFAULT_ROLES: Record<string, RoleDefinition> = {
  super_admin: {
    code: 'super_admin',
    name: '超级管理员',
    description: '拥有系统全部权限，新增模块自动授权',
    permissions: Object.values(PERMISSIONS), // 动态，始终包含所有权限
    dataScope: 'ALL',
  },

  admin: {
    code: 'admin',
    name: '租户管理员',
    description: '管理租户内所有事务，不含系统级 API 配置',
    permissions: [
      PERMISSIONS.VIEW_DASHBOARD,
      // 客户
      PERMISSIONS.VIEW_CUSTOMERS,
      PERMISSIONS.CREATE_CUSTOMERS,
      PERMISSIONS.UPDATE_CUSTOMERS,
      PERMISSIONS.DELETE_CUSTOMERS,
      // 设备
      PERMISSIONS.VIEW_DEVICES,
      PERMISSIONS.CREATE_DEVICES,
      PERMISSIONS.UPDATE_DEVICES,
      PERMISSIONS.DELETE_DEVICES,
      // 区域
      PERMISSIONS.VIEW_AREAS,
      // 告警
      PERMISSIONS.VIEW_ALERT_CENTER,
      PERMISSIONS.CREATE_ALERTS,
      PERMISSIONS.UPDATE_ALERTS,
      PERMISSIONS.DELETE_ALERTS,
      // 监控 & 分析
      PERMISSIONS.VIEW_MONITORING,
      PERMISSIONS.VIEW_ANALYTICS,
      // 空气质量
      PERMISSIONS.VIEW_AIR_QUALITY,
      // 环境监测
      PERMISSIONS.VIEW_ENVIRONMENT_MONITORING,
      // 档案
      PERMISSIONS.VIEW_ARCHIVES,
      PERMISSIONS.CREATE_ARCHIVES,
      PERMISSIONS.UPDATE_ARCHIVES,
      PERMISSIONS.DELETE_ARCHIVES,
      // 工单
      PERMISSIONS.VIEW_WORK_ORDERS,
      PERMISSIONS.CREATE_WORK_ORDERS,
      PERMISSIONS.UPDATE_WORK_ORDERS,
      PERMISSIONS.DELETE_WORK_ORDERS,
      // 日志
      PERMISSIONS.VIEW_LOGS,
      // 用户
      PERMISSIONS.VIEW_USERS,
      PERMISSIONS.CREATE_USERS,
      PERMISSIONS.UPDATE_USERS,
      PERMISSIONS.DELETE_USERS,
      // 角
      PERMISSIONS.VIEW_ROLES,
      PERMISSIONS.CREATE_ROLES,
      PERMISSIONS.UPDATE_ROLES,
      PERMISSIONS.DELETE_ROLES,
      // 设置（不含 API 配置，仅 super_admin 通过菜单访问）
      PERMISSIONS.VIEW_SETTINGS,
      PERMISSIONS.UPDATE_SETTINGS,
      // 字典管理
      PERMISSIONS.VIEW_DICTIONARY,
      PERMISSIONS.CREATE_DICTIONARY,
      PERMISSIONS.UPDATE_DICTIONARY,
      PERMISSIONS.DELETE_DICTIONARY,
      // 数据采集
      PERMISSIONS.VIEW_DATA_COLLECTION,
      PERMISSIONS.MANAGE_PROTOCOLS,
      PERMISSIONS.MANAGE_RULES,
      PERMISSIONS.EXPORT_DATA,
      // 协议与接入管理
      PERMISSIONS.VIEW_PROTOCOL_CONFIG,
      PERMISSIONS.MANAGE_PROTOCOL_CONFIG,
      PERMISSIONS.VIEW_PROTOCOL_GATEWAY,
      PERMISSIONS.MANAGE_PROTOCOL_GATEWAY,
      PERMISSIONS.VIEW_NETWORK_TUNNEL,
      PERMISSIONS.MANAGE_NETWORK_TUNNEL,
      PERMISSIONS.VIEW_PLUGIN_SYSTEM,
      PERMISSIONS.MANAGE_PLUGIN_SYSTEM,
      // 数据处理
      PERMISSIONS.VIEW_DATA_CENTER,
      PERMISSIONS.MANAGE_DATA_CENTER,
      PERMISSIONS.VIEW_RULE_ENGINE,
      PERMISSIONS.MANAGE_RULE_ENGINE,
      PERMISSIONS.VIEW_DATA_TRANSFORM,
      PERMISSIONS.MANAGE_DATA_TRANSFORM,
      PERMISSIONS.VIEW_DATABASE_CONFIG,
      PERMISSIONS.MANAGE_DATABASE_CONFIG,
      PERMISSIONS.VIEW_DATA_EXPORT,
      PERMISSIONS.PERFORM_DATA_EXPORT,
    ],
    dataScope: 'ALL',
  },

  operator: {
    code: 'operator',
    name: '运维主管',
    description: '负责设备监控与运维，可查看所辖区域',
    permissions: [
      PERMISSIONS.VIEW_DASHBOARD,
      // 设备（可编辑状态）
      PERMISSIONS.VIEW_DEVICES,
      PERMISSIONS.UPDATE_DEVICES,
      // 区域（只读）
      PERMISSIONS.VIEW_AREAS,
      // 告警（可处理）
      PERMISSIONS.VIEW_ALERT_CENTER,
      PERMISSIONS.UPDATE_ALERTS,
      // 监控 & 分析
      PERMISSIONS.VIEW_MONITORING,
      PERMISSIONS.VIEW_ANALYTICS,
      // 空气质量
      PERMISSIONS.VIEW_AIR_QUALITY,
      // 环境监测
      PERMISSIONS.VIEW_ENVIRONMENT_MONITORING,
      // 档案（只读）
      PERMISSIONS.VIEW_ARCHIVES,
      // 工单（可处理）
      PERMISSIONS.VIEW_WORK_ORDERS,
      PERMISSIONS.CREATE_WORK_ORDERS,
      PERMISSIONS.UPDATE_WORK_ORDERS,
    ],
    dataScope: 'CUSTOM',
  },

  chef: {
    code: 'chef',
    name: '厨师长',
    description: '仅查看厨房相关设备与工单',
    permissions: [
      PERMISSIONS.VIEW_DASHBOARD,
      PERMISSIONS.VIEW_DEVICES,
      PERMISSIONS.VIEW_MONITORING,
      PERMISSIONS.VIEW_WORK_ORDERS,
      PERMISSIONS.UPDATE_WORK_ORDERS,
    ],
    dataScope: 'CUSTOM',
  },

  staff: {
    code: 'staff',
    name: '普通员工',
    description: '基础查看权限',
    permissions: [
      PERMISSIONS.VIEW_DASHBOARD,
      PERMISSIONS.VIEW_MONITORING,
    ],
    dataScope: 'CUSTOM',
  },
};