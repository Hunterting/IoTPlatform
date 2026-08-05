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

// ════════════════════════════════════════════════════════════════
// T10：安圣定时任务（对应后端 AnShengScheduleController）
//
// 【字段名权威来源】
//   Controllers/AnShengScheduleController.cs
//   DTOs/Requests/AnShengRequests.cs   （AnShengSetTimeTasksRequest 等）
//   DTOs/Responses/AnShengResponses.cs （AnShengTimeTaskDto 等）
//   Models/AnShengTimeTask.cs          （AnShengTimeTaskKind 枚举）
//
// 【camelCase 换算】后端 MVC 用 JsonSerializerDefaults.Web，.NET 的 CamelCase 策略
//   只把**前导连续大写**降格：SHour → sHour、EMinute → eMinute、OnMins → onMins、
//   RowVersion → rowVersion、TaskKind → taskKind、UploadEnable → uploadEnable。
//
// 【铁律②：拒绝走信封】业务拒绝 HTTP 恒 200 + ApiResponse.code=400，
//   机器可读原因在 data.rejectReason。**唯一例外**是乐观并发冲突：
//   控制器显式 StatusCode(409, ...)，axios 会抛异常，需在 catch 里判定（见页面注释）。
//
// 【铁律④：枚举以字符串原名出网】taskKind 是 "Normal" / "Loop"，不是 0 / 1。
// ════════════════════════════════════════════════════════════════

/**
 * 定时任务类型。
 *
 * 后端 Models/AnShengTimeTask.cs 的 AnShengTimeTaskKind 枚举，
 * 因全局注册 JsonStringEnumConverter 而以**字符串成员名**出网。
 * 前端分支必须用 'Normal' / 'Loop'，绝不能按 0 / 1 判断。
 */
export type AnShengTimeTaskKind = 'Normal' | 'Loop';

// ── T10 请求 DTO ────────────────────────────────────────────

/**
 * 单条普通定时任务请求项。
 * 对应后端 AnShengTimeTaskItemRequest。到点执行一次 action。
 */
export interface AnShengTimeTaskItemRequest {
  /** 设备分配的任务 id；新建时传 null。 */
  id?: string | null;
  /** 是否启用。 */
  enable: boolean;
  /** 每周生效的星期几（1-7）；空数组表示仅一次。 */
  weekDays?: number[] | null;
  /** 动作小时（0-23）。 */
  hour: number;
  /** 动作分钟（0-59）。 */
  minute: number;
  /** 动作：on / off / toggle。 */
  action: AnShengSwitchAction;
  /** 任务触发时是否上报 timeEvent。 */
  uploadEnable: boolean;
}

/**
 * 单条循环定时任务请求项。
 * 对应后端 AnShengLoopTimeTaskItemRequest。时间窗内按 onMins / offMins 往复通断。
 */
export interface AnShengLoopTimeTaskItemRequest {
  /** 设备分配的任务 id；新建时传 null。 */
  id?: string | null;
  /** 是否启用。 */
  enable: boolean;
  /** 每周生效的星期几（1-7）；空数组表示仅一次。 */
  weekDays?: number[] | null;
  /** 每天循环开始的小时（0-23）。后端属性 SHour。 */
  sHour: number;
  /** 每天循环开始的分钟（0-59）。后端属性 SMinute。 */
  sMinute: number;
  /** 每天循环结束的小时（0-23）。后端属性 EHour。 */
  eHour: number;
  /** 每天循环结束的分钟（0-59）。后端属性 EMinute。 */
  eMinute: number;
  /** 循环中打开的分钟数。 */
  onMins: number;
  /** 循环中关闭的分钟数。 */
  offMins: number;
}

/**
 * 单插槽定时任务集合请求体（整表覆盖时的数组元素）。
 * 对应后端 AnShengSlotTimeTaskSetRequest。
 */
export interface AnShengSlotTimeTaskSetRequest {
  /** 插槽编号，从 1 开始。 */
  slotNum: number;
  /** 普通定时任务列表。 */
  timeTasks: AnShengTimeTaskItemRequest[];
  /** 循环定时任务列表。 */
  loopTimeTasks: AnShengLoopTimeTaskItemRequest[];
}

