import { httpClient } from './httpClient';
import { AxiosResponse } from 'axios';
import { 
  ApiResponse, 
  PagedResponse 
} from './types';
import {
  CustomerDto,
  CustomerDetailDto,
  CreateCustomerRequest,
  UpdateCustomerRequest,
  CustomerFilters
} from './types/customer.types';

/**
 * 客户管理API服务
 */
export const customerApi = {
  /**
   * 获取客户列表（分页）
   */
  getCustomers: async (
    page: number = 1,
    pageSize: number = 20,
    filters?: CustomerFilters
  ): Promise<AxiosResponse<ApiResponse<PagedResponse<CustomerDto>>>> => {
    const params: Record<string, any> = {
      page,
      pageSize
    };

    if (filters?.keyword) {
      params.keyword = filters.keyword;
    }

    return httpClient.get<ApiResponse<PagedResponse<CustomerDto>>>('/customers', { params });
  },

  /**
   * 获取单个客户详情
   */
  getCustomer: async (id: string): Promise<AxiosResponse<ApiResponse<CustomerDetailDto>>> => {
    return httpClient.get<ApiResponse<CustomerDetailDto>>(`/customers/${id}`);
  },

  /**
   * 创建客户
   */
  createCustomer: async (data: CreateCustomerRequest): Promise<AxiosResponse<ApiResponse<CustomerDto>>> => {
    return httpClient.post<ApiResponse<CustomerDto>>('/customers', data);
  },

  /**
   * 更新客户
   */
  updateCustomer: async (
    id: string,
    data: UpdateCustomerRequest
  ): Promise<AxiosResponse<ApiResponse<CustomerDto>>> => {
    return httpClient.put<ApiResponse<CustomerDto>>(`/customers/${id}`, data);
  },

  /**
   * 删除客户
   */
  deleteCustomer: async (id: string): Promise<AxiosResponse<ApiResponse>> => {
    return httpClient.delete<ApiResponse>(`/customers/${id}`);
  },

  /**
   * 搜索客户（简化接口）
   */
  searchCustomers: async (keyword: string): Promise<AxiosResponse<ApiResponse<CustomerDto[]>>> => {
    return httpClient.get<ApiResponse<CustomerDto[]>>('/customers/search', {
      params: { keyword }
    });
  }
};

export default customerApi;