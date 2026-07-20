import { httpClient } from './httpClient';
import { AxiosResponse } from 'axios';
import { ApiResponse } from './types';
import type {
  DiscoveredDeviceListResponse,
  ClaimAnShengDeviceRequest,
  ClaimAnShengDeviceResponse,
  AnShengCommandRequest,
  AnShengCommandResponse,
  AnShengAutoReportRequest,
  DiscoveredDeviceQueryParams,
  SwitchControlRequest,
  SwitchConfigRequest,
} from './types/ansheng.types';

/**
 * 安圣 MQTT 设备管理 API 服务
 */
export const anshengApi = {
  /**
   * 获取待认领设备列表（分页）
   */
  getDiscoveredDevices: async (
    params: DiscoveredDeviceQueryParams = {}
  ): Promise<AxiosResponse<ApiResponse<DiscoveredDeviceListResponse>>> => {
    return httpClient.get<ApiResponse<DiscoveredDeviceListResponse>>('/ansheng/discovered', { params });
  },

  /**
   * 触发设备发现扫描
   */
  triggerDiscovery: async (): Promise<AxiosResponse<ApiResponse<void>>> => {
    return httpClient.post<ApiResponse<void>>('/ansheng/discover');
  },

  /**
   * 认领设备（创建正式设备记录）
   */
  claimDevice: async (
    request: ClaimAnShengDeviceRequest
  ): Promise<AxiosResponse<ApiResponse<ClaimAnShengDeviceResponse>>> => {
    return httpClient.post<ApiResponse<ClaimAnShengDeviceResponse>>('/ansheng/claim', request);
  },

  /**
   * 向安圣设备下发命令
   */
  sendCommand: async (
    request: AnShengCommandRequest
  ): Promise<AxiosResponse<ApiResponse<AnShengCommandResponse>>> => {
    return httpClient.post<ApiResponse<AnShengCommandResponse>>(
      `/ansheng/${request.deviceId}/command`,
      { method: request.method, params: request.params }
    );
  },

  /**
   * 配置设备自动上报
   */
  configureAutoReport: async (
    request: AnShengAutoReportRequest
  ): Promise<AxiosResponse<ApiResponse<AnShengCommandResponse>>> => {
    return httpClient.post<ApiResponse<AnShengCommandResponse>>(
      `/ansheng/${request.deviceId}/auto-report`,
      request
    );
  },

  // ── 二开设备开关命令 ──────────────────────────────────────

  /** 控制开关通断 */
  controlSwitch: async (
    request: SwitchControlRequest
  ): Promise<AxiosResponse<ApiResponse<AnShengCommandResponse>>> => {
    return httpClient.post<ApiResponse<AnShengCommandResponse>>(
      `/ansheng/${request.deviceId}/switch`,
      { switchId: request.switchId, on: request.on }
    );
  },

  /** 查询开关状态 */
  getSwitchStatus: async (
    deviceId: number,
    switchId?: number
  ): Promise<AxiosResponse<ApiResponse<AnShengCommandResponse>>> => {
    return httpClient.get<ApiResponse<AnShengCommandResponse>>(
      `/ansheng/${deviceId}/switch-status`,
      { params: { switchId } }
    );
  },

  /** 配置开关参数 */
  configureSwitch: async (
    request: SwitchConfigRequest
  ): Promise<AxiosResponse<ApiResponse<AnShengCommandResponse>>> => {
    return httpClient.post<ApiResponse<AnShengCommandResponse>>(
      `/ansheng/${request.deviceId}/switch-config`,
      { switchId: request.switchId, config: request.config }
    );
  },

  /** 远程重启设备 */
  rebootDevice: async (
    deviceId: number
  ): Promise<AxiosResponse<ApiResponse<AnShengCommandResponse>>> => {
    return httpClient.post<ApiResponse<AnShengCommandResponse>>(
      `/ansheng/${deviceId}/reboot`
    );
  },
};

export default anshengApi;
