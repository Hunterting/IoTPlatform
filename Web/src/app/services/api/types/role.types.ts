/**
 * 角色相关类型定义
 * 对应后端 RoleDto、CreateRoleRequest、UpdateRoleRequest
 */

// 后端角色DTO
export interface BackendRoleDto {
  id: number;
  code: string;
  name: string;
  description?: string | null;
  permissions?: string | null;
  permissionList?: string[] | null;
  dataScope: string;
  appCode?: string | null;
  isSystem: boolean;
  createdAt: string;
  updatedAt: string;
}

// 前端角色DTO
export interface RoleDto {
  id: string;
  code: string;
  name: string;
  description: string | null;
  permissions: string[];
  dataScope: string;
  appCode: string | null;
  isSystem: boolean;
  createdAt: string;
  updatedAt: string;
}

// 创建角色请求
export interface CreateRoleRequest {
  code: string;
  name: string;
  description?: string;
  permissions?: string[];
  dataScope: string;
}

// 更新角色请求
export interface UpdateRoleRequest {
  name: string;
  description?: string;
  permissions?: string[];
  dataScope: string;
}

// 角色列表项（简化版）
export interface RoleListItem {
  id: string;
  code: string;
  name: string;
  description: string | null;
  permissionCount: number;
  dataScope: string;
  isSystem: boolean;
  createdAt: string;
  updatedAt: string;
}

// 角色筛选条件
export interface RoleFilters {
  keyword?: string;
  dataScope?: string;
  appCode?: string;
}

// 角色权限分组
export interface RolePermissionGroup {
  name: string;
  permissions: Array<{
    code: string;
    name: string;
    description?: string;
  }>;
}

// 数据范围选项
export interface DataScopeOption {
  value: string;
  label: string;
  description: string;
}

// 系统角色代码（不可删除）
export const SYSTEM_ROLE_CODES = ['super_admin', 'admin'] as const;

// 数据范围常量
export const DATA_SCOPE = {
  ALL: 'ALL',
  CUSTOM: 'CUSTOM'
} as const;
