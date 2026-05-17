import { httpClient as apiClient } from './httpClient';
import { ApiResponse } from './types';

/**
 * 通用附件DTO接口
 */
export interface AttachmentDto {
  id: number;
  module: string;
  businessId?: number;
  name: string;
  originalName?: string;
  extension?: string;
  fileUrl?: string;
  fileSize?: string;
  fileSizeBytes: number;
  contentType?: string;
  uploadDate: string;
  uploadUserId?: number;
  remark?: string;
}

/**
 * 上传附件请求
 */
export interface UploadAttachmentRequest {
  file: File;
  module: string;
  businessId?: number;
  name?: string;
  remark?: string;
  appCode?: string;
}

/**
 * 批量上传附件请求
 */
export interface BatchUploadRequest {
  files: File[];
  module: string;
  businessId?: number;
  remark?: string;
}

/**
 * 附件列表查询参数
 */
export interface AttachmentQueryParams {
  module: string;
  businessId?: number;
  page?: number;
  pageSize?: number;
}

/**
 * 分页结果
 */
export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
}

const BASE_URL = 'attachments';

/**
 * 上传单个附件
 */
export async function uploadAttachment(
  request: UploadAttachmentRequest
): Promise<ApiResponse<AttachmentDto>> {
  const formData = new FormData();
  formData.append('file', request.file);
  formData.append('module', request.module);

  if (request.businessId) {
    formData.append('businessId', request.businessId.toString());
  }

  if (request.name) {
    formData.append('name', request.name);
  }

  if (request.remark) {
    formData.append('remark', request.remark);
  }

  // 如果提供了appCode，添加到请求头
  const headers: Record<string, string> = {
    'Content-Type': 'multipart/form-data',
  };

  if (request.appCode) {
    headers['X-App-Code'] = request.appCode;
  }

  const response = await apiClient.post<ApiResponse<AttachmentDto>>(
    `${BASE_URL}/upload`,
    formData,
    { headers }
  );

  return response.data;
}

/**
 * 批量上传附件
 */
export async function uploadAttachmentsBatch(
  request: BatchUploadRequest
): Promise<ApiResponse<AttachmentDto[]>> {
  const formData = new FormData();

  request.files.forEach((file) => {
    formData.append('files', file);
  });

  formData.append('module', request.module);

  if (request.businessId) {
    formData.append('businessId', request.businessId.toString());
  }

  if (request.remark) {
    formData.append('remark', request.remark);
  }

  const response = await apiClient.post<ApiResponse<AttachmentDto[]>>(
    `${BASE_URL}/upload/batch`,
    formData,
    {
      headers: {
        'Content-Type': 'multipart/form-data',
      },
    }
  );

  return response.data;
}

/**
 * 获取附件列表
 */
export async function getAttachments(
  params: AttachmentQueryParams
): Promise<ApiResponse<PagedResult<AttachmentDto>>> {
  const queryParams = new URLSearchParams();
  queryParams.append('module', params.module);

  if (params.businessId) {
    queryParams.append('businessId', params.businessId.toString());
  }

  if (params.page) {
    queryParams.append('page', params.page.toString());
  }

  if (params.pageSize) {
    queryParams.append('pageSize', params.pageSize.toString());
  }

  const response = await apiClient.get<ApiResponse<PagedResult<AttachmentDto>>>(
    `${BASE_URL}?${queryParams.toString()}`
  );

  return response.data;
}

/**
 * 获取附件详情
 */
export async function getAttachment(
  id: number
): Promise<ApiResponse<AttachmentDto>> {
  const response = await apiClient.get<ApiResponse<AttachmentDto>>(
    `${BASE_URL}/${id}`
  );

  return response.data;
}

/**
 * 删除附件
 */
export async function deleteAttachment(
  id: number
): Promise<ApiResponse<void>> {
  const response = await apiClient.delete<ApiResponse<void>>(
    `${BASE_URL}/${id}`
  );

  return response.data;
}

/**
 * 下载附件
 */
export async function downloadAttachment(
  id: number,
  fileName?: string
): Promise<void> {
  const response = await apiClient.get(`${BASE_URL}/${id}/download`, {
    responseType: 'blob',
  });

  // 获取文件名
  const contentDisposition = response.headers['content-disposition'];
  let downloadFileName = fileName || `附件_${id}`;

  if (contentDisposition) {
    const match = contentDisposition.match(
      /filename[^;=\n]*=((['"]).*?\2|[^;\n]*)/
    );
    if (match) {
      downloadFileName = decodeURIComponent(match[1].replace(/['"]/g, ''));
    }
  }

  // 创建下载链接
  const url = window.URL.createObjectURL(new Blob([response.data]));
  const link = document.createElement('a');
  link.href = url;
  link.setAttribute('download', downloadFileName);
  document.body.appendChild(link);
  link.click();
  link.remove();
  window.URL.revokeObjectURL(url);
}

/**
 * 获取允许的文件类型
 */
export async function getAllowedFileTypes(): Promise<
  ApiResponse<{
    extensions: string[];
    maxFileSizeMB: number;
    maxFileSizeBytes: number;
  }>
> {
  const response = await apiClient.get<
    ApiResponse<{
      extensions: string[];
      maxFileSizeMB: number;
      maxFileSizeBytes: number;
    }>
  >(`${BASE_URL}/allowed-types`);

  return response.data;
}

// ==================== 合同模块专用函数 ====================

/**
 * 上传合同文件
 */
export async function uploadContractFile(
  projectId: number,
  file: File,
  name: string,
  type: string = 'service'
): Promise<ApiResponse<AttachmentDto>> {
  return uploadAttachment({
    file,
    module: 'contracts',
    businessId: projectId,
    name,
    remark: `合同类型: ${type}`,
  });
}

/**
 * 获取项目的合同列表
 */
export async function getProjectContracts(
  projectId: number
): Promise<ApiResponse<AttachmentDto[]>> {
  const result = await getAttachments({
    module: 'contracts',
    businessId: projectId,
  });

  return {
    ...result,
    data: result.data?.items || [],
  };
}
