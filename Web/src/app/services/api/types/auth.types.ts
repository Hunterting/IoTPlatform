// 认证相关类型定义

// 登录请求
export interface LoginRequest {
  email: string;
  password: string;
}

// 切换客户请求
export interface SwitchCustomerRequest {
  customerId: number;
}

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
  allowedAreaIds?: string[] | null; // 后端是List<long>?，前端转为string[]
  permissions?: string[]; // 后端下发的有效权限（单一事实来源 = DB Role.Permissions）
  // 多客户授权模型：后端 UserDto.ManagedCustomerIds；仅 admin 有值（下级/超管为 null）。
  // 后端 List<long>? 序列化为 number[] | null，前端消费见 M09 衔接说明。
  managedCustomerIds?: number[] | null;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

// 用户详情
export interface UserDetailDto extends UserDto {
  lastLoginAt?: string | null;
}

// 客户信息
export interface CustomerDto {
  id: string; // 后端是long，前端转为string
  name: string;
  code: string;
  appCode: string;
  contactPerson?: string | null;
  contactPhone?: string | null;
  contactEmail?: string | null;
  address?: string | null;
  status: string;
  createdAt: string;
  deviceCount: number;
  maxDeviceCount?: number | null; // 配额字段：后端仅超管下发，非超管为 null/缺失
}

// 登录响应
export interface LoginResponse {
  token: string;
  user: UserDto;
  currentCustomer?: CustomerDto | null;
}

// 注册请求
export interface RegisterRequest {
  name: string;
  email: string;
  password: string;
  role?: string;
  customerId?: number | null;
}

// 修改密码请求
export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
}

// 重置密码请求
export interface ResetPasswordRequest {
  email: string;
  token: string;
  newPassword: string;
}

// 忘记密码请求
export interface ForgotPasswordRequest {
  email: string;
}

// 用户角色枚举
export enum UserRole {
  SUPER_ADMIN = 'super_admin',
  ADMIN = 'admin',
  OPERATOR = 'operator',
  VIEWER = 'viewer',
  GUEST = 'guest'
}

// 用户状态枚举
export enum UserStatus {
  ACTIVE = 'active',
  INACTIVE = 'inactive',
  SUSPENDED = 'suspended'
}

// 客户状态枚举
export enum CustomerStatus {
  ACTIVE = 'active',
  INACTIVE = 'inactive',
  PENDING = 'pending',
  SUSPENDED = 'suspended'
}