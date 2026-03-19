import { useState, useMemo } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import {
  ChevronRight, ChevronDown, MapPin, Wind, Droplets, Thermometer,
  Activity, Cpu, AlertTriangle, Clock, TrendingUp, TrendingDown,
  Power, Settings as SettingsIcon, X, Download, Check,
} from 'lucide-react';
import { useAuth } from '@/app/contexts/AuthContext';
import { useArea } from '@/app/contexts/AreaContext';
import { useArchive } from '@/app/contexts/ArchiveContext';
import { PieChart, Pie, Cell, LineChart, Line, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer } from 'recharts';
import * as XLSX from 'xlsx';
import { archivesData } from '@/app/data/archivesData';

// 空气质量数据接口
interface AirQualityData {
  areaId: string;
  areaName: string;
  freshAirVolume: number; // 新风风量 m³/h
  exhaustAirVolume: number; // 排风风量 m³/h
  smokePurification: number; // 烟雾浓度 mg/m³
  linkageEfficiency: number; // 联动效率 %
  pm25: number; // PM2.5浓度 mg/m³
  temperature: number; // 温度 °C
  humidity: number; // 湿度 %
  co2: number; // CO2浓度 ppm
}

// 模拟空气质量数据
const mockAirQualityData: AirQualityData[] = [
  {
    areaId: 'L2-001',
    areaName: '一层大厅区',
    freshAirVolume: 3000,
    exhaustAirVolume: 2500,
    smokePurification: 45,
    linkageEfficiency: 92,
    pm25: 45,
    temperature: 24,
    humidity: 55,
    co2: 680,
  },
  {
    areaId: 'L2-002',
    areaName: 'A区用餐区',
    freshAirVolume: 2800,
    exhaustAirVolume: 2300,
    smokePurification: 38,
    linkageEfficiency: 88,
    pm25: 38,
    temperature: 23,
    humidity: 52,
    co2: 620,
  },
];

// 用能分析数据 - 日
const energyUsageDataDay = [
  { time: '00:00', value: 35 },
  { time: '04:00', value: 50 },
  { time: '08:00', value: 42 },
  { time: '12:00', value: 68 },
  { time: '16:00', value: 55 },
  { time: '20:00', value: 48 },
  { time: '24:00', value: 38 },
];

// 用能分析数据 - 月
const energyUsageDataMonth = [
  { time: '3/1', value: 850 },
  { time: '3/5', value: 920 },
  { time: '3/9', value: 880 },
  { time: '3/13', value: 1050 },
  { time: '3/17', value: 980 },
  { time: '3/21', value: 890 },
  { time: '3/25', value: 1100 },
  { time: '3/29', value: 950 },
];

// 用能分析数据 - 年
const energyUsageDataYear = [
  { time: '1月', value: 22000 },
  { time: '2月', value: 19800 },
  { time: '3月', value: 25000 },
  { time: '4月', value: 27500 },
  { time: '5月', value: 30000 },
  { time: '6月', value: 32500 },
  { time: '7月', value: 35000 },
  { time: '8月', value: 36000 },
  { time: '9月', value: 31000 },
  { time: '10月', value: 28000 },
  { time: '11月', value: 24000 },
  { time: '12月', value: 21000 },
];

// 风柜使用时间统计数据
const usageTimeData = [
  { name: '运行', value: 168, color: '#6366f1', percentage: 70 },
  { name: '待机', value: 72, color: '#00ff9f', percentage: 30 },
];

// 区域树节点组件
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
        className={`flex items-center gap-2 py-2 px-3 rounded cursor-pointer transition-all ${
          isSelected
            ? 'bg-blue-500/20 border border-blue-500/50'
            : 'hover:bg-white/5 border border-transparent'
        }`}
        style={{ paddingLeft: `${level * 16 + 12}px` }}
      >
        {hasChildren && (
          <motion.div
            animate={{ rotate: expanded ? 90 : 0 }}
            transition={{ duration: 0.2 }}
          >
            <ChevronRight className="w-3 h-3 text-blue-400" />
          </motion.div>
        )}
        {!hasChildren && <div className="w-3" />}
        <MapPin className={`w-3 h-3 ${isSelected ? 'text-blue-400' : 'text-gray-500'}`} />
        <span className={`text-xs ${isSelected ? 'text-blue-300 font-medium' : 'text-gray-400'}`}>
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

