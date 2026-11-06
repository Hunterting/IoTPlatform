// ── Shared mock alert records (only consumer: LogManagement page) ─────────────
// 工单与工作台已接入真实后端接口，工单侧 mock 数据（SHARED_WORK_ORDERS /
// ACTIVE_WORK_ORDER_COUNT / WorkOrder / WorkOrderLog / WorkOrder* 类型）已删除。

export type AlertLevel = 'info' | 'warning' | 'critical';
export type AlertStatus = 'pending' | 'processing' | 'resolved' | 'ignored';
export type AlertType = 'temperature' | 'pressure' | 'offline' | 'power' | 'smoke' | 'door';

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

// ── Computed stats ────────────────────────────────────────────────────────────

/** 报警：级别=严重 且 状态=待处理|处理中 */
export const ALARM_COUNT = SHARED_ALERT_RECORDS.filter(
  r => r.level === 'critical' && (r.status === 'pending' || r.status === 'processing')
).length;

/** 故障：级别=警告 且 状态=待处理|处理中 */
export const FAULT_COUNT = SHARED_ALERT_RECORDS.filter(
  r => r.level === 'warning' && (r.status === 'pending' || r.status === 'processing')
).length;
