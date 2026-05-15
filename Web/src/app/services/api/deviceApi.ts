import { httpClient } from './httpClient';
import { AxiosResponse } from 'axios';
import { 
  ApiResponse, 
  PagedResponse 
} from './types';
import {
  DeviceDto,
  DeviceDetailDto,
  CreateDeviceRequest,
  UpdateDeviceRequest,
  DeviceFilters,
  DeviceStatus,
  DeviceSensorDto
} from './types/device.types';

/**
 * 设备管理API服务
 */
export const deviceApi = {
  /**
   * 获取设备列表（分页）
   */
  getDevices: async (
    page: number = 1,
    pageSize: number = 20,
    filters?: DeviceFilters
  ): Promise<AxiosResponse<ApiResponse<PagedResponse<DeviceDto>>>> => {
    const params: Record<string, any> = {
      page,
      pageSize
    };

    if (filters?.keyword) {
      params.keyword = filters.keyword;
    }
    if (filters?.status) {
      params.status = filters.status;
    }
    if (filters?.areaId) {
      params.areaId = filters.areaId;
    }
    if (filters?.projectId) {
      params.projectId = filters.projectId;
    }
    if (filters?.category) {
      params.category = filters.category;
    }

    return httpClient.get<ApiResponse<PagedResponse<DeviceDto>>>('/devices', { params });
  },

  /**
   * 获取单个设备详情
   */
  getDevice: async (id: string): Promise<AxiosResponse<ApiResponse<DeviceDto>>> => {
    return httpClient.get<ApiResponse<DeviceDto>>(`/devices/${id}`);
  },

  /**
   * 获取设备详情（包含传感器）
   */
  getDeviceDetail: async (id: string): Promise<AxiosResponse<ApiResponse<DeviceDetailDto>>> => {
    return httpClient.get<ApiResponse<DeviceDetailDto>>(`/devices/${id}/detail`);
  },

  /**
   * 根据区域获取设备列表
   */
  getDevicesByArea: async (areaId: string): Promise<AxiosResponse<ApiResponse<DeviceDto[]>>> => {
    return httpClient.get<ApiResponse<DeviceDto[]>>(`/devices/area/${areaId}`);
  },

  /**
   * 创建设备
   */
  createDevice: async (data: CreateDeviceRequest): Promise<AxiosResponse<ApiResponse<DeviceDto>>> => {
    return httpClient.post<ApiResponse<DeviceDto>>('/devices', data);
  },

  /**
   * 更新设备
   */
  updateDevice: async (
    id: string,
    data: UpdateDeviceRequest
  ): Promise<AxiosResponse<ApiResponse<DeviceDto>>> => {
    return httpClient.put<ApiResponse<DeviceDto>>(`/devices/${id}`, data);
  },

  /**
   * 删除设备
   */
  deleteDevice: async (id: string): Promise<AxiosResponse<ApiResponse>> => {
    return httpClient.delete<ApiResponse>(`/devices/${id}`);
  },

  /**
   * 获取设备统计数据
   */
  getDeviceStats: async (): Promise<AxiosResponse<ApiResponse<{
    total: number;
    online: number;
    offline: number;
    warning: number;
    byCategory: Record<string, number>;
    byStatus: Record<string, number>;
  }>>> => {
    return httpClient.get<ApiResponse<any>>('/devices/stats');
  },

  /**
   * 获取设备实时数据
   */
  getDeviceRealTimeData: async (deviceId: string): Promise<AxiosResponse<ApiResponse<{
    deviceId: string;
    deviceName: string;
    lastUpdated: string;
    sensors: Array<{
      sensorId: string;
      sensorName: string;
      sensorType: string;
      value: string;
      unit: string;
      timestamp: string;
    }>;
  }>>> => {
    return httpClient.get<ApiResponse<any>>(`/devices/${deviceId}/realtime`);
  },

  /**
   * 获取设备传感器列表
   */
  getDeviceSensors: async (deviceId: string): Promise<AxiosResponse<ApiResponse<DeviceSensorDto[]>>> => {
    return httpClient.get<ApiResponse<DeviceSensorDto[]>>(`/devices/${deviceId}/sensors`);
  },

  /**
   * 批量更新设备状态
   */
  batchUpdateDeviceStatus: async (
    deviceIds: string[],
    status: DeviceStatus
  ): Promise<AxiosResponse<ApiResponse>> => {
    return httpClient.post<ApiResponse>('/devices/batch-update-status', {
      deviceIds,
      status
    });
  }
};

export default deviceApi;