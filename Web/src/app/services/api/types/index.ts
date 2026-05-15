// API通用类型定义
export interface ApiResponse<T> {
  code: number;
  message: string;
  data: T;
  timestamp: number;
}

export interface PagedResponse<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export interface ApiError {
  code: number;
  message: string;
  details?: Record<string, string[]>;
  timestamp?: number;
}

// 通用的分页请求参数
export interface PaginationParams {
  page?: number;
  pageSize?: number;
  sortBy?: string;
  sortDirection?: 'asc' | 'desc';
}

// 通用的列表请求参数
export interface ListParams extends PaginationParams {
  keyword?: string;
  status?: string;
  [key: string]: any;
}

// 通用的ID参数类型（处理long到string的转换）
export type ApiId = string | number;
export type ApiIdString = string;
export type ApiIdNumber = number;

// 通用的状态枚举
export enum ApiStatus {
  ACTIVE = 'active',
  INACTIVE = 'inactive',
  PENDING = 'pending',
  DELETED = 'deleted'
}

export * from './deviceCommand.types';