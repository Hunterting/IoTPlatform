import { httpClient as apiClient } from './httpClient';

// ── 类型定义 ─────────────────────────────────────────────────────────────────

/** ETL任务 */
export interface ETLTask {
  id: number;
  name: string;
  taskType?: string;
  sourceType?: string;
  targetType?: string;
  status: 'running' | 'stopped' | 'failed' | 'pending';
  isActive: boolean;
  config?: Record<string, unknown>;
  transformRules?: TransformRule[];
  fieldMappings?: FieldMapping[];
  lastRunAt?: string;
  recordsProcessed?: number;
  errorMessage?: string;
  description?: string;
  appCode?: string;
  createdAt?: string;
  updatedAt?: string;
}

/** 转换规则 */
export interface TransformRule {
  id?: string;
  type: 'format' | 'calculate' | 'filter' | 'merge';
  name: string;
  description?: string;
  config?: Record<string, unknown>;
}

/** 字段映射 */
export interface FieldMapping {
  id?: string;
  sourceField: string;
  targetField: string;
  dataType?: string;
  transform?: string;
}

/** 创建ETL任务请求 */
export interface CreateETLTaskRequest {
  name: string;
  taskType?: string;
  sourceType?: string;
  targetType?: string;
  isActive?: boolean;
  config?: Record<string, unknown>;
  transformRules?: TransformRule[];
  fieldMappings?: FieldMapping[];
  description?: string;
}

/** 更新ETL任务请求 */
export interface UpdateETLTaskRequest {
  name?: string;
  taskType?: string;
  sourceType?: string;
  targetType?: string;
  status?: string;
  isActive?: boolean;
  config?: Record<string, unknown>;
  transformRules?: TransformRule[];
  fieldMappings?: FieldMapping[];
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

const BASE_URL = '/etl-tasks';

/**
 * 获取ETL任务列表
 */
export async function getETLTasks(params?: {
  page?: number;
  pageSize?: number;
  keyword?: string;
  taskType?: string;
}): Promise<ApiResponse<PagedResponse<ETLTask>>> {
  const searchParams = new URLSearchParams();
  if (params?.page) searchParams.set('page', params.page.toString());
  if (params?.pageSize) searchParams.set('pageSize', params.pageSize.toString());
  if (params?.keyword) searchParams.set('keyword', params.keyword);
  if (params?.taskType) searchParams.set('taskType', params.taskType);

  const query = searchParams.toString();
  const response = await apiClient.get<ApiResponse<PagedResponse<ETLTask>>>(
    `${BASE_URL}${query ? `?${query}` : ''}`
  );
  return response.data;
}

/**
 * 获取ETL任务详情
 */
export async function getETLTask(id: number): Promise<ApiResponse<ETLTask>> {
  const response = await apiClient.get<ApiResponse<ETLTask>>(`${BASE_URL}/${id}`);
  return response.data;
}

/**
 * 创建ETL任务
 */
export async function createETLTask(
  request: CreateETLTaskRequest
): Promise<ApiResponse<ETLTask>> {
  const response = await apiClient.post<ApiResponse<ETLTask>>(BASE_URL, request);
  return response.data;
}

/**
 * 更新ETL任务
 */
export async function updateETLTask(
  id: number,
  request: UpdateETLTaskRequest
): Promise<ApiResponse<ETLTask>> {
  const response = await apiClient.put<ApiResponse<ETLTask>>(`${BASE_URL}/${id}`, request);
  return response.data;
}

/**
 * 删除ETL任务
 */
export async function deleteETLTask(id: number): Promise<ApiResponse> {
  const response = await apiClient.delete<ApiResponse>(`${BASE_URL}/${id}`);
  return response.data;
}

/**
 * 启动ETL任务
 */
export async function startETLTask(id: number): Promise<ApiResponse> {
  const response = await apiClient.post<ApiResponse>(`${BASE_URL}/${id}/start`);
  return response.data;
}

/**
 * 停止ETL任务
 */
export async function stopETLTask(id: number): Promise<ApiResponse> {
  const response = await apiClient.post<ApiResponse>(`${BASE_URL}/${id}/stop`);
  return response.data;
}

// ── 导出 API 对象 ──────────────────────────────────────────────────────────────

export const etlTaskApi = {
  getETLTasks,
  getETLTask,
  createETLTask,
  updateETLTask,
  deleteETLTask,
  startETLTask,
  stopETLTask,
};

export default etlTaskApi;
