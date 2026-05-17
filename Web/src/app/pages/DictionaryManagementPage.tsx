import { useState, useEffect, useCallback } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import { 
  Plus, Edit2, Trash2, Search, 
  Cpu, Archive, Activity, Tag, 
  FileText, AlertTriangle, Bell, Radio,
  LucideIcon, Settings, Loader2, RefreshCw
} from 'lucide-react';
import { 
  useDictionary, 
  DictionaryItem, 
  DictionaryTypeValue,
  DictionaryTypeConfig,
  getDictionaryTypeConfig,
  DictionaryType
} from '@/app/contexts/DictionaryContext';
import { useTheme } from '@/app/contexts/ThemeContext';
import * as dictionaryApi from '@/app/services/api/dictionaryApi';
import { DictionaryTypeDto, DictionaryItemDto } from '@/app/services/api/types/dictionary.types';

// 图标映射
const iconMap: Record<string, LucideIcon> = {
  Cpu,
  Archive,
  Activity,
  Tag,
  FileText,
  AlertTriangle,
  Bell,
  Radio,
  Settings,
};

// 颜色映射
const colorMap: Record<string, { border: string; text: string; bg: string; hover: string }> = {
  blue: { border: 'border-blue-500', text: 'text-blue-400', bg: 'bg-blue-900/20', hover: 'hover:bg-blue-900/30' },
  purple: { border: 'border-purple-500', text: 'text-purple-400', bg: 'bg-purple-900/20', hover: 'hover:bg-purple-900/30' },
  green: { border: 'border-green-500', text: 'text-green-400', bg: 'bg-green-900/20', hover: 'hover:bg-green-900/30' },
  orange: { border: 'border-orange-500', text: 'text-orange-400', bg: 'bg-orange-900/20', hover: 'hover:bg-orange-900/30' },
  cyan: { border: 'border-cyan-500', text: 'text-cyan-400', bg: 'bg-cyan-900/20', hover: 'hover:bg-cyan-900/30' },
  red: { border: 'border-red-500', text: 'text-red-400', bg: 'bg-red-900/20', hover: 'hover:bg-red-900/30' },
  yellow: { border: 'border-yellow-500', text: 'text-yellow-400', bg: 'bg-yellow-900/20', hover: 'hover:bg-yellow-900/30' },
  indigo: { border: 'border-indigo-500', text: 'text-indigo-400', bg: 'bg-indigo-900/20', hover: 'hover:bg-indigo-900/30' },
};

// 默认图标映射 (根据字典类型名称)
const defaultIconMap: Record<string, string> = {
  DeviceType: 'Cpu',
  DeviceStatus: 'Activity',
  AlertLevel: 'Bell',
  WorkOrderStatus: 'FileText',
  ArchiveCategory: 'Archive',
  device_category: 'Cpu',
  device_status: 'Activity',
  alert_level: 'Bell',
  work_order_status: 'FileText',
  archive_category: 'Archive',
};

