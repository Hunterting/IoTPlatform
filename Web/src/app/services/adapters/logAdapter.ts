/**
 * 日志数据适配器
 * 负责前后端数据格式转换
 */
import { adaptIdFromBackend, adaptDateTime } from './index';
import {
  BackendOperationLogDto,
  BackendLoginLogDto,
  OperationLogDto,
  LoginLogDto,
  LOG_STATUS
} from '../api/types/log.types';

/**
 * 将后端操作日志转换为前端格式
 */
export const adaptOperationLogFromBackend = (backendLog: BackendOperationLogDto): OperationLogDto => {
  // 判断状态
  const status = backendLog.status?.toLowerCase() === 'success' || backendLog.status?.toLowerCase() === '成功'
    ? 'success' as const
    : 'failed' as const;

  return {
    id: adaptIdFromBackend(backendLog.id),
    userId: adaptIdFromBackend(backendLog.userId),
    userName: backendLog.userName || '未知用户',
    role: backendLog.role || '',
    time: adaptDateTime(backendLog.time),
    module: backendLog.module || '',
    action: backendLog.action,
    target: backendLog.target || '',
    detail: backendLog.detail || '',
    ip: backendLog.ip || '',
    status,
    duration: backendLog.duration || 0
  };
};

/**
 * 将后端登录日志转换为前端格式
 */
export const adaptLoginLogFromBackend = (backendLog: BackendLoginLogDto): LoginLogDto => {
  // 判断状态
  let status: 'success' | 'failed' | 'locked' = 'success';
  if (backendLog.status?.toLowerCase() === 'failed' || backendLog.status?.toLowerCase() === '失败') {
    status = 'failed';
  } else if (backendLog.status?.toLowerCase() === 'locked' || backendLog.status?.toLowerCase() === '锁定') {
    status = 'locked';
  }

  return {
    id: adaptIdFromBackend(backendLog.id),
    userId: adaptIdFromBackend(backendLog.userId),
    userName: backendLog.userName || '未知用户',
    role: backendLog.role || '',
    loginTime: adaptDateTime(backendLog.loginTime),
    ip: backendLog.ip || '',
    userAgent: backendLog.userAgent || '',
    status,
    failReason: backendLog.failReason || undefined
  };
};

/**
 * 格式化日期时间
 */
export const formatLogTime = (dateTime: string): string => {
  if (!dateTime) return '-';
  
  try {
    const date = new Date(dateTime);
    return date.toLocaleString('zh-CN', {
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit'
    });
  } catch {
    return dateTime;
  }
};

/**
 * 获取状态标签信息
 */
export const getLoginStatusInfo = (status: string): { label: string; color: string } => {
  switch (status) {
    case LOG_STATUS.SUCCESS:
      return { label: '成功', color: '#10b981' };
    case LOG_STATUS.FAILED:
      return { label: '失败', color: '#f59e0b' };
    case LOG_STATUS.LOCKED:
      return { label: '锁定', color: '#ef4444' };
    default:
      return { label: status, color: '#6a8ca8' };
  }
};

/**
 * 获取操作状态标签信息
 */
export const getOperationStatusInfo = (status: string): { label: string; color: string } => {
  switch (status) {
    case LOG_STATUS.SUCCESS:
      return { label: '成功', color: '#10b981' };
    case LOG_STATUS.FAILED:
      return { label: '失败', color: '#ef4444' };
    default:
      return { label: status, color: '#6a8ca8' };
  }
};

/**
 * 解析UserAgent获取设备信息
 */
export const parseUserAgent = (userAgent: string): { device: string; browser: string } => {
  if (!userAgent) {
    return { device: 'Unknown', browser: 'Unknown' };
  }

  let device = 'Unknown';
  let browser = 'Unknown';

  // 检测设备类型
  if (userAgent.includes('iPhone')) {
    device = 'iPhone';
  } else if (userAgent.includes('iPad')) {
    device = 'iPad';
  } else if (userAgent.includes('Android')) {
    device = 'Android Phone';
  } else if (userAgent.includes('Windows')) {
    device = 'Windows PC';
  } else if (userAgent.includes('Mac')) {
    device = 'Mac';
  } else if (userAgent.includes('Linux')) {
    device = 'Linux';
  }

  // 检测浏览器
  if (userAgent.includes('Chrome') && !userAgent.includes('Edg')) {
    browser = 'Chrome';
  } else if (userAgent.includes('Safari') && !userAgent.includes('Chrome')) {
    browser = 'Safari';
  } else if (userAgent.includes('Firefox')) {
    browser = 'Firefox';
  } else if (userAgent.includes('Edg')) {
    browser = 'Edge';
  }

  return { device, browser };
};

/**
 * 解析IP地址获取位置
 */
export const parseIpLocation = (ip: string): string => {
  // 实际项目中可能需要调用IP地理位置API
  // 这里简单处理内网IP
  if (ip.startsWith('192.168.') || ip.startsWith('10.') || ip.startsWith('127.')) {
    return '内网';
  }
  return '未知地区';
};

/**
 * 计算日志统计数据
 */
export const calculateLoginStats = (logs: LoginLogDto[]): {
  total: number;
  success: number;
  failed: number;
  locked: number;
} => {
  const total = logs.length;
  const success = logs.filter(l => l.status === LOG_STATUS.SUCCESS).length;
  const failed = logs.filter(l => l.status === LOG_STATUS.FAILED).length;
  const locked = logs.filter(l => l.status === LOG_STATUS.LOCKED).length;
  
  return { total, success, failed, locked };
};

/**
 * 计算操作日志统计数据
 */
export const calculateOperationStats = (logs: OperationLogDto[]): {
  total: number;
  success: number;
  failed: number;
  uniqueUsers: number;
} => {
  const total = logs.length;
  const success = logs.filter(l => l.status === LOG_STATUS.SUCCESS).length;
  const failed = logs.filter(l => l.status === LOG_STATUS.FAILED).length;
  const uniqueUsers = new Set(logs.map(l => l.userId)).size;
  
  return { total, success, failed, uniqueUsers };
};
