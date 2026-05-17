/**
 * 受控设备相关类型定义
 */

/** 受控设备 DTO */
export interface ControlledDeviceDto {
  id: number;
  appCode?: string;
  deviceId: number;
  deviceName: string;
  serialNumber?: string;
  model?: string;
  category?: string;
  location?: string;
  remark?: string;
  priority: number;
  isEnabled: boolean;
  isFavorite: boolean;
  registeredAt: string;
  lastCommandAt?: string;
  commandCount: number;
  createdBy?: number;
  createdByName?: string;
  createdAt: string;
  updatedAt: string;
  deviceStatus?: string;
}

/** 注册受控设备请求 */
export interface RegisterControlledDeviceRequest {
  deviceId: number;
  remark?: string;
  priority?: number;
}

/** 批量注册受控设备请求 */
export interface BatchRegisterControlledDeviceRequest {
  deviceIds: number[];
}

/** 更新受控设备请求 */
export interface UpdateControlledDeviceRequest {
  remark?: string;
  priority?: number;
  isEnabled?: boolean;
  isFavorite?: boolean;
}

/** 受控设备查询参数 */
export interface ControlledDeviceQueryParams {
  page?: number;
  pageSize?: number;
  isEnabled?: boolean;
  isFavorite?: boolean;
}
