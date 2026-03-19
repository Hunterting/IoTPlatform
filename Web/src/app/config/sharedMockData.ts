// ── Shared mock data used across Dashboard, LogManagement, and WorkOrder pages ──

export type WorkOrderStatus = 'pending' | 'assigned' | 'in_progress' | 'resolved' | 'closed' | 'rejected';
export type WorkOrderPriority = 'low' | 'normal' | 'high' | 'urgent';
export type WorkOrderType = 'fault' | 'maintenance' | 'inspection' | 'installation' | 'other';
export type AlertLevel = 'info' | 'warning' | 'critical';
export type AlertStatus = 'pending' | 'processing' | 'resolved' | 'ignored';
export type AlertType = 'temperature' | 'pressure' | 'offline' | 'power' | 'smoke' | 'door';

export interface WorkOrderLog {
  id: string;
  time: string;
  operator: string;
  action: string;
  comment?: string;
}

export interface WorkOrder {
  id: string;
  orderNo: string;
  title: string;
  type: WorkOrderType;
  priority: WorkOrderPriority;
  status: WorkOrderStatus;
  customer: string;
  project: string;
  deviceName: string;
  deviceCode: string;
  area: string;
  description: string;
  reporter: string;
  reportTime: string;
  assignee?: string;
  assigneeAvatar?: string;
  estimatedTime?: string;
  resolvedTime?: string;
  logs: WorkOrderLog[];
}

export interface AlertRecord {
  id: string;
  alertNo: string;
  deviceName: string;
  deviceCode: string;
  area: string;
  alertType: AlertType;
  level: AlertLevel;
  value: string;
  threshold: string;
  alertTime: string;
  status: AlertStatus;
  assignee?: string;
  resolveTime?: string;
  remark?: string;
}

