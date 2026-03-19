import { useState } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import {
  LayoutDashboard,
  Users,
  Cpu,
  BarChart3,
  Eye,
  FolderOpen,
  Settings,
  ChevronRight,
  ChevronDown,
  Building2,
  List,
  Tag,
  Globe,
  Bell,
  Shield,
  Zap,
  Key,
  UserCog,
  UsersRound,
  Map,
  ClipboardList,
  ScrollText,
  LogIn,
  Activity,
  AlertTriangle,
  ClipboardCheck,
  Wind,
  BookOpen,
} from 'lucide-react';
import { useAuth } from '@/app/contexts/AuthContext';
import { PERMISSIONS, Permission } from '@/app/config/permissions';

export type PageType =
  | 'dashboard'
  | 'customers'
  | 'devices'
  | 'area-management'
  | 'alert-center'
  | 'analytics'
  | 'air-quality'
  | 'environment-monitoring'
  | 'monitoring'
  | 'archives'
  | 'work-orders'
  | 'logs'
  | 'logs-login'
  | 'logs-operation'
  | 'users'
  | 'users-list'
  | 'users-roles'
  | 'settings'
  | 'settings-general'
  | 'settings-notifications'
  | 'settings-security'
  | 'settings-devices'
  | 'settings-api'
  | 'settings-dictionary';

interface SidebarProps {
  currentPage: PageType;
  onNavigate: (page: PageType) => void;
  userRole: string;
}

interface SubMenuItem {
  id: PageType;
  label: string;
  icon: React.ReactNode;
  /** 需要持有此权限才可见；undefined = 所有人可见 */
  requiredPermission?: Permission;
}

interface MenuItem {
  id: PageType;
  label: string;
  icon: React.ReactNode;
  /** 需要持有此权限才可见；undefined = 所有人可见 */
  requiredPermission?: Permission;
  subItems?: SubMenuItem[];
}