/**
 * 整表覆盖定时任务请求。
 * 对应 POST /ansheng/{deviceId}/time-tasks，后端 DTO：AnShengSetTimeTasksRequest。
 *
 * 【整表覆盖语义】未列出的插槽其定时任务将被清空，属高危操作，
 * 故 confirm 必须为 true，否则后端以 RejectedByConfirm 业务拒绝且命令零出网。
 */
export interface AnShengSetTimeTasksRequest {
  /** 二次确认开关，必须为 true 才下发。 */
  confirm: boolean;
  /** 每个插槽的完整定时任务集合（按插槽升序）。 */
  slots: AnShengSlotTimeTaskSetRequest[];
  /** 乐观并发令牌，取自 GET /time-tasks 任意行的 rowVersion；不一致返回 HTTP 409。 */
  rowVersion?: number | null;
}

/**
 * 单插槽定时任务请求。
 * 对应 POST /ansheng/{deviceId}/time-tasks/{slotNum}，后端 DTO：AnShengSetSlotTimeTasksRequest。
 *
 * 【slotNum 不在请求体里】它取自路由段，后端在下发前拦截越界值（HTTP 200 + code=400）。
 */
export interface AnShengSetSlotTimeTasksRequest {
  /** 二次确认开关，必须为 true 才下发。 */
  confirm: boolean;
  /** 普通定时任务列表。 */
  timeTasks: AnShengTimeTaskItemRequest[];
  /** 循环定时任务列表。 */
  loopTimeTasks: AnShengLoopTimeTaskItemRequest[];
  /** 乐观并发令牌；不一致返回 HTTP 409。 */
  rowVersion?: number | null;
}

// ── T10 响应 DTO ────────────────────────────────────────────

/**
 * 单条定时任务镜像（普通 / 循环共用同一形状）。
 * 对应后端 AnShengTimeTaskDto。
 *
 * 【字段按 taskKind 分组生效】taskKind='Normal' 时 hour / minute / action / uploadEnable 有意义；
 * taskKind='Loop' 时 sHour / sMinute / eHour / eMinute / onMins / offMins 有意义。
 * 另一组字段为默认值 0 / 空串，不代表设备真值，渲染时须按 taskKind 分支。
 */
export interface AnShengTimeTaskDto {
  /** 插槽编号，从 1 开始。 */
  slotNum: number;
  /** 任务类型：'Normal'（普通）/ 'Loop'（循环）。字符串枚举。 */
  taskKind: AnShengTimeTaskKind;
  /** 同插槽同类型内序号，从 1 开始。 */
  taskIndex: number;
  /** 设备分配的任务 id；平台新建尚未回读时为 null。 */
  taskId?: string | null;
  /** 是否启用。 */
  enable: boolean;
  /** 每周生效的星期几（1-7）；空数组表示仅一次。 */
  weekDays: number[];
  /** 【普通定时】动作小时（0-23）。 */
  hour: number;
  /** 【普通定时】动作分钟（0-59）。 */
  minute: number;
  /** 【普通定时】动作：on / off / toggle。 */
  action: string;
  /** 【普通定时】任务触发时是否上报。 */
  uploadEnable: boolean;
  /** 【循环定时】开始小时。 */
  sHour: number;
  /** 【循环定时】开始分钟。 */
  sMinute: number;
  /** 【循环定时】结束小时。 */
  eHour: number;
  /** 【循环定时】结束分钟。 */
  eMinute: number;
  /** 【循环定时】打开分钟数。 */
  onMins: number;
  /** 【循环定时】关闭分钟数。 */
  offMins: number;
  /** 镜像最后与设备同步的时刻（UTC ISO 串）；写后回读会 bump 该值。 */
  syncedAt: string;
  /** 是否陈旧：(UtcNow - syncedAt) > 24h。 */
  isStale: boolean;
  /** 乐观并发令牌；下发前应原样回传，不一致时后端返回 HTTP 409。 */
  rowVersion: number;
}

/**
 * 单插槽定时任务集合只读视图。
 * 对应后端 AnShengSlotTimeTaskSetDto。GET /time-tasks 返回的是它的**数组**。
 */
