import { httpClient as apiClient } from './httpClient';
import { ApiResponse } from './types';
import * as attachmentApi from './attachmentApi';

/**
 * 合同DTO接口
 */
export interface ContractDto {
  id: number;
  projectId: number;
  name: string;
  type: string;
  fileUrl?: string;
  fileSize?: string;
  uploadDate: string;
}

/**
 * 上传合同请求
 * 对应后端 ContractsController.UploadContract 的 UploadContractRequest DTO
 */
export interface UploadContractRequest {
  file: File;
  projectId: number;
  name: string;
  type: string;
}

const BASE_URL = 'contracts';

/**
 * 上传合同文件 - 直接调用后端 /api/v1/contracts/upload 接口
 * 后端接收 UploadContractRequest DTO: { File, ProjectId, Name, Type }
 */
export async function uploadContract(
  request: UploadContractRequest
): Promise<ApiResponse<ContractDto>> {
  try {
    // 构造 FormData，字段名匹配后端 UploadContractRequest 的属性（camelCase）
    const formData = new FormData();
    formData.append('File', request.file);
    formData.append('ProjectId', request.projectId.toString());
    formData.append('Name', request.name);
    formData.append('Type', request.type);

    const response = await apiClient.post<ApiResponse<any>>(
      `${BASE_URL}/upload`,
      formData,
      {
        headers: {
          'Content-Type': 'multipart/form-data',
        },
      }
    );

    const result = response.data;

    // 转换后端响应为前端 ContractDto
    if (result.code === 200 && result.data) {
      return {
        code: result.code,
        message: result.message,
        data: {
          id: result.data.id,
          projectId: result.data.projectId || request.projectId,
          name: result.data.name || request.name,
          type: result.data.type || request.type,
          fileUrl: result.data.fileUrl || '',
          fileSize: result.data.fileSize || '',
          uploadDate: result.data.uploadDate || new Date().toISOString(),
        },
        timestamp: result.timestamp,
      };
    }

    return result as unknown as ApiResponse<ContractDto>;
  } catch (error) {
    console.error('上传合同失败:', error);
    return {
      code: 500,
      message: '上传失败',
      data: null as unknown as ContractDto,
      timestamp: Date.now(),
    };
  }
}

/**
 * 获取项目的合同列表
 */
export async function getProjectContracts(
  projectId: string
): Promise<ApiResponse<ContractDto[]>> {
  try {
    const result = await attachmentApi.getProjectContracts(parseInt(projectId));

    // 转换 AttachmentDto[] 为 ContractDto[]
    if (result.code === 200 && result.data) {
      const contracts: ContractDto[] = result.data.map((a) => ({
        id: a.id,
        projectId: a.businessId || 0,
        name: a.name,
        type: extractContractType(a.remark),
        fileUrl: a.fileUrl,
        fileSize: a.fileSize,
        uploadDate: a.uploadDate,
      }));

      return {
        code: result.code,
        message: result.message,
        data: contracts,
        timestamp: result.timestamp,
      };
    }

    return result as unknown as ApiResponse<ContractDto[]>;
  } catch (error) {
    console.error('获取合同列表失败:', error);
    return {
      code: 500,
      message: '获取失败',
      data: [],
      timestamp: Date.now(),
    };
  }
}

/**
 * 获取合同详情
 */
export async function getContract(
  id: number
): Promise<ApiResponse<ContractDto>> {
  const response = await apiClient.get<ApiResponse<ContractDto>>(
    `${BASE_URL}/${id}`
  );
  return response.data;
}

/**
 * 删除合同
 */
export async function deleteContract(
  id: number
): Promise<ApiResponse<void>> {
  const response = await apiClient.delete<ApiResponse<void>>(
    `${BASE_URL}/${id}`
  );
  return response.data;
}

/**
 * 下载合同文件
 */
export async function downloadContract(id: number): Promise<void> {
  const response = await apiClient.get(`${BASE_URL}/${id}/download`, {
    responseType: 'blob',
  });

  // 获取文件名
  const contentDisposition = response.headers['content-disposition'];
  let fileName = `合同_${id}`;
  if (contentDisposition) {
    const match = contentDisposition.match(/filename[^;=\n]*=((['"]).*?\2|[^;\n]*)/);
    if (match) {
      fileName = decodeURIComponent(match[1].replace(/['"]/g, ''));
    }
  }

  // 创建下载链接
  const url = window.URL.createObjectURL(new Blob([response.data]));
  const link = document.createElement('a');
  link.href = url;
  link.setAttribute('download', fileName);
  document.body.appendChild(link);
  link.click();
  link.remove();
  window.URL.revokeObjectURL(url);
}

/**
 * 从备注中提取合同类型
 */
function extractContractType(remark?: string): string {
  if (!remark) return 'service';

  const prefix = '合同类型: ';
  const index = remark.indexOf(prefix);
  if (index >= 0) {
    let type = remark.substring(index + prefix.length).trim();
    const endIndex = type.indexOf(',');
    if (endIndex > 0) {
      type = type.substring(0, endIndex).trim();
    }
    return type;
  }

  return 'service';
}

// ==================== 通用附件 API 导出 ====================
// 导出通用附件上传功能供其他模块使用
export {
  uploadAttachment,
  uploadAttachmentsBatch,
  getAttachments,
  getAttachment,
  deleteAttachment,
  downloadAttachment,
  getAllowedFileTypes,
  type AttachmentDto,
  type UploadAttachmentRequest,
  type BatchUploadRequest,
  type AttachmentQueryParams,
  type PagedResult,
} from './attachmentApi';
