import { httpClient as apiClient } from './httpClient';

// ── 类型定义（与后端 WorkOrderResponse.cs camelCase 出网对齐） ──────────────────

export interface ApiResponse<T = unknown> {
  code: number;
  data?: T;
  message?: string;
}

/** 真实工单 */
export interface WorkOrder {
  id: number;
  orderNo: string;
  title: string;
  type: string;               // fault / maintenance / inspection / installation / other
  priority: string;           // low / normal / high / urgent
  status: string;             // pending / assigned / in_progress / resolved / closed / rejected
  customerId?: number | null;
  customerName?: string | null;
  deviceId?: number | null;
  areaId?: number | null;
  deviceName?: string | null;
  deviceCode?: string | null;
  areaName?: string | null;
  description?: string | null;
  reporter?: string | null;
  reportTime: string;         // "yyyy-MM-dd HH:mm:ss"
  assignee?: string | null;
  assignTime?: string | null;
  estimatedTime?: string | null;
  resolvedTime?: string | null;
  closedTime?: string | null;
  resolveDescription?: string | null;
  projectName?: string | null;
  appCode?: string | null;    // 区分租户
  createdAt: string;
  updatedAt: string;
}

/** 工单处理日志 */
export interface WorkOrderLog {
  id: number;
  workOrderId: number;
  operator?: string | null;
  action: string;
  comment?: string | null;
  createdAt: string;
}

export interface PagedResponse<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export interface WorkOrderListParams {
  page?: number;
  pageSize?: number;
  status?: string;
  type?: string;
  priority?: string;
}

export interface CreateWorkOrderRequest {
  title: string;
  type: string;
  priority: string;
  customerId?: number | null;
  deviceId?: number | null;
  deviceName?: string | null;
  deviceCode?: string | null;
  areaId?: number | null;
  areaName?: string | null;
  description?: string | null;
  reporter?: string | null;
  estimatedTime?: string | null;
  projectName?: string | null;
  appCode?: string | null;    // 仅超管代租户创建时生效
}

const BASE_URL = '/work-orders';

/** 工单列表（后端按 JWT AppCode 区分租户；超管看全部） */
export async function getWorkOrders(
  params: WorkOrderListParams = {}
): Promise<ApiResponse<PagedResponse<WorkOrder>>> {
  const sp = new URLSearchParams();
  if (params.page) sp.set('page', String(params.page));
  if (params.pageSize) sp.set('pageSize', String(params.pageSize));
  if (params.status) sp.set('status', params.status);
  if (params.type) sp.set('type', params.type);
  if (params.priority) sp.set('priority', params.priority);
  const q = sp.toString();
  const r = await apiClient.get<ApiResponse<PagedResponse<WorkOrder>>>(`${BASE_URL}${q ? `?${q}` : ''}`);
  return r.data;
}

/** 工单详情 */
export async function getWorkOrder(id: number): Promise<ApiResponse<WorkOrder>> {
  const r = await apiClient.get<ApiResponse<WorkOrder>>(`${BASE_URL}/${id}`);
  return r.data;
}

/** 创建工单 */
export async function createWorkOrder(request: CreateWorkOrderRequest): Promise<ApiResponse<WorkOrder>> {
  const r = await apiClient.post<ApiResponse<WorkOrder>>(BASE_URL, request);
  return r.data;
}

/** 更新工单 */
export async function updateWorkOrder(id: number, title: string, priority: string, description?: string): Promise<ApiResponse<WorkOrder>> {
  const r = await apiClient.put<ApiResponse<WorkOrder>>(`${BASE_URL}/${id}`, { title, priority, description });
  return r.data;
}

/** 分配工单 */
export async function assignWorkOrder(id: number, assignee: string): Promise<ApiResponse<WorkOrder>> {
  const r = await apiClient.post<ApiResponse<WorkOrder>>(`${BASE_URL}/${id}/assign`, { assignee });
  return r.data;
}

/** 开始处理 */
export async function startWorkOrder(id: number): Promise<ApiResponse<WorkOrder>> {
  const r = await apiClient.post<ApiResponse<WorkOrder>>(`${BASE_URL}/${id}/start`, {});
  return r.data;
}

/** 解决工单 */
export async function resolveWorkOrder(id: number, resolveDescription: string): Promise<ApiResponse<WorkOrder>> {
  const r = await apiClient.post<ApiResponse<WorkOrder>>(`${BASE_URL}/${id}/resolve`, { resolveDescription });
  return r.data;
}

/** 关闭工单 */
export async function closeWorkOrder(id: number): Promise<ApiResponse<WorkOrder>> {
  const r = await apiClient.post<ApiResponse<WorkOrder>>(`${BASE_URL}/${id}/close`, {});
  return r.data;
}

/** 驳回工单 */
export async function rejectWorkOrder(id: number, reason: string): Promise<ApiResponse<WorkOrder>> {
  const r = await apiClient.post<ApiResponse<WorkOrder>>(`${BASE_URL}/${id}/reject`, { reason });
  return r.data;
}

/** 处理记录 */
export async function getWorkOrderLogs(id: number): Promise<ApiResponse<WorkOrderLog[]>> {
  const r = await apiClient.get<ApiResponse<WorkOrderLog[]>>(`${BASE_URL}/${id}/logs`);
  return r.data;
}

export const workOrderApi = {
  getWorkOrders,
  getWorkOrder,
  createWorkOrder,
  updateWorkOrder,
  assignWorkOrder,
  startWorkOrder,
  resolveWorkOrder,
  closeWorkOrder,
  rejectWorkOrder,
  getWorkOrderLogs,
};

export default workOrderApi;
