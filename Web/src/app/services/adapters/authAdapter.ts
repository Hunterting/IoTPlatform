// 认证数据适配器
import { adaptIdFromBackend, adaptIdToBackend, adaptDateTime } from './index';
import { 
  LoginResponse, 
  UserDto, 
  CustomerDto, 
  UserDetailDto,
  UserRole,
  UserStatus,
  CustomerStatus 
} from '../api/types/auth.types';

// 后端响应类型接口
interface BackendLoginResponse {
  token: string;
  user: BackendUserDto;
  currentCustomer?: BackendCustomerDto | null;
}

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
  createdAt: string;
  updatedAt: string;
}

interface BackendUserDetailDto extends BackendUserDto {
  lastLoginAt?: string | null;
}

interface BackendCustomerDto {
  id: number;
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
  projectCount?: number;
}

/**
 * 将后端登录响应转换为前端格式
 */
export const adaptLoginResponse = (backendResponse: BackendLoginResponse): LoginResponse => {
  return {
    token: backendResponse.token,
    user: adaptUserFromBackend(backendResponse.user),
    currentCustomer: backendResponse.currentCustomer 
      ? adaptCustomerFromBackend(backendResponse.currentCustomer)
      : null
  };
};

/**
 * 将后端用户信息转换为前端格式
 */
export const adaptUserFromBackend = (backendUser: BackendUserDto): UserDto => {
  return {
    id: adaptIdFromBackend(backendUser.id),
    name: backendUser.name,
    email: backendUser.email,
    role: backendUser.role,
    customerId: adaptIdFromBackend(backendUser.customerId),
    customerName: backendUser.customerName,
    appCode: backendUser.appCode,
    avatar: backendUser.avatar,
    allowedAreaIds: backendUser.allowedAreaIds?.map(id => adaptIdFromBackend(id)),
    isActive: backendUser.isActive,
    createdAt: adaptDateTime(backendUser.createdAt),
    updatedAt: adaptDateTime(backendUser.updatedAt)
  };
};

/**
 * 将后端用户详情转换为前端格式
 */
export const adaptUserDetailFromBackend = (backendUser: BackendUserDetailDto): UserDetailDto => {
  const userDto = adaptUserFromBackend(backendUser);
  return {
    ...userDto,
    lastLoginAt: adaptDateTime(backendUser.lastLoginAt)
  };
};

/**
 * 将前端用户信息转换为后端格式
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
  if (user.appCode !== undefined) {
    backendUser.appCode = user.appCode;
  }
  if (user.avatar !== undefined) {
    backendUser.avatar = user.avatar;
  }
  if (user.allowedAreaIds !== undefined) {
    backendUser.allowedAreaIds = user.allowedAreaIds?.map(id => adaptIdToBackend(id) as number);
  }
  if (user.isActive !== undefined) {
    backendUser.isActive = user.isActive;
  }
  
  return backendUser;
};

/**
 * 将后端客户信息转换为前端格式
 */
export const adaptCustomerFromBackend = (backendCustomer: BackendCustomerDto): CustomerDto => {
  return {
    id: adaptIdFromBackend(backendCustomer.id),
    name: backendCustomer.name,
    code: backendCustomer.code,
    appCode: backendCustomer.appCode,
    contactPerson: backendCustomer.contactPerson,
    contactPhone: backendCustomer.contactPhone,
    contactEmail: backendCustomer.contactEmail,
    address: backendCustomer.address,
    status: backendCustomer.status,
    createdAt: adaptDateTime(backendCustomer.createdAt),
    deviceCount: backendCustomer.deviceCount
  };
};

/**
 * 将前端客户信息转换为后端格式
 */
export const adaptCustomerToBackend = (customer: Partial<CustomerDto>): Partial<BackendCustomerDto> => {
  const backendCustomer: Partial<BackendCustomerDto> = {};
  
  if (customer.id !== undefined) {
    backendCustomer.id = adaptIdToBackend(customer.id) as number;
  }
  if (customer.name !== undefined) {
    backendCustomer.name = customer.name;
  }
  if (customer.code !== undefined) {
    backendCustomer.code = customer.code;
  }
  if (customer.appCode !== undefined) {
    backendCustomer.appCode = customer.appCode;
  }
  if (customer.contactPerson !== undefined) {
    backendCustomer.contactPerson = customer.contactPerson;
  }
  if (customer.contactPhone !== undefined) {
    backendCustomer.contactPhone = customer.contactPhone;
  }
  if (customer.contactEmail !== undefined) {
    backendCustomer.contactEmail = customer.contactEmail;
  }
  if (customer.address !== undefined) {
    backendCustomer.address = customer.address;
  }
  if (customer.status !== undefined) {
    backendCustomer.status = customer.status;
  }
  
  return backendCustomer;
};

