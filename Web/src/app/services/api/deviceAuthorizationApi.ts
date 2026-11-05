import { httpClient as apiClient } from './httpClient';

// ── 类型定义（与后端 DeviceAuthorizationResponse.cs camelCase 对齐） ───────────

export interface ApiResponse<T = unknown> {
  code: number;
  data?: T;
  message?: string;
}

export interface DeviceAuthorization {
  deviceId: number;
  deviceName: string;
  model?: string | null;
  serialNumber?: string | null;
  category?: string | null;
  areaId?: number | null;
  areaName?: string | null;
  appCode?: string | null;
  deviceStatus: string;
  authorizationId?: number | null;
  /** active / expiring_soon / expired / revoked / pending */
  authorizationStatus: string;
  statusLabel: string;
  authorizedAt?: string | null;
  expireAt?: string | null;
  authorizedByName?: string | null;
  remark?: string | null;
  daysUntilExpire?: number | null;
  isExpiringSoon: boolean;
  isDisabled: boolean;
}

export interface DeviceAuthorizationSummary {
  total: number;
  active: number;
  expiringSoon: number;
  expired: number;
  pending: number;
  revoked: number;
  expiringSoonDays: number;
}

export interface GrantRequest {
  deviceId: number;
  expireAt?: string | null;
  expireDays?: number;
  appCode?: string | null;
  remark?: string | null;
}

export interface RevokeRequest {
  deviceId: number;
  appCode?: string | null;
  remark?: string | null;
}

const BASE_URL = '/device-authorizations';

/** 授权列表（每台设备一行，未授权也列出为 pending） */
export async function getList(): Promise<ApiResponse<DeviceAuthorization[]>> {
  const r = await apiClient.get<ApiResponse<DeviceAuthorization[]>>(BASE_URL);
  return r.data;
}

/** 统计 */
export async function getSummary(): Promise<ApiResponse<DeviceAuthorizationSummary>> {
  const r = await apiClient.get<ApiResponse<DeviceAuthorizationSummary>>(`${BASE_URL}/summary`);
  return r.data;
}

/** 授权 / 续期（仅超管） */
export async function grant(request: GrantRequest): Promise<ApiResponse<DeviceAuthorization>> {
  const r = await apiClient.post<ApiResponse<DeviceAuthorization>>(`${BASE_URL}/grant`, request);
  return r.data;
}

/** 撤销授权（仅超管） */
export async function revoke(request: RevokeRequest): Promise<ApiResponse<DeviceAuthorization>> {
  const r = await apiClient.post<ApiResponse<DeviceAuthorization>>(`${BASE_URL}/revoke`, request);
  return r.data;
}

/** 手动触收到期扫描（仅超管，运维即时刷新） */
export async function scan(): Promise<ApiResponse<{ created: number }>> {
  const r = await apiClient.post<ApiResponse<{ created: number }>>(`${BASE_URL}/scan`, {});
  return r.data;
}

export const deviceAuthorizationApi = {
  getList,
  getSummary,
  grant,
  revoke,
  scan,
};

export default deviceAuthorizationApi;