export interface AnShengSlotTimeTaskSetDto {
  /** 插槽编号，从 1 开始。 */
  slotNum: number;
  /** 普通定时任务列表（每项 taskKind='Normal'）。 */
  timeTasks: AnShengTimeTaskDto[];
  /** 循环定时任务列表（每项 taskKind='Loop'）。 */
  loopTimeTasks: AnShengTimeTaskDto[];
}

/**
 * 定时任务下发结果（setTimeTasks / setSlotTimeTasks 端点的 data）。
 * 对应后端 AnShengTimeTaskResultDto。
 */
export interface AnShengTimeTaskResultDto {
  /** 平台是否受理并下发了命令；被 Guard / confirm / 并发校验拒收时为 false。 */
  accepted: boolean;
  /** 平台命令标识（GUID），被拒时为 null。 */
  commandId?: string | null;
  /** 安圣 FrameId，被拒（未出网）时为 null。 */
  frameId?: string | null;
  /** 机器可读拒绝原因；喇叭类为 "RejectedByKind"，缺 confirm 为 "RejectedByConfirm"。 */
  rejectReason?: AnShengCommandRejectReason | null;
  /** 面向人的失败原因，勿用于程序分支判断。 */
  errorMessage?: string | null;
  /** 实际出网的 JSON 报文回显；被拒时为 null。 */
  payload?: string | null;
  /** 是否因乐观并发冲突被拒；true 时 HTTP 状态码为 409（而非 200）。 */
  concurrencyConflict: boolean;
  /** 乐观镜像快照（立即返回，可能尚未反映本次下发）。 */
  slots?: AnShengSlotTimeTaskSetDto[] | null;
}

// ════════════════════════════════════════════════════════════════
// T11：安圣电量计（对应后端 AnShengEnergyController）
//
// 【字段名权威来源】
//   Controllers/AnShengEnergyController.cs
//   DTOs/Requests/AnShengRequests.cs   （AnShengSetCalParamsRequest 等）
//   DTOs/Responses/AnShengResponses.cs （AnShengEnergyResultDto / AnShengEmStatisticDto）
//   Models/AnShengEmStatistic.cs       （AnShengEmGranularity 枚举）
//
// 【无 HTTP 409】电量计是只读采集语义，平台不写乐观镜像、没有并发令牌，
//   八个端点的全部结果都走 HTTP 200：成功 code=200，被拒 code=400 + data.rejectReason。
//
// 【camelCase 换算】C# 的 RL 属性经 CamelCase 策略降格为 **rl**（前导连续大写整体小写化），
//   Q → q，Kwh → kwh，PeriodKey → periodKey，SlotNum → slotNum。
// ════════════════════════════════════════════════════════════════

/**
 * 电量计统计粒度。
 *
 * 后端 Models/AnShengEmStatistic.cs 的 AnShengEmGranularity 枚举，字符串出网。
 * · 'Total'   —— 累计电量，每插槽仅一行，periodKey 恒为 'total'；
 * · 'HourSum' —— 日内半小时分布画像（定长 48 项），periodKey 为 '00:00'~'23:30'；
 * · 'Hour'    —— 半小时累计，periodKey 为 'yyyyMMddHHmm'；
 * · 'Day'     —— 日累计，periodKey 为 'yyyyMMdd'；
 * · 'Month'   —— 月累计，periodKey 为 'yyyyMM'。
 */
export type AnShengEmGranularity = 'Total' | 'HourSum' | 'Hour' | 'Day' | 'Month';

// ── T11 请求 DTO ────────────────────────────────────────────

/**
 * 拉取电量计统计请求。
 * 对应 POST /ansheng/{deviceId}/energy/statistics/refresh，后端 DTO：AnShengGetEMStatisticsRequest。
 */
export interface AnShengGetEMStatisticsRequest {
  /**
   * 查询串：all / month / day / hour / hourSum / total，可用逗号组合（如 "total,day,hour"）。
   * 留空表示不带该参数、由设备返回默认集合。
   */
  q?: string | null;
}

/**
 * 清空电量计统计请求。
 * 对应 POST /ansheng/{deviceId}/energy/statistics/clear，后端 DTO：AnShengClearEMStatisticsRequest。
 *
 * 【只清设备、不清平台】平台聚合表一行不删，命令出网后仅追加一条 EmCleared 标记事件用于对账。
 * confirm=false 时后端直接业务拒绝且命令零出网。
 */
