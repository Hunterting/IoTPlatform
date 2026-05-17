/**
 * 档案管理类型定义
 * 对应后端 ArchivesController.cs 和 ArchiveDto
 */

/**
 * 后端档案DTO
 */
export interface BackendArchiveDto {
  id: number;
  name: string;
  appCode?: string;
  type?: string;
  size?: string;
  date?: string;
  category?: string;
  is3DModel: boolean;
  areaId?: number;
  areaName?: string;
  imageUrl?: string;
  filePath?: string;
  sceneConfig?: string;
  createdAt: string;
  updatedAt: string;
}

/**
 * 前端档案数据结构
 */
export interface ArchiveDto {
  id: string;
  name: string;
  appCode?: string;
  type: 'document' | 'image' | 'pdf' | 'folder' | 'blueprint' | '3d_model';
  size?: string;
  date: string;
  category: string;
  is3DModel?: boolean;
  devices?: DeviceMarkerDto[];
  areaId?: string;
  areaName?: string;
  imageUrl?: string;
  sceneConfig?: any;
  extension?: string;
  fileUrl?: string;
  contentType?: string;
  uploadUserId?: number;
  remark?: string;
  createdAt?: string;
  updatedAt?: string;
}

/**
 * 设备标记（3D视图中的设备位置）
 */
export interface DeviceMarkerDto {
  id: string;
  name: string;
  x: number;
  y: number;
  type: string;
  model?: string;
  serialNumber?: string;
  sensors?: string[];
  specification?: string;
  deviceId?: string;
  deviceType?: string;
}

/**
 * 档案设备标记（后端返回格式）
 */
export interface BackendMarkerDto {
  id: number;
  archiveId: number;
  deviceId?: number;
  deviceName: string;
  name: string;
  deviceType?: string;
  model?: string;
  x: number;
  y: number;
  z: number;
  sensors?: string;
  createdAt: string;
}

/**
 * 上传档案请求
 */
export interface UploadArchiveRequest {
  file?: File;
  name?: string;
  type?: string;
  category?: string;
  areaId?: number;
  remark?: string;
  imageUrl?: string;
  is3DModel?: boolean;
  size?: string;
  date?: string;
  filePath?: string;
  sceneConfig?: string;
}

/**
 * 创建档案请求
 */
export interface CreateArchiveRequest {
  name: string;
  type: string;
  category?: string;
  areaId?: number;
  imageUrl?: string;
  is3DModel?: boolean;
  size?: string;
  date?: string;
  filePath?: string;
  sceneConfig?: string;
}

/**
 * 更新档案请求
 */
export interface UpdateArchiveRequest {
  name?: string;
  type?: string;
  category?: string;
  areaId?: number;
  imageUrl?: string;
  is3DModel?: boolean;
  size?: string;
  date?: string;
  filePath?: string;
  sceneConfig?: string;
}

/**
 * 档案列表查询参数
 */
export interface ArchiveQueryParams {
  keyword?: string;
  type?: string;
  areaId?: number;
  page?: number;
  pageSize?: number;
}

/**
 * 档案统计信息
 */
export interface ArchiveStats {
  total: number;
  pdfCount: number;
  imageCount: number;
  blueprintCount: number;
  documentCount: number;
  totalSize: number;
}

/**
 * 档案类型映射（后端类型到前端类型）
 */
export const ARCHIVE_TYPE_MAP: Record<string, ArchiveDto['type']> = {
  'floor_plan': 'image',
  '3d_model': '3d_model',
  'photo': 'image',
  'document': 'document',
  'pdf': 'pdf',
  'doc': 'document',
  'docx': 'document',
  'txt': 'document',
  'xls': 'document',
  'xlsx': 'document',
  'csv': 'document',
  'png': 'image',
  'jpg': 'image',
  'jpeg': 'image',
  'gif': 'image',
  'webp': 'image',
  'bmp': 'image',
  'dwg': 'blueprint',
  'dxf': 'blueprint',
  'svg': 'blueprint',
  'cad': 'blueprint',
};

/**
 * 文件类型映射
 */
export const FILE_TYPE_MAP: Record<string, ArchiveDto['type']> = {
  'pdf': 'pdf',
  'doc': 'document',
  'docx': 'document',
  'txt': 'document',
  'xls': 'document',
  'xlsx': 'document',
  'csv': 'document',
  'png': 'image',
  'jpg': 'image',
  'jpeg': 'image',
  'gif': 'image',
  'webp': 'image',
  'bmp': 'image',
  'dwg': 'blueprint',
  'dxf': 'blueprint',
  'svg': 'blueprint',
  'cad': 'blueprint',
};

/**
 * 档案模块常量
 */
export const ARCHIVE_MODULE = 'archives';

/**
 * 档案类型选项（对应后端）
 */
export const ARCHIVE_TYPE_OPTIONS = [
  { value: 'floor_plan', label: '平面图' },
  { value: '3d_model', label: '3D模型' },
  { value: 'photo', label: '照片' },
  { value: 'document', label: '文档' },
];

/**
 * 根据文件扩展名获取档案类型
 */
export function getArchiveTypeFromExtension(extension: string): ArchiveDto['type'] {
  const ext = extension.toLowerCase().replace('.', '');
  return FILE_TYPE_MAP[ext] || 'document';
}