/**
 * 验证用户角色
 */
export const validateUserRole = (role: string): UserRole => {
  const validRoles = Object.values(UserRole);
  return validRoles.includes(role as UserRole) ? role as UserRole : UserRole.VIEWER;
};

/**
 * 验证用户状态
 */
export const validateUserStatus = (status: string): UserStatus => {
  const validStatuses = Object.values(UserStatus);
  return validStatuses.includes(status as UserStatus) ? status as UserStatus : UserStatus.INACTIVE;
};

/**
 * 验证客户状态
 */
export const validateCustomerStatus = (status: string): CustomerStatus => {
  const validStatuses = Object.values(CustomerStatus);
  return validStatuses.includes(status as CustomerStatus) ? status as CustomerStatus : CustomerStatus.PENDING;
};

/**
 * 检查用户是否有权限
 */
export const checkUserPermission = (
  user: UserDto,
  requiredRole: UserRole,
  requiredCustomerId?: string
): boolean => {
  // 检查角色权限
  const roleHierarchy = {
    [UserRole.SUPER_ADMIN]: 5,
    [UserRole.ADMIN]: 4,
    [UserRole.OPERATOR]: 3,
    [UserRole.VIEWER]: 2,
    [UserRole.GUEST]: 1
  };
  
  const userRoleLevel = roleHierarchy[user.role as UserRole] || 0;
  const requiredRoleLevel = roleHierarchy[requiredRole] || 0;
  
  if (userRoleLevel < requiredRoleLevel) {
    return false;
  }
  
  // 检查客户权限（如果需要）
  if (requiredCustomerId && user.customerId !== requiredCustomerId) {
    // 超级管理员可以访问所有客户
    if (user.role !== UserRole.SUPER_ADMIN) {
      return false;
    }
  }
  
  return true;
};

// ------------------------------------------------------------------------------
// AuthContext 专用适配器函数
// ------------------------------------------------------------------------------

/**
 * 将登录响应转换为前端AuthContext的用户格式
 */
export const adaptLoginResponseToUser = (loginResponse: LoginResponse): any => {
  return {
    id: loginResponse.user.id,
    name: loginResponse.user.name,
    email: loginResponse.user.email,
    role: loginResponse.user.role,
    customerId: loginResponse.user.customerId,
    appCode: loginResponse.user.appCode,
    avatar: loginResponse.user.avatar,
    allowedAreaIds: loginResponse.user.allowedAreaIds,
    isActive: loginResponse.user.isActive
  };
};

/**
 * 将后端客户DTO转换为AuthContext的Customer格式
 */
export const adaptCustomerDtoToCustomer = (customerDto: CustomerDto): any => {
  return {
    id: customerDto.id,
    name: customerDto.name,
    code: customerDto.code,
    appCode: customerDto.appCode,
    contact: customerDto.contactPerson || null,
    phone: customerDto.contactPhone || null,
    address: customerDto.address || null,
    status: customerDto.status,
    createdAt: customerDto.createdAt,
    deviceCount: customerDto.deviceCount,
    projectCount: customerDto.projectCount ?? 0,
    projects: [] // 真实项目列表在详情弹窗中单独加载
  };
};

/**
 * 将前端Customer格式转换为创建客户的请求
 */
export const adaptCreateCustomerRequest = (
  customer: Omit<any, 'id' | 'createdAt' | 'deviceCount' | 'status' | 'projects'>
): any => {
  return {
    name: customer.name,
    code: customer.code,
    appCode: customer.appCode,
    contact: customer.contact,
    phone: customer.phone,
    address: customer.address
  };
};

/**
 * 将前端Customer更新数据转换为更新客户的请求
 */
export const adaptUpdateCustomerRequest = (
  updates: Partial<any>
): any => {
  const request: any = {};
  
  if (updates.name !== undefined) request.name = updates.name;
  if (updates.contact !== undefined) request.contact = updates.contact;
  if (updates.phone !== undefined) request.phone = updates.phone;
  if (updates.address !== undefined) request.address = updates.address;
  if (updates.status !== undefined) request.status = updates.status;
  
  return request;
};