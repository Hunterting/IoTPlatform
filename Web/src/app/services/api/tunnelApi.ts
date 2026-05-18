import { httpClient as apiClient } from './httpClient';

// ── 类型定义 ─────────────────────────────────────────────────────────────────

/** 隧道配置 */
export interface Tunnel {
  id: number;
  name: string;
  tunnelType: 'P2P' | 'Proxy' | 'VPN';
  status: 'connected' | 'disconnected' | 'error';
  isActive: boolean;
  localPort: number;
  remotePort: number;
  remoteHost?: string;
  encryption: boolean;
  bandwidth?: string;
  config?: Record<string, unknown>;
  description?: string;
  appCode?: string;
  createdAt?: string;
  updatedAt?: string;
}

/** 创建隧道请求 */
export interface CreateTunnelRequest {
  name: string;
  tunnelType: 'P2P' | 'Proxy' | 'VPN';
  isActive?: boolean;
  localPort: number;
  remotePort: number;
  remoteHost?: string;
  encryption?: boolean;
  config?: Record<string, unknown>;
  description?: string;
}

/** 更新隧道请求 */
export interface UpdateTunnelRequest {
  name: string;
  tunnelType?: 'P2P' | 'Proxy' | 'VPN';
  status?: string;
  isActive?: boolean;
  localPort?: number;
  remotePort?: number;
  remoteHost?: string;
  encryption?: boolean;
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

const BASE_URL = '/tunnels';

/**
 * 获取隧道列表
 */
export async function getTunnels(params?: {
  page?: number;
  pageSize?: number;
  keyword?: string;
}): Promise<ApiResponse<PagedResponse<Tunnel>>> {
  const searchParams = new URLSearchParams();
  if (params?.page) searchParams.set('page', params.page.toString());
  if (params?.pageSize) searchParams.set('pageSize', params.pageSize.toString());
  if (params?.keyword) searchParams.set('keyword', params.keyword);

  const query = searchParams.toString();
  const response = await apiClient.get<ApiResponse<PagedResponse<Tunnel>>>(
    `${BASE_URL}${query ? `?${query}` : ''}`
  );
  return response.data;
}

/**
 * 获取隧道详情
 */
export async function getTunnel(id: number): Promise<ApiResponse<Tunnel>> {
  const response = await apiClient.get<ApiResponse<Tunnel>>(`${BASE_URL}/${id}`);
  return response.data;
}

/**
 * 创建隧道
 */
export async function createTunnel(
  request: CreateTunnelRequest
): Promise<ApiResponse<Tunnel>> {
  const response = await apiClient.post<ApiResponse<Tunnel>>(BASE_URL, request);
  return response.data;
}

/**
 * 更新隧道
 */
export async function updateTunnel(
  id: number,
  request: UpdateTunnelRequest
): Promise<ApiResponse<Tunnel>> {
  const response = await apiClient.put<ApiResponse<Tunnel>>(`${BASE_URL}/${id}`, request);
  return response.data;
}

/**
 * 删除隧道
 */
export async function deleteTunnel(id: number): Promise<ApiResponse> {
  const response = await apiClient.delete<ApiResponse>(`${BASE_URL}/${id}`);
  return response.data;
}

/**
 * 连接隧道
 */
export async function connectTunnel(id: number): Promise<ApiResponse> {
  const response = await apiClient.post<ApiResponse>(`${BASE_URL}/${id}/connect`);
  return response.data;
}

/**
 * 断开隧道
 */
export async function disconnectTunnel(id: number): Promise<ApiResponse> {
  const response = await apiClient.post<ApiResponse>(`${BASE_URL}/${id}/disconnect`);
  return response.data;
}

// ── 导出 API 对象 ──────────────────────────────────────────────────────────────

export const tunnelApi = {
  getTunnels,
  getTunnel,
  createTunnel,
  updateTunnel,
  deleteTunnel,
  connectTunnel,
  disconnectTunnel,
};

export default tunnelApi;
