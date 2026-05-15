import { httpClient } from './httpClient';
import { AxiosResponse } from 'axios';
import { 
  ApiResponse, 
  PagedResponse 
} from './types';
import {
  AreaDto,
  AreaTreeNodeDto,
  CreateAreaRequest,
  UpdateAreaRequest,
  AreaFilters,
  AreaType
} from './types/area.types';

/**
 * 区域管理API服务
 */
export const areaApi = {
  /**
   * 获取区域列表（分页）
   */
  getAreas: async (
    page: number = 1,
    pageSize: number = 20,
    filters?: AreaFilters
  ): Promise<AxiosResponse<ApiResponse<PagedResponse<AreaDto>>>> => {
    const params: Record<string, any> = {
      page,
      pageSize
    };

    if (filters?.keyword) {
      params.keyword = filters.keyword;
    }
    if (filters?.type) {
      params.type = filters.type;
    }
    if (filters?.parentId) {
      params.parentId = filters.parentId;
    }
    if (filters?.customerId) {
      params.customerId = filters.customerId;
    }

    return httpClient.get<ApiResponse<PagedResponse<AreaDto>>>('/areas', { params });
  },

  /**
   * 获取区域树
   */
  getAreaTree: async (): Promise<AxiosResponse<ApiResponse<AreaTreeNodeDto[]>>> => {
    return httpClient.get<ApiResponse<AreaTreeNodeDto[]>>('/areas/tree');
  },

  /**
   * 获取子区域列表
   */
  getChildAreas: async (parentId: string): Promise<AxiosResponse<ApiResponse<AreaDto[]>>> => {
    return httpClient.get<ApiResponse<AreaDto[]>>(`/areas/${parentId}/children`);
  },

  /**
   * 获取单个区域详情
   */
  getArea: async (id: string): Promise<AxiosResponse<ApiResponse<AreaDto>>> => {
    return httpClient.get<ApiResponse<AreaDto>>(`/areas/${id}`);
  },

  /**
   * 创建区域
   */
  createArea: async (data: CreateAreaRequest): Promise<AxiosResponse<ApiResponse<AreaDto>>> => {
    return httpClient.post<ApiResponse<AreaDto>>('/areas', data);
  },

  /**
   * 更新区域
   */
  updateArea: async (
    id: string,
    data: UpdateAreaRequest
  ): Promise<AxiosResponse<ApiResponse<AreaDto>>> => {
    return httpClient.put<ApiResponse<AreaDto>>(`/areas/${id}`, data);
  },

  /**
   * 删除区域
   */
  deleteArea: async (id: string): Promise<AxiosResponse<ApiResponse>> => {
    return httpClient.delete<ApiResponse>(`/areas/${id}`);
  },

  /**
   * 获取区域统计信息
   */
  getAreaStats: async (): Promise<AxiosResponse<ApiResponse<{
    totalAreas: number;
    byType: Record<string, number>;
    byLevel: Record<string, number>;
    topAreas: Array<{
      id: string;
      name: string;
      type: string;
      deviceCount: number;
      areaRatio: number;
    }>;
  }>>> => {
    return httpClient.get<ApiResponse<any>>('/areas/stats');
  },

  /**
   * 获取区域设备分布
   */
  getAreaDeviceDistribution: async (areaId: string): Promise<AxiosResponse<ApiResponse<{
    areaId: string;
    areaName: string;
    totalDevices: number;
    byStatus: Record<string, number>;
    byCategory: Record<string, number>;
    byEnergyType: Record<string, number>;
  }>>> => {
    return httpClient.get<ApiResponse<any>>(`/areas/${areaId}/device-distribution`);
  },

  /**
   * 搜索区域
   */
  searchAreas: async (keyword: string): Promise<AxiosResponse<ApiResponse<AreaDto[]>>> => {
    return httpClient.get<ApiResponse<AreaDto[]>>('/areas/search', {
      params: { keyword }
    });
  },

  /**
   * 批量更新区域排序
   */
  batchUpdateAreaOrder: async (
    orders: Array<{ id: string; sortOrder: number }>
  ): Promise<AxiosResponse<ApiResponse>> => {
    return httpClient.post<ApiResponse>('/areas/batch-update-order', { orders });
  }
};

export default areaApi;