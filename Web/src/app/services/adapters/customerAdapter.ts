// 客户数据适配器
import { adaptIdFromBackend, adaptIdToBackend, adaptDateTime } from './index';
import { 
  CustomerDto,
  CustomerStatus
} from '../api/types/customer.types';

// 后端响应类型接口（与auth.types.ts中的相同）
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
}

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
 * 验证客户状态
 */
export const validateCustomerStatus = (status: string): CustomerStatus => {
  const validStatuses = Object.values(CustomerStatus);
  return validStatuses.includes(status as CustomerStatus) ? status as CustomerStatus : CustomerStatus.PENDING;
};

/**
 * 获取客户状态文本
 */
export const getCustomerStatusText = (status: CustomerStatus): string => {
  const statusTexts: Record<CustomerStatus, string> = {
    [CustomerStatus.ACTIVE]: '活跃',
    [CustomerStatus.INACTIVE]: '停用',
    [CustomerStatus.PENDING]: '待激活',
    [CustomerStatus.SUSPENDED]: '暂停'
  };
  
  return statusTexts[status] || '未知';
};

/**
 * 获取客户状态颜色
 */
export const getCustomerStatusColor = (status: CustomerStatus): string => {
  const statusColors: Record<CustomerStatus, string> = {
    [CustomerStatus.ACTIVE]: 'green',
    [CustomerStatus.INACTIVE]: 'gray',
    [CustomerStatus.PENDING]: 'orange',
    [CustomerStatus.SUSPENDED]: 'red'
  };
  
  return statusColors[status] || 'gray';
};

/**
 * 生成客户AppCode
 */
export const generateCustomerAppCode = (customerName: string): string => {
  // 从客户名称生成简码
  const words = customerName.split(/[\s\-_]+/);
  let code = '';
  
  if (words.length === 1) {
    // 单个词：取前4个字符大写
    code = words[0].substring(0, 4).toUpperCase();
  } else {
    // 多个词：取每个词的首字母
    code = words.map(word => word.charAt(0).toUpperCase()).join('');
  }
  
  // 添加时间戳后缀确保唯一性
  const timestamp = Date.now().toString().slice(-4);
  return `${code}${timestamp}`;
};

/**
 * 验证客户代码格式
 */
export const validateCustomerCode = (code: string): boolean => {
  // 客户代码格式：大写字母和数字，长度3-20
  const codeRegex = /^[A-Z0-9]{3,20}$/;
  return codeRegex.test(code);
};

/**
 * 验证客户AppCode格式
 */
export const validateCustomerAppCode = (appCode: string): boolean => {
  // AppCode格式：字母开头，包含字母、数字、下划线，长度4-50
  const appCodeRegex = /^[A-Za-z][A-Za-z0-9_]{3,49}$/;
  return appCodeRegex.test(appCode);
};

/**
 * 格式化客户联系信息
 */
export const formatCustomerContact = (customer: CustomerDto): string => {
  const parts = [];
  
  if (customer.contactPerson) {
    parts.push(customer.contactPerson);
  }
  
  if (customer.contactPhone) {
    parts.push(`电话: ${customer.contactPhone}`);
  }
  
  if (customer.contactEmail) {
    parts.push(`邮箱: ${customer.contactEmail}`);
  }
  
  return parts.join(' | ');
};

/**
 * 检查客户是否可用（活跃状态）
 */
export const isCustomerActive = (customer: CustomerDto): boolean => {
  return customer.status === CustomerStatus.ACTIVE;
};

/**
 * 获取客户设备统计文本
 */
export const getCustomerDeviceStatsText = (customer: CustomerDto): string => {
  return `设备数量: ${customer.deviceCount}`;
};

/**
 * 比较两个客户是否相同
 */
export const areCustomersEqual = (customer1: CustomerDto, customer2: CustomerDto): boolean => {
  return customer1.id === customer2.id;
};

/**
 * 过滤客户列表
 */
export const filterCustomers = (
  customers: CustomerDto[],
  filters: {
    keyword?: string;
    status?: string;
    minDeviceCount?: number;
    maxDeviceCount?: number;
  }
): CustomerDto[] => {
  return customers.filter(customer => {
    // 关键词过滤
    if (filters.keyword) {
      const keyword = filters.keyword.toLowerCase();
      const matches = 
        customer.name.toLowerCase().includes(keyword) ||
        customer.code.toLowerCase().includes(keyword) ||
        customer.appCode.toLowerCase().includes(keyword) ||
        (customer.contactPerson && customer.contactPerson.toLowerCase().includes(keyword)) ||
        (customer.contactEmail && customer.contactEmail.toLowerCase().includes(keyword));
      
      if (!matches) return false;
    }
    
    // 状态过滤
    if (filters.status && customer.status !== filters.status) {
      return false;
    }
    
    // 设备数量过滤
    if (filters.minDeviceCount !== undefined && customer.deviceCount < filters.minDeviceCount) {
      return false;
    }
    
    if (filters.maxDeviceCount !== undefined && customer.deviceCount > filters.maxDeviceCount) {
      return false;
    }
    
    return true;
  });
};