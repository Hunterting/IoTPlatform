/**
 * 角色数据适配器
 * 负责前后端数据格式转换
 */
import { adaptIdFromBackend, adaptDateTime } from './index';
import {
  BackendRoleDto,
  RoleDto,
  CreateRoleRequest,
  UpdateRoleRequest,
  SYSTEM_ROLE_CODES,
  DATA_SCOPE
} from '../api/types/role.types';

/**
 * 将后端角色信息转换为前端格式
 */
export const adaptRoleFromBackend = (backendRole: BackendRoleDto): RoleDto => {
  // 处理权限列表
  let permissions: string[] = [];
  if (backendRole.permissionList && Array.isArray(backendRole.permissionList)) {
    permissions = backendRole.permissionList;
  } else if (backendRole.permissions && typeof backendRole.permissions === 'string') {
    // 如果是逗号分隔的字符串，解析它
    permissions = backendRole.permissions
      .split(',')
      .map(p => p.trim())
      .filter(p => p.length > 0);
  }

  return {
    id: adaptIdFromBackend(backendRole.id),
    code: backendRole.code,
    name: backendRole.name,
    description: backendRole.description || null,
    permissions,
    dataScope: backendRole.dataScope || DATA_SCOPE.CUSTOM,
    appCode: backendRole.appCode || null,
    isSystem: backendRole.isSystem,
    createdAt: adaptDateTime(backendRole.createdAt),
    updatedAt: adaptDateTime(backendRole.updatedAt)
  };
};

/**
 * 将前端角色信息转换为创建角色请求
 */
export const adaptCreateRoleToBackend = (role: Partial<RoleDto>): CreateRoleRequest => {
  return {
    code: role.code || '',
    name: role.name || '',
    description: role.description || undefined,
    permissions: role.permissions || [],
    dataScope: role.dataScope || DATA_SCOPE.CUSTOM
  };
};

/**
 * 将前端角色信息转换为更新角色请求
 */
export const adaptUpdateRoleToBackend = (role: Partial<RoleDto>): UpdateRoleRequest => {
  return {
    name: role.name || '',
    description: role.description || undefined,
    permissions: role.permissions || [],
    dataScope: role.dataScope || DATA_SCOPE.CUSTOM
  };
};

/**
 * 判断角色是否为系统角色（不可删除）
 */
export const isSystemRole = (roleCode: string): boolean => {
  return SYSTEM_ROLE_CODES.includes(roleCode as typeof SYSTEM_ROLE_CODES[number]);
};

/**
 * 获取数据范围文本
 */
export const getDataScopeText = (dataScope: string): string => {
  return dataScope === DATA_SCOPE.ALL ? '全部数据' : '自定义指定';
};

/**
 * 获取数据范围描述
 */
export const getDataScopeDescription = (dataScope: string): string => {
  return dataScope === DATA_SCOPE.ALL
    ? '可查看租户下所有区域和设备，无需单独分配'
    : '仅可查看分配给用户的特定区域及其数据';
};

/**
 * 格式化角色创建/更新时间
 */
export const formatRoleTime = (dateTime: string): string => {
  if (!dateTime) return '-';
  
  try {
    const date = new Date(dateTime);
    return date.toLocaleString('zh-CN', {
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit'
    });
  } catch {
    return dateTime;
  }
};

/**
 * 验证角色数据
 */
export const validateRole = (role: Partial<RoleDto>): string | null => {
  if (!role.name || role.name.trim() === '') {
    return '角色名称不能为空';
  }
  
  if (!role.code || role.code.trim() === '') {
    return '角色代码不能为空';
  }
  
  if (!/^[a-z_]+$/.test(role.code)) {
    return '角色代码只能包含小写字母和下划线';
  }
  
  return null;
};
