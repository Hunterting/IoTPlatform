import { httpClient as apiClient } from './httpClient';

// ── 类型定义（与后端 EzvizResponse.cs camelCase 出网对齐） ──────────────────────

export interface ApiResponse<T = unknown> {
  code: number;
  data?: T;
  message?: string;
  success?: boolean;
}

/** 萤石配置状态 */
export interface EzvizConfigStatus {
  configured: boolean;
  baseUrl: string;
  liveProtocol: number;
  message: string;
}

/** 萤石设备（含本地区域绑定） */
export interface EzvizDevice {
  deviceSerial: string;
  deviceName: string;
  deviceType?: string;
  /** 1-在线 0-离线 */
  status: number;
  defence?: number;
  deviceVersion?: string;
  parentCategory?: string;
  netAddress?: string | null;
  addTime?: number | null;
  updateTime?: number | null;
  areaId?: number | null;
  areaName?: string | null;
  channelNo?: number;
  picUrl?: string | null;
  isShared?: boolean;
  source?: string;
  /** 是否具备直播/抓拍等操作权限（分享设备常为 false） */
  operable?: boolean;
}

export interface EzvizPagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
}

/** 通道 */
export interface EzvizCamera {
  deviceSerial: string;
  channelNo: number;
  channelName?: string;
  status: number;
  picUrl?: string;
  isEncrypt?: boolean;
}

/** 直播/回放地址 */
export interface EzvizLiveAddress {
  id?: string;
  url: string;
  expireTime: number;
  deviceSerial: string;
  channelNo: number;
}

export interface EzvizLiveAddressRequest {
  deviceSerial: string;
  channelNo?: number;
  type?: number; // 1-预览 2-回放
  startTime?: string;
  stopTime?: string;
  ewcode?: string; // 设备验证码（设备加密时必填）
}

/** 抓拍 */
export interface EzvizCapture {
  picUrl: string;
  deviceSerial: string;
  channelNo: number;
}
export interface EzvizCaptureRequest {
  deviceSerial: string;
  channelNo?: number;
}

/** 设备状态 */
export interface EzvizDeviceStatus {
  deviceSerial: string;
  status: number;
  statusText: string;
  defence: number;
}

/** 回放录像片段 */
export interface EzvizPlaybackRecord {
  deviceSerial: string;
  channelNo: number;
  startTime?: number | null;
  endTime?: number | null;
  recType: number;
  fileId?: string;
  fileName?: string;
  coverPic?: string;
  downloadPath?: string;
  fileSize?: number | null;
  videoLong?: number | null;
}
export interface EzvizPlaybackRequest {
  deviceSerial: string;
  channelNo?: number;
  startTime: string;
  endTime: string;
  recType?: number;
}

/** 云台 */
export interface EzvizPtzRequest {
  deviceSerial: string;
  channelNo?: number;
  direction: number; // 0-上 1-下 2-左 3-右
  speed?: number;
}

/** 设备区域绑定 */
export interface EzvizBindRequest {
  deviceSerial: string;
  name: string;
  areaId?: number | null;
  channelNo?: number;
  ewcode?: string; // 设备验证码（设备加密时必填）
}

// ── API 函数 ──────────────────────────────────────────────────────────────────

const BASE_URL = '/ezviz';

/** 配置状态（首屏调用，判断是否显示真实画面） */
export async function getConfigStatus(): Promise<ApiResponse<EzvizConfigStatus>> {
  const r = await apiClient.get<ApiResponse<EzvizConfigStatus>>(`${BASE_URL}/config/status`);
  return r.data;
}

/** 设备列表 */
export async function getDevices(params?: {
  pageStart?: number;
  pageSize?: number;
  includeUnbound?: boolean;
}): Promise<ApiResponse<EzvizPagedResult<EzvizDevice>>> {
  const sp = new URLSearchParams();
  if (params?.pageStart !== undefined) sp.set('pageStart', String(params.pageStart));
  if (params?.pageSize !== undefined) sp.set('pageSize', String(params.pageSize));
  if (params?.includeUnbound !== undefined) sp.set('includeUnbound', String(params.includeUnbound));
  const q = sp.toString();
  const r = await apiClient.get<ApiResponse<EzvizPagedResult<EzvizDevice>>>(
    `${BASE_URL}/devices${q ? `?${q}` : ''}`
  );
  return r.data;
}

/** 通道列表 */
export async function getCameras(deviceSerial: string): Promise<ApiResponse<EzvizCamera[]>> {
  const r = await apiClient.get<ApiResponse<EzvizCamera[]>>(
    `${BASE_URL}/devices/${encodeURIComponent(deviceSerial)}/cameras`
  );
  return r.data;
}

/** 直播/回放地址 */
export async function getLiveAddress(
  request: EzvizLiveAddressRequest
): Promise<ApiResponse<EzvizLiveAddress>> {
  const r = await apiClient.post<ApiResponse<EzvizLiveAddress>>(
    `${BASE_URL}/live-address`, request
  );
  return r.data;
}

/** 抓拍 */
export async function capture(request: EzvizCaptureRequest): Promise<ApiResponse<EzvizCapture>> {
  const r = await apiClient.post<ApiResponse<EzvizCapture>>(`${BASE_URL}/capture`, request);
  return r.data;
}

/** 回放查询 */
export async function getPlayback(
  request: EzvizPlaybackRequest
): Promise<ApiResponse<EzvizPlaybackRecord[]>> {
  const r = await apiClient.post<ApiResponse<EzvizPlaybackRecord[]>>(
    `${BASE_URL}/playback`, request
  );
  return r.data;
}

/** 云台开始 / 停止 */
export async function ptzStart(request: EzvizPtzRequest): Promise<ApiResponse<unknown>> {
  const r = await apiClient.post<ApiResponse<unknown>>(`${BASE_URL}/ptz/start`, request);
  return r.data;
}
export async function ptzStop(request: EzvizPtzRequest): Promise<ApiResponse<unknown>> {
  const r = await apiClient.post<ApiResponse<unknown>>(`${BASE_URL}/ptz/stop`, request);
  return r.data;
}

/** 设备状态 */
export async function getDeviceStatus(
  deviceSerial: string
): Promise<ApiResponse<EzvizDeviceStatus>> {
  const r = await apiClient.get<ApiResponse<EzvizDeviceStatus>>(
    `${BASE_URL}/devices/${encodeURIComponent(deviceSerial)}/status`
  );
  return r.data;
}

/** 绑定设备到本地区域 */
export async function bindCamera(
  request: EzvizBindRequest
): Promise<ApiResponse<EzvizDevice>> {
  const r = await apiClient.post<ApiResponse<EzvizDevice>>(`${BASE_URL}/bindings`, request);
  return r.data;
}

export const ezvizApi = {
  getConfigStatus,
  getDevices,
  getCameras,
  getLiveAddress,
  capture,
  getPlayback,
  ptzStart,
  ptzStop,
  getDeviceStatus,
  bindCamera,
};

export default ezvizApi;