export function DictionaryManagementPage() {
  const { themeConfig } = useTheme();
  const { getAllItems, getItemsByType, addItem, updateItem, deleteItem, typeConfigs, addTypeConfig, updateTypeConfig, loadFromBackend } = useDictionary();
  
  // 状态管理
  const [activeTab, setActiveTab] = useState<DictionaryTypeValue | null>(null); // 初始为null，表示未选择
  const [searchQuery, setSearchQuery] = useState('');
  const [isAddModalOpen, setIsAddModalOpen] = useState(false);
  const [isEditModalOpen, setIsEditModalOpen] = useState(false);
  const [selectedItem, setSelectedItem] = useState<DictionaryItem | null>(null);
  
  // 字典类型管理状态
  const [isAddTypeModalOpen, setIsAddTypeModalOpen] = useState(false);
  const [isEditTypeModalOpen, setIsEditTypeModalOpen] = useState(false);
  const [selectedTypeConfig, setSelectedTypeConfig] = useState<DictionaryTypeConfig | null>(null);
  const [typeFormData, setTypeFormData] = useState({
    key: '' as DictionaryTypeValue,
    name: '',
    icon: 'Settings',
    description: '',
    color: 'blue',
  });
  
  // 是否已加载过字典项（用于显示空状态提示）
  const [hasLoadedOnce, setHasLoadedOnce] = useState(false);
  
  const [formData, setFormData] = useState({
    code: '',
    name: '',
    sort: 1,
    description: '',
    status: 'active' as 'active' | 'inactive',
  });

  // API 数据状态
  const [dictionaryTypes, setDictionaryTypes] = useState<DictionaryTypeDto[]>([]);
  const [dictionaryItems, setDictionaryItems] = useState<DictionaryItemDto[]>([]);
  const [loading, setLoading] = useState(false);
  const [loadingItems, setLoadingItems] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // 加载字典类型列表
  const loadDictionaryTypes = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const response = await dictionaryApi.getDictionaryTypes({ page: 1, pageSize: 100 });
      if (response.data?.code === 200 && response.data?.data) {
        setDictionaryTypes(response.data?.data.items);
        // 初始不自动选择第一个类型，等待用户点击
      } else {
        setError(response.data?.message || '加载字典类型失败');
      }
    } catch (err: any) {
      setError(err?.message || '加载字典类型失败');
      console.error('加载字典类型失败:', err);
    } finally {
      setLoading(false);
    }
  }, []);

  // 加载指定类型的字典项
  const loadDictionaryItems = useCallback(async (type: string) => {
    if (!type) return;
    setLoadingItems(true);
    setHasLoadedOnce(true); // 标记已加载过一次
    try {
      const response = await dictionaryApi.getDictionaryItems({ type });
      if (response.data?.code === 200 && response.data?.data) {
        setDictionaryItems(response.data?.data);
      }
    } catch (err) {
      console.error('加载字典项失败:', err);
    } finally {
      setLoadingItems(false);
    }
  }, []);

  // 初始化加载
  useEffect(() => {
    loadDictionaryTypes();
    loadFromBackend();
  }, []);

  // 当切换类型时加载字典项
  useEffect(() => {
    if (activeTab) {
      loadDictionaryItems(activeTab);
    }
  }, [activeTab, loadDictionaryItems]);

  // 处理类型选择
  const handleTypeSelect = (typeCode: string) => {
    setActiveTab(typeCode as DictionaryTypeValue);
    setSearchQuery(''); // 切换类型时清空搜索
  };

  // 获取当前配置
  const currentConfig = getDictionaryTypeConfig(activeTab);
  const currentColor = currentConfig ? colorMap[currentConfig.color] : colorMap.blue;
  const IconComponent = currentConfig ? iconMap[currentConfig.icon] : Cpu;

  // 获取所有字典项用于计数 (合并本地和API数据)
  const allItems = getAllItems();
  
  // 获取当前Tab的字典项 (使用API数据)
  const currentItems = dictionaryItems
    .filter(item =>
      item.name.toLowerCase().includes(searchQuery.toLowerCase()) ||
      item.code.toLowerCase().includes(searchQuery.toLowerCase())
    )
    .sort((a, b) => a.sort - b.sort);

  // 打开添加弹窗
  const handleOpenAdd = () => {
    setFormData({
      code: '',
      name: '',
      sort: dictionaryItems.length + 1,
      description: '',
      status: 'active',
    });
    setIsAddModalOpen(true);
  };

  // 打开编辑弹窗
  const handleOpenEdit = (item: DictionaryItemDto) => {
    setSelectedItem(item as any);
    setFormData({
      code: item.code,
      name: item.name,
      sort: item.sort,
      description: item.description || '',
      status: item.status === 'active' ? 'active' : 'inactive',
    });
    setIsEditModalOpen(true);
  };

  // 删除
  const handleDelete = async (item: DictionaryItemDto) => {
    if (!confirm(`确定要删除"${item.name}"吗？`)) return;
    
    try {
      const response = await dictionaryApi.deleteDictionaryItem(item.id);
      if (response.data?.code === 200) {
        loadDictionaryItems(activeTab);
        loadFromBackend();
      } else {
        alert(response.data?.message || '删除失败');
      }
    } catch (err) {
      console.error('删除失败:', err);
      alert('删除失败，请稍后重试');
    }
  };

  // 打开添加字典类型弹窗
  const handleOpenAddType = () => {
    setTypeFormData({
      key: '' as DictionaryTypeValue,
      name: '',
      icon: 'Settings',
      description: '',
      color: 'blue',
    });
    setIsAddTypeModalOpen(true);
  };

  // 打开编辑字典类型弹窗
  const handleOpenEditType = (typeCode: string) => {
    const type = dictionaryTypes.find(t => t.code === typeCode);
    if (!type) return;
    
    setSelectedTypeConfig({ 
      key: type.code as DictionaryTypeValue,
      name: type.name,
      icon: defaultIconMap[type.code] || 'Settings',
      description: type.description || '',
      color: 'blue',
    });
    setTypeFormData({
      key: type.code as DictionaryTypeValue,
      name: type.name,
      icon: defaultIconMap[type.code] || 'Settings',
      description: type.description || '',
      color: 'blue',
    });
    setIsEditTypeModalOpen(true);
  };

  // 保存新增字典类型
  const handleAddType = async () => {
    if (!typeFormData.name.trim() || !typeFormData.key.trim()) {
      alert('请填写字典类型Key和名称');
      return;
    }

    try {
      const response = await dictionaryApi.createDictionaryType({
        code: typeFormData.key.trim(),
        name: typeFormData.name.trim(),
        description: typeFormData.description.trim(),
        isActive: true,
      });

      if (response.data?.code === 200) {
        loadDictionaryTypes();
        setActiveTab(typeFormData.key);
        setIsAddTypeModalOpen(false);
      } else {
        alert(response.data?.message || '创建失败');
      }
    } catch (err) {
      console.error('创建字典类型失败:', err);
      alert('创建失败，请稍后重试');
    }
  };

  // 保存编辑字典类型
  const handleEditType = async () => {
    if (!selectedTypeConfig) return;
    if (!typeFormData.name.trim()) {
      alert('请填写字典类型名称');
      return;
    }

    try {
      const type = dictionaryTypes.find(t => t.code === selectedTypeConfig.key);
      if (!type) return;

      const response = await dictionaryApi.updateDictionaryType(type.id, {
        name: typeFormData.name.trim(),
        description: typeFormData.description.trim(),
      });

      if (response.data?.code === 200) {
        loadDictionaryTypes();
        setIsEditTypeModalOpen(false);
        setSelectedTypeConfig(null);
      } else {
        alert(response.data?.message || '更新失败');
      }
    } catch (err) {
      console.error('更新字典类型失败:', err);
      alert('更新失败，请稍后重试');
    }
  };

  // 保存新增字典项
  const handleAdd = async () => {
    if (!formData.name.trim() || !formData.code.trim()) {
      alert('请填写字典编码和名称');
      return;
    }

    try {
      const response = await dictionaryApi.createDictionaryItem({
        type: activeTab,
        code: formData.code.trim(),
        name: formData.name.trim(),
        sort: formData.sort,
        description: formData.description.trim(),
        status: formData.status,
      });

      if (response.data?.code === 200) {
        loadDictionaryItems(activeTab);
        loadFromBackend();
        setIsAddModalOpen(false);
      } else {
        alert(response.data?.message || '创建失败');
      }
    } catch (err) {
      console.error('创建字典项失败:', err);
      alert('创建失败，请稍后重试');
    }
  };

  // 保存编辑字典项
  const handleEdit = async () => {
    if (!selectedItem) return;
    if (!formData.name.trim() || !formData.code.trim()) {
      alert('请填写字典编码和名称');
      return;
    }

    try {
      const response = await dictionaryApi.updateDictionaryItem(selectedItem.id as number, {
        code: formData.code.trim(),
        name: formData.name.trim(),
        sort: formData.sort,
        description: formData.description.trim(),
        status: formData.status,
      });

      if (response.data?.code === 200) {
        loadDictionaryItems(activeTab);
        loadFromBackend();
        setIsEditModalOpen(false);
        setSelectedItem(null);
      } else {
        alert(response.data?.message || '更新失败');
      }
    } catch (err) {
      console.error('更新字典项失败:', err);
      alert('更新失败，请稍后重试');
    }
  };

  // 刷新数据
  const handleRefresh = () => {
    loadDictionaryTypes();
    if (activeTab) {
      loadDictionaryItems(activeTab);
    }
  };

  return (
    <div className="p-6 h-full flex flex-col">
      {/* 页面标题 */}
      <div className="flex items-center justify-between mb-6">
        <div>
          <h1 className="text-3xl font-bold text-white mb-2">
            字典管理
          </h1>
          <p className="text-sm mt-1 text-gray-400">
            统一管理系统中的所有字典数据，支持灵活扩展
          </p>
        </div>
        <div className="flex items-center gap-3">
          <button
            onClick={handleRefresh}
            className="flex items-center gap-2 px-4 py-2 bg-white/5 hover:bg-white/10 border border-white/10 rounded-lg transition-all text-gray-300"
          >
            <RefreshCw className="w-4 h-4" />
            刷新
          </button>
        </div>
      </div>

      {/* 主体：左右布局 */}
      <div className="flex-1 flex gap-6 overflow-hidden">
        {/* 左侧：字典类型列表 */}
        <div className="w-80 flex-shrink-0 bg-white/5 backdrop-blur-xl border border-white/10 rounded-xl p-4 overflow-y-auto">
          <div className="flex items-center justify-between mb-3 px-2">
            <h3 className="text-sm font-semibold text-gray-400 uppercase tracking-wider">
              字典类型
            </h3>
            <button
              onClick={handleOpenAddType}
              className="flex items-center gap-1 px-2 py-1 text-xs bg-blue-900/20 text-blue-400 rounded hover:bg-blue-900/30 transition-colors border border-blue-500/30"
              title="添加字典类型"
            >
              <Plus className="w-3 h-3" />
              新增
            </button>
          </div>
          
          {loading ? (
            <div className="flex items-center justify-center py-8">
              <Loader2 className="w-6 h-6 text-gray-400 animate-spin" />
            </div>
          ) : error ? (
            <div className="text-center py-8 text-red-400 text-sm">
              {error}
              <button 
                onClick={loadDictionaryTypes}
                className="block mx-auto mt-2 text-blue-400 hover:underline"
              >
                重试
              </button>
            </div>
          ) : (
            <div className="space-y-2">
              {dictionaryTypes.map((type) => {
                const Icon = iconMap[defaultIconMap[type.code]] || Settings;
                const colors = colorMap.blue;
                const isActive = activeTab === type.code;
                const itemCount = dictionaryItems.filter(item => item.type === type.code).length;
                
                return (
                  <div key={type.id} className="relative group">
                    <button
                      onClick={() => handleTypeSelect(type.code)}
                      className={`w-full text-left px-4 py-3 rounded-lg transition-all ${
                        isActive
                          ? `${colors.bg} ${colors.border} border-2`
                          : 'border-2 border-transparent hover:bg-white/5'
                      }`}
                    >
                      <div className="flex items-center gap-3">
                        <div className={`p-2 rounded-lg ${isActive ? colors.bg : 'bg-white/5'}`}>
                          <Icon className={`w-5 h-5 ${isActive ? colors.text : 'text-gray-400'}`} />
                        </div>
                        <div className="flex-1 min-w-0">
                          <div className={`font-medium truncate ${isActive ? colors.text : 'text-gray-300'}`}>
                            {type.name}
                          </div>
                          <div className="text-xs text-gray-500 mt-0.5">
                            {itemCount} 项数据
                          </div>
                        </div>
                        {isActive && (
                          <motion.div
                            layoutId="activeIndicator"
                            className="w-1.5 h-8 rounded-full bg-blue-500"
                          />
                        )}
                      </div>
                      {isActive && type.description && (
                        <div className="mt-2 pt-2 border-t border-white/10">
                          <p className="text-xs text-gray-400 leading-relaxed">
                            {type.description}
                          </p>
                        </div>
                      )}
                    </button>
                    {/* 编辑按钮 - 在hover时显示 */}
                    <button
                      onClick={(e) => {
                        e.stopPropagation();
                        handleOpenEditType(type.code);
                      }}
                      className="absolute top-2 right-2 p-1.5 bg-gray-800/90 text-gray-300 rounded opacity-0 group-hover:opacity-100 transition-opacity hover:text-blue-400 hover:bg-gray-700"
                      title="编辑字典类型"
                    >
                      <Edit2 className="w-3 h-3" />
                    </button>
                  </div>
                );
              })}
            </div>
          )}
        </div>

        {/* 右侧：数据内容区 */}
        <div className="flex-1 flex flex-col overflow-hidden">
          {activeTab ? (
            <>
              {/* 当前类型标题和操作栏 */}
              <div className="mb-4">
                <div className="flex items-center justify-between mb-4">
                  <div className="flex items-center gap-3">
                    <IconComponent className={`w-6 h-6 ${currentColor.text}`} />
                    <div>
                      <h2 className="text-xl font-bold text-white">
                        {currentConfig?.name || (dictionaryTypes.find(t => t.code === activeTab)?.name) || '选择类型'}
                      </h2>
                      <p className="text-sm text-gray-400 mt-0.5">
                        {currentConfig?.description || dictionaryTypes.find(t => t.code === activeTab)?.description || ''}
                      </p>
                    </div>
                  </div>
                  <button
                    onClick={handleOpenAdd}
                    className={`flex items-center gap-2 px-4 py-2 rounded-lg transition-all ${currentColor.bg} ${currentColor.text} ${currentColor.hover} border ${currentColor.border}`}
                  >
                    <Plus className="w-4 h-4" />
                    添加{currentConfig?.name || '字典项'}
                  </button>
                </div>

                {/* 搜索框 */}
                <div className="relative">
                  <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-400" />
                  <input
                    type="text"
                    placeholder="搜索字典编码或名称..."
                    value={searchQuery}
                    onChange={(e) => setSearchQuery(e.target.value)}
                    className="w-full pl-10 pr-4 py-2 rounded-lg border border-white/10 bg-white/5 text-white placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent"
                  />
                </div>
              </div>

              {/* 字典列表 */}
              <div className="flex-1 rounded-lg border border-white/10 overflow-hidden bg-white/5 backdrop-blur-xl">
                <div className="overflow-auto h-full">
                  <table className="w-full">
                    <thead className="bg-white/5 sticky top-0 z-10">
                      <tr>
                        <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-400">
                          字典编码
                        </th>
                        <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-400">
                          字典名称
                        </th>
                        <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-400">
                          排序
                        </th>
                        <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-400">
                          描述
                        </th>
                        <th className="px-6 py-3 text-left text-xs font-medium uppercase tracking-wider text-gray-400">
                          状态
                        </th>
                        <th className="px-6 py-3 text-right text-xs font-medium uppercase tracking-wider text-gray-400">
                          操作
                        </th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-white/10">
                      {loadingItems ? (
                        <tr>
                          <td colSpan={6} className="px-6 py-8 text-center">
                            <Loader2 className="w-6 h-6 text-gray-400 animate-spin mx-auto" />
                          </td>
                        </tr>
                      ) : currentItems.length === 0 ? (
                        <tr>
                          <td colSpan={6} className="px-6 py-8 text-center text-gray-400">
                            {searchQuery ? '没有找到匹配的字典项' : `暂无${currentConfig?.name || '字典项'}数据`}
                          </td>
                        </tr>
                      ) : (
                        currentItems.map((item) => (
                          <motion.tr
                            key={item.id}
                            initial={{ opacity: 0 }}
                            animate={{ opacity: 1 }}
                            className="hover:bg-white/5 transition-colors"
                          >
                            <td className="px-6 py-4 whitespace-nowrap">
                              <code className={`px-2 py-1 rounded text-xs ${currentColor.bg} ${currentColor.text}`}>
                                {item.code}
                              </code>
                            </td>
                            <td className="px-6 py-4 whitespace-nowrap font-medium text-white">
                              {item.name}
                            </td>
                            <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-400">
                              {item.sort}
                            </td>
                            <td className="px-6 py-4 text-sm text-gray-400">
                              {item.description || '-'}
                            </td>
                            <td className="px-6 py-4 whitespace-nowrap">
                              <span
                                className={`px-2 py-1 text-xs rounded-full ${
                                  item.status === 'active'
                                    ? 'bg-green-900/30 text-green-400'
                                    : 'bg-gray-800 text-gray-400'
                                }`}
                              >
                                {item.status === 'active' ? '启用' : '禁用'}
                              </span>
                            </td>
                            <td className="px-6 py-4 whitespace-nowrap text-right text-sm space-x-2">
                              <button
                                onClick={() => handleOpenEdit(item)}
                                className="inline-flex items-center gap-1 px-3 py-1 text-blue-400 hover:bg-blue-900/20 rounded transition-colors"
                              >
                                <Edit2 className="w-3 h-3" />
                                编辑
                              </button>
                              <button
                                onClick={() => handleDelete(item)}
                                className="inline-flex items-center gap-1 px-3 py-1 text-red-400 hover:bg-red-900/20 rounded transition-colors"
                              >
                                <Trash2 className="w-3 h-3" />
                                删除
                              </button>
                            </td>
                          </motion.tr>
                        ))
                      )}
                    </tbody>
                  </table>
                </div>
              </div>
            </>
          ) : (
            /* 未选择类型时的提示 */
            <div className="flex-1 flex flex-col items-center justify-center rounded-lg border border-white/10 bg-white/5 backdrop-blur-xl">
              <div className="text-center">
                <div className="w-20 h-20 mx-auto mb-6 rounded-full bg-blue-900/20 flex items-center justify-center">
                  <Tag className="w-10 h-10 text-blue-400" />
                </div>
                <h3 className="text-xl font-medium text-white mb-2">
                  请选择左侧字典类型
                </h3>
                <p className="text-gray-400 max-w-sm">
                  点击左侧的字典类型，如"设备类型"、"档案分类"等，即可查看对应的字典数据
                </p>
              </div>
            </div>
          )}
        </div>
      </div>

      {/* 添加弹窗 */}
      <AnimatePresence>
        {isAddModalOpen && (
          <>
            <motion.div
              initial={{ opacity: 0 }}
              animate={{ opacity: 1 }}
              exit={{ opacity: 0 }}
              className="fixed inset-0 bg-black/50 backdrop-blur-sm z-40"
              onClick={() => setIsAddModalOpen(false)}
            />
            <motion.div
              initial={{ opacity: 0, scale: 0.95 }}
              animate={{ opacity: 1, scale: 1 }}
              exit={{ opacity: 0, scale: 0.95 }}
              className="fixed left-1/2 top-1/2 -translate-x-1/2 -translate-y-1/2 w-full max-w-md bg-gray-900 border border-white/10 rounded-xl shadow-2xl z-50 p-6"
            >
              <h2 className="text-xl font-bold mb-4 text-white flex items-center gap-2">
                <IconComponent className={`w-5 h-5 ${currentColor.text}`} />
                添加{currentConfig?.name}
              </h2>
              <div className="space-y-4">
                <div>
                  <label className="block text-sm font-medium mb-1 text-gray-300">
                    字典编码 <span className="text-red-500">*</span>
                  </label>
                  <input
                    type="text"
                    value={formData.code}
                    onChange={(e) => setFormData({ ...formData, code: e.target.value })}
                    className="w-full px-3 py-2 rounded-lg border border-white/10 bg-white/5 text-white placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent"
                    placeholder="例如: refrigeration"
                  />
                </div>
                <div>
                  <label className="block text-sm font-medium mb-1 text-gray-300">
                    字典名称 <span className="text-red-500">*</span>
                  </label>
                  <input
                    type="text"
                    value={formData.name}
                    onChange={(e) => setFormData({ ...formData, name: e.target.value })}
                    className="w-full px-3 py-2 rounded-lg border border-white/10 bg-white/5 text-white placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent"
                    placeholder="例如: 制冷设备"
                  />
                </div>
                <div>
                  <label className="block text-sm font-medium mb-1 text-gray-300">
                    排序
                  </label>
                  <input
                    type="number"
                    value={formData.sort}
                    onChange={(e) => setFormData({ ...formData, sort: parseInt(e.target.value) || 1 })}
                    className="w-full px-3 py-2 rounded-lg border border-white/10 bg-white/5 text-white placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent"
                    min="1"
                  />
                </div>
                <div>
                  <label className="block text-sm font-medium mb-1 text-gray-300">
                    描述
                  </label>
                  <textarea
                    value={formData.description}
                    onChange={(e) => setFormData({ ...formData, description: e.target.value })}
                    className="w-full px-3 py-2 rounded-lg border border-white/10 bg-white/5 text-white placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent resize-none"
                    rows={3}
                    placeholder="请输入描述信息"
                  />
                </div>
                <div>
                  <label className="block text-sm font-medium mb-1 text-gray-300">
                    状态
                  </label>
                  <select
                    value={formData.status}
                    onChange={(e) => setFormData({ ...formData, status: e.target.value as 'active' | 'inactive' })}
                    className="w-full px-3 py-2 rounded-lg border border-white/10 bg-gray-800 text-white focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent"
                  >
                    <option value="active" className="bg-gray-800 text-white">启用</option>
                    <option value="inactive" className="bg-gray-800 text-white">禁用</option>
                  </select>
                </div>
              </div>
              <div className="flex gap-3 mt-6">
                <button
                  onClick={() => setIsAddModalOpen(false)}
                  className="flex-1 px-4 py-2 border border-white/10 text-gray-300 rounded-lg hover:bg-white/5 transition-colors"
                >
                  取消
                </button>
                <button
                  onClick={handleAdd}
                  className={`flex-1 px-4 py-2 rounded-lg transition-all ${currentColor.bg} ${currentColor.text} ${currentColor.hover} border ${currentColor.border}`}
                >
                  确定
                </button>
              </div>
            </motion.div>
          </>
        )}
      </AnimatePresence>

      {/* 编辑弹窗 */}
      <AnimatePresence>
        {isEditModalOpen && (
          <>
            <motion.div
              initial={{ opacity: 0 }}
              animate={{ opacity: 1 }}
              exit={{ opacity: 0 }}
              className="fixed inset-0 bg-black/50 backdrop-blur-sm z-40"
              onClick={() => setIsEditModalOpen(false)}
            />
            <motion.div
              initial={{ opacity: 0, scale: 0.95 }}
              animate={{ opacity: 1, scale: 1 }}
              exit={{ opacity: 0, scale: 0.95 }}
              className="fixed left-1/2 top-1/2 -translate-x-1/2 -translate-y-1/2 w-full max-w-md bg-gray-900 border border-white/10 rounded-xl shadow-2xl z-50 p-6"
            >
              <h2 className="text-xl font-bold mb-4 text-white flex items-center gap-2">
                <IconComponent className={`w-5 h-5 ${currentColor.text}`} />
                编辑{currentConfig?.name}
              </h2>
              <div className="space-y-4">
                <div>
                  <label className="block text-sm font-medium mb-1 text-gray-300">
                    字典编码 <span className="text-red-500">*</span>
                  </label>
                  <input
                    type="text"
                    value={formData.code}
                    onChange={(e) => setFormData({ ...formData, code: e.target.value })}
                    className="w-full px-3 py-2 rounded-lg border border-white/10 bg-white/5 text-white placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent"
                  />
                </div>
                <div>
                  <label className="block text-sm font-medium mb-1 text-gray-300">
                    字典名称 <span className="text-red-500">*</span>
                  </label>
                  <input
                    type="text"
                    value={formData.name}
                    onChange={(e) => setFormData({ ...formData, name: e.target.value })}
                    className="w-full px-3 py-2 rounded-lg border border-white/10 bg-white/5 text-white placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent"
                  />
                </div>
                <div>
                  <label className="block text-sm font-medium mb-1 text-gray-300">
                    排序
                  </label>
                  <input
                    type="number"
                    value={formData.sort}
                    onChange={(e) => setFormData({ ...formData, sort: parseInt(e.target.value) || 1 })}
                    className="w-full px-3 py-2 rounded-lg border border-white/10 bg-white/5 text-white placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent"
                    min="1"
                  />
                </div>
                <div>
                  <label className="block text-sm font-medium mb-1 text-gray-300">
                    描述
                  </label>
                  <textarea
                    value={formData.description}
                    onChange={(e) => setFormData({ ...formData, description: e.target.value })}
                    className="w-full px-3 py-2 rounded-lg border border-white/10 bg-white/5 text-white placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent resize-none"
                    rows={3}
                  />
                </div>
                <div>
                  <label className="block text-sm font-medium mb-1 text-gray-300">
                    状态
                  </label>
                  <select
                    value={formData.status}
                    onChange={(e) => setFormData({ ...formData, status: e.target.value as 'active' | 'inactive' })}
                    className="w-full px-3 py-2 rounded-lg border border-white/10 bg-gray-800 text-white focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent"
                  >
                    <option value="active" className="bg-gray-800 text-white">启用</option>
                    <option value="inactive" className="bg-gray-800 text-white">禁用</option>
                  </select>
                </div>
              </div>
              <div className="flex gap-3 mt-6">
                <button
                  onClick={() => setIsEditModalOpen(false)}
                  className="flex-1 px-4 py-2 border border-white/10 text-gray-300 rounded-lg hover:bg-white/5 transition-colors"
                >
                  取消
                </button>
                <button
                  onClick={handleEdit}
                  className={`flex-1 px-4 py-2 rounded-lg transition-all ${currentColor.bg} ${currentColor.text} ${currentColor.hover} border ${currentColor.border}`}
                >
                  保存
                </button>
              </div>
            </motion.div>
          </>
        )}
      </AnimatePresence>

      {/* 添加字典类型弹窗 */}
      <AnimatePresence>
        {isAddTypeModalOpen && (
          <>
            <motion.div
              initial={{ opacity: 0 }}
              animate={{ opacity: 1 }}
              exit={{ opacity: 0 }}
              className="fixed inset-0 bg-black/50 backdrop-blur-sm z-40"
              onClick={() => setIsAddTypeModalOpen(false)}
            />
            <motion.div
              initial={{ opacity: 0, scale: 0.95 }}
              animate={{ opacity: 1, scale: 1 }}
              exit={{ opacity: 0, scale: 0.95 }}
              className="fixed left-1/2 top-1/2 -translate-x-1/2 -translate-y-1/2 w-full max-w-md bg-gray-900 border border-white/10 rounded-xl shadow-2xl z-50 p-6"
            >
              <h2 className="text-xl font-bold mb-4 text-white flex items-center gap-2">
                <Plus className="w-5 h-5 text-blue-400" />
                添加字典类型
              </h2>
              <div className="space-y-4">
                <div>
                  <label className="block text-sm font-medium mb-1 text-gray-300">
                    类型Key <span className="text-red-500">*</span>
                  </label>
                  <input
                    type="text"
                    value={typeFormData.key}
                    onChange={(e) => setTypeFormData({ ...typeFormData, key: e.target.value as DictionaryTypeValue })}
                    className="w-full px-3 py-2 rounded-lg border border-white/10 bg-white/5 text-white placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent"
                    placeholder="例如: custom_type"
                  />
                </div>
                <div>
                  <label className="block text-sm font-medium mb-1 text-gray-300">
                    类型名称 <span className="text-red-500">*</span>
                  </label>
                  <input
                    type="text"
                    value={typeFormData.name}
                    onChange={(e) => setTypeFormData({ ...typeFormData, name: e.target.value })}
                    className="w-full px-3 py-2 rounded-lg border border-white/10 bg-white/5 text-white placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent"
                    placeholder="例如: 自定义类型"
                  />
                </div>
                <div>
                  <label className="block text-sm font-medium mb-1 text-gray-300">
                    图标
                  </label>
                  <select
                    value={typeFormData.icon}
                    onChange={(e) => setTypeFormData({ ...typeFormData, icon: e.target.value })}
                    className="w-full px-3 py-2 rounded-lg border border-white/10 bg-gray-800 text-white focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent"
                  >
                    <option value="Settings">Settings</option>
                    <option value="Cpu">Cpu</option>
                    <option value="Archive">Archive</option>
                    <option value="Activity">Activity</option>
                    <option value="Tag">Tag</option>
                    <option value="FileText">FileText</option>
                    <option value="AlertTriangle">AlertTriangle</option>
                    <option value="Bell">Bell</option>
                    <option value="Radio">Radio</option>
                  </select>
                </div>
                <div>
                  <label className="block text-sm font-medium mb-1 text-gray-300">
                    颜色主题
                  </label>
                  <select
                    value={typeFormData.color}
                    onChange={(e) => setTypeFormData({ ...typeFormData, color: e.target.value })}
                    className="w-full px-3 py-2 rounded-lg border border-white/10 bg-gray-800 text-white focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent"
                  >
                    <option value="blue">蓝色 (Blue)</option>
                    <option value="purple">紫色 (Purple)</option>
                    <option value="green">绿色 (Green)</option>
                    <option value="orange">橙色 (Orange)</option>
                    <option value="cyan">青色 (Cyan)</option>
                    <option value="red">红色 (Red)</option>
                    <option value="yellow">黄色 (Yellow)</option>
                    <option value="indigo">靛蓝色 (Indigo)</option>
                  </select>
                </div>
                <div>
                  <label className="block text-sm font-medium mb-1 text-gray-300">
                    描述
                  </label>
                  <textarea
                    value={typeFormData.description}
                    onChange={(e) => setTypeFormData({ ...typeFormData, description: e.target.value })}
                    className="w-full px-3 py-2 rounded-lg border border-white/10 bg-white/5 text-white placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent resize-none"
                    rows={3}
                    placeholder="请输入字典类型的描述信息"
                  />
                </div>
              </div>
              <div className="flex gap-3 mt-6">
                <button
                  onClick={() => setIsAddTypeModalOpen(false)}
                  className="flex-1 px-4 py-2 border border-white/10 text-gray-300 rounded-lg hover:bg-white/5 transition-colors"
                >
                  取消
                </button>
                <button
                  onClick={handleAddType}
                  className="flex-1 px-4 py-2 rounded-lg bg-blue-900/20 text-blue-400 hover:bg-blue-900/30 transition-colors border border-blue-500"
                >
                  确定
                </button>
              </div>
            </motion.div>
          </>
        )}
      </AnimatePresence>

      {/* 编辑字典类型弹窗 */}
      <AnimatePresence>
        {isEditTypeModalOpen && (
          <>
            <motion.div
              initial={{ opacity: 0 }}
              animate={{ opacity: 1 }}
              exit={{ opacity: 0 }}
              className="fixed inset-0 bg-black/50 backdrop-blur-sm z-40"
              onClick={() => setIsEditTypeModalOpen(false)}
            />
            <motion.div
              initial={{ opacity: 0, scale: 0.95 }}
              animate={{ opacity: 1, scale: 1 }}
              exit={{ opacity: 0, scale: 0.95 }}
              className="fixed left-1/2 top-1/2 -translate-x-1/2 -translate-y-1/2 w-full max-w-md bg-gray-900 border border-white/10 rounded-xl shadow-2xl z-50 p-6"
            >
              <h2 className="text-xl font-bold mb-4 text-white flex items-center gap-2">
                <Edit2 className="w-5 h-5 text-blue-400" />
                编辑字典类型
              </h2>
              <div className="space-y-4">
                <div>
                  <label className="block text-sm font-medium mb-1 text-gray-300">
                    类型Key
                  </label>
                  <input
                    type="text"
                    value={typeFormData.key}
                    disabled
                    className="w-full px-3 py-2 rounded-lg border border-white/10 bg-gray-800/50 text-gray-500 cursor-not-allowed"
                  />
                  <p className="text-xs text-gray-500 mt-1">类型Key不可修改</p>
                </div>
                <div>
                  <label className="block text-sm font-medium mb-1 text-gray-300">
                    类型名称 <span className="text-red-500">*</span>
                  </label>
                  <input
                    type="text"
                    value={typeFormData.name}
                    onChange={(e) => setTypeFormData({ ...typeFormData, name: e.target.value })}
                    className="w-full px-3 py-2 rounded-lg border border-white/10 bg-white/5 text-white placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent"
                  />
                </div>
                <div>
                  <label className="block text-sm font-medium mb-1 text-gray-300">
                    图标
                  </label>
                  <select
                    value={typeFormData.icon}
                    onChange={(e) => setTypeFormData({ ...typeFormData, icon: e.target.value })}
                    className="w-full px-3 py-2 rounded-lg border border-white/10 bg-gray-800 text-white focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent"
                  >
                    <option value="Settings">Settings</option>
                    <option value="Cpu">Cpu</option>
                    <option value="Archive">Archive</option>
                    <option value="Activity">Activity</option>
                    <option value="Tag">Tag</option>
                    <option value="FileText">FileText</option>
                    <option value="AlertTriangle">AlertTriangle</option>
                    <option value="Bell">Bell</option>
                    <option value="Radio">Radio</option>
                  </select>
                </div>
                <div>
                  <label className="block text-sm font-medium mb-1 text-gray-300">
                    颜色主题
                  </label>
                  <select
                    value={typeFormData.color}
                    onChange={(e) => setTypeFormData({ ...typeFormData, color: e.target.value })}
                    className="w-full px-3 py-2 rounded-lg border border-white/10 bg-gray-800 text-white focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent"
                  >
                    <option value="blue">蓝色 (Blue)</option>
                    <option value="purple">紫色 (Purple)</option>
                    <option value="green">绿色 (Green)</option>
                    <option value="orange">橙色 (Orange)</option>
                    <option value="cyan">青色 (Cyan)</option>
                    <option value="red">红色 (Red)</option>
                    <option value="yellow">黄色 (Yellow)</option>
                    <option value="indigo">靛蓝色 (Indigo)</option>
                  </select>
                </div>
                <div>
                  <label className="block text-sm font-medium mb-1 text-gray-300">
                    描述
                  </label>
                  <textarea
                    value={typeFormData.description}
                    onChange={(e) => setTypeFormData({ ...typeFormData, description: e.target.value })}
                    className="w-full px-3 py-2 rounded-lg border border-white/10 bg-white/5 text-white placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent resize-none"
                    rows={3}
                  />
                </div>
              </div>
              <div className="flex gap-3 mt-6">
                <button
                  onClick={() => setIsEditTypeModalOpen(false)}
                  className="flex-1 px-4 py-2 border border-white/10 text-gray-300 rounded-lg hover:bg-white/5 transition-colors"
                >
                  取消
                </button>
                <button
                  onClick={handleEditType}
                  className="flex-1 px-4 py-2 rounded-lg bg-blue-900/20 text-blue-400 hover:bg-blue-900/30 transition-colors border border-blue-500"
                >
                  保存
                </button>
              </div>
            </motion.div>
          </>
        )}
      </AnimatePresence>
    </div>
  );
}
