// 认证API服务
import { http, tokenManager } from './httpClient';
import { withErrorHandling, handleSingleRequest, handleUpdateRequest } from './commonApi';
import { 
  LoginRequest, 
  LoginResponse, 
  UserDto,
  CustomerDto,
  SwitchCustomerRequest,
  RegisterRequest,
  ChangePasswordRequest,
  ResetPasswordRequest,
  ForgotPasswordRequest
} from './types/auth.types';
import { adaptLoginResponse, adaptUserFromBackend } from '../adapters/authAdapter';
import { ApiResponse } from './types';

// API路径
const AUTH_API_PATHS = {
  LOGIN: '/auth/login',
  LOGOUT: '/auth/logout',
  CURRENT_USER: '/auth/me',
  SWITCH_CUSTOMER: '/auth/switch-customer',
  REGISTER: '/auth/register',
  CHANGE_PASSWORD: '/auth/change-password',
  FORGOT_PASSWORD: '/auth/forgot-password',
  RESET_PASSWORD: '/auth/reset-password',
  REFRESH_TOKEN: '/auth/refresh-token',
  VERIFY_EMAIL: '/auth/verify-email',
  RESEND_VERIFICATION: '/auth/resend-verification'
};

/**
 * 用户登录
 */
export const login = withErrorHandling(async (credentials: LoginRequest): Promise<LoginResponse> => {
  const response = await http.post<ApiResponse<LoginResponse>>(AUTH_API_PATHS.LOGIN, credentials);
  
  // 保存token
  if (response.data.data.token) {
    tokenManager.setToken(response.data.data.token);
  }
  
  return response.data.data;
});

/**
 * 用户登出
 */
export const logout = withErrorHandling(async (): Promise<void> => {
  try {
    // 尝试调用后端登出接口
    await http.post(AUTH_API_PATHS.LOGOUT);
  } catch (error) {
    // 即使后端登出失败，也要清除本地token
    console.warn('后端登出失败，但已清除本地token:', error);
  } finally {
    // 清除本地token
    tokenManager.clearToken();
  }
});

/**
 * 获取当前用户信息
 */
export const getCurrentUser = withErrorHandling(async (): Promise<UserDto> => {
  const response = await http.get<ApiResponse<LoginResponse>>(AUTH_API_PATHS.CURRENT_USER);
  return response.data.data.user;
});

/**
 * 切换当前客户（仅超级管理员）
 */
export const switchCurrentCustomer = withErrorHandling(async (request: SwitchCustomerRequest): Promise<LoginResponse> => {
  const response = await http.post<ApiResponse<LoginResponse>>(AUTH_API_PATHS.SWITCH_CUSTOMER, request);
  
  // 更新token（如果返回了新token）
  if (response.data.data.token) {
    tokenManager.setToken(response.data.data.token);
  }
  
  return response.data.data;
});

/**
 * 用户注册
 */
export const register = withErrorHandling(async (userData: RegisterRequest): Promise<UserDto> => {
  const response = await http.post<ApiResponse<UserDto>>(AUTH_API_PATHS.REGISTER, userData);
  return response.data.data;
});

/**
 * 修改密码
 */
export const changePassword = withErrorHandling(async (request: ChangePasswordRequest): Promise<void> => {
  await http.post(AUTH_API_PATHS.CHANGE_PASSWORD, request);
});

/**
 * 忘记密码请求
 */
export const forgotPassword = withErrorHandling(async (request: ForgotPasswordRequest): Promise<void> => {
  await http.post(AUTH_API_PATHS.FORGOT_PASSWORD, request);
});

/**
 * 重置密码
 */
export const resetPassword = withErrorHandling(async (request: ResetPasswordRequest): Promise<void> => {
  await http.post(AUTH_API_PATHS.RESET_PASSWORD, request);
});

/**
 * 刷新token
 */
export const refreshToken = withErrorHandling(async (): Promise<string> => {
  const response = await http.post<ApiResponse<{ token: string }>>(AUTH_API_PATHS.REFRESH_TOKEN);
  
  if (response.data.data.token) {
    tokenManager.setToken(response.data.data.token);
  }
  
  return response.data.data.token;
});

/**
 * 验证邮箱
 */
