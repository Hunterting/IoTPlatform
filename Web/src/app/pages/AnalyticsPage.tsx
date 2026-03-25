import { useState, useMemo } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import {
  ChevronRight, ChevronDown, MapPin, Droplets, Zap, Flame,
  AlertTriangle, Bell, Clock, TrendingUp, Calendar, LogOut, Cpu,
} from 'lucide-react';
import {
  BarChart, Bar, LineChart, Line, XAxis, YAxis, CartesianGrid,
  Tooltip, ResponsiveContainer, Legend,
} from 'recharts';
import { useAuth } from '@/app/contexts/AuthContext';
import { useDevices } from '@/app/contexts/DeviceContext';
import { useArea } from '@/app/contexts/AreaContext';
import { SHARED_ALERT_RECORDS } from '@/app/config/sharedMockData';
import { SceneViewer } from '@/app/components/3d-scene/SceneViewer';
import { DeviceInfoPanel } from '@/app/components/3d-scene/DeviceInfoPanel';

type EnergyType = 'electric' | 'water' | 'gas';

// ── Mock Data ──────────────────────────────────────────────────────────────────
// 模拟用能数据 - 日历视图（以电为例）
const dailyElectricData = [
  { day: '1', week: '一', value: 1.06 }, { day: '2', week: '二', value: 1.12 }, { day: '3', week: '三', value: 0.98 },
  { day: '4', week: '四', value: 1.36 }, { day: '5', week: '五', value: 0.9 }, { day: '6', week: '六', value: 1.05 }, { day: '7', week: '日', value: 0.7 },
  { day: '8', week: '一', value: 1.30 }, { day: '9', week: '二', value: 0.9 }, { day: '10', week: '三', value: 0.98 },
  { day: '11', week: '四', value: 1.36 }, { day: '12', week: '五', value: 0.9 }, { day: '13', week: '六', value: 1.42 }, { day: '14', week: '日', value: 0.7 },
  { day: '15', week: '一', value: 1.36 }, { day: '16', week: '二', value: 0.9 }, { day: '17', week: '三', value: 1.20 }, { day: '18', week: '四', value: 0.9 },
  { day: '19', week: '五', value: 1.36 }, { day: '20', week: '六', value: 0.7 }, { day: '21', week: '日', value: 1.42 },
  { day: '22', week: '一', value: 1.20 }, { day: '23', week: '二', value: 0.9 }, { day: '24', week: '三', value: 0.9 }, { day: '25', week: '四', value: 1.36 },
  { day: '26', week: '五', value: 0.9 }, { day: '27', week: '六', value: 1.45 }, { day: '28', week: '日', value: 0.7 },
];

const dailyWaterData = dailyElectricData.map(d => ({ ...d, value: (parseFloat(d.value.toFixed(2)) * 850).toFixed(0) }));
const dailyGasData = dailyElectricData.map(d => ({ ...d, value: (parseFloat(d.value.toFixed(2)) * 62).toFixed(0) }));

// 月度趋势 - 柱状图
const monthlyElectricTrend = [
  { month: '1月', value: 4200 }, { month: '2月', value: 3800 }, { month: '3月', value: 5100 },
  { month: '4月', value: 4500 }, { month: '5月', value: 4800 }, { month: '6月', value: 3900 },
  { month: '7月', value: 4200 }, { month: '8月', value: 3600 }, { month: '9月', value: 4800 },
  { month: '10月', value: 5300 }, { month: '11月', value: 4900 }, { month: '12月', value: 5200 },
];

const monthlyWaterTrend = monthlyElectricTrend.map(m => ({ ...m, value: m.value * 85 }));
const monthlyGasTrend = monthlyElectricTrend.map(m => ({ ...m, value: Math.round(m.value * 6.2) }));

// 年度趋势 - 折线图
const yearlyElectricTrend = [
  { year: '2021年', value: 48000 }, { year: '2022年', value: 52000 },
  { year: '2023年', value: 55000 }, { year: '2024年', value: 58000 }, { year: '2025年', value: 62000 },
];

const yearlyWaterTrend = yearlyElectricTrend.map(y => ({ ...y, value: y.value * 85 }));
const yearlyGasTrend = yearlyElectricTrend.map(y => ({ ...y, value: Math.round(y.value * 6.2) }));