// ── Work Orders ───────────────────────────────────────────────────────────────
export const SHARED_WORK_ORDERS: WorkOrder[] = [
  {
    id: 'wo1',
    orderNo: 'WO20260301001',
    title: '中央厨房冷藏柜温度异常告警',
    type: 'fault',
    priority: 'urgent',
    status: 'in_progress',
    customer: '海底捞火锅连锁',
    project: '北京朝阳店智能化项目',
    deviceName: '冷藏柜 SR-F520BX',
    deviceCode: 'DEV-C-001',
    area: '中央厨房',
    description: '冷藏柜温度传感器显示温度持续高于设定值 3°C，已超阈值 2 小时，存在食材安全风险，需立即处理。',
    reporter: '张经理',
    reportTime: '2026-03-01 08:23',
    assignee: '张工',
    estimatedTime: '2026-03-01 12:00',
    logs: [
      { id: 'l1', time: '2026-03-01 08:23', operator: '张经理', action: '创建工单', comment: '冷藏柜温度异常，立即处理' },
      { id: 'l2', time: '2026-03-01 08:35', operator: '系统管理员', action: '分配工单', comment: '分配给张工处理' },
      { id: 'l3', time: '2026-03-01 09:10', operator: '张工', action: '开始处理', comment: '已到现场检查，发现压缩机散热异常' },
    ],
  },
  {
    id: 'wo2',
    orderNo: 'WO20260301002',
    title: '排烟系统风机噪音过大',
    type: 'maintenance',
    priority: 'high',
    status: 'assigned',
    customer: '海底捞火锅连锁',
    project: '北京望京店智能化项目',
    deviceName: '变频排风机 FAN-V2000',
    deviceCode: 'DEV-F-003',
    area: '主厨房',
    description: '排烟风机运行时噪音明显增大，比正常噪音高约 15dB，初步判断轴承可能磨损，需检修。',
    reporter: '李厨师',
    reportTime: '2026-03-01 10:05',
    assignee: '李工',
    estimatedTime: '2026-03-02 18:00',
    logs: [
      { id: 'l4', time: '2026-03-01 10:05', operator: '李厨师', action: '创建工单' },
      { id: 'l5', time: '2026-03-01 10:30', operator: '王管理员', action: '分配工单', comment: '分配给李工，明日上午处理' },
    ],
  },
  {
    id: 'wo3',
    orderNo: 'WO20260228001',
    title: '智能烤箱温控模块季度巡检',
    type: 'inspection',
    priority: 'normal',
    status: 'resolved',
    customer: '喜茶门店管理',
    project: '上海旗舰店智能化项目',
    deviceName: '智能烤箱 OV-X8000',
    deviceCode: 'DEV-O-002',
    area: '烘焙区',
    description: '季度例行巡检，检查烤箱温控系统、加热元件及传感器工作状态。',
    reporter: '李总监',
    reportTime: '2026-02-28 09:00',
    assignee: '赵工',
    estimatedTime: '2026-02-28 17:00',
    resolvedTime: '2026-02-28 15:30',
    logs: [
      { id: 'l6', time: '2026-02-28 09:00', operator: '李总监', action: '创建工单', comment: '季度巡检计划' },
      { id: 'l7', time: '2026-02-28 09:10', operator: '系统管理员', action: '分配工单' },
      { id: 'l8', time: '2026-02-28 10:00', operator: '赵工', action: '开始处理', comment: '开始巡检' },
      { id: 'l9', time: '2026-02-28 15:30', operator: '赵工', action: '解决工单', comment: '巡检完毕，所有指标正常' },
    ],
  },
  {
    id: 'wo4',
    orderNo: 'WO20260227001',
    title: '咖啡机水路堵塞',
    type: 'fault',
    priority: 'high',
    status: 'closed',
    customer: '星巴克华东区',
    project: '杭州西湖店智能化项目',
    deviceName: '咖啡机 CF-L5000',
    deviceCode: 'DEV-CF-001',
    area: '吧台区',
    description: '咖啡机出水量减少至正常的 30%，检测水路压力不足，需清洗过滤网及水路。',
    reporter: '王主管',
    reportTime: '2026-02-27 14:00',
    assignee: '刘工',
    estimatedTime: '2026-02-27 18:00',
    resolvedTime: '2026-02-27 17:20',
    logs: [
      { id: 'l10', time: '2026-02-27 14:00', operator: '王主管', action: '创建工单' },
      { id: 'l11', time: '2026-02-27 14:15', operator: '系统管理员', action: '分配工单' },
      { id: 'l12', time: '2026-02-27 15:00', operator: '刘工', action: '开始处理' },
      { id: 'l13', time: '2026-02-27 17:20', operator: '刘工', action: '解决工单', comment: '清洗水路后恢复正常' },
      { id: 'l14', time: '2026-02-27 18:00', operator: '王主管', action: '关闭工单', comment: '已验收，问题解决' },
    ],
  },
  {
    id: 'wo5',
    orderNo: 'WO20260301003',
    title: '洗碗机控制面板无响应',
    type: 'fault',
    priority: 'normal',
    status: 'pending',
    customer: '海底捞火锅连锁',
    project: '北京朝阳店智能化项目',
    deviceName: '洗碗机 DW-M9000',
    deviceCode: 'DEV-D-002',
    area: '清洗区',
    description: '洗碗机触摸控制面板偶发无响应，重启后可恢复，疑为主控板问题，需专业检测。',
    reporter: '张经理',
    reportTime: '2026-03-01 11:40',
    logs: [
      { id: 'l15', time: '2026-03-01 11:40', operator: '张经理', action: '创建工单', comment: '请尽快安排处理' },
    ],
  },
  {
    id: 'wo6',
    orderNo: 'WO20260301004',
    title: '新增蒸箱设备安装调试',
    type: 'installation',
    priority: 'normal',
    status: 'pending',
    customer: '喜茶门店管理',
    project: '上海旗舰店智能化项目',
    deviceName: '电蒸箱 ST-E6000',
    deviceCode: 'DEV-ST-003',
    area: '烘焙区',
    description: '新购入 2 台电蒸箱，需要安装接线、联网配置及功能调试，并接入 IoT 平台。',
    reporter: '李总监',
    reportTime: '2026-03-01 13:00',
    logs: [
      { id: 'l16', time: '2026-03-01 13:00', operator: '李总监', action: '创建工单' },
    ],
  },
];

