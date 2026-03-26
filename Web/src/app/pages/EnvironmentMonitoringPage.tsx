import { useState, useMemo, useEffect } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import {
  ChevronRight, MapPin, Thermometer, Droplets, Wind, 
  Volume2, AlertTriangle, Flame, Activity, FileDown,
  Settings as SettingsIcon, Check, X, RotateCcw,
} from 'lucide-react';
import { useAuth } from '@/app/contexts/AuthContext';
import { useArea } from '@/app/contexts/AreaContext';
import { LineChart, Line, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer } from 'recharts';
import * as XLSX from 'xlsx';
import { archivesData } from '@/app/data/archivesData';
import { SceneViewer } from '@/app/components/3d-scene/SceneViewer';
import type { SceneConfig } from '@/app/types/3d-scene';

// 3D档案数据接口
interface Archive3D {
  id: string;
  name: string;
  areaId: string;
  areaName: string;
  imageUrl: string;
  categoryName?: string; // 档案分类名称
  sceneConfig?: any; // 兼容archivesData中的sceneConfig
}

// 环境监测数据接口
interface EnvironmentData {
  areaId: string;
  areaName: string;
  pm25: number;
  co2: number;
  co: number;
  temperature: number;
  humidity: number;
  noise: number;
  methane: number;
  smoke: number;
}

// 模拟3D档案数据（根据档案管理中的分类和区域）


// 模拟环境监测数据
const mockEnvironmentData: EnvironmentData[] = [
  {
    areaId: 'L2-001',
    areaName: '一层大厅区',
    pm25: 35,
    co2: 450,
    co: 50,
    temperature: 26,
    humidity: 55,
    noise: 58,
    methane: 0.05,
    smoke: 12,
  },
  {
    areaId: 'L2-002',
    areaName: 'A区用餐区',
    pm25: 28,
    co2: 380,
    co: 45,
    temperature: 24,
    humidity: 52,
    noise: 52,
    methane: 0.03,
    smoke: 8,
  },
  {
    areaId: 'L2-003',
    areaName: '一层厨区',
    pm25: 42,
    co2: 520,
    co: 60,
    temperature: 28,
    humidity: 58,
    noise: 65,
    methane: 0.08,
    smoke: 18,
  },
];

// 空气质量趋势数据
const airQualityTrendData = [
  { time: '12:00', pm25: 35, co2: 1.8, co: 50, methane: 1.2 },
  { time: '17:00', pm25: 42, co2: 1.5, co: 48, methane: 1.0 },
  { time: '22:00', pm25: 38, co2: 2.2, co: 52, methane: 1.5 },
  { time: '3:00', pm25: 28, co2: 1.0, co: 45, methane: 0.8 },
  { time: '8:00', pm25: 45, co2: 2.0, co: 55, methane: 1.8 },
];

// 空气质量趋势数据 - 日
const airQualityTrendDataDay = [
  { time: '00:00', pm25: 28, co2: 1.0, co: 45, methane: 0.8 },
  { time: '03:00', pm25: 25, co2: 0.9, co: 42, methane: 0.7 },
  { time: '06:00', pm25: 30, co2: 1.2, co: 46, methane: 0.9 },
  { time: '09:00', pm25: 38, co2: 1.6, co: 50, methane: 1.1 },
  { time: '12:00', pm25: 45, co2: 2.0, co: 55, methane: 1.5 },
  { time: '15:00', pm25: 42, co2: 1.8, co: 52, methane: 1.3 },
  { time: '18:00', pm25: 48, co2: 2.2, co: 58, methane: 1.8 },
  { time: '21:00', pm25: 35, co2: 1.4, co: 48, methane: 1.0 },
];

// 空气质量趋势数据 - 月
const airQualityTrendDataMonth = [
  { time: '3/1', pm25: 32, co2: 1.3, co: 47, methane: 1.0 },
  { time: '3/5', pm25: 38, co2: 1.5, co: 50, methane: 1.2 },
  { time: '3/9', pm25: 35, co2: 1.4, co: 48, methane: 1.1 },
  { time: '3/13', pm25: 42, co2: 1.8, co: 53, methane: 1.4 },
  { time: '3/17', pm25: 40, co2: 1.7, co: 51, methane: 1.3 },
  { time: '3/21', pm25: 36, co2: 1.5, co: 49, methane: 1.2 },
  { time: '3/25', pm25: 45, co2: 2.0, co: 55, methane: 1.6 },
  { time: '3/29', pm25: 38, co2: 1.6, co: 50, methane: 1.3 },
];

