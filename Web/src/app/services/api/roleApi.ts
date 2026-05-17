/**
 * 角色管理API服务
 */
import { httpClient } from './httpClient';
import { AxiosResponse } from 'axios';
import { ApiResponse, PagedResponse } from './types';
import { BackendRoleDto } from './types/role.types';
import {
  CreateRoleRequest,
  UpdateRoleRequest
} from './types/role.types';

/**
 * 角色管理API服务
 */
export const roleApi = {
  /**
   * 获取角色列表（分页）
   */
  getRoles: async (
    page: number = 1,
    pageSize: number = 20,
    keyword?: string
  ): Promise<AxiosResponse<ApiResponse<PagedResponse<BackendRoleDto>>>> => {
    const params: Record<string, any> = {
      page,
      pageSize
    };

    if (keyword) {
      params.keyword = keyword;
    }

    return httpClient.get<ApiResponse<PagedResponse<BackendRoleDto>>>('/roles', { params });
  },

  /**
   * 获取单个角色详情
   */
  getRole: async (id: number): Promise<AxiosResponse<ApiResponse<BackendRoleDto>>> => {
    return httpClient.get<ApiResponse<BackendRoleDto>>(`/roles/${id}`);
  },

  /**
   * 创建角色
   */
  createRole: async (data: CreateRoleRequest): Promise<AxiosResponse<ApiResponse<BackendRoleDto>>> => {
    return httpClient.post<ApiResponse<BackendRoleDto>>('/roles', data);
  },

  /**
   * 更新角色
   */
  updateRole: async (
    id: number,
    data: UpdateRoleRequest
  ): Promise<AxiosResponse<ApiResponse<BackendRoleDto>>> => {
    return httpClient.put<ApiResponse<BackendRoleDto>>(`/roles/${id}`, data);
  },

  /**
   * 删除角色
   */
  deleteRole: async (id: number): Promise<AxiosResponse<ApiResponse>> => {
    return httpClient.delete<ApiResponse>(`/roles/${id}`);
  },

  /**
   * 获取角色权限列表
   */
  getRolePermissions: async (id: number): Promise<AxiosResponse<ApiResponse<string[]>>> => {
    return httpClient.get<ApiResponse<string[]>>(`/roles/${id}/permissions`);
  }
};

export default roleApi;
