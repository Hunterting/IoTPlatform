import { httpClient as apiClient } from './httpClient';

// ── 类型定义 ─────────────────────────────────────────────────────────────────

/** 插件配置 */
export interface Plugin {
  id: number;
  name: string;
  version: string;
  status: 'running' | 'stopped' | 'error';
  isActive: boolean;
  pluginType?: string;
  description?: string;
  author?: string;
  filePath?: string;
  config?: Record<string, unknown>;
  dependencies?: string;
  installedAt?: string;
  appCode?: string;
  createdAt?: string;
  updatedAt?: string;
}

/** 创建插件请求 */
export interface CreatePluginRequest {
  name: string;
  version?: string;
  pluginType?: string;
  description?: string;
  author?: string;
  filePath?: string;
  config?: Record<string, unknown>;
  dependencies?: string;
}

/** 更新插件请求 */
export interface UpdatePluginRequest {
  name: string;
  version?: string;
  status?: string;
  isActive?: boolean;
  pluginType?: string;
  description?: string;
  author?: string;
  filePath?: string;
  config?: Record<string, unknown>;
  dependencies?: string;
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

const BASE_URL = '/plugins';

/**
 * 获取插件列表
 */
export async function getPlugins(params?: {
  page?: number;
  pageSize?: number;
  keyword?: string;
}): Promise<ApiResponse<PagedResponse<Plugin>>> {
  const searchParams = new URLSearchParams();
  if (params?.page) searchParams.set('page', params.page.toString());
  if (params?.pageSize) searchParams.set('pageSize', params.pageSize.toString());
  if (params?.keyword) searchParams.set('keyword', params.keyword);

  const query = searchParams.toString();
  const response = await apiClient.get<ApiResponse<PagedResponse<Plugin>>>(
    `${BASE_URL}${query ? `?${query}` : ''}`
  );
  return response.data;
}

/**
 * 获取插件详情
 */
export async function getPlugin(id: number): Promise<ApiResponse<Plugin>> {
  const response = await apiClient.get<ApiResponse<Plugin>>(`${BASE_URL}/${id}`);
  return response.data;
}

/**
 * 创建插件
 */
export async function createPlugin(
  request: CreatePluginRequest
): Promise<ApiResponse<Plugin>> {
  const response = await apiClient.post<ApiResponse<Plugin>>(BASE_URL, request);
  return response.data;
}

/**
 * 更新插件
 */
export async function updatePlugin(
  id: number,
  request: UpdatePluginRequest
): Promise<ApiResponse<Plugin>> {
  const response = await apiClient.put<ApiResponse<Plugin>>(`${BASE_URL}/${id}`, request);
  return response.data;
}

/**
 * 删除插件
 */
export async function deletePlugin(id: number): Promise<ApiResponse> {
  const response = await apiClient.delete<ApiResponse>(`${BASE_URL}/${id}`);
  return response.data;
}

/**
 * 启动插件
 */
export async function startPlugin(id: number): Promise<ApiResponse> {
  const response = await apiClient.post<ApiResponse>(`${BASE_URL}/${id}/start`);
  return response.data;
}

/**
 * 停止插件
 */
export async function stopPlugin(id: number): Promise<ApiResponse> {
  const response = await apiClient.post<ApiResponse>(`${BASE_URL}/${id}/stop`);
  return response.data;
}

// ── 导出 API 对象 ──────────────────────────────────────────────────────────────

export const pluginApi = {
  getPlugins,
  getPlugin,
  createPlugin,
  updatePlugin,
  deletePlugin,
  startPlugin,
  stopPlugin,
};

export default pluginApi;