// 空气质量趋势数据 - 年
const airQualityTrendDataYear = [
  { time: '1月', pm25: 35, co2: 1.5, co: 48, methane: 1.1 },
  { time: '2月', pm25: 30, co2: 1.3, co: 45, methane: 0.9 },
  { time: '3月', pm25: 38, co2: 1.6, co: 50, methane: 1.2 },
  { time: '4月', pm25: 42, co2: 1.8, co: 52, methane: 1.4 },
  { time: '5月', pm25: 45, co2: 2.0, co: 55, methane: 1.6 },
  { time: '6月', pm25: 48, co2: 2.1, co: 57, methane: 1.7 },
  { time: '7月', pm25: 50, co2: 2.3, co: 60, methane: 1.9 },
  { time: '8月', pm25: 52, co2: 2.4, co: 62, methane: 2.0 },
  { time: '9月', pm25: 46, co2: 2.0, co: 56, methane: 1.6 },
  { time: '10月', pm25: 40, co2: 1.7, co: 51, methane: 1.3 },
  { time: '11月', pm25: 36, co2: 1.5, co: 49, methane: 1.2 },
  { time: '12月', pm25: 32, co2: 1.4, co: 47, methane: 1.0 },
];

// 待处理预警数据
const pendingAlerts = [
  { id: 1, device: '厨房 PM2.5 超标', location: '厨房二大厅', status: '高浓度', time: '14:45:22', level: 'critical' },
  { id: 2, device: '可燃气体浓度异常', location: '厨房一大厅', status: '高浓度', time: '13:45:22', level: 'critical' },
  { id: 3, device: '室内温度过高', location: '操作大厅', status: '高温报警', time: '13:45:22', level: 'warning' },
  { id: 4, device: '案板噪音过高', location: '厨房大厅 B', status: '噪音超标', time: '13:45:22', level: 'warning' },
  { id: 5, device: '室内温度过高', location: '操作大厅', status: '高温报警', time: '19:45:22', level: 'warning' },
];