// ── 菜单定义 ──────────────────────────────────────────────────
// 每个菜单项绑定一个 Permission。
// super_admin 因为 hasPermission 始终返回 true，自动看到所有菜单（含未来新增项）。
// 其他角色需在角色权限配置中显式授予对应 Permission 才能看到对应菜单。
const menuItems: MenuItem[] = [
  {
    id: 'dashboard',
    label: '工作台',
    icon: <LayoutDashboard className="w-5 h-5" />,
    requiredPermission: PERMISSIONS.VIEW_DASHBOARD,
  },
  {
    id: 'customers',
    label: '客户管理',
    icon: <Building2 className="w-5 h-5" />,
    requiredPermission: PERMISSIONS.VIEW_CUSTOMERS,
  },
  {
    id: 'devices',
    label: '设备管理',
    icon: <Cpu className="w-5 h-5" />,
    requiredPermission: PERMISSIONS.VIEW_DEVICES,
    subItems: [
      {
        id: 'devices',
        label: '设备列表',
        icon: <List className="w-4 h-4" />,
        requiredPermission: PERMISSIONS.VIEW_DEVICES,
      },
      {
        id: 'area-management',
        label: '区域管理',
        icon: <Map className="w-4 h-4" />,
        requiredPermission: PERMISSIONS.VIEW_AREAS,
      },
      {
        id: 'alert-center',
        label: '告警中心',
        icon: <AlertTriangle className="w-4 h-4" />,
        requiredPermission: PERMISSIONS.VIEW_ALERT_CENTER,
      },
    ],
  },
  {
    id: 'analytics',
    label: '智能分析',
    icon: <BarChart3 className="w-5 h-5" />,
    requiredPermission: PERMISSIONS.VIEW_ANALYTICS,
  },
  {
    id: 'air-quality',
    label: '空气质量',
    icon: <Wind className="w-5 h-5" />,
    requiredPermission: PERMISSIONS.VIEW_AIR_QUALITY,
  },
  {
    id: 'environment-monitoring',
    label: '环境监测',
    icon: <Wind className="w-5 h-5" />,
    requiredPermission: PERMISSIONS.VIEW_ENVIRONMENT_MONITORING,
  },
  {
    id: 'monitoring',
    label: '视频监控',
    icon: <Eye className="w-5 h-5" />,
    requiredPermission: PERMISSIONS.VIEW_MONITORING,
  },
  {
    id: 'archives',
    label: '档案管理',
    icon: <FolderOpen className="w-5 h-5" />,
    requiredPermission: PERMISSIONS.VIEW_ARCHIVES,
  },
  {
    id: 'work-orders',
    label: '工单管理',
    icon: <ClipboardList className="w-5 h-5" />,
    requiredPermission: PERMISSIONS.VIEW_WORK_ORDERS,
  },
  {
    id: 'logs',
    label: '日志管理',
    icon: <ScrollText className="w-5 h-5" />,
    requiredPermission: PERMISSIONS.VIEW_LOGS,
    subItems: [
      {
        id: 'logs-login',
        label: '登录日志',
        icon: <LogIn className="w-4 h-4" />,
        requiredPermission: PERMISSIONS.VIEW_LOGS,
      },
      {
        id: 'logs-operation',
        label: '操作日志',
        icon: <Activity className="w-4 h-4" />,
        requiredPermission: PERMISSIONS.VIEW_LOGS,
      },
    ],
  },
  {
    id: 'users',
    label: '用户管理',
    icon: <Users className="w-5 h-5" />,
    requiredPermission: PERMISSIONS.VIEW_USERS,
    subItems: [
      {
        id: 'users-list',
        label: '用户列表',
        icon: <UsersRound className="w-4 h-4" />,
        requiredPermission: PERMISSIONS.VIEW_USERS,
      },
      {
        id: 'users-roles',
        label: '角色管理',
        icon: <UserCog className="w-4 h-4" />,
        requiredPermission: PERMISSIONS.VIEW_ROLES,
      },
    ],
  },
  {
    id: 'settings',
    label: '系统设置',
    icon: <Settings className="w-5 h-5" />,
    requiredPermission: PERMISSIONS.VIEW_SETTINGS,
    subItems: [
      {
        id: 'settings-general',
        label: '通用设置',
        icon: <Globe className="w-4 h-4" />,
        requiredPermission: PERMISSIONS.VIEW_SETTINGS,
      },
      {
        id: 'settings-notifications',
        label: '通知设置',
        icon: <Bell className="w-4 h-4" />,
        requiredPermission: PERMISSIONS.VIEW_SETTINGS,
      },
      {
        id: 'settings-security',
        label: '安全设置',
        icon: <Shield className="w-4 h-4" />,
        requiredPermission: PERMISSIONS.VIEW_SETTINGS,
      },
      {
        id: 'settings-devices',
        label: '设备授权',
        icon: <Zap className="w-4 h-4" />,
        requiredPermission: PERMISSIONS.VIEW_SETTINGS,
      },
      {
        id: 'settings-api',
        label: 'API 配置',
        icon: <Key className="w-4 h-4" />,
        // 仅超级管理员可见，绑定专属权限 VIEW_API_CONFIG
        requiredPermission: PERMISSIONS.VIEW_API_CONFIG,
      },
      {
        id: 'settings-dictionary',
        label: '字典管理',
        icon: <BookOpen className="w-4 h-4" />,
        requiredPermission: PERMISSIONS.VIEW_DICTIONARY,
      },
    ],
  },
];

