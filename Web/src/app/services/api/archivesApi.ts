import { httpClient as apiClient } from './httpClient';
import { ApiResponse, PagedResponse } from './types';

/**
 * 后端档案DTO
 */
export interface ArchiveDto {
  id: number;
  name: string;
  appCode?: string;
  type?: string;
  size?: string;
  date?: string;
  category?: string;
  is3DModel: boolean;
  areaId?: number;
  areaName?: string;
  imageUrl?: string;
  filePath?: string;
  sceneConfig?: string;
  createdAt: string;
  updatedAt: string;
}

/**
 * 档案设备标记DTO
 */
export interface ArchiveDeviceMarkerDto {
  id: number;
  archiveId: number;
  deviceId?: number;
  deviceName: string;
  name: string;
  deviceType?: string;
  model?: string;
  x: number;
  y: number;
  z: number;
  sensors?: string;
  createdAt: string;
}

/**
 * 档案列表查询参数
 */
export interface ArchiveQueryParams {
  keyword?: string;
  type?: string;
  areaId?: number;
  page?: number;
  pageSize?: number;
}

/**
 * 创建档案请求
 */
export interface CreateArchiveRequest {
  name: string;
  appCode?: string;
  type: string;
  size?: string;
  date?: string;
  category?: string;
  is3DModel?: boolean;
  areaId?: number;
  imageUrl?: string;
  filePath?: string;
  sceneConfig?: string;
}

/**
 * 更新档案请求
 */
export interface UpdateArchiveRequest {
  name: string;
  type?: string;
  size?: string;
  date?: string;
  category?: string;
  is3DModel?: boolean;
  areaId?: number;
  imageUrl?: string;
  filePath?: string;
  sceneConfig?: string;
}

const BASE_URL = '/archives';

/**
 * 获取档案列表
 */
export async function getArchives(
  params: ArchiveQueryParams = {}
): Promise<ApiResponse<PagedResponse<ArchiveDto>>> {
  const queryParams = new URLSearchParams();

  if (params.keyword) {
    queryParams.append('keyword', params.keyword);
  }
  if (params.type) {
    queryParams.append('type', params.type);
  }
  if (params.areaId) {
    queryParams.append('areaId', params.areaId.toString());
  }
  if (params.page) {
    queryParams.append('page', params.page.toString());
  }
  if (params.pageSize) {
    queryParams.append('pageSize', params.pageSize.toString());
  }

  const queryString = queryParams.toString();
  const url = queryString ? `${BASE_URL}?${queryString}` : BASE_URL;

  const response = await apiClient.get<ApiResponse<PagedResponse<ArchiveDto>>>(url);
  return response.data;
}

/**
 * 获取档案详情
 */
export async function getArchive(
  id: number
): Promise<ApiResponse<ArchiveDto>> {
  const response = await apiClient.get<ApiResponse<ArchiveDto>>(`${BASE_URL}/${id}`);
  return response.data;
}

/**
 * 创建档案
 */
export async function createArchive(
  request: CreateArchiveRequest
): Promise<ApiResponse<ArchiveDto>> {
  const response = await apiClient.post<ApiResponse<ArchiveDto>>(
    BASE_URL,
    request
  );
  return response.data;
}

/**
 * 更新档案
 */
export async function updateArchive(
  id: number,
  request: UpdateArchiveRequest
): Promise<ApiResponse<ArchiveDto>> {
  const response = await apiClient.put<ApiResponse<ArchiveDto>>(
    `${BASE_URL}/${id}`,
    request
  );
  return response.data;
}

/**
 * 删除档案
 */
export async function deleteArchive(
  id: number
): Promise<ApiResponse<void>> {
  const response = await apiClient.delete<ApiResponse<void>>(`${BASE_URL}/${id}`);
  return response.data;
}

/**
 * 获取档案设备标记
 */
export async function getArchiveMarkers(
  id: number
): Promise<ApiResponse<ArchiveDeviceMarkerDto[]>> {
  const response = await apiClient.get<ApiResponse<ArchiveDeviceMarkerDto[]>>(
    `${BASE_URL}/${id}/markers`
  );
  return response.data;
}
