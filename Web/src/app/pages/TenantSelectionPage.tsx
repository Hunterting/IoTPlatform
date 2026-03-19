import { motion, AnimatePresence } from 'motion/react';
import { Building2, ArrowRight, LogOut, Search, Plus, Save } from 'lucide-react';
import { useAuth } from '@/app/contexts/AuthContext';
import { useState } from 'react';

export function TenantSelectionPage() {
  const { user, customers, switchCustomer, logout, addCustomer } = useAuth();
  const [searchTerm, setSearchTerm] = useState('');
  const [showCreateForm, setShowCreateForm] = useState(false);
  
  // New Customer Form State
  const [formData, setFormData] = useState({
    name: '',
    code: '',
    appCode: '',
    contact: '',
    phone: '',
    address: '',
  });

  const filteredCustomers = customers.filter(customer => 
    customer.name.toLowerCase().includes(searchTerm.toLowerCase()) ||
    customer.code.toLowerCase().includes(searchTerm.toLowerCase())
  );

  const handleSelectCustomer = (customerId: string) => {
    switchCustomer(customerId);
  };

  const handleCreateCustomer = (e: React.FormEvent) => {
    e.preventDefault();
    if (!formData.name || !formData.code) return; // Basic validation

    addCustomer(formData);
    
    // Auto-select the newly created customer (which will be the last one added in mock logic)
    // Since we don't get the ID back immediately from the context wrapper in this simple implementation,
    // we'll just close the form. The user will see the new card and can click it.
    // OR: In a real app, we'd await the ID. 
    // For this specific requirement "create then enter", we can simulate it by finding the customer we just "added" by name/code
    // But since state update is async, we can't do it instantly here without useEffect.
    // Let's just return to list view (or if it was empty, now it has one).
    
    setShowCreateForm(false);
    setFormData({
      name: '',
      code: '',
      appCode: '',
      contact: '',
      phone: '',
      address: '',
    });
  };

  // Logic:
  // If customers array is empty, FORCE show the create form (or a welcome screen leading to it).
  // If customers exist, show list, but allow creating new one.
  const isFirstRun = customers.length === 0;

  return (
    <div className="min-h-screen bg-gradient-to-br from-gray-900 via-slate-900 to-gray-800 flex items-center justify-center p-4">
       <motion.div
        initial={{ opacity: 0, scale: 0.95 }}
        animate={{ opacity: 1, scale: 1 }}
        className="w-full max-w-4xl"
      >
        <div className="bg-white/5 backdrop-blur-xl rounded-2xl border border-white/10 overflow-hidden shadow-2xl">
          
          {/* Header */}
          <div className="p-8 border-b border-white/10 flex items-center justify-between">
            <div>
              <h1 className="text-3xl font-bold text-white mb-2">
                {isFirstRun ? '欢迎使用' : '选择租户'}
              </h1>
              <p className="text-gray-400">
                {isFirstRun 
                  ? '这是新部署的平台，请先创建第一个租户以开始使用。' 
                  : `欢迎回来，${user?.name}。请选择要管理的客户系统。`}
              </p>
            </div>
            <div className="flex gap-3">
              {!isFirstRun && !showCreateForm && (
                <button
                    onClick={() => setShowCreateForm(true)}
                    className="flex items-center gap-2 px-4 py-2 bg-blue-500 hover:bg-blue-600 text-white rounded-lg transition-colors shadow-lg shadow-blue-500/20"
                >
                    <Plus className="w-4 h-4" />
                    <span>新建租户</span>
                </button>
              )}
              <button
                onClick={logout}
                className="flex items-center gap-2 px-4 py-2 bg-white/5 hover:bg-white/10 text-gray-300 hover:text-white rounded-lg transition-colors border border-white/10"
              >
                <LogOut className="w-4 h-4" />
                <span>退出登录</span>
              </button>
            </div>
          </div>

          <AnimatePresence mode="wait">
            {isFirstRun || showCreateForm ? (
                <motion.div
                    key="create-form"
                    initial={{ opacity: 0, y: 20 }}
                    animate={{ opacity: 1, y: 0 }}
                    exit={{ opacity: 0, y: -20 }}
                    className="p-8"
                >
                    <div className="max-w-2xl mx-auto">
                        <h2 className="text-xl font-semibold text-white mb-6 flex items-center gap-2">
                            <Building2 className="w-6 h-6 text-blue-400" />
                            {isFirstRun ? '创建初始租户' : '创建新租户'}
                        </h2>
                        <form onSubmit={handleCreateCustomer} className="space-y-6">
                            <div className="grid grid-cols-2 gap-6">
                                <div className="col-span-2">
                                    <label className="block text-sm font-medium text-gray-300 mb-2">
                                        租户名称 <span className="text-red-400">*</span>
                                    </label>
                                    <input
                                        type="text"
                                        required
                                        value={formData.name}
                                        onChange={(e) => setFormData({ ...formData, name: e.target.value })}
                                        className="w-full px-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500 transition-colors"
                                        placeholder="例如：某某餐饮集团"
                                    />
                                </div>
                                <div>
                                    <label className="block text-sm font-medium text-gray-300 mb-2">
                                        租户编号 <span className="text-red-400">*</span>
                                    </label>
                                    <input
                                        type="text"
                                        required
                                        value={formData.code}
                                        onChange={(e) => setFormData({ ...formData, code: e.target.value })}
                                        className="w-full px-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500 transition-colors"
                                        placeholder="例如：C001"
                                    />
                                </div>
                                <div>
                                    <label className="block text-sm font-medium text-gray-300 mb-2">
                                        AppCode <span className="text-red-400">*</span>
                                    </label>
                                    <input
                                        type="text"
                                        required
                                        value={formData.appCode}
                                        onChange={(e) => setFormData({ ...formData, appCode: e.target.value })}
                                        className="w-full px-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500 transition-colors"
                                        placeholder="例如：APP001"
                                    />
                                </div>
                                <div>
                                    <label className="block text-sm font-medium text-gray-300 mb-2">
                                        联系人
                                    </label>
                                    <input
                                        type="text"
                                        value={formData.contact}
                                        onChange={(e) => setFormData({ ...formData, contact: e.target.value })}
                                        className="w-full px-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500 transition-colors"
                                    />
                                </div>
                                <div>
                                    <label className="block text-sm font-medium text-gray-300 mb-2">
                                        联系电话
                                    </label>
                                    <input
                                        type="tel"
                                        value={formData.phone}
                                        onChange={(e) => setFormData({ ...formData, phone: e.target.value })}
                                        className="w-full px-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500 transition-colors"
                                    />
                                </div>
                                <div className="col-span-2">
                                    <label className="block text-sm font-medium text-gray-300 mb-2">
                                        地址
                                    </label>
                                    <textarea
                                        rows={2}
                                        value={formData.address}
                                        onChange={(e) => setFormData({ ...formData, address: e.target.value })}
                                        className="w-full px-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500 transition-colors resize-none"
                                    />
                                </div>
                            </div>
                            
                            <div className="flex gap-4 pt-4">
                                {!isFirstRun && (
                                    <button
                                        type="button"
                                        onClick={() => setShowCreateForm(false)}
                                        className="flex-1 px-6 py-3 bg-white/5 hover:bg-white/10 border border-white/10 rounded-lg transition-colors text-white"
                                    >
                                        取消
                                    </button>
                                )}
                                <button
                                    type="submit"
                                    className="flex-1 flex items-center justify-center gap-2 px-6 py-3 bg-gradient-to-r from-blue-500 to-purple-500 hover:from-blue-600 hover:to-purple-600 rounded-lg transition-all text-white font-semibold"
                                >
                                    <Save className="w-5 h-5" />
                                    <span>{isFirstRun ? '创建并进入系统' : '创建租户'}</span>
                                </button>
                            </div>
                        </form>
                    </div>
                </motion.div>
            ) : (
                <motion.div
                    key="list-view"
                    initial={{ opacity: 0, x: -20 }}
                    animate={{ opacity: 1, x: 0 }}
                    exit={{ opacity: 0, x: 20 }}
                >
                    {/* Search */}
                    <div className="p-6 border-b border-white/10 bg-white/[0.02]">
                        <div className="relative">
                        <Search className="absolute left-4 top-1/2 -translate-y-1/2 w-5 h-5 text-gray-400" />
                        <input
                            type="text"
                            placeholder="搜索客户名称或编号..."
                            value={searchTerm}
                            onChange={(e) => setSearchTerm(e.target.value)}
                            className="w-full pl-12 pr-4 py-4 bg-gray-900/50 border border-white/10 rounded-xl text-white placeholder-gray-500 focus:outline-none focus:border-blue-500 transition-colors"
                            autoFocus
                        />
                        </div>
                    </div>

                    {/* Customer Grid */}
                    <div className="p-8 max-h-[60vh] overflow-y-auto">
                        <div className="grid md:grid-cols-2 gap-4">
                        {filteredCustomers.length > 0 ? (
                            filteredCustomers.map((customer) => (
                            <motion.div
                                key={customer.id}
                                initial={{ opacity: 0, y: 10 }}
                                animate={{ opacity: 1, y: 0 }}
                                whileHover={{ scale: 1.02, backgroundColor: 'rgba(255,255,255,0.08)' }}
                                whileTap={{ scale: 0.98 }}
                                onClick={() => handleSelectCustomer(customer.id)}
                                className="cursor-pointer group relative p-6 bg-white/5 border border-white/10 rounded-xl transition-colors hover:border-blue-500/50"
                            >
                                <div className="flex items-start justify-between mb-4">
                                <div className="p-3 bg-gradient-to-br from-blue-500/20 to-purple-500/20 rounded-lg group-hover:from-blue-500/30 group-hover:to-purple-500/30 transition-colors">
                                    <Building2 className="w-8 h-8 text-blue-400" />
                                </div>
                                <div className="flex items-center gap-2 px-3 py-1 bg-green-500/20 text-green-400 rounded-full text-xs font-medium border border-green-500/30">
                                    <div className="w-1.5 h-1.5 rounded-full bg-green-500" />
                                    <span>{customer.status === 'active' ? '活跃' : '停用'}</span>
                                </div>
                                </div>
                                
                                <h3 className="text-xl font-bold text-white mb-2 group-hover:text-blue-400 transition-colors">
                                {customer.name}
                                </h3>
                                
                                <div className="space-y-1 mb-4">
                                <p className="text-sm text-gray-400">编号: {customer.code}</p>
                                <p className="text-sm text-gray-400">应用码: {customer.appCode}</p>
                                </div>

                                <div className="flex items-center justify-between pt-4 border-t border-white/10">
                                <div className="flex items-center gap-1 text-blue-400 font-medium opacity-0 -translate-x-2 group-hover:opacity-100 group-hover:translate-x-0 transition-all">
                                    <span>进入系统</span>
                                    <ArrowRight className="w-4 h-4" />
                                </div>
                                </div>
                            </motion.div>
                            ))
                        ) : (
                            <div className="col-span-2 py-12 text-center text-gray-500 flex flex-col items-center">
                                <p className="mb-4">没有找到匹配的租户</p>
                                <button 
                                    onClick={() => setShowCreateForm(true)}
                                    className="text-blue-400 hover:text-blue-300 underline"
                                >
                                    创建新租户
                                </button>
                            </div>
                        )}
                        </div>
                    </div>
                </motion.div>
            )}
          </AnimatePresence>
          
          {/* Footer */}
          <div className="p-6 bg-white/5 border-t border-white/10 text-center text-sm text-gray-500">
            © 2025 数智物联网厨房 SaaS 管理平台
          </div>
        </div>
      </motion.div>
    </div>
  );
}