// ── Alert Records ─────────────────────────────────────────────────────────────
export const SHARED_ALERT_RECORDS: AlertRecord[] = [
  { id: 'a1', alertNo: 'ALT20260304001', deviceName: '冷藏柜 SR-F520BX', deviceCode: 'DEV-C-001', area: '中央厨房', alertType: 'temperature', level: 'critical', value: '8.5°C', threshold: '5°C', alertTime: '2026-03-04 06:23:11', status: 'processing', assignee: '张工', remark: '已派人现场检修' },
  { id: 'a2', alertNo: 'ALT20260304002', deviceName: '排烟风机 EXH-200', deviceCode: 'DEV-E-003', area: '烹饪区', alertType: 'smoke', level: 'warning', value: '检测到异常', threshold: '正常范围', alertTime: '2026-03-04 07:45:00', status: 'resolved', assignee: '李工', resolveTime: '2026-03-04 09:12:00', remark: '清洁过滤网后恢复正常' },
  { id: 'a3', alertNo: 'ALT20260304003', deviceName: '智能烤箱 CVS-800', deviceCode: 'DEV-A-012', area: '烘焙间', alertType: 'temperature', level: 'warning', value: '235°C', threshold: '230°C', alertTime: '2026-03-04 08:10:22', status: 'pending', remark: '' },
  { id: 'a4', alertNo: 'ALT20260304004', deviceName: '工业洗碗机 WM-Pro', deviceCode: 'DEV-W-007', area: '洗碗间', alertType: 'power', level: 'info', value: '8.2kW', threshold: '8.0kW', alertTime: '2026-03-04 08:55:00', status: 'ignored', remark: '属于正常波动，已忽略' },
  { id: 'a5', alertNo: 'ALT20260304005', deviceName: '冷冻库 FZ-1000', deviceCode: 'DEV-C-002', area: '冷冻库', alertType: 'temperature', level: 'critical', value: '-8°C', threshold: '-18°C', alertTime: '2026-03-04 09:30:00', status: 'processing', assignee: '王工', remark: '制冷系统故障，正在维修' },
  { id: 'a6', alertNo: 'ALT20260304006', deviceName: '蒸汽设备 STE-600', deviceCode: 'DEV-S-004', area: '蒸制区', alertType: 'pressure', level: 'warning', value: '6.2bar', threshold: '6.0bar', alertTime: '2026-03-04 10:02:00', status: 'pending', remark: '' },
  { id: 'a7', alertNo: 'ALT20260303001', deviceName: '油烟净化器 OC-Pro', deviceCode: 'DEV-E-010', area: '烹饪区', alertType: 'offline', level: 'critical', value: '离线', threshold: '在线', alertTime: '2026-03-03 15:20:00', status: 'resolved', assignee: '赵工', resolveTime: '2026-03-03 17:45:00', remark: '网络模块重启后恢复' },
  { id: 'a8', alertNo: 'ALT20260303002', deviceName: '智能货架 SH-200', deviceCode: 'DEV-R-001', area: '储藏室', alertType: 'door', level: 'info', value: '门体开启超时', threshold: '15分钟', alertTime: '2026-03-03 20:10:00', status: 'resolved', assignee: '', resolveTime: '2026-03-03 20:30:00', remark: '人员确认后关闭' },
];

// ── Computed stats (used in Dashboard "运营情况") ─────────────────────────────

/** 报警：级别=严重 且 状态=待处理|处理中 */
export const ALARM_COUNT = SHARED_ALERT_RECORDS.filter(
  r => r.level === 'critical' && (r.status === 'pending' || r.status === 'processing')
).length;

/** 故障：级别=警告 且 状态=待处理|处理中 */
export const FAULT_COUNT = SHARED_ALERT_RECORDS.filter(
  r => r.level === 'warning' && (r.status === 'pending' || r.status === 'processing')
).length;

/** 工单：排除已解决、已关闭、已驳回 */
export const ACTIVE_WORK_ORDER_COUNT = SHARED_WORK_ORDERS.filter(
  o => o.status !== 'resolved' && o.status !== 'closed' && o.status !== 'rejected'
).length;
