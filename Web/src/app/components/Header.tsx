import { Bell, User, LogOut, ChevronDown, Palette } from 'lucide-react';
import { useState } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import { useAuth, Customer } from '@/app/contexts/AuthContext';
import { useTheme } from '@/app/contexts/ThemeContext';
import type { PageType } from '@/app/components/Sidebar';

interface HeaderProps {
  currentPage: PageType;
}

export function Header({ currentPage }: HeaderProps) {
  const { user, logout, customers, currentCustomer, switchCustomer } = useAuth();
  const { theme, themes, setTheme } = useTheme();
  const [showUserMenu, setShowUserMenu] = useState(false);
  const [showCustomerMenu, setShowCustomerMenu] = useState(false);
  const [showThemeMenu, setShowThemeMenu] = useState(false);

  const roleLabels = {
    super_admin: '超级管理员',
    admin: '管理员',
    operator: '运维人员',
    chef: '厨师长',
    staff: '员工',
  };

  // 页面标题映射
  const pageTitles: Record<PageType, string> = {
    'dashboard': '工作台',
    'customers': '客户管理',
    'devices': '设备管理',
    'area-management': '区域管理',
    'analytics': '水、电、气能耗分析',
    'air-quality': '空气质量',
    'environment-monitoring': '环境监测',
    'monitoring': '实时监控',
    'archives': '档案管理',
    'work-orders': '工单管理',
    'alert-center': '告警中心',
    'logs': '日志管理',
    'logs-login': '登录日志',
    'logs-operation': '操作日志',
    'users': '用户管理',
    'users-list': '用户列表',
    'users-roles': '角色权限',
    'settings': '系统设置',
    'settings-general': '常规设置',
    'settings-notifications': '通知设置',
    'settings-security': '安全设置',
    'settings-devices': '设备设置',
    'settings-api': 'API设置',
    'settings-dictionary': '字典管理',
  };

  const pageTitle = pageTitles[currentPage] || '';

  return (
    <div className="sticky top-0 z-40 h-16 bg-gray-800/50 backdrop-blur-xl border-b border-white/10 flex items-center justify-between px-6">
      {/* Left - Customer Selector (for super admin) */}
      <div className="flex items-center gap-4">
        {user?.role === 'super_admin' && currentCustomer && (
          <div className="relative">
            <button
              onClick={() => setShowCustomerMenu(!showCustomerMenu)}
              className="flex items-center gap-2 px-4 py-2 bg-white/5 hover:bg-white/10 border border-white/10 rounded-lg transition-colors"
            >
              <span className="text-sm text-white font-medium">{currentCustomer.name}</span>
              <ChevronDown className="w-4 h-4 text-gray-400" />
            </button>

            <AnimatePresence>
              {showCustomerMenu && (
                <motion.div
                  initial={{ opacity: 0, y: -10 }}
                  animate={{ opacity: 1, y: 0 }}
                  exit={{ opacity: 0, y: -10 }}
                  className="absolute top-full left-0 mt-2 w-64 bg-gray-800 border border-white/10 rounded-lg shadow-xl z-50 overflow-hidden"
                >
                  <div className="p-2 max-h-64 overflow-y-auto">
                    {customers.map((customer) => (
                      <button
                        key={customer.id}
                        onClick={() => {
                          switchCustomer(customer.id);
                          setShowCustomerMenu(false);
                        }}
                        className={`w-full text-left px-3 py-2 rounded hover:bg-white/10 transition-colors ${
                          currentCustomer.id === customer.id ? 'bg-blue-500/20 text-blue-400' : 'text-gray-300'
                        }`}
                      >
                        <div className="font-medium">{customer.name}</div>
                        <div className="text-xs text-gray-400">{customer.code}</div>
                      </button>
                    ))}
                  </div>
                </motion.div>
              )}
            </AnimatePresence>
          </div>
        )}
      </div>

      {/* Center - Page Title */}
      <div className="absolute left-1/2 top-1/2 -translate-x-1/2 -translate-y-1/2 flex items-center gap-3">
        <div className="flex gap-1">
          {[...Array(3)].map((_, i) => (
            <div key={`left-${i}`} className="w-6 h-0.5 bg-cyan-500" />
          ))}
        </div>
        <h1 className="text-lg font-medium text-cyan-300 tracking-wider whitespace-nowrap">{pageTitle}</h1>
        <div className="flex gap-1">
          {[...Array(3)].map((_, i) => (
            <div key={`right-${i}`} className="w-6 h-0.5 bg-cyan-500" />
          ))}
        </div>
      </div>

      {/* Right - User Info & Actions */}
      <div className="flex items-center gap-4">
        {/* Notifications */}
        <button className="relative p-2 hover:bg-white/10 rounded-lg transition-colors">
          <Bell className="w-5 h-5 text-gray-400" />
          <span className="absolute top-1 right-1 w-2 h-2 bg-red-500 rounded-full"></span>
        </button>

        {/* User Menu */}
        <div className="relative">
          <button
            onClick={() => setShowUserMenu(!showUserMenu)}
            className="flex items-center gap-3 px-3 py-2 hover:bg-white/10 rounded-lg transition-colors"
          >
            <div className="w-8 h-8 bg-gradient-to-br from-blue-500 to-purple-500 rounded-full flex items-center justify-center">
              <User className="w-5 h-5 text-white" />
            </div>
            <div className="text-left">
              <div className="text-sm font-medium text-white">{user?.name}</div>
              <div className="text-xs text-gray-400">{user && roleLabels[user.role]}</div>
            </div>
            <ChevronDown className="w-4 h-4 text-gray-400" />
          </button>

          <AnimatePresence>
            {showUserMenu && (
              <motion.div
                initial={{ opacity: 0, y: -10 }}
                animate={{ opacity: 1, y: 0 }}
                exit={{ opacity: 0, y: -10 }}
                className="absolute top-full right-0 mt-2 w-48 bg-gray-800 border border-white/10 rounded-lg shadow-xl z-50 overflow-hidden"
              >
                <div className="p-2">
                  <button
                    onClick={() => {
                      logout();
                      setShowUserMenu(false);
                    }}
                    className="w-full flex items-center gap-2 px-3 py-2 text-red-400 hover:bg-red-500/10 rounded transition-colors"
                  >
                    <LogOut className="w-4 h-4" />
                    <span>退出登录</span>
                  </button>
                </div>
              </motion.div>
            )}
          </AnimatePresence>
        </div>

        {/* Theme Menu */}
        <div className="relative">
          <button
            onClick={() => setShowThemeMenu(!showThemeMenu)}
            className="p-2 hover:bg-white/10 rounded-lg transition-colors"
          >
            <Palette className="w-5 h-5 text-gray-400" />
          </button>

          <AnimatePresence>
            {showThemeMenu && (
              <motion.div
                initial={{ opacity: 0, y: -10 }}
                animate={{ opacity: 1, y: 0 }}
                exit={{ opacity: 0, y: -10 }}
                className="absolute top-full right-0 mt-2 w-48 bg-gray-800 border border-white/10 rounded-lg shadow-xl z-50 overflow-hidden"
              >
                <div className="p-2">
                  {Object.values(themes).map((themeOption) => (
                    <button
                      key={themeOption.id}
                      onClick={() => {
                        setTheme(themeOption.id);
                        setShowThemeMenu(false);
                      }}
                      className={`w-full flex items-center justify-between gap-2 px-3 py-2 rounded hover:bg-white/10 transition-colors ${
                        theme === themeOption.id ? 'bg-blue-500/20 text-blue-400' : 'text-gray-300'
                      }`}
                    >
                      <span>{themeOption.name}</span>
                      {theme === themeOption.id && (
                        <div className="w-2 h-2 bg-blue-400 rounded-full"></div>
                      )}
                    </button>
                  ))}
                </div>
              </motion.div>
            )}
          </AnimatePresence>
        </div>
      </div>
    </div>
  );
}