import { httpClient as apiClient } from './httpClient';
import { getMonitoringData, type MonitoringDataDto } from './monitoringApi';

// ── 类型定义 ─────────────────────────────────────────────────────────────────

/** 通用 API 响应信封 */
export interface ApiResponse<T = unknown> {
  code: number;
  data?: T;
  message?: string;
  success?: boolean;
}

/** 分页响应 */
export interface PagedResponse<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

/** 告警汇总（/alerts/summary） */
export interface AlertSummaryDto {
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

/** 告警条目（/alerts） */
export interface AlertDto {
  id: number;
  alertNo: string;
  deviceName: string;
  deviceCode?: string | null;
  area?: string | null;
  alertType: string;
  level: string;
  value?: number | null;
  threshold?: number | null;
  alertTime: string;
  status: string;
  assignee?: string | null;
}

/** 监控数据点（复用 monitoringApi 的 DTO） */
export type { MonitoringDataDto };

// ── API 函数 ──────────────────────────────────────────────────────────────────

/**
 * 告警汇总（待处理/严重/按类型分布）
 */
export async function getAlertSummary(): Promise<ApiResponse<AlertSummaryDto>> {
  const response = await apiClient.get<ApiResponse<AlertSummaryDto>>('/alerts/summary');
  return response.data;
}

/**
 * 告警列表（用于「预警事件」面板）
 */
export async function getAlerts(params?: {
  page?: number;
  pageSize?: number;
  status?: string;
  level?: string;
}): Promise<ApiResponse<PagedResponse<AlertDto>>> {
  const searchParams = new URLSearchParams();
  if (params?.page) searchParams.set('page', params.page.toString());
  if (params?.pageSize) searchParams.set('pageSize', params.pageSize.toString());
  if (params?.status) searchParams.set('status', params.status);
  if (params?.level) searchParams.set('level', params.level);
  const query = searchParams.toString();
  const response = await apiClient.get<ApiResponse<PagedResponse<AlertDto>>>(
    `/alerts${query ? `?${query}` : ''}`
  );
  return response.data;
}

/**
 * 工单数（取分页 totalCount，status=pending 优先；取不到则用总数）
 */
export async function getWorkOrderCount(): Promise<ApiResponse<number>> {
  const pending = await apiClient.get<ApiResponse<PagedResponse<unknown>>>(
    '/work-orders?page=1&pageSize=1&status=pending'
  );
  const pendingBody = pending.data;
  if (pendingBody.code === 200 && pendingBody.data) {
    return { code: 200, data: pendingBody.data.totalCount ?? 0, message: pendingBody.message };
  }
  // 退化：不带 status 拉总数
  const all = await apiClient.get<ApiResponse<PagedResponse<unknown>>>(
    '/work-orders?page=1&pageSize=1'
  );
  const allBody = all.data;
  return {
    code: allBody.code,
    data: allBody.code === 200 && allBody.data ? (allBody.data.totalCount ?? 0) : 0,
    message: allBody.message,
  };
}

/**
 * 最近监控数据点（环境/变频排风/能耗三条曲线的真实数据源）
 */
export async function getRecentMonitoringData(
  pageSize = 50
): Promise<ApiResponse<MonitoringDataDto[]>> {
  const resp = await getMonitoringData({ page: 1, pageSize });
  if (resp.code === 200 && resp.data) {
    return { code: 200, data: resp.data.items || [], message: resp.message };
  }
  return { code: resp.code, data: [], message: resp.message || '获取监控数据失败' };
}

// ── 导出 API 对象 ──────────────────────────────────────────────────────────────

export const dashboardApi = {
  getAlertSummary,
  getAlerts,
  getWorkOrderCount,
  getRecentMonitoringData,
};

export default dashboardApi;