// ── Area Tree Component ────────────────────────────────────────────────────────
interface AreaTreeNodeProps {
  area: any;
  level: number;
  selectedArea: string | null;
  onSelect: (areaName: string) => void;
}

function AreaTreeNode({ area, level, selectedArea, onSelect }: AreaTreeNodeProps) {
  const [expanded, setExpanded] = useState(level === 0);
  const hasChildren = area.children && area.children.length > 0;
  const isSelected = selectedArea === area.name;

  return (
    <div>
      <div
        onClick={() => {
          if (hasChildren) setExpanded(!expanded);
          onSelect(area.name);
        }}
        className={`flex items-center gap-2 py-2 px-3 rounded-lg cursor-pointer transition-all ${
          isSelected
            ? 'bg-cyan-500/20 border border-cyan-500/50'
            : 'hover:bg-white/5 border border-transparent'
        }`}
        style={{ paddingLeft: `${level * 16 + 12}px` }}
      >
        {hasChildren && (
          <motion.div
            animate={{ rotate: expanded ? 90 : 0 }}
            transition={{ duration: 0.2 }}
          >
            <ChevronRight className="w-3 h-3 text-cyan-400" />
          </motion.div>
        )}
        {!hasChildren && <div className="w-3" />}
        <MapPin className={`w-3 h-3 ${isSelected ? 'text-cyan-400' : 'text-gray-500'}`} />
        <span className={`text-xs ${isSelected ? 'text-cyan-300 font-medium' : 'text-gray-400'}`}>
          {area.name}
        </span>
      </div>
      <AnimatePresence>
        {expanded && hasChildren && (
          <motion.div
            initial={{ height: 0, opacity: 0 }}
            animate={{ height: 'auto', opacity: 1 }}
            exit={{ height: 0, opacity: 0 }}
            transition={{ duration: 0.2 }}
          >
            {area.children.map((child: any) => (
              <AreaTreeNode
                key={child.id}
                area={child}
                level={level + 1}
                selectedArea={selectedArea}
                onSelect={onSelect}
              />
            ))}
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}

// ── Main Component ─────────────────────────────────────────────────────────────
export function AnalyticsPage() {
  const { user, logout, currentCustomer } = useAuth();
  const { devices, updateDeviceStatus } = useDevices();
  const { areas } = useArea();
  const [selectedArea, setSelectedArea] = useState<string | null>(null);
  const [selectedEnergyType, setSelectedEnergyType] = useState<EnergyType>('electric');
  const [viewMode, setViewMode] = useState<'day' | 'month' | 'year'>('day');
  const [selectedYear, setSelectedYear] = useState<number>(2026);
  const [selectedMonth, setSelectedMonth] = useState<number>(1);
  const [showYearPicker, setShowYearPicker] = useState(false);
  const [showMonthPicker, setShowMonthPicker] = useState(false);
  const [showYearOnlyPicker, setShowYearOnlyPicker] = useState(false);
  
  // 3D scene states
  const [selectedDeviceId, setSelectedDeviceId] = useState<string | null>(null);
  const [selected3DDevice, setSelected3DDevice] = useState<any>(null);
  const [showDeviceInfo, setShowDeviceInfo] = useState(false);

  // 可选年份列表（2020-2030）
  const availableYears = Array.from({ length: 11 }, (_, i) => 2020 + i);
  
  // 可选月份列表
  const availableMonths = Array.from({ length: 12 }, (_, i) => i + 1);
  
  // 根据选中的区域获取3D场景配置
  const currentSceneConfig = useMemo(() => {
    if (!selectedArea) return null;
    
    // 查找匹配的区域
    const matchingArea = areas.find((area: any) => area.name === selectedArea);
    if (!matchingArea) return null;
    
    // 返回3D场景配置（这里使用mock配置，实际应从archivesData获取）
    return {
      areaId: matchingArea.id,
      areaName: selectedArea,
      cadFilePath: '/assets/cad/default.dxf',
      devices: [
        {
          id: 'device-1',
          deviceId: '1',
          name: '咖啡机-E01',
          position: { x: -3, y: 0, z: -1 },
          color: 0x00c3ff,
        },
        {
          id: 'device-2',
          deviceId: '2',
          name: '蒸箱-F01',
          position: { x: 3, y: 0, z: 2 },
          color: 0x00c3ff,
        },
        {
          id: 'device-3',
          deviceId: '3',
          name: '新风机-AQ01',
          position: { x: -5, y: 0, z: -3 },
          color: 0x00c3ff,
        },
      ],
      camera: {
        position: [12, 10, 12],
        lookAt: [0, 0, 0],
      },
      backgroundColor: 0x0a1628,
    };
  }, [selectedArea, areas]);

  // 处理设备点击
  const handleDeviceClick = (deviceId: string, deviceData: any) => {
    setSelectedDeviceId(deviceId);
    setSelected3DDevice(deviceData);
    setShowDeviceInfo(true);
  };

  // 处理设备悬停
  const handleHoverDevice = (deviceId: string | null, deviceData?: any) => {
    // 可以用于高亮设备或显示悬停提示
    console.log('Hovered device:', deviceId);
  };

  // 处理设备状态切换
  const handleTogglePower = async (deviceId: string, status: 'online' | 'offline') => {
    try {
      await updateDeviceStatus(deviceId, status);
    } catch (error) {
      console.error('设备状态更新失败:', error);
      throw error;
    }
  };

  // 关闭设备信息面板
  const closeDeviceInfo = () => {
    setShowDeviceInfo(false);
    setSelectedDeviceId(null);
    setSelected3DDevice(null);
  };

  // 统计设备状态
  const deviceStats = useMemo(() => {
    const total = devices.length;
    const online = devices.filter(d => d.status === 'online').length;
    const warning = devices.filter(d => d.status === 'warning').length;
    const offline = devices.filter(d => d.status === 'offline').length;
    
    return { total, online, warning, offline };
  }, [devices]);

  // 用能数据汇总（模拟）
  const energyStats = useMemo(() => {
    return {
      water: { current: 1400, target: 1500 },
      electric: { current: 120, target: 150 },
      gas: { current: 75, target: 90 },
    };
  }, []);

  // 获取告警记录
  const alerts = useMemo(() => {
    return SHARED_ALERT_RECORDS.filter(a => a.status !== 'resolved').slice(0, 5);
  }, []);

  // 获取图表数据
  const chartData = useMemo(() => {
    if (viewMode === 'day') {
      if (selectedEnergyType === 'electric') return dailyElectricData;
      if (selectedEnergyType === 'water') return dailyWaterData;
      return dailyGasData;
    } else if (viewMode === 'month') {
      if (selectedEnergyType === 'electric') return monthlyElectricTrend;
      if (selectedEnergyType === 'water') return monthlyWaterTrend;
      return monthlyGasTrend;
    } else {
      if (selectedEnergyType === 'electric') return yearlyElectricTrend;
      if (selectedEnergyType === 'water') return yearlyWaterTrend;
      return yearlyGasTrend;
    }
  }, [viewMode, selectedEnergyType]);

  // 单位配置
  const unitConfig = {
    electric: { unit: 'kWh', label: '电' },
    water: { unit: 'm³', label: '水' },
    gas: { unit: 'm³', label: '气' },
  };

  const currentUnit = unitConfig[selectedEnergyType];

  return (
    <div className="relative w-full h-full overflow-hidden bg-gradient-to-br from-[#0a1628] via-[#0d1b2a] to-[#0a1628] flex flex-col">
      {/* 背景装饰 */}
      <div className="absolute inset-0 opacity-30">
        <div className="absolute top-0 left-0 w-96 h-96 bg-cyan-500/10 rounded-full blur-3xl" />
        <div className="absolute bottom-0 right-0 w-96 h-96 bg-blue-500/10 rounded-full blur-3xl" />
      </div>

      {/* Main Content */}
      <div className="relative z-10 flex gap-4 px-4 pt-4 flex-1 min-h-0 pb-4">
        {/* Left Panel - 区域导航 & 设备状态 */}
        <div className="w-56 flex flex-col gap-4 h-full">
          {/* 区域导航 */}
          <div className="bg-black/40 backdrop-blur-sm border border-cyan-500/30 rounded-lg p-4 flex-1 overflow-hidden flex flex-col">
            <div className="flex items-center gap-2 mb-3 pb-3 border-b border-cyan-500/20">
              <MapPin className="w-4 h-4 text-cyan-400" />
              <h3 className="text-sm font-bold text-cyan-300">区域导航</h3>
            </div>
            <div className="flex-1 overflow-y-auto space-y-1 scrollbar-thin scrollbar-thumb-cyan-500/50 scrollbar-track-transparent">
              {areas.map((area) => (
                <AreaTreeNode
                  key={area.id}
                  area={area}
                  level={0}
                  selectedArea={selectedArea}
                  onSelect={setSelectedArea}
                />
              ))}
            </div>
          </div>

          {/* 设备状态 */}
          <div className="bg-black/40 backdrop-blur-sm border border-cyan-500/30 rounded-lg p-4 flex-shrink-0">
            <h3 className="text-sm font-bold text-cyan-300 mb-3">设备状态</h3>
            <div className="text-center mb-4">
              <p className="text-3xl font-bold text-white mb-1">{deviceStats.total}</p>
              <p className="text-xs text-gray-400">设备总数</p>
            </div>
            <div className="grid grid-cols-3 gap-2 text-center">
              <div>
                <p className="text-xl font-bold text-green-400">{deviceStats.online}</p>
                <p className="text-xs text-gray-400">在线设备</p>
              </div>
              <div>
                <p className="text-xl font-bold text-yellow-400">{deviceStats.warning}</p>
                <p className="text-xs text-gray-400">警告设备</p>
              </div>
              <div>
                <p className="text-xl font-bold text-red-400">{deviceStats.offline}</p>
                <p className="text-xs text-gray-400">离线设备</p>
              </div>
            </div>
          </div>
        </div>

        {/* Center Panel - 3D场景 & 用能卡片 */}
        <div className="flex-1 flex flex-col gap-4">
          {/* 用能卡片 */}
          <div className="grid grid-cols-3 gap-4">
            {/* 用水量 */}
            <motion.div
              whileHover={{ scale: 1.02 }}
              className="bg-black/40 backdrop-blur-sm border border-cyan-500/30 rounded-lg p-4 relative overflow-hidden"
            >
              <div className="absolute top-0 right-0 w-24 h-24 bg-cyan-500/10 rounded-full blur-2xl" />
              <div className="relative flex items-center gap-3 mb-2">
                <div className="p-2 bg-cyan-500/20 rounded-lg">
                  <Droplets className="w-6 h-6 text-cyan-400" />
                </div>
                <div>
                  <p className="text-xs text-gray-400">用水量</p>
                  <p className="text-2xl font-bold text-white">{energyStats.water.current}<span className="text-sm text-gray-400">m³</span></p>
                </div>
              </div>
              <p className="text-xs text-cyan-400">当日 {energyStats.water.current}m³</p>
              <p className="text-xs text-gray-500">累计 {energyStats.water.target}m³</p>
            </motion.div>

            {/* 用电量 */}
            <motion.div
              whileHover={{ scale: 1.02 }}
              className="bg-black/40 backdrop-blur-sm border border-yellow-500/30 rounded-lg p-4 relative overflow-hidden"
            >
              <div className="absolute top-0 right-0 w-24 h-24 bg-yellow-500/10 rounded-full blur-2xl" />
              <div className="relative flex items-center gap-3 mb-2">
                <div className="p-2 bg-yellow-500/20 rounded-lg">
                  <Zap className="w-6 h-6 text-yellow-400" />
                </div>
                <div>
                  <p className="text-xs text-gray-400">用电量</p>
                  <p className="text-2xl font-bold text-white">{energyStats.electric.current}<span className="text-sm text-gray-400">kWh</span></p>
                </div>
              </div>
              <p className="text-xs text-yellow-400">当日 {energyStats.electric.current}kWh</p>
              <p className="text-xs text-gray-500">累计 {energyStats.electric.target}kWh</p>
            </motion.div>

            {/* 用气量 */}
            <motion.div
              whileHover={{ scale: 1.02 }}
              className="bg-black/40 backdrop-blur-sm border border-orange-500/30 rounded-lg p-4 relative overflow-hidden"
            >
              <div className="absolute top-0 right-0 w-24 h-24 bg-orange-500/10 rounded-full blur-2xl" />
              <div className="relative flex items-center gap-3 mb-2">
                <div className="p-2 bg-orange-500/20 rounded-lg">
                  <Flame className="w-6 h-6 text-orange-400" />
                </div>
                <div>
                  <p className="text-xs text-gray-400">用气量</p>
                  <p className="text-2xl font-bold text-white">{energyStats.gas.current}<span className="text-sm text-gray-400">m³</span></p>
                </div>
              </div>
              <p className="text-xs text-orange-400">当日 {energyStats.gas.current}m³</p>
              <p className="text-xs text-gray-500">累计 {energyStats.gas.target}m³</p>
            </motion.div>
          </div>

          {/* 3D 场景 */}
          <div className="flex-1 bg-black/40 backdrop-blur-sm border border-cyan-500/30 rounded-lg p-4 relative overflow-hidden">
            {!currentSceneConfig ? (
              <div className="absolute inset-0 flex items-center justify-center">
                <div className="text-center">
                  <MapPin className="w-16 h-16 text-gray-600 mx-auto mb-4" />
                  <p className="text-gray-400 text-sm">请从左侧区域导航中选择一个区域</p>
                  <p className="text-gray-500 text-xs mt-2">查看该区域的3D场景和设备标注</p>
                </div>
              </div>
            ) : (
              <div className="w-full h-full flex flex-col">
                {/* 3D Scene Header */}
                <div className="flex items-center justify-between mb-3 pb-2 border-b border-cyan-500/20">
                  <div className="flex items-center gap-2">
                    <Cpu className="w-4 h-4 text-cyan-400" />
                    <h3 className="text-sm font-bold text-cyan-300">{currentSceneConfig.areaName}</h3>
                  </div>
                  <span className="text-xs text-gray-400">
                    {currentSceneConfig.devices.length} 个设备标注
                  </span>
                </div>

                {/* 3D Scene View */}
                <div className="flex-1 relative bg-gray-900/50 rounded-lg border border-white/10 overflow-hidden">
                  {/* SceneViewer */}
                  <SceneViewer
                    sceneConfig={currentSceneConfig}
                    onDeviceClick={handleDeviceClick}
                    onHoverDevice={handleHoverDevice}
                    className="w-full h-full"
                  />

                  {/* Device Info Panel */}
                  <AnimatePresence>
                    {showDeviceInfo && selectedDeviceId && selected3DDevice && (
                      <DeviceInfoPanel
                        device={selected3DDevice}
                        onClose={closeDeviceInfo}
                        onTogglePower={handleTogglePower}
                        className="absolute right-0 top-0 bottom-0"
                      />
                    )}
                  </AnimatePresence>
                </div>
              </div>
            )}
          </div>
        </div>

        {/* Right Panel - 预警汇总 */}
        <div className="w-80 bg-black/40 backdrop-blur-sm border border-cyan-500/30 rounded-lg p-4 overflow-hidden flex flex-col">
          <div className="flex items-center gap-2 mb-3 pb-3 border-b border-cyan-500/20">
            <Bell className="w-4 h-4 text-yellow-400" />
            <h3 className="text-sm font-bold text-cyan-300">预警汇总</h3>
          </div>
          <div className="flex-1 overflow-y-auto space-y-2 scrollbar-thin scrollbar-thumb-cyan-500/50 scrollbar-track-transparent">
            {alerts.map((alert) => (
              <motion.div
                key={alert.id}
                initial={{ opacity: 0, x: 20 }}
                animate={{ opacity: 1, x: 0 }}
                className="p-3 rounded-lg border border-white/10 hover:border-cyan-500/50 transition-all bg-black/20"
              >
                <div className="flex items-start justify-between mb-2">
                  <div className="flex items-center gap-2">
                    <div className={`w-2 h-2 rounded-full ${
                      alert.level === 'critical' ? 'bg-red-500' :
                      alert.level === 'warning' ? 'bg-yellow-500' : 'bg-blue-500'
                    }`} />
                    <span className="text-xs font-medium text-white">{alert.alertType}</span>
                  </div>
                  <Clock className="w-3 h-3 text-gray-500" />
                </div>
                <p className="text-xs text-gray-400 mb-1">{alert.deviceName}</p>
                <p className="text-xs text-gray-500 truncate">{alert.description}</p>
                <p className="text-xs text-cyan-400 mt-2">{alert.triggerTime?.split(' ')[1] || alert.triggerTime || '-'}</p>
              </motion.div>
            ))}
          </div>
        </div>
      </div>

      {/* Bottom Panel - 用能分析 */}
      <div className="relative z-10 bg-black/60 backdrop-blur-md border-t border-cyan-500/30 p-4 flex-shrink-0">
        <div className="flex items-center justify-between mb-4">
          <h3 className="text-base font-bold text-white">用能分析</h3>
          {/* 能源类型切换 */}
          <div className="flex bg-black/40 rounded-lg p-1 border border-cyan-500/30 gap-1">
            <button
              onClick={() => setSelectedEnergyType('electric')}
              className={`px-6 py-1.5 rounded text-sm transition-all ${
                selectedEnergyType === 'electric'
                  ? 'bg-cyan-500 text-white'
                  : 'text-gray-400 hover:text-gray-300'
              }`}
            >
              电
            </button>
            <button
              onClick={() => setSelectedEnergyType('water')}
              className={`px-6 py-1.5 rounded text-sm transition-all ${
                selectedEnergyType === 'water'
                  ? 'bg-cyan-500 text-white'
                  : 'text-gray-400 hover:text-gray-300'
              }`}
            >
              水
            </button>
            <button
              onClick={() => setSelectedEnergyType('gas')}
              className={`px-6 py-1.5 rounded text-sm transition-all ${
                selectedEnergyType === 'gas'
                  ? 'bg-cyan-500 text-white'
                  : 'text-gray-400 hover:text-gray-300'
              }`}
            >
              气
            </button>
          </div>
        </div>

        {/* Charts */}
        <div className="flex gap-4 h-56">
          {/* 左侧 - 日历视图 */}
          <div className="w-[340px] flex flex-col">
            <div className="flex items-center justify-between mb-2">
              <span className="text-xs text-gray-400">单位：{currentUnit.unit}</span>
              <div className="relative">
                <button
                  onClick={() => {
                    setShowYearPicker(!showYearPicker);
                    setShowMonthPicker(false);
                    setShowYearOnlyPicker(false);
                  }}
                  className="text-xs text-cyan-400 hover:text-cyan-300 transition-colors flex items-center gap-1"
                >
                  {selectedYear}年{selectedMonth}月
                  <ChevronDown className="w-3 h-3" />
                </button>
                
                {/* 年月选择器下拉面板 */}
                <AnimatePresence>
                  {showYearPicker && (
                    <motion.div
                      initial={{ opacity: 0, y: -10 }}
                      animate={{ opacity: 1, y: 0 }}
                      exit={{ opacity: 0, y: -10 }}
                      className="absolute top-full right-0 mt-1 bg-black/95 border border-cyan-500/30 rounded-lg p-3 z-50 backdrop-blur-md"
                    >
                      <div className="flex gap-3">
                        {/* 年份选择 */}
                        <div className="w-24">
                          <p className="text-xs text-gray-400 mb-2">年份</p>
                          <div className="max-h-48 overflow-y-auto space-y-1 scrollbar-thin scrollbar-thumb-cyan-500/50 scrollbar-track-transparent">
                            {availableYears.map(year => (
                              <button
                                key={year}
                                onClick={() => setSelectedYear(year)}
                                className={`w-full px-3 py-1.5 rounded text-xs transition-all ${
                                  selectedYear === year
                                    ? 'bg-cyan-500 text-white'
                                    : 'text-gray-400 hover:bg-cyan-500/20 hover:text-cyan-300'
                                }`}
                              >
                                {year}年
                              </button>
                            ))}
                          </div>
                        </div>
                        
                        {/* 月份选择 */}
                        <div className="w-32">
                          <p className="text-xs text-gray-400 mb-2">月份</p>
                          <div className="grid grid-cols-3 gap-1">
                            {availableMonths.map(month => (
                              <button
                                key={month}
                                onClick={() => {
                                  setSelectedMonth(month);
                                  setShowYearPicker(false);
                                }}
                                className={`px-2 py-1.5 rounded text-xs transition-all ${
                                  selectedMonth === month
                                    ? 'bg-cyan-500 text-white'
                                    : 'text-gray-400 hover:bg-cyan-500/20 hover:text-cyan-300'
                                }`}
                              >
                                {month}月
                              </button>
                            ))}
                          </div>
                        </div>
                      </div>
                    </motion.div>
                  )}
                </AnimatePresence>
              </div>
            </div>
            <div className="flex-1 bg-black/40 border border-cyan-500/20 rounded-lg p-3">
              <div className="grid grid-cols-7 gap-1 text-xs h-full">
                {/* 星期标题 */}
                <div className="text-center text-gray-500 flex items-center justify-center">日</div>
                <div className="text-center text-gray-500 flex items-center justify-center">一</div>
                <div className="text-center text-gray-500 flex items-center justify-center">二</div>
                <div className="text-center text-gray-500 flex items-center justify-center">三</div>
                <div className="text-center text-gray-500 flex items-center justify-center">四</div>
                <div className="text-center text-gray-500 flex items-center justify-center">五</div>
                <div className="text-center text-gray-500 flex items-center justify-center">六</div>
                
                {/* 日期网格 */}
                {dailyElectricData.map((item: any, idx: number) => {
                  const value = selectedEnergyType === 'electric' 
                    ? dailyElectricData[idx].value 
                    : selectedEnergyType === 'water'
                    ? dailyWaterData[idx].value
                    : dailyGasData[idx].value;
                  
                  return (
                    <div
                      key={idx}
                      className="bg-gradient-to-br from-cyan-500/10 to-cyan-500/5 border border-cyan-500/20 rounded flex flex-col items-center justify-center hover:bg-cyan-500/20 transition-all cursor-pointer"
                    >
                      <span className="text-[10px] text-gray-400">{item.day}</span>
                      <span className="text-[11px] text-cyan-300 font-medium">{value}</span>
                    </div>
                  );
                })}
              </div>
            </div>
          </div>

          {/* 中间 - 月度柱状图 */}
          <div className="flex-1 flex flex-col">
            <div className="flex items-center justify-between mb-2">
              <span className="text-xs text-gray-400">单位：{currentUnit.unit}</span>
              <div className="relative">
                <button
                  onClick={() => {
                    setShowMonthPicker(!showMonthPicker);
                    setShowYearPicker(false);
                    setShowYearOnlyPicker(false);
                  }}
                  className="text-xs text-cyan-400 hover:text-cyan-300 transition-colors flex items-center gap-1"
                >
                  {selectedYear}年
                  <ChevronDown className="w-3 h-3" />
                </button>
                
                {/* 年份选择器下拉面板 */}
                <AnimatePresence>
                  {showMonthPicker && (
                    <motion.div
                      initial={{ opacity: 0, y: -10 }}
                      animate={{ opacity: 1, y: 0 }}
                      exit={{ opacity: 0, y: -10 }}
                      className="absolute top-full right-0 mt-1 bg-black/95 border border-cyan-500/30 rounded-lg p-3 z-50 backdrop-blur-md"
                    >
                      <div className="w-24">
                        <p className="text-xs text-gray-400 mb-2">年份</p>
                        <div className="max-h-48 overflow-y-auto space-y-1 scrollbar-thin scrollbar-thumb-cyan-500/50 scrollbar-track-transparent">
                          {availableYears.map(year => (
                            <button
                              key={year}
                              onClick={() => {
                                setSelectedYear(year);
                                setShowMonthPicker(false);
                              }}
                              className={`w-full px-3 py-1.5 rounded text-xs transition-all ${
                                selectedYear === year
                                  ? 'bg-cyan-500 text-white'
                                  : 'text-gray-400 hover:bg-cyan-500/20 hover:text-cyan-300'
                              }`}
                            >
                              {year}年
                            </button>
                          ))}
                        </div>
                      </div>
                    </motion.div>
                  )}
                </AnimatePresence>
              </div>
            </div>
            <div className="flex-1 bg-black/40 border border-cyan-500/20 rounded-lg p-3">
              <ResponsiveContainer width="100%" height="100%">
                <BarChart data={selectedEnergyType === 'electric' ? monthlyElectricTrend : selectedEnergyType === 'water' ? monthlyWaterTrend : monthlyGasTrend}>
                  <CartesianGrid strokeDasharray="3 3" stroke="rgba(6,182,212,0.15)" vertical={false} />
                  <XAxis 
                    dataKey="month" 
                    stroke="#5a7a90" 
                    style={{ fontSize: '11px' }}
                    axisLine={{ stroke: 'rgba(6,182,212,0.3)' }}
                    tickLine={false}
                  />
                  <YAxis 
                    stroke="#5a7a90" 
                    style={{ fontSize: '11px' }}
                    axisLine={{ stroke: 'rgba(6,182,212,0.3)' }}
                    tickLine={false}
                  />
                  <Tooltip
                    contentStyle={{
                      backgroundColor: 'rgba(0,0,0,0.9)',
                      border: '1px solid rgba(6,182,212,0.5)',
                      borderRadius: '6px',
                      fontSize: '12px',
                    }}
                    cursor={{ fill: 'rgba(6,182,212,0.1)' }}
                  />
                  <Bar dataKey="value" fill="url(#barGradient)" radius={[4, 4, 0, 0]} />
                  <defs>
                    <linearGradient id="barGradient" x1="0" y1="0" x2="0" y2="1">
                      <stop offset="0%" stopColor="#06b6d4" stopOpacity={1} />
                      <stop offset="100%" stopColor="#06b6d4" stopOpacity={0.6} />
                    </linearGradient>
                  </defs>
                </BarChart>
              </ResponsiveContainer>
            </div>
          </div>

          {/* 右侧 - 年度折线图 */}
          <div className="w-[400px] flex flex-col">
            <div className="flex items-center justify-between mb-2">
              <span className="text-xs text-gray-400">单位：{currentUnit.unit}</span>
              <div className="relative">
                <button
                  onClick={() => {
                    setShowYearOnlyPicker(!showYearOnlyPicker);
                    setShowYearPicker(false);
                    setShowMonthPicker(false);
                  }}
                  className="text-xs text-cyan-400 hover:text-cyan-300 transition-colors flex items-center gap-1"
                >
                  {selectedYear}年
                  <ChevronDown className="w-3 h-3" />
                </button>
                
                {/* 年份选择器下拉面板 */}
                <AnimatePresence>
                  {showYearOnlyPicker && (
                    <motion.div
                      initial={{ opacity: 0, y: -10 }}
                      animate={{ opacity: 1, y: 0 }}
                      exit={{ opacity: 0, y: -10 }}
                      className="absolute top-full right-0 mt-1 bg-black/95 border border-cyan-500/30 rounded-lg p-3 z-50 backdrop-blur-md"
                    >
                      <div className="w-24">
                        <p className="text-xs text-gray-400 mb-2">年份</p>
                        <div className="max-h-48 overflow-y-auto space-y-1 scrollbar-thin scrollbar-thumb-cyan-500/50 scrollbar-track-transparent">
                          {availableYears.map(year => (
                            <button
                              key={year}
                              onClick={() => {
                                setSelectedYear(year);
                                setShowYearOnlyPicker(false);
                              }}
                              className={`w-full px-3 py-1.5 rounded text-xs transition-all ${
                                selectedYear === year
                                  ? 'bg-cyan-500 text-white'
                                  : 'text-gray-400 hover:bg-cyan-500/20 hover:text-cyan-300'
                              }`}
                            >
                              {year}年
                            </button>
                          ))}
                        </div>
                      </div>
                    </motion.div>
                  )}
                </AnimatePresence>
              </div>
            </div>
            <div className="flex-1 bg-black/40 border border-cyan-500/20 rounded-lg p-3">
              <ResponsiveContainer width="100%" height="100%">
                <LineChart data={selectedEnergyType === 'electric' ? yearlyElectricTrend : selectedEnergyType === 'water' ? yearlyWaterTrend : yearlyGasTrend}>
                  <CartesianGrid strokeDasharray="3 3" stroke="rgba(6,182,212,0.15)" />
                  <XAxis 
                    dataKey="year" 
                    stroke="#5a7a90" 
                    style={{ fontSize: '11px' }}
                    axisLine={{ stroke: 'rgba(6,182,212,0.3)' }}
                    tickLine={false}
                  />
                  <YAxis 
                    stroke="#5a7a90" 
                    style={{ fontSize: '11px' }}
                    axisLine={{ stroke: 'rgba(6,182,212,0.3)' }}
                    tickLine={false}
                  />
                  <Tooltip
                    contentStyle={{
                      backgroundColor: 'rgba(0,0,0,0.9)',
                      border: '1px solid rgba(6,182,212,0.5)',
                      borderRadius: '6px',
                      fontSize: '12px',
                    }}
                  />
                  <Line 
                    type="monotone" 
                    dataKey="value" 
                    stroke="#06b6d4" 
                    strokeWidth={3} 
                    dot={{ fill: '#06b6d4', r: 5, strokeWidth: 2, stroke: '#0a1628' }} 
                    activeDot={{ r: 7 }}
                  />
                </LineChart>
              </ResponsiveContainer>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}