export interface AnShengClearEMStatisticsRequest {
  /** 二次确认开关，必须为 true 才下发。 */
  confirm: boolean;
  /** 插槽编号，从 1 开始；null 或 0 表示清空所有插槽。 */
  slotNum?: number | null;
}

/**
 * 设置电量计校准参数请求。
 * 对应 POST /ansheng/{deviceId}/energy/cal-params，后端 DTO：AnShengSetCalParamsRequest。
 *
 * 【rl 与 calParams 的关系】后端 MergeCalParams：字典优先；字典里缺 RL 而 rl 有值时自动补入。
 * 合并后为空字典会被拒（code=400，"至少需要提供 RL"）。
 */
export interface AnShengSetCalParamsRequest {
  /** 校准电阻值 RL（C# 属性 RL，camelCase 后为 rl）。 */
  rl?: number | null;
  /** 完整校准参数字典，键名原样出网。 */
  calParams: Record<string, number>;
}

/**
 * 自动校准请求。
 * 对应 POST /ansheng/{deviceId}/energy/cal-params/auto，后端 DTO：AnShengAutoCalRequest。
 */
export interface AnShengAutoCalRequest {
  /** 已知负载功率（W），必须是真实接在插槽上的负载功率。 */
  power: number;
}

/**
 * 查询平台电量计统计聚合表的 query 参数。
 * 对应 GET /ansheng/{deviceId}/energy/statistics 的 [FromQuery] 绑定。
 *
 * 【granularity 用字符串】ASP.NET 的 query 模型绑定按枚举**成员名**解析（大小写不敏感），
 * 与出网时的字符串枚举保持同一书写形式，故此处直接复用 AnShengEmGranularity。
 */
export interface AnShengEnergyStatisticsQueryParams {
  /** 按插槽过滤；留空表示全部插槽。必须 ≥ 1。 */
  slotNum?: number;
  /** 按粒度过滤；留空表示全部粒度。 */
  granularity?: AnShengEmGranularity;
}

// ── T11 响应 DTO ────────────────────────────────────────────

/**
 * 电量计命令下发结果（T11 全部写端点共用的 data）。
 * 对应后端 AnShengEnergyResultDto。
 *
 * 【为什么不带镜像】电量计是只读采集：统计行只在设备应答真的回来时才由 Router 钩子写库。
 * 前端拿到 accepted=true 后应延时轮询 GET /energy/statistics 取真值。
 */
export interface AnShengEnergyResultDto {
  /** 平台是否受理并下发了命令。 */
  accepted: boolean;
  /** 平台命令标识（GUID），被拒时为 null。 */
  commandId?: string | null;
  /** 安圣 FrameId，被拒（未出网）时为 null。 */
  frameId?: string | null;
  /** 机器可读拒绝原因；喇叭类下发校准 / 统计命令为 "RejectedByKind"。 */
  rejectReason?: AnShengCommandRejectReason | null;
  /** 面向人的失败原因，勿用于程序分支判断。 */
  errorMessage?: string | null;
  /** 实际出网的 JSON 报文回显；被拒时为 null。 */
  payload?: string | null;
}

/**
 * 电量计统计聚合行只读视图（GET /energy/statistics 的 data 元素）。
 * 对应后端 AnShengEmStatisticDto。
 *
 * 唯一键为 (deviceId, slotNum, granularity, periodKey)，设备应答幂等 UPSERT，无空洞行。
 */
export interface AnShengEmStatisticDto {
  /** 插槽编号，从 1 开始。 */
  slotNum: number;
  /** 统计粒度（字符串枚举）。 */
  granularity: AnShengEmGranularity;
  /** 周期键：'total' / '00:00'~'23:30' / 'yyyyMMddHHmm' / 'yyyyMMdd' / 'yyyyMM'。 */
  periodKey: string;
  /** 累计电量（kWh）。C# 属性 Kwh。 */
  kwh: number;
  /** 本行最后一次被设备应答刷新的时刻（UTC ISO 串）。 */
  syncedAt: string;
  /** 是否陈旧：(UtcNow - syncedAt) > 24h。 */
  isStale: boolean;
}
