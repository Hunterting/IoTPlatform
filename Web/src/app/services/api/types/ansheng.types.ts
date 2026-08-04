// 安圣 MQTT 设备管理相关类型定义

// ── 安圣待认领设备 ──────────────────────────────────────────
export interface DiscoveredAnShengDeviceDto {
  id: number;
  imei: string;
  model?: string | null;
  netType?: string | null;
  isClaimed: boolean;
  firstSeenAt: string;
  lastSeenAt?: string | null;
  createdAt: string;
  updatedAt: string;
}

// ── 待认领设备列表分页响应 ──────────────────────────────────
export interface DiscoveredDeviceListResponse {
  items: DiscoveredAnShengDeviceDto[];
  totalCount: number;
  page: number;
  pageSize: number;
}

// ── 认领设备请求 ────────────────────────────────────────────
export interface ClaimAnShengDeviceRequest {
  discoveredDeviceId: number;
  deviceName: string;
  model?: string | null;
  category?: string | null;
  location?: string | null;
  energyTypes?: string | null;
  status?: string;
}

// ── 认领设备响应 ────────────────────────────────────────────
export interface ClaimAnShengDeviceResponse {
  success: boolean;
  deviceId?: number | null;
  deviceName?: string | null;
  errorMessage?: string | null;
}

// ── 安圣命令请求 ────────────────────────────────────────────
export interface AnShengCommandRequest {
  deviceId: number;
  method: string;
  params?: Record<string, unknown>;
}

// ── 安圣命令响应 ────────────────────────────────────────────
export interface AnShengCommandResponse {
  success: boolean;
  message?: string | null;
  frameId?: string | null;
}

// ── 自动上报配置请求 ────────────────────────────────────────
export interface AnShengAutoReportRequest {
  deviceId: number;
  getDevStatusSec: number;
  orderUpSec: number;
  orderEndSec?: number;
  orderStartSec?: number;
  emRealtimeSec?: number;
  rs485StatusSec?: number;
}

// ── 待认领设备查询参数 ──────────────────────────────────────
export interface DiscoveredDeviceQueryParams {
  page?: number;
  pageSize?: number;
  keyword?: string;
  claimed?: boolean;
}

// ── 已删除：二开设备「开关」伪命令类型 ──────────────────────
// SwitchControlRequest / SwitchConfigRequest / SwitchQueryParams 三个类型
// 服务于 setSwitch / getSwitchStatus / setSwitchConfig / getSwitchConfig 四个
// 官方协议（asopen.md）中并不存在的伪命令，后端端点已物理删除，故一并移除。
// 开关相关操作请改用官方 action / actions / getDevStatus 方法，
// 统一通过 AnShengCommandRequest 走 /ansheng/{deviceId}/command。

// ════════════════════════════════════════════════════════════════
// T9：安圣开关动作 + 延时任务（对应后端 AnShengSwitchController）
//
// 【字段名权威来源】
//   Controllers/AnShengSwitchController.cs
//   DTOs/Requests/AnShengRequests.cs
//   DTOs/Responses/AnShengResponses.cs
//   后端 MVC 使用 JsonSerializerDefaults.Web（camelCase），故 C# 的
//   SAction / EAction 出网为 sAction / eAction，此处逐一对应。
// ════════════════════════════════════════════════════════════════

/**
 * 命令被拒原因（机器可读）。
 *
 * 【为什么是字符串而不是数字】后端在 Program.cs 全局注册了 JsonStringEnumConverter，
 * 枚举一律以**字符串原名**出网（如 "RejectedByKind"）。前端断言/分支必须用字符串，
 * 不要假设它是整数。取值与 Models/AnShengCommandRecord.cs 的
 * AnShengCommandRejectReason 枚举成员名严格一致。
 */
export type AnShengCommandRejectReason =
  | 'RejectedByKind'
  | 'RejectedByValidation'
  | 'RejectedByFirmware'
  | 'RejectedByOffline'
  | 'RejectedByUnknownMethod'
  | 'RejectedByConfirm';

/** 开关动作。后端 AnShengCommandGuard 负责合法性判定，前端仅提供常用取值。 */
export type AnShengSwitchAction = 'on' | 'off' | 'toggle';

/** 延时任务的「开始动作」，比开关动作多一个 none（表示开始时不动作）。 */
export type AnShengDelayStartAction = AnShengSwitchAction | 'none';

// ── 请求 DTO ────────────────────────────────────────────────

/**
 * 单插槽开关动作请求。
 * 对应 POST /ansheng/{deviceId}/action，后端 DTO：AnShengActionRequest。
 */
export interface AnShengSwitchActionRequest {
  /** 插槽编号，从 1 开始；0 表示所有插槽。 */
  slotNum: number;
  /** 动作：on / off / toggle。 */
  action: AnShengSwitchAction;
  /** 是否同时停止该插槽的延时任务；省略时后端不下发该字段。 */
  hasStopDelayTask?: boolean;
}

/**
 * 多插槽批量开关动作请求。
 * 对应 POST /ansheng/{deviceId}/actions，后端 DTO：AnShengActionsRequest。
 *
 * 【slotNums 必须是整数数组】报文里是 JSON 数组（如 [1,3]）而非逗号串，
 * 这是 T8 验收 #2 的断言点，前端务必以 number[] 下发。
 */
export interface AnShengSwitchActionsRequest {
  /** 插槽编号数组，子项从 1 开始，非空（空数组会被后端以 code=400 拒绝）。 */
  slotNums: number[];
  /** 动作：on / off / toggle。 */
  action: AnShengSwitchAction;
  /** 是否同时停止延时任务；省略时后端不下发该字段。 */
  hasStopDelayTask?: boolean;
}

