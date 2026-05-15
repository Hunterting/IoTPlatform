import { createContext, useContext, useState, ReactNode, useCallback, useEffect } from 'react';
import { DEFAULT_ROLES, RoleDefinition, Permission } from '@/app/config/permissions';
import { authApi } from '../services/api/authApi';
import { customerApi } from '../services/api/customerApi';
import { 
  adaptLoginResponseToUser,
  adaptCustomerDtoToCustomer,
  adaptCreateCustomerRequest,
  adaptUpdateCustomerRequest 
} from '../services/adapters';

export type UserRole = 'super_admin' | 'admin' | 'operator' | 'chef' | 'staff';

export interface User {
  id: string;
  name: string;
  email: string;
  role: string;
  customerId?: string;
  appCode?: string;
  avatar?: string;
  allowedAreaIds?: string[];
}

// 项目下的合同
export interface Contract {
  id: string;
  name: string;
  type: 'service' | 'purchase' | 'other';
  uploadDate: string;
  fileSize: string;
  fileUrl: string;
  file?: File;
}

// 工作纪要
export interface WorkSummary {
  id: string;
  feedbackPerson: string;
  assignee: string;
  assistant?: string;
  workContent: string;
  date: string;
}

// 项目
export interface Project {
  id: string;
  name: string;
  address: string;
  deviceCount: number;
  onlineDate: string;
  status: 'planning' | 'building' | 'online' | 'offline';
  contracts: Contract[];
  workSummaries: WorkSummary[];
}

export interface Customer {
  id: string;
  name: string;
  code: string;
  appCode: string;
  contact: string;
  phone: string;
  address: string;
  status: 'active' | 'inactive';
  createdAt: string;
  deviceCount: number;
  projectCount: number; // 项目数量（后端返回）
  projects: Project[]; // 真实项目列表（详情弹窗用）
}

interface AuthContextType {
  user: User | null;
  customers: Customer[];
  currentCustomer: Customer | null;
  roles: Record<string, RoleDefinition>;
  loading: boolean;
  error: string | null;

  // Actions
  clearError: () => void;
  refreshCustomers: () => Promise<void>;
  login: (email: string, password: string) => Promise<void>;
  logout: () => void;
  switchCustomer: (customerId: string) => void;
  addCustomer: (customer: Omit<Customer, 'id' | 'createdAt' | 'deviceCount' | 'status' | 'projects'>) => void;
  updateUser: (userId: string, updates: Partial<User>) => void;
  updateCustomer: (customerId: string, updates: Partial<Customer>) => void;
  updateCustomerProjects: (customerId: string, projects: Project[]) => void;

  // Role Management
  addRole: (role: RoleDefinition) => void;
  updateRole: (role: RoleDefinition) => void;
  deleteRole: (roleCode: string) => void;

  // RBAC Helpers
  hasPermission: (permission: Permission) => boolean;
  getUserRole: () => RoleDefinition | null;
}

const AuthContext = createContext<AuthContextType | undefined>(undefined);

// 初始状态，API加载前使用空数据



