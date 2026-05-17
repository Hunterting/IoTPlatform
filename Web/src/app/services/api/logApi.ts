/**
 * 日志管理API服务
 */
import { httpClient } from './httpClient';
import { AxiosResponse } from 'axios';
import { ApiResponse, PagedResponse } from './types';
import { BackendOperationLogDto, BackendLoginLogDto } from './types/log.types';
import {
  OperationLogFilters,
  LoginLogFilters
} from './types/log.types';

/**
 * 日志管理API服务
 */
export const logApi = {
  /**
   * 获取操作日志列表（分页）
   */
  getOperationLogs: async (
    page: number = 1,
    pageSize: number = 20,
    filters?: OperationLogFilters
  ): Promise<AxiosResponse<ApiResponse<PagedResponse<BackendOperationLogDto>>>> => {
    const params: Record<string, any> = {
      page,
      pageSize
    };

    if (filters?.module) {
      params.module = filters.module;
    }
    if (filters?.action) {
      params.action = filters.action;
    }
    if (filters?.userId) {
      params.userId = filters.userId;
    }
    if (filters?.startTime) {
      params.startTime = filters.startTime;
    }
    if (filters?.endTime) {
      params.endTime = filters.endTime;
    }

    return httpClient.get<ApiResponse<PagedResponse<BackendOperationLogDto>>>('/logs/operation', { params });
  },

  /**
   * 获取单个操作日志详情
   */
  getOperationLog: async (id: number): Promise<AxiosResponse<ApiResponse<BackendOperationLogDto>>> => {
    return httpClient.get<ApiResponse<BackendOperationLogDto>>(`/logs/operation/${id}`);
  },

  /**
   * 获取登录日志列表（分页）
   */
  getLoginLogs: async (
    page: number = 1,
    pageSize: number = 20,
    filters?: LoginLogFilters
  ): Promise<AxiosResponse<ApiResponse<PagedResponse<BackendLoginLogDto>>>> => {
    const params: Record<string, any> = {
      page,
      pageSize
    };

    if (filters?.userId) {
      params.userId = filters.userId;
    }
    if (filters?.status) {
      params.status = filters.status;
    }
    if (filters?.startTime) {
      params.startTime = filters.startTime;
    }
    if (filters?.endTime) {
      params.endTime = filters.endTime;
    }

    return httpClient.get<ApiResponse<PagedResponse<BackendLoginLogDto>>>('/logs/login', { params });
  },

  /**
   * 获取单个登录日志详情
   */
  getLoginLog: async (id: number): Promise<AxiosResponse<ApiResponse<BackendLoginLogDto>>> => {
    return httpClient.get<ApiResponse<BackendLoginLogDto>>(`/logs/login/${id}`);
  }
};

export default logApi;
