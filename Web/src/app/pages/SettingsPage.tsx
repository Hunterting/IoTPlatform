import { useState } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import {
  Settings,
  Bell,
  Shield,
  Users,
  Zap,
  Globe,
  Palette,
  Database,
  Key,
  Save,
  X,
  Plus,
  Lock,
  BookOpen,
} from 'lucide-react';
import { PageType } from '@/app/components/Sidebar';
import { DictionaryManagementPage } from '@/app/pages/DictionaryManagementPage';

interface SettingsPageProps {
  activePage: PageType;
}

export function SettingsPage({ activePage }: SettingsPageProps) {
  const getPageTitle = () => {
    switch (activePage) {
      case 'settings-general':
        return { title: '通用设置', desc: '系统基础配置' };
      case 'settings-notifications':
        return { title: '通知设置', desc: '消息通知配置' };
      case 'settings-security':
        return { title: '安全设置', desc: '安全策略配置' };
      case 'settings-users':
        return { title: '用户管理', desc: '用户与权限管理' };
      case 'settings-devices':
        return { title: '设备授权', desc: '设备授权管理' };
      case 'settings-api':
        return { title: 'API配置', desc: 'API接口配置' };
      case 'settings-dictionary':
        return { title: '字典管理', desc: '统一管理系统字典数据' };
      default:
        return { title: '系统设置', desc: '系统配置与权限管理' };
    }
  };

  const pageInfo = getPageTitle();

  // 字典管理页面有自己的完整布局，不需要额外包装
  if (activePage === 'settings-dictionary') {
    return <DictionaryManagementPage />;
  }

  return (
    <div className="p-6 space-y-6">
      {/* Header */}
      <div>
        <h1 className="text-3xl font-bold text-white mb-2">{pageInfo.title}</h1>
        <p className="text-gray-400">{pageInfo.desc}</p>
      </div>

      {/* Content Area */}
      <motion.div
        key={activePage}
        initial={{ opacity: 0, y: 20 }}
        animate={{ opacity: 1, y: 0 }}
        className="bg-white/5 backdrop-blur-xl border border-white/10 rounded-xl p-6"
      >
        {(activePage === 'settings-general' || activePage === 'settings') && <GeneralSettings />}
        {activePage === 'settings-notifications' && <NotificationSettings />}
        {activePage === 'settings-security' && <SecuritySettings />}
        {activePage === 'settings-users' && <UserManagement />}
        {activePage === 'settings-devices' && <DeviceAuthorization />}
        {activePage === 'settings-api' && <APIConfiguration />}
      </motion.div>
    </div>
  );
}

function GeneralSettings() {
  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-xl font-semibold text-white mb-4 flex items-center gap-2">
          <Globe className="w-5 h-5 text-blue-400" />
          通用设置
        </h2>
      </div>

      <div className="space-y-4">
        <div>
          <label className="block text-sm font-medium text-gray-300 mb-2">系统名称</label>
          <input
            type="text"
            defaultValue="数智物联网厨房 SaaS 平台"
            className="w-full px-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500 transition-colors"
          />
        </div>

        <div>
          <label className="block text-sm font-medium text-gray-300 mb-2">系统语言</label>
          <select className="w-full px-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500 transition-colors [&>option]:bg-gray-800 [&>option]:text-white">
            <option className="bg-gray-800 text-white">简体中文</option>
            <option className="bg-gray-800 text-white">English</option>
            <option className="bg-gray-800 text-white">繁體中文</option>
          </select>
        </div>

        <div>
          <label className="block text-sm font-medium text-gray-300 mb-2">时区设置</label>
          <select className="w-full px-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500 transition-colors [&>option]:bg-gray-800 [&>option]:text-white">
            <option className="bg-gray-800 text-white">UTC+8 北京时间</option>
            <option className="bg-gray-800 text-white">UTC+0 格林威治时间</option>
            <option className="bg-gray-800 text-white">UTC-5 东部时间</option>
          </select>
        </div>

        <div className="flex items-center justify-between p-4 bg-white/5 rounded-lg">
          <div>
            <p className="font-medium text-white">自动备份</p>
            <p className="text-sm text-gray-400">每日自动备份系统数据</p>
          </div>
          <label className="relative inline-flex items-center cursor-pointer">
            <input type="checkbox" className="sr-only peer" defaultChecked />
            <div className="w-11 h-6 bg-gray-700 peer-focus:outline-none rounded-full peer peer-checked:after:translate-x-full peer-checked:after:border-white after:content-[''] after:absolute after:top-[2px] after:left-[2px] after:bg-white after:rounded-full after:h-5 after:w-5 after:transition-all peer-checked:bg-blue-500"></div>
          </label>
        </div>
      </div>

      <button className="w-full flex items-center justify-center gap-2 px-6 py-3 bg-gradient-to-r from-blue-500 to-purple-500 hover:from-blue-600 hover:to-purple-600 rounded-lg transition-all">
        <Save className="w-5 h-5" />
        <span>保存设置</span>
      </button>
    </div>
  );
}

function NotificationSettings() {
  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-xl font-semibold text-white mb-4 flex items-center gap-2">
          <Bell className="w-5 h-5 text-blue-400" />
          通知设置
        </h2>
      </div>

      <div className="space-y-4">
        {[
          { title: '设备故障预警', desc: '设备出现异常时发送通知' },
          { title: '温度异常提醒', desc: '温度超过阈值时发送提醒' },
          { title: '能耗异常通知', desc: '能耗异常波动时发送通知' },
          { title: '维护到期提醒', desc: '设备维护到期前发送提醒' },
          { title: '系统更新通知', desc: '系统版本更新时发送通知' },
        ].map((item, index) => (
          <div key={index} className="flex items-center justify-between p-4 bg-white/5 rounded-lg">
            <div>
              <p className="font-medium text-white">{item.title}</p>
              <p className="text-sm text-gray-400">{item.desc}</p>
            </div>
            <label className="relative inline-flex items-center cursor-pointer">
              <input type="checkbox" className="sr-only peer" defaultChecked />
              <div className="w-11 h-6 bg-gray-700 peer-focus:outline-none rounded-full peer peer-checked:after:translate-x-full peer-checked:after:border-white after:content-[''] after:absolute after:top-[2px] after:left-[2px] after:bg-white after:rounded-full after:h-5 after:w-5 after:transition-all peer-checked:bg-blue-500"></div>
            </label>
          </div>
        ))}
      </div>

      <button className="w-full flex items-center justify-center gap-2 px-6 py-3 bg-gradient-to-r from-blue-500 to-purple-500 hover:from-blue-600 hover:to-purple-600 rounded-lg transition-all">
        <Save className="w-5 h-5" />
        <span>保存设置</span>
      </button>
    </div>
  );
}