export function Sidebar({ currentPage, onNavigate }: SidebarProps) {
  const { hasPermission } = useAuth();

  const [expandedMenu, setExpandedMenu] = useState<PageType | null>(() => {
    if (currentPage.startsWith('settings')) return 'settings';
    if (currentPage.startsWith('users')) return 'users';
    if (currentPage.startsWith('logs')) return 'logs';
    if (
      currentPage === 'devices' ||
      currentPage === 'area-management' ||
      currentPage === 'alert-center'
    )
      return 'devices';
    return null;
  });

  // ── 菜单可见性判断（完全基于 RBAC hasPermission） ──────────
  const canSeeItem = (perm?: Permission): boolean => {
    if (!perm) return true; // 无权限要求 → 所有人可见
    return hasPermission(perm);
  };

  // 过滤顶级菜单
  const visibleMenuItems = menuItems
    .map((item) => {
      // 过滤子菜单
      const visibleSubs = item.subItems?.filter((sub) => canSeeItem(sub.requiredPermission));
      return { ...item, subItems: visibleSubs };
    })
    .filter((item) => {
      if (!canSeeItem(item.requiredPermission)) return false;
      // 如果有子菜单定义，但过滤后全为空，则隐藏父菜单
      if (item.subItems !== undefined && item.subItems.length === 0) return false;
      return true;
    });

  const handleMenuClick = (item: MenuItem) => {
    if (item.subItems && item.subItems.length > 0) {
      setExpandedMenu(expandedMenu === item.id ? null : item.id);
    } else {
      onNavigate(item.id);
    }
  };

  const isMenuActive = (item: MenuItem) => {
    if (item.subItems) return item.subItems.some((sub) => sub.id === currentPage);
    return currentPage === item.id;
  };

  return (
    <div
      className="w-64 flex flex-col flex-shrink-0"
      style={{
        background: 'linear-gradient(180deg, #020e25 0%, #010b1d 100%)',
        borderRight: '1px solid rgba(0,195,255,0.2)',
      }}
    >
      {/* Logo */}
      <div className="p-4 border-b" style={{ borderColor: 'rgba(0,195,255,0.15)' }}>
        <div className="flex items-center gap-2">
          <div
            className="w-1.5 h-8 rounded-full"
            style={{ background: 'linear-gradient(180deg, #00c3ff, #0057b8)' }}
          />
          <div>
            <h2
              className="text-base font-bold tracking-wider"
              style={{
                background: 'linear-gradient(90deg, #00c3ff, #ffffff)',
                WebkitBackgroundClip: 'text',
                WebkitTextFillColor: 'transparent',
              }}
            >
              智厨云平台
            </h2>
            <p className="text-xs mt-0.5" style={{ color: '#3a6e9a' }}>
              Smart Kitchen Cloud
            </p>
          </div>
        </div>
      </div>

      {/* Menu Items */}
      <nav className="flex-1 p-3 space-y-0.5 overflow-y-auto">
        {visibleMenuItems.map((item) => {
          const isActive = isMenuActive(item);
          const isExpanded = expandedMenu === item.id;

          return (
            <div key={item.id}>
              <motion.button
                whileHover={{ x: 2 }}
                onClick={() => handleMenuClick(item)}
                className="w-full flex items-center justify-between px-3 py-2.5 rounded transition-all"
                style={
                  isActive
                    ? {
                        background:
                          'linear-gradient(90deg, rgba(0,195,255,0.15), rgba(0,195,255,0.05))',
                        borderLeft: '2px solid #00c3ff',
                        color: '#00c3ff',
                      }
                    : {
                        color: '#6a8ca8',
                        borderLeft: '2px solid transparent',
                      }
                }
              >
                <div className="flex items-center gap-3">
                  {item.icon}
                  <span className="text-sm font-medium">{item.label}</span>
                </div>
                {item.subItems && item.subItems.length > 0 ? (
                  isExpanded ? (
                    <ChevronDown className="w-4 h-4" />
                  ) : (
                    <ChevronRight className="w-4 h-4" />
                  )
                ) : (
                  isActive && <ChevronRight className="w-4 h-4" />
                )}
              </motion.button>

              {/* Sub Menu */}
              <AnimatePresence>
                {item.subItems && item.subItems.length > 0 && isExpanded && (
                  <motion.div
                    initial={{ opacity: 0, height: 0 }}
                    animate={{ opacity: 1, height: 'auto' }}
                    exit={{ opacity: 0, height: 0 }}
                    transition={{ duration: 0.2 }}
                    className="ml-4 mt-0.5 space-y-0.5 overflow-hidden"
                  >
                    {item.subItems.map((subItem) => {
                      const isSubActive = currentPage === subItem.id;
                      return (
                        <motion.button
                          key={subItem.id}
                          whileHover={{ x: 2 }}
                          onClick={() => onNavigate(subItem.id)}
                          className="w-full flex items-center gap-3 px-3 py-2 rounded transition-all text-sm"
                          style={
                            isSubActive
                              ? {
                                  background: 'rgba(0,195,255,0.1)',
                                  color: '#00c3ff',
                                  borderLeft: '2px solid rgba(0,195,255,0.5)',
                                }
                              : {
                                  color: '#4a6a80',
                                  borderLeft: '2px solid transparent',
                                }
                          }
                        >
                          {subItem.icon}
                          <span>{subItem.label}</span>
                        </motion.button>
                      );
                    })}
                  </motion.div>
                )}
              </AnimatePresence>
            </div>
          );
        })}
      </nav>

      {/* Version Info */}
      <div className="p-3 border-t text-center" style={{ borderColor: 'rgba(0,195,255,0.1)' }}>
        <p className="text-xs" style={{ color: '#2a4a60' }}>
          Version 2.0.1
        </p>
      </div>
    </div>
  );
}