// 设备运行状态数据
const deviceStatus = [
  { id: 1, name: '排烟风机', status: 'running', statusText: '运行中' },
  { id: 2, name: '油烟净化器', status: 'running', statusText: '运行中' },
  { id: 3, name: '空调系统', status: 'auto', statusText: '自动' },
  { id: 4, name: '烟雾报警器', status: 'normal', statusText: '正常' },
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
export function EnvironmentMonitoringPage() {
  const { currentCustomer } = useAuth();
  const { areas } = useArea(); // 从租户区域管理获取数据
  const [selectedArea, setSelectedArea] = useState<string | null>(null);
  const [viewMode, setViewMode] = useState<'day' | 'month' | 'year'>('day');

  // 智能联动控制状态
  const [acTemperature, setAcTemperature] = useState(26);
  const [lightBrightness, setLightBrightness] = useState(75);
  const [fanPower, setFanPower] = useState(75);

  // 预警阈值配置状态
  const [isEditingThreshold, setIsEditingThreshold] = useState(false);
  const [thresholds, setThresholds] = useState({
    pm25: 75,
    co: 50,
    formaldehyde: 0.1,
    temperature: 26,
    humidity: 68,
    noise: 70,
  });
  const [editingThresholds, setEditingThresholds] = useState({ ...thresholds });

  // 预警处理状态
  const [alerts, setAlerts] = useState(pendingAlerts);
  const [isProcessingAlert, setIsProcessingAlert] = useState(false);
  const [currentAlert, setCurrentAlert] = useState<typeof pendingAlerts[0] | null>(null);
  const [processingNote, setProcessingNote] = useState('');
  const [processingHandler, setProcessingHandler] = useState('');

  // 处理预警
  const handleProcessAlert = (alert: typeof pendingAlerts[0]) => {
    setCurrentAlert(alert);
    setProcessingNote('');
    setProcessingHandler('');
    setIsProcessingAlert(true);
  };

  const handleConfirmProcess = () => {
    if (!currentAlert) return;
    
    // 验证必填项
    if (!processingNote.trim()) {
      alert('请填写处理意见');
      return;
    }
    if (!processingHandler.trim()) {
      alert('请填写处理人');
      return;
    }

    // 从列表中移除已处理的预警
    setAlerts(prev => prev.filter(a => a.id !== currentAlert.id));
    
    // 关闭对话框
    setIsProcessingAlert(false);
    setCurrentAlert(null);
    setProcessingNote('');
    setProcessingHandler('');
  };

  const handleCancelProcess = () => {
    setIsProcessingAlert(false);
    setCurrentAlert(null);
    setProcessingNote('');
    setProcessingHandler('');
  };

  // 处理阈值编辑
  const handleStartEdit = () => {
    setEditingThresholds({ ...thresholds });
    setIsEditingThreshold(true);
  };

  const handleCancelEdit = () => {
    setEditingThresholds({ ...thresholds });
    setIsEditingThreshold(false);
  };

  const handleSaveThresholds = () => {
    setThresholds({ ...editingThresholds });
    setIsEditingThreshold(false);
  };

  const handleThresholdChange = (key: keyof typeof thresholds, value: number) => {
    setEditingThresholds(prev => ({ ...prev, [key]: value }));
  };

  // 根据选中区域获取3D模型 - 优先查找环境监测分类的图纸
  const current3DArchive = useMemo(() => {
    if (!selectedArea) return null;
    // 优先查找环境监测分类的图纸
    const environmentArchive = archivesData.find(
      archive => archive.areaName === selectedArea && (archive.category === '环境监测' || archive.category === '图纸资料') && archive.is3DModel
    );
    if (environmentArchive) return environmentArchive;
    // 如果没有找到环境监测图纸，查找其他3D图纸
    return archivesData.find(archive => archive.areaName === selectedArea && archive.is3DModel) || null;
  }, [selectedArea]);

  // 3D场景配置与加载状态
  const [sceneConfig, setSceneConfig] = useState<SceneConfig | null>(null);
  const [loading, setLoading] = useState(false);

  // 监听区域选择变化，提取sceneConfig
  useEffect(() => {
    if (!current3DArchive) {
      setSceneConfig(null);
      return;
    }

    setLoading(true);
    try {
      if (current3DArchive.sceneConfig) {
        setSceneConfig(current3DArchive.sceneConfig as SceneConfig);
      } else {
        setSceneConfig(null);
      }
    } catch (error) {
      console.error('查找3D模型失败:', error);
      setSceneConfig(null);
    } finally {
      setLoading(false);
    }
  }, [current3DArchive]);

  // 根据选中区域获取环境数据
  const currentEnvironment = useMemo(() => {
    if (!selectedArea) return null;
    return mockEnvironmentData.find(data => data.areaName === selectedArea) || null;
  }, [selectedArea]);

  // 导出数据
  const handleExportData = () => {
    const worksheetData = airQualityTrendData.map(item => ({
      时间: item.time,
      PM25: item.pm25,
      CO2: item.co2,
      甲烷: item.methane,
    }));
    const worksheet = XLSX.utils.json_to_sheet(worksheetData);
    const workbook = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(workbook, worksheet, '空气质量趋势');
    XLSX.writeFile(workbook, '空气质量趋势分析.xlsx');
  };

  return (
    <div className="relative w-full h-full overflow-hidden bg-gradient-to-br from-[#0a1628] via-[#0d1b2a] to-[#0a1628] flex">
      {/* 背景装饰 */}
      <div className="absolute inset-0 opacity-20">
        <div className="absolute top-0 left-0 w-96 h-96 bg-blue-500/10 rounded-full blur-3xl" />
        <div className="absolute bottom-0 right-0 w-96 h-96 bg-cyan-500/10 rounded-full blur-3xl" />
      </div>

      {/* 左侧面板 - 区域导航 + 智能联动控制 + 待处理预警 */}
      <div className="relative z-10 w-72 flex flex-col gap-4 p-4 border-r border-blue-500/20">
        {/* 区域导航 - 来自租户区域管理数据 */}
        <div className="bg-black/40 backdrop-blur-sm border border-blue-500/30 rounded-lg p-4 flex-shrink-0">
          <div className="flex items-center gap-2 mb-3 pb-3 border-b border-blue-500/20">
            <MapPin className="w-4 h-4 text-blue-400" />
            <h3 className="text-sm font-bold text-blue-300">区域导航</h3>
          </div>
          <div className="space-y-1 max-h-48 overflow-y-auto hide-scrollbar">
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

        {/* 智能联动控制 */}
        <div className="bg-black/40 backdrop-blur-sm border border-blue-500/30 rounded-lg p-4 flex-shrink-0">
          <h3 className="text-sm font-bold text-blue-300 mb-4">智能联动控制</h3>
          
          {/* 空调温度 */}
          <div className="mb-4">
            <div className="flex items-center justify-between mb-2">
              <span className="text-xs text-gray-300">空调温度</span>
              <span className="text-sm text-white font-medium">{acTemperature}°C</span>
            </div>
            <input
              type="range"
              min="16"
              max="30"
              value={acTemperature}
              onChange={(e) => setAcTemperature(Number(e.target.value))}
              className="w-full h-1.5 bg-gray-700 rounded-lg appearance-none cursor-pointer accent-blue-500"
            />
          </div>

          {/* 灯光亮度 */}
          <div className="mb-4">
            <div className="flex items-center justify-between mb-2">
              <span className="text-xs text-gray-300">灯光亮度</span>
              <span className="text-sm text-white font-medium">{lightBrightness}%</span>
            </div>
            <input
              type="range"
              min="0"
              max="100"
              value={lightBrightness}
              onChange={(e) => setLightBrightness(Number(e.target.value))}
              className="w-full h-1.5 bg-gray-700 rounded-lg appearance-none cursor-pointer accent-blue-500"
            />
          </div>

          {/* 风机功率 */}
          <div>
            <div className="flex items-center justify-between mb-2">
              <span className="text-xs text-gray-300">风机功率</span>
              <span className="text-sm text-white font-medium">{fanPower}%</span>
            </div>
            <input
              type="range"
              min="0"
              max="100"
              value={fanPower}
              onChange={(e) => setFanPower(Number(e.target.value))}
              className="w-full h-1.5 bg-gray-700 rounded-lg appearance-none cursor-pointer accent-blue-500"
            />
          </div>
        </div>

        {/* 待处理预警 */}
        <div className="bg-black/40 backdrop-blur-sm border border-blue-500/30 rounded-lg p-4 flex-1 overflow-hidden flex flex-col">
          <div className="flex items-center justify-between mb-3 pb-3 border-b border-blue-500/20">
            <h3 className="text-sm font-bold text-blue-300">待处理预警 ({alerts.length})</h3>
          </div>
          <div className="flex-1 overflow-y-auto space-y-2 scrollbar-thin scrollbar-thumb-red-500/50 scrollbar-track-transparent">
            {alerts.map((alert) => (
              <div
                key={alert.id}
                className={`p-3 rounded-lg border-l-4 ${
                  alert.level === 'critical'
                    ? 'border-l-red-500 bg-red-500/10'
                    : 'border-l-yellow-500 bg-yellow-500/10'
                }`}
              >
                <div className="flex items-start justify-between mb-1">
                  <span className={`text-xs font-medium ${
                    alert.level === 'critical' ? 'text-red-400' : 'text-yellow-400'
                  }`}>{alert.device}</span>
                  <button 
                    onClick={() => handleProcessAlert(alert)}
                    className="px-2 py-0.5 text-xs bg-red-500 text-white rounded hover:bg-red-600 transition-colors"
                  >
                    处理
                  </button>
                </div>
                <p className="text-xs text-gray-400">位置：{alert.location}</p>
                <p className="text-xs text-gray-300 mt-1">{alert.status}</p>
                <p className="text-xs text-blue-400 mt-1">{alert.time}</p>
              </div>
            ))}
          </div>
        </div>
      </div>

      {/* 中间面板 - 区域实时热力监测 + 核心监测指标 + 空气质量趋势 */}
      <div className="relative z-10 flex-1 flex flex-col gap-4 p-4">
        {/* 区域实时热力监测 - 从档案管理图纸资料获取3D图 */}
        <div className="bg-black/40 backdrop-blur-sm border border-blue-500/30 rounded-lg p-4 flex-1 overflow-hidden flex flex-col">
          <div className="flex items-center justify-between mb-4">
            <h3 className="text-lg font-bold text-white">区域实时热力监测</h3>
            {current3DArchive && (
              <div className="flex items-center gap-2">
                <div className="flex items-center gap-1">
                  <div className="w-3 h-3 rounded-full bg-green-500" />
                  <span className="text-xs text-gray-400">正常</span>
                </div>
                <div className="flex items-center gap-1">
                  <div className="w-3 h-3 rounded-full bg-red-500" />
                  <span className="text-xs text-gray-400">超标</span>
                </div>
                <div className="flex items-center gap-1">
                  <div className="w-3 h-3 rounded-full bg-yellow-500" />
                  <span className="text-xs text-gray-400">预警</span>
                </div>
              </div>
            )}
          </div>
          
          {!current3DArchive ? (
            <div className="flex-1 flex items-center justify-center">
              <div className="text-center">
                <MapPin className="w-16 h-16 text-gray-600 mx-auto mb-4" />
                <p className="text-gray-400 text-sm">请从左侧区域导航中选择一个区域</p>
                <p className="text-gray-500 text-xs mt-2">查看该区域的3D热力监测图和环境数据</p>
              </div>
            </div>
          ) : (
            <div className="flex-1 flex flex-col min-h-0">
              {/* 3D场景视图 */}
              <div className="flex-1 relative bg-gray-900/50 rounded-lg border border-white/10 overflow-hidden">
                {/* 清除选择按钮 */}
                <button
                  onClick={() => {
                    setSelectedArea(null);
                    setSceneConfig(null);
                  }}
                  className="absolute top-3 right-3 z-20 flex items-center gap-1.5 px-3 py-1.5 bg-black/70 backdrop-blur-sm border border-white/20 rounded-lg text-gray-300 text-xs hover:bg-black/90 hover:text-white transition-colors"
                >
                  <RotateCcw className="w-3 h-3" />
                  清除选择
                </button>

                {/* 3D场景渲染 */}
                {sceneConfig ? (
                  <SceneViewer
                    key={sceneConfig.areaId}
                    sceneConfig={sceneConfig}
                    className="w-full h-full"
                    onDeviceClick={(deviceId, deviceData) => {
                      console.log('设备点击:', deviceId, deviceData);
                    }}
                    onHoverDevice={(deviceId, deviceData) => {
                      // 设备悬停交互，可扩展
                    }}
                  />
                ) : !loading ? (
                  <div className="flex items-center justify-center w-full h-full">
                    <div className="text-center">
                      <MapPin className="w-12 h-12 text-gray-600 mx-auto mb-3" />
                      <p className="text-gray-400 text-sm">暂无3D模型数据</p>
                      <p className="text-gray-500 text-xs mt-1">来自档案管理 - {current3DArchive.category || current3DArchive.categoryName}</p>
                    </div>
                  </div>
                ) : null}

                {/* 热力图覆盖效果 */}
                <div className="absolute inset-0 bg-gradient-to-br from-blue-500/10 via-orange-500/20 to-red-500/10 mix-blend-multiply pointer-events-none z-0" />
              </div>
            </div>
          )}
        </div>

        {/* 底部数据区 */}
        <div className="grid grid-cols-2 gap-4">
          {/* 核心监测指标 */}
          <div className="bg-black/40 backdrop-blur-sm border border-blue-500/30 rounded-lg p-4">
            <h3 className="text-sm font-bold text-blue-300 mb-3">核心监测指标</h3>
            
            {currentEnvironment ? (
              <div className="grid grid-cols-4 gap-3">
                {/* PM2.5 */}
                <div className="bg-gradient-to-br from-blue-500/10 to-blue-500/5 border border-blue-500/30 rounded-lg p-3 flex flex-col items-center">
                  <Wind className="w-6 h-6 text-blue-400 mb-2" />
                  <p className="text-2xl font-bold text-green-400">{currentEnvironment.pm25}</p>
                  <p className="text-xs text-gray-400 mt-1">PM2.5</p>
                  <p className="text-xs text-gray-500">μg/m³</p>
                </div>

                {/* 二氧化碳 */}
                <div className="bg-gradient-to-br from-yellow-500/10 to-yellow-500/5 border border-yellow-500/30 rounded-lg p-3 flex flex-col items-center">
                  <AlertTriangle className="w-6 h-6 text-yellow-400 mb-2" />
                  <p className="text-2xl font-bold text-green-400">{currentEnvironment.co2}</p>
                  <p className="text-xs text-gray-400 mt-1">二氧化碳</p>
                  <p className="text-xs text-gray-500">ppm</p>
                </div>

                {/* 一氧化碳 */}
                <div className="bg-gradient-to-br from-blue-500/10 to-blue-500/5 border border-blue-500/30 rounded-lg p-3 flex flex-col items-center">
                  <Activity className="w-6 h-6 text-blue-400 mb-2" />
                  <p className="text-2xl font-bold text-green-400">{currentEnvironment.smoke}</p>
                  <p className="text-xs text-gray-400 mt-1">一氧化碳</p>
                  <p className="text-xs text-gray-500">ppm</p>
                </div>

                {/* 可燃气体 */}
                <div className="bg-gradient-to-br from-orange-500/10 to-orange-500/5 border border-orange-500/30 rounded-lg p-3 flex flex-col items-center">
                  <Flame className="w-6 h-6 text-orange-400 mb-2" />
                  <p className="text-2xl font-bold text-green-400">{currentEnvironment.methane}</p>
                  <p className="text-xs text-gray-400 mt-1">可燃气体</p>
                  <p className="text-xs text-gray-500">mg/m³</p>
                </div>

                {/* 温度 */}
                <div className="bg-gradient-to-br from-red-500/10 to-red-500/5 border border-red-500/30 rounded-lg p-3 flex flex-col items-center">
                  <Thermometer className="w-6 h-6 text-red-400 mb-2" />
                  <p className="text-2xl font-bold text-green-400">{currentEnvironment.temperature}</p>
                  <p className="text-xs text-gray-400 mt-1">温度</p>
                  <p className="text-xs text-gray-500">°C</p>
                </div>

                {/* 湿度 */}
                <div className="bg-gradient-to-br from-cyan-500/10 to-cyan-500/5 border border-cyan-500/30 rounded-lg p-3 flex flex-col items-center">
                  <Droplets className="w-6 h-6 text-cyan-400 mb-2" />
                  <p className="text-2xl font-bold text-green-400">{currentEnvironment.humidity}</p>
                  <p className="text-xs text-gray-400 mt-1">湿度</p>
                  <p className="text-xs text-gray-500">%RH</p>
                </div>

                {/* 噪音 */}
                <div className="bg-gradient-to-br from-purple-500/10 to-purple-500/5 border border-purple-500/30 rounded-lg p-3 flex flex-col items-center">
                  <Volume2 className="w-6 h-6 text-purple-400 mb-2" />
                  <p className="text-2xl font-bold text-green-400">{currentEnvironment.noise}</p>
                  <p className="text-xs text-gray-400 mt-1">噪音</p>
                  <p className="text-xs text-gray-500">dB</p>
                </div>

                {/* 甲醛 */}
                <div className="bg-gradient-to-br from-green-500/10 to-green-500/5 border border-green-500/30 rounded-lg p-3 flex flex-col items-center">
                  <Activity className="w-6 h-6 text-green-400 mb-2" />
                  <p className="text-2xl font-bold text-green-400">0.05</p>
                  <p className="text-xs text-gray-400 mt-1">甲醛</p>
                  <p className="text-xs text-gray-500">mg/m³</p>
                </div>
              </div>
            ) : (
              <div className="text-center py-8">
                <p className="text-gray-500 text-sm">请选择区域查看监测数据</p>
              </div>
            )}
          </div>

          {/* 空气质量趋势分析 */}
          <div className="bg-black/40 backdrop-blur-sm border border-blue-500/30 rounded-lg p-4">
            <h3 className="text-base font-bold text-white mb-3 text-center">空气质量趋势分析</h3>
            
            {/* 顶部按钮组 - 居 */}
            <div className="flex items-center justify-center gap-2 mb-4">
              <button
                onClick={() => setViewMode('day')}
                className={`px-4 py-1.5 text-sm rounded ${
                  viewMode === 'day' ? 'bg-blue-500 text-white font-medium' : 'bg-transparent border border-blue-500/30 text-gray-400 hover:bg-blue-500/10'
                }`}
              >
                日
              </button>
              <button
                onClick={() => setViewMode('month')}
                className={`px-4 py-1.5 text-sm rounded ${
                  viewMode === 'month' ? 'bg-blue-500 text-white font-medium' : 'bg-transparent border border-blue-500/30 text-gray-400 hover:bg-blue-500/10'
                }`}
              >
                月
              </button>
              <button
                onClick={() => setViewMode('year')}
                className={`px-4 py-1.5 text-sm rounded ${
                  viewMode === 'year' ? 'bg-blue-500 text-white font-medium' : 'bg-transparent border border-blue-500/30 text-gray-400 hover:bg-blue-500/10'
                }`}
              >
                年
              </button>
              <button
                onClick={handleExportData}
                className="px-4 py-1.5 text-sm rounded bg-blue-500 text-white font-medium flex items-center gap-1"
              >
                <FileDown className="w-4 h-4" />
                导出数据
              </button>
            </div>
            
            {/* 图表 */}
            <div className="h-48">
              <ResponsiveContainer width="100%" height="100%">
                <LineChart
                  data={viewMode === 'day' ? airQualityTrendDataDay : viewMode === 'month' ? airQualityTrendDataMonth : airQualityTrendDataYear}
                  margin={{ top: 10, right: 30, left: 10, bottom: 20 }}
                >
                  <CartesianGrid strokeDasharray="3 3" stroke="rgba(59, 130, 246, 0.2)" />
                  <XAxis
                    dataKey="time"
                    stroke="#6b7280"
                    style={{ fontSize: '12px' }}
                    tick={{ fill: '#9ca3af' }}
                    axisLine={{ stroke: 'rgba(59, 130, 246, 0.3)' }}
                  />
                  <YAxis
                    yAxisId="left"
                    stroke="#3b82f6"
                    style={{ fontSize: '12px' }}
                    tick={{ fill: '#3b82f6' }}
                    axisLine={{ stroke: 'rgba(59, 130, 246, 0.5)' }}
                    domain={[0, 70]}
                    ticks={[0, 10, 20, 30, 40, 50, 60, 70]}
                  />
                  <YAxis
                    yAxisId="right"
                    orientation="right"
                    stroke="#fbbf24"
                    style={{ fontSize: '12px' }}
                    tick={{ fill: '#fbbf24' }}
                    axisLine={{ stroke: 'rgba(251, 191, 36, 0.5)' }}
                    domain={[0, 2.5]}
                    ticks={[0, 0.5, 1, 1.5, 2, 2.5]}
                  />
                  <Tooltip
                    contentStyle={{
                      backgroundColor: 'rgba(0,0,0,0.9)',
                      border: '1px solid rgba(59, 130, 246, 0.5)',
                      borderRadius: '6px',
                      fontSize: '12px',
                    }}
                  />
                  <Line
                    yAxisId="left"
                    type="monotone"
                    dataKey="pm25"
                    stroke="#3b82f6"
                    strokeWidth={2.5}
                    dot={{ fill: '#3b82f6', r: 4 }}
                    name="PM2.5"
                  />
                  <Line
                    yAxisId="right"
                    type="monotone"
                    dataKey="co2"
                    stroke="#fbbf24"
                    strokeWidth={2.5}
                    dot={{ fill: '#fbbf24', r: 4 }}
                    name="CO₂(mg/m³)"
                  />
                  <Line
                    yAxisId="right"
                    type="monotone"
                    dataKey="methane"
                    stroke="#10b981"
                    strokeWidth={2.5}
                    dot={{ fill: '#10b981', r: 4 }}
                    name="CO(ppm)"
                  />
                </LineChart>
              </ResponsiveContainer>
            </div>
            
            {/* 底部图例 */}
            <div className="flex items-center justify-center gap-6 mt-3">
              <div className="flex items-center gap-2">
                <div className="w-10 h-0.5 bg-blue-500 rounded"></div>
                <span className="text-xs text-gray-400">PM2.5</span>
              </div>
              <div className="flex items-center gap-2">
                <div className="w-10 h-0.5 bg-yellow-400 rounded"></div>
                <span className="text-xs text-gray-400">CO₂(mg/m³)</span>
              </div>
              <div className="flex items-center gap-2">
                <div className="w-10 h-0.5 bg-green-500 rounded"></div>
                <span className="text-xs text-gray-400">CO(ppm)</span>
              </div>
            </div>
          </div>
        </div>
      </div>

      {/* 右侧面板 - 设备运行状态 + 预警阈值配置 */}
      <div className="relative z-10 w-80 flex flex-col gap-4 p-4 border-l border-blue-500/20">
        {/* 设备运行状态 */}
        <div className="bg-black/40 backdrop-blur-sm border border-blue-500/30 rounded-lg p-4">
          <h3 className="text-sm font-bold text-blue-300 mb-4">设备运行状态</h3>
          <div className="space-y-3">
            {deviceStatus.map((device) => (
              <div
                key={device.id}
                className="bg-gradient-to-r from-blue-500/5 to-transparent border border-blue-500/20 rounded-lg p-3 flex items-center justify-between"
              >
                <div className="flex items-center gap-3">
                  <div className={`w-2 h-2 rounded-full ${
                    device.status === 'running' ? 'bg-green-400 animate-pulse' :
                    device.status === 'auto' ? 'bg-blue-400' : 'bg-green-400'
                  }`} />
                  <span className="text-sm text-gray-300">{device.name}</span>
                </div>
                <span className={`text-xs font-medium ${
                  device.status === 'running' ? 'text-green-400' :
                  device.status === 'auto' ? 'text-blue-400' : 'text-green-400'
                }`}>{device.statusText}</span>
              </div>
            ))}
          </div>
        </div>

        {/* 预警阈值配置 */}
        <div className="bg-black/40 backdrop-blur-sm border border-blue-500/30 rounded-lg p-4 flex-1">
          <h3 className="text-sm font-bold text-blue-300 mb-4">预警阈值配置</h3>
          <div className="space-y-4">
            {/* PM2.5 */}
            <div className="pb-4 border-b border-dashed border-gray-700">
              <div className="flex items-center justify-between">
                <span className="text-xs text-gray-300">PM2.5</span>
                {isEditingThreshold ? (
                  <input
                    type="number"
                    value={editingThresholds.pm25}
                    onChange={(e) => handleThresholdChange('pm25', Number(e.target.value))}
                    className="w-24 px-2 py-1 text-sm bg-gray-700 border border-blue-500/30 rounded text-white focus:outline-none focus:border-blue-500"
                    step="1"
                  />
                ) : (
                  <span className="text-sm text-white font-medium">{thresholds.pm25} μg/m³</span>
                )}
              </div>
            </div>

            {/* CO */}
            <div className="pb-4 border-b border-dashed border-gray-700">
              <div className="flex items-center justify-between">
                <span className="text-xs text-gray-300">CO</span>
                {isEditingThreshold ? (
                  <input
                    type="number"
                    value={editingThresholds.co}
                    onChange={(e) => handleThresholdChange('co', Number(e.target.value))}
                    className="w-24 px-2 py-1 text-sm bg-gray-700 border border-blue-500/30 rounded text-white focus:outline-none focus:border-blue-500"
                    step="1"
                  />
                ) : (
                  <span className="text-sm text-white font-medium">{thresholds.co} ppm</span>
                )}
              </div>
            </div>

            {/* 甲醛 */}
            <div className="pb-4 border-b border-dashed border-gray-700">
              <div className="flex items-center justify-between">
                <span className="text-xs text-gray-300">甲醛</span>
                {isEditingThreshold ? (
                  <input
                    type="number"
                    value={editingThresholds.formaldehyde}
                    onChange={(e) => handleThresholdChange('formaldehyde', Number(e.target.value))}
                    className="w-24 px-2 py-1 text-sm bg-gray-700 border border-blue-500/30 rounded text-white focus:outline-none focus:border-blue-500"
                    step="0.01"
                  />
                ) : (
                  <span className="text-sm text-white font-medium">{thresholds.formaldehyde} mg/m³</span>
                )}
              </div>
            </div>

            {/* 温度 */}
            <div className="pb-4 border-b border-dashed border-gray-700">
              <div className="flex items-center justify-between">
                <span className="text-xs text-gray-300">温度</span>
                {isEditingThreshold ? (
                  <input
                    type="number"
                    value={editingThresholds.temperature}
                    onChange={(e) => handleThresholdChange('temperature', Number(e.target.value))}
                    className="w-24 px-2 py-1 text-sm bg-gray-700 border border-blue-500/30 rounded text-white focus:outline-none focus:border-blue-500"
                    step="1"
                  />
                ) : (
                  <span className="text-sm text-white font-medium">{thresholds.temperature}°C</span>
                )}
              </div>
            </div>

            {/* 湿度 */}
            <div className="pb-4 border-b border-dashed border-gray-700">
              <div className="flex items-center justify-between">
                <span className="text-xs text-gray-300">湿度</span>
                {isEditingThreshold ? (
                  <input
                    type="number"
                    value={editingThresholds.humidity}
                    onChange={(e) => handleThresholdChange('humidity', Number(e.target.value))}
                    className="w-24 px-2 py-1 text-sm bg-gray-700 border border-blue-500/30 rounded text-white focus:outline-none focus:border-blue-500"
                    step="1"
                  />
                ) : (
                  <span className="text-sm text-white font-medium">{thresholds.humidity}%RH</span>
                )}
              </div>
            </div>

            {/* 噪音 */}
            <div className="pb-4 border-b border-dashed border-gray-700">
              <div className="flex items-center justify-between">
                <span className="text-xs text-gray-300">噪音</span>
                {isEditingThreshold ? (
                  <input
                    type="number"
                    value={editingThresholds.noise}
                    onChange={(e) => handleThresholdChange('noise', Number(e.target.value))}
                    className="w-24 px-2 py-1 text-sm bg-gray-700 border border-blue-500/30 rounded text-white focus:outline-none focus:border-blue-500"
                    step="1"
                  />
                ) : (
                  <span className="text-sm text-white font-medium">{thresholds.noise} dB</span>
                )}
              </div>
            </div>

            {/* 编辑/保存/取消按钮 */}
            {!isEditingThreshold ? (
              <button 
                onClick={handleStartEdit}
                className="w-full flex items-center justify-center gap-1 px-3 py-2 text-xs bg-blue-500 text-white rounded hover:bg-blue-600 transition-colors"
              >
                <SettingsIcon className="w-3.5 h-3.5" />
                编辑阈值
              </button>
            ) : (
              <div className="flex gap-2">
                <button 
                  onClick={handleSaveThresholds}
                  className="flex-1 flex items-center justify-center gap-1 px-3 py-2 text-xs bg-green-500 text-white rounded hover:bg-green-600 transition-colors"
                >
                  <Check className="w-3.5 h-3.5" />
                  保存
                </button>
                <button 
                  onClick={handleCancelEdit}
                  className="flex-1 flex items-center justify-center gap-1 px-3 py-2 text-xs bg-gray-600 text-white rounded hover:bg-gray-700 transition-colors"
                >
                  <X className="w-3.5 h-3.5" />
                  取消
                </button>
              </div>
            )}
          </div>
        </div>
      </div>

      {/* 预警处理对话框 */}
      <AnimatePresence>
        {isProcessingAlert && currentAlert && (
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            className="fixed inset-0 bg-black/60 backdrop-blur-sm z-50 flex items-center justify-center p-4"
            onClick={handleCancelProcess}
          >
            <motion.div
              initial={{ scale: 0.9, opacity: 0 }}
              animate={{ scale: 1, opacity: 1 }}
              exit={{ scale: 0.9, opacity: 0 }}
              onClick={(e) => e.stopPropagation()}
              className="bg-gradient-to-br from-gray-900 to-gray-800 border border-blue-500/30 rounded-lg p-6 w-full max-w-md shadow-2xl"
            >
              {/* 标题 */}
              <div className="flex items-center justify-between mb-6">
                <h3 className="text-lg font-bold text-white">处理预警</h3>
                <button
                  onClick={handleCancelProcess}
                  className="text-gray-400 hover:text-white transition-colors"
                >
                  <X className="w-5 h-5" />
                </button>
              </div>

              {/* 预警信息 */}
              <div className={`mb-6 p-4 rounded-lg border-l-4 ${
                currentAlert.level === 'critical'
                  ? 'border-l-red-500 bg-red-500/10'
                  : 'border-l-yellow-500 bg-yellow-500/10'
              }`}>
                <div className="flex items-start justify-between mb-2">
                  <span className={`text-sm font-medium ${
                    currentAlert.level === 'critical' ? 'text-red-400' : 'text-yellow-400'
                  }`}>{currentAlert.device}</span>
                  <span className={`px-2 py-0.5 text-xs rounded ${
                    currentAlert.level === 'critical'
                      ? 'bg-red-500 text-white'
                      : 'bg-yellow-500 text-white'
                  }`}>
                    {currentAlert.level === 'critical' ? '紧急' : '警告'}
                  </span>
                </div>
                <p className="text-xs text-gray-400 mb-1">位置：{currentAlert.location}</p>
                <p className="text-xs text-gray-300 mb-1">状态：{currentAlert.status}</p>
                <p className="text-xs text-blue-400">时间：{currentAlert.time}</p>
              </div>

              {/* 处理人 */}
              <div className="mb-4">
                <label className="block text-sm font-medium text-gray-300 mb-2">
                  处理人 <span className="text-red-500">*</span>
                </label>
                <input
                  type="text"
                  value={processingHandler}
                  onChange={(e) => setProcessingHandler(e.target.value)}
                  placeholder="请输入处理人姓名"
                  className="w-full px-3 py-2 bg-gray-700 border border-blue-500/30 rounded text-white placeholder-gray-500 focus:outline-none focus:border-blue-500"
                />
              </div>

              {/* 处理意见 */}
              <div className="mb-6">
                <label className="block text-sm font-medium text-gray-300 mb-2">
                  处理意见 <span className="text-red-500">*</span>
                </label>
                <textarea
                  value={processingNote}
                  onChange={(e) => setProcessingNote(e.target.value)}
                  placeholder="请输入处理意见和采取的措施..."
                  rows={4}
                  className="w-full px-3 py-2 bg-gray-700 border border-blue-500/30 rounded text-white placeholder-gray-500 focus:outline-none focus:border-blue-500 resize-none"
                />
              </div>

              {/* 操作按钮 */}
              <div className="flex gap-3">
                <button
                  onClick={handleConfirmProcess}
                  className="flex-1 flex items-center justify-center gap-2 px-4 py-2.5 bg-green-500 text-white rounded hover:bg-green-600 transition-colors font-medium"
                >
                  <Check className="w-4 h-4" />
                  确认处理
                </button>
                <button
                  onClick={handleCancelProcess}
                  className="flex-1 flex items-center justify-center gap-2 px-4 py-2.5 bg-gray-600 text-white rounded hover:bg-gray-700 transition-colors font-medium"
                >
                  <X className="w-4 h-4" />
                  取消
                </button>
              </div>
            </motion.div>
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}