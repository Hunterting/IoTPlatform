// 用户数据适配器
import { adaptIdFromBackend, adaptIdToBackend, adaptDateTime, adaptNullable } from './index';
import {
  UserDto,
  UserDetailDto,
  CreateUserRequest,
  UpdateUserRequest,
  UserFormData,
  UserStatus
} from '../api/types/user.types';

// 后端用户响应类型接口
interface BackendUserDto {
  id: number;
  name: string;
  email: string;
  role: string;
  customerId?: number | null;
  customerName?: string | null;
  appCode?: string | null;
  avatar?: string | null;
  allowedAreaIds?: number[] | null;
  isActive: boolean;
  isSuperAdmin: boolean;
  createdAt: string;
  updatedAt: string;
}

interface BackendUserDetailDto extends BackendUserDto {
  lastLoginAt?: string | null;
}

// 后端创建用户请求
interface BackendCreateUserRequest {
  name: string;
  email: string;
  password: string;
  role: string;
  customerId?: number | null;
  avatar?: string | null;
  allowedAreaIds?: string | null;
  isActive: boolean;
}

// 后端更新用户请求
interface BackendUpdateUserRequest {
  name?: string;
  email?: string;
  role?: string | null;
  avatar?: string | null;
  allowedAreaIds?: string | null;
  isActive?: boolean;
}

/**
 * 将后端用户信息转换为前端格式
 */
export const adaptUserFromBackend = (backendUser: BackendUserDto): UserDto => {
  // 处理 allowedAreaIds
  let allowedAreaIds: string[] | null = null;
  if (backendUser.allowedAreaIds) {
    if (Array.isArray(backendUser.allowedAreaIds)) {
      allowedAreaIds = backendUser.allowedAreaIds.map(id => adaptIdFromBackend(id));
    } else if (typeof backendUser.allowedAreaIds === 'string') {
      // 如果是字符串（逗号分隔），解析它
      allowedAreaIds = backendUser.allowedAreaIds
        .split(',')
        .map(id => id.trim())
        .filter(id => id.length > 0)
        .map(id => adaptIdFromBackend(parseInt(id, 10)));
    }
  }
  
  return {
    id: adaptIdFromBackend(backendUser.id),
    name: backendUser.name,
    email: backendUser.email,
    role: backendUser.role,
    customerId: adaptIdFromBackend(backendUser.customerId),
    customerName: backendUser.customerName || null,
    appCode: backendUser.appCode || null,
    avatar: backendUser.avatar || null,
    allowedAreaIds,
    isActive: backendUser.isActive,
    isSuperAdmin: backendUser.isSuperAdmin,
    createdAt: adaptDateTime(backendUser.createdAt),
    updatedAt: adaptDateTime(backendUser.updatedAt)
  };
};

/**
 * 将后端用户详情转换为前端格式
 */
export const adaptUserDetailFromBackend = (backendUser: BackendUserDetailDto): UserDetailDto => {
  return {
    ...adaptUserFromBackend(backendUser),
    lastLoginAt: backendUser.lastLoginAt ? adaptDateTime(backendUser.lastLoginAt) : null
  };
};

/**
 * 将前端用户信息转换为后端创建格式
 */
export const adaptUserToBackend = (user: Partial<UserDto>): Partial<BackendUserDto> => {
  const backendUser: Partial<BackendUserDto> = {};

  if (user.id !== undefined) {
    backendUser.id = adaptIdToBackend(user.id) as number;
  }
  if (user.name !== undefined) {
    backendUser.name = user.name;
  }
  if (user.email !== undefined) {
    backendUser.email = user.email;
  }
  if (user.role !== undefined) {
    backendUser.role = user.role;
  }
  if (user.customerId !== undefined) {
    backendUser.customerId = adaptIdToBackend(user.customerId);
  }
  if (user.avatar !== undefined) {
    backendUser.avatar = user.avatar;
  }
  if (user.allowedAreaIds !== undefined) {
    backendUser.allowedAreaIds = user.allowedAreaIds?.map(id => adaptIdToBackend(id) as number) || null;
  }
  if (user.isActive !== undefined) {
    backendUser.isActive = user.isActive;
  }
  if (user.isSuperAdmin !== undefined) {
    backendUser.isSuperAdmin = user.isSuperAdmin;
  }

  return backendUser;
};

/**
 * 将前端表单数据转换为后端创建用户请求
 */
