import { httpClient as apiClient } from './httpClient';

// ── 类型定义 ─────────────────────────────────────────────────────────────────

/** 数据规则 */
export interface DataRule {
  id: number;
  name: string;
  ruleType?: 'alert' | 'transform' | 'validation';
  dataType?: string;
  minValue?: number;
  maxValue?: number;
  level?: 'info' | 'warning' | 'critical';
  priority: number;
  isActive: boolean;
  deviceId?: number;
  areaId?: number;
  ruleExpression?: string;
  description?: string;
  appCode?: string;
  createdAt?: string;
  updatedAt?: string;
}

/** 创建数据规则请求 */
export interface CreateDataRuleRequest {
  name: string;
  ruleType?: string;
  dataType?: string;
  minValue?: number;
  maxValue?: number;
  level?: string;
  priority?: number;
  isActive?: boolean;
  deviceId?: number;
  areaId?: number;
  ruleExpression?: string;
  description?: string;
}

/** 更新数据规则请求 */
export interface UpdateDataRuleRequest {
  name?: string;
  ruleType?: string;
  dataType?: string;
  minValue?: number;
  maxValue?: number;
  level?: string;
  priority?: number;
  isActive?: boolean;
  deviceId?: number;
  areaId?: number;
  ruleExpression?: string;
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

const BASE_URL = '/data-rules';

/**
 * 获取数据规则列表
 */
export async function getDataRules(params?: {
  page?: number;
  pageSize?: number;
  keyword?: string;
  ruleType?: string;
}): Promise<ApiResponse<PagedResponse<DataRule>>> {
  const searchParams = new URLSearchParams();
  if (params?.page) searchParams.set('page', params.page.toString());
  if (params?.pageSize) searchParams.set('pageSize', params.pageSize.toString());
  if (params?.keyword) searchParams.set('keyword', params.keyword);
  if (params?.ruleType) searchParams.set('ruleType', params.ruleType);

  const query = searchParams.toString();
  const response = await apiClient.get<ApiResponse<PagedResponse<DataRule>>>(
    `${BASE_URL}${query ? `?${query}` : ''}`
  );
  return response.data;
}

/**
 * 获取数据规则详情
 */
export async function getDataRule(id: number): Promise<ApiResponse<DataRule>> {
  const response = await apiClient.get<ApiResponse<DataRule>>(`${BASE_URL}/${id}`);
  return response.data;
}

/**
 * 创建数据规则
 */
export async function createDataRule(
  request: CreateDataRuleRequest
): Promise<ApiResponse<DataRule>> {
  const response = await apiClient.post<ApiResponse<DataRule>>(BASE_URL, request);
  return response.data;
}

/**
 * 更新数据规则
 */
export async function updateDataRule(
  id: number,
  request: UpdateDataRuleRequest
): Promise<ApiResponse<DataRule>> {
  const response = await apiClient.put<ApiResponse<DataRule>>(`${BASE_URL}/${id}`, request);
  return response.data;
}

/**
 * 删除数据规则
 */
export async function deleteDataRule(id: number): Promise<ApiResponse> {
  const response = await apiClient.delete<ApiResponse>(`${BASE_URL}/${id}`);
  return response.data;
}

// ── 导出 API 对象 ──────────────────────────────────────────────────────────────

export const dataRuleApi = {
  getDataRules,
  getDataRule,
  createDataRule,
  updateDataRule,
  deleteDataRule,
};

export default dataRuleApi;
