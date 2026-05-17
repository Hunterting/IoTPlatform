/**
 * 日志相关类型定义
 * 对应后端 OperationLogDto、LoginLogDto
 */

// 后端操作日志DTO
export interface BackendOperationLogDto {
  id: number;
  userId: number;
  userName?: string | null;
  role?: string | null;
  time: string;
  module?: string | null;
  action: string;
  target?: string | null;
  detail?: string | null;
  ip?: string | null;
  status: string;
  duration?: number | null;
  appCode?: string | null;
}

// 后端登录日志DTO
export interface BackendLoginLogDto {
  id: number;
  userId: number;
  userName?: string | null;
  role?: string | null;
  loginTime: string;
  ip?: string | null;
  userAgent?: string | null;
  status: string;
  failReason?: string | null;
  appCode?: string | null;
}

// 前端操作日志DTO
export interface OperationLogDto {
  id: string;
  userId: string;
  userName: string;
  role: string;
  time: string;
  module: string;
  action: string;
  target: string;
  detail: string;
  ip: string;
  status: 'success' | 'failed';
  duration: number;
}

// 前端登录日志DTO
export interface LoginLogDto {
  id: string;
  userId: string;
  userName: string;
  role: string;
  loginTime: string;
  ip: string;
  userAgent: string;
  status: 'success' | 'failed' | 'locked';
  failReason?: string;
}

// 操作日志筛选条件
export interface OperationLogFilters {
  module?: string;
  action?: string;
  userId?: number;
  startTime?: string;
  endTime?: string;
  keyword?: string;
}

// 登录日志筛选条件
export interface LoginLogFilters {
  userId?: number;
  status?: string;
  startTime?: string;
  endTime?: string;
  keyword?: string;
}

// 日志统计信息
export interface LogStatistics {
  totalCount: number;
  successCount: number;
  failedCount: number;
  todayCount: number;
}

// 操作日志统计
export interface OperationLogStatistics extends LogStatistics {
  moduleStats: Record<string, number>;
  topUsers: Array<{
    userId: string;
    userName: string;
    count: number;
  }>;
}

// 登录日志统计
export interface LoginLogStatistics extends LogStatistics {
  lockedCount: number;
  topLocations: Array<{
    location: string;
    count: number;
  }>;
}

// 日志状态枚举
export const LOG_STATUS = {
  SUCCESS: 'success',
  FAILED: 'failed',
  LOCKED: 'locked'
} as const;

// 操作日志模块颜色映射
export const MODULE_COLORS: Record<string, string> = {
  '设备管理': '#00c3ff',
  '用户管理': '#a855f7',
  '工单管理': '#f59e0b',
  '档案管理': '#10b981',
  '智能分析': '#3b82f6',
  '客户管理': '#ec4899',
  '区域管理': '#14b8a6',
  '系统设置': '#6366f1',
  '视频监控': '#84cc16'
};
