// 区域管理相关类型定义

// 区域信息
export interface AreaDto {
  id: string; // 后端是long，前端转为string
  name: string;
  type: string;
  image?: string | null;
  parentId?: string | null; // 后端是long?，前端转为string | null
  parentName?: string | null;
  customerId?: string | null; // 后端是long?，前端转为string | null
  customerName?: string | null;
  appCode?: string | null;
  description?: string | null;
  deviceCount: number;
  sortOrder: number;
  createdAt: string;
  updatedAt: string;
}

// 区域树节点
export interface AreaTreeNodeDto {
  id: string; // 后端是long，前端转为string
  name: string;
  type: string;
  parentId?: string | null; // 后端是long?，前端转为string | null
  deviceCount: number;
  children?: AreaTreeNodeDto[] | null;
}

// 创建区域请求
export interface CreateAreaRequest {
  name: string;
  type: string;
  image?: string | null;
  parentId?: number | null;
  customerId?: number | null;
  appCode?: string | null;
  description?: string | null;
  sortOrder?: number;
}

// 更新区域请求
export interface UpdateAreaRequest {
  name?: string;
  type?: string;
  image?: string | null;
  parentId?: number | null;
  customerId?: number | null;
  appCode?: string | null;
  description?: string | null;
  sortOrder?: number;
}

// 区域过滤参数
export interface AreaFilters {
  keyword?: string;
  type?: string;
  parentId?: string;
  customerId?: string;
  appCode?: string;
}

// 区域类型枚举
export enum AreaType {
  BUILDING = 'building',
  FLOOR = 'floor',
  ROOM = 'room',
  AREA = 'area',
  ZONE = 'zone',
  REGION = 'region'
}

// 区域列表响应
export interface AreaListResponse {
  items: AreaDto[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

// 区域树响应
export interface AreaTreeResponse {
  tree: AreaTreeNodeDto[];
  flatList: AreaDto[];
}

// 区域统计信息
export interface AreaStatsDto {
  areaId: string;
  areaName: string;
  totalDevices: number;
  onlineDevices: number;
  offlineDevices: number;
  warningDevices: number;
  errorDevices: number;
  deviceCategories: Record<string, number>;
  energyConsumption: number;
  lastUpdated: string;
}

// 区域设备分布
export interface AreaDeviceDistribution {
  areaId: string;
  areaName: string;
  totalDevices: number;
  deviceStatus: {
    online: number;
    offline: number;
    warning: number;
    error: number;
  };
  deviceTypes: Record<string, number>;
}

// 区域详情响应
export interface AreaDetailResponse extends AreaDto {
  parentArea?: AreaDto | null;
  childAreas: AreaDto[];
  devices: Array<{
    id: string;
    name: string;
    model?: string | null;
    status: string;
    lastUpdated: string;
  }>;
  stats: {
    totalDevices: number;
    onlineDevices: number;
    offlineDevices: number;
    warningDevices: number;
    errorDevices: number;
  };
}

// 区域移动请求
export interface MoveAreaRequest {
  targetParentId?: number | null;
  sortOrder?: number;
}

// 区域批量操作请求
export interface BatchAreaOperationRequest {
  areaIds: number[];
  operation: 'delete' | 'enable' | 'disable' | 'move';
  targetParentId?: number | null;
}