/**
 * 开始 / 配置延时任务请求。
 * 对应 POST /ansheng/{deviceId}/delay-tasks/start，后端 DTO：AnShengStartDelayTaskRequest。
 */
export interface AnShengStartDelayTaskRequest {
  /** 插槽编号，从 1 开始；0 表示所有插槽。 */
  slotNum: number;
  /** 是否启用。 */
  enable: boolean;
  /** 开始动作：on / off / toggle / none。 */
  sAction: AnShengDelayStartAction;
  /** 结束动作：on / off / toggle。 */
  eAction: AnShengSwitchAction;
  /** 延时秒数，> 0。 */
  secs: number;
}

/**
 * 停止延时任务请求。
 * 对应 POST /ansheng/{deviceId}/delay-tasks/stop，后端 DTO：AnShengStopDelayTaskRequest。
 */
export interface AnShengStopDelayTaskRequest {
  /** 插槽编号，从 1 开始。 */
  slotNum: number;
}

// ── 响应 DTO ────────────────────────────────────────────────

/**
 * 开关动作下发结果（action / actions 端点的 data）。
 * 对应后端 DTOs/Responses/AnShengResponses.cs 的 AnShengSwitchResultDto。
 */
export interface AnShengSwitchResultDto {
  /** 平台是否受理并下发了命令；被 Guard 拒收时为 false。 */
  accepted: boolean;
  /** 平台命令标识（GUID），被拒时为 null。 */
  commandId?: string | null;
  /** 安圣 FrameId，被拒（未出网）时为 null。 */
  frameId?: string | null;
  /** 机器可读拒绝原因；喇叭类设备为 "RejectedByKind"。 */
  rejectReason?: AnShengCommandRejectReason | null;
  /** 面向人的失败原因，勿用于程序分支判断。 */
  errorMessage?: string | null;
  /** 实际出网的 JSON 报文回显；被拒时为 null。 */
  payload?: string | null;
  /**
   * 当前插槽状态快照（0=关 1=开），下标 i 对应 slotNum = i + 1。
   * 来自 Profile.SlotsSnapshot，设备应答异步写回，可能为 null 或陈旧值。
   */
  slots?: number[] | null;
}

/**
 * 单个延时任务镜像。
 * 对应后端 AnShengDelayTaskDto。GET /delay-tasks 返回的是它的**数组**。
 */
export interface AnShengDelayTaskDto {
  /** 插槽编号，从 1 开始。 */
  slotNum: number;
  /** 是否启用。 */
  enable: boolean;
  /** 开始动作（on/off/toggle/none）。 */
  sAction: string;
  /** 结束动作（on/off/toggle）。 */
  eAction: string;
  /** 延时秒数。 */
  secs: number;
  /** 任务计数快照（非实时）。 */
  cnt: number;
  /** 镜像最后与设备同步的时刻（UTC ISO 串）。 */
  syncedAt: string;
  /** 是否陈旧：(UtcNow - syncedAt) > 24h。 */
  isStale: boolean;
}

/**
 * 延时任务下发结果（start / stop 端点的 data）。
 * 对应后端 AnShengDelayTaskResultDto。
 */
export interface AnShengDelayTaskResultDto {
  /** 平台是否受理并下发了命令。 */
  accepted: boolean;
  /** 平台命令标识（GUID），被拒时为 null。 */
  commandId?: string | null;
  /** 安圣 FrameId，被拒时为 null。 */
  frameId?: string | null;
  /** 机器可读拒绝原因。 */
  rejectReason?: AnShengCommandRejectReason | null;
  /** 面向人的失败原因。 */
  errorMessage?: string | null;
  /** 乐观镜像快照（立即返回，可能尚未反映本次下发）。 */
  tasks?: AnShengDelayTaskDto[] | null;
}

/**
 * 安圣设备能力档案只读视图（GET /ansheng/{deviceId}/profile）。
 * 对应后端 AnShengDeviceProfileDto。
 *
 * 【T9 为什么需要它】开关的「当前通断态」权威落点是 Profile.SlotsSnapshot，
 * 由设备应答异步写回；delay-tasks 端点只返回延时任务配置镜像，不含通断态。
 * 故插槽矩阵的初始状态与插槽数量均取自本档案。
 */
export interface AnShengDeviceProfileDto {
  /** 档案主键。 */
  id: number;
  /** 设备 IMEI。 */
  imei: string;
  /** 关联正式设备主键；认领前为 null。 */
  deviceId?: number | null;
  /** 设备品类枚举名（字符串出网，如 "Switch4G" / "Speaker4G"）。 */
  kind: string;
  /** 品类中文名。 */
  kindName: string;
  /** 品类来源枚举名。 */
  kindSource: string;
  /** 联网类型（4G / WiFi）。 */
  netType?: string | null;
  /** 插槽数量；未探测时为 null。 */
  slotAmount?: number | null;
  /** 相位数量。 */
  phaseAmount?: number | null;
  /** 固件版本号。 */
  version?: string | null;
  /** 模组型号。 */
  model?: string | null;
  /** 物联卡 ICCID。 */
  iccid?: string | null;
  /** 信号强度 1-31。 */
  signal?: number | null;
  /** 最近一次探测状态枚举名。 */
  probeStatus: string;
  /** 探测失败原因摘要。 */
  probeError?: string | null;
  /** 最近一次探测时间（UTC ISO 串）。 */
  lastProbedAt?: string | null;
  /** 最近一次插槽状态快照（0=关 1=开），下标 i 对应 slotNum = i + 1。 */
  slots?: number[] | null;
  /** slots 写入时间（UTC ISO 串）；未写回为 null。 */
  slotsSnapshotAt?: string | null;
}
