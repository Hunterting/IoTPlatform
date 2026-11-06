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
  permissions?: string[]; // 服务端下发的有效权限（单一事实来源 = DB Role.Permissions）
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

// ── 租户上下文纯函数（模块级，不进入 useCallback 依赖链）────────────────────────
// 服务端已按 JWT AppCode claim 隔离：非超管拿到的列表即其所属客户，是权威数据源。
// 优先级：appCode 匹配 → customerId 匹配（String 归一，见 F4）→ 单条收敛态。
const deriveTenantContext = (u: User | null, list: Customer[]): Customer | null => {
  if (!u || u.role === 'super_admin' || list.length === 0) return null;
  const byAppCode = u.appCode ? list.find(c => c.appCode === u.appCode) : undefined;
  if (byAppCode) return byAppCode;
  const byId = u.customerId != null
    ? list.find(c => String(c.id) === String(u.customerId))
    : undefined;
  if (byId) return byId;
  return list.length === 1 ? list[0] : null;   // 隔离后仅 1 条 ⇒ 即本租户（9-B）
};

// 兼容「修复前已登录」的存量会话：localStorage.user.appCode / customerId 为 null 时静默自愈（9-C），
// 无需强制重新登录。
const patchTenantUser = (u: User, cc: Customer | null): User => {
  if (!cc) return u;
  if (u.appCode && u.customerId != null) return u;   // 已是完整态，避免无谓 setState / localStorage 写入
  return {
    ...u,
    appCode: u.appCode ?? cc.appCode,
    // 运行时 id 为 number、TS 声明为 string（F4）；不做 String() 以免与 c.id 类型失配
    customerId: u.customerId ?? (cc.id as unknown as string),
  };
};

// 初始状态，API加载前使用空数据



// ─── Provider ─────────────────────────────────────────────────────────────────
export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null);
  const [customers, setCustomers] = useState<Customer[]>([]);
  const [currentCustomer, setCurrentCustomer] = useState<Customer | null>(null);
  const [roles, setRoles] = useState<Record<string, RoleDefinition>>(DEFAULT_ROLES);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // ── 租户上下文单一入口 ──────────────────────────────────────────────────────
  // applyCustomers 统一负责 customers / currentCustomer / user.appCode 三者的收敛，
  // 任何路径（全新登录 / F5 / 关闭重开 / 旧版本 localStorage 升级 / 手动刷新）都走这里。
  const applyCustomers = (list: Customer[], u: User | null) => {
    setCustomers(list);
    // 超管：只更新列表，绝不设置 currentCustomer（App.tsx 选择租户门控零回归）
    if (!u || u.role === 'super_admin') return;
    const cc = deriveTenantContext(u, list);
    setCurrentCustomer(cc);
    if (cc) {
      localStorage.setItem('currentCustomer', JSON.stringify(cc));
      // 旧会话 appCode / customerId 为 null 时静默自愈（9-C）
      const patched = patchTenantUser(u, cc);
      if (patched !== u) {
        setUser(patched);
        localStorage.setItem('user', JSON.stringify(patched));
      }
    } else {
      localStorage.removeItem('currentCustomer');
    }
  };

  // 单一 loadCustomers：保留原有 token 门控，但改为可按登录态 u 重入。
  const loadCustomers = async (u: User | null): Promise<Customer[]> => {
    if (!localStorage.getItem('token')) return [];
    try {
      setLoading(true);
      // 注意：httpClient 响应拦截器返回完整 AxiosResponse，业务用 response.data.code / response.data.data.items
      const response = await customerApi.getCustomers(1, 100);
      if (response.data.code !== 200) {
        setError('加载客户列表失败');
        return [];
      }
      const list = response.data.data.items.map(adaptCustomerDtoToCustomer);
      applyCustomers(list, u);
      return list;
    } catch (err) {
      console.error('Failed to load customers:', err);
      setError('加载客户列表失败');
      return [];
    } finally {
      setLoading(false);
    }
  };

  // 挂载：合并原「加载客户列表」与「恢复本地登录态」两个 useEffect，路径同源
  useEffect(() => {
    const storedUser = localStorage.getItem('user');
    const storedToken = localStorage.getItem('token');
    if (!storedUser || !storedToken) return;
    try {
      const parsedUser: User = JSON.parse(storedUser);
      setUser(parsedUser);
      // 首屏快速填充（仅租户）；超管刷新后不恢复 currentCustomer，仍需重新选租户（9-D 本次不改）
      const storedCustomer = localStorage.getItem('currentCustomer');
      if (storedCustomer && parsedUser.role !== 'super_admin') {
        setCurrentCustomer(JSON.parse(storedCustomer));
      }
      // 服务端列表为准：覆盖旧值 + 自愈 appCode / currentCustomer
      void loadCustomers(parsedUser);
    } catch (err) {
      console.error('Failed to parse stored user:', err);
      localStorage.removeItem('user');
      localStorage.removeItem('token');
      localStorage.removeItem('currentCustomer');
    }
  }, []);

  const login = async (email: string, password: string) => {
    try {
      setLoading(true);
      setError(null);

      // authApi.login 内部已 setToken（F6），因此紧接着的请求一定带得上 token
      const response = await authApi.login({ email, password });
      console.log('Login success:', response);
      if (!response) throw new Error('登录失败');

      const adaptedUser = adaptLoginResponseToUser(response);

      // 保存用户信息和token到localStorage
      localStorage.setItem('user', JSON.stringify(adaptedUser));
      localStorage.setItem('token', response.token);

      setUser(adaptedUser);

      // 超级管理员：currentCustomer 恒 null，语义逐字保留
      if (adaptedUser.role === 'super_admin') {
        setCurrentCustomer(null);
        localStorage.removeItem('currentCustomer');
      }

      // 超管与租户都无条件加载客户列表（原 else if (adaptedUser.customerId) 分支不可达且用错身份主键，已删除）
      const list = await loadCustomers(adaptedUser);

      // 兜底：列表为空（网络/权限异常）时，用后端 LoginResponse.CurrentCustomer（F3 → 走同一适配器）
      if (list.length === 0 && adaptedUser.role !== 'super_admin' && response.currentCustomer) {
        const cc = adaptCustomerDtoToCustomer(response.currentCustomer);
        setCurrentCustomer(cc);
        localStorage.setItem('currentCustomer', JSON.stringify(cc));
        const patched = patchTenantUser(adaptedUser, cc);
        if (patched !== adaptedUser) {
          setUser(patched);
          localStorage.setItem('user', JSON.stringify(patched));
        }
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
    const currentUser = user;
    if (!currentUser) return false;
    // 超级管理员直接放行（角色名短路，与后端 PermissionFilter 一致）
    if (currentUser.role === 'super_admin') return true;
    // 基于服务端下发的权限数组判定（单一事实来源 = 后端 permissions）
    return currentUser.permissions?.includes(permission) ?? false;
  }, [user]);

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
          await loadCustomers(user);
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
