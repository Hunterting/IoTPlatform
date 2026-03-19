import { useState } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import { Users, X, Plus, Shield, Check, Edit2, Trash2, Search } from 'lucide-react';
import { PageType } from '@/app/components/Sidebar';
import { useAuth, User } from '@/app/contexts/AuthContext';
import { PERMISSIONS, DEFAULT_ROLES, RoleDefinition, Permission, PERMISSION_GROUPS } from '@/app/config/permissions';
import { useArea } from '@/app/contexts/AreaContext';

interface UserManagementPageProps {
  activePage: PageType;
}

export function UserManagementPage({ activePage }: UserManagementPageProps) {
  const getPageTitle = () => {
    switch (activePage) {
      case 'users-list':
        return { title: '用户列表', desc: '管理系统用户信息' };
      case 'users-roles':
        return { title: '角色管理', desc: '配置用户角色与权限' };
      default:
        return { title: '用户管理', desc: '用户与权限管理' };
    }
  };

  const pageInfo = getPageTitle();

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
        {(activePage === 'users-list' || activePage === 'users') && <UserList />}
        {activePage === 'users-roles' && <RoleManagement />}
      </motion.div>
    </div>
  );
}

// 用户列表组件
function UserList() {
  const authContext = useAuth();
  const { currentCustomer, roles, hasPermission } = authContext;
  const { flattenAreas, areas } = useArea();
  
  const availableAreas = flattenAreas(areas);

  const [showAddUserModal, setShowAddUserModal] = useState(false);
  const [showEditUserModal, setShowEditUserModal] = useState(false);
  const [editingUser, setEditingUser] = useState<User | null>(null);
  const [searchTerm, setSearchTerm] = useState('');
  
  const [newUserForm, setNewUserForm] = useState({
    name: '',
    email: '',
    phone: '',
    department: '',
    role: 'staff',
    password: '',
    status: 'active',
    allowedAreaIds: [] as string[],
  });

  const [users, setUsers] = useState<any[]>([
    { id: '1', name: '超级管理员', role: 'super_admin', email: 'admin@system.com', status: 'active', phone: '138-0000-0000', department: '系统管理部', allowedAreaIds: [] },
    { id: '2', name: '海底捞管理员', role: 'admin', email: 'admin@haidilao.com', status: 'active', phone: '138-0000-1111', department: '海底捞管理部', allowedAreaIds: [] },
    { id: '3', name: '运维人员', role: 'operator', email: 'operator@haidilao.com', status: 'active', phone: '138-0000-2222', department: '运维部', allowedAreaIds: ['L2-001'] },
    { id: '4', name: '厨师长', role: 'chef', email: 'chef@haidilao.com', status: 'active', phone: '138-0000-3333', department: '厨房部', allowedAreaIds: ['L3-003', 'L3-004'] },
  ]);

  const getRoleName = (roleCode: string) => {
    return roles[roleCode]?.name || roleCode;
  };

  const getRoleColor = (roleCode: string) => {
    const colors: Record<string, string> = {
      super_admin: 'from-purple-600 to-indigo-600',
      admin: 'from-red-500 to-orange-500',
      operator: 'from-blue-500 to-cyan-500',
      chef: 'from-green-500 to-emerald-500',
      staff: 'from-gray-500 to-slate-500',
    };
    return colors[roleCode] || 'from-gray-500 to-slate-500';
  };

  const filteredUsers = users.filter(user => {
    const term = searchTerm.toLowerCase();
    const roleName = getRoleName(user.role).toLowerCase();
    return (
      user.name.toLowerCase().includes(term) ||
      user.email.toLowerCase().includes(term) ||
      user.phone.includes(term) ||
      roleName.includes(term) ||
      (user.department && user.department.toLowerCase().includes(term))
    );
  });

  const handleSubmit = (e: React.FormEvent, isEdit: boolean) => {
    e.preventDefault();
    if (isEdit && editingUser) {
      setUsers(users.map(u => u.id === editingUser.id ? { ...u, ...newUserForm, id: u.id } : u));
      setShowEditUserModal(false);
    } else {
      const newUser = {
        id: `user_${Date.now()}`,
        ...newUserForm,
        appCode: currentCustomer?.appCode || '',
      };
      setUsers([...users, newUser]);
      setShowAddUserModal(false);
    }
    setNewUserForm({ name: '', email: '', phone: '', department: '', role: 'staff', password: '', status: 'active', allowedAreaIds: [] });
  };

  const toggleAreaSelection = (areaId: string) => {
    setNewUserForm(prev => {
      const exists = prev.allowedAreaIds.includes(areaId);
      if (exists) {
        return { ...prev, allowedAreaIds: prev.allowedAreaIds.filter(id => id !== areaId) };
      } else {
        return { ...prev, allowedAreaIds: [...prev.allowedAreaIds, areaId] };
      }
    });
  };

  const selectedRoleDef = roles[newUserForm.role];
  const showAreaSelection = selectedRoleDef?.dataScope === 'CUSTOM';

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between gap-4">
        <h2 className="text-xl font-semibold text-white flex items-center gap-2 shrink-0">
          <Users className="w-5 h-5 text-blue-400" />
          用户列表
        </h2>
        
        <div className="flex-1 max-w-md relative">
          <Search className="absolute left-3 top-1/2 transform -translate-y-1/2 w-4 h-4 text-gray-400" />
          <input
            type="text"
            placeholder="搜索用户、邮箱、角色..."
            value={searchTerm}
            onChange={(e) => setSearchTerm(e.target.value)}
            className="w-full pl-10 pr-4 py-2 bg-white/5 border border-white/10 rounded-lg text-white placeholder-gray-500 focus:outline-none focus:border-blue-500 transition-colors"
          />
        </div>

        {hasPermission(PERMISSIONS.CREATE_USERS) && (
          <button 
            onClick={() => {
               setNewUserForm({ name: '', email: '', phone: '', department: '', role: 'staff', password: '', status: 'active', allowedAreaIds: [] });
               setShowAddUserModal(true);
            }}
            className="px-4 py-2 bg-blue-500 hover:bg-blue-600 rounded-lg transition-colors flex items-center gap-2 shrink-0"
          >
            <Plus className="w-4 h-4" />
            添加用户
          </button>
        )}
      </div>

      {/* Users Table */}
      <div className="bg-white/5 border border-white/10 rounded-xl overflow-hidden">
        <table className="w-full">
          <thead className="border-b border-white/10">
            <tr>
              <th className="px-6 py-4 text-left text-sm font-semibold text-gray-300">用户</th>
              <th className="px-6 py-4 text-left text-sm font-semibold text-gray-300">角色</th>
              <th className="px-6 py-4 text-left text-sm font-semibold text-gray-300">区域权限</th>
              <th className="px-6 py-4 text-left text-sm font-semibold text-gray-300">状态</th>
              <th className="px-6 py-4 text-center text-sm font-semibold text-gray-300">操作</th>
            </tr>
          </thead>
          <tbody>
            {filteredUsers.length > 0 ? (
              filteredUsers.map((user) => {
                const roleDef = roles[user.role];
                return (
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
                    <span className="text-sm text-gray-300">
                      {roleDef?.dataScope === 'ALL' ? (
                        <span className="text-green-400">全部区域</span>
                      ) : (
                        user.allowedAreaIds && user.allowedAreaIds.length > 0 
                          ? `${user.allowedAreaIds.length} 个指定区域` 
                          : <span className="text-gray-500">无权限</span>
                      )}
                    </span>
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
                      {hasPermission(PERMISSIONS.UPDATE_USERS) && (
                        <button
                          onClick={() => {
                            setEditingUser(user);
                            setNewUserForm({
                              name: user.name,
                              email: user.email,
                              phone: user.phone,
                              department: user.department || '',
                              role: user.role,
                              password: '',
                              status: user.status,
                              allowedAreaIds: user.allowedAreaIds || [],
                            });
                            setShowEditUserModal(true);
                          }}
                          className="px-3 py-1 bg-amber-500/20 hover:bg-amber-500/30 text-amber-400 rounded text-xs transition-colors"
                        >
                          编辑
                        </button>
                      )}
                      {user.role === 'super_admin' ? (
                        <span className="flex items-center gap-1 px-3 py-1 bg-purple-500/10 text-purple-400 border border-purple-500/30 rounded text-xs cursor-not-allowed select-none" title="超级管理员受系统保护，不可删除">
                          <Shield className="w-3 h-3" />
                          受保护
                        </span>
                      ) : (
                        hasPermission(PERMISSIONS.DELETE_USERS) && (
                          <button 
                            onClick={() => {
                               if (confirm('确认删除?')) {
                                   setUsers(users.filter(u => u.id !== user.id));
                               }
                            }}
                            className="px-3 py-1 bg-red-500/20 hover:bg-red-500/30 text-red-400 rounded text-xs transition-colors"
                          >
                            删除
                          </button>
                        )
                      )}
                    </div>
                  </td>
                </tr>
              )})
            ) : (
              <tr>
                <td colSpan={5} className="px-6 py-8 text-center text-gray-500">
                  没有找到匹配的用户
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      {/* Modals ... (keep existing implementation) */}
      <AnimatePresence>
        {(showAddUserModal || showEditUserModal) && (
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            className="fixed inset-0 bg-black/50 backdrop-blur-sm flex items-center justify-center z-[60] p-4"
            onClick={() => { setShowAddUserModal(false); setShowEditUserModal(false); }}
          >
            <motion.div
              initial={{ scale: 0.9, y: 20 }}
              animate={{ scale: 1, y: 0 }}
              exit={{ scale: 0.9, y: 20 }}
              onClick={(e) => e.stopPropagation()}
              className="bg-gray-800 border border-white/10 rounded-xl max-w-2xl w-full max-h-[90vh] overflow-hidden flex flex-col"
            >
              <div className="flex items-center justify-between p-6 border-b border-white/10 shrink-0">
                <h3 className="text-2xl font-bold text-white">{showEditUserModal ? '编辑用户' : '添加新用户'}</h3>
                <button onClick={() => { setShowAddUserModal(false); setShowEditUserModal(false); }} className="p-2 hover:bg-white/10 rounded-lg transition-colors">
                  <X className="w-5 h-5 text-gray-400" />
                </button>
              </div>

              <form onSubmit={(e) => handleSubmit(e, showEditUserModal)} className="p-6 overflow-y-auto flex-1 space-y-4">
                <div className="grid grid-cols-2 gap-4">
                  <div>
                    <label className="block text-sm font-medium text-gray-300 mb-2">用户名 *</label>
                    <input
                      type="text"
                      required
                      value={newUserForm.name}
                      onChange={(e) => setNewUserForm({ ...newUserForm, name: e.target.value })}
                      className="w-full px-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500 transition-colors"
                    />
                  </div>
                  <div>
                    <label className="block text-sm font-medium text-gray-300 mb-2">邮箱 *</label>
                    <input
                      type="email"
                      required
                      value={newUserForm.email}
                      onChange={(e) => setNewUserForm({ ...newUserForm, email: e.target.value })}
                      className="w-full px-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500 transition-colors"
                    />
                  </div>
                </div>
                
                <div className="grid grid-cols-2 gap-4">
                    <div>
                    <label className="block text-sm font-medium text-gray-300 mb-2">角色 *</label>
                    <select
                        value={newUserForm.role}
                        onChange={(e) => setNewUserForm({ ...newUserForm, role: e.target.value })}
                        disabled={showEditUserModal && editingUser?.role === 'super_admin'}
                        className={`w-full px-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500 transition-colors [&>option]:bg-gray-800 ${showEditUserModal && editingUser?.role === 'super_admin' ? 'opacity-50 cursor-not-allowed' : ''}`}
                    >
                        {Object.values(roles)
                          .filter((role) => role.code !== 'super_admin')
                          .map((role) => (
                            <option key={role.code} value={role.code}>{role.name}</option>
                          ))
                        }
                        {/* 编辑超管时保留其选项为只读显示 */}
                        {showEditUserModal && editingUser?.role === 'super_admin' && (
                          <option value="super_admin">{roles['super_admin']?.name ?? '超级管理员'}</option>
                        )}
                    </select>
                    {showEditUserModal && editingUser?.role === 'super_admin' && (
                      <p className="mt-1 text-xs text-purple-400 flex items-center gap-1">
                        <Shield className="w-3 h-3" />
                        超级管理员角色受系统保护，不可变更
                      </p>
                    )}
                    </div>
                    <div>
                        <label className="block text-sm font-medium text-gray-300 mb-2">状态</label>
                        <select
                            value={newUserForm.status}
                            onChange={(e) => setNewUserForm({ ...newUserForm, status: e.target.value })}
                            className="w-full px-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500 transition-colors [&>option]:bg-gray-800"
                        >
                            <option value="active">活跃</option>
                            <option value="inactive">未激活</option>
                        </select>
                    </div>
                </div>

                {showAreaSelection && (
                    <div className="mt-4 p-4 bg-white/5 rounded-lg border border-white/10">
                        <label className="block text-sm font-medium text-blue-400 mb-3 flex items-center gap-2">
                            <Shield className="w-4 h-4" />
                            区域数据权限配置
                        </label>
                        <p className="text-xs text-gray-400 mb-3">勾选该用户可查看和管理的区域。未勾选的区域（及其设备）将对该用户不可见。</p>
                        
                        <div className="max-h-48 overflow-y-auto space-y-1 custom-scrollbar pr-2">
                            {availableAreas.map(area => (
                                <div 
                                    key={area.id} 
                                    onClick={() => toggleAreaSelection(area.id)}
                                    className={`flex items-center gap-3 p-2 rounded cursor-pointer transition-colors ${
                                        newUserForm.allowedAreaIds.includes(area.id) ? 'bg-blue-500/20 border border-blue-500/30' : 'hover:bg-white/5 border border-transparent'
                                    }`}
                                >
                                    <div className={`w-4 h-4 rounded border flex items-center justify-center ${
                                        newUserForm.allowedAreaIds.includes(area.id) ? 'bg-blue-500 border-blue-500' : 'border-gray-500'
                                    }`}>
                                        {newUserForm.allowedAreaIds.includes(area.id) && <Check className="w-3 h-3 text-white" />}
                                    </div>
                                    <span className="text-sm text-gray-300">
                                        {area.name} 
                                        <span className="text-xs text-gray-500 ml-2">({area.type})</span>
                                    </span>
                                </div>
                            ))}
                        </div>
                    </div>
                )}

                <div>
                  <label className="block text-sm font-medium text-gray-300 mb-2">密码</label>
                  <input
                    type="password"
                    value={newUserForm.password}
                    onChange={(e) => setNewUserForm({ ...newUserForm, password: e.target.value })}
                    className="w-full px-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500 transition-colors"
                    placeholder={showEditUserModal ? "如需修改密码请输入" : "设置初始密码"}
                  />
                </div>

                <div className="flex gap-3 pt-4">
                  <button type="button" onClick={() => { setShowAddUserModal(false); setShowEditUserModal(false); }} className="flex-1 px-6 py-3 bg-white/5 hover:bg-white/10 border border-white/10 rounded-lg transition-colors">
                    取消
                  </button>
                  <button type="submit" className="flex-1 px-6 py-3 bg-gradient-to-r from-blue-500 to-purple-500 hover:from-blue-600 hover:to-purple-600 rounded-lg transition-all font-semibold">
                    {showEditUserModal ? '保存更改' : '创建用户'}
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

// 角色管理组件
function RoleManagement() {
  const { roles, addRole, updateRole, deleteRole, hasPermission } = useAuth();
  const [showRoleModal, setShowRoleModal] = useState(false);
  const [editingRoleCode, setEditingRoleCode] = useState<string | null>(null);
  const [searchTerm, setSearchTerm] = useState('');

  const [roleForm, setRoleForm] = useState<RoleDefinition>({
    code: '',
    name: '',
    description: '',
    permissions: [],
    dataScope: 'CUSTOM',
  });

  const rolesList = Object.values(roles);

  const filteredRoles = rolesList.filter(role => {
    const term = searchTerm.toLowerCase();
    return (
      role.name.toLowerCase().includes(term) ||
      role.code.toLowerCase().includes(term) ||
      role.description.toLowerCase().includes(term)
    );
  });

  const openAddModal = () => {
    setEditingRoleCode(null);
    setRoleForm({
      code: '',
      name: '',
      description: '',
      permissions: [],
      dataScope: 'CUSTOM',
    });
    setShowRoleModal(true);
  };

  const openEditModal = (role: RoleDefinition) => {
    setEditingRoleCode(role.code);
    setRoleForm({ ...role });
    setShowRoleModal(true);
  };

  const handleDelete = (code: string) => {
    if (confirm('确定要删除这个角色吗？此操作无法撤销。')) {
        deleteRole(code);
    }
  };

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (!roleForm.code || !roleForm.name) {
        alert('请填写角色名称和代码');
        return;
    }

    if (editingRoleCode) {
        updateRole(roleForm);
    } else {
        if (roles[roleForm.code]) {
            alert('角色代码已存在，请使用其他代码');
            return;
        }
        addRole(roleForm);
    }
    setShowRoleModal(false);
  };

  const togglePermission = (perm: Permission) => {
    setRoleForm(prev => {
        const hasPerm = prev.permissions.includes(perm);
        if (hasPerm) {
            return { ...prev, permissions: prev.permissions.filter(p => p !== perm) };
        } else {
            return { ...prev, permissions: [...prev.permissions, perm] };
        }
    });
  };

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between gap-4">
        <h2 className="text-xl font-semibold text-white flex items-center gap-2 shrink-0">
          <Shield className="w-5 h-5 text-blue-400" />
          角色定义
        </h2>

        <div className="flex-1 max-w-md relative">
          <Search className="absolute left-3 top-1/2 transform -translate-y-1/2 w-4 h-4 text-gray-400" />
          <input
            type="text"
            placeholder="搜索角色名称、代码、描述..."
            value={searchTerm}
            onChange={(e) => setSearchTerm(e.target.value)}
            className="w-full pl-10 pr-4 py-2 bg-white/5 border border-white/10 rounded-lg text-white placeholder-gray-500 focus:outline-none focus:border-blue-500 transition-colors"
          />
        </div>

        {hasPermission(PERMISSIONS.CREATE_ROLES) && (
          <button 
              onClick={openAddModal}
              className="px-4 py-2 bg-purple-500 hover:bg-purple-600 rounded-lg transition-colors flex items-center gap-2 shrink-0"
          >
            <Plus className="w-4 h-4" />
            创建角色
          </button>
        )}
      </div>

      <div className="space-y-4">
        {filteredRoles.length > 0 ? (
          filteredRoles.map((role) => (
            <div key={role.code} className="p-6 bg-white/5 border border-white/10 rounded-xl">
              <div className="flex items-start justify-between mb-4">
                <div className="flex items-center gap-3">
                  <div className={`w-12 h-12 bg-gradient-to-br from-blue-500 to-indigo-500 rounded-lg flex items-center justify-center`}>
                    <Shield className="w-6 h-6 text-white" />
                  </div>
                  <div>
                    <h4 className="font-bold text-white text-lg">{role.name}</h4>
                    <p className="text-sm text-gray-400">{role.description}</p>
                  </div>
                </div>
                <div className="flex gap-2 items-center">
                   <span className={`mr-2 px-3 py-1 rounded text-xs font-medium border ${role.dataScope === 'ALL' ? 'border-green-500 text-green-400' : 'border-blue-500 text-blue-400'}`}>
                      数据范围: {role.dataScope === 'ALL' ? '全部数据' : '自定义指定'}
                   </span>
                   
                   {hasPermission(PERMISSIONS.UPDATE_ROLES) && (
                     <button 
                        onClick={() => openEditModal(role)}
                        className="p-2 bg-white/5 hover:bg-white/10 rounded-lg transition-colors text-blue-400"
                        title="编辑"
                     >
                        <Edit2 className="w-4 h-4" />
                     </button>
                   )}

                   {hasPermission(PERMISSIONS.DELETE_ROLES) && role.code !== 'super_admin' && role.code !== 'admin' && (
                      <button 
                          onClick={() => handleDelete(role.code)}
                          className="p-2 bg-white/5 hover:bg-red-500/20 rounded-lg transition-colors text-red-400"
                          title="删除"
                      >
                          <Trash2 className="w-4 h-4" />
                      </button>
                   )}
                </div>
              </div>
              
              <div className="space-y-2">
                <p className="text-sm font-medium text-gray-300">权限列表：</p>
                <div className="flex flex-wrap gap-2">
                  {role.permissions.length === 0 ? <span className="text-gray-500 text-xs">无权限</span> : 
                    role.permissions.map((perm) => (
                      <span key={perm} className="px-3 py-1 bg-white/10 text-gray-300 rounded-full text-xs">
                        {perm}
                      </span>
                    ))
                  }
                </div>
              </div>
            </div>
          ))
        ) : (
          <div className="p-8 text-center text-gray-500 bg-white/5 border border-white/10 rounded-xl">
            没有找到匹配的角色
          </div>
        )}
      </div>

      {/* Role Edit Modal - keeping existing code */}
      <AnimatePresence>
        {showRoleModal && (
            <motion.div
                initial={{ opacity: 0 }}
                animate={{ opacity: 1 }}
                exit={{ opacity: 0 }}
                className="fixed inset-0 bg-black/50 backdrop-blur-sm flex items-center justify-center z-[60] p-4"
                onClick={() => setShowRoleModal(false)}
            >
                <motion.div
                    initial={{ scale: 0.9, y: 20 }}
                    animate={{ scale: 1, y: 0 }}
                    exit={{ scale: 0.9, y: 20 }}
                    onClick={(e) => e.stopPropagation()}
                    className="bg-gray-800 border border-white/10 rounded-xl max-w-4xl w-full max-h-[90vh] overflow-hidden flex flex-col"
                >
                    <div className="flex items-center justify-between p-6 border-b border-white/10 shrink-0">
                        <h3 className="text-2xl font-bold text-white">{editingRoleCode ? '编辑角色' : '创建新角色'}</h3>
                        <button onClick={() => setShowRoleModal(false)} className="p-2 hover:bg-white/10 rounded-lg transition-colors">
                            <X className="w-5 h-5 text-gray-400" />
                        </button>
                    </div>

                    <form onSubmit={handleSubmit} className="p-6 overflow-y-auto flex-1 space-y-6">
                        {/* Basic Info */}
                        <div className="grid grid-cols-2 gap-4">
                            <div>
                                <label className="block text-sm font-medium text-gray-300 mb-2">角色名称 *</label>
                                <input
                                    type="text"
                                    required
                                    value={roleForm.name}
                                    onChange={(e) => setRoleForm({ ...roleForm, name: e.target.value })}
                                    className="w-full px-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500 transition-colors"
                                    placeholder="例如：财务专员"
                                />
                            </div>
                            <div>
                                <label className="block text-sm font-medium text-gray-300 mb-2">角色代码 (英文唯一标识) *</label>
                                <input
                                    type="text"
                                    required
                                    disabled={!!editingRoleCode} 
                                    value={roleForm.code}
                                    onChange={(e) => setRoleForm({ ...roleForm, code: e.target.value })}
                                    className={`w-full px-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500 transition-colors ${editingRoleCode ? 'opacity-50 cursor-not-allowed' : ''}`}
                                    placeholder="例如：finance_staff"
                                />
                            </div>
                        </div>

                        <div>
                            <label className="block text-sm font-medium text-gray-300 mb-2">描述</label>
                            <input
                                type="text"
                                value={roleForm.description}
                                onChange={(e) => setRoleForm({ ...roleForm, description: e.target.value })}
                                className="w-full px-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500 transition-colors"
                                placeholder="角色职责描述"
                            />
                        </div>

                         {/* Data Scope */}
                         <div>
                            <label className="block text-sm font-medium text-gray-300 mb-3">数据范围权限</label>
                            <div className="flex gap-4">
                                <label className={`flex-1 p-4 rounded-lg border cursor-pointer transition-colors ${roleForm.dataScope === 'ALL' ? 'bg-blue-500/20 border-blue-500' : 'bg-white/5 border-white/10 hover:bg-white/10'}`}>
                                    <input 
                                        type="radio" 
                                        name="dataScope" 
                                        value="ALL"
                                        checked={roleForm.dataScope === 'ALL'}
                                        onChange={() => setRoleForm({...roleForm, dataScope: 'ALL'})}
                                        className="hidden" 
                                    />
                                    <div className="flex items-center gap-2 mb-1">
                                        <div className={`w-4 h-4 rounded-full border flex items-center justify-center ${roleForm.dataScope === 'ALL' ? 'border-blue-400' : 'border-gray-500'}`}>
                                            {roleForm.dataScope === 'ALL' && <div className="w-2 h-2 rounded-full bg-blue-400" />}
                                        </div>
                                        <span className={`font-semibold ${roleForm.dataScope === 'ALL' ? 'text-blue-400' : 'text-gray-300'}`}>全部数据权限</span>
                                    </div>
                                    <p className="text-xs text-gray-400 pl-6">
                                        可查看租户下所有区域和设备，无需单独分配。
                                    </p>
                                </label>

                                <label className={`flex-1 p-4 rounded-lg border cursor-pointer transition-colors ${roleForm.dataScope === 'CUSTOM' ? 'bg-blue-500/20 border-blue-500' : 'bg-white/5 border-white/10 hover:bg-white/10'}`}>
                                    <input 
                                        type="radio" 
                                        name="dataScope" 
                                        value="CUSTOM"
                                        checked={roleForm.dataScope === 'CUSTOM'}
                                        onChange={() => setRoleForm({...roleForm, dataScope: 'CUSTOM'})}
                                        className="hidden" 
                                    />
                                    <div className="flex items-center gap-2 mb-1">
                                        <div className={`w-4 h-4 rounded-full border flex items-center justify-center ${roleForm.dataScope === 'CUSTOM' ? 'border-blue-400' : 'border-gray-500'}`}>
                                            {roleForm.dataScope === 'CUSTOM' && <div className="w-2 h-2 rounded-full bg-blue-400" />}
                                        </div>
                                        <span className={`font-semibold ${roleForm.dataScope === 'CUSTOM' ? 'text-blue-400' : 'text-gray-300'}`}>自定义数据权限</span>
                                    </div>
                                    <p className="text-xs text-gray-400 pl-6">
                                        仅可查看分配给用户的特定区域及其数据。
                                    </p>
                                </label>
                            </div>
                        </div>

                        {/* Permissions Matrix */}
                        <div>
                            <label className="block text-sm font-medium text-gray-300 mb-3">功能操作权限</label>
                            <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
                                {PERMISSION_GROUPS.map((group) => (
                                    <div key={group.name} className="p-4 bg-white/5 rounded-lg border border-white/10">
                                        <h5 className="font-semibold text-gray-200 mb-3 pb-2 border-b border-white/10">
                                            {group.name}
                                        </h5>
                                        <div className="space-y-2">
                                            {group.permissions.map((perm) => {
                                                const isChecked = roleForm.permissions.includes(perm.code);
                                                return (
                                                    <label key={perm.code} className="flex items-center gap-3 cursor-pointer group">
                                                        <div 
                                                            className={`w-4 h-4 rounded border flex items-center justify-center transition-colors ${
                                                                isChecked ? 'bg-blue-500 border-blue-500' : 'border-gray-500 group-hover:border-gray-400'
                                                            }`}
                                                            onClick={(e) => {
                                                                e.preventDefault();
                                                                togglePermission(perm.code);
                                                            }}
                                                        >
                                                            {isChecked && <Check className="w-3 h-3 text-white" />}
                                                        </div>
                                                        <span className={`text-sm ${isChecked ? 'text-white' : 'text-gray-400 group-hover:text-gray-300'}`}>
                                                            {perm.name}
                                                        </span>
                                                    </label>
                                                );
                                            })}
                                        </div>
                                    </div>
                                ))}
                            </div>
                        </div>

                        <div className="flex gap-3 pt-4 border-t border-white/10 mt-6">
                            <button type="button" onClick={() => setShowRoleModal(false)} className="flex-1 px-6 py-3 bg-white/5 hover:bg-white/10 border border-white/10 rounded-lg transition-colors">
                                取消
                            </button>
                            <button type="submit" className="flex-1 px-6 py-3 bg-gradient-to-r from-blue-500 to-purple-500 hover:from-blue-600 hover:to-purple-600 rounded-lg transition-all font-semibold">
                                {editingRoleCode ? '保存更改' : '创建角色'}
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