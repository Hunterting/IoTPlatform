// 设备管理相关类型定义

// 设备信息
export interface DeviceDto {
  id: string; // 后端是long，前端转为string
  appCode: string;
  name: string;
  model?: string | null;
  serialNumber?: string | null;
  category?: string | null;
  location?: string | null;
  areaId?: string | null; // 后端是long?，前端转为string | null
  areaName?: string | null;
  projectId?: string | null; // 后端是long?，前端转为string | null
  projectName?: string | null;
  energyTypes?: string | null;
  status: string;
  installDate?: string | null;
  lastMaintenance?: string | null;
  supplier?: string | null;
  warrantyDate?: string | null;
  power?: number | null;
  voltage?: string | null;
  meterInstalled: boolean;
  createdAt: string;
  updatedAt: string;
}

// 设备传感器信息
export interface DeviceSensorDto {
  id: string; // 后端是long，前端转为string
  deviceId: string; // 后端是long，前端转为string
  name: string;
  sensorType?: string | null;
  lastValue?: string | null;
  unit?: string | null;
}

// 设备详情
export interface DeviceDetailDto extends DeviceDto {
  sensors?: DeviceSensorDto[] | null;
}

// 创建设备请求
export interface CreateDeviceRequest {
  appCode: string;
  name: string;
  model?: string | null;
  serialNumber?: string | null;
  category?: string | null;
  location?: string | null;
  areaId?: number | null;
  projectId?: number | null;
  energyTypes?: string | null;
  status?: string;
  installDate?: string | null;
  lastMaintenance?: string | null;
  supplier?: string | null;
  warrantyDate?: string | null;
  power?: number | null;
  voltage?: string | null;
  meterInstalled?: boolean;
}

// 更新设备请求
export interface UpdateDeviceRequest {
  name?: string;
  model?: string | null;
  serialNumber?: string | null;
  category?: string | null;
  location?: string | null;
  areaId?: number | null;
  projectId?: number | null;
  energyTypes?: string | null;
  status?: string;
  installDate?: string | null;
  lastMaintenance?: string | null;
  supplier?: string | null;
  warrantyDate?: string | null;
  power?: number | null;
  voltage?: string | null;
  meterInstalled?: boolean;
}

// 设备过滤参数
export interface DeviceFilters {
  keyword?: string;
  status?: string;
  category?: string;
  areaId?: string;
  projectId?: string;
  appCode?: string;
}

// 设备状态枚举
export enum DeviceStatus {
  ONLINE = 'online',
  OFFLINE = 'offline',
  WARNING = 'warning',
  ERROR = 'error',
  MAINTENANCE = 'maintenance'
}

// 设备分类枚举
export enum DeviceCategory {
  POWER = 'power',
  LIGHTING = 'lighting',
  HVAC = 'hvac',
  SECURITY = 'security',
  NETWORK = 'network',
  OTHER = 'other'
}

// 设备列表响应
export interface DeviceListResponse {
  items: DeviceDto[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

// 设备统计信息
export interface DeviceStatsDto {
  totalDevices: number;
  onlineDevices: number;
  offlineDevices: number;
  warningDevices: number;
  errorDevices: number;
  maintenanceDevices: number;
  byCategory: Record<string, number>;
  byStatus: Record<string, number>;
  byArea: Array<{
    areaId: string;
    areaName: string;
    deviceCount: number;
    onlineCount: number;
  }>;
}

// 设备实时数据
export interface DeviceRealtimeData {
  deviceId: string;
  deviceName: string;
  timestamp: string;
  sensors: Array<{
    sensorId: string;
    sensorName: string;
    sensorType: string;
    value: number;
    unit: string;
    status: string;
  }>;
}

// 设备历史数据查询参数
export interface DeviceHistoryQueryParams {
  deviceId: string;
  sensorId?: string;
  startTime: string;
  endTime: string;
  interval?: string; // '1m', '5m', '15m', '1h', '1d'
  limit?: number;
}

// 设备历史数据点
export interface DeviceHistoryDataPoint {
  timestamp: string;
  value: number;
  unit: string;
  status: string;
}

// 设备报警信息
export interface DeviceAlertDto {
  id: string;
  deviceId: string;
  deviceName: string;
  alertType: string;
  severity: string;
  message: string;
  timestamp: string;
  acknowledged: boolean;
  acknowledgedBy?: string;
  acknowledgedAt?: string;
}

// 设备操作日志
export interface DeviceOperationLog {
  id: string;
  deviceId: string;
  deviceName: string;
  operationType: string;
  operator: string;
  details: string;
  timestamp: string;
}