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
  AnShengSetTimeTasksRequest,
  AnShengSetSlotTimeTasksRequest,
  AnShengSlotTimeTaskSetDto,
  AnShengTimeTaskResultDto,
  AnShengGetEMStatisticsRequest,
  AnShengClearEMStatisticsRequest,
  AnShengSetCalParamsRequest,
  AnShengAutoCalRequest,
  AnShengEnergyStatisticsQueryParams,
  AnShengEnergyResultDto,
  AnShengEmStatisticDto,
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

  // ── T10：定时任务（后端 AnShengScheduleController）────────────────
  //
  // 四个方法一一对应四个真实端点，全部走官方协议 getTimeTasks / setTimeTasks /
  // getSlotTimeTasks / setSlotTimeTasks。
  //
  // 【响应信封】同 T9：业务失败 HTTP 恒 200 + code=400，原因在 data.rejectReason。
  // 【唯一例外】乐观并发冲突时控制器返回**真正的 HTTP 409**（带信封体），
  //   axios 会抛异常，调用方必须在 catch 里判定，详见页面 ScheduleEditorPage。
  // 【写端点权限】POST 两个端点在后端标了 SEND_DEVICE_COMMANDS，
  //   GET 两个端点走类级 VIEW_DEVICES。

  /**
   * 读取全部插槽的定时任务镜像（平台侧视图，按插槽升序）。
   * GET /api/v1/ansheng/{deviceId}/time-tasks
   */
  getTimeTasks: async (
    deviceId: number
  ): Promise<AxiosResponse<ApiResponse<AnShengSlotTimeTaskSetDto[]>>> => {
    return httpClient.get<ApiResponse<AnShengSlotTimeTaskSetDto[]>>(
      `/ansheng/${deviceId}/time-tasks`
    );
  },

  /**
   * 整表覆盖定时任务（未列出的插槽会被清空）。
   * POST /api/v1/ansheng/{deviceId}/time-tasks
   *
   * request.confirm 必须为 true，否则后端以 RejectedByConfirm 拒绝且命令零出网。
   */
  setTimeTasks: async (
    deviceId: number,
    request: AnShengSetTimeTasksRequest
  ): Promise<AxiosResponse<ApiResponse<AnShengTimeTaskResultDto>>> => {
    return httpClient.post<ApiResponse<AnShengTimeTaskResultDto>>(
      `/ansheng/${deviceId}/time-tasks`,
      request
    );
  },

  /**
   * 读取单个插槽的定时任务镜像；该插槽无任务时 data 为 null。
   * GET /api/v1/ansheng/{deviceId}/time-tasks/{slotNum}
   */
  getSlotTimeTasks: async (
    deviceId: number,
    slotNum: number
  ): Promise<AxiosResponse<ApiResponse<AnShengSlotTimeTaskSetDto | null>>> => {
    return httpClient.get<ApiResponse<AnShengSlotTimeTaskSetDto | null>>(
      `/ansheng/${deviceId}/time-tasks/${slotNum}`
    );
  },

  /**
   * 设置单个插槽的定时任务。
   * POST /api/v1/ansheng/{deviceId}/time-tasks/{slotNum}
   *
   * slotNum 走**路由段**而非请求体，与后端 AnShengSetSlotTimeTasksRequest 保持一致。
   */
  setSlotTimeTasks: async (
    deviceId: number,
    slotNum: number,
    request: AnShengSetSlotTimeTasksRequest
  ): Promise<AxiosResponse<ApiResponse<AnShengTimeTaskResultDto>>> => {
    return httpClient.post<ApiResponse<AnShengTimeTaskResultDto>>(
      `/ansheng/${deviceId}/time-tasks/${slotNum}`,
      request
    );
  },

  // ── T11：电量计（后端 AnShengEnergyController）────────────────────
  //
  // 八个方法一一对应八个真实端点。**全部结果走 HTTP 200**：
  // 成功 code=200，被拒 code=400 + data.rejectReason（喇叭类为 "RejectedByKind"），
  // 电量计没有乐观并发令牌，因此不会出现 409。
  //
  // 【权限】除 GET /energy/statistics 走类级 VIEW_DEVICES 外，
  //   其余七个（含 GET /energy/cal-params，因为它其实是一次命令下发）均需 SEND_DEVICE_COMMANDS。

  /**
   * 下发 getEMRealtime，拉取电量计实时读数。
   * POST /api/v1/ansheng/{deviceId}/energy/realtime
   *
   * 应答归一化入 DeviceDataRecord，本端点只回下发受理情况，真值需另行查数据曲线。
   */
  requestEnergyRealtime: async (
    deviceId: number
  ): Promise<AxiosResponse<ApiResponse<AnShengEnergyResultDto>>> => {
    return httpClient.post<ApiResponse<AnShengEnergyResultDto>>(
      `/ansheng/${deviceId}/energy/realtime`
    );
  },

  /**
   * 下发 getEMStatistics，拉取电量计统计。
   * POST /api/v1/ansheng/{deviceId}/energy/statistics/refresh
   *
   * 应答按唯一键 (deviceId, slotNum, granularity, periodKey) 幂等 UPSERT 进聚合表；
   * 前端应延时后再调 getEnergyStatistics 取真值。
   */
  refreshEnergyStatistics: async (
    deviceId: number,
    request: AnShengGetEMStatisticsRequest = {}
  ): Promise<AxiosResponse<ApiResponse<AnShengEnergyResultDto>>> => {
    return httpClient.post<ApiResponse<AnShengEnergyResultDto>>(
      `/ansheng/${deviceId}/energy/statistics/refresh`,
      request
    );
  },

  /**
   * 下发 clearEMStatistics，清空**设备侧**统计（平台聚合表一行不删）。
   * POST /api/v1/ansheng/{deviceId}/energy/statistics/clear
   *
   * request.confirm 必须为 true，否则后端直接业务拒绝且命令零出网。
   */
  clearEnergyStatistics: async (
    deviceId: number,
    request: AnShengClearEMStatisticsRequest
  ): Promise<AxiosResponse<ApiResponse<AnShengEnergyResultDto>>> => {
    return httpClient.post<ApiResponse<AnShengEnergyResultDto>>(
      `/ansheng/${deviceId}/energy/statistics/clear`,
      request
    );
  },

  /**
   * 读取平台电量计统计聚合表（设备权威镜像，平台只累积保留）。
   * GET /api/v1/ansheng/{deviceId}/energy/statistics?slotNum=&granularity=
   */
  getEnergyStatistics: async (
    deviceId: number,
    params: AnShengEnergyStatisticsQueryParams = {}
  ): Promise<AxiosResponse<ApiResponse<AnShengEmStatisticDto[]>>> => {
    return httpClient.get<ApiResponse<AnShengEmStatisticDto[]>>(
      `/ansheng/${deviceId}/energy/statistics`,
      { params }
    );
  },

  /**
   * 下发 getCalParams，读取校准参数。仅开关类设备放行（喇叭类 RejectedByKind）。
   * GET /api/v1/ansheng/{deviceId}/energy/cal-params
   *
   * 【注意】它虽是 GET，但语义是一次命令下发，后端要求 SEND_DEVICE_COMMANDS 权限。
   */
  getCalParams: async (
    deviceId: number
  ): Promise<AxiosResponse<ApiResponse<AnShengEnergyResultDto>>> => {
    return httpClient.get<ApiResponse<AnShengEnergyResultDto>>(
      `/ansheng/${deviceId}/energy/cal-params`
    );
  },

  /**
   * 下发 setCalParams，设置校准参数（至少需含 RL）。仅开关类放行。
   * POST /api/v1/ansheng/{deviceId}/energy/cal-params
   */
  setCalParams: async (
    deviceId: number,
    request: AnShengSetCalParamsRequest
  ): Promise<AxiosResponse<ApiResponse<AnShengEnergyResultDto>>> => {
    return httpClient.post<ApiResponse<AnShengEnergyResultDto>>(
      `/ansheng/${deviceId}/energy/cal-params`,
      request
    );
  },

  /**
   * 下发 resetCalParams，重置校准参数。仅开关类放行。
   * POST /api/v1/ansheng/{deviceId}/energy/cal-params/reset
   */
  resetCalParams: async (
    deviceId: number
  ): Promise<AxiosResponse<ApiResponse<AnShengEnergyResultDto>>> => {
    return httpClient.post<ApiResponse<AnShengEnergyResultDto>>(
      `/ansheng/${deviceId}/energy/cal-params/reset`
    );
  },

  /**
   * 下发 autoCal，按已知负载功率自动校准。仅开关类放行。
   * POST /api/v1/ansheng/{deviceId}/energy/cal-params/auto
   */
  autoCalParams: async (
    deviceId: number,
    request: AnShengAutoCalRequest
  ): Promise<AxiosResponse<ApiResponse<AnShengEnergyResultDto>>> => {
    return httpClient.post<ApiResponse<AnShengEnergyResultDto>>(
      `/ansheng/${deviceId}/energy/cal-params/auto`,
      request
    );
  },
};

export default anshengApi;
