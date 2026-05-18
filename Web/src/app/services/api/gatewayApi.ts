import { httpClient as apiClient } from './httpClient';

// ── 类型定义 ─────────────────────────────────────────────────────────────────

/** 网关配置 */
export interface Gateway {
  id: number;
  name: string;
  gatewayType?: string;
  sourceProtocol: string;
  targetProtocol: string;
  status: 'online' | 'offline' | 'error';
  isActive: boolean;
  throughput: number;
  config?: Record<string, unknown>;
  description?: string;
  appCode?: string;
  createdAt?: string;
  updatedAt?: string;
}

/** 创建网关请求 */
export interface CreateGatewayRequest {
  name: string;
  gatewayType?: string;
  sourceProtocol: string;
  targetProtocol: string;
  isActive?: boolean;
  config?: Record<string, unknown>;
  description?: string;
}

/** 更新网关请求 */
export interface UpdateGatewayRequest {
  name: string;
  gatewayType?: string;
  sourceProtocol?: string;
  targetProtocol?: string;
  status?: string;
  isActive?: boolean;
  config?: Record<string, unknown>;
  description?: string;
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

const BASE_URL = '/gateways';

/**
 * 获取网关列表
 */
export async function getGateways(params?: {
  page?: number;
  pageSize?: number;
  keyword?: string;
}): Promise<ApiResponse<PagedResponse<Gateway>>> {
  const searchParams = new URLSearchParams();
  if (params?.page) searchParams.set('page', params.page.toString());
  if (params?.pageSize) searchParams.set('pageSize', params.pageSize.toString());
  if (params?.keyword) searchParams.set('keyword', params.keyword);

  const query = searchParams.toString();
  const response = await apiClient.get<ApiResponse<PagedResponse<Gateway>>>(
    `${BASE_URL}${query ? `?${query}` : ''}`
  );
  return response.data;
}

/**
 * 获取网关详情
 */
export async function getGateway(id: number): Promise<ApiResponse<Gateway>> {
  const response = await apiClient.get<ApiResponse<Gateway>>(`${BASE_URL}/${id}`);
  return response.data;
}

/**
 * 创建网关
 */
export async function createGateway(
  request: CreateGatewayRequest
): Promise<ApiResponse<Gateway>> {
  const response = await apiClient.post<ApiResponse<Gateway>>(BASE_URL, request);
  return response.data;
}

/**
 * 更新网关
 */
export async function updateGateway(
  id: number,
  request: UpdateGatewayRequest
): Promise<ApiResponse<Gateway>> {
  const response = await apiClient.put<ApiResponse<Gateway>>(`${BASE_URL}/${id}`, request);
  return response.data;
}

/**
 * 删除网关
 */
export async function deleteGateway(id: number): Promise<ApiResponse> {
  const response = await apiClient.delete<ApiResponse>(`${BASE_URL}/${id}`);
  return response.data;
}

/**
 * 启动网关
 */
export async function startGateway(id: number): Promise<ApiResponse> {
  const response = await apiClient.post<ApiResponse>(`${BASE_URL}/${id}/start`);
  return response.data;
}

/**
 * 停止网关
 */
export async function stopGateway(id: number): Promise<ApiResponse> {
  const response = await apiClient.post<ApiResponse>(`${BASE_URL}/${id}/stop`);
  return response.data;
}

// ── 导出 API 对象 ──────────────────────────────────────────────────────────────

export const gatewayApi = {
  getGateways,
  getGateway,
  createGateway,
  updateGateway,
  deleteGateway,
  startGateway,
  stopGateway,
};

export default gatewayApi;