export const adaptCreateUserToBackend = (formData: UserFormData): BackendCreateUserRequest => {
  return {
    name: formData.name,
    email: formData.email,
    password: formData.password || '',
    role: formData.role,
    customerId: adaptIdToBackend(formData.customerId),
    avatar: formData.avatar || null,
    // 后端使用逗号分隔的字符串
    allowedAreaIds: formData.allowedAreaIds.length > 0 
      ? formData.allowedAreaIds.map(id => adaptIdToBackend(id) as number).join(',')
      : null,
    isActive: formData.isActive
  };
};

/**
 * 将前端表单数据转换为后端更新用户请求
 */
export const adaptUpdateUserToBackend = (formData: Partial<UserFormData>): BackendUpdateUserRequest => {
  const backendRequest: BackendUpdateUserRequest = {};

  if (formData.name !== undefined) {
    backendRequest.name = formData.name;
  }
  if (formData.email !== undefined) {
    backendRequest.email = formData.email;
  }
  if (formData.role !== undefined) {
    backendRequest.role = formData.role;
  }
  if (formData.avatar !== undefined) {
    backendRequest.avatar = formData.avatar || null;
  }
  if (formData.allowedAreaIds !== undefined) {
    backendRequest.allowedAreaIds = formData.allowedAreaIds.length > 0 
      ? formData.allowedAreaIds.map(id => adaptIdToBackend(id) as number).join(',')
      : null;
  }
  if (formData.isActive !== undefined) {
    backendRequest.isActive = formData.isActive;
  }

  return backendRequest;
};

/**
 * 将后端AllowedAreaIds字符串转换为数组
 */
export const parseAllowedAreaIds = (areaIdsString: string | null | undefined): string[] => {
  if (!areaIdsString) return [];
  return areaIdsString.split(',').map(id => id.trim()).filter(id => id.length > 0);
};

/**
 * 获取用户状态文本
 */
export const getUserStatusText = (isActive: boolean): string => {
  return isActive ? '活跃' : '未激活';
};

/**
 * 获取用户状态值
 */
export const getUserStatus = (isActive: boolean): UserStatus => {
  return isActive ? 'active' : 'inactive';
};

/**
 * 转换状态到布尔值
 */
export const statusToBoolean = (status: UserStatus): boolean => {
  return status === 'active';
};

/**
 * 转换布尔值到状态
 */
export const booleanToStatus = (isActive: boolean): UserStatus => {
  return isActive ? 'active' : 'inactive';
};

/**
 * 获取用户角色颜色
 */
export const getUserRoleColor = (role: string): string => {
  const colors: Record<string, string> = {
    'super_admin': 'from-purple-600 to-indigo-600',
    'admin': 'from-red-500 to-orange-500',
    'operator': 'from-blue-500 to-cyan-500',
    'staff': 'from-gray-500 to-slate-500',
    'viewer': 'from-green-500 to-emerald-500',
  };
  return colors[role] || 'from-gray-500 to-slate-500';
};

/**
 * 获取用户角色显示名称
 */
export const getUserRoleName = (role: string, roleNames?: Record<string, string>): string => {
  if (roleNames && roleNames[role]) {
    return roleNames[role];
  }
  
  const defaultNames: Record<string, string> = {
    'super_admin': '超级管理员',
    'admin': '管理员',
    'operator': '运维人员',
    'staff': '员工',
    'viewer': '查看者',
  };
  
  return defaultNames[role] || role;
};

/**
 * 将用户DTO转换为列表显示格式
 */
export const adaptUserToListItem = (user: UserDto): {
  id: string;
  name: string;
  email: string;
  role: string;
  roleName: string;
  status: UserStatus;
  statusText: string;
  allowedAreaCount: number;
  isSuperAdmin: boolean;
  createdAt: string;
} => {
  return {
    id: user.id,
    name: user.name,
    email: user.email,
    role: user.role,
    roleName: getUserRoleName(user.role),
    status: getUserStatus(user.isActive),
    statusText: getUserStatusText(user.isActive),
    allowedAreaCount: user.allowedAreaIds?.length || 0,
    isSuperAdmin: user.isSuperAdmin,
    createdAt: user.createdAt
  };
};

/**
 * 创建空的表单数据
 */
export const createEmptyUserFormData = (): UserFormData => {
  return {
    name: '',
    email: '',
    password: '',
    role: 'staff',
    customerId: null,
    avatar: null,
    allowedAreaIds: [],
    isActive: true
  };
};

/**
 * 从用户DTO创建表单数据
 */
export const adaptUserToFormData = (user: UserDto): UserFormData => {
  return {
    name: user.name,
    email: user.email,
    password: '', // 编辑时不显示密码
    role: user.role,
    customerId: user.customerId,
    avatar: user.avatar,
    allowedAreaIds: user.allowedAreaIds || [],
    isActive: user.isActive
  };
};
