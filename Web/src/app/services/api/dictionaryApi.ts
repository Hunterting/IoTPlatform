import { httpClient as apiClient } from './httpClient';
import type { ApiResponse, PagedResponse } from './types';
import type { DictionaryItemDto, DictionaryTypeDto } from './types/dictionary.types';

export interface GetDictionaryItemsParams {
  type: string;
}

export interface GetDictionaryTypesParams {
  page?: number;
  pageSize?: number;
  keyword?: string;
}

export interface CreateDictionaryItemRequest {
  type: string;
  code: string;
  name: string;
  sort?: number;
  description?: string;
  status?: string;
}

export interface UpdateDictionaryItemRequest {
  code?: string;
  name?: string;
  sort?: number;
  description?: string;
  status?: string;
}

export interface CreateDictionaryTypeRequest {
  code: string;
  name: string;
  description?: string;
  sortOrder?: number;
  isActive?: boolean;
}

export interface UpdateDictionaryTypeRequest {
  name?: string;
  description?: string;
  sortOrder?: number;
  isActive?: boolean;
}

// ==================== 字典类型 API ====================

/**
 * 获取字典类型列表
 */
export async function getDictionaryTypes(
  params?: GetDictionaryTypesParams
): Promise<ApiResponse<PagedResponse<DictionaryTypeDto>>> {
  return apiClient.get<ApiResponse<PagedResponse<DictionaryTypeDto>>>(
    '/dictionaries/types',
    { params }
  );
}

/**
 * 获取字典类型详情
 */
export async function getDictionaryType(
  id: number
): Promise<ApiResponse<DictionaryTypeDto>> {
  return apiClient.get<ApiResponse<DictionaryTypeDto>>(
    `/dictionaries/types/${id}`
  );
}

/**
 * 创建字典类型
 */
export async function createDictionaryType(
  data: CreateDictionaryTypeRequest
): Promise<ApiResponse<DictionaryTypeDto>> {
  return apiClient.post<ApiResponse<DictionaryTypeDto>>(
    '/dictionaries/types',
    data
  );
}

/**
 * 更新字典类型
 */
export async function updateDictionaryType(
  id: number,
  data: UpdateDictionaryTypeRequest
): Promise<ApiResponse<DictionaryTypeDto>> {
  return apiClient.put<ApiResponse<DictionaryTypeDto>>(
    `/dictionaries/types/${id}`,
    data
  );
}

/**
 * 删除字典类型
 */
export async function deleteDictionaryType(
  id: number
): Promise<ApiResponse<void>> {
  return apiClient.delete<ApiResponse<void>>(`/dictionaries/types/${id}`);
}

// ==================== 字典项 API ====================

/**
 * 获取指定类型的字典项列表
 */
export async function getDictionaryItems(
  params: GetDictionaryItemsParams
): Promise<ApiResponse<DictionaryItemDto[]>> {
  return apiClient.get<ApiResponse<DictionaryItemDto[]>>(
    '/dictionaries/items',
    { params }
  );
}

/**
 * 获取字典项详情
 */
export async function getDictionaryItem(
  id: number
): Promise<ApiResponse<DictionaryItemDto>> {
  return apiClient.get<ApiResponse<DictionaryItemDto>>(
    `/dictionaries/items/${id}`
  );
}

/**
 * 创建字典项
 */
export async function createDictionaryItem(
  data: CreateDictionaryItemRequest
): Promise<ApiResponse<DictionaryItemDto>> {
  return apiClient.post<ApiResponse<DictionaryItemDto>>(
    '/dictionaries/items',
    data
  );
}

/**
 * 更新字典项
 */
export async function updateDictionaryItem(
  id: number,
  data: UpdateDictionaryItemRequest
): Promise<ApiResponse<DictionaryItemDto>> {
  return apiClient.put<ApiResponse<DictionaryItemDto>>(
    `/dictionaries/items/${id}`,
    data
  );
}

/**
 * 删除字典项
 */
export async function deleteDictionaryItem(
  id: number
): Promise<ApiResponse<void>> {
  return apiClient.delete<ApiResponse<void>>(`/dictionaries/items/${id}`);
}

// ==================== 便捷方法 ====================

/**
 * 获取档案分类列表
 */
export async function getArchiveCategories(): Promise<ApiResponse<DictionaryItemDto[]>> {
  return getDictionaryItems({ type: 'archive_category' });
}

/**
 * 获取设备类别列表
 */
export async function getDeviceCategories(): Promise<ApiResponse<DictionaryItemDto[]>> {
  return getDictionaryItems({ type: 'device_category' });
}