export const verifyEmail = withErrorHandling(async (token: string): Promise<void> => {
  await http.post(AUTH_API_PATHS.VERIFY_EMAIL, { token });
});

/**
 * 重新发送验证邮件
 */
export const resendVerification = withErrorHandling(async (email: string): Promise<void> => {
  await http.post(AUTH_API_PATHS.RESEND_VERIFICATION, { email });
});

/**
 * 检查登录状态
 */
export const checkAuthStatus = withErrorHandling(async (): Promise<boolean> => {
  try {
    // 检查本地是否有token
    if (!tokenManager.isAuthenticated()) {
      return false;
    }
    
    // 尝试获取当前用户信息验证token是否有效
    await getCurrentUser();
    return true;
  } catch (error) {
    // token无效或过期
    tokenManager.clearToken();
    return false;
  }
});

/**
 * 获取用户权限
 */
export const getUserPermissions = withErrorHandling(async (): Promise<string[]> => {
  try {
    const user = await getCurrentUser();
    // 这里可以根据用户角色返回权限列表
    // 实际实现可能需要调用专门的权限接口
    return getUserPermissionsByRole(user.role);
  } catch (error) {
    return [];
  }
});

/**
 * 根据用户角色获取权限列表
 */
const getUserPermissionsByRole = (role: string): string[] => {
  const permissions: Record<string, string[]> = {
    'super_admin': [
      'user:create', 'user:read', 'user:update', 'user:delete',
      'customer:create', 'customer:read', 'customer:update', 'customer:delete',
      'device:create', 'device:read', 'device:update', 'device:delete',
      'area:create', 'area:read', 'area:update', 'area:delete',
      'dashboard:read', 'report:read', 'settings:read', 'settings:update'
    ],
    'admin': [
      'user:read', 'user:create', 'user:update',
      'customer:read', 'customer:update',
      'device:create', 'device:read', 'device:update', 'device:delete',
      'area:create', 'area:read', 'area:update', 'area:delete',
      'dashboard:read', 'report:read', 'settings:read'
    ],
    'operator': [
      'device:read', 'device:update',
      'area:read',
      'dashboard:read', 'report:read'
    ],
    'viewer': [
      'device:read',
      'area:read',
      'dashboard:read'
    ],
    'guest': [
      'dashboard:read'
    ]
  };
  
  return permissions[role] || permissions['guest'];
};

/**
 * 检查用户是否有特定权限
 */
export const hasPermission = async (permission: string): Promise<boolean> => {
  try {
    const permissions = await getUserPermissions();
    return permissions.includes(permission);
  } catch (error) {
    return false;
  }
};

/**
 * 获取可访问的客户列表（用于切换客户）
 */
export const getAccessibleCustomers = withErrorHandling(async (): Promise<CustomerDto[]> => {
  // 注意：这个API可能需要根据实际后端实现进行调整
  // 假设后端有一个接口返回用户可以访问的客户列表
  const response = await http.get<ApiResponse<CustomerDto[]>>('/customers/accessible');
  return response.data.data;
});

/**
 * 更新用户个人信息
 */
export const updateUserProfile = withErrorHandling(async (userData: Partial<UserDto>): Promise<UserDto> => {
  const response = await http.put<ApiResponse<UserDto>>('/users/profile', userData);
  return response.data.data;
});

/**
 * 更新用户头像
 */
export const updateUserAvatar = withErrorHandling(async (avatarFile: File): Promise<{ avatarUrl: string }> => {
  const formData = new FormData();
  formData.append('avatar', avatarFile);
  
  const response = await http.post<ApiResponse<{ avatarUrl: string }>>('/users/avatar', formData, {
    headers: {
      'Content-Type': 'multipart/form-data',
    }
  });
  
  return response.data.data;
});

// 认证API服务统一导出
export const authApi = {
  login,
  logout,
  getCurrentUser,
  switchCurrentCustomer,
  register,
  changePassword,
  forgotPassword,
  resetPassword,
  refreshToken,
  verifyEmail,
  resendVerification,
  checkAuthStatus,
  getUserPermissions,
  hasPermission,
  getAccessibleCustomers,
  updateUserProfile,
  updateUserAvatar,
  
  // Token管理函数
  tokenManager,
  
  // 常量
  PATHS: AUTH_API_PATHS
};