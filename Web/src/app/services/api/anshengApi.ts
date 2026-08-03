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

  // ── 二开设备命令 ──────────────────────────────────────────
  //
  // 说明：原 controlSwitch / getSwitchStatus / configureSwitch 三个方法分别指向
  //       /ansheng/{id}/switch、/switch-status、/switch-config，这些端点依赖官方协议
  //       asopen.md 中并不存在的伪命令，后端已于 T3 物理删除（调用必然 404），
  //       故一并移除。开关通断请改用 sendCommand({ method: 'action' | 'actions' })，
  //       状态查询请用 sendCommand({ method: 'getDevStatus', params: { q: 'slots' } })。

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
