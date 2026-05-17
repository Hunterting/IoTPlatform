import { useState, useRef, useCallback, useEffect } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import { 
  FolderOpen, 
  File, 
  FileText, 
  Image, 
  Download, 
  Upload, 
  Search, 
  Plus,
  Eye,
  X,
  Cpu,
  Edit,
  Check,
  ExternalLink,
  Save,
  List,
  CloudUpload,
  Trash2,
  FileType,
  Tag,
  MapPin,
  Loader2,
  AlertCircle,
} from 'lucide-react';
import { useAuth } from '@/app/contexts/AuthContext';
import { useArea, Area } from '@/app/contexts/AreaContext';
import { useArchive } from '@/app/contexts/ArchiveContext';
import { useDictionary, DictionaryType } from '@/app/contexts/DictionaryContext';
import { DeviceInventoryModal } from '@/app/components/DeviceInventoryModal';
import { AreaTreeSelect } from '@/app/components/AreaTreeSelect';
import { PERMISSIONS } from '@/app/config/permissions';
import { Archive as ArchiveType, DeviceMarker } from '@/app/data/archivesData';
import * as archivesApi from '@/app/services/api/archivesApi';
import * as attachmentApi from '@/app/services/api/attachmentApi';
import { 
  adaptArchiveFromBackend, 
  formatFileSize,
  detectFileType,
  calculateArchiveStats,
  formatTotalSize,
} from '@/app/services/adapters/archiveAdapter';
import { ArchiveDto } from '@/app/services/api/types/archive.types';

interface DeviceInventoryItem {
  id: string;
  name: string;
  type: string;
  model: string;
  specification: string;
  serialNumber: string;
  sensors: string[];
}

interface ArchivesPageProps {
  onNavigateToDevice: (deviceId: string) => void;
}