// 主组件
export function AirQualityPage() {
  const { currentCustomer } = useAuth();
  const { areas } = useArea();
  const { setSelectedAreaForArchive } = useArchive();
  const [selectedArea, setSelectedArea] = useState<string | null>(null);
  const [viewMode, setViewMode] = useState<'day' | 'month' | 'year'>('day');

  // 联动逻辑配置状态
  const [linkageConfig, setLinkageConfig] = useState([
    { label: '根据油烟浓度自动启用排风风量', enabled: true },
    { label: '新风风量 = 排风风量 × 0.85', enabled: true },
    { label: '温度超过30°C时增加排风量', enabled: false },
    { label: 'CO₂浓度超标自动启用新风', enabled: true },
  ]);

  // 处理区域选择
  const handleAreaSelect = (areaName: string) => {
    setSelectedArea(areaName);
    // 同时更新ArchiveContext中的选中区域
    setSelectedAreaForArchive(areaName);
  };

  // 切换联动逻辑配置
  const toggleLinkageConfig = (index: number) => {
    setLinkageConfig(prev => prev.map((item, idx) => 
      idx === index ? { ...item, enabled: !item.enabled } : item
    ));
  };

  // 操作控制状态
  const [freshAirVolume1, setFreshAirVolume1] = useState(3000);
  const [inputFreshAirVolume1, setInputFreshAirVolume1] = useState('3000');
  const [exhaustAirVolume, setExhaustAirVolume] = useState(3000);
  const [inputExhaustAirVolume, setInputExhaustAirVolume] = useState('3000');
  const [startTime, setStartTime] = useState('08:00');
  const [endTime, setEndTime] = useState('22:00');
  const [selectedStartTime, setSelectedStartTime] = useState('开机时间');
  const [selectedEndTime, setSelectedEndTime] = useState('关机时间');

  // 确认新风风量设置
  const handleConfirmFreshAir = () => {
    const value = Number(inputFreshAirVolume1);
    if (!isNaN(value) && value >= 500 && value <= 5000) {
      setFreshAirVolume1(value);
    } else {
      alert('请输入500-5000之间的有效数值');
    }
  };

  // 确认排风风量设置
  const handleConfirmExhaustAir = () => {
    const value = Number(inputExhaustAirVolume);
    if (!isNaN(value) && value >= 500 && value <= 5000) {
      setExhaustAirVolume(value);
    } else {
      alert('请输入500-5000之间的有效数值');
    }
  };

  // 确认定时开关机
  const handleConfirmTiming = () => {
    if (selectedStartTime !== '开机时间') {
      setStartTime(selectedStartTime);
    }
    if (selectedEndTime !== '关机时间') {
      setEndTime(selectedEndTime);
    }
    if (selectedStartTime === '开机时间' || selectedEndTime === '关机时间') {
      alert('请选择开机时间和关机时间');
    }
  };

  // 根据选择的时间维度获取用能数据
  const energyData = useMemo(() => {
    switch (viewMode) {
      case 'day':
        return energyUsageDataDay;
      case 'month':
        return energyUsageDataMonth;
      case 'year':
        return energyUsageDataYear;
      default:
        return energyUsageDataDay;
    }
  }, [viewMode]);

  // 导出用能数据
  const handleExportData = () => {
    const worksheetData = energyData.map(item => ({
      时间: item.time,
      用能量: item.value,
    }));
    const worksheet = XLSX.utils.json_to_sheet(worksheetData);
    const workbook = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(workbook, worksheet, '用能分析');
    const fileName = `用能分析-${viewMode === 'day' ? '日' : viewMode === 'month' ? '月' : '年'}.xlsx`;
    XLSX.writeFile(workbook, fileName);
  };

  // 根据选中区域获取3D模型 - 优先查找空气质量分类的图纸
  const current3DArchive = useMemo(() => {
    if (!selectedArea) return null;
    // 优先查找空气质量分类的图纸
    const airQualityArchive = archivesData.find(
      archive => archive.areaName === selectedArea && archive.category === '空气质量' && archive.is3DModel
    );
    if (airQualityArchive) return airQualityArchive;
    // 如果没有找到空气质量图纸，查找其他3D图纸
    return archivesData.find(archive => archive.areaName === selectedArea && archive.is3DModel) || null;
  }, [selectedArea]);

  // 根据选中区域获取空气质量数据
  const currentAirQuality = useMemo(() => {
    if (!selectedArea) return null;
    return mockAirQualityData.find(data => data.areaName === selectedArea) || null;
  }, [selectedArea]);

  // 预警汇总数据
  const alerts = useMemo(() => {
    return [
      { time: '09:12:45', device: '高压微雾', status: 'E01:电压过低', level: 'critical' },
      { time: '08:45:22', device: '电磁阀相', status: 'E01:电磁损坏', level: 'warning' },
      { time: '08:45:22', device: '风量异常', status: 'E02:风量过低', level: 'critical' },
      { time: '08:45:22', device: '温度过高', status: 'E03:超过阈值', level: 'warning' },
    ];
  }, []);

  return (
    <div className="relative w-full h-full overflow-hidden bg-gradient-to-br from-[#0a1628] via-[#0d1b2a] to-[#0a1628] flex">
      {/* 背景装饰 */}
      <div className="absolute inset-0 opacity-20">
        <div className="absolute top-0 left-0 w-96 h-96 bg-blue-500/10 rounded-full blur-3xl" />
        <div className="absolute bottom-0 right-0 w-96 h-96 bg-cyan-500/10 rounded-full blur-3xl" />
      </div>

      {/* 左侧面板 - 区域导航 + 联动辑 + 空气质量监测 */}
      <div className="relative z-10 w-56 flex flex-col gap-4 p-4 border-r border-blue-500/20">
        {/* 区域导航 */}
        <div className="bg-black/40 backdrop-blur-sm border border-blue-500/30 rounded-lg p-4 flex-shrink-0">
          <div className="flex items-center gap-2 mb-3 pb-3 border-b border-blue-500/20">
            <MapPin className="w-4 h-4 text-blue-400" />
            <h3 className="text-sm font-bold text-blue-300">区域导航</h3>
          </div>
          <div className="space-y-1 max-h-48 overflow-y-auto" style={{ scrollbarWidth: 'none', msOverflowStyle: 'none' }}>
            <style dangerouslySetInnerHTML={{ __html: `
              .space-y-1::-webkit-scrollbar {
                display: none;
              }
            ` }} />
            {areas.map((area) => (
              <AreaTreeNode
                key={area.id}
                area={area}
                level={0}
                selectedArea={selectedArea}
                onSelect={handleAreaSelect}
              />
            ))}
          </div>
        </div>

        {/* 联动逻辑配置 */}
        <div className="bg-black/40 backdrop-blur-sm border border-blue-500/30 rounded-lg p-4 flex-shrink-0">
          <h3 className="text-sm font-bold text-blue-300 mb-3">联动逻辑配置</h3>
          <div className="space-y-2">
            {linkageConfig.map((config, idx) => (
              <div key={idx} className="flex items-start gap-2">
                <div
                  className={`w-4 h-4 rounded flex items-center justify-center flex-shrink-0 mt-0.5 cursor-pointer transition-colors ${
                    config.enabled ? 'bg-blue-500' : 'bg-gray-700'
                  }`}
                  onClick={() => toggleLinkageConfig(idx)}
                >
                  {config.enabled && (
                    <div className="w-2 h-2 bg-white rounded-sm" />
                  )}
                </div>
                <span className="text-xs text-gray-300 leading-relaxed">{config.label}</span>
              </div>
            ))}
          </div>
        </div>

        {/* 空气质量监测 */}
        <div className="bg-black/40 backdrop-blur-sm border border-blue-500/30 rounded-lg p-4 flex-1 flex flex-col items-center justify-center">
          <h3 className="text-sm font-bold text-blue-300 mb-4 self-start">空气质量监测</h3>
          
          {/* 环形进度条 */}
          <div className="relative w-32 h-32 mb-4">
            <svg className="w-full h-full -rotate-90" viewBox="0 0 100 100">
              <circle
                cx="50"
                cy="50"
                r="42"
                fill="none"
                stroke="rgba(59, 130, 246, 0.1)"
                strokeWidth="8"
              />
              <circle
                cx="50"
                cy="50"
                r="42"
                fill="none"
                stroke="url(#airQualityGradient)"
                strokeWidth="8"
                strokeDasharray={`${(currentAirQuality?.pm25 || 45) * 2.64} 264`}
                strokeLinecap="round"
              />
              <defs>
                <linearGradient id="airQualityGradient" x1="0%" y1="0%" x2="100%" y2="100%">
                  <stop offset="0%" stopColor="#3b82f6" />
                  <stop offset="100%" stopColor="#60a5fa" />
                </linearGradient>
              </defs>
            </svg>
            <div className="absolute inset-0 flex flex-col items-center justify-center">
              <span className="text-3xl font-bold text-white">{currentAirQuality?.pm25 || 45}</span>
            </div>
          </div>

          <div className="text-center">
            <p className="text-sm text-gray-300">油烟浓度: <span className="text-blue-400 font-medium">{currentAirQuality?.pm25 || 45}</span></p>
            <p className="text-xs text-gray-400 mt-1">mg/m³</p>
            <p className="text-xs text-green-400 mt-2">空气质量：良好</p>
          </div>
        </div>
      </div>

      {/* 中间面板 - 场景区域图 + 风柜使用 + 用能分析 */}
      <div className="relative z-10 flex-1 flex flex-col gap-4 p-4">
        {/* 场景区域 */}
        <div className="bg-black/40 backdrop-blur-sm border border-blue-500/30 rounded-lg p-4 flex-1 overflow-hidden flex flex-col">
          <h3 className="text-lg font-bold text-white mb-4">场景区域图</h3>
          
          {!current3DArchive ? (
            <div className="flex-1 flex items-center justify-center">
              <div className="text-center">
                <MapPin className="w-16 h-16 text-gray-600 mx-auto mb-4" />
                <p className="text-gray-400 text-sm">请从左侧区域导航中选择一个区域</p>
                <p className="text-gray-500 text-xs mt-2">查看该区域的3D场景和空气质量数据</p>
              </div>
            </div>
          ) : (
            <div className="flex-1 flex flex-col min-h-0">
              {/* 3D场景视图 */}
              <div className="flex-1 relative bg-gray-900/50 rounded-lg border border-white/10 overflow-hidden">
                <img
                  src={current3DArchive.imageUrl}
                  alt={current3DArchive.areaName}
                  className="absolute inset-0 w-full h-full object-cover opacity-40"
                />

                {/* 空气质量指标覆盖层 - 左侧垂直排列 */}
                {currentAirQuality && (
                  <>
                    <div className="absolute left-4 top-1/2 -translate-y-1/2 space-y-3">
                      {/* 新风风量 */}
                      <div className="bg-black/80 backdrop-blur-sm border border-blue-400/50 rounded-lg p-2">
                        <div className="flex items-center gap-2 mb-1">
                          <Wind className="w-4 h-4 text-blue-400" />
                          <span className="text-xs text-gray-400">新风风量</span>
                        </div>
                        <p className="text-lg font-bold text-white">{currentAirQuality.freshAirVolume} <span className="text-xs text-gray-400">m³/h</span></p>
                      </div>

                      {/* 排风风量 */}
                      <div className="bg-black/80 backdrop-blur-sm border border-cyan-400/50 rounded-lg p-2">
                        <div className="flex items-center gap-2 mb-1">
                          <Wind className="w-4 h-4 text-cyan-400" />
                          <span className="text-xs text-gray-400">排风风量</span>
                        </div>
                        <p className="text-lg font-bold text-white">{currentAirQuality.exhaustAirVolume} <span className="text-xs text-gray-400">m³/h</span></p>
                      </div>

                      {/* 烟雾浓度 */}
                      <div className="bg-black/80 backdrop-blur-sm border border-orange-400/50 rounded-lg p-2">
                        <div className="flex items-center gap-2 mb-1">
                          <Activity className="w-4 h-4 text-orange-400" />
                          <span className="text-xs text-gray-400">烟雾浓度</span>
                        </div>
                        <p className="text-lg font-bold text-white">{currentAirQuality.smokePurification} <span className="text-xs text-gray-400">mg/m³</span></p>
                      </div>

                      {/* 联动效率 */}
                      <div className="bg-black/80 backdrop-blur-sm border border-green-400/50 rounded-lg p-2">
                        <div className="flex items-center gap-2 mb-1">
                          <Cpu className="w-4 h-4 text-green-400" />
                          <span className="text-xs text-gray-400">联动效率</span>
                        </div>
                        <p className="text-lg font-bold text-white">{currentAirQuality.linkageEfficiency}<span className="text-xs text-gray-400">%</span></p>
                      </div>
                    </div>

                    {/* 3D建筑模型提示 */}
                    <div className="absolute top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2 pointer-events-none">
                      <div className="text-center">
                        <div className="w-16 h-16 mx-auto mb-2 border-4 border-blue-500/30 border-t-blue-400 rounded-full animate-spin" />
                        <p className="text-blue-400 text-xs">3D场景渲染中...</p>
                      </div>
                    </div>
                  </>
                )}
              </div>
            </div>
          )}
        </div>

        {/* 底部图表区 */}
        <div className="grid grid-cols-2 gap-4">
          {/* 风柜使用时间统计 */}
          <div className="bg-black/40 backdrop-blur-sm border border-blue-500/30 rounded-lg p-4">
            <h3 className="text-sm font-bold text-blue-300 mb-3 text-center">风柜使用时间统计</h3>
            
            {/* 图例 */}
            <div className="flex items-center justify-center gap-4 mb-3">
              <div className="flex items-center gap-1.5">
                <div className="w-3 h-3 rounded-sm" style={{ background: '#00ff9f' }} />
                <span className="text-xs text-gray-300">待机</span>
              </div>
              <div className="flex items-center gap-1.5">
                <div className="w-3 h-3 rounded-sm" style={{ background: '#6366f1' }} />
                <span className="text-xs text-gray-300">运行</span>
              </div>
            </div>

            {/* 环形图和标注 */}
            <div className="relative h-40 flex items-center justify-center">
              <PieChart width={180} height={180}>
                <Pie
                  data={usageTimeData}
                  cx={90}
                  cy={90}
                  innerRadius={50}
                  outerRadius={70}
                  paddingAngle={0}
                  dataKey="value"
                  startAngle={90}
                  endAngle={450}
                >
                  {usageTimeData.map((entry, index) => (
                    <Cell key={`cell-${index}`} fill={entry.color} />
                  ))}
                </Pie>
              </PieChart>

              {/* 左侧标注：待机 */}
              <div className="absolute left-2 top-1/2 -translate-y-1/2">
                <div className="text-left">
                  <p className="text-xs text-gray-400">待机</p>
                  <p className="text-sm font-bold text-white">{usageTimeData[1].value}小时</p>
                  <p className="text-xs text-gray-500">({usageTimeData[1].percentage}%)</p>
                </div>
                {/* 指示 */}
                <svg className="absolute top-1/2 left-full w-8 h-px" style={{ transform: 'translateY(-50%)' }}>
                  <line x1="0" y1="0" x2="32" y2="0" stroke="#00ff9f" strokeWidth="1" />
                </svg>
              </div>

              {/* 右下标注：运行 */}
              <div className="absolute right-2 bottom-4">
                <div className="text-right">
                  <p className="text-xs text-gray-400">运行</p>
                  <p className="text-sm font-bold text-white">{usageTimeData[0].value}小时</p>
                  <p className="text-xs text-gray-500">({usageTimeData[0].percentage}%)</p>
                </div>
                {/* 指示线 */}
                <svg className="absolute bottom-1/2 right-full w-8 h-px" style={{ transform: 'translateY(50%)' }}>
                  <line x1="0" y1="0" x2="32" y2="0" stroke="#6366f1" strokeWidth="1" />
                </svg>
              </div>
            </div>
          </div>

          {/* 用能分析 */}
          <div className="bg-black/40 backdrop-blur-sm border border-blue-500/30 rounded-lg p-4">
            <h3 className="text-sm font-bold text-blue-300 mb-3 text-center">用能分析</h3>
            
            {/* 顶部按钮组 */}
            <div className="flex items-center justify-center gap-1 mb-3">
              <button
                className={`px-3 py-1.5 text-xs rounded font-medium ${
                  viewMode === 'day' ? 'bg-blue-500 text-white' : 'bg-transparent text-gray-400 hover:bg-white/5'
                }`}
                onClick={() => setViewMode('day')}
              >
                日
              </button>
              <button
                className={`px-3 py-1.5 text-xs rounded font-medium ${
                  viewMode === 'month' ? 'bg-blue-500 text-white' : 'bg-transparent text-gray-400 hover:bg-white/5'
                }`}
                onClick={() => setViewMode('month')}
              >
                月
              </button>
              <button
                className={`px-3 py-1.5 text-xs rounded font-medium ${
                  viewMode === 'year' ? 'bg-blue-500 text-white' : 'bg-transparent text-gray-400 hover:bg-white/5'
                }`}
                onClick={() => setViewMode('year')}
              >
                年
              </button>
              <button className="px-3 py-1.5 text-xs rounded bg-blue-500 text-white font-medium flex items-center gap-1 ml-2" onClick={handleExportData}>
                <Download className="w-3 h-3" />
                导出数据
              </button>
            </div>

            {/* 左下角位 */}
            <div className="relative">
              <p className="text-xs text-gray-400 mb-2">单位：千瓦时</p>
              
              {/* 图表 */}
              <div className="h-44">
                <ResponsiveContainer width="100%" height="100%">
                  <LineChart data={energyData} margin={{ top: 10, right: 10, left: -20, bottom: 5 }}>
                    <CartesianGrid strokeDasharray="3 3" stroke="rgba(59, 130, 246, 0.15)" vertical={false} />
                    <XAxis
                      dataKey="time"
                      stroke="#6b7280"
                      style={{ fontSize: '11px' }}
                      tick={{ fill: '#9ca3af' }}
                      axisLine={{ stroke: 'rgba(59, 130, 246, 0.2)' }}
                    />
                    <YAxis
                      stroke="#6b7280"
                      style={{ fontSize: '11px' }}
                      tick={{ fill: '#9ca3af' }}
                      axisLine={{ stroke: 'rgba(59, 130, 246, 0.2)' }}
                      domain={[0, 70]}
                      ticks={[0, 10, 20, 30, 40, 50, 60, 70]}
                    />
                    <Tooltip
                      contentStyle={{
                        backgroundColor: 'rgba(0,0,0,0.9)',
                        border: '1px solid rgba(59, 130, 246, 0.5)',
                        borderRadius: '6px',
                        fontSize: '11px',
                      }}
                    />
                    <Line
                      type="monotone"
                      dataKey="value"
                      stroke="#3b82f6"
                      strokeWidth={2.5}
                      dot={false}
                    />
                  </LineChart>
                </ResponsiveContainer>
              </div>
            </div>
          </div>
        </div>
      </div>

      {/* 右侧面板 - 预警汇总 + 操作控制 */}
      <div className="relative z-10 w-80 flex flex-col gap-4 p-4 border-l border-blue-500/20">
        {/* 警汇总 */}
        <div className="bg-black/40 backdrop-blur-sm border border-blue-500/30 rounded-lg p-4 flex-1 overflow-hidden flex flex-col">
          <div className="flex items-center justify-between mb-3 pb-3 border-b border-blue-500/20">
            <h3 className="text-sm font-bold text-blue-300">预警汇总</h3>
            <button className="text-xs text-blue-400 hover:text-blue-300">查看全部预警</button>
          </div>
          <div className="flex-1 overflow-y-auto space-y-2 scrollbar-thin scrollbar-thumb-blue-500/50 scrollbar-track-transparent">
            {alerts.map((alert, idx) => (
              <div
                key={idx}
                className={`p-3 rounded-lg border ${
                  alert.level === 'critical'
                    ? 'border-red-500/50 bg-red-500/10'
                    : 'border-yellow-500/50 bg-yellow-500/10'
                }`}
              >
                <div className="flex items-center justify-between mb-2">
                  <span className={`text-xs font-medium ${
                    alert.level === 'critical' ? 'text-red-400' : 'text-yellow-400'
                  }`}>{alert.device}</span>
                  <Clock className="w-3 h-3 text-gray-500" />
                </div>
                <p className="text-xs text-gray-300 mb-1">{alert.status}</p>
                <p className="text-xs text-blue-400">{alert.time}</p>
              </div>
            ))}
          </div>
        </div>

        {/* 操作控制 */}
        <div className="bg-black/40 backdrop-blur-sm border border-blue-500/30 rounded-lg p-4">
          <h3 className="text-sm font-bold text-blue-300 mb-4">操作控制</h3>
          
          {/* 新风风量设置 */}
          <div className="mb-4">
            <p className="text-xs text-gray-300 mb-2">新风风量设置(500-5000m³/h)</p>
            
            {/* 滑块区域 */}
            <div className="relative mb-2">
              <div className="flex items-center justify-between text-xs text-gray-400 mb-1">
                <span>500</span>
                <span>当前: {freshAirVolume1} m³/h</span>
                <span>5000</span>
              </div>
              <input
                type="range"
                min="500"
                max="5000"
                value={freshAirVolume1}
                onChange={(e) => {
                  const value = Number(e.target.value);
                  setFreshAirVolume1(value);
                  setInputFreshAirVolume1(String(value));
                }}
                className="w-full h-2 bg-gray-700 rounded-lg appearance-none cursor-pointer accent-blue-500"
              />
            </div>

            {/* 输入框和确认按钮 */}
            <div className="flex items-center gap-2">
              <input
                type="text"
                value={inputFreshAirVolume1}
                onChange={(e) => setInputFreshAirVolume1(e.target.value)}
                className="w-32 px-3 py-2 bg-[#0a1628] border border-blue-500/30 rounded text-white text-sm h-9"
              />
              <button className="px-4 bg-blue-500 text-white text-sm rounded hover:bg-blue-600 flex items-center gap-1 h-9" onClick={handleConfirmFreshAir}>
                <Check className="w-4 h-4" />
                确认
              </button>
            </div>
          </div>

          {/* 排风风量设置 */}
          <div className="mb-4">
            <p className="text-xs text-gray-300 mb-2">排风风量设置(500-5000m³/h)</p>
            
            {/* 滑块区域 */}
            <div className="relative mb-2">
              <div className="flex items-center justify-between text-xs text-gray-400 mb-1">
                <span>500</span>
                <span>当前: {exhaustAirVolume} m³/h</span>
                <span>5000</span>
              </div>
              <input
                type="range"
                min="500"
                max="5000"
                value={exhaustAirVolume}
                onChange={(e) => {
                  const value = Number(e.target.value);
                  setExhaustAirVolume(value);
                  setInputExhaustAirVolume(String(value));
                }}
                className="w-full h-2 bg-gray-700 rounded-lg appearance-none cursor-pointer accent-blue-500"
              />
            </div>

            {/* 输入框和确认按钮 */}
            <div className="flex items-center gap-2">
              <input
                type="text"
                value={inputExhaustAirVolume}
                onChange={(e) => setInputExhaustAirVolume(e.target.value)}
                className="w-32 px-3 py-2 bg-[#0a1628] border border-blue-500/30 rounded text-white text-sm h-9"
              />
              <button className="px-4 bg-blue-500 text-white text-sm rounded hover:bg-blue-600 flex items-center gap-1 h-9" onClick={handleConfirmExhaustAir}>
                <Check className="w-4 h-4" />
                确认
              </button>
            </div>
          </div>

          {/* 定时开关机 */}
          <div>
            <p className="text-xs text-gray-300 mb-2">定时关机</p>
            
            {/* 时间选择器和确认按钮 */}
            <div className="flex items-center gap-2 mb-2">
              <select
                value={selectedStartTime}
                onChange={(e) => setSelectedStartTime(e.target.value)}
                className="w-20 px-2 py-2 bg-[#0a1628] border border-blue-500/30 rounded text-gray-300 text-sm h-9"
              >
                <option>开机时间</option>
                <option>06:00</option>
                <option>07:00</option>
                <option>08:00</option>
              </select>
              <select
                value={selectedEndTime}
                onChange={(e) => setSelectedEndTime(e.target.value)}
                className="w-20 px-2 py-2 bg-[#0a1628] border border-blue-500/30 rounded text-gray-300 text-sm h-9"
              >
                <option>关机时间</option>
                <option>20:00</option>
                <option>21:00</option>
                <option>22:00</option>
              </select>
              <button className="px-4 bg-blue-500 text-white text-sm rounded hover:bg-blue-600 flex items-center justify-center gap-1 h-9 whitespace-nowrap" onClick={handleConfirmTiming}>
                <Check className="w-4 h-4" />
                确认
              </button>
            </div>

            {/* 已设置提示 */}
            <p className="text-xs text-gray-400 mt-2">已设置为：{startTime}开机 {endTime}关机</p>
          </div>
        </div>
      </div>
    </div>
  );
}