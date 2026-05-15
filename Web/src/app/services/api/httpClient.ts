import axios, { AxiosInstance, InternalAxiosRequestConfig, AxiosResponse, AxiosError } from 'axios';
import { ApiResponse, ApiError } from './types';

// 从环境变量获取API基础URL
// 注意：baseURL 已包含 /api/v1 前缀，各 API 文件的 BASE_URL 只需写资源路径（如 /contracts、/attachments）
const API_BASE_URL = import.meta.env.VITE_API_BASE_URL || 'http://localhost:5011/api/v1';

// API配置接口
export interface ApiConfig {
  baseURL: string;
  timeout: number;
  withCredentials: boolean;
}

// 默认配置
const defaultConfig: ApiConfig = {
  baseURL: API_BASE_URL,
  timeout: 30000, // 30秒超时
  withCredentials: false,
};

// Token存储键名
const TOKEN_KEY = 'token';
const REFRESH_TOKEN_KEY = 'refresh_token';

// 创建HTTP客户端实例
export const createHttpClient = (config: Partial<ApiConfig> = {}): AxiosInstance => {
  const mergedConfig: ApiConfig = { ...defaultConfig, ...config };
  const client = axios.create(mergedConfig);

  // 请求拦截器 - 添加JWT Token
  client.interceptors.request.use(
    (config: InternalAxiosRequestConfig) => {
      const token = localStorage.getItem(TOKEN_KEY);
      if (token) {
        config.headers.Authorization = `Bearer ${token}`;
      }
      
      // 设置Content-Type
      if (!config.headers['Content-Type']) {
        config.headers['Content-Type'] = 'application/json';
      }
      
      return config;
    },
    (error: AxiosError) => {
      console.error('请求拦截器错误:', error);
      return Promise.reject(error);
    }
  );

  // 响应拦截器 - 统一错误处理
  client.interceptors.response.use(
    (response: AxiosResponse<ApiResponse<any>>) => {
      // 返回完整的ApiResponse结构，包含code、message、data等字段
      // 这样API层可以统一处理成功和错误状态
      return response;
    },
    (error: AxiosError<ApiError>) => {
      console.error('API请求错误:', error);
      
      // 处理网络错误
      if (!error.response) {
        return Promise.reject({
          code: 0,
          message: '网络连接错误，请检查网络设置',
          timestamp: Date.now()
        });
      }
      
      const { status, data } = error.response;
      
      // 处理401未授权错误
      if (status === 401) {
        // 清除token并跳转到登录页
        localStorage.removeItem(TOKEN_KEY);
        localStorage.removeItem(REFRESH_TOKEN_KEY);
        
        // 如果不是登录页，则跳转到登录页
        if (!window.location.pathname.includes('/login')) {
          window.location.href = '/login';
        }
        
        return Promise.reject({
          code: 401,
          message: data?.message || '登录已过期，请重新登录',
          timestamp: Date.now()
        });
      }
      
      // 处理403禁止访问错误
      if (status === 403) {
        return Promise.reject({
          code: 403,
          message: data?.message || '没有访问权限',
          timestamp: Date.now()
        });
      }
      
      // 处理404未找到错误
      if (status === 404) {
        return Promise.reject({
          code: 404,
          message: data?.message || '请求的资源不存在',
          timestamp: Date.now()
        });
      }
      
      // 处理其他错误
      return Promise.reject({
        code: status,
        message: data?.message || '服务器错误，请稍后重试',
        details: data?.details,
        timestamp: Date.now()
      });
    }
  );

  return client;
};

// 导出默认的HTTP客户端实例
export const httpClient = createHttpClient();

// Token管理函数
export const tokenManager = {
  // 保存token
  setToken: (token: string): void => {
    localStorage.setItem(TOKEN_KEY, token);
  },
  
  // 获取token
  getToken: (): string | null => {
    return localStorage.getItem(TOKEN_KEY);
  },
  
  // 清除token
  clearToken: (): void => {
    localStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem(REFRESH_TOKEN_KEY);
  },
  
  // 检查是否已登录
  isAuthenticated: (): boolean => {
    return !!localStorage.getItem(TOKEN_KEY);
  }
};

// 导出HTTP客户端工具函数
export const http = {
  // GET请求
  get: <T>(url: string, config?: InternalAxiosRequestConfig) => {
    return httpClient.get<T>(url, config);
  },
  
  // POST请求
  post: <T>(url: string, data?: any, config?: InternalAxiosRequestConfig) => {
    return httpClient.post<T>(url, data, config);
  },
  
  // PUT请求
  put: <T>(url: string, data?: any, config?: InternalAxiosRequestConfig) => {
    return httpClient.put<T>(url, data, config);
  },
  
  // DELETE请求
  delete: <T>(url: string, config?: InternalAxiosRequestConfig) => {
    return httpClient.delete<T>(url, config);
  },
  
  // PATCH请求
  patch: <T>(url: string, data?: any, config?: InternalAxiosRequestConfig) => {
    return httpClient.patch<T>(url, data, config);
  }
};