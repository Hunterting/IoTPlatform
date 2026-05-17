// 用户管理相关类型定义

// 用户信息
export interface UserDto {
  id: string; // 后端是long，前端转为string
  name: string;
  email: string;
  role: string;
  customerId?: string | null; // 后端是long?，前端转为string | null
  customerName?: string | null;
  appCode?: string | null;
  avatar?: string | null;
  allowedAreaIds?: string[] | null; // 后端是List<long>，前端转为string[]
  isActive: boolean; // 后端是IsActive，前端用camelCase
  isSuperAdmin: boolean; // 后端是IsSuperAdmin
  createdAt: string;
  updatedAt: string;
}

// 用户详情（包含登录信息）
export interface UserDetailDto extends UserDto {
  lastLoginAt?: string | null;
}

// 创建用户请求
export interface CreateUserRequest {
  name: string;
  email: string;
  password: string;
  role: string;
  customerId?: number | null;
  avatar?: string | null;
  allowedAreaIds?: string | null; // 后端用逗号分隔的字符串
  isActive: boolean;
}

// 更新用户请求
export interface UpdateUserRequest {
  name?: string;
  email?: string;
  role?: string | null;
  avatar?: string | null;
  allowedAreaIds?: string | null; // 后端用逗号分隔的字符串
  isActive?: boolean;
}

// 用户列表查询参数
export interface UserFilters {
  keyword?: string;
  role?: string;
  customerId?: string;
  isActive?: boolean;
}

// 用户列表响应
export interface UserListResponse {
  items: UserDto[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

// 用户状态枚举（前端使用）
export type UserStatus = 'active' | 'inactive';

// 用户角色（前端使用）
export interface UserRole {
  code: string;
  name: string;
  description?: string;
  dataScope?: 'ALL' | 'CUSTOM';
}

// 前端用户表单数据
export interface UserFormData {
  name: string;
  email: string;
  password?: string;
  role: string;
  customerId?: string | null;
  avatar?: string | null;
  allowedAreaIds: string[];
  isActive: boolean;
}

// 密码修改请求
export interface ChangePasswordRequest {
  oldPassword: string;
  newPassword: string;
}

// 用户统计信息
export interface UserStatsDto {
  totalUsers: number;
  activeUsers: number;
  inactiveUsers: number;
  byRole: Record<string, number>;
  recentCreated: UserDto[];
}

// 用户批量操作请求
export interface BatchUserOperationRequest {
  userIds: string[];
  operation: 'enable' | 'disable' | 'delete';
}
