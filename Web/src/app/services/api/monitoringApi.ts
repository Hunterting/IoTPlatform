import { httpClient as apiClient } from './httpClient';

// ── 类型定义 ─────────────────────────────────────────────────────────────────

/** 监控数据点 DTO（对应后端 MonitoringDataDto） */
export interface MonitoringDataDto {
  id: number;
  deviceId?: number;
  deviceName?: string;
  areaId?: number;
  areaName?: string;
  /** 设备能源类型（取 EnergyTypes 首个，如 electric/water/gas） */
  energyType?: string;
  /** 原始上报数据点（来自 DeviceDataRecord.SensorData JSON） */
  sensorValues?: Record<string, unknown>;
  /** 数据质量（良好 / 警告 / 异常 / 正常） */
  quality?: string;
  timestamp: string;
}

/** 分页响应 */
export interface PagedResponse<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

/** 通用 API 响应 */
export interface ApiResponse<T = unknown> {
  code: number;
  data?: T;
  message?: string;
  success?: boolean;
}

// ── API 函数 ──────────────────────────────────────────────────────────────────

const BASE_URL = '/monitoring';

/**
 * 获取监控数据列表（数据点）
 */
export async function getMonitoringData(params?: {
  page?: number;
  pageSize?: number;
}): Promise<ApiResponse<PagedResponse<MonitoringDataDto>>> {
  const searchParams = new URLSearchParams();
  if (params?.page) searchParams.set('page', params.page.toString());
  if (params?.pageSize) searchParams.set('pageSize', params.pageSize.toString());

  const query = searchParams.toString();
  const response = await apiClient.get<ApiResponse<PagedResponse<MonitoringDataDto>>>(
    `${BASE_URL}/data${query ? `?${query}` : ''}`
  );
  return response.data;
}

// ── 导出 API 对象 ──────────────────────────────────────────────────────────────

export const monitoringApi = {
  getMonitoringData,
};

export default monitoringApi;
