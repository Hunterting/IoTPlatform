import { http, tokenManager } from './httpClient';
import { ApiResponse, ApiId, PagedResponse, ListParams } from './types';

// 通用的API工具函数

/**
 * 处理ID参数转换（将string转换为number，用于后端long类型）
 */
export const processIdParam = (id: ApiId): string => {
  if (typeof id === 'number') {
    return id.toString();
  }
  return id;
};

/**
 * 处理日期参数转换
 */
export const processDateParam = (date: Date | string): string => {
  if (date instanceof Date) {
    return date.toISOString();
  }
  return date;
};

/**
 * 构建查询参数字符串
 */
export const buildQueryString = (params: Record<string, any>): string => {
  const queryParams = new URLSearchParams();
  
  Object.entries(params).forEach(([key, value]) => {
    if (value !== undefined && value !== null && value !== '') {
      if (Array.isArray(value)) {
        // 处理数组参数
        value.forEach(item => {
          queryParams.append(key, item.toString());
        });
      } else if (value instanceof Date) {
        // 处理日期参数
        queryParams.append(key, value.toISOString());
      } else {
        queryParams.append(key, value.toString());
      }
    }
  });
  
  const queryString = queryParams.toString();
  return queryString ? `?${queryString}` : '';
};

/**
 * 处理分页列表请求
 */
export const handlePagedRequest = async <T>(
  url: string,
  params: ListParams = {}
): Promise<PagedResponse<T>> => {
  const queryString = buildQueryString(params);
  const response = await http.get<ApiResponse<PagedResponse<T>>>(`${url}${queryString}`);
  return response.data;
};

/**
 * 处理单条数据请求
 */
export const handleSingleRequest = async <T>(
  url: string,
  id: ApiId
): Promise<T> => {
  const processedId = processIdParam(id);
  const response = await http.get<ApiResponse<T>>(`${url}/${processedId}`);
  return response.data;
};

/**
 * 处理创建请求
 */
export const handleCreateRequest = async <T>(
  url: string,
  data: any
): Promise<T> => {
  const response = await http.post<ApiResponse<T>>(url, data);
  return response.data;
};

/**
 * 处理更新请求
 */
export const handleUpdateRequest = async <T>(
  url: string,
  id: ApiId,
  data: any
): Promise<T> => {
  const processedId = processIdParam(id);
  const response = await http.put<ApiResponse<T>>(`${url}/${processedId}`, data);
  return response.data;
};

/**
 * 处理删除请求
 */
export const handleDeleteRequest = async (
  url: string,
  id: ApiId
): Promise<void> => {
  const processedId = processIdParam(id);
  await http.delete<ApiResponse<void>>(`${url}/${processedId}`);
};

/**
 * 文件上传处理
 */
export const handleFileUpload = async (
  url: string,
  file: File,
  onProgress?: (progress: number) => void
): Promise<any> => {
  const formData = new FormData();
  formData.append('file', file);
  
  const response = await http.post<ApiResponse<any>>(url, formData, {
    headers: {
      'Content-Type': 'multipart/form-data',
    },
    onUploadProgress: (progressEvent) => {
      if (onProgress && progressEvent.total) {
        const progress = Math.round((progressEvent.loaded * 100) / progressEvent.total);
        onProgress(progress);
      }
    },
  });
  
  return response.data;
};

/**
 * 错误处理装饰器
 */
export const withErrorHandling = <T extends any[], R>(
  fn: (...args: T) => Promise<R>
): ((...args: T) => Promise<R>) => {
  return async (...args: T): Promise<R> => {
    try {
      return await fn(...args);
    } catch (error: any) {
      console.error('API调用错误:', error);
      
      // 这里可以添加全局错误处理逻辑，比如显示错误提示
      const errorMessage = error.message || '请求失败，请稍后重试';
      
      // 如果是开发环境，打印详细错误信息
      if (import.meta.env.VITE_DEBUG === 'true') {
        console.error('详细错误信息:', error);
      }
      
      throw error;
    }
  };
};

/**
 * 检查网络状态
 */
export const checkNetworkStatus = async (): Promise<boolean> => {
  try {
    // 发送一个简单的HEAD请求检查网络连通性
    await http.head('/');
    return true;
  } catch {
    return false;
  }
};

/**
 * 重试请求
 */
export const retryRequest = async <T>(
  fn: () => Promise<T>,
  maxRetries = 3,
  delay = 1000
): Promise<T> => {
  let lastError: any;
  
  for (let i = 0; i < maxRetries; i++) {
    try {
      return await fn();
    } catch (error) {
      lastError = error;
      
      // 如果不是最后一次重试，等待一段时间
      if (i < maxRetries - 1) {
        await new Promise(resolve => setTimeout(resolve, delay));
        delay *= 2; // 指数退避
      }
    }
  }
  
  throw lastError;
};