export function ArchivesPage({ onNavigateToDevice }: ArchivesPageProps) {
  const { currentCustomer, hasPermission } = useAuth();
  const { areas, flattenAreas } = useArea();
  const { selectedAreaForArchive, setSelectedAreaForArchive } = useArchive();
  const { getItemsByType } = useDictionary();
  
  const [searchTerm, setSearchTerm] = useState('');
  const [selectedCategory, setSelectedCategory] = useState('all');
  const [show3DViewer, setShow3DViewer] = useState(false);
  const [selected3DFile, setSelected3DFile] = useState<ArchiveType | null>(null);
  const [showDeviceInfo, setShowDeviceInfo] = useState(false);
  const [selectedDevice, setSelectedDevice] = useState<DeviceMarker | null>(null);
  const [hoveredDevice, setHoveredDevice] = useState<string | null>(null);
  const [showEditModal, setShowEditModal] = useState(false);
  const [editingDevice, setEditingDevice] = useState<DeviceMarker | null>(null);
  const [showInventoryModal, setShowInventoryModal] = useState(false);
  const [showUploadModal, setShowUploadModal] = useState(false);
  const [showDeleteConfirm, setShowDeleteConfirm] = useState(false);
  const [deletingArchive, setDeletingArchive] = useState<ArchiveType | null>(null);
  
  // 档案数据状态
  const [archives, setArchives] = useState<ArchiveType[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // 加载档案列表
  const loadArchives = useCallback(async (keyword?: string) => {
    setLoading(true);
    setError(null);
    try {
      // 使用档案管理API获取档案列表
      const response = await archivesApi.getArchives({
        keyword: keyword || searchTerm || undefined,
        page: 1,
        pageSize: 100,
      });
      
      if (response.code === 200 && response.data) {
        const adaptedArchives = response.data.items.map((item: any) => adaptArchiveFromBackend(item));
        setArchives(adaptedArchives);
      } else {
        setError(response.message || '加载档案列表失败');
      }
    } catch (err) {
      setError('加载档案列表失败，请稍后重试');
      console.error('加载档案列表失败:', err);
    } finally {
      setLoading(false);
    }
  }, [searchTerm]);

  // 组件加载时获取档案列表
  useEffect(() => {
    loadArchives();
  }, [loadArchives]);

  // 计算档案统计
  const archiveStats = calculateArchiveStats(archives);

  // 从字典管理获取档案分类 (同时支持两种类型名称)
  const archiveCategoriesV1 = getItemsByType(DictionaryType.ARCHIVE_CATEGORY);
  const archiveCategoriesV2 = getItemsByType(DictionaryType.ARCHIVE_CATEGORY_V2);
  const archiveCategories = [...archiveCategoriesV1, ...archiveCategoriesV2];
  const categories = ['all', ...archiveCategories.map(item => item.name)];

  const filteredArchives = archives.filter((archive) => {
    const matchesSearch = archive.name.toLowerCase().includes(searchTerm.toLowerCase());
    const matchesCategory = selectedCategory === 'all' || archive.category === selectedCategory;
    const matchesCustomer = currentCustomer ? archive.appCode === currentCustomer.appCode : false;
    return matchesSearch && matchesCategory && matchesCustomer;
  });

  const handleView3D = (archive: Archive) => {
    setSelected3DFile(archive);
    setShow3DViewer(true);
  };

  const handleDeviceClick = (device: DeviceMarker) => {
    setSelectedDevice(device);
    setShowDeviceInfo(true);
  };

  const handleJumpToDevice = () => {
    if (!selectedDevice) return;
    
    // 关闭所有弹窗
    setShow3DViewer(false);
    setShowDeviceInfo(false);
    
    // 调用父组件传递的导航函数，跳转到设备管理页面并定位设备
    onNavigateToDevice(selectedDevice.id);
  };

  // 下载档案
  const handleDownload = async (archive: ArchiveType) => {
    try {
      const fileId = parseInt(archive.id);
      if (isNaN(fileId)) {
        alert('无效的文件ID');
        return;
      }
      await attachmentApi.downloadAttachment(fileId, archive.name);
    } catch (err) {
      console.error('下载失败:', err);
      alert('下载失败，请稍后重试');
    }
  };

  const getFileIcon = (type: string) => {
    switch (type) {
      case 'folder':
        return <FolderOpen className="w-8 h-8 text-blue-400" />;
      case 'pdf':
        return <File className="w-8 h-8 text-red-400" />;
      case 'document':
        return <FileText className="w-8 h-8 text-green-400" />;
      case 'image':
        return <Image className="w-8 h-8 text-purple-400" />;
      case 'blueprint':
        return <Image className="w-8 h-8 text-cyan-400" />;
      default:
        return <File className="w-8 h-8 text-gray-400" />;
    }
  };

  return (
    <div className="p-6 space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-3xl font-bold text-white mb-2">档案管理</h1>
          <p className="text-gray-400">设备档案、图纸资料与文档管理</p>
        </div>
        <div className="flex items-center gap-3">
          {hasPermission(PERMISSIONS.CREATE_ARCHIVES) && (
            <>
                <button 
                    onClick={() => setShowInventoryModal(true)}
                    className="flex items-center gap-2 px-4 py-2 bg-gradient-to-r from-green-500 to-emerald-500 hover:from-green-600 hover:to-emerald-600 rounded-lg transition-all"
                >
                    <List className="w-4 h-4" />
                    <span>清单录入</span>
                </button>
                <button
                    onClick={() => setShowUploadModal(true)}
                    className="flex items-center gap-2 px-4 py-2 bg-gradient-to-r from-blue-500 to-purple-500 hover:from-blue-600 hover:to-purple-600 rounded-lg transition-all"
                >
                    <Upload className="w-4 h-4" />
                    <span>上传文件</span>
                </button>
            </>
          )}
        </div>
      </div>

      {/* Stats */}
      <div className="grid grid-cols-4 gap-4">
        {/* ... stats ... */}
        <motion.div
          initial={{ opacity: 0, y: 20 }}
          animate={{ opacity: 1, y: 0 }}
          className="p-6 bg-gradient-to-br from-blue-500/20 to-cyan-500/10 border border-blue-500/30 rounded-xl"
        >
          <div className="flex items-center justify-between mb-2">
            <FolderOpen className="w-8 h-8 text-blue-400" />
            <span className="text-3xl font-bold text-white">{filteredArchives.length}</span>
          </div>
          <p className="text-gray-300">总文件数</p>
        </motion.div>
         {/* ... (other stats similar to before, preserving structure) ... */}
         <motion.div
          initial={{ opacity: 0, y: 20 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ delay: 0.1 }}
          className="p-6 bg-gradient-to-br from-purple-500/20 to-pink-500/10 border border-purple-500/30 rounded-xl"
        >
          <div className="flex items-center justify-between mb-2">
            <File className="w-8 h-8 text-purple-400" />
            <span className="text-3xl font-bold text-white">
              {archiveStats.pdfCount}
            </span>
          </div>
          <p className="text-gray-300">PDF 文档</p>
        </motion.div>

        <motion.div
          initial={{ opacity: 0, y: 20 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ delay: 0.2 }}
          className="p-6 bg-gradient-to-br from-green-500/20 to-emerald-500/10 border border-green-500/30 rounded-xl"
        >
          <div className="flex items-center justify-between mb-2">
            <Image className="w-8 h-8 text-green-400" />
            <span className="text-3xl font-bold text-white">
              {archiveStats.imageCount + archiveStats.blueprintCount}
            </span>
          </div>
          <p className="text-gray-300">图纸资料</p>
        </motion.div>

        <motion.div
          initial={{ opacity: 0, y: 20 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ delay: 0.3 }}
          className="p-6 bg-gradient-to-br from-amber-500/20 to-orange-500/10 border border-amber-500/30 rounded-xl"
        >
          <div className="flex items-center justify-between mb-2">
            <FileText className="w-8 h-8 text-amber-400" />
            <span className="text-2xl font-bold text-white">{formatTotalSize(archiveStats.totalSize)}</span>
          </div>
          <p className="text-gray-300">总大小</p>
        </motion.div>
      </div>

      {/* Search and Filter */}
      <div className="flex items-center gap-4">
        <div className="flex-1 relative">
          <Search className="absolute left-4 top-1/2 -translate-y-1/2 w-5 h-5 text-gray-400" />
          <input
            type="text"
            placeholder="搜索文件名..."
            value={searchTerm}
            onChange={(e) => setSearchTerm(e.target.value)}
            className="w-full pl-12 pr-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white placeholder-gray-500 focus:outline-none focus:border-blue-500 transition-colors"
          />
        </div>
      </div>

      {/* Category Tabs */}
      <div className="flex gap-2 overflow-x-auto pb-2">
        {categories.map((category) => (
          <button
            key={category}
            onClick={() => setSelectedCategory(category)}
            className={`px-4 py-2 rounded-lg whitespace-nowrap transition-all ${
              selectedCategory === category
                ? 'bg-gradient-to-r from-blue-500 to-purple-500 text-white'
                : 'bg-white/5 text-gray-400 hover:bg-white/10'
            }`}
          >
            {category === 'all' ? '全部' : category}
          </button>
        ))}
      </div>

      {/* Files Grid */}
      {loading ? (
        <div className="flex flex-col items-center justify-center py-16">
          <Loader2 className="w-12 h-12 text-blue-400 animate-spin mb-4" />
          <p className="text-gray-400">加载档案列表中...</p>
        </div>
      ) : error ? (
        <div className="flex flex-col items-center justify-center py-16">
          <AlertCircle className="w-12 h-12 text-red-400 mb-4" />
          <p className="text-red-400 mb-2">{error}</p>
          <button
            onClick={() => loadArchives()}
            className="px-4 py-2 bg-blue-500 hover:bg-blue-600 text-white rounded-lg transition-colors"
          >
            重试
          </button>
        </div>
      ) : archives.length === 0 ? (
        <div className="flex flex-col items-center justify-center py-16">
          <FolderOpen className="w-16 h-16 text-gray-600 mb-4" />
          <p className="text-gray-400 text-lg mb-2">暂无记录</p>
          <p className="text-gray-500 text-sm">点击上方"上传文件"按钮添加档案</p>
        </div>
      ) : filteredArchives.length === 0 ? (
        <div className="flex flex-col items-center justify-center py-16">
          <Search className="w-16 h-16 text-gray-600 mb-4" />
          <p className="text-gray-400 text-lg mb-2">没有找到匹配的文件</p>
          <p className="text-gray-500 text-sm">请尝试其他搜索条件</p>
        </div>
      ) : (
        <div className="grid grid-cols-4 gap-4">
          {filteredArchives.map((archive, index) => (
            <motion.div
              key={archive.id}
              initial={{ opacity: 0, scale: 0.9 }}
              animate={{ opacity: 1, scale: 1 }}
              transition={{ delay: index * 0.05 }}
              className="bg-white/5 backdrop-blur-xl border border-white/10 rounded-xl p-6 hover:bg-white/10 transition-all group relative"
            >
              {/* Action Buttons - Show on hover */}
              <div className="absolute top-3 right-3 flex gap-2 opacity-0 group-hover:opacity-100 transition-opacity z-10">
                {hasPermission(PERMISSIONS.DELETE_ARCHIVES) && (
                  <motion.button
                    whileHover={{ scale: 1.1 }}
                    whileTap={{ scale: 0.95 }}
                    onClick={(e) => {
                      e.stopPropagation();
                      setDeletingArchive(archive);
                      setShowDeleteConfirm(true);
                    }}
                    className="p-2 bg-red-500/80 hover:bg-red-500 rounded-lg transition-colors backdrop-blur-sm"
                    title="删除档案"
                  >
                    <Trash2 className="w-4 h-4 text-white" />
                  </motion.button>
                )}
              </div>

              <div className="flex flex-col items-center text-center">
                <div className="mb-4">{getFileIcon(archive.type)}</div>
                <h3 className="font-medium text-white mb-2 line-clamp-2">{archive.name}</h3>
                <div className="space-y-1 text-xs text-gray-400 mb-4">
                  <p>{archive.category}</p>
                  {archive.size && <p>{archive.size}</p>}
                  <p>{archive.date}</p>
                  {archive.areaName && (
                    <div className="flex items-center justify-center gap-1 text-blue-400">
                      <MapPin className="w-3 h-3" />
                      <span>{archive.areaName}</span>
                    </div>
                  )}
                  {archive.is3DModel && (
                    <div className="flex items-center justify-center gap-1 text-cyan-400">
                      <Cpu className="w-3 h-3" />
                      <span>含设备标注</span>
                    </div>
                  )}
                </div>
                <div className="flex gap-2">
                  {archive.is3DModel ? (
                    <button
                      onClick={() => handleView3D(archive)}
                      className="py-2 px-4 bg-cyan-500/20 hover:bg-cyan-500/30 text-cyan-400 rounded-lg transition-colors flex items-center gap-2"
                    >
                      <Eye className="w-4 h-4" />
                      <span>3D查看</span>
                    </button>
                  ) : (
                    <button 
                      onClick={() => handleDownload(archive)}
                      className="py-2 px-4 bg-blue-500/20 hover:bg-blue-500/30 text-blue-400 rounded-lg transition-colors flex items-center gap-2"
                    >
                      <Download className="w-4 h-4" />
                      <span>下载</span>
                    </button>
                  )}
                </div>
              </div>
            </motion.div>
          ))}
        </div>
      )}

      {/* 3D Viewer Modal */}
      <AnimatePresence>
        {show3DViewer && selected3DFile && (
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            className="fixed inset-0 bg-black/80 backdrop-blur-sm flex items-center justify-center z-50 p-4"
            onClick={() => {
              setShow3DViewer(false);
              setSelectedDevice(null);
              setShowDeviceInfo(false);
            }}
          >
            <motion.div
              initial={{ scale: 0.9, y: 20 }}
              animate={{ scale: 1, y: 0 }}
              exit={{ scale: 0.9, y: 20 }}
              onClick={(e) => e.stopPropagation()}
              className="bg-gray-800 border border-white/10 rounded-xl max-w-7xl w-full h-[90vh] flex flex-col"
            >
              {/* Header */}
              <div className="flex items-center justify-between p-6 border-b border-white/10">
                <div className="flex items-center gap-3">
                  <Image className="w-6 h-6 text-cyan-400" />
                  <div>
                    <h3 className="text-2xl font-bold text-white">{selected3DFile.name}</h3>
                    <p className="text-sm text-gray-400">点击设标记查看详情，实现可视化定位</p>
                  </div>
                </div>
                <button
                  onClick={() => {
                    setShow3DViewer(false);
                    setSelectedDevice(null);
                    setShowDeviceInfo(false);
                  }}
                  className="p-2 hover:bg-white/10 rounded-lg transition-colors"
                >
                  <X className="w-5 h-5 text-gray-400" />
                </button>
              </div>

              {/* 3D View Area */}
              <div className="flex-1 p-6 overflow-hidden">
                <div className="relative w-full h-full bg-gray-900/50 rounded-lg border border-white/10 overflow-hidden">
                  {/* Simulated 3D Kitchen Layout */}
                  <img
                    src="https://images.unsplash.com/photo-1556911220-bff31c812dba?w=1200&h=800&fit=crop"
                    alt="厨房布局"
                    className="w-full h-full object-cover opacity-60"
                  />

                  {/* Device Markers */}
                  {selected3DFile.devices?.map((device) => (
                    <motion.div
                      key={device.id}
                      initial={{ scale: 0 }}
                      animate={{ scale: 1 }}
                      whileHover={{ scale: 1.2 }}
                      className="absolute cursor-pointer"
                      style={{
                        left: `${device.x}%`,
                        top: `${device.y}%`,
                        transform: 'translate(-50%, -50%)',
                      }}
                      onClick={(e) => {
                        e.stopPropagation();
                        handleDeviceClick(device);
                      }}
                      onMouseEnter={() => setHoveredDevice(device.id)}
                      onMouseLeave={() => setHoveredDevice(null)}
                    >
                      {/* Pulsing Circle */}
                      <div className="relative">
                        <motion.div
                          animate={{ scale: [1, 1.5, 1], opacity: [0.6, 0, 0.6] }}
                          transition={{ repeat: Infinity, duration: 2 }}
                          className="absolute inset-0 w-12 h-12 bg-cyan-500 rounded-full"
                        />
                        <div className="relative w-12 h-12 bg-cyan-500 rounded-full flex items-center justify-center border-2 border-white shadow-lg">
                          <Cpu className="w-6 h-6 text-white" />
                        </div>
                      </div>

                      {/* Device Label */}
                      {hoveredDevice === device.id && (
                        <motion.div
                          initial={{ opacity: 0, y: 10 }}
                          animate={{ opacity: 1, y: 0 }}
                          className="absolute top-full mt-2 left-1/2 -translate-x-1/2 bg-gray-900 border border-white/20 rounded-lg p-2 whitespace-nowrap shadow-xl z-10"
                        >
                          <p className="text-white text-sm font-medium">{device.name}</p>
                          <p className="text-gray-400 text-xs">{device.type}</p>
                        </motion.div>
                      )}
                    </motion.div>
                  ))}

                  {/* Device Info Panel */}
                  <AnimatePresence>
                    {showDeviceInfo && selectedDevice && (
                      <motion.div
                        initial={{ x: 300, opacity: 0 }}
                        animate={{ x: 0, opacity: 1 }}
                        exit={{ x: 300, opacity: 0 }}
                        className="absolute right-6 top-6 bottom-6 w-80 bg-gray-800/95 backdrop-blur-xl border border-white/10 rounded-xl p-6 overflow-y-auto"
                      >
                        <div className="flex items-center justify-between mb-4">
                          <h4 className="text-xl font-bold text-white">设备详情</h4>
                          <button
                            onClick={(e) => {
                              e.stopPropagation();
                              setShowDeviceInfo(false);
                              setSelectedDevice(null);
                            }}
                            className="p-1 hover:bg-white/10 rounded transition-colors"
                          >
                            <X className="w-4 h-4 text-gray-400" />
                          </button>
                        </div>

                        <div className="space-y-4">
                          {/* Device Icon */}
                          <div className="flex items-center justify-center p-4 bg-cyan-500/20 rounded-lg">
                            <Cpu className="w-16 h-16 text-cyan-400" />
                          </div>

                          {/* Device Name */}
                          <div>
                            <p className="text-sm text-gray-400 mb-1">设备名称</p>
                            <p className="text-white font-medium">{selectedDevice.name}</p>
                          </div>

                          {/* Device Type */}
                          <div>
                            <p className="text-sm text-gray-400 mb-1">设备类</p>
                            <p className="text-white">{selectedDevice.type}</p>
                          </div>

                          {/* Model */}
                          {selectedDevice.model && (
                            <div>
                              <p className="text-sm text-gray-400 mb-1">设备型号</p>
                              <p className="text-white">{selectedDevice.model}</p>
                            </div>
                          )}

                          {/* Serial Number */}
                          {selectedDevice.serialNumber && (
                            <div>
                              <p className="text-sm text-gray-400 mb-1">序列号</p>
                              <p className="text-white font-mono text-sm">{selectedDevice.serialNumber}</p>
                            </div>
                          )}

                          {/* Sensors */}
                          {selectedDevice.sensors && selectedDevice.sensors.length > 0 && (
                            <div>
                              <p className="text-sm text-gray-400 mb-2">关联传感器</p>
                              <div className="space-y-2">
                                {selectedDevice.sensors.map((sensor, idx) => (
                                  <div
                                    key={idx}
                                    className="flex items-center gap-2 p-2 bg-white/5 rounded-lg"
                                  >
                                    <Check className="w-4 h-4 text-green-400" />
                                    <span className="text-white text-sm">{sensor}</span>
                                  </div>
                                ))}
                              </div>
                            </div>
                          )}

                          {/* Actions */}
                          <div className="pt-4 border-t border-white/10 space-y-3">
                            <button
                              onClick={handleJumpToDevice}
                              className="w-full flex items-center justify-center gap-2 px-4 py-3 bg-gradient-to-r from-blue-500 to-purple-500 hover:from-blue-600 hover:to-purple-600 rounded-lg transition-all"
                            >
                              <ExternalLink className="w-4 h-4" />
                              <span>跳转到设备管理</span>
                            </button>
                            {hasPermission(PERMISSIONS.UPDATE_ARCHIVES) && (
                                <button
                                    onClick={() => {
                                        setEditingDevice(selectedDevice);
                                        setShowEditModal(true);
                                    }}
                                    className="w-full flex items-center justify-center gap-2 px-4 py-3 bg-white/5 hover:bg-white/10 border border-white/10 rounded-lg transition-colors"
                                >
                                    <Edit className="w-4 h-4" />
                                    <span>编辑设备信息</span>
                                </button>
                            )}
                          </div>
                        </div>
                      </motion.div>
                    )}
                  </AnimatePresence>
                </div>
              </div>
            </motion.div>
          </motion.div>
        )}
      </AnimatePresence>

      {/* Edit Device Modal */}
      <AnimatePresence>
        {showEditModal && editingDevice && (
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            className="fixed inset-0 bg-black/80 backdrop-blur-sm flex items-center justify-center z-50 p-4"
            onClick={() => {
              setShowEditModal(false);
              setEditingDevice(null);
            }}
          >
            <motion.div
              initial={{ scale: 0.9, y: 20 }}
              animate={{ scale: 1, y: 0 }}
              exit={{ scale: 0.9, y: 20 }}
              onClick={(e) => e.stopPropagation()}
              className="bg-gray-800 border border-white/10 rounded-xl max-w-7xl w-full h-[90vh] flex flex-col"
            >
              {/* Header */}
              <div className="flex items-center justify-between p-6 border-b border-white/10">
                <div className="flex items-center gap-3">
                  <Image className="w-6 h-6 text-cyan-400" />
                  <div>
                    <h3 className="text-2xl font-bold text-white">编辑设备信息</h3>
                    <p className="text-sm text-gray-400">修改设备的详细信息</p>
                  </div>
                </div>
                <button
                  onClick={() => {
                    setShowEditModal(false);
                    setEditingDevice(null);
                  }}
                  className="p-2 hover:bg-white/10 rounded-lg transition-colors"
                >
                  <X className="w-5 h-5 text-gray-400" />
                </button>
              </div>

              {/* Edit Form */}
              <div className="flex-1 p-6 overflow-hidden">
                <div className="relative w-full h-full bg-gray-900/50 rounded-lg border border-white/10 overflow-hidden">
                  {/* Simulated 3D Kitchen Layout */}
                  <img
                    src="https://images.unsplash.com/photo-1556911220-bff31c812dba?w=1200&h=800&fit=crop"
                    alt="厨房布局"
                    className="w-full h-full object-cover opacity-60"
                  />

                  {/* Device Markers */}
                  {selected3DFile?.devices?.map((device) => (
                    <motion.div
                      key={device.id}
                      initial={{ scale: 0 }}
                      animate={{ scale: 1 }}
                      whileHover={{ scale: 1.2 }}
                      className="absolute cursor-pointer"
                      style={{
                        left: `${device.x}%`,
                        top: `${device.y}%`,
                        transform: 'translate(-50%, -50%)',
                      }}
                      onClick={(e) => {
                        e.stopPropagation();
                        // Prevent clicking background
                      }}
                    >
                      {/* Pulsing Circle */}
                      <div className="relative">
                         {/* Highlight the one being edited */}
                        <div className={`relative w-12 h-12 rounded-full flex items-center justify-center border-2 shadow-lg ${
                            device.id === editingDevice.id ? 'bg-purple-500 border-white' : 'bg-gray-600 border-gray-400'
                        }`}>
                          <Cpu className="w-6 h-6 text-white" />
                        </div>
                      </div>
                    </motion.div>
                  ))}

                  {/* Edit Form Panel */}
                  <div className="absolute right-6 top-6 bottom-6 w-80 bg-gray-800/95 backdrop-blur-xl border border-white/10 rounded-xl p-6 overflow-y-auto">
                    <div className="space-y-4">
                      {/* Device Icon */}
                      <div className="flex items-center justify-center p-4 bg-cyan-500/20 rounded-lg">
                        <Cpu className="w-16 h-16 text-cyan-400" />
                      </div>

                      {/* Device Name */}
                      <div>
                        <p className="text-sm text-gray-400 mb-1">设备名称</p>
                        <input
                          type="text"
                          value={editingDevice.name}
                          onChange={(e) => setEditingDevice({ ...editingDevice, name: e.target.value })}
                          className="w-full pl-4 pr-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white placeholder-gray-500 focus:outline-none focus:border-blue-500 transition-colors"
                        />
                      </div>

                      {/* Device Type */}
                      <div>
                        <p className="text-sm text-gray-400 mb-1">设备类型</p>
                        <input
                          type="text"
                          value={editingDevice.type}
                          onChange={(e) => setEditingDevice({ ...editingDevice, type: e.target.value })}
                          className="w-full pl-4 pr-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white placeholder-gray-500 focus:outline-none focus:border-blue-500 transition-colors"
                        />
                      </div>

                      {/* Model */}
                      <div>
                          <p className="text-sm text-gray-400 mb-1">设备型号</p>
                          <input
                            type="text"
                            value={editingDevice.model || ''}
                            onChange={(e) => setEditingDevice({ ...editingDevice, model: e.target.value })}
                            className="w-full pl-4 pr-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white placeholder-gray-500 focus:outline-none focus:border-blue-500 transition-colors"
                          />
                      </div>

                      {/* Serial Number */}
                      <div>
                          <p className="text-sm text-gray-400 mb-1">序列号</p>
                          <input
                            type="text"
                            value={editingDevice.serialNumber || ''}
                            onChange={(e) => setEditingDevice({ ...editingDevice, serialNumber: e.target.value })}
                            className="w-full pl-4 pr-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white placeholder-gray-500 focus:outline-none focus:border-blue-500 transition-colors"
                          />
                      </div>

                      {/* Actions */}
                      <div className="pt-4 border-t border-white/10 space-y-3">
                        <button
                            onClick={() => {
                                // Save logic would go here
                                // For now just update state and close
                                if (selected3DFile && selected3DFile.devices) {
                                    const updatedDevices = selected3DFile.devices.map(d => 
                                        d.id === editingDevice.id ? editingDevice : d
                                    );
                                    setSelected3DFile({ ...selected3DFile, devices: updatedDevices });
                                    // Also update info panel selection if it matches
                                    if (selectedDevice?.id === editingDevice.id) {
                                        setSelectedDevice(editingDevice);
                                    }
                                }
                                setShowEditModal(false);
                                setEditingDevice(null);
                            }}
                            className="w-full flex items-center justify-center gap-2 px-4 py-3 bg-blue-500 hover:bg-blue-600 rounded-lg transition-colors text-white"
                        >
                            <Save className="w-4 h-4" />
                            <span>保存更改</span>
                        </button>
                      </div>
                    </div>
                  </div>
                </div>
              </div>
            </motion.div>
          </motion.div>
        )}
      </AnimatePresence>

      <DeviceInventoryModal 
        isOpen={showInventoryModal}
        onClose={() => setShowInventoryModal(false)}
      />

      {/* Upload File Modal */}
      <UploadFileModal
        show={showUploadModal}
        categories={categories.filter(c => c !== 'all')}
        appCode={currentCustomer?.appCode || ''}
        areas={areas}
        onClose={() => setShowUploadModal(false)}
        onSuccess={() => {
          setShowUploadModal(false);
          loadArchives();
        }}
      />

      {/* Delete Confirm Modal */}
      <DeleteConfirmModal
        show={showDeleteConfirm}
        archive={deletingArchive}
        onClose={() => {
          setShowDeleteConfirm(false);
          setDeletingArchive(null);
        }}
        onConfirm={async () => {
          if (deletingArchive) {
            try {
              const archiveId = parseInt(deletingArchive.id);
              if (!isNaN(archiveId)) {
                // 使用档案管理API删除档案
                const response = await archivesApi.deleteArchive(archiveId);
                if (response.code === 200) {
                  loadArchives();
                } else {
                  alert(response.message || '删除失败');
                }
              }
            } catch (err) {
              console.error('删除失败:', err);
              alert('删除失败，请稍后重试');
            }
          }
          setShowDeleteConfirm(false);
          setDeletingArchive(null);
        }}
      />
    </div>
  );
}

// ── 上传文件弹窗 ──────────────────────────────────────────────────────────────
function UploadFileModal({
  show,
  categories,
  appCode,
  areas,
  onClose,
  onSuccess,
}: {
  show: boolean;
  categories: string[];
  appCode: string;
  areas: Area[];
  onClose: () => void;
  onSuccess: () => void;
}) {
  const fileInputRef = useRef<HTMLInputElement>(null);
  const [dragOver, setDragOver] = useState(false);
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [fileName, setFileName] = useState('');
  const [category, setCategory] = useState('');
  const [areaName, setAreaName] = useState('');
  const [areaId, setAreaId] = useState('');
  const [uploading, setUploading] = useState(false);
  const [uploadDone, setUploadDone] = useState(false);
  const [uploadError, setUploadError] = useState<string | null>(null);

  // Flatten areas to count all nodes including children
  const flattenAreas = (areaList: Area[]): Area[] => {
    let result: Area[] = [];
    areaList.forEach(area => {
      result.push(area);
      if (area.children) {
        result = [...result, ...flattenAreas(area.children)];
      }
    });
    return result;
  };

  const totalAreaCount = flattenAreas(areas).length;

  const formatSize = (bytes: number) => {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  };

  const handleFile = useCallback((file: File) => {
    setSelectedFile(file);
    setFileName(file.name.replace(/\.[^/.]+$/, ''));
    setUploadDone(false);
    setUploadError(null);
  }, []);

  const handleDrop = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    setDragOver(false);
    const file = e.dataTransfer.files[0];
    if (file) handleFile(file);
  }, [handleFile]);

  const handleInputChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (file) handleFile(file);
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!selectedFile) return;
    setUploading(true);
    setUploadError(null);
    
    try {
      // 首先使用附件API上传文件
      const uploadResponse = await attachmentApi.uploadAttachment({
        file: selectedFile,
        module: 'archives',
        businessId: areaId ? parseInt(areaId) : undefined,
        name: fileName || selectedFile.name,
        remark: category || '其他',
        appCode: appCode, // 传递租户代码
      });

      if (uploadResponse.code === 200 && uploadResponse.data) {
        // 获取上传后的文件信息
        const uploadedFile = uploadResponse.data;
        const fileUrl = uploadedFile.fileUrl;
        const fileSize = uploadedFile.fileSize;
        const fileType = detectFileType(selectedFile);
        
        // 根据文件类型确定档案类型
        let archiveType = 'document';
        if (fileType === 'pdf') archiveType = 'pdf';
        else if (fileType === 'image') archiveType = 'photo';
        else if (fileType === 'blueprint') archiveType = 'floor_plan';
        
        // 使用档案管理API创建档案记录
        const archiveResponse = await archivesApi.createArchive({
          name: fileName || selectedFile.name,
          type: archiveType,
          category: category || '其他',
          areaId: areaId ? parseInt(areaId) : undefined,
          imageUrl: fileUrl,
          is3DModel: false,
          size: fileSize,
          appCode: appCode, // 传递租户代码
        });

        if (archiveResponse.code === 200) {
          setUploading(false);
          setUploadDone(true);
          await new Promise(r => setTimeout(r, 600));
          onSuccess();
        } else {
          setUploadError(archiveResponse.message || '创建档案记录失败');
          setUploading(false);
        }
      } else {
        setUploadError(uploadResponse.message || '上传文件失败');
        setUploading(false);
      }
    } catch (err: any) {
      setUploadError(err?.message || '上传失败，请稍后重试');
      setUploading(false);
    }
  };

  const handleClose = () => {
    if (uploading) return;
    setSelectedFile(null);
    setFileName('');
    setCategory('');
    setAreaName('');
    setAreaId('');
    setUploadDone(false);
    setUploadError(null);
    onClose();
  };

  const typeLabel: Record<string, string> = {
    pdf: 'PDF 文档',
    document: '文档',
    image: '图片',
    blueprint: '图纸',
  };

  const typeColor: Record<string, string> = {
    pdf: 'text-red-400 bg-red-500/10 border-red-500/30',
    document: 'text-green-400 bg-green-500/10 border-green-500/30',
    image: 'text-purple-400 bg-purple-500/10 border-purple-500/30',
    blueprint: 'text-cyan-400 bg-cyan-500/10 border-cyan-500/30',
  };

  if (!show) return null;

  return (
    <AnimatePresence>
      <motion.div
        initial={{ opacity: 0 }}
        animate={{ opacity: 1 }}
        exit={{ opacity: 0 }}
        className="fixed inset-0 bg-black/60 backdrop-blur-sm flex items-center justify-center z-50 p-4"
        onClick={handleClose}
      >
        <motion.div
          initial={{ scale: 0.92, y: 24 }}
          animate={{ scale: 1, y: 0 }}
          exit={{ scale: 0.92, y: 24 }}
          onClick={e => e.stopPropagation()}
          className="bg-gray-900 border border-white/10 rounded-2xl w-full max-w-lg shadow-2xl"
        >
          {/* Header */}
          <div className="flex items-center justify-between px-6 py-5 border-b border-white/10">
            <div className="flex items-center gap-3">
              <div className="p-2 bg-blue-500/20 rounded-lg">
                <CloudUpload className="w-5 h-5 text-blue-400" />
              </div>
              <h3 className="text-xl font-bold text-white">上传文件</h3>
            </div>
            <button
              onClick={handleClose}
              className="p-2 hover:bg-white/10 rounded-lg transition-colors"
            >
              <X className="w-5 h-5 text-gray-400" />
            </button>
          </div>

          {/* Scrollable Content */}
          <div className="max-h-[calc(100vh-200px)] overflow-y-auto">
            <form onSubmit={handleSubmit} className="p-6 space-y-5">
              {/* Drop Zone */}
              <div
                onClick={() => !selectedFile && fileInputRef.current?.click()}
                onDragOver={e => { e.preventDefault(); setDragOver(true); }}
                onDragLeave={() => setDragOver(false)}
                onDrop={handleDrop}
                className={`relative flex flex-col items-center justify-center gap-3 p-8 border-2 border-dashed rounded-xl transition-all cursor-pointer
                  ${dragOver
                    ? 'border-blue-400 bg-blue-500/10'
                    : selectedFile
                      ? 'border-green-500/50 bg-green-500/5'
                      : 'border-white/20 bg-white/3 hover:border-white/40 hover:bg-white/5'
                  }`}
              >
                <input
                  ref={fileInputRef}
                  type="file"
                  className="hidden"
                  onChange={handleInputChange}
                />

                {selectedFile ? (
                  <>
                    {/* File preview */}
                    <div className={`px-3 py-1 rounded-full text-xs font-medium border ${typeColor[detectFileType(selectedFile)] || 'text-gray-400 bg-gray-500/10 border-gray-500/30'}`}>
                      {typeLabel[detectFileType(selectedFile)] || '文件'}
                    </div>
                    <p className="text-white font-medium text-center break-all">{selectedFile.name}</p>
                    <p className="text-gray-400 text-sm">{formatSize(selectedFile.size)}</p>
                    <button
                      type="button"
                      onClick={e => { e.stopPropagation(); setSelectedFile(null); setFileName(''); }}
                      className="flex items-center gap-1 text-xs text-red-400 hover:text-red-300 mt-1"
                    >
                      <Trash2 className="w-3 h-3" /> 移除文件
                    </button>
                  </>
                ) : (
                  <>
                    <div className={`p-4 rounded-full transition-colors ${dragOver ? 'bg-blue-500/20' : 'bg-white/5'}`}>
                      <CloudUpload className={`w-8 h-8 ${dragOver ? 'text-blue-400' : 'text-gray-400'}`} />
                    </div>
                    <div className="text-center">
                      <p className="text-white font-medium">拖拽文件到此处，或 <span className="text-blue-400 underline">点击浏览</span></p>
                      <p className="text-gray-500 text-sm mt-1">支持 PDF、Word、Excel、图片、CAD 等格式</p>
                    </div>
                  </>
                )}
              </div>

              {/* File Name */}
              <div>
                <label className="flex items-center gap-2 text-sm font-medium text-gray-300 mb-2">
                  <FileType className="w-4 h-4 text-gray-400" />
                  文件名称 <span className="text-red-400">*</span>
                </label>
                <input
                  required
                  type="text"
                  value={fileName}
                  onChange={e => setFileName(e.target.value)}
                  placeholder="请输入文件显示名称"
                  className="w-full px-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white placeholder-gray-500 focus:outline-none focus:border-blue-500 transition-colors"
                />
              </div>

              {/* Category */}
              <div>
                <label className="flex items-center gap-2 text-sm font-medium text-gray-300 mb-2">
                  <Tag className="w-4 h-4 text-gray-400" />
                  文件分类 <span className="text-red-400">*</span>
                </label>
                <select
                  required
                  value={category}
                  onChange={e => setCategory(e.target.value)}
                  className="w-full px-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500 transition-colors [&>option]:bg-gray-800 [&>option]:text-white"
                >
                  <option value="">请选择分类</option>
                  {categories.map(cat => (
                    <option key={cat} value={cat}>{cat}</option>
                  ))}
                </select>
              </div>

              {/* Area */}
              <div>
                <label className="flex items-center gap-2 text-sm font-medium text-gray-300 mb-2">
                  <MapPin className="w-4 h-4 text-gray-400" />
                  所属区域 <span className="text-gray-500">（可选）</span>
                  {totalAreaCount > 0 && <span className="text-xs text-gray-500">- {totalAreaCount} 个区域可用</span>}
                </label>
                {totalAreaCount === 0 ? (
                  <div className="px-4 py-3 bg-amber-500/10 border border-amber-500/30 rounded-lg text-amber-400 text-sm">
                    当前客户暂无可用区域数据
                  </div>
                ) : (
                  <AreaTreeSelect
                    areas={areas}
                    value={areaName}
                    onChange={(name, id) => {
                      setAreaName(name);
                      setAreaId(id);
                    }}
                    placeholder="请选择区域"
                  />
                )}
              </div>

              {/* Detected type hint */}
              {selectedFile && (
                <div className="flex items-center gap-2 px-4 py-3 bg-white/5 rounded-lg border border-white/10">
                  <Check className="w-4 h-4 text-green-400 shrink-0" />
                  <span className="text-sm text-gray-300">
                    自动识别类型：<span className={`font-medium ${(typeColor[detectFileType(selectedFile)] || '').split(' ')[0]}`}>{typeLabel[detectFileType(selectedFile)]}</span>
                  </span>
                </div>
              )}

              {/* Error message */}
              {uploadError && (
                <div className="flex items-center gap-2 px-4 py-3 bg-red-500/10 rounded-lg border border-red-500/30">
                  <AlertCircle className="w-4 h-4 text-red-400 shrink-0" />
                  <span className="text-sm text-red-400">{uploadError}</span>
                </div>
              )}

              {/* Actions */}
              <div className="flex gap-3 pt-1">
                <button
                  type="button"
                  onClick={handleClose}
                  disabled={uploading}
                  className="flex-1 px-4 py-3 bg-white/5 hover:bg-white/10 border border-white/10 rounded-lg transition-colors disabled:opacity-50"
                >
                  取消
                </button>
                <button
                  type="submit"
                  disabled={!selectedFile || uploading}
                  className="flex-1 px-4 py-3 bg-gradient-to-r from-blue-500 to-purple-500 hover:from-blue-600 hover:to-purple-600 rounded-lg transition-all font-semibold disabled:opacity-50 disabled:cursor-not-allowed flex items-center justify-center gap-2"
                >
                  {uploading ? (
                    <>
                      <motion.div
                        animate={{ rotate: 360 }}
                        transition={{ repeat: Infinity, duration: 0.8, ease: 'linear' }}
                        className="w-4 h-4 border-2 border-white/30 border-t-white rounded-full"
                      />
                      <span>上传中...</span>
                    </>
                  ) : uploadDone ? (
                    <>
                      <Check className="w-4 h-4" />
                      <span>上传成功！</span>
                    </>
                  ) : (
                    <>
                      <Upload className="w-4 h-4" />
                      <span>确认上传</span>
                    </>
                  )}
                </button>
              </div>
            </form>
          </div>
        </motion.div>
      </motion.div>
    </AnimatePresence>
  );
}

