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
  AnShengSwitchActionRequest,
  AnShengSwitchActionsRequest,
  AnShengStartDelayTaskRequest,
  AnShengStopDelayTaskRequest,
  AnShengSwitchResultDto,
  AnShengDelayTaskDto,
  AnShengDelayTaskResultDto,
  AnShengDeviceProfileDto,
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

  /**
   * 读取设备能力档案（含插槽数量与最近一次插槽通断快照）。
   * GET /api/v1/ansheng/{deviceId}/profile
   *
   * 【T9 为何需要它】开关矩阵的「当前通断态」权威落点是 Profile.SlotsSnapshot；
   * delay-tasks 端点只返回延时任务配置镜像，并不含通断态。
   */
  getProfile: async (
    deviceId: number
  ): Promise<AxiosResponse<ApiResponse<AnShengDeviceProfileDto>>> => {
    return httpClient.get<ApiResponse<AnShengDeviceProfileDto>>(
      `/ansheng/${deviceId}/profile`
    );
  },

  // ── T9：开关动作 + 延时任务（后端 AnShengSwitchController）────────
  //
  // 五个方法一一对应五个真实端点，全部走官方协议 action / actions /
  // getDelayTasks / startDelayTask / stopDelayTask，**不得**重新引入
  // setSwitch / getSwitchStatus / setSwitchConfig / getSwitchConfig 伪命令。
  //
  // 【响应信封】ApiResponse<T> = { code, message, data, timestamp }。
  // 业务失败（含喇叭类被拒）HTTP 状态恒为 200，靠 code=400 表达，
  // 机器可读原因在 data.rejectReason（字符串枚举，如 "RejectedByKind"）。

  /**
   * 单插槽开关动作。
   * POST /api/v1/ansheng/{deviceId}/action
   */
  switchAction: async (
    deviceId: number,
    request: AnShengSwitchActionRequest
  ): Promise<AxiosResponse<ApiResponse<AnShengSwitchResultDto>>> => {
    return httpClient.post<ApiResponse<AnShengSwitchResultDto>>(
      `/ansheng/${deviceId}/action`,
      request
    );
  },

  /**
   * 多插槽批量开关动作。
   * POST /api/v1/ansheng/{deviceId}/actions
   *
   * request.slotNums 必须是整数数组（如 [1,3]），绝不能是逗号串。
   */
  switchActions: async (
    deviceId: number,
    request: AnShengSwitchActionsRequest
  ): Promise<AxiosResponse<ApiResponse<AnShengSwitchResultDto>>> => {
    return httpClient.post<ApiResponse<AnShengSwitchResultDto>>(
      `/ansheng/${deviceId}/actions`,
      request
    );
  },

  /**
   * 读取延时任务镜像（平台侧视图，按插槽升序）。
   * GET /api/v1/ansheng/{deviceId}/delay-tasks
   */
  getDelayTasks: async (
    deviceId: number
  ): Promise<AxiosResponse<ApiResponse<AnShengDelayTaskDto[]>>> => {
    return httpClient.get<ApiResponse<AnShengDelayTaskDto[]>>(
      `/ansheng/${deviceId}/delay-tasks`
    );
  },

  /**
   * 开始 / 配置某插槽的延时任务。
   * POST /api/v1/ansheng/{deviceId}/delay-tasks/start
   */
  startDelayTask: async (
    deviceId: number,
    request: AnShengStartDelayTaskRequest
  ): Promise<AxiosResponse<ApiResponse<AnShengDelayTaskResultDto>>> => {
    return httpClient.post<ApiResponse<AnShengDelayTaskResultDto>>(
      `/ansheng/${deviceId}/delay-tasks/start`,
      request
    );
  },

  /**
   * 停止某插槽的延时任务。
   * POST /api/v1/ansheng/{deviceId}/delay-tasks/stop
   */
  stopDelayTask: async (
    deviceId: number,
    request: AnShengStopDelayTaskRequest
  ): Promise<AxiosResponse<ApiResponse<AnShengDelayTaskResultDto>>> => {
    return httpClient.post<ApiResponse<AnShengDelayTaskResultDto>>(
      `/ansheng/${deviceId}/delay-tasks/stop`,
      request
    );
  },
};

export default anshengApi;
