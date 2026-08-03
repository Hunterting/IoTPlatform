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
