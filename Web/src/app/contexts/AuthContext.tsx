import { createContext, useContext, useState, ReactNode, useCallback } from 'react';
import { DEFAULT_ROLES, RoleDefinition, Permission } from '@/app/config/permissions';

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
  projects: Project[]; // 真实项目列表，projectCount 由此派生
}

interface AuthContextType {
  user: User | null;
  customers: Customer[];
  currentCustomer: Customer | null;
  roles: Record<string, RoleDefinition>;

  // Actions
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

// ─── Mock data ────────────────────────────────────────────────────────────────
const mockCustomers: Customer[] = [
  {
    id: '1',
    name: '海底捞火锅连锁',
    code: 'HDL001',
    appCode: 'HDLAPP001',
    contact: '张经理',
    phone: '138-0000-1111',
    address: '北京市朝阳区建国路88号',
    status: 'active',
    createdAt: '2025-01-01',
    deviceCount: 24,
    projects: [
      {
        id: 'p1',
        name: '海底捞北京朝阳店智能化项目',
        address: '北京市朝阳区建国路88号',
        deviceCount: 24,
        onlineDate: '2025-01-15',
        status: 'online',
        contracts: [
          {
            id: 'c1',
            name: '设备采购合同-2025',
            type: 'purchase',
            uploadDate: '2025-01-01',
            fileSize: '2.5 MB',
            fileUrl: 'https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf',
          },
        ],
        workSummaries: [],
      },
      {
        id: 'p2',
        name: '海底捞北京望京店智能化项目',
        address: '北京市朝阳区望京SOHO',
        deviceCount: 18,
        onlineDate: '2025-02-01',
        status: 'building',
        contracts: [
          {
            id: 'c2',
            name: '服务合同-2025',
            type: 'service',
            uploadDate: '2025-01-01',
            fileSize: '1.8 MB',
            fileUrl: 'https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf',
          },
        ],
        workSummaries: [],
      },
    ],
  },
  {
    id: '2',
    name: '喜茶门店管理',
    code: 'XC002',
    appCode: 'XCAPP002',
    contact: '李总监',
    phone: '138-0000-2222',
    address: '上海市浦东新区世纪大道100号',
    status: 'active',
    createdAt: '2025-02-15',
    deviceCount: 18,
    projects: [
      {
        id: 'p3',
        name: '喜茶上海旗舰店智能化项目',
        address: '上海市浦东新区世纪大道100号',
        deviceCount: 18,
        onlineDate: '2025-02-15',
        status: 'online',
        contracts: [
          {
            id: 'c3',
            name: '设备采购合同-2025',
            type: 'purchase',
            uploadDate: '2025-01-15',
            fileSize: '2.2 MB',
            fileUrl: 'https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf',
          },
        ],
        workSummaries: [],
      },
    ],
  },
  {
    id: '3',
    name: '星巴克华东区',
    code: 'SBK003',
    appCode: 'SBKAPP003',
    contact: '王主管',
    phone: '138-0000-3333',
    address: '杭州市西湖区文一西路100号',
    status: 'active',
    createdAt: '2025-03-10',
    deviceCount: 32,
    projects: [
      {
        id: 'p4',
        name: '星巴克杭州西湖店智能化项目',
        address: '杭州市西湖区文一西路100号',
        deviceCount: 32,
        onlineDate: '2025-03-10',
        status: 'building',
        contracts: [
          {
            id: 'c4',
            name: '设备采购合同-2025',
            type: 'purchase',
            uploadDate: '2025-02-01',
            fileSize: '3.1 MB',
            fileUrl: 'https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf',
          },
          {
            id: 'c5',
            name: '服务合同-2025',
            type: 'service',
            uploadDate: '2025-02-01',
            fileSize: '1.5 MB',
            fileUrl: 'https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf',
          },
        ],
        workSummaries: [],
      },
    ],
  },
];

const mockUsers: User[] = [
  {
    id: '1',
    name: '超级管理员',
    email: 'admin@system.com',
    role: 'super_admin',
  },
  {
    id: '2',
    name: '海底捞管理员',
    email: 'admin@haidilao.com',
    role: 'admin',
    customerId: '1',
    appCode: 'HDLAPP001',
  },
  {
    id: '3',
    name: '运维人员',
    email: 'operator@haidilao.com',
    role: 'operator',
    customerId: '1',
    appCode: 'HDLAPP001',
    allowedAreaIds: ['L2-001'],
  },
  {
    id: '4',
    name: '厨师长',
    email: 'chef@haidilao.com',
    role: 'chef',
    customerId: '1',
    appCode: 'HDLAPP001',
    allowedAreaIds: ['L3-003', 'L3-004'],
  },
];

// ─── Provider ─────────────────────────────────────────────────────────────────
export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null);
  const [customers, setCustomers] = useState<Customer[]>(mockCustomers);
  const [currentCustomer, setCurrentCustomer] = useState<Customer | null>(null);
  const [roles, setRoles] = useState<Record<string, RoleDefinition>>(DEFAULT_ROLES);

  const login = async (email: string, password: string) => {
    await new Promise((resolve) => setTimeout(resolve, 800));
    const mockUser = mockUsers.find((u) => u.email === email);
    if (!mockUser) throw new Error('邮箱或密码错误');
    setUser(mockUser);
    if (mockUser.customerId) {
      const customer = customers.find((c) => c.id === mockUser.customerId);
      setCurrentCustomer(customer || null);
    } else if (mockUser.role === 'super_admin') {
      setCurrentCustomer(null);
    }
  };

  const logout = () => {
    setUser(null);
    setCurrentCustomer(null);
  };

  const switchCustomer = (customerId: string) => {
    const customer = customers.find((c) => c.id === customerId);
    setCurrentCustomer(customer || null);
  };

  const addCustomer = (customerData: Omit<Customer, 'id' | 'createdAt' | 'deviceCount' | 'status' | 'projects'>) => {
    const newCustomer: Customer = {
      id: Date.now().toString(),
      ...customerData,
      status: 'active',
      createdAt: new Date().toISOString().split('T')[0],
      deviceCount: 0,
      projects: [],
    };
    setCustomers(prev => [...prev, newCustomer]);
  };

  const updateUser = (userId: string, updates: Partial<User>) => {
    if (user && user.id === userId) {
      setUser({ ...user, ...updates });
    }
  };

  const updateCustomer = (customerId: string, updates: Partial<Customer>) => {
    setCustomers(prev =>
      prev.map(c => c.id === customerId ? { ...c, ...updates } : c)
    );
    if (currentCustomer?.id === customerId) {
      setCurrentCustomer(prev => prev ? { ...prev, ...updates } : prev);
    }
  };

  // 专门用于更新某个客户的项目列表（CustomersPage 调用）
  const updateCustomerProjects = (customerId: string, projects: Project[]) => {
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
        login: async () => {},
        logout: () => {},
        switchCustomer: () => {},
        addCustomer: () => {},
        updateUser: () => {},
        updateCustomer: () => {},
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
