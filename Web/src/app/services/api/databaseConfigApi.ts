import { httpClient as apiClient } from './httpClient';

// ── 类型定义 ─────────────────────────────────────────────────────────────────

/** 数据库配置 */
export interface DatabaseConfig {
  id: number;
  name: string;
  databaseType: 'MySQL' | 'TDengine' | 'InfluxDB' | 'PostgreSQL' | 'MongoDB';
  status: 'connected' | 'disconnected' | 'error';
  isActive: boolean;
  host: string;
  port: number;
  databaseName: string;
  username?: string;
  hasPassword: boolean;
  description?: string;
  lastTestAt?: string;
  appCode?: string;
  createdAt?: string;
  updatedAt?: string;
}

/** 创建数据库配置请求 */
export interface CreateDatabaseConfigRequest {
  name: string;
  databaseType: 'MySQL' | 'TDengine' | 'InfluxDB' | 'PostgreSQL' | 'MongoDB';
  isActive?: boolean;
  host: string;
  port?: number;
  databaseName: string;
  username?: string;
  password?: string;
  connectionString?: string;
  config?: Record<string, unknown>;
  description?: string;
}

/** 更新数据库配置请求 */
export interface UpdateDatabaseConfigRequest {
  name: string;
  databaseType?: string;
  status?: string;
  isActive?: boolean;
  host?: string;
  port?: number;
  databaseName?: string;
  username?: string;
  password?: string;
  connectionString?: string;
  config?: Record<string, unknown>;
  description?: string;
}

/** 测试数据库连接请求 */
export interface TestDatabaseConnectionRequest {
  databaseType: string;
  host: string;
  port?: number;
  databaseName: string;
  username?: string;
  password?: string;
  connectionString?: string;
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

const BASE_URL = '/database-configs';

/**
 * 获取数据库配置列表
 */
export async function getDatabaseConfigs(params?: {
  page?: number;
  pageSize?: number;
  keyword?: string;
  databaseType?: string;
}): Promise<ApiResponse<PagedResponse<DatabaseConfig>>> {
  const searchParams = new URLSearchParams();
  if (params?.page) searchParams.set('page', params.page.toString());
  if (params?.pageSize) searchParams.set('pageSize', params.pageSize.toString());
  if (params?.keyword) searchParams.set('keyword', params.keyword);
  if (params?.databaseType) searchParams.set('databaseType', params.databaseType);

  const query = searchParams.toString();
  const response = await apiClient.get<ApiResponse<PagedResponse<DatabaseConfig>>>(
    `${BASE_URL}${query ? `?${query}` : ''}`
  );
  return response.data;
}

/**
 * 获取数据库配置详情
 */
export async function getDatabaseConfig(id: number): Promise<ApiResponse<DatabaseConfig>> {
  const response = await apiClient.get<ApiResponse<DatabaseConfig>>(`${BASE_URL}/${id}`);
  return response.data;
}

/**
 * 创建数据库配置
 */
export async function createDatabaseConfig(
  request: CreateDatabaseConfigRequest
): Promise<ApiResponse<DatabaseConfig>> {
  const response = await apiClient.post<ApiResponse<DatabaseConfig>>(BASE_URL, request);
  return response.data;
}

/**
 * 更新数据库配置
 */
export async function updateDatabaseConfig(
  id: number,
  request: UpdateDatabaseConfigRequest
): Promise<ApiResponse<DatabaseConfig>> {
  const response = await apiClient.put<ApiResponse<DatabaseConfig>>(`${BASE_URL}/${id}`, request);
  return response.data;
}

/**
 * 删除数据库配置
 */
export async function deleteDatabaseConfig(id: number): Promise<ApiResponse> {
  const response = await apiClient.delete<ApiResponse>(`${BASE_URL}/${id}`);
  return response.data;
}

/**
 * 测试数据库连接
 */
export async function testDatabaseConnection(
  request: TestDatabaseConnectionRequest
): Promise<ApiResponse<boolean>> {
  const response = await apiClient.post<ApiResponse<boolean>>(`${BASE_URL}/test-connection`, request);
  return response.data;
}

/**
 * 测试已有配置的连接
 */
export async function testDatabaseConnectionById(id: number): Promise<ApiResponse<boolean>> {
  const response = await apiClient.post<ApiResponse<boolean>>(`${BASE_URL}/${id}/test-connection`);
  return response.data;
}

// ── 导出 API 对象 ──────────────────────────────────────────────────────────────

export const databaseConfigApi = {
  getDatabaseConfigs,
  getDatabaseConfig,
  createDatabaseConfig,
  updateDatabaseConfig,
  deleteDatabaseConfig,
  testDatabaseConnection,
  testDatabaseConnectionById,
};

export default databaseConfigApi;