// ─── Provider ─────────────────────────────────────────────────────────────────
export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null);
  const [customers, setCustomers] = useState<Customer[]>([]);
  const [currentCustomer, setCurrentCustomer] = useState<Customer | null>(null);
  const [roles, setRoles] = useState<Record<string, RoleDefinition>>(DEFAULT_ROLES);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // 加载客户列表 - 只在用户登录后加载
  useEffect(() => {
    const loadCustomers = async () => {
      // 只有已登录用户才加载客户列表
      const token = localStorage.getItem('token');
      if (!token) {
        return;
      }
      
      try {
        setLoading(true);
        const response = await customerApi.getCustomers(1, 100);
        // 注意：httpClient响应拦截器返回的是AxiosResponse，data是ApiResponse
        if (response.data.code === 200) {
          const adaptedCustomers = response.data.data.items.map(adaptCustomerDtoToCustomer);
          setCustomers(adaptedCustomers);
        }
      } catch (err) {
        console.error('Failed to load customers:', err);
        setError('加载客户列表失败');
      } finally {
        setLoading(false);
      }
    };

    loadCustomers();
  }, []);

  // 检查本地存储的登录状态
  useEffect(() => {
    const storedUser = localStorage.getItem('user');
    const storedToken = localStorage.getItem('token');
    
    if (storedUser && storedToken) {
      try {
        const parsedUser = JSON.parse(storedUser);
        setUser(parsedUser);
        
        // 如果有客户ID，设置当前客户
        if (parsedUser.customerId) {
          const storedCustomer = localStorage.getItem('currentCustomer');
          if (storedCustomer) {
            setCurrentCustomer(JSON.parse(storedCustomer));
          }
        }
      } catch (err) {
        console.error('Failed to parse stored user:', err);
        localStorage.removeItem('user');
        localStorage.removeItem('token');
        localStorage.removeItem('currentCustomer');
      }
    }
  }, []);

  const login = async (email: string, password: string) => {
    try {
      setLoading(true);
      setError(null);
      
      const response = await authApi.login({ email, password });
      console.log('Login success:', response);
      if (response) {
        const adaptedUser = adaptLoginResponseToUser(response);
        
        // 保存用户信息和token到localStorage
        localStorage.setItem('user', JSON.stringify(adaptedUser));
        localStorage.setItem('token', response.token);
        
        setUser(adaptedUser);
        
        // 如果是超级管理员，设置currentCustomer为null
        if (adaptedUser.role === 'super_admin') {
          setCurrentCustomer(null);
          localStorage.removeItem('currentCustomer');
          // 超级管理员登录后刷新客户列表
          const customersResponse = await customerApi.getCustomers(1, 100);
          if (customersResponse.data.code === 200) {
            const adaptedCustomers = customersResponse.data.data.items.map(adaptCustomerDtoToCustomer);
            setCustomers(adaptedCustomers);
          }
        } else if (adaptedUser.customerId) {
          // 根据客户ID查找客户
          const customerResponse = await customerApi.getCustomer(adaptedUser.customerId);
          if (customerResponse.data.code === 200) {
            const customer = adaptCustomerDtoToCustomer(customerResponse.data.data);
            setCurrentCustomer(customer);
            localStorage.setItem('currentCustomer', JSON.stringify(customer));
          }
        }
      } else {
        throw new Error('登录失败');
      }
    } catch (err) {
      console.error('Login error:', err);
      setError(err instanceof Error ? err.message : '登录失败，请检查网络连接');
      throw err;
    } finally {
      setLoading(false);
    }
  };

  const logout = async () => {
    try {
      // 调用后端登出API
      await authApi.logout();
    } catch (err) {
      console.error('Logout error:', err);
    } finally {
      // 清除本地存储
      localStorage.removeItem('user');
      localStorage.removeItem('token');
      localStorage.removeItem('currentCustomer');
      
      setUser(null);
      setCurrentCustomer(null);
      setError(null);
    }
  };

  const switchCustomer = async (customerId: string) => {
    try {
      setLoading(true);
      const response = await customerApi.getCustomer(customerId);
      if (response.data.code === 200) {
        const customer = adaptCustomerDtoToCustomer(response.data.data);
        setCurrentCustomer(customer);
        localStorage.setItem('currentCustomer', JSON.stringify(customer));
      } else {
        throw new Error(response.data.message || '切换客户失败');
      }
    } catch (err) {
      console.error('Switch customer error:', err);
      setError(err instanceof Error ? err.message : '切换客户失败');
      throw err;
    } finally {
      setLoading(false);
    }
  };

  const addCustomer = async (customerData: Omit<Customer, 'id' | 'createdAt' | 'deviceCount' | 'status' | 'projects'>) => {
    try {
      setLoading(true);
      const createRequest = adaptCreateCustomerRequest(customerData);
      const response = await customerApi.createCustomer(createRequest);
      
      if (response.data.code === 200) {
        const newCustomer = adaptCustomerDtoToCustomer(response.data.data);
        setCustomers(prev => [...prev, newCustomer]);
        return newCustomer;
      } else {
        throw new Error(response.data.message || '创建客户失败');
      }
    } catch (err) {
      console.error('Add customer error:', err);
      setError(err instanceof Error ? err.message : '创建客户失败');
      throw err;
    } finally {
      setLoading(false);
    }
  };

  const updateUser = async (userId: string, updates: Partial<User>) => {
    try {
      if (user && user.id === userId) {
        // 这里需要根据后端API实现updateUser接口
        // 暂时只更新本地状态
        setUser({ ...user, ...updates });
        
        // 更新localStorage
        const updatedUser = { ...user, ...updates };
        localStorage.setItem('user', JSON.stringify(updatedUser));
      }
    } catch (err) {
      console.error('Update user error:', err);
      setError(err instanceof Error ? err.message : '更新用户信息失败');
      throw err;
    }
  };

  const updateCustomer = async (customerId: string, updates: Partial<Customer>) => {
    try {
      setLoading(true);
      const updateRequest = adaptUpdateCustomerRequest(updates);
      const response = await customerApi.updateCustomer(customerId, updateRequest);
      
      if (response.data.code === 200) {
        const updatedCustomer = adaptCustomerDtoToCustomer(response.data.data);
        
        // 更新客户列表
        setCustomers(prev =>
          prev.map(c => c.id === customerId ? updatedCustomer : c)
        );
        
        // 更新当前客户
        if (currentCustomer?.id === customerId) {
          setCurrentCustomer(updatedCustomer);
          localStorage.setItem('currentCustomer', JSON.stringify(updatedCustomer));
        }
        
        return updatedCustomer;
      } else {
        throw new Error(response.data.message || '更新客户失败');
      }
    } catch (err) {
      console.error('Update customer error:', err);
      setError(err instanceof Error ? err.message : '更新客户失败');
      throw err;
    } finally {
      setLoading(false);
    }
  };

  // 专门用于更新某个客户的项目列表（CustomersPage 调用）
  const updateCustomerProjects = (customerId: string, projects: Project[]) => {
    // 注意：这个方法使用模拟数据结构，后端可能没有对应的API
    // 暂时保持本地状态更新，后续需要根据实际情况调整
    setCustomers(prev =>
      prev.map(c => c.id === customerId ? { ...c, projects } : c)
    );
    if (currentCustomer?.id === customerId) {
      setCurrentCustomer(prev => prev ? { ...prev, projects } : prev);
    }
  };

  // Role Management
  const addRole = (role: RoleDefinition) => {
    setRoles(prev => ({ ...prev, [role.code]: role }));
  };

  const updateRole = (role: RoleDefinition) => {
    setRoles(prev => ({ ...prev, [role.code]: role }));
  };

  const deleteRole = (roleCode: string) => {
    setRoles(prev => {
      const next = { ...prev };
      delete next[roleCode];
      return next;
    });
  };

  const getUserRole = useCallback(() => {
    if (!user) return null;
    return roles[user.role] || null;
  }, [user, roles]);

  const hasPermission = useCallback((permission: Permission) => {
    const roleDef = getUserRole();
    if (!roleDef) return false;
    if (roleDef.code === 'super_admin') return true;
    return roleDef.permissions.includes(permission);
  }, [getUserRole]);

  return (
    <AuthContext.Provider
      value={{
        user,
        customers,
        currentCustomer,
        roles,
        loading,
        error,
        clearError: () => setError(null),
        refreshCustomers: async () => {
          try {
            setLoading(true);
            const response = await customerApi.getCustomers(1, 100);
            if (response.data.code === 200) {
              const adaptedCustomers = response.data.data.items.map(adaptCustomerDtoToCustomer);
              setCustomers(adaptedCustomers);
            }
          } catch (err) {
            console.error('Failed to refresh customers:', err);
            setError('刷新客户列表失败');
          } finally {
            setLoading(false);
          }
        },
        login,
        logout,
        switchCustomer,
        addCustomer,
        updateUser,
        updateCustomer,
        updateCustomerProjects,
        addRole,
        updateRole,
        deleteRole,
        hasPermission,
        getUserRole,
      }}
    >
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  const context = useContext(AuthContext);
  if (context === undefined) {
    if (import.meta.env.DEV) {
      return {
        user: null,
        customers: [] as Customer[],
        currentCustomer: null,
        roles: DEFAULT_ROLES,
        loading: false,
        error: null,
        clearError: () => {},
        refreshCustomers: async () => {},
        login: async () => {},
        logout: () => {},
        switchCustomer: async () => {},
        addCustomer: async () => {},
        updateUser: async () => {},
        updateCustomer: async () => {},
        updateCustomerProjects: () => {},
        addRole: () => {},
        updateRole: () => {},
        deleteRole: () => {},
        hasPermission: () => false,
        getUserRole: () => null,
      };
    }
    throw new Error('useAuth must be used within an AuthProvider');
  }
  return context;
}