// ── 删除确认弹窗 ──────────────────────────────────────────────────────────────
function DeleteConfirmModal({
  show,
  archive,
  onClose,
  onConfirm,
}: {
  show: boolean;
  archive: Archive | null;
  onClose: () => void;
  onConfirm: () => void;
}) {
  const handleClose = () => {
    onClose();
  };

  if (!show || !archive) return null;

  return (
    <AnimatePresence>
      <motion.div
        initial={{ opacity: 0 }}
        animate={{ opacity: 1 }}
        exit={{ opacity: 0 }}
        className="fixed inset-0 bg-black/60 backdrop-blur-sm flex items-center justify-center z-50 p-4"
        onClick={handleClose}
      >
        <motion.div
          initial={{ scale: 0.92, y: 24 }}
          animate={{ scale: 1, y: 0 }}
          exit={{ scale: 0.92, y: 24 }}
          onClick={e => e.stopPropagation()}
          className="bg-gray-900 border border-white/10 rounded-2xl w-full max-w-lg shadow-2xl"
        >
          {/* Header */}
          <div className="flex items-center justify-between px-6 py-5 border-b border-white/10">
            <div className="flex items-center gap-3">
              <div className="p-2 bg-red-500/20 rounded-lg">
                <Trash2 className="w-5 h-5 text-red-400" />
              </div>
              <h3 className="text-xl font-bold text-white">删除档案</h3>
            </div>
            <button
              onClick={handleClose}
              className="p-2 hover:bg-white/10 rounded-lg transition-colors"
            >
              <X className="w-5 h-5 text-gray-400" />
            </button>
          </div>

          {/* Scrollable Content */}
          <div className="max-h-[calc(100vh-200px)] overflow-y-auto">
            <form onSubmit={onConfirm} className="p-6 space-y-5">
              {/* File Name */}
              <div>
                <label className="flex items-center gap-2 text-sm font-medium text-gray-300 mb-2">
                  <FileType className="w-4 h-4 text-gray-400" />
                  文件名称 <span className="text-red-400">*</span>
                </label>
                <input
                  required
                  type="text"
                  value={archive.name}
                  readOnly
                  className="w-full px-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white placeholder-gray-500 focus:outline-none focus:border-blue-500 transition-colors"
                />
              </div>

              {/* Category */}
              <div>
                <label className="flex items-center gap-2 text-sm font-medium text-gray-300 mb-2">
                  <Tag className="w-4 h-4 text-gray-400" />
                  文件分类 <span className="text-red-400">*</span>
                </label>
                <input
                  required
                  type="text"
                  value={archive.category}
                  readOnly
                  className="w-full px-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white placeholder-gray-500 focus:outline-none focus:border-blue-500 transition-colors"
                />
              </div>

              {/* Area */}
              <div>
                <label className="flex items-center gap-2 text-sm font-medium text-gray-300 mb-2">
                  <MapPin className="w-4 h-4 text-gray-400" />
                  所属区域 <span className="text-gray-500">（可选）</span>
                </label>
                {archive.areaName ? (
                  <input
                    type="text"
                    value={archive.areaName}
                    readOnly
                    className="w-full px-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white placeholder-gray-500 focus:outline-none focus:border-blue-500 transition-colors"
                  />
                ) : (
                  <div className="px-4 py-3 bg-amber-500/10 border border-amber-500/30 rounded-lg text-amber-400 text-sm">
                    当前档案未关联区域
                  </div>
                )}
              </div>

              {/* Actions */}
              <div className="flex gap-3 pt-1">
                <button
                  type="button"
                  onClick={handleClose}
                  className="flex-1 px-4 py-3 bg-white/5 hover:bg-white/10 border border-white/10 rounded-lg transition-colors"
                >
                  取消
                </button>
                <button
                  type="submit"
                  className="flex-1 px-4 py-3 bg-red-500 hover:bg-red-600 rounded-lg transition-all font-semibold flex items-center justify-center gap-2"
                >
                  <Trash2 className="w-4 h-4" />
                  <span>确认删除</span>
                </button>
              </div>
            </form>
          </div>
        </motion.div>
      </motion.div>
    </AnimatePresence>
  );
}