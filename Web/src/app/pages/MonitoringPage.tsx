import { useState, useMemo, useEffect } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import { 
  ChevronRight,
  ChevronDown,
  Grid3x3,
  Play,
  Pause,
  Volume2,
  VolumeX,
  Settings,
  AlertCircle,
  Clock,
  Camera,
  X,
  Download,
  Calendar,
  Search,
  Check,
  Plus,
  Box,
} from 'lucide-react';
import { useAuth } from '@/app/contexts/AuthContext';
import { useArea } from '@/app/contexts/AreaContext';
import { PieChart, Pie, Cell } from 'recharts';
import { archivesData, Archive } from '@/app/data/archivesData';
import { DeviceMarker3D } from '@/app/types/3d-scene';
import { SceneViewer } from '@/app/components/3d-scene/SceneViewer';

interface Camera {
  id: string;
  name: string;
  areaId: string;
  imageUrl: string;
  status: 'online' | 'offline';
}

interface AlarmRecord {
  id: string;
  type: string;
  area: string;
  time: string;
  thumbnail: string;
}

export function MonitoringPage() {
  const { currentCustomer } = useAuth();
  const { areas, flattenAreas } = useArea();
  const [expandedNodes, setExpandedNodes] = useState<Set<string>>(new Set(['root']));
  const [selectedArea, setSelectedArea] = useState<string | null>(null);

  // 工具栏功能状态
  const [gridMode, setGridMode] = useState<'1x1' | '2x2' | '3x3' | '4x4'>('4x4');
  const [showGridModal, setShowGridModal] = useState(false);
  const [showControlModal, setShowControlModal] = useState(false);
  const [showScreenshotModal, setShowScreenshotModal] = useState(false);
  const [showPlaybackModal, setShowPlaybackModal] = useState(false);
  const [showSettingsModal, setShowSettingsModal] = useState(false);
  const [showAddCameraModal, setShowAddCameraModal] = useState(false);
  const [isPlaying, setIsPlaying] = useState(true);
  const [isMuted, setIsMuted] = useState(true);
  const [selectedCameras, setSelectedCameras] = useState<string[]>([]);
  const [playbackDate, setPlaybackDate] = useState(new Date().toISOString().split('T')[0]);
  const [playbackTime, setPlaybackTime] = useState('00:00');
  
  // 摄像头列表状态
  const [cameraList, setCameraList] = useState<Camera[]>([
    { id: 'cam-001', name: '主厨房-01', areaId: 'L3-003', imageUrl: 'https://images.unsplash.com/photo-1556909114-f6e7ad7d3136?w=400', status: 'online' },
    { id: 'cam-002', name: '主厨房-02', areaId: 'L3-003', imageUrl: 'https://images.unsplash.com/photo-1740803292814-13d2e35924c3?w=400', status: 'online' },
    { id: 'cam-003', name: '主厨房-03', areaId: 'L3-003', imageUrl: 'https://images.unsplash.com/photo-1574269909862-7e1d70bb8078?w=400', status: 'online' },
    { id: 'cam-004', name: '主厨房-04', areaId: 'L3-003', imageUrl: 'https://images.unsplash.com/photo-1721152531946-040f73b312e2?w=400', status: 'online' },
    { id: 'cam-005', name: '主厨房-05', areaId: 'L3-003', imageUrl: 'https://images.unsplash.com/photo-1556909114-f6e7ad7d3136?w=400', status: 'online' },
    { id: 'cam-006', name: '主厨房-06', areaId: 'L3-003', imageUrl: 'https://images.unsplash.com/photo-1740803292814-13d2e35924c3?w=400', status: 'online' },
    { id: 'cam-007', name: '主厨房-07', areaId: 'L3-003', imageUrl: 'https://images.unsplash.com/photo-1574269909862-7e1d70bb8078?w=400', status: 'online' },
    { id: 'cam-008', name: '主厨房-08', areaId: 'L3-003', imageUrl: 'https://images.unsplash.com/photo-1721152531946-040f73b312e2?w=400', status: 'online' },
    { id: 'cam-009', name: 'A区用餐-01', areaId: 'L3-001', imageUrl: 'https://images.unsplash.com/photo-1584308972272-9e4e7685e80f?w=400', status: 'online' },
    { id: 'cam-010', name: 'A区用餐-02', areaId: 'L3-001', imageUrl: 'https://images.unsplash.com/photo-1608403376217-1c256df9f15a?w=400', status: 'online' },
    { id: 'cam-011', name: 'A区用餐-03', areaId: 'L3-001', imageUrl: 'https://images.unsplash.com/photo-1584308972272-9e4e7685e80f?w=400', status: 'online' },
    { id: 'cam-012', name: 'A区用餐-04', areaId: 'L3-001', imageUrl: 'https://images.unsplash.com/photo-1608403376217-1c256df9f15a?w=400', status: 'online' },
    { id: 'cam-013', name: '冷藏区-01', areaId: 'L3-004', imageUrl: 'https://images.unsplash.com/photo-1574269909862-7e1d70bb8078?w=400', status: 'online' },
    { id: 'cam-014', name: '冷藏区-02', areaId: 'L3-004', imageUrl: 'https://images.unsplash.com/photo-1574269909862-7e1d70bb8078?w=400', status: 'online' },
    { id: 'cam-015', name: '冷藏区-03', areaId: 'L3-004', imageUrl: 'https://images.unsplash.com/photo-1574269909862-7e1d70bb8078?w=400', status: 'offline' },
    { id: 'cam-016', name: '冷藏区-04', areaId: 'L3-004', imageUrl: 'https://images.unsplash.com/photo-1574269909862-7e1d70bb8078?w=400', status: 'offline' },
  ]);
  
  // 新增摄像头表单
  const [newCameraForm, setNewCameraForm] = useState({
    name: '',
    areaId: '',
    rtspUrl: '',
  });

  // 3D场景和设备详情状态
  const [viewMode, setViewMode] = useState<'video' | '3d'>('video');
  const [selectedCADArchive, setSelectedCADArchive] = useState<Archive | null>(null);
  const [selectedDevice, setSelectedDevice] = useState<DeviceMarker3D | null>(null);
  const [showDeviceDetail, setShowDeviceDetail] = useState(false);

  // 模拟报警记录
  const alarmRecords: AlarmRecord[] = [
    { id: 'alarm-001', type: '打电话', area: '主厨房', time: '01-13 16:51', thumbnail: 'https://images.unsplash.com/photo-1763888786535-06dbeea05036?w=200' },
    { id: 'alarm-002', type: '烟雾超标异常', area: '主厨房', time: '02-09 17:23', thumbnail: 'https://images.unsplash.com/photo-1763888786535-06dbeea05036?w=200' },
    { id: 'alarm-003', type: '卫生间久留', area: '主厨房', time: '02-09 17:23', thumbnail: 'https://images.unsplash.com/photo-1608403376217-1c256df9f15a?w=200' },
    { id: 'alarm-004', type: '发霉区久留', area: '主厨房', time: '02-09 17:23', thumbnail: 'https://images.unsplash.com/photo-1608403376217-1c256df9f15a?w=200' },
    { id: 'alarm-005', type: '合规违停提醒', area: '主厨房', time: '02-09 17:23', thumbnail: 'https://images.unsplash.com/photo-1721152531946-040f73b312e2?w=200' },
  ];

  // 设备状态统计
  const onlineCount = cameraList.filter(c => c.status === 'online').length;
  const offlineCount = cameraList.filter(c => c.status === 'offline').length;
  const deviceStats = [
    { name: '在线数', value: onlineCount, color: '#00ff9d' },
    { name: '离线数', value: offlineCount, color: '#666' },
  ];

  // 构建区域树
  const areaTree = useMemo(() => {
    if (!currentCustomer || areas.length === 0) return [];
    
    // 只显示当前租户的区域
    const customerAreas = areas.filter(a => a.appCode === currentCustomer.appCode);
    
    return customerAreas;
  }, [areas, currentCustomer]);

  // 切换节点展开/收起
  const toggleNode = (nodeId: string) => {
    const newExpanded = new Set(expandedNodes);
    if (newExpanded.has(nodeId)) {
      newExpanded.delete(nodeId);
    } else {
      newExpanded.add(nodeId);
    }
    setExpandedNodes(newExpanded);
  };

  // 递归渲染区域树
  const renderAreaTree = (areaList: typeof areas, level = 0) => {
    return areaList.map((area) => {
      const hasChildren = area.children && area.children.length > 0;
      const isExpanded = expandedNodes.has(area.id);
      const isSelected = selectedArea === area.id;

      return (
        <div key={area.id}>
          <motion.div
            className={`flex items-center gap-2 px-2 py-1.5 rounded cursor-pointer transition-colors ${
              isSelected ? 'bg-blue-500/20 text-blue-400' : 'hover:bg-white/5 text-gray-300'
            }`}
            style={{ paddingLeft: `${level * 16 + 8}px` }}
            onClick={() => {
              setSelectedArea(area.id);
              if (hasChildren) {
                toggleNode(area.id);
              }
            }}
          >
            {hasChildren ? (
              isExpanded ? (
                <ChevronDown className="w-4 h-4 flex-shrink-0" />
              ) : (
                <ChevronRight className="w-4 h-4 flex-shrink-0" />
              )
            ) : (
              <div className="w-4" />
            )}
            
            {/* 区域类型图标 */}
            {area.type === 'level1' && <div className="w-2 h-2 rounded-full bg-purple-500 flex-shrink-0" />}
            {area.type === 'level2' && <div className="w-2 h-2 rounded-full bg-yellow-500 flex-shrink-0" />}
            {area.type === 'level3' && <div className="w-2 h-2 rounded-full bg-blue-500 flex-shrink-0" />}
            
            <span className="text-sm truncate">{area.name}</span>
          </motion.div>

          {hasChildren && isExpanded && (
            <motion.div
              initial={{ height: 0, opacity: 0 }}
              animate={{ height: 'auto', opacity: 1 }}
              exit={{ height: 0, opacity: 0 }}
            >
              {renderAreaTree(area.children || [], level + 1)}
            </motion.div>
          )}
        </div>
      );
    });
  };

  // 过滤摄像头
  const filteredCameras = useMemo(() => {
    if (!selectedArea) return cameraList;
    
    // 获取选中域及其所有子区域的ID
    const getAreaAndDescendantIds = (areaId: string): string[] => {
      const flat = flattenAreas(areas);
      const area = flat.find(a => a.id === areaId);
      if (!area) return [areaId];
      
      const ids = [areaId];
      const collectIds = (children: typeof areas) => {
        children.forEach(child => {
          ids.push(child.id);
          if (child.children) {
            collectIds(child.children);
          }
        });
      };
      
      if (area.children) {
        collectIds(area.children);
      }
      
      return ids;
    };
    
    const areaIds = getAreaAndDescendantIds(selectedArea);
    return cameraList.filter(c => areaIds.includes(c.areaId));
  }, [selectedArea, cameraList, areas, flattenAreas]);

  // 显示的摄像头（最多16个）
  const displayCameras = filteredCameras.slice(0, 16);

  // 根据区域查找CAD档案
  const findCADArchiveByArea = (areaId: string | null, areaName: string | null): Archive | null => {
    if (!areaId && !areaName) return null;

    const cadArchives = archivesData.filter(archive => 
      archive.category === '图纸资料' &&
      archive.type === 'blueprint' &&
      archive.is3DModel === true &&
      archive.sceneConfig
    );

    // 优先按areaId匹配
    if (areaId) {
      const matchedByAreaId = cadArchives.find(archive => archive.areaId === areaId);
      if (matchedByAreaId) return matchedByAreaId;
    }

    // 其次按areaName匹配
    if (areaName) {
      const matchedByAreaName = cadArchives.find(archive => archive.areaName === areaName);
      if (matchedByAreaName) return matchedByAreaName;
    }

    return null;
  };

  // 监听区域选择变化，查找对应CAD档案
  useEffect(() => {
    const selectedAreaData = flattenAreas(areas).find(a => a.id === selectedArea);
    if (selectedAreaData) {
      const cadArchive = findCADArchiveByArea(selectedAreaData.id, selectedAreaData.name);
      setSelectedCADArchive(cadArchive);
      
      // 如果找到CAD档案且有3D配置，自动切换到3D视图
      if (cadArchive && cadArchive.sceneConfig) {
        setViewMode('3d');
      } else {
        setViewMode('video');
      }
    } else {
      setSelectedCADArchive(null);
      setViewMode('video');
      setSelectedDevice(null);
      setShowDeviceDetail(false);
    }
  }, [selectedArea, areas, flattenAreas]);

  // 视图模式切换处理
  const handleViewModeToggle = () => {
    setViewMode(prev => prev === 'video' ? '3d' : 'video');
  };

  // 设备点击处理
  const handleDeviceClick = (deviceId: string, deviceData: any) => {
    setSelectedDevice(deviceData);
    setShowDeviceDetail(true);
  };

  return (
    <div className="h-screen flex flex-col bg-[#0a0e1a] overflow-hidden">
      {/* 主内容区域 */}
      <div className="flex-1 flex gap-2 p-2 overflow-hidden">
        {/* 左侧面板 */}
        <div className="w-56 flex flex-col gap-2 overflow-hidden">
          {/* 区域导航 */}
          <div className="bg-gradient-to-br from-[#1a1f35] to-[#0f1321] rounded-lg border border-blue-500/30 p-3 overflow-hidden flex flex-col">
            <h3 className="text-sm font-bold text-white mb-2 flex-shrink-0">区域导航</h3>
            <div className="flex-1 overflow-y-auto custom-scrollbar">
              {areaTree.length > 0 ? renderAreaTree(areaTree) : (
                <p className="text-xs text-gray-500 text-center py-4">暂无区域数据</p>
              )}
            </div>
          </div>

          {/* 点击区域同步切换 */}
          <div className="bg-gradient-to-br from-[#1a1f35] to-[#0f1321] rounded-lg border border-blue-500/30 p-3">
            <h3 className="text-sm font-bold text-white mb-2">点击区域同步切换</h3>
            <div className="relative aspect-square bg-blue-900/20 rounded border border-blue-500/30 overflow-hidden">
              <img
                src="https://images.unsplash.com/photo-1721244654346-9be0c0129e36?w=400"
                alt="区域平面图"
                className="w-full h-full object-cover opacity-60"
              />
              {/* 区域标记点 */}
              <div className="absolute top-1/4 left-1/4 w-3 h-3 bg-yellow-400 rounded-full animate-pulse" />
              <div className="absolute top-1/3 right-1/3 w-3 h-3 bg-green-400 rounded-full animate-pulse" />
              <div className="absolute bottom-1/3 left-1/2 w-3 h-3 bg-blue-400 rounded-full animate-pulse" />
            </div>
          </div>

          {/* 设备状态 */}
          <div className="bg-gradient-to-br from-[#1a1f35] to-[#0f1321] rounded-lg border border-blue-500/30 p-3">
            <h3 className="text-sm font-bold text-white mb-2">设备状态</h3>
            <div className="flex items-center justify-center">
              <div className="relative w-32 h-32">
                <PieChart width={128} height={128}>
                  <Pie
                    data={deviceStats}
                    cx={64}
                    cy={64}
                    innerRadius={35}
                    outerRadius={55}
                    paddingAngle={2}
                    dataKey="value"
                  >
                    {deviceStats.map((entry, index) => (
                      <Cell key={`cell-${index}`} fill={entry.color} />
                    ))}
                  </Pie>
                </PieChart>
                <div className="absolute inset-0 flex flex-col items-center justify-center">
                  <div className="text-lg font-bold text-white">{onlineCount}</div>
                  <div className="text-xs text-gray-400">在线数</div>
                  <div className="text-xs text-gray-500 mt-1">
                    ({((onlineCount / cameraList.length) * 100).toFixed(0)}%)
                  </div>
                </div>
              </div>
            </div>
            <div className="mt-2 flex items-center justify-between text-xs">
              <div className="flex items-center gap-1">
                <div className="w-2 h-2 rounded-full bg-[#00ff9d]" />
                <span className="text-gray-400">在线数</span>
                <span className="text-white font-bold">{onlineCount}</span>
              </div>
              <div className="flex items-center gap-1">
                <div className="w-2 h-2 rounded-full bg-[#666]" />
                <span className="text-gray-400">离线数</span>
                <span className="text-white font-bold">{offlineCount}</span>
              </div>
            </div>
          </div>
        </div>

        {/* 中间面板 - 场景区域图 */}
        <div className="flex-1 flex flex-col gap-2 overflow-hidden">
          <div className="flex-1 bg-gradient-to-br from-[#1a1f35] to-[#0f1321] rounded-lg border border-blue-500/30 p-3 overflow-hidden flex flex-col">
            <div className="flex items-center justify-between mb-2 flex-shrink-0">
              <h3 className="text-sm font-bold text-white">
                {viewMode === 'video' ? '场景区域图' : `3D场景 - ${selectedCADArchive?.name || '未知'}`}
              </h3>
              <button
                onClick={handleViewModeToggle}
                className={`px-3 py-1 rounded text-xs transition-colors ${
                  viewMode === '3d'
                    ? 'bg-cyan-500/20 text-cyan-400 border border-cyan-500/40'
                    : 'bg-blue-500/20 text-blue-400 border border-blue-500/40'
                }`}
              >
                {viewMode === 'video' ? '切换到3D场景' : '返回视频监控'}
              </button>
            </div>
            
            <AnimatePresence mode="wait">
            {/* 视频监控视图 */}
            {viewMode === 'video' && (
              <motion.div
                initial={{ opacity: 0, x: -20 }}
                animate={{ opacity: 1, x: 0 }}
                exit={{ opacity: 0, x: 20 }}
                transition={{ duration: 0.3 }}
                className="flex-1"
              >
            {/* 摄像头网格 */}
            <div className="h-full grid grid-cols-4 grid-rows-4 gap-1 overflow-hidden">
              {displayCameras.map((camera, index) => (
                <div
                  key={camera.id}
                  className="relative bg-black rounded overflow-hidden border border-white/10 group"
                >
                  <img
                    src={camera.imageUrl}
                    alt={camera.name}
                    className="w-full h-full object-cover"
                  />
                  
                  {/* 叠加层 */}
                  <div className="absolute inset-0 bg-gradient-to-t from-black/80 via-transparent to-black/40" />
                  
                  {/* 摄像头信息 */}
                  <div className="absolute top-1 left-1 bg-black/60 px-1.5 py-0.5 rounded text-[10px] text-white">
                    {camera.name}
                  </div>
                  
                  {/* 状态指示器 */}
                  <div className="absolute top-1 right-1">
                    <div className={`w-2 h-2 rounded-full ${
                      camera.status === 'online' ? 'bg-green-500' : 'bg-red-500'
                    }`} />
                  </div>
                  
                  {/* 时间戳 */}
                  <div className="absolute bottom-1 right-1 bg-black/60 px-1.5 py-0.5 rounded text-[9px] text-white/70">
                    {new Date().toLocaleTimeString('zh-CN', { hour12: false })}
                  </div>
                </div>
              ))}
              
              {/* 填充空白格子 */}
              {Array.from({ length: 16 - displayCameras.length }).map((_, index) => (
                <div
                  key={`empty-${index}`}
                  className="bg-black/30 rounded border border-white/5 flex items-center justify-center"
                >
                  <span className="text-gray-600 text-xs">无信号</span>
                </div>
              ))}
            </div>
              </motion.div>
            )}
            </AnimatePresence>

            {/* 3D场景视图 */}
            {viewMode === '3d' && selectedCADArchive?.sceneConfig && (
              <motion.div
                initial={{ opacity: 0, scale: 0.95 }}
                animate={{ opacity: 1, scale: 1 }}
                exit={{ opacity: 0, scale: 0.95 }}
                transition={{ duration: 0.3 }}
                className="flex-1 relative"
              >
                <SceneViewer
                  sceneConfig={selectedCADArchive.sceneConfig}
                  onDeviceClick={handleDeviceClick}
                  onHoverDevice={(deviceId, deviceData) => {
                    // 处理设备悬停
                  }}
                  className="w-full h-full"
                />
              </motion.div>
            )}

            {/* 无CAD档案提示 */}
            {viewMode === '3d' && !selectedCADArchive?.sceneConfig && (
              <motion.div
                initial={{ opacity: 0, y: 20 }}
                animate={{ opacity: 1, y: 0 }}
                exit={{ opacity: 0, y: -20 }}
                transition={{ duration: 0.3 }}
                className="flex-1 flex items-center justify-center"
              >
                <div className="text-center">
                  <Box className="w-16 h-16 mx-auto mb-4 text-cyan-400/50" />
                  <div className="text-gray-400 text-sm mb-2">当前区域暂无3D场景</div>
                  <div className="text-gray-500 text-xs">请先在档案管理中上传该区域的CAD图纸</div>
                </div>
              </motion.div>
            )}
          </div>

          {/* 底部工具栏 */}
          <div className="h-12 bg-gradient-to-br from-[#1a1f35] to-[#0f1321] rounded-lg border border-blue-500/30 px-4 flex items-center justify-center gap-6 flex-shrink-0">
            <button 
              onClick={() => setShowGridModal(true)}
              className="flex items-center gap-2 px-3 py-1.5 bg-blue-500/20 hover:bg-blue-500/30 border border-blue-500/40 rounded text-xs text-blue-300 transition-colors"
            >
              <Grid3x3 className="w-3 h-3" />
              <span>分屏切换</span>
            </button>
            <button 
              onClick={() => setShowControlModal(true)}
              className="flex items-center gap-2 px-3 py-1.5 bg-blue-500/20 hover:bg-blue-500/30 border border-blue-500/40 rounded text-xs text-blue-300 transition-colors"
            >
              <Play className="w-3 h-3" />
              <span>画面控制</span>
            </button>
            <button 
              onClick={() => setShowScreenshotModal(true)}
              className="flex items-center gap-2 px-3 py-1.5 bg-blue-500/20 hover:bg-blue-500/30 border border-blue-500/40 rounded text-xs text-blue-300 transition-colors"
            >
              <Camera className="w-3 h-3" />
              <span>截图取证</span>
            </button>
            <button 
              onClick={() => setShowPlaybackModal(true)}
              className="flex items-center gap-2 px-3 py-1.5 bg-blue-500/20 hover:bg-blue-500/30 border border-blue-500/40 rounded text-xs text-blue-300 transition-colors"
            >
              <Clock className="w-3 h-3" />
              <span>回放查询</span>
            </button>
            <button 
              onClick={() => setShowSettingsModal(true)}
              className="flex items-center gap-2 px-3 py-1.5 bg-blue-500/20 hover:bg-blue-500/30 border border-blue-500/40 rounded text-xs text-blue-300 transition-colors"
            >
              <Settings className="w-3 h-3" />
              <span>系统设置</span>
            </button>
            <button 
              onClick={() => setShowAddCameraModal(true)}
              className="flex items-center gap-2 px-3 py-1.5 bg-blue-500/20 hover:bg-blue-500/30 border border-blue-500/40 rounded text-xs text-blue-300 transition-colors"
            >
              <Plus className="w-3 h-3" />
              <span>添加摄像头</span>
            </button>
          </div>
        </div>

        {/* 右侧面板 - 实时报警 */}
        <div className="w-64 bg-gradient-to-br from-[#1a1f35] to-[#0f1321] rounded-lg border border-blue-500/30 p-3 flex flex-col overflow-hidden">
          <h3 className="text-sm font-bold text-white mb-3 flex-shrink-0">实时报警</h3>
          
          {/* 统计卡片 */}
          <div className="grid grid-cols-2 gap-2 mb-3 flex-shrink-0">
            <div className="bg-gradient-to-br from-red-500/20 to-red-600/10 border border-red-500/30 rounded-lg p-2">
              <div className="flex items-center gap-1 mb-1">
                <AlertCircle className="w-3 h-3 text-red-400" />
                <span className="text-[10px] text-red-300">今日异常次数</span>
              </div>
              <div className="text-xl font-bold text-white">28</div>
            </div>
            <div className="bg-gradient-to-br from-blue-500/20 to-blue-600/10 border border-blue-500/30 rounded-lg p-2">
              <div className="flex items-center gap-1 mb-1">
                <AlertCircle className="w-3 h-3 text-blue-400" />
                <span className="text-[10px] text-blue-300">累计报警次数</span>
              </div>
              <div className="text-xl font-bold text-white">5286</div>
            </div>
          </div>

          {/* 报警列表 */}
          <div className="flex-1 overflow-y-auto custom-scrollbar space-y-2">
            {alarmRecords.map((alarm) => (
              <div
                key={alarm.id}
                className="bg-white/5 border border-white/10 rounded-lg p-2 hover:bg-white/10 transition-colors cursor-pointer"
              >
                <div className="flex gap-2">
                  <img
                    src={alarm.thumbnail}
                    alt={alarm.type}
                    className="w-20 h-14 rounded object-cover flex-shrink-0"
                  />
                  <div className="flex-1 min-w-0">
                    <div className="text-xs font-medium text-white truncate mb-0.5">
                      事件类型：{alarm.type}
                    </div>
                    <div className="text-[10px] text-gray-400 mb-0.5">
                      所属区域：{alarm.area}
                    </div>
                    <div className="text-[10px] text-gray-500">
                      事件时间：{alarm.time}
                    </div>
                  </div>
                </div>
              </div>
            ))}
          </div>

          {/* 查看全部按钮 */}
          <button className="mt-3 w-full py-2 bg-blue-500/20 hover:bg-blue-500/30 border border-blue-500/40 rounded text-xs text-blue-300 transition-colors flex-shrink-0">
            查看全部报警信息
          </button>
        </div>
        {/* 设备详情面板 */}
        {showDeviceDetail && selectedDevice && (
          <div className="w-64 bg-gradient-to-br from-[#1a1f35] to-[#0f1321] rounded-lg border border-cyan-500/30 p-3 flex flex-col overflow-hidden">
            <div className="flex items-center justify-between mb-3 flex-shrink-0">
              <h3 className="text-sm font-bold text-white">设备详情</h3>
              <button
                onClick={() => setShowDeviceDetail(false)}
                className="p-1 hover:bg-white/10 rounded-lg transition-colors"
              >
                <X className="w-4 h-4 text-gray-400" />
              </button>
            </div>

            {/* 设备信息 */}
            <div className="flex-1 overflow-y-auto custom-scrollbar space-y-3">
              {/* 设备名称 */}
              <div>
                <p className="text-xs text-gray-400 mb-1">设备名称</p>
                <p className="text-white font-medium">{selectedDevice.name || '未知'}</p>
              </div>

              {/* 设备类型 */}
              <div>
                <p className="text-xs text-gray-400 mb-1">设备类型</p>
                <p className="text-white">{selectedDevice.type || '未知'}</p>
              </div>

              {/* 设备型号 */}
              {selectedDevice.model && (
                <div>
                  <p className="text-xs text-gray-400 mb-1">设备型号</p>
                  <p className="text-white">{selectedDevice.model}</p>
                </div>
              )}

              {/* 序列号 */}
              {selectedDevice.serialNumber && (
                <div>
                  <p className="text-xs text-gray-400 mb-1">序列号</p>
                  <p className="text-white font-mono text-sm">{selectedDevice.serialNumber}</p>
                </div>
              )}

              {/* 关联传感器 */}
              {selectedDevice.sensors && selectedDevice.sensors.length > 0 && (
                <div>
                  <p className="text-xs text-gray-400 mb-1">关联传感器</p>
                  <div className="space-y-1">
                    {selectedDevice.sensors.map((sensor, index) => (
                      <div
                        key={index}
                        className="bg-white/5 border border-white/10 rounded p-2"
                      >
                        <p className="text-xs text-white">{sensor.name || '未知传感器'}</p>
                        {sensor.value !== undefined && (
                          <p className="text-[10px] text-gray-400">数值: {sensor.value}</p>
                        )}
                        {sensor.status && (
                          <div className="flex items-center gap-1 mt-1">
                            <div className={`w-2 h-2 rounded-full ${
                              sensor.status === 'normal' ? 'bg-green-500' : 'bg-red-500'
                            }`} />
                            <span className="text-[10px] text-gray-500">{sensor.status}</span>
                          </div>
                        )}
                      </div>
                    ))}
                  </div>
                </div>
              )}
            </div>

            {/* 操作按钮 */}
            <div className="mt-3 space-y-2 flex-shrink-0">
              <button
                onClick={() => setShowDeviceDetail(false)}
                className="w-full py-2 bg-cyan-500/20 hover:bg-cyan-500/30 border border-cyan-500/40 rounded text-xs text-cyan-300 transition-colors"
              >
                查看完整设备信息
              </button>
            </div>
          </div>
        )}
      </div>

      {/* 1. 分屏切换弹窗 */}
      <AnimatePresence>
        {showGridModal && (
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            className="fixed inset-0 bg-black/60 backdrop-blur-sm flex items-center justify-center z-50 p-4"
            onClick={() => setShowGridModal(false)}
          >
            <motion.div
              initial={{ scale: 0.9, y: 20 }}
              animate={{ scale: 1, y: 0 }}
              exit={{ scale: 0.9, y: 20 }}
              onClick={(e) => e.stopPropagation()}
              className="bg-gradient-to-br from-[#1a1f35] to-[#0f1321] border border-blue-500/30 rounded-xl max-w-md w-full p-6"
            >
              <div className="flex items-center justify-between mb-6">
                <h3 className="text-xl font-bold text-white">分屏切换</h3>
                <button
                  onClick={() => setShowGridModal(false)}
                  className="p-2 hover:bg-white/10 rounded-lg transition-colors"
                >
                  <X className="w-5 h-5 text-gray-400" />
                </button>
              </div>

              <div className="grid grid-cols-2 gap-4">
                {(['1x1', '2x2', '3x3', '4x4'] as const).map((mode) => (
                  <button
                    key={mode}
                    onClick={() => {
                      setGridMode(mode);
                      setShowGridModal(false);
                    }}
                    className={`p-4 rounded-lg border-2 transition-all ${
                      gridMode === mode
                        ? 'border-blue-500 bg-blue-500/20'
                        : 'border-white/10 bg-white/5 hover:bg-white/10'
                    }`}
                  >
                    <div className="flex flex-col items-center gap-2">
                      <Grid3x3 className="w-8 h-8 text-white" />
                      <span className="text-white font-medium">{mode}</span>
                      {gridMode === mode && <Check className="w-4 h-4 text-blue-400" />}
                    </div>
                  </button>
                ))}
              </div>
            </motion.div>
          </motion.div>
        )}
      </AnimatePresence>

      {/* 2. 画面控制弹窗 */}
      <AnimatePresence>
        {showControlModal && (
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            className="fixed inset-0 bg-black/60 backdrop-blur-sm flex items-center justify-center z-50 p-4"
            onClick={() => setShowControlModal(false)}
          >
            <motion.div
              initial={{ scale: 0.9, y: 20 }}
              animate={{ scale: 1, y: 0 }}
              exit={{ scale: 0.9, y: 20 }}
              onClick={(e) => e.stopPropagation()}
              className="bg-gradient-to-br from-[#1a1f35] to-[#0f1321] border border-blue-500/30 rounded-xl max-w-md w-full p-6"
            >
              <div className="flex items-center justify-between mb-6">
                <h3 className="text-xl font-bold text-white">画面控制</h3>
                <button
                  onClick={() => setShowControlModal(false)}
                  className="p-2 hover:bg-white/10 rounded-lg transition-colors"
                >
                  <X className="w-5 h-5 text-gray-400" />
                </button>
              </div>

              <div className="space-y-4">
                <div className="flex items-center justify-between p-4 bg-white/5 rounded-lg">
                  <span className="text-white">播放/暂停</span>
                  <button
                    onClick={() => setIsPlaying(!isPlaying)}
                    className="p-2 bg-blue-500/20 hover:bg-blue-500/30 border border-blue-500/40 rounded-lg transition-colors"
                  >
                    {isPlaying ? (
                      <Pause className="w-5 h-5 text-blue-300" />
                    ) : (
                      <Play className="w-5 h-5 text-blue-300" />
                    )}
                  </button>
                </div>

                <div className="flex items-center justify-between p-4 bg-white/5 rounded-lg">
                  <span className="text-white">音量控制</span>
                  <button
                    onClick={() => setIsMuted(!isMuted)}
                    className="p-2 bg-blue-500/20 hover:bg-blue-500/30 border border-blue-500/40 rounded-lg transition-colors"
                  >
                    {isMuted ? (
                      <VolumeX className="w-5 h-5 text-blue-300" />
                    ) : (
                      <Volume2 className="w-5 h-5 text-blue-300" />
                    )}
                  </button>
                </div>

                <div className="p-4 bg-white/5 rounded-lg">
                  <p className="text-sm text-gray-400">
                    当前状态：{isPlaying ? '播放中' : '已暂停'} / {isMuted ? '静音' : '有声音'}
                  </p>
                </div>
              </div>
            </motion.div>
          </motion.div>
        )}
      </AnimatePresence>

      {/* 3. 截图取证弹窗 */}
      <AnimatePresence>
        {showScreenshotModal && (
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            className="fixed inset-0 bg-black/60 backdrop-blur-sm flex items-center justify-center z-50 p-4"
            onClick={() => setShowScreenshotModal(false)}
          >
            <motion.div
              initial={{ scale: 0.9, y: 20 }}
              animate={{ scale: 1, y: 0 }}
              exit={{ scale: 0.9, y: 20 }}
              onClick={(e) => e.stopPropagation()}
              className="bg-gradient-to-br from-[#1a1f35] to-[#0f1321] border border-blue-500/30 rounded-xl max-w-2xl w-full p-6"
            >
              <div className="flex items-center justify-between mb-6">
                <h3 className="text-xl font-bold text-white">截图取证</h3>
                <button
                  onClick={() => setShowScreenshotModal(false)}
                  className="p-2 hover:bg-white/10 rounded-lg transition-colors"
                >
                  <X className="w-5 h-5 text-gray-400" />
                </button>
              </div>

              <div className="space-y-4">
                <p className="text-sm text-gray-400">选择要截图的摄像头（可多选）</p>
                
                <div className="grid grid-cols-2 gap-3 max-h-96 overflow-y-auto custom-scrollbar">
                  {displayCameras.map((camera) => (
                    <div
                      key={camera.id}
                      onClick={() => {
                        setSelectedCameras((prev) =>
                          prev.includes(camera.id)
                            ? prev.filter((id) => id !== camera.id)
                            : [...prev, camera.id]
                        );
                      }}
                      className={`relative p-2 rounded-lg cursor-pointer transition-all ${
                        selectedCameras.includes(camera.id)
                          ? 'bg-blue-500/20 border-2 border-blue-500'
                          : 'bg-white/5 border-2 border-white/10 hover:bg-white/10'
                      }`}
                    >
                      <img
                        src={camera.imageUrl}
                        alt={camera.name}
                        className="w-full aspect-video object-cover rounded mb-2"
                      />
                      <p className="text-xs text-white">{camera.name}</p>
                      {selectedCameras.includes(camera.id) && (
                        <div className="absolute top-4 right-4 w-6 h-6 bg-blue-500 rounded-full flex items-center justify-center">
                          <Check className="w-4 h-4 text-white" />
                        </div>
                      )}
                    </div>
                  ))}
                </div>

                <div className="flex gap-3 pt-4">
                  <button
                    onClick={() => {
                      setShowScreenshotModal(false);
                      setSelectedCameras([]);
                    }}
                    className="flex-1 px-6 py-3 bg-white/5 hover:bg-white/10 border border-white/10 rounded-lg transition-colors text-white"
                  >
                    取消
                  </button>
                  <button
                    onClick={() => {
                      alert(`已截取 ${selectedCameras.length} 个摄像头的画面`);
                      setShowScreenshotModal(false);
                      setSelectedCameras([]);
                    }}
                    disabled={selectedCameras.length === 0}
                    className="flex-1 px-6 py-3 bg-gradient-to-r from-blue-500 to-purple-500 hover:from-blue-600 hover:to-purple-600 rounded-lg transition-all font-semibold disabled:opacity-50 disabled:cursor-not-allowed text-white flex items-center justify-center gap-2"
                  >
                    <Download className="w-4 h-4" />
                    <span>截图 ({selectedCameras.length})</span>
                  </button>
                </div>
              </div>
            </motion.div>
          </motion.div>
        )}
      </AnimatePresence>

      {/* 4. 回放查询弹窗 */}
      <AnimatePresence>
        {showPlaybackModal && (
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            className="fixed inset-0 bg-black/60 backdrop-blur-sm flex items-center justify-center z-50 p-4"
            onClick={() => setShowPlaybackModal(false)}
          >
            <motion.div
              initial={{ scale: 0.9, y: 20 }}
              animate={{ scale: 1, y: 0 }}
              exit={{ scale: 0.9, y: 20 }}
              onClick={(e) => e.stopPropagation()}
              className="bg-gradient-to-br from-[#1a1f35] to-[#0f1321] border border-blue-500/30 rounded-xl max-w-md w-full p-6"
            >
              <div className="flex items-center justify-between mb-6">
                <h3 className="text-xl font-bold text-white">回放查询</h3>
                <button
                  onClick={() => setShowPlaybackModal(false)}
                  className="p-2 hover:bg-white/10 rounded-lg transition-colors"
                >
                  <X className="w-5 h-5 text-gray-400" />
                </button>
              </div>

              <div className="space-y-4">
                <div>
                  <label className="block text-sm font-medium text-gray-300 mb-2">
                    选择日期
                  </label>
                  <div className="relative">
                    <input
                      type="date"
                      value={playbackDate}
                      onChange={(e) => setPlaybackDate(e.target.value)}
                      className="w-full px-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500 transition-colors"
                    />
                    <Calendar className="absolute right-3 top-3 w-5 h-5 text-gray-400 pointer-events-none" />
                  </div>
                </div>

                <div>
                  <label className="block text-sm font-medium text-gray-300 mb-2">
                    选择时间
                  </label>
                  <div className="relative">
                    <input
                      type="time"
                      value={playbackTime}
                      onChange={(e) => setPlaybackTime(e.target.value)}
                      className="w-full px-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500 transition-colors"
                    />
                    <Clock className="absolute right-3 top-3 w-5 h-5 text-gray-400 pointer-events-none" />
                  </div>
                </div>

                <div>
                  <label className="block text-sm font-medium text-gray-300 mb-2">
                    选择摄像头
                  </label>
                  <select className="w-full px-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500 transition-colors [&>option]:bg-gray-800 [&>option]:text-white">
                    <option value="">全部摄像头</option>
                    {displayCameras.map((camera) => (
                      <option key={camera.id} value={camera.id} className="bg-gray-800 text-white">
                        {camera.name}
                      </option>
                    ))}
                  </select>
                </div>

                <div className="flex gap-3 pt-4">
                  <button
                    onClick={() => setShowPlaybackModal(false)}
                    className="flex-1 px-6 py-3 bg-white/5 hover:bg-white/10 border border-white/10 rounded-lg transition-colors text-white"
                  >
                    取消
                  </button>
                  <button
                    onClick={() => {
                      alert(`查询 ${playbackDate} ${playbackTime} 的回放录像`);
                      setShowPlaybackModal(false);
                    }}
                    className="flex-1 px-6 py-3 bg-gradient-to-r from-blue-500 to-purple-500 hover:from-blue-600 hover:to-purple-600 rounded-lg transition-all font-semibold text-white flex items-center justify-center gap-2"
                  >
                    <Search className="w-4 h-4" />
                    <span>查询</span>
                  </button>
                </div>
              </div>
            </motion.div>
          </motion.div>
        )}
      </AnimatePresence>

      {/* 5. 系统设置弹窗 */}
      <AnimatePresence>
        {showSettingsModal && (
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            className="fixed inset-0 bg-black/60 backdrop-blur-sm flex items-center justify-center z-50 p-4"
            onClick={() => setShowSettingsModal(false)}
          >
            <motion.div
              initial={{ scale: 0.9, y: 20 }}
              animate={{ scale: 1, y: 0 }}
              exit={{ scale: 0.9, y: 20 }}
              onClick={(e) => e.stopPropagation()}
              className="bg-gradient-to-br from-[#1a1f35] to-[#0f1321] border border-blue-500/30 rounded-xl max-w-md w-full p-6"
            >
              <div className="flex items-center justify-between mb-6">
                <h3 className="text-xl font-bold text-white">系统设置</h3>
                <button
                  onClick={() => setShowSettingsModal(false)}
                  className="p-2 hover:bg-white/10 rounded-lg transition-colors"
                >
                  <X className="w-5 h-5 text-gray-400" />
                </button>
              </div>

              <div className="space-y-4">
                <div className="p-4 bg-white/5 rounded-lg">
                  <label className="flex items-center justify-between">
                    <span className="text-white">自动录像</span>
                    <input type="checkbox" defaultChecked className="w-5 h-5" />
                  </label>
                </div>

                <div className="p-4 bg-white/5 rounded-lg">
                  <label className="flex items-center justify-between">
                    <span className="text-white">运动检测</span>
                    <input type="checkbox" defaultChecked className="w-5 h-5" />
                  </label>
                </div>

                <div className="p-4 bg-white/5 rounded-lg">
                  <label className="flex items-center justify-between">
                    <span className="text-white">声音告警</span>
                    <input type="checkbox" className="w-5 h-5" />
                  </label>
                </div>

                <div className="p-4 bg-white/5 rounded-lg">
                  <label className="block text-white mb-2">录像保存天数</label>
                  <select className="w-full px-4 py-2 bg-white/5 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500 [&>option]:bg-gray-800 [&>option]:text-white">
                    <option value="7" className="bg-gray-800 text-white">7天</option>
                    <option value="15" className="bg-gray-800 text-white">15天</option>
                    <option value="30" selected className="bg-gray-800 text-white">30天</option>
                    <option value="60" className="bg-gray-800 text-white">60天</option>
                  </select>
                </div>

                <div className="flex gap-3 pt-4">
                  <button
                    onClick={() => setShowSettingsModal(false)}
                    className="flex-1 px-6 py-3 bg-white/5 hover:bg-white/10 border border-white/10 rounded-lg transition-colors text-white"
                  >
                    取消
                  </button>
                  <button
                    onClick={() => {
                      alert('设置已保存');
                      setShowSettingsModal(false);
                    }}
                    className="flex-1 px-6 py-3 bg-gradient-to-r from-blue-500 to-purple-500 hover:from-blue-600 hover:to-purple-600 rounded-lg transition-all font-semibold text-white"
                  >
                    保存
                  </button>
                </div>
              </div>
            </motion.div>
          </motion.div>
        )}
      </AnimatePresence>

      {/* 6. 添加摄像头弹窗 */}
      <AnimatePresence>
        {showAddCameraModal && (
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            className="fixed inset-0 bg-black/60 backdrop-blur-sm flex items-center justify-center z-50 p-4"
            onClick={() => setShowAddCameraModal(false)}
          >
            <motion.div
              initial={{ scale: 0.9, y: 20 }}
              animate={{ scale: 1, y: 0 }}
              exit={{ scale: 0.9, y: 20 }}
              onClick={(e) => e.stopPropagation()}
              className="bg-gradient-to-br from-[#1a1f35] to-[#0f1321] border border-blue-500/30 rounded-xl max-w-md w-full p-6"
            >
              <div className="flex items-center justify-between mb-6">
                <h3 className="text-xl font-bold text-white">添加摄像头</h3>
                <button
                  onClick={() => setShowAddCameraModal(false)}
                  className="p-2 hover:bg-white/10 rounded-lg transition-colors"
                >
                  <X className="w-5 h-5 text-gray-400" />
                </button>
              </div>

              <div className="space-y-4">
                <div>
                  <label className="block text-sm font-medium text-gray-300 mb-2">
                    摄像头名称
                  </label>
                  <input
                    type="text"
                    value={newCameraForm.name}
                    onChange={(e) => setNewCameraForm({ ...newCameraForm, name: e.target.value })}
                    className="w-full px-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500 transition-colors"
                  />
                </div>

                <div>
                  <label className="block text-sm font-medium text-gray-300 mb-2">
                    所属区域
                  </label>
                  <select
                    value={newCameraForm.areaId}
                    onChange={(e) => setNewCameraForm({ ...newCameraForm, areaId: e.target.value })}
                    className="w-full px-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500 transition-colors [&>option]:bg-gray-800 [&>option]:text-white"
                  >
                    <option value="">请选择区域</option>
                    {areas.map((area) => (
                      <option key={area.id} value={area.id} className="bg-gray-800 text-white">
                        {area.name}
                      </option>
                    ))}
                  </select>
                </div>

                <div>
                  <label className="block text-sm font-medium text-gray-300 mb-2">
                    RTSP URL
                  </label>
                  <input
                    type="text"
                    value={newCameraForm.rtspUrl}
                    onChange={(e) => setNewCameraForm({ ...newCameraForm, rtspUrl: e.target.value })}
                    className="w-full px-4 py-3 bg-white/5 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500 transition-colors"
                  />
                </div>

                <div className="flex gap-3 pt-4">
                  <button
                    onClick={() => setShowAddCameraModal(false)}
                    className="flex-1 px-6 py-3 bg-white/5 hover:bg-white/10 border border-white/10 rounded-lg transition-colors text-white"
                  >
                    取消
                  </button>
                  <button
                    onClick={() => {
                      if (newCameraForm.name && newCameraForm.areaId && newCameraForm.rtspUrl) {
                        const newCamera: Camera = {
                          id: `cam-${cameraList.length + 1}`,
                          name: newCameraForm.name,
                          areaId: newCameraForm.areaId,
                          imageUrl: 'https://images.unsplash.com/photo-1556909114-f6e7ad7d3136?w=400',
                          status: 'online',
                        };
                        setCameraList([...cameraList, newCamera]);
                        alert('摄像头已添加');
                        setShowAddCameraModal(false);
                        setNewCameraForm({ name: '', areaId: '', rtspUrl: '' });
                      } else {
                        alert('请填写完整信息');
                      }
                    }}
                    className="flex-1 px-6 py-3 bg-gradient-to-r from-blue-500 to-purple-500 hover:from-blue-600 hover:to-purple-600 rounded-lg transition-all font-semibold text-white flex items-center justify-center gap-2"
                  >
                    <Plus className="w-4 h-4" />
                    <span>添加</span>
                  </button>
                </div>
              </div>
            </motion.div>
          </motion.div>
        )}
      </AnimatePresence>

      <style>{`
        .custom-scrollbar::-webkit-scrollbar {
          width: 4px;
          height: 4px;
        }
        .custom-scrollbar::-webkit-scrollbar-track {
          background: rgba(255, 255, 255, 0.05);
          border-radius: 2px;
        }
        .custom-scrollbar::-webkit-scrollbar-thumb {
          background: rgba(59, 130, 246, 0.5);
          border-radius: 2px;
        }
        .custom-scrollbar::-webkit-scrollbar-thumb:hover {
          background: rgba(59, 130, 246, 0.7);
        }
      `}</style>
    </div>
  );
}