function SecuritySettings() {
  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-xl font-semibold text-white mb-4 flex items-center gap-2">
          <Shield className="w-5 h-5 text-blue-400" />
          安全设置
        </h2>
      </div>

      <div className="space-y-4">
        <div>
          <label className="block text-sm font-medium text-gray-300 mb-2">登录超时时间（分钟）</label>
          <input
            type="number"
            defaultValue="30"
            className="w-full px-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500 transition-colors"
          />
        </div>

        <div>
          <label className="block text-sm font-medium text-gray-300 mb-2">密码最小长度</label>
          <input
            type="number"
            defaultValue="8"
            className="w-full px-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500 transition-colors"
          />
        </div>

        <div className="flex items-center justify-between p-4 bg-white/5 rounded-lg">
          <div>
            <p className="font-medium text-white">双因素认证</p>
            <p className="text-sm text-gray-400">启用二次验证提高安全性</p>
          </div>
          <label className="relative inline-flex items-center cursor-pointer">
            <input type="checkbox" className="sr-only peer" />
            <div className="w-11 h-6 bg-gray-700 peer-focus:outline-none rounded-full peer peer-checked:after:translate-x-full peer-checked:after:border-white after:content-[''] after:absolute after:top-[2px] after:left-[2px] after:bg-white after:rounded-full after:h-5 after:w-5 after:transition-all peer-checked:bg-blue-500"></div>
          </label>
        </div>

        <div className="flex items-center justify-between p-4 bg-white/5 rounded-lg">
          <div>
            <p className="font-medium text-white">IP白名单</p>
            <p className="text-sm text-gray-400">限制特定IP地址访问</p>
          </div>
          <label className="relative inline-flex items-center cursor-pointer">
            <input type="checkbox" className="sr-only peer" />
            <div className="w-11 h-6 bg-gray-700 peer-focus:outline-none rounded-full peer peer-checked:after:translate-x-full peer-checked:after:border-white after:content-[''] after:absolute after:top-[2px] after:left-[2px] after:bg-white after:rounded-full after:h-5 after:w-5 after:transition-all peer-checked:bg-blue-500"></div>
          </label>
        </div>
      </div>

      <button className="w-full flex items-center justify-center gap-2 px-6 py-3 bg-gradient-to-r from-blue-500 to-purple-500 hover:from-blue-600 hover:to-purple-600 rounded-lg transition-all">
        <Save className="w-5 h-5" />
        <span>保存设置</span>
      </button>
    </div>
  );
}

