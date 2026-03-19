import { useState, useEffect, useMemo } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import {
  Search,
  Filter,
  Plus,
  Edit,
  Trash2,
  Download,
  Upload,
  X,
  Power,
  CheckCircle,
  XCircle,
  AlertTriangle,
  Wifi,
  WifiOff,
} from 'lucide-react';
import { useDictionary, DictionaryType } from '@/app/contexts/DictionaryContext';
import { useAuth } from '@/app/contexts/AuthContext';
import { useArea, Area } from '@/app/contexts/AreaContext';
import { useDevices, DeviceItem } from '@/app/contexts/DeviceContext';
import { AreaTreeSelect } from '@/app/components/AreaTreeSelect';
import { PERMISSIONS } from '@/app/config/permissions';

interface DevicesPageProps {
  highlightDeviceId?: string | null;
  onClearHighlight?: () => void;
  initialAreaFilter?: string | null;
}

export function DevicesPage({ highlightDeviceId, onClearHighlight, initialAreaFilter }: DevicesPageProps = {}) {
  const { currentCustomer, hasPermission } = useAuth();
  const { getAreasByCustomerId } = useArea();
  const { devices, addDevice, updateDevice, deleteDevice } = useDevices();
  const [searchTerm, setSearchTerm] = useState('');
  const [filterStatus, setFilterStatus] = useState<string>('all');
  const [filterArea, setFilterArea] = useState<string>(initialAreaFilter || 'all');

  // Sync filterArea when initialAreaFilter prop changes (e.g., navigated from dashboard)
  useEffect(() => {
    if (initialAreaFilter) {
      setFilterArea(initialAreaFilter);
    }
  }, [initialAreaFilter]);

  const [showAddModal, setShowAddModal] = useState(false);
  const [showEditModal, setShowEditModal] = useState(false);
  const [showDeleteModal, setShowDeleteModal] = useState(false);
  const [selectedDevice, setSelectedDevice] = useState<DeviceItem | null>(null);

  // Tree data for filter - includes "All Areas" option
  const filterAreasTree = useMemo(() => {
    if (!currentCustomer) return [];
    
    const tree = getAreasByCustomerId(currentCustomer.id);
    
    // Create a special "All Areas" node
    const allNode: Area = {
      id: 'all',
      name: '全部区域',
      type: 'level1', // Fallback icon
      children: []
    };
    
    return [allNode, ...tree];
  }, [currentCustomer, getAreasByCustomerId]);

  const filteredDevices = devices.filter((device) => {
    // 1. Filter by search term
    const matchesSearch =
      device.name.toLowerCase().includes(searchTerm.toLowerCase()) ||
      device.serialNumber.toLowerCase().includes(searchTerm.toLowerCase()) ||
      device.model.toLowerCase().includes(searchTerm.toLowerCase());
    
    // 2. Filter by status
    const matchesStatus = filterStatus === 'all' || device.status === filterStatus;
    
    // 3. Filter by area (location)
    const matchesArea = filterArea === 'all' || device.location === filterArea;

    return matchesSearch && matchesStatus && matchesArea;
  });

  const statusStats = {
    total: filteredDevices.length,
    online: filteredDevices.filter((d) => d.status === 'online').length,
    offline: filteredDevices.filter((d) => d.status === 'offline').length,
    warning: filteredDevices.filter((d) => d.status === 'warning').length,
  };

  const getStatusBadge = (status: string) => {
    switch (status) {
      case 'online':
        return (
          <span className="inline-flex items-center gap-1 px-3 py-1 rounded-full text-xs font-medium bg-green-500/20 text-green-400 border border-green-500/30">
            <CheckCircle className="w-3 h-3" />
            在线
          </span>
        );
      case 'offline':
        return (
          <span className="inline-flex items-center gap-1 px-3 py-1 rounded-full text-xs font-medium bg-gray-500/20 text-gray-400 border border-gray-500/30">
            <XCircle className="w-3 h-3" />
            离线
          </span>
        );
      case 'warning':
        return (
          <span className="inline-flex items-center gap-1 px-3 py-1 rounded-full text-xs font-medium bg-amber-500/20 text-amber-400 border border-amber-500/30">
            <AlertTriangle className="w-3 h-3" />
            警告
          </span>
        );
    }
  };

  const handleAddDevice = (device: Partial<DeviceItem>) => {
    addDevice(device);
    setShowAddModal(false);
  };

  const handleEditDevice = (device: DeviceItem) => {
    updateDevice(device);
    setShowEditModal(false);
    setSelectedDevice(null);
  };

  const handleDeleteDevice = (id: string) => {
    deleteDevice(id);
    setShowDeleteModal(false);
    setSelectedDevice(null);
  };

  const handleBatchImport = () => {
    // Simulate batch import
    alert('批量导入功能：请准备包含设备信息的Excel/CSV文件');
  };

  const handleExport = () => {
    // Simulate export
    alert('导出功能：设备列表将导出为Excel文件');
  };

  return (
    <div className="p-6 space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-3xl font-bold text-white mb-2">设备管理</h1>
          <p className="text-gray-400">管理监控所有厨房设备</p>
        </div>
        <div className="flex items-center gap-3">
          {hasPermission(PERMISSIONS.CREATE_DEVICES) && (
            <button
                onClick={handleBatchImport}
                className="flex items-center gap-2 px-4 py-2 bg-gray-700 hover:bg-gray-600 border border-gray-600 rounded-lg transition-colors text-white"
            >
                <Upload className="w-4 h-4" />
                <span>批量导入</span>
            </button>
          )}
          <button
            onClick={handleExport}
            className="flex items-center gap-2 px-4 py-2 bg-gray-700 hover:bg-gray-600 border border-gray-600 rounded-lg transition-colors text-white"
          >
            <Download className="w-4 h-4" />
            <span>导出</span>
          </button>
          {hasPermission(PERMISSIONS.CREATE_DEVICES) && (
            <button
                onClick={() => setShowAddModal(true)}
                className="flex items-center gap-2 px-4 py-2 bg-gradient-to-r from-blue-500 to-purple-500 hover:from-blue-600 hover:to-purple-600 rounded-lg transition-all"
            >
                <Plus className="w-5 h-5" />
                <span>添加设备</span>
            </button>
          )}
        </div>
      </div>

      {/* Stats Cards */}
      <div className="grid grid-cols-4 gap-4">
        <motion.div initial={{ opacity: 0, y: 20 }} animate={{ opacity: 1, y: 0 }} className="p-6 bg-gradient-to-br from-blue-500/20 to-cyan-500/10 border border-blue-500/30 rounded-xl">
          <div className="flex items-center justify-between mb-2">
            <Power className="w-8 h-8 text-blue-400" />
            <span className="text-3xl font-bold text-white">{statusStats.total}</span>
          </div>
          <p className="text-gray-300">总设备数</p>
        </motion.div>

        <motion.div initial={{ opacity: 0, y: 20 }} animate={{ opacity: 1, y: 0 }} transition={{ delay: 0.1 }} className="p-6 bg-gradient-to-br from-green-500/20 to-emerald-500/10 border border-green-500/30 rounded-xl">
          <div className="flex items-center justify-between mb-2">
            <CheckCircle className="w-8 h-8 text-green-400" />
            <span className="text-3xl font-bold text-white">{statusStats.online}</span>
          </div>
          <p className="text-gray-300">在线设备</p>
        </motion.div>

        <motion.div initial={{ opacity: 0, y: 20 }} animate={{ opacity: 1, y: 0 }} transition={{ delay: 0.2 }} className="p-6 bg-gradient-to-br from-amber-500/20 to-orange-500/10 border border-amber-500/30 rounded-xl">
          <div className="flex items-center justify-between mb-2">
            <AlertTriangle className="w-8 h-8 text-amber-400" />
            <span className="text-3xl font-bold text-white">{statusStats.warning}</span>
          </div>
          <p className="text-gray-300">警告设备</p>
        </motion.div>

        <motion.div initial={{ opacity: 0, y: 20 }} animate={{ opacity: 1, y: 0 }} transition={{ delay: 0.3 }} className="p-6 bg-gradient-to-br from-gray-500/20 to-slate-500/10 border border-gray-500/30 rounded-xl">
          <div className="flex items-center justify-between mb-2">
            <XCircle className="w-8 h-8 text-gray-400" />
            <span className="text-3xl font-bold text-white">{statusStats.offline}</span>
          </div>
          <p className="text-gray-300">离线设备</p>
        </motion.div>
      </div>

      {/* Filters and Search */}
      <div className="flex items-center gap-4">
        <div className="flex-1 relative">
          <Search className="absolute left-4 top-1/2 -translate-y-1/2 w-5 h-5 text-gray-400" />
          <input
            type="text"
            placeholder="搜索设备名称或序列号..."
            value={searchTerm}
            onChange={(e) => setSearchTerm(e.target.value)}
            className="w-full pl-12 pr-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white placeholder-gray-500 focus:outline-none focus:border-blue-500 transition-colors"
          />
        </div>
        <div className="flex items-center gap-2">
          <Filter className="w-5 h-5 text-gray-400" />
          
          {/* Area Filter - Tree Select */}
          <div className="w-48">
             <AreaTreeSelect 
                areas={filterAreasTree}
                value={filterArea === 'all' ? '全部区域' : filterArea}
                onChange={(name, id) => setFilterArea(id === 'all' ? 'all' : name)}
                placeholder="全部区域"
             />
          </div>

          {/* Status Filter */}
          <select
            value={filterStatus}
            onChange={(e) => setFilterStatus(e.target.value)}
            className="px-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500 transition-colors [&>option]:bg-gray-800 [&>option]:text-white"
          >
            <option value="all" className="bg-gray-800 text-white">全部状态</option>
            <option value="online" className="bg-gray-800 text-white">在线</option>
            <option value="offline" className="bg-gray-800 text-white">离线</option>
            <option value="warning" className="bg-gray-800 text-white">警告</option>
          </select>
        </div>
      </div>

      {/* Devices Table */}
      <div className="bg-white/5 backdrop-blur-xl border border-white/10 rounded-xl overflow-hidden">
        <div className="overflow-x-auto">
          <table className="w-full">
            <thead>
              <tr className="border-b border-white/10">
                <th className="px-6 py-4 text-left text-sm font-semibold text-gray-300">设备信息</th>
                <th className="px-6 py-4 text-left text-sm font-semibold text-gray-300">型号/序列号</th>
                <th className="px-6 py-4 text-left text-sm font-semibold text-gray-300">类别</th>
                <th className="px-6 py-4 text-left text-sm font-semibold text-gray-300">所属项目</th>
                <th className="px-6 py-4 text-left text-sm font-semibold text-gray-300">区域</th>
                <th className="px-6 py-4 text-left text-sm font-semibold text-gray-300">状态</th>
                <th className="px-6 py-4 text-left text-sm font-semibold text-gray-300">安装日期</th>
                <th className="px-6 py-4 text-left text-sm font-semibold text-gray-300">最后维护</th>
                <th className="px-6 py-4 text-center text-sm font-semibold text-gray-300">操作</th>
              </tr>
            </thead>
            <tbody>
              {filteredDevices.length > 0 ? (
                filteredDevices.map((device, index) => (
                  <motion.tr
                    key={device.id}
                    initial={{ opacity: 0, y: 20 }}
                    animate={{ opacity: 1, y: 0 }}
                    transition={{ delay: index * 0.05 }}
                    className="border-b border-white/5 hover:bg-white/5 transition-colors"
                  >
                    <td className="px-6 py-4">
                      <div className="flex items-center gap-3">
                        <div className="relative">
                          {device.status === 'online' ? (
                            <Wifi className="w-5 h-5 text-green-400" />
                          ) : (
                            <WifiOff className="w-5 h-5 text-gray-400" />
                          )}
                          {device.meterInstalled && (
                            <div className="absolute -top-1 -right-1 w-2 h-2 bg-blue-500 rounded-full" />
                          )}
                        </div>
                        <div>
                          <p className="font-medium text-white">{device.name}</p>
                          <p className="text-xs text-gray-400">{device.area}</p>
                        </div>
                      </div>
                    </td>
                    <td className="px-6 py-4">
                      <div className="space-y-1">
                        <p className="text-sm text-gray-300">{device.model}</p>
                        <p className="text-xs text-gray-500">{device.serialNumber}</p>
                      </div>
                    </td>
                    <td className="px-6 py-4">
                      <span className="text-sm text-gray-300">{device.category}</span>
                    </td>
                    <td className="px-6 py-4">
                      {device.projectName ? (
                        <span className="text-sm text-blue-300">{device.projectName}</span>
                      ) : (
                        <span className="text-sm text-gray-500">—</span>
                      )}
                    </td>
                    <td className="px-6 py-4">
                      <span className="text-sm text-gray-300">{device.location}</span>
                    </td>
                    <td className="px-6 py-4">{getStatusBadge(device.status)}</td>
                    <td className="px-6 py-4">
                      <span className="text-sm text-gray-400">{device.installDate}</span>
                    </td>
                    <td className="px-6 py-4">
                      <span className="text-sm text-gray-400">{device.lastMaintenance}</span>
                    </td>
                    <td className="px-6 py-4">
                      <div className="flex items-center justify-center gap-2">
                        {hasPermission(PERMISSIONS.UPDATE_DEVICES) && (
                            <button
                                onClick={() => {
                                    setSelectedDevice(device);
                                    setShowEditModal(true);
                                }}
                                className="p-2 hover:bg-blue-500/20 text-blue-400 rounded-lg transition-colors"
                            >
                                <Edit className="w-4 h-4" />
                            </button>
                        )}
                        {hasPermission(PERMISSIONS.DELETE_DEVICES) && (
                            <button
                                onClick={() => {
                                    setSelectedDevice(device);
                                    setShowDeleteModal(true);
                                }}
                                className="p-2 hover:bg-red-500/20 text-red-400 rounded-lg transition-colors"
                                >
                                <Trash2 className="w-4 h-4" />
                            </button>
                        )}
                      </div>
                    </td>
                  </motion.tr>
                ))
              ) : (
                <tr>
                  <td colSpan={9} className="px-6 py-8 text-center text-gray-400">
                    暂无数据
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </div>

      {/* Add Device Modal */}
      <DeviceFormModal
        show={showAddModal}
        onClose={() => setShowAddModal(false)}
        onSave={handleAddDevice}
        title="添加设备"
      />

      {/* Edit Device Modal */}
      {selectedDevice && (
        <DeviceFormModal
          show={showEditModal}
          onClose={() => {
            setShowEditModal(false);
            setSelectedDevice(null);
          }}
          onSave={handleEditDevice}
          title="编辑设备"
          initialData={selectedDevice}
        />
      )}

      {/* Delete Confirmation Modal */}
      <AnimatePresence>
        {showDeleteModal && selectedDevice && (
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            className="fixed inset-0 bg-black/50 backdrop-blur-sm flex items-center justify-center z-50 p-4"
            onClick={() => {
              setShowDeleteModal(false);
              setSelectedDevice(null);
            }}
          >
            <motion.div
              initial={{ scale: 0.9, y: 20 }}
              animate={{ scale: 1, y: 0 }}
              exit={{ scale: 0.9, y: 20 }}
              onClick={(e) => e.stopPropagation()}
              className="bg-gray-800 border border-red-500/30 rounded-xl p-6 max-w-md w-full"
            >
              <div className="flex items-center gap-3 mb-4">
                <div className="p-3 bg-red-500/20 rounded-lg">
                  <AlertTriangle className="w-6 h-6 text-red-400" />
                </div>
                <div>
                  <h3 className="text-xl font-semibold text-white">确认删除</h3>
                  <p className="text-sm text-gray-400">此操作无法撤销</p>
                </div>
              </div>

              <p className="text-gray-300 mb-6">
                确定要删除设备 <span className="text-white font-medium">{selectedDevice.name}</span> 吗？
              </p>

              <div className="flex gap-3">
                <button
                  onClick={() => {
                    setShowDeleteModal(false);
                    setSelectedDevice(null);
                  }}
                  className="flex-1 px-4 py-2 bg-white/5 hover:bg-white/10 border border-white/10 rounded-lg transition-colors"
                >
                  取消
                </button>
                <button
                  onClick={() => handleDeleteDevice(selectedDevice.id)}
                  className="flex-1 px-4 py-2 bg-red-500 hover:bg-red-600 rounded-lg transition-colors"
                >
                  确认删除
                </button>
              </div>
            </motion.div>
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}

// Device Form Modal Component
function DeviceFormModal({
  show,
  onClose,
  onSave,
  title,
  initialData,
}: {
  show: boolean;
  onClose: () => void;
  onSave: (device: any) => void;
  title: string;
  initialData?: DeviceItem;
}) {
  const { getItemsByType } = useDictionary();
  const { currentCustomer } = useAuth();
  const { getAreasByCustomerId } = useArea();
  
  const customerAreas = useMemo(() => {
      if (!currentCustomer) return [];
      return getAreasByCustomerId(currentCustomer.id);
  }, [currentCustomer, getAreasByCustomerId]);

  // 从字典管理获取设备类别列表
  const deviceCategories = useMemo(() => {
    return getItemsByType(DictionaryType.DEVICE_CATEGORY)
      .filter(item => item.status === 'active') // 只显示启用的类别
      .sort((a, b) => a.sort - b.sort); // 按排序字段排序
  }, [getItemsByType]);

  // 从当前客户获取项目列表
  const customerProjects = useMemo(() => {
    return currentCustomer?.projects ?? [];
  }, [currentCustomer]);

  const [formData, setFormData] = useState<Partial<DeviceItem>>(
    initialData || {
      name: '',
      model: '',
      serialNumber: '',
      category: '',
      location: '',
      area: '',
      projectId: '',
      projectName: '',
      energyType: ['electric'],
      installDate: '',
      lastMaintenance: '',
      supplier: '',
      warrantyDate: '',
      power: 0,
      voltage: '',
      meterInstalled: false,
    }
  );

  useEffect(() => {
    if (initialData) {
      setFormData(initialData);
    }
  }, [initialData]);

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    onSave(formData);
  };

  const toggleEnergyType = (type: 'electric' | 'gas' | 'water') => {
    const currentTypes = formData.energyType || [];
    if (currentTypes.includes(type)) {
      setFormData({ ...formData, energyType: currentTypes.filter(t => t !== type) });
    } else {
      setFormData({ ...formData, energyType: [...currentTypes, type] });
    }
  };

  if (!show) return null;

  return (
    <AnimatePresence>
      <motion.div
        initial={{ opacity: 0 }}
        animate={{ opacity: 1 }}
        exit={{ opacity: 0 }}
        className="fixed inset-0 bg-black/50 backdrop-blur-sm flex items-center justify-center z-50 p-4"
        onClick={onClose}
      >
        <motion.div
          initial={{ scale: 0.9, y: 20 }}
          animate={{ scale: 1, y: 0 }}
          exit={{ scale: 0.9, y: 20 }}
          onClick={(e) => e.stopPropagation()}
          className="bg-gray-800 border border-white/10 rounded-xl max-w-4xl w-full max-h-[90vh] overflow-hidden flex flex-col"
        >
          {/* Header */}
          <div className="flex items-center justify-between p-6 border-b border-white/10 shrink-0">
            <h3 className="text-2xl font-bold text-white">{title}</h3>
            <button
              onClick={onClose}
              className="p-2 hover:bg-white/10 rounded-lg transition-colors"
            >
              <X className="w-5 h-5 text-gray-400" />
            </button>
          </div>

          {/* Form */}
          <form onSubmit={handleSubmit} className="p-6 overflow-y-auto flex-1">
            <div className="grid grid-cols-2 gap-6">
              {/* Basic Info */}
              <div className="col-span-2">
                <h4 className="text-lg font-semibold text-white mb-4">基本信息</h4>
              </div>

              <div>
                <label className="block text-sm font-medium text-gray-300 mb-2">
                  设备名称 <span className="text-red-400">*</span>
                </label>
                <input
                  type="text"
                  required
                  value={formData.name}
                  onChange={(e) => setFormData({ ...formData, name: e.target.value })}
                  className="w-full px-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500 transition-colors"
                  placeholder="例如：智冰箱-A01"
                />
              </div>

              <div>
                <label className="block text-sm font-medium text-gray-300 mb-2">
                  设备型号 <span className="text-red-400">*</span>
                </label>
                <input
                  type="text"
                  required
                  value={formData.model}
                  onChange={(e) => setFormData({ ...formData, model: e.target.value })}
                  className="w-full px-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500 transition-colors"
                  placeholder="例如：SR-F520BX"
                />
              </div>

              <div>
                <label className="block text-sm font-medium text-gray-300 mb-2">
                  序列号 <span className="text-red-400">*</span>
                </label>
                <input
                  type="text"
                  required
                  value={formData.serialNumber}
                  onChange={(e) => setFormData({ ...formData, serialNumber: e.target.value })}
                  className="w-full px-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500 transition-colors"
                  placeholder="SN..."
                />
              </div>

              <div>
                <label className="block text-sm font-medium text-gray-300 mb-2">
                  设备类别
                </label>
                <select
                  value={formData.category}
                  onChange={(e) => setFormData({ ...formData, category: e.target.value })}
                  className="w-full px-4 py-3 bg-gray-800 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500 transition-colors"
                >
                  <option value="" className="bg-gray-800 text-white">请选择类别（可选）</option>
                  {deviceCategories.map(cat => (
                    <option key={cat.id} value={cat.name} className="bg-gray-800 text-white">{cat.name}</option>
                  ))}
                </select>
              </div>

              {/* 所属项目 */}
              <div>
                <label className="block text-sm font-medium text-gray-300 mb-2">
                  所属项目 <span className="text-red-400">*</span>
                </label>
                <select
                  required
                  value={formData.projectId || ''}
                  onChange={(e) => {
                    const pid = e.target.value;
                    const proj = customerProjects.find(p => p.id === pid);
                    setFormData({
                      ...formData,
                      projectId: pid,
                      projectName: proj?.name ?? '',
                    });
                  }}
                  className="w-full px-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500 transition-colors [&>option]:bg-gray-800 [&>option]:text-white"
                >
                  <option value="">请选择项目</option>
                  {customerProjects.map(proj => (
                    <option key={proj.id} value={proj.id}>{proj.name}</option>
                  ))}
                </select>
              </div>

              <div>
                <label className="block text-sm font-medium text-gray-300 mb-2">
                  所属区域 <span className="text-red-400">*</span>
                </label>
                <AreaTreeSelect
                    areas={customerAreas}
                    value={formData.location}
                    onChange={(name, id) => setFormData({ ...formData, location: name })}
                    placeholder="请选择区域"
                />
              </div>

              <div>
                <label className="block text-sm font-medium text-gray-300 mb-2">
                  安装日期
                </label>
                <input
                  type="date"
                  value={formData.installDate}
                  onChange={(e) => setFormData({ ...formData, installDate: e.target.value })}
                  className="w-full px-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500 transition-colors [color-scheme:dark]"
                />
              </div>

              {/* Energy & Specs */}
              <div className="col-span-2 pt-4 border-t border-white/10">
                <h4 className="text-lg font-semibold text-white mb-4">能耗规格</h4>
              </div>

              <div className="col-span-2">
                <label className="block text-sm font-medium text-gray-300 mb-2">
                  能源类型
                </label>
                <div className="flex gap-4">
                  {[
                    { type: 'electric', label: '用电' },
                    { type: 'gas', label: '用气' },
                    { type: 'water', label: '用水' },
                  ].map((item) => (
                    <label key={item.type} className="flex items-center gap-2 cursor-pointer">
                      <input
                        type="checkbox"
                        checked={formData.energyType?.includes(item.type as any)}
                        onChange={() => toggleEnergyType(item.type as any)}
                        className="rounded border-gray-600 bg-gray-700 text-blue-500 focus:ring-blue-500 focus:ring-offset-gray-800"
                      />
                      <span className="text-white">{item.label}</span>
                    </label>
                  ))}
                </div>
              </div>

              <div>
                <label className="block text-sm font-medium text-gray-300 mb-2">
                  额定功率 (W)
                </label>
                <input
                  type="number"
                  value={formData.power}
                  onChange={(e) => setFormData({ ...formData, power: Number(e.target.value) })}
                  className="w-full px-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500 transition-colors"
                  placeholder="0"
                />
              </div>

              <div>
                <label className="block text-sm font-medium text-gray-300 mb-2">
                  额定电压
                </label>
                <input
                  type="text"
                  value={formData.voltage}
                  onChange={(e) => setFormData({ ...formData, voltage: e.target.value })}
                  className="w-full px-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500 transition-colors"
                  placeholder="例如：220V"
                />
              </div>

              <div className="col-span-2">
                 <label className="flex items-center gap-2 cursor-pointer">
                    <input
                      type="checkbox"
                      checked={formData.meterInstalled}
                      onChange={(e) => setFormData({ ...formData, meterInstalled: e.target.checked })}
                      className="rounded border-gray-600 bg-gray-700 text-blue-500 focus:ring-blue-500 focus:ring-offset-gray-800"
                    />
                    <span className="text-white">已安装智能电表/水表/气表</span>
                  </label>
              </div>

               {/* Maintenance */}
               <div className="col-span-2 pt-4 border-t border-white/10">
                <h4 className="text-lg font-semibold text-white mb-4">维保信息</h4>
              </div>

              <div>
                <label className="block text-sm font-medium text-gray-300 mb-2">
                  供应商
                </label>
                <input
                  type="text"
                  value={formData.supplier}
                  onChange={(e) => setFormData({ ...formData, supplier: e.target.value })}
                  className="w-full px-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500 transition-colors"
                  placeholder="例如：美的商用"
                />
              </div>

              <div>
                <label className="block text-sm font-medium text-gray-300 mb-2">
                  质保到期日
                </label>
                <input
                  type="date"
                  value={formData.warrantyDate}
                  onChange={(e) => setFormData({ ...formData, warrantyDate: e.target.value })}
                  className="w-full px-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500 transition-colors [color-scheme:dark]"
                />
              </div>
            </div>

            <div className="flex gap-3 pt-6 mt-6 border-t border-white/10">
              <button
                type="button"
                onClick={onClose}
                className="flex-1 px-6 py-3 bg-white/5 hover:bg-white/10 border border-white/10 rounded-lg transition-colors"
              >
                取消
              </button>
              <button
                type="submit"
                className="flex-1 px-6 py-3 bg-gradient-to-r from-blue-500 to-purple-500 hover:from-blue-600 hover:to-purple-600 rounded-lg transition-all font-semibold"
              >
                {initialData ? '保存更改' : '添加设备'}
              </button>
            </div>
          </form>
        </motion.div>
      </motion.div>
    </AnimatePresence>
  );
}