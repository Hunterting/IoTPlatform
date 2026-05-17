import { httpClient } from './httpClient';
import { AxiosResponse } from 'axios';
import { ApiResponse, PagedResponse } from './types';
import {
  UserDto,
  UserDetailDto,
  CreateUserRequest,
  UpdateUserRequest,
  UserFilters,
  ChangePasswordRequest
} from './types/user.types';

/**
 * 用户管理API服务
 */
export const userApi = {
  /**
   * 获取用户列表（分页）
   */
  getUsers: async (
    page: number = 1,
    pageSize: number = 20,
    keyword?: string
  ): Promise<AxiosResponse<ApiResponse<PagedResponse<UserDto>>>> => {
    const params: Record<string, any> = {
      page,
      pageSize
    };

    if (keyword) {
      params.keyword = keyword;
    }

    return httpClient.get<ApiResponse<PagedResponse<UserDto>>>('/users', { params });
  },

  /**
   * 获取单个用户详情
   */
  getUser: async (id: string): Promise<AxiosResponse<ApiResponse<UserDto>>> => {
    return httpClient.get<ApiResponse<UserDto>>(`/users/${id}`);
  },

  /**
   * 获取用户详情（包含更多信息）
   */
  getUserDetail: async (id: string): Promise<AxiosResponse<ApiResponse<UserDetailDto>>> => {
    return httpClient.get<ApiResponse<UserDetailDto>>(`/users/${id}/detail`);
  },

  /**
   * 创建用户
   */
  createUser: async (data: CreateUserRequest): Promise<AxiosResponse<ApiResponse<UserDto>>> => {
    return httpClient.post<ApiResponse<UserDto>>('/users', data);
  },

  /**
   * 更新用户
   */
  updateUser: async (
    id: string,
    data: UpdateUserRequest
  ): Promise<AxiosResponse<ApiResponse<UserDto>>> => {
    return httpClient.put<ApiResponse<UserDto>>(`/users/${id}`, data);
  },

  /**
   * 删除用户
   */
  deleteUser: async (id: string): Promise<AxiosResponse<ApiResponse>> => {
    return httpClient.delete<ApiResponse>(`/users/${id}`);
  },

  /**
   * 修改密码
   */
  changePassword: async (
    id: string,
    data: ChangePasswordRequest
  ): Promise<AxiosResponse<ApiResponse>> => {
    return httpClient.post<ApiResponse>(`/users/${id}/change-password`, data);
  },

  /**
   * 批量启用用户
   */
  batchEnableUsers: async (userIds: string[]): Promise<AxiosResponse<ApiResponse>> => {
    return httpClient.post<ApiResponse>('/users/batch-enable', { userIds });
  },

  /**
   * 批量禁用用户
   */
  batchDisableUsers: async (userIds: string[]): Promise<AxiosResponse<ApiResponse>> => {
    return httpClient.post<ApiResponse>('/users/batch-disable', { userIds });
  },

  /**
   * 批量删除用户
   */
  batchDeleteUsers: async (userIds: string[]): Promise<AxiosResponse<ApiResponse>> => {
    return httpClient.post<ApiResponse>('/users/batch-delete', { userIds });
  }
};

export default userApi;
