// 客户管理相关类型定义

// 客户信息DTO（来自后端）
export interface CustomerDto {
  id: string; // 后端是long，前端转为string
  name: string;
  code: string;
  appCode: string;
  contact?: string | null;
  phone?: string | null;
  address?: string | null;
  status: string;
  createdAt: string;
  deviceCount: number;
}

// 客户详情DTO（扩展自CustomerDto）
export interface CustomerDetailDto extends CustomerDto {
  projectCount: number;
}

// 创建客户请求DTO（来自后端）
export interface CreateCustomerRequest {
  name: string;
  code: string;
  appCode: string;
  contact?: string | null;
  phone?: string | null;
  address?: string | null;
}

// 更新客户请求DTO（来自后端）
export interface UpdateCustomerRequest {
  name: string;
  contact?: string | null;
  phone?: string | null;
  address?: string | null;
  status: string;
}

// 客户统计信息
export interface CustomerStatsDto {
  customerId: string;
  customerName: string;
  totalDevices: number;
  activeDevices: number;
  offlineDevices: number;
  totalAlerts: number;
  criticalAlerts: number;
  warningAlerts: number;
  totalEnergyConsumption: number;
  lastUpdated: string;
}

// 客户设备统计
export interface CustomerDeviceStats {
  customerId: string;
  customerName: string;
  deviceCount: number;
  deviceStatusDistribution: {
    online: number;
    offline: number;
    warning: number;
    error: number;
  };
  deviceTypeDistribution: Record<string, number>;
}

// 客户过滤参数
export interface CustomerFilters {
  keyword?: string;
}

// 客户状态枚举
export enum CustomerStatus {
  ACTIVE = 'active',
  INACTIVE = 'inactive',
  PENDING = 'pending',
  SUSPENDED = 'suspended'
}

// 客户列表响应（使用通用的PagedResponse）
export type CustomerListResponse = PagedResponse<CustomerDto>;