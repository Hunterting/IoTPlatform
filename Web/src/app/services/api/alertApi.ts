import { httpClient as apiClient } from './httpClient';

// ── 类型定义（与后端 AlertResponse.cs camelCase 出网对齐） ───────────────────────

export interface ApiResponse<T = unknown> {
  code: number;
  data?: T;
  message?: string;
}

/** 真实告警记录 */
export interface AlertRecord {
  id: number;
  alertNo: string;
  deviceName: string;
  deviceCode?: string | null;
  area?: string | null;        // 后端 AlertDto.Area（来自 AlertRecord.AreaName）
  alertType: string;
  level: string;               // info / warning / critical
  value?: number | null;
  threshold?: number | null;
  alertTime: string;           // "yyyy-MM-dd HH:mm:ss"
  status: string;              // pending / processing / resolved / ignored
  assignee?: string | null;
  resolveTime?: string | null;
  remark?: string | null;
  deviceId?: number | null;
  areaId?: number | null;
  appCode?: string | null;     // 区分租户
  createdAt: string;
  updatedAt: string;
}

export interface PagedResponse<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export interface AlertSummary {
  totalAlerts: number;
  pendingAlerts: number;
  processingAlerts: number;
  resolvedAlerts: number;
  ignoredAlerts: number;
  criticalAlerts: number;
  warningAlerts: number;
  infoAlerts: number;
  alertsByType: Record<string, number>;
  lastUpdate: string;
}

export interface AlertListParams {
  page?: number;
  pageSize?: number;
  status?: string;
  level?: string;
  alertType?: string;
}

const BASE_URL = '/alerts';

/** 告警列表（后端按 JWT AppCode 自动区分租户；超管看全部） */
export async function getAlerts(
  params: AlertListParams = {}
): Promise<ApiResponse<PagedResponse<AlertRecord>>> {
  const sp = new URLSearchParams();
  if (params.page) sp.set('page', String(params.page));
  if (params.pageSize) sp.set('pageSize', String(params.pageSize));
  if (params.status) sp.set('status', params.status);
  if (params.level) sp.set('level', params.level);
  if (params.alertType) sp.set('alertType', params.alertType);
  const q = sp.toString();
  const r = await apiClient.get<ApiResponse<PagedResponse<AlertRecord>>>(
    `${BASE_URL}${q ? `?${q}` : ''}`
  );
  return r.data;
}

/** 告警汇总（同样按租户隔离） */
export async function getSummary(): Promise<ApiResponse<AlertSummary>> {
  const r = await apiClient.get<ApiResponse<AlertSummary>>(`${BASE_URL}/summary`);
  return r.data;
}

/** 告警详情 */
export async function getAlert(id: number): Promise<ApiResponse<AlertRecord>> {
  const r = await apiClient.get<ApiResponse<AlertRecord>>(`${BASE_URL}/${id}`);
  return r.data;
}

/** 处理告警（assign/process/resolve/ignore/remark） */
export async function processAlert(
  id: number, action: string, comment?: string
): Promise<ApiResponse<AlertRecord>> {
  const r = await apiClient.post<ApiResponse<AlertRecord>>(`${BASE_URL}/${id}/process`, { action, comment });
  return r.data;
}

/** 分配告警 */
export async function assignAlert(id: number, assignee: string): Promise<ApiResponse<AlertRecord>> {
  const r = await apiClient.post<ApiResponse<AlertRecord>>(`${BASE_URL}/${id}/assign`, { assignee });
  return r.data;
}

/** 解决告警 */
export async function resolveAlert(id: number, remark?: string): Promise<ApiResponse<AlertRecord>> {
  const r = await apiClient.post<ApiResponse<AlertRecord>>(`${BASE_URL}/${id}/resolve`, { remark });
  return r.data;
}

/** 忽略告警 */
export async function ignoreAlert(id: number, remark?: string): Promise<ApiResponse<AlertRecord>> {
  const r = await apiClient.post<ApiResponse<AlertRecord>>(`${BASE_URL}/${id}/ignore`, { remark });
  return r.data;
}

export const alertApi = {
  getAlerts,
  getSummary,
  getAlert,
  processAlert,
  assignAlert,
  resolveAlert,
  ignoreAlert,
};

export default alertApi;
