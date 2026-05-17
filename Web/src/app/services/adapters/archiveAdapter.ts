/**
 * 档案适配器
 * 转换后端 Archive 数据为前端 Archive 数据
 */

import {
  ArchiveDto,
  BackendArchiveDto,
  ArchiveStats,
  FILE_TYPE_MAP,
  ARCHIVE_TYPE_MAP,
} from '../api/types/archive.types';

/**
 * 从后端档案数据转换为前端档案数据
 */
export function adaptArchiveFromBackend(data: BackendArchiveDto): ArchiveDto {
  // 根据后端 type 字段映射到前端 type
  let type: ArchiveDto['type'] = 'document';
  
  if (data.type) {
    type = ARCHIVE_TYPE_MAP[data.type] || 'document';
  }
  
  // 如果是3D模型
  if (data.is3DModel) {
    type = '3d_model';
  }

  return {
    id: data.id.toString(),
    name: data.name || '',
    appCode: data.appCode,
    type,
    size: data.size,
    date: data.date || data.createdAt?.split('T')[0] || '',
    category: data.category || '其他',
    is3DModel: data.is3DModel,
    areaId: data.areaId?.toString(),
    areaName: data.areaName,
    imageUrl: data.imageUrl,
    fileUrl: data.filePath,
    sceneConfig: data.sceneConfig ? JSON.parse(data.sceneConfig) : undefined,
    createdAt: data.createdAt,
    updatedAt: data.updatedAt,
  };
}

/**
 * 格式化文件大小
 */
export function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  if (bytes < 1024 * 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  return `${(bytes / (1024 * 1024 * 1024)).toFixed(1)} GB`;
}

/**
 * 根据文件扩展名判断档案类型
 */
export function detectFileType(file: File): ArchiveDto['type'] {
  const ext = file.name.split('.').pop()?.toLowerCase() || '';
  return FILE_TYPE_MAP[ext] || 'document';
}

/**
 * 计算档案统计信息
 */
export function calculateArchiveStats(archives: ArchiveDto[]): ArchiveStats {
  const stats: ArchiveStats = {
    total: archives.length,
    pdfCount: 0,
    imageCount: 0,
    blueprintCount: 0,
    documentCount: 0,
    totalSize: 0,
  };

  archives.forEach((archive) => {
    switch (archive.type) {
      case 'pdf':
        stats.pdfCount++;
        break;
      case 'image':
        stats.imageCount++;
        break;
      case 'blueprint':
        stats.blueprintCount++;
        break;
      case 'document':
        stats.documentCount++;
        break;
      case '3d_model':
        stats.imageCount++; // 3D模型归类到图纸资料
        break;
    }
    // 尝试从 size 字符串解析大小
    if (archive.size) {
      const sizeMatch = archive.size.match(/(\d+(?:\.\d+)?)\s*(KB|MB|GB|B)/i);
      if (sizeMatch) {
        const value = parseFloat(sizeMatch[1]);
        const unit = sizeMatch[2].toUpperCase();
        let bytes = value;
        if (unit === 'KB') bytes = value * 1024;
        else if (unit === 'MB') bytes = value * 1024 * 1024;
        else if (unit === 'GB') bytes = value * 1024 * 1024 * 1024;
        stats.totalSize += bytes;
      }
    }
  });

  return stats;
}

/**
 * 格式化文件大小显示
 */
export function formatTotalSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  if (bytes < 1024 * 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  return `${(bytes / (1024 * 1024 * 1024)).toFixed(1)} GB`;
}

/**
 * 格式化上传日期
 */
export function formatUploadDate(date: string): string {
  if (!date) return '';
  try {
    const dateObj = new Date(date);
    return dateObj.toLocaleString('zh-CN', {
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
    });
  } catch {
    return date;
  }
}

/**
 * 判断是否为3D模型文件
 */
export function is3DModelFile(extension: string): boolean {
  const ext = extension.toLowerCase();
  return ['dwg', 'dxf', 'obj', 'fbx', '3ds', 'step', 'stp', 'iges', 'igs'].includes(ext);
}

/**
 * 从档案类型获取图标类型
 */
export function getArchiveIconType(archive: ArchiveDto): ArchiveDto['type'] {
  if (archive.is3DModel) {
    return '3d_model';
  }
  return archive.type;
}