function UserManagement() {
  const [showAddUserModal, setShowAddUserModal] = useState(false);
  const [showRoleModal, setShowRoleModal] = useState(false);
  const [showPermissionModal, setShowPermissionModal] = useState(false);
  const [showCreateRoleModal, setShowCreateRoleModal] = useState(false);
  const [showEditRoleModal, setShowEditRoleModal] = useState(false);
  const [showEditUserModal, setShowEditUserModal] = useState(false);
  const [selectedUser, setSelectedUser] = useState<any>(null);
  const [selectedRole, setSelectedRole] = useState<string | null>(null);
  const [editingRole, setEditingRole] = useState<any>(null);
  const [editingUser, setEditingUser] = useState<any>(null);
  const [newRole, setNewRole] = useState({
    name: '',
    description: '',
    color: 'from-blue-500 to-cyan-500',
    permissions: [] as string[],
  });
  const [newUser, setNewUser] = useState({
    name: '',
    email: '',
    phone: '',
    department: '',
    role: 'staff',
    password: '',
  });

  const [users, setUsers] = useState([
    { id: '1', name: '超级管理员', role: 'super_admin', email: 'admin@system.com', status: 'active', phone: '138-0000-0000', department: '系统管理部' },
    { id: '2', name: '张三', role: 'admin', email: 'zhangsan@example.com', status: 'active', phone: '138-0000-1111', department: '管理部' },
    { id: '3', name: '李四', role: 'operator', email: 'lisi@example.com', status: 'active', phone: '138-0000-2222', department: '运维部' },
    { id: '4', name: '王五', role: 'chef', email: 'wangwu@example.com', status: 'inactive', phone: '138-0000-3333', department: '厨房部' },
    { id: '5', name: '赵六', role: 'staff', email: 'zhaoliu@example.com', status: 'active', phone: '138-0000-4444', department: '后勤部' },
  ]);

  const [roles, setRoles] = useState([
    { 
      id: 'super_admin', 
      name: '超级管理员', 
      description: '系统最高权限，受系统保护不可删除',
      color: 'from-purple-600 to-indigo-600',
      permissions: ['all']
    },
    { 
      id: 'admin', 
      name: '管理员', 
      description: '系统最高权限，可管理所有功能',
      color: 'from-red-500 to-orange-500',
      permissions: ['all']
    },
    { 
      id: 'operator', 
      name: '运维主管', 
      description: '负责设备运维和监控管理',
      color: 'from-blue-500 to-cyan-500',
      permissions: ['devices', 'monitoring', 'analytics', 'archives']
    },
    { 
      id: 'chef', 
      name: '厨师长', 
      description: '负责厨房设备使用和日常管理',
      color: 'from-green-500 to-emerald-500',
      permissions: ['devices_view', 'monitoring_view', 'orders']
    },
    { 
      id: 'staff', 
      name: '普通员工', 
      description: '基础查看权限',
      color: 'from-gray-500 to-slate-500',
      permissions: ['monitoring_view']
    },
  ]);

  const permissions = {
    customers: { name: '客户管理', desc: '查看和管理客户信息' },
    devices: { name: '设备管理', desc: '完整的设备增删改查权限' },
    devices_view: { name: '设备查看', desc: '仅查看设备信息' },
    monitoring: { name: '实时监控', desc: '查看和配置监控系统' },
    monitoring_view: { name: '监控查看', desc: '仅查看监控数据' },
    analytics: { name: '智能分析', desc: '查看能耗分析和统计' },
    archives: { name: '档案管理', desc: '管理文档和图纸' },
    orders: { name: '订单管理', desc: '查看和处理订单' },
    settings: { name: '系统设置', desc: '修改系统配置' },
    users: { name: '用户管理', desc: '管理用户和权限' },
  };

  const getRoleName = (roleId: string) => {
    return roles.find(r => r.id === roleId)?.name || roleId;
  };

  const getRoleColor = (roleId: string) => {
    return roles.find(r => r.id === roleId)?.color || 'from-gray-500 to-slate-500';
  };

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h2 className="text-xl font-semibold text-white flex items-center gap-2">
          <Users className="w-5 h-5 text-blue-400" />
          用户与权限管理
        </h2>
        <div className="flex gap-3">
          <button 
            onClick={() => setShowRoleModal(true)}
            className="px-4 py-2 bg-purple-500 hover:bg-purple-600 rounded-lg transition-colors"
          >
            角色管理
          </button>
          <button 
            onClick={() => setShowAddUserModal(true)}
            className="px-4 py-2 bg-blue-500 hover:bg-blue-600 rounded-lg transition-colors"
          >
            添加用户
          </button>
        </div>
      </div>

      {/* Role Stats */}
      <div className="grid grid-cols-4 gap-4">
        {roles.slice(1).map((role) => (
          <motion.div
            key={role.id}
            initial={{ opacity: 0, scale: 0.9 }}
            animate={{ opacity: 1, scale: 1 }}
            className={`p-4 bg-gradient-to-br ${role.color} bg-opacity-10 border border-white/10 rounded-xl`}
          >
            <div className="flex items-center justify-between mb-2">
              <span className="text-white font-semibold">{role.name}</span>
              <span className="text-2xl font-bold text-white">
                {users.filter(u => u.role === role.id).length}
              </span>
            </div>
            <p className="text-xs text-gray-300">{role.description}</p>
          </motion.div>
        ))}
      </div>

      {/* Users Table */}
      <div className="bg-white/5 border border-white/10 rounded-xl overflow-hidden">
        <table className="w-full">
          <thead className="border-b border-white/10">
            <tr>
              <th className="px-6 py-4 text-left text-sm font-semibold text-gray-300">用户</th>
              <th className="px-6 py-4 text-left text-sm font-semibold text-gray-300">角色</th>
              <th className="px-6 py-4 text-left text-sm font-semibold text-gray-300">部门</th>
              <th className="px-6 py-4 text-left text-sm font-semibold text-gray-300">联系方式</th>
              <th className="px-6 py-4 text-left text-sm font-semibold text-gray-300">状态</th>
              <th className="px-6 py-4 text-center text-sm font-semibold text-gray-300">操作</th>
            </tr>
          </thead>
          <tbody>
            {users.map((user) => (
              <tr key={user.id} className="border-b border-white/5 hover:bg-white/5 transition-colors">
                <td className="px-6 py-4">
                  <div className="flex items-center gap-3">
                    <div className={`w-10 h-10 bg-gradient-to-br ${getRoleColor(user.role)} rounded-full flex items-center justify-center`}>
                      <Users className="w-5 h-5 text-white" />
                    </div>
                    <div>
                      <p className="font-medium text-white">{user.name}</p>
                      <p className="text-sm text-gray-400">{user.email}</p>
                    </div>
                  </div>
                </td>
                <td className="px-6 py-4">
                  <span className={`px-3 py-1 rounded-full text-xs font-medium bg-gradient-to-r ${getRoleColor(user.role)} bg-opacity-20 text-white`}>
                    {getRoleName(user.role)}
                  </span>
                </td>
                <td className="px-6 py-4">
                  <span className="text-sm text-gray-300">{user.department}</span>
                </td>
                <td className="px-6 py-4">
                  <span className="text-sm text-gray-300">{user.phone}</span>
                </td>
                <td className="px-6 py-4">
                  <span className={`px-3 py-1 rounded-full text-xs ${
                    user.status === 'active' ? 'bg-green-500/20 text-green-400' : 'bg-gray-500/20 text-gray-400'
                  }`}>
                    {user.status === 'active' ? '活跃' : '未激活'}
                  </span>
                </td>
                <td className="px-6 py-4">
                  <div className="flex items-center justify-center gap-2">
                    <button
                      onClick={() => {
                        setSelectedUser(user);
                        setSelectedRole(user.role);
                        setShowPermissionModal(true);
                      }}
                      className="px-3 py-1 bg-blue-500/20 hover:bg-blue-500/30 text-blue-400 rounded text-xs transition-colors"
                    >
                      权限
                    </button>
                    <button
                      onClick={() => {
                        setEditingUser(user);
                        setNewUser({
                          name: user.name,
                          email: user.email,
                          phone: user.phone,
                          department: user.department,
                          role: user.role,
                          password: '',
                        });
                        setShowEditUserModal(true);
                      }}
                      className="px-3 py-1 bg-amber-500/20 hover:bg-amber-500/30 text-amber-400 rounded text-xs transition-colors"
                    >
                      编辑
                    </button>
                    {user.role === 'super_admin' ? (
                      <span
                        className="flex items-center gap-1 px-3 py-1 bg-purple-500/10 text-purple-400 border border-purple-500/30 rounded text-xs cursor-not-allowed select-none"
                        title="超级管理员受系统保护，不可删除"
                      >
                        <Lock className="w-3 h-3" />
                        受保护
                      </span>
                    ) : (
                      <button
                        onClick={() => {
                          if (confirm(`确定要删除用户"${user.name}"吗？`)) {
                            setUsers(users.filter(u => u.id !== user.id));
                            alert('用户删除成功！');
                          }
                        }}
                        className="px-3 py-1 bg-red-500/20 hover:bg-red-500/30 text-red-400 rounded text-xs transition-colors"
                      >
                        删除
                      </button>
                    )}
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {/* Role Management Modal */}
      <AnimatePresence>
        {showRoleModal && (
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            className="fixed inset-0 bg-black/50 backdrop-blur-sm flex items-center justify-center z-50 p-4"
            onClick={() => setShowRoleModal(false)}
          >
            <motion.div
              initial={{ scale: 0.9, y: 20 }}
              animate={{ scale: 1, y: 0 }}
              exit={{ scale: 0.9, y: 20 }}
              onClick={(e) => e.stopPropagation()}
              className="bg-gray-800 border border-white/10 rounded-xl max-w-4xl w-full max-h-[90vh] overflow-hidden"
            >
              <div className="flex items-center justify-between p-6 border-b border-white/10">
                <h3 className="text-2xl font-bold text-white">角色管理</h3>
                <button onClick={() => setShowRoleModal(false)} className="p-2 hover:bg-white/10 rounded-lg transition-colors">
                  <X className="w-5 h-5 text-gray-400" />
                </button>
              </div>

              <div className="p-6 overflow-y-auto max-h-[calc(90vh-140px)] space-y-4">
                {roles.map((role) => (
                  <div key={role.id} className="p-6 bg-white/5 border border-white/10 rounded-xl">
                    <div className="flex items-start justify-between mb-4">
                      <div className="flex items-center gap-3">
                        <div className={`w-12 h-12 bg-gradient-to-br ${role.color} rounded-lg flex items-center justify-center`}>
                          <Shield className="w-6 h-6 text-white" />
                        </div>
                        <div>
                          <h4 className="font-bold text-white text-lg">{role.name}</h4>
                          <p className="text-sm text-gray-400">{role.description}</p>
                        </div>
                      </div>
                      <div className="flex gap-2 items-center">
                        {role.id !== 'super_admin' && (
                          <button 
                            onClick={() => {
                              setEditingRole(role);
                              setNewRole({
                                name: role.name,
                                description: role.description,
                                color: role.color,
                                permissions: role.permissions,
                              });
                              setShowEditRoleModal(true);
                            }}
                            className="px-3 py-1 bg-blue-500/20 hover:bg-blue-500/30 text-blue-400 rounded text-sm transition-colors"
                          >
                            编辑
                          </button>
                        )}
                        {role.id === 'super_admin' ? (
                          <span
                            className="flex items-center gap-1 px-3 py-1 bg-purple-500/10 text-purple-400 border border-purple-500/30 rounded text-xs cursor-not-allowed select-none"
                            title="超级管理员角色受系统保护，不可删除"
                          >
                            <Lock className="w-3 h-3" />
                            受保护
                          </span>
                        ) : role.id !== 'admin' ? (
                          <button 
                            onClick={() => {
                              if (confirm(`确定要删除角色"${role.name}"吗？`)) {
                                setRoles(roles.filter(r => r.id !== role.id));
                              }
                            }}
                            className="px-3 py-1 bg-red-500/20 hover:bg-red-500/30 text-red-400 rounded text-sm transition-colors"
                          >
                            删除
                          </button>
                        ) : null}
                      </div>
                    </div>
                    
                    <div className="space-y-2">
                      <p className="text-sm font-medium text-gray-300">权限列表：</p>
                      <div className="flex flex-wrap gap-2">
                        {role.permissions.includes('all') ? (
                          <span className="px-3 py-1 bg-green-500/20 text-green-400 rounded-full text-xs">
                            ✓ 全部权限
                          </span>
                        ) : (
                          role.permissions.map((perm) => (
                            <span key={perm} className="px-3 py-1 bg-blue-500/20 text-blue-400 rounded-full text-xs">
                              ✓ {permissions[perm as keyof typeof permissions]?.name || perm}
                            </span>
                          ))
                        )}
                      </div>
                    </div>
                  </div>
                ))}

                <button
                  onClick={() => {
                    setNewRole({
                      name: '',
                      description: '',
                      color: 'from-blue-500 to-cyan-500',
                      permissions: [],
                    });
                    setEditingRole(null);
                    setShowCreateRoleModal(true);
                  }}
                  className="w-full py-4 border-2 border-dashed border-white/20 rounded-xl text-gray-400 hover:border-blue-500 hover:text-blue-400 transition-all flex items-center justify-center gap-2"
                >
                  <Plus className="w-5 h-5" />
                  <span>自定义新角色</span>
                </button>
              </div>
            </motion.div>
          </motion.div>
        )}
      </AnimatePresence>

      {/* Permission Configuration Modal */}
      <AnimatePresence>
        {showPermissionModal && selectedUser && (
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            className="fixed inset-0 bg-black/50 backdrop-blur-sm flex items-center justify-center z-50 p-4"
            onClick={() => setShowPermissionModal(false)}
          >
            <motion.div
              initial={{ scale: 0.9, y: 20 }}
              animate={{ scale: 1, y: 0 }}
              exit={{ scale: 0.9, y: 20 }}
              onClick={(e) => e.stopPropagation()}
              className="bg-gray-800 border border-white/10 rounded-xl max-w-2xl w-full max-h-[90vh] overflow-hidden"
            >
              <div className="flex items-center justify-between p-6 border-b border-white/10">
                <div>
                  <h3 className="text-2xl font-bold text-white">权限配置</h3>
                  <p className="text-sm text-gray-400 mt-1">{selectedUser.name} - {getRoleName(selectedUser.role)}</p>
                </div>
                <button onClick={() => setShowPermissionModal(false)} className="p-2 hover:bg-white/10 rounded-lg transition-colors">
                  <X className="w-5 h-5 text-gray-400" />
                </button>
              </div>

              <div className="p-6 overflow-y-auto max-h-[calc(90vh-200px)] space-y-4">
                <div className="p-4 bg-blue-500/10 border border-blue-500/30 rounded-lg">
                  <p className="text-sm text-blue-400 mb-2">💡 提示</p>
                  <p className="text-xs text-gray-300">用户权限由其角色决定。如需修改，请更改用户角色或自定义角色权限。</p>
                </div>

                <div>
                  <label className="block text-sm font-medium text-gray-300 mb-3">当前角色</label>
                  <select
                    value={selectedRole || ''}
                    onChange={(e) => setSelectedRole(e.target.value)}
                    className="w-full px-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500 transition-colors [&>option]:bg-gray-800 [&>option]:text-white"
                  >
                    {roles.map((role) => (
                      <option key={role.id} value={role.id} className="bg-gray-800 text-white">
                        {role.name} - {role.description}
                      </option>
                    ))}
                  </select>
                </div>

                <div>
                  <label className="block text-sm font-medium text-gray-300 mb-3">权限列表</label>
                  <div className="space-y-2">
                    {Object.entries(permissions).map(([key, perm]) => {
                      const currentRole = roles.find(r => r.id === selectedRole);
                      const hasPermission = currentRole?.permissions.includes('all') || currentRole?.permissions.includes(key);
                      
                      return (
                        <div key={key} className="flex items-center justify-between p-3 bg-white/5 rounded-lg">
                          <div>
                            <p className="font-medium text-white text-sm">{perm.name}</p>
                            <p className="text-xs text-gray-400">{perm.desc}</p>
                          </div>
                          <div className={`px-3 py-1 rounded-full text-xs ${
                            hasPermission ? 'bg-green-500/20 text-green-400' : 'bg-gray-500/20 text-gray-400'
                          }`}>
                            {hasPermission ? '已授权' : '未授权'}
                          </div>
                        </div>
                      );
                    })}
                  </div>
                </div>
              </div>

              <div className="flex gap-3 p-6 border-t border-white/10">
                <button
                  onClick={() => setShowPermissionModal(false)}
                  className="flex-1 px-6 py-3 bg-white/5 hover:bg-white/10 border border-white/10 rounded-lg transition-colors"
                >
                  取消
                </button>
                <button
                  onClick={() => {
                    setUsers(users.map(u => u.id === selectedUser.id ? { ...u, role: selectedRole! } : u));
                    setShowPermissionModal(false);
                    alert('角色更新成功！');
                  }}
                  className="flex-1 px-6 py-3 bg-gradient-to-r from-blue-500 to-purple-500 hover:from-blue-600 hover:to-purple-600 rounded-lg transition-all"
                >
                  保存更改
                </button>
              </div>
            </motion.div>
          </motion.div>
        )}
      </AnimatePresence>

      {/* Create/Edit Role Modal */}
      <AnimatePresence>
        {(showCreateRoleModal || showEditRoleModal) && (
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            className="fixed inset-0 bg-black/50 backdrop-blur-sm flex items-center justify-center z-[60] p-4"
            onClick={() => {
              setShowCreateRoleModal(false);
              setShowEditRoleModal(false);
            }}
          >
            <motion.div
              initial={{ scale: 0.9, y: 20 }}
              animate={{ scale: 1, y: 0 }}
              exit={{ scale: 0.9, y: 20 }}
              onClick={(e) => e.stopPropagation()}
              className="bg-gray-800 border border-white/10 rounded-xl max-w-3xl w-full max-h-[90vh] overflow-hidden"
            >
              <div className="flex items-center justify-between p-6 border-b border-white/10">
                <h3 className="text-2xl font-bold text-white">
                  {editingRole ? '编辑角色' : '自定义新角色'}
                </h3>
                <button
                  onClick={() => {
                    setShowCreateRoleModal(false);
                    setShowEditRoleModal(false);
                  }}
                  className="p-2 hover:bg-white/10 rounded-lg transition-colors"
                >
                  <X className="w-5 h-5 text-gray-400" />
                </button>
              </div>

              <form
                onSubmit={(e) => {
                  e.preventDefault();
                  if (!newRole.name.trim()) {
                    alert('请��入角色名称');
                    return;
                  }
                  if (newRole.permissions.length === 0) {
                    alert('请至少选择一个权限');
                    return;
                  }

                  if (editingRole) {
                    setRoles(roles.map(r => 
                      r.id === editingRole.id 
                        ? { ...r, ...newRole } 
                        : r
                    ));
                    alert('角色更新成功！');
                  } else {
                    const newRoleData = {
                      id: `role_${Date.now()}`,
                      name: newRole.name,
                      description: newRole.description,
                      color: newRole.color,
                      permissions: newRole.permissions,
                    };
                    setRoles([...roles, newRoleData]);
                    alert('角色创建成功！');
                  }

                  setShowCreateRoleModal(false);
                  setShowEditRoleModal(false);
                  setNewRole({
                    name: '',
                    description: '',
                    color: 'from-blue-500 to-cyan-500',
                    permissions: [],
                  });
                }}
                className="p-6 overflow-y-auto max-h-[calc(90vh-140px)] space-y-6"
              >
                <div>
                  <label className="block text-sm font-medium text-gray-300 mb-2">
                    角色名称 <span className="text-red-400">*</span>
                  </label>
                  <input
                    type="text"
                    required
                    value={newRole.name}
                    onChange={(e) => setNewRole({ ...newRole, name: e.target.value })}
                    className="w-full px-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500 transition-colors"
                    placeholder="例如：项目经理"
                  />
                </div>

                <div>
                  <label className="block text-sm font-medium text-gray-300 mb-2">
                    角色描述
                  </label>
                  <textarea
                    value={newRole.description}
                    onChange={(e) => setNewRole({ ...newRole, description: e.target.value })}
                    className="w-full px-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500 transition-colors resize-none"
                    placeholder="描述该角色的职责和权限范围"
                    rows={2}
                  />
                </div>

                <div>
                  <label className="block text-sm font-medium text-gray-300 mb-3">
                    角色颜色主题
                  </label>
                  <div className="grid grid-cols-5 gap-3">
                    {[
                      { color: 'from-red-500 to-orange-500', name: '红色' },
                      { color: 'from-blue-500 to-cyan-500', name: '蓝色' },
                      { color: 'from-green-500 to-emerald-500', name: '绿色' },
                      { color: 'from-purple-500 to-pink-500', name: '紫色' },
                      { color: 'from-amber-500 to-yellow-500', name: '琥珀色' },
                      { color: 'from-indigo-500 to-blue-500', name: '靛蓝色' },
                      { color: 'from-teal-500 to-cyan-500', name: '青色' },
                      { color: 'from-rose-500 to-pink-500', name: '玫瑰色' },
                      { color: 'from-lime-500 to-green-500', name: '青柠色' },
                      { color: 'from-gray-500 to-slate-500', name: '灰色' },
                    ].map((item) => (
                      <button
                        key={item.color}
                        type="button"
                        onClick={() => setNewRole({ ...newRole, color: item.color })}
                        className={`p-4 rounded-lg transition-all ${
                          newRole.color === item.color
                            ? 'ring-2 ring-white ring-offset-2 ring-offset-gray-800 scale-105'
                            : 'opacity-70 hover:opacity-100'
                        }`}
                      >
                        <div className={`w-full h-10 bg-gradient-to-br ${item.color} rounded-lg`} />
                        <p className="text-xs text-gray-400 mt-2 text-center">{item.name}</p>
                      </button>
                    ))}
                  </div>
                </div>

                <div>
                  <label className="block text-sm font-medium text-gray-300 mb-3">
                    权限配置 <span className="text-red-400">*</span>
                  </label>
                  <div className="space-y-2">
                    {Object.entries(permissions).map(([key, perm]) => {
                      const isSelected = newRole.permissions.includes(key) || newRole.permissions.includes('all');
                      const isAllSelected = newRole.permissions.includes('all');
                      
                      return (
                        <div
                          key={key}
                          className={`p-4 rounded-lg border transition-all cursor-pointer ${
                            isSelected
                              ? 'bg-blue-500/10 border-blue-500/30'
                              : 'bg-white/5 border-white/10 hover:border-white/20'
                          }`}
                          onClick={() => {
                            if (key === 'all') {
                              if (isAllSelected) {
                                setNewRole({ ...newRole, permissions: [] });
                              } else {
                                setNewRole({ ...newRole, permissions: ['all'] });
                              }
                            } else {
                              let newPerms = newRole.permissions.filter(p => p !== 'all');
                              if (isSelected) {
                                newPerms = newPerms.filter(p => p !== key);
                              } else {
                                newPerms = [...newPerms, key];
                              }
                              setNewRole({ ...newRole, permissions: newPerms });
                            }
                          }}
                        >
                          <div className="flex items-center justify-between">
                            <div className="flex-1">
                              <p className="font-medium text-white text-sm">{perm.name}</p>
                              <p className="text-xs text-gray-400 mt-1">{perm.desc}</p>
                            </div>
                            <div className={`w-5 h-5 rounded border-2 flex items-center justify-center transition-all ${
                              isSelected
                                ? 'bg-blue-500 border-blue-500'
                                : 'border-gray-600'
                            }`}>
                              {isSelected && (
                                <svg className="w-3 h-3 text-white" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={3} d="M5 13l4 4L19 7" />
                                </svg>
                              )}
                            </div>
                          </div>
                        </div>
                      );
                    })}
                    
                    <div
                      className={`p-4 rounded-lg border-2 border-dashed transition-all cursor-pointer ${
                        newRole.permissions.includes('all')
                          ? 'bg-green-500/10 border-green-500/50'
                          : 'bg-white/5 border-white/20 hover:border-green-500/30'
                      }`}
                      onClick={() => {
                        if (newRole.permissions.includes('all')) {
                          setNewRole({ ...newRole, permissions: [] });
                        } else {
                          setNewRole({ ...newRole, permissions: ['all'] });
                        }
                      }}
                    >
                      <div className="flex items-center justify-between">
                        <div className="flex-1">
                          <p className="font-bold text-white text-sm">✨ 全部权限</p>
                          <p className="text-xs text-gray-400 mt-1">授予所有系统权限（仅推荐管理员角色）</p>
                        </div>
                        <div className={`w-5 h-5 rounded border-2 flex items-center justify-center transition-all ${
                          newRole.permissions.includes('all')
                            ? 'bg-green-500 border-green-500'
                            : 'border-gray-600'
                        }`}>
                          {newRole.permissions.includes('all') && (
                            <svg className="w-3 h-3 text-white" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={3} d="M5 13l4 4L19 7" />
                            </svg>
                          )}
                        </div>
                      </div>
                    </div>
                  </div>
                </div>

                <div className="flex gap-3 pt-4 border-t border-white/10">
                  <button
                    type="button"
                    onClick={() => {
                      setShowCreateRoleModal(false);
                      setShowEditRoleModal(false);
                    }}
                    className="flex-1 px-6 py-3 bg-white/5 hover:bg-white/10 border border-white/10 rounded-lg transition-colors"
                  >
                    取消
                  </button>
                  <button
                    type="submit"
                    className="flex-1 px-6 py-3 bg-gradient-to-r from-blue-500 to-purple-500 hover:from-blue-600 hover:to-purple-600 rounded-lg transition-all font-semibold"
                  >
                    {editingRole ? '保存更改' : '创建角色'}
                  </button>
                </div>
              </form>
            </motion.div>
          </motion.div>
        )}
      </AnimatePresence>

      {/* Add User Modal */}
      <AnimatePresence>
        {showAddUserModal && (
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            className="fixed inset-0 bg-black/50 backdrop-blur-sm flex items-center justify-center z-[60] p-4"
            onClick={() => setShowAddUserModal(false)}
          >
            <motion.div
              initial={{ scale: 0.9, y: 20 }}
              animate={{ scale: 1, y: 0 }}
              exit={{ scale: 0.9, y: 20 }}
              onClick={(e) => e.stopPropagation()}
              className="bg-gray-800 border border-white/10 rounded-xl max-w-3xl w-full max-h-[90vh] overflow-hidden"
            >
              <div className="flex items-center justify-between p-6 border-b border-white/10">
                <h3 className="text-2xl font-bold text-white">添加新用户</h3>
                <button
                  onClick={() => setShowAddUserModal(false)}
                  className="p-2 hover:bg-white/10 rounded-lg transition-colors"
                >
                  <X className="w-5 h-5 text-gray-400" />
                </button>
              </div>

              <form
                onSubmit={(e) => {
                  e.preventDefault();
                  if (!newUser.name.trim()) { alert('请输入用户名'); return; }
                  if (!newUser.email.trim()) { alert('请输入邮箱地址'); return; }
                  if (!newUser.phone.trim()) { alert('请输入联系电话'); return; }
                  if (!newUser.department.trim()) { alert('请输入部门名称'); return; }
                  if (!newUser.password.trim()) { alert('请输入密码'); return; }

                  const newUserData = {
                    id: `user_${Date.now()}`,
                    name: newUser.name,
                    email: newUser.email,
                    phone: newUser.phone,
                    department: newUser.department,
                    role: newUser.role,
                    password: newUser.password,
                    status: 'active',
                  };
                  setUsers([...users, newUserData]);
                  alert('用户创建成功！');
                  setShowAddUserModal(false);
                  setNewUser({ name: '', email: '', phone: '', department: '', role: 'staff', password: '' });
                }}
                className="p-6 overflow-y-auto max-h-[calc(90vh-140px)] space-y-6"
              >
                <div>
                  <label className="block text-sm font-medium text-gray-300 mb-2">
                    用户名 <span className="text-red-400">*</span>
                  </label>
                  <input
                    type="text"
                    required
                    value={newUser.name}
                    onChange={(e) => setNewUser({ ...newUser, name: e.target.value })}
                    className="w-full px-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500 transition-colors"
                    placeholder="例如：张三"
                  />
                </div>

                <div>
                  <label className="block text-sm font-medium text-gray-300 mb-2">
                    邮箱地址 <span className="text-red-400">*</span>
                  </label>
                  <input
                    type="email"
                    required
                    value={newUser.email}
                    onChange={(e) => setNewUser({ ...newUser, email: e.target.value })}
                    className="w-full px-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500 transition-colors"
                    placeholder="例如：zhangsan@example.com"
                  />
                </div>

                <div>
                  <label className="block text-sm font-medium text-gray-300 mb-2">
                    联系电话 <span className="text-red-400">*</span>
                  </label>
                  <input
                    type="text"
                    required
                    value={newUser.phone}
                    onChange={(e) => setNewUser({ ...newUser, phone: e.target.value })}
                    className="w-full px-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500 transition-colors"
                    placeholder="例如：138-0000-1111"
                  />
                </div>

                <div>
                  <label className="block text-sm font-medium text-gray-300 mb-2">
                    部门名称 <span className="text-red-400">*</span>
                  </label>
                  <input
                    type="text"
                    required
                    value={newUser.department}
                    onChange={(e) => setNewUser({ ...newUser, department: e.target.value })}
                    className="w-full px-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500 transition-colors"
                    placeholder="例如：运维部"
                  />
                </div>

                <div>
                  <label className="block text-sm font-medium text-gray-300 mb-2">
                    角色 <span className="text-red-400">*</span>
                  </label>
                  <select
                    value={newUser.role}
                    onChange={(e) => setNewUser({ ...newUser, role: e.target.value })}
                    className="w-full px-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500 transition-colors [&>option]:bg-gray-800 [&>option]:text-white"
                  >
                    {roles.filter(r => r.id !== 'super_admin').map((role) => (
                      <option key={role.id} value={role.id} className="bg-gray-800 text-white">
                        {role.name}
                      </option>
                    ))}
                  </select>
                </div>

                <div>
                  <label className="block text-sm font-medium text-gray-300 mb-2">
                    密码 <span className="text-red-400">*</span>
                  </label>
                  <input
                    type="password"
                    required
                    value={newUser.password}
                    onChange={(e) => setNewUser({ ...newUser, password: e.target.value })}
                    className="w-full px-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500 transition-colors"
                    placeholder="设置初始密码"
                  />
                </div>

                <div className="flex gap-3 pt-4 border-t border-white/10">
                  <button
                    type="button"
                    onClick={() => setShowAddUserModal(false)}
                    className="flex-1 px-6 py-3 bg-white/5 hover:bg-white/10 border border-white/10 rounded-lg transition-colors"
                  >
                    取消
                  </button>
                  <button
                    type="submit"
                    className="flex-1 px-6 py-3 bg-gradient-to-r from-blue-500 to-purple-500 hover:from-blue-600 hover:to-purple-600 rounded-lg transition-all font-semibold"
                  >
                    创建用户
                  </button>
                </div>
              </form>
            </motion.div>
          </motion.div>
        )}
      </AnimatePresence>

      {/* Edit User Modal */}
      <AnimatePresence>
        {showEditUserModal && editingUser && (
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            className="fixed inset-0 bg-black/50 backdrop-blur-sm flex items-center justify-center z-[60] p-4"
            onClick={() => setShowEditUserModal(false)}
          >
            <motion.div
              initial={{ scale: 0.9, y: 20 }}
              animate={{ scale: 1, y: 0 }}
              exit={{ scale: 0.9, y: 20 }}
              onClick={(e) => e.stopPropagation()}
              className="bg-gray-800 border border-white/10 rounded-xl max-w-3xl w-full max-h-[90vh] overflow-hidden"
            >
              <div className="flex items-center justify-between p-6 border-b border-white/10">
                <h3 className="text-2xl font-bold text-white">编辑用户</h3>
                <button onClick={() => setShowEditUserModal(false)} className="p-2 hover:bg-white/10 rounded-lg transition-colors">
                  <X className="w-5 h-5 text-gray-400" />
                </button>
              </div>

              <form
                onSubmit={(e) => {
                  e.preventDefault();
                  setUsers(users.map(u => u.id === editingUser.id ? { ...u, ...newUser } : u));
                  setShowEditUserModal(false);
                  alert('用户信息更新成功！');
                }}
                className="p-6 overflow-y-auto max-h-[calc(90vh-140px)] space-y-6"
              >
                <div className="grid grid-cols-2 gap-4">
                  <div>
                    <label className="block text-sm font-medium text-gray-300 mb-2">用户名</label>
                    <input
                      type="text"
                      value={newUser.name}
                      onChange={(e) => setNewUser({ ...newUser, name: e.target.value })}
                      className="w-full px-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500 transition-colors"
                    />
                  </div>
                  <div>
                    <label className="block text-sm font-medium text-gray-300 mb-2">邮箱</label>
                    <input
                      type="email"
                      value={newUser.email}
                      onChange={(e) => setNewUser({ ...newUser, email: e.target.value })}
                      className="w-full px-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500 transition-colors"
                    />
                  </div>
                </div>

                <div className="grid grid-cols-2 gap-4">
                  <div>
                    <label className="block text-sm font-medium text-gray-300 mb-2">电话</label>
                    <input
                      type="text"
                      value={newUser.phone}
                      onChange={(e) => setNewUser({ ...newUser, phone: e.target.value })}
                      className="w-full px-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500 transition-colors"
                    />
                  </div>
                  <div>
                    <label className="block text-sm font-medium text-gray-300 mb-2">部门</label>
                    <input
                      type="text"
                      value={newUser.department}
                      onChange={(e) => setNewUser({ ...newUser, department: e.target.value })}
                      className="w-full px-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500 transition-colors"
                    />
                  </div>
                </div>

                <div>
                  <label className="block text-sm font-medium text-gray-300 mb-2">角色</label>
                  <select
                    value={newUser.role}
                    onChange={(e) => setNewUser({ ...newUser, role: e.target.value })}
                    className="w-full px-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500 transition-colors [&>option]:bg-gray-800 [&>option]:text-white"
                  >
                    {roles.filter(r => r.id !== 'super_admin').map((role) => (
                      <option key={role.id} value={role.id} className="bg-gray-800 text-white">
                        {role.name}
                      </option>
                    ))}
                  </select>
                </div>

                <div className="flex gap-3 pt-4 border-t border-white/10">
                  <button type="button" onClick={() => setShowEditUserModal(false)} className="flex-1 px-6 py-3 bg-white/5 hover:bg-white/10 border border-white/10 rounded-lg transition-colors">
                    取消
                  </button>
                  <button type="submit" className="flex-1 px-6 py-3 bg-gradient-to-r from-blue-500 to-purple-500 hover:from-blue-600 hover:to-purple-600 rounded-lg transition-all font-semibold">
                    保存更改
                  </button>
                </div>
              </form>
            </motion.div>
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}

function DeviceAuthorization() {
  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-xl font-semibold text-white mb-4 flex items-center gap-2">
          <Zap className="w-5 h-5 text-blue-400" />
          设备授权管理
        </h2>
      </div>
      <div className="space-y-4">
        {[
          { name: '智能冰箱-A01', model: 'SR-F520BX', status: '已授权', expires: '2026-12-31' },
          { name: '智能烤箱-B01', model: 'OV-X8000', status: '已授权', expires: '2026-12-31' },
          { name: '智能炉灶-C01', model: 'GS-P4000', status: '待授权', expires: '-' },
          { name: '洗碗机-D01', model: 'DW-M9000', status: '已过期', expires: '2025-12-31' },
        ].map((device, index) => (
          <div key={index} className="flex items-center justify-between p-4 bg-white/5 border border-white/10 rounded-lg">
            <div className="flex items-center gap-3">
              <div className="w-10 h-10 bg-blue-500/20 rounded-lg flex items-center justify-center">
                <Zap className="w-5 h-5 text-blue-400" />
              </div>
              <div>
                <p className="font-medium text-white">{device.name}</p>
                <p className="text-sm text-gray-400">{device.model}</p>
              </div>
            </div>
            <div className="flex items-center gap-4">
              <div>
                <p className="text-xs text-gray-400">到期时间</p>
                <p className="text-sm text-white">{device.expires}</p>
              </div>
              <span className={`px-3 py-1 rounded-full text-xs ${
                device.status === '已授权' ? 'bg-green-500/20 text-green-400' :
                device.status === '待授权' ? 'bg-amber-500/20 text-amber-400' :
                'bg-red-500/20 text-red-400'
              }`}>
                {device.status}
              </span>
              <button className="px-3 py-1 bg-blue-500/20 hover:bg-blue-500/30 text-blue-400 rounded text-xs transition-colors">
                {device.status === '已授权' ? '续期' : '授权'}
              </button>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

function APIConfiguration() {
  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-xl font-semibold text-white mb-4 flex items-center gap-2">
          <Key className="w-5 h-5 text-blue-400" />
          API 配置
        </h2>
      </div>
      <div className="space-y-4">
        <div>
          <label className="block text-sm font-medium text-gray-300 mb-2">API Base URL</label>
          <input
            type="text"
            defaultValue="https://api.kitchen-iot.com/v1"
            className="w-full px-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500 transition-colors font-mono text-sm"
          />
        </div>
        <div>
          <label className="block text-sm font-medium text-gray-300 mb-2">API Key</label>
          <div className="flex gap-2">
            <input
              type="password"
              defaultValue="sk-xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"
              className="flex-1 px-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500 transition-colors font-mono text-sm"
            />
            <button className="px-4 py-2 bg-white/5 hover:bg-white/10 border border-white/10 rounded-lg transition-colors text-gray-300 text-sm">
              重新生成
            </button>
          </div>
        </div>
        <div>
          <label className="block text-sm font-medium text-gray-300 mb-2">Webhook URL</label>
          <input
            type="text"
            defaultValue="https://your-domain.com/webhook"
            className="w-full px-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500 transition-colors font-mono text-sm"
          />
        </div>
        <div className="flex items-center justify-between p-4 bg-white/5 rounded-lg">
          <div>
            <p className="font-medium text-white">启用 API 访问</p>
            <p className="text-sm text-gray-400">允许通过 API 访问系统数据</p>
          </div>
          <label className="relative inline-flex items-center cursor-pointer">
            <input type="checkbox" className="sr-only peer" defaultChecked />
            <div className="w-11 h-6 bg-gray-700 peer-focus:outline-none rounded-full peer peer-checked:after:translate-x-full peer-checked:after:border-white after:content-[''] after:absolute after:top-[2px] after:left-[2px] after:bg-white after:rounded-full after:h-5 after:w-5 after:transition-all peer-checked:bg-blue-500"></div>
          </label>
        </div>
      </div>
      <button className="w-full flex items-center justify-center gap-2 px-6 py-3 bg-gradient-to-r from-blue-500 to-purple-500 hover:from-blue-600 hover:to-purple-600 rounded-lg transition-all">
        <Save className="w-5 h-5" />
        <span>保存设置</span>
      </button>
    </div>
  );
}
