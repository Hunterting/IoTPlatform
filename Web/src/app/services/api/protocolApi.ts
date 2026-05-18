import { httpClient as apiClient } from './httpClient';

// ── 类型定义 ─────────────────────────────────────────────────────────────────

/** 协议配置项 */
export interface ProtocolConfig {
  id: number;
  name: string;
  type: string;
  status: string;
  isActive?: boolean;
  deviceIds?: number[];
  config?: Record<string, unknown>;
  description?: string;
  appCode?: string;
  createdAt?: string | Date;
  updatedAt?: string | Date;
}

/** 创建协议配置请求 */
export interface CreateProtocolConfigRequest {
  name: string;
  type: string;
  description?: string;
  deviceIds?: number[];
  config?: Record<string, unknown>;
  status?: string;
  isActive?: boolean;
  appCode?: string;
}

/** 更新协议配置请求 */
export interface UpdateProtocolConfigRequest {
  name: string;
  status?: string;
  isActive?: boolean;
  description?: string;
  deviceIds?: number[];
  config?: Record<string, unknown>;
  appCode?: string;
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

const BASE_URL = '/protocol-configs';

/**
 * 获取协议配置列表
 */
export async function getProtocolConfigs(params?: {
  page?: number;
  pageSize?: number;
  keyword?: string;
  type?: string;
}): Promise<ApiResponse<PagedResponse<ProtocolConfig>>> {
  const searchParams = new URLSearchParams();
  if (params?.page) searchParams.set('page', params.page.toString());
  if (params?.pageSize) searchParams.set('pageSize', params.pageSize.toString());
  if (params?.keyword) searchParams.set('keyword', params.keyword);
  if (params?.type) searchParams.set('type', params.type);

  const query = searchParams.toString();
  const response = await apiClient.get<ApiResponse<PagedResponse<ProtocolConfig>>>(
    `${BASE_URL}${query ? `?${query}` : ''}`
  );
  return response.data;
}

/**
 * 获取协议配置详情
 */
export async function getProtocolConfig(id: number): Promise<ApiResponse<ProtocolConfig>> {
  const response = await apiClient.get<ApiResponse<ProtocolConfig>>(`${BASE_URL}/${id}`);
  return response.data;
}

/**
 * 创建协议配置
 */
export async function createProtocolConfig(
  request: CreateProtocolConfigRequest
): Promise<ApiResponse<ProtocolConfig>> {
  const response = await apiClient.post<ApiResponse<ProtocolConfig>>(BASE_URL, request);
  return response.data;
}

/**
 * 更新协议配置
 */
export async function updateProtocolConfig(
  id: number,
  request: UpdateProtocolConfigRequest
): Promise<ApiResponse<ProtocolConfig>> {
  const response = await apiClient.put<ApiResponse<ProtocolConfig>>(`${BASE_URL}/${id}`, request);
  return response.data;
}

/**
 * 删除协议配置
 */
export async function deleteProtocolConfig(id: number): Promise<ApiResponse> {
  const response = await apiClient.delete<ApiResponse>(`${BASE_URL}/${id}`);
  return response.data;
}

/**
 * 启动协议
 */
export async function startProtocol(id: number): Promise<ApiResponse> {
  const response = await apiClient.post<ApiResponse>(`${BASE_URL}/${id}/start`);
  return response.data;
}

/**
 * 停止协议
 */
export async function stopProtocol(id: number): Promise<ApiResponse> {
  const response = await apiClient.post<ApiResponse>(`${BASE_URL}/${id}/stop`);
  return response.data;
}

/**
 * 根据设备ID列表获取设备信息
 */
export async function getDevicesByIds(deviceIds: number[]): Promise<ApiResponse<Array<{
  id: number;
  name: string;
  serialNumber?: string;
  status: string;
  location?: string;
}>>> {
  if (!deviceIds || deviceIds.length === 0) {
    return { code: 200, data: [], message: '成功', success: true };
  }
  const idsParam = deviceIds.join(',');
  const response = await apiClient.get<ApiResponse<any[]>>(
    `/devices?ids=${idsParam}&pageSize=${deviceIds.length}`
  );
  return response.data;
}

// ── 导出 API 对象 ──────────────────────────────────────────────────────────────

export const protocolApi = {
  getProtocolConfigs,
  getProtocolConfig,
  createProtocolConfig,
  updateProtocolConfig,
  deleteProtocolConfig,
  startProtocol,
  stopProtocol,
  getDevicesByIds,
};

export default protocolApi;
