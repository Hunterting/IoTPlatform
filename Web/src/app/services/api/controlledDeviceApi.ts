import { httpClient } from './httpClient';
import { AxiosResponse } from 'axios';
import {
  ApiResponse,
  PagedResponse,
} from './types';
import {
  ControlledDeviceDto,
  RegisterControlledDeviceRequest,
  BatchRegisterControlledDeviceRequest,
  UpdateControlledDeviceRequest,
  ControlledDeviceQueryParams,
} from './types/controlledDevice.types';

/**
 * 受控设备 API 服务
 * 用于管理已添加到指令控制系统的设备
 */
export const controlledDeviceApi = {
  /**
   * 注册设备到控制系统
   */
  registerDevice: async (
    request: RegisterControlledDeviceRequest
  ): Promise<AxiosResponse<ApiResponse<ControlledDeviceDto>>> => {
    return httpClient.post<ApiResponse<ControlledDeviceDto>>(
      '/controlled-devices/register',
      request
    );
  },

  /**
   * 批量注册设备
   */
  registerDevices: async (
    request: BatchRegisterControlledDeviceRequest
  ): Promise<AxiosResponse<ApiResponse<ControlledDeviceDto[]>>> => {
    return httpClient.post<ApiResponse<ControlledDeviceDto[]>>(
      '/controlled-devices/register/batch',
      request
    );
  },

  /**
   * 取消注册设备
   */
  unregisterDevice: async (
    id: number
  ): Promise<AxiosResponse<ApiResponse<void>>> => {
    return httpClient.delete<ApiResponse<void>>(
      `/controlled-devices/${id}`
    );
  },

  /**
   * 获取受控设备列表
   */
  getDevices: async (
    params: ControlledDeviceQueryParams = {}
  ): Promise<AxiosResponse<ApiResponse<PagedResponse<ControlledDeviceDto>>>> => {
    return httpClient.get<ApiResponse<PagedResponse<ControlledDeviceDto>>>(
      '/controlled-devices',
      { params }
    );
  },

  /**
   * 获取单个受控设备
   */
  getDevice: async (
    id: number
  ): Promise<AxiosResponse<ApiResponse<ControlledDeviceDto>>> => {
    return httpClient.get<ApiResponse<ControlledDeviceDto>>(
      `/controlled-devices/${id}`
    );
  },

  /**
   * 更新受控设备
   */
  updateDevice: async (
    id: number,
    request: UpdateControlledDeviceRequest
  ): Promise<AxiosResponse<ApiResponse<ControlledDeviceDto>>> => {
    return httpClient.put<ApiResponse<ControlledDeviceDto>>(
      `/controlled-devices/${id}`,
      request
    );
  },

  /**
   * 切换收藏状态
   */
  toggleFavorite: async (
    id: number
  ): Promise<AxiosResponse<ApiResponse<boolean>>> => {
    return httpClient.post<ApiResponse<boolean>>(
      `/controlled-devices/${id}/toggle-favorite`
    );
  },

  /**
   * 检查设备是否已注册
   */
  checkDeviceRegistered: async (
    deviceId: number
  ): Promise<AxiosResponse<ApiResponse<boolean>>> => {
    return httpClient.get<ApiResponse<boolean>>(
      `/controlled-devices/check/${deviceId}`
    );
  },
};

export default controlledDeviceApi;
