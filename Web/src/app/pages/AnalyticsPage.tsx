import { useState, useMemo, useEffect } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import {
  ChevronRight, ChevronDown, MapPin, Droplets, Zap, Flame,
  Bell, Clock, Cpu, Activity,
} from 'lucide-react';
import {
  BarChart, Bar, LineChart, Line, XAxis, YAxis, CartesianGrid,
  Tooltip, ResponsiveContainer,
} from 'recharts';
import { useAuth } from '@/app/contexts/AuthContext';
import { useDevices } from '@/app/contexts/DeviceContext';
import { useArea } from '@/app/contexts/AreaContext';
import {
  analyticsApi,
  type EnergyAnalysis,
} from '@/app/services/api/analyticsApi';
import {
  dashboardApi,
  type AlertDto,
} from '@/app/services/api/dashboardApi';

type EnergyType = 'electric' | 'water' | 'gas';

const ENERGY_TYPES: EnergyType[] = ['electric', 'water', 'gas'];

/** 告警类型 → 中文 */
const ALERT_TYPE_LABEL: Record<string, string> = {
  temperature: '温度异常', pressure: '压力异常', offline: '设备离线',
  power: '功率异常', smoke: '烟雾异常', door: '门体异常',
};

/** 真实告警 → 预警汇总展示项 */
interface AlertView {
  id: string;
  type: string;
  level: string;
  deviceName: string;
  description: string;
  time: string;
}
function toAlertView(a: AlertDto): AlertView {
  const d = new Date(a.alertTime);
  const time = Number.isNaN(d.getTime())
    ? String(a.alertTime ?? '')
    : `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')} ${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`;
  return {
    id: String(a.id),
    type: ALERT_TYPE_LABEL[a.alertType] || a.alertType || '异常',
    level: a.level,
    deviceName: a.deviceName || '未知设备',
    description: a.area || a.deviceCode || a.status || '',
    time,
  };
}

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
  const { devices } = useDevices();
  const { areas } = useArea();
  const [selectedArea, setSelectedArea] = useState<string | null>(null);
  const [selectedEnergyType, setSelectedEnergyType] = useState<EnergyType>('electric');
  const [selectedYear, setSelectedYear] = useState<number>(new Date().getFullYear());
  const [selectedMonth, setSelectedMonth] = useState<number>(new Date().getMonth() + 1);
  const [showYearPicker, setShowYearPicker] = useState(false);
  const [showMonthPicker, setShowMonthPicker] = useState(false);
  const [showYearOnlyPicker, setShowYearOnlyPicker] = useState(false);

  // ── 真实后端数据（每个来源独立容错） ──
  const [analysisByType, setAnalysisByType] = useState<Record<EnergyType, EnergyAnalysis | null>>({
    electric: null, water: null, gas: null,
  });
  const [alerts, setAlerts] = useState<AlertView[]>([]);
  const [panelError, setPanelError] = useState<Record<string, string>>({});

  // 选中区域名 → areaId（后端聚合按 areaId 过滤）
  const selectedAreaId = useMemo(() => {
    if (!selectedArea) return undefined;
    const match = areas.find((a: any) => a.name === selectedArea);
    return match ? Number(match.id) : undefined;
  }, [selectedArea, areas]);

  useEffect(() => {
    let cancelled = false;
    const markError = (key: string, msg: string) => {
      if (!cancelled) setPanelError(prev => ({ ...prev, [key]: msg }));
    };
    const clearError = (key: string) => {
      if (!cancelled) setPanelError(prev => {
        const next = { ...prev }; delete next[key]; return next;
      });
    };

    const load = () => {
      // 三种能源各拉一次，卡片汇总 + 图表都用真实数据
      ENERGY_TYPES.forEach((type) => {
        analyticsApi.getEnergyAnalysis({
          energyType: type,
          year: selectedYear,
          month: selectedMonth,
          areaId: selectedAreaId,
        })
          .then((res) => {
            if (cancelled) return;
            if (res.code === 200 && res.data) {
              setAnalysisByType(prev => ({ ...prev, [type]: res.data! }));
              clearError(type);
            } else {
              markError(type, res.message || '能耗数据不可用');
            }
          })
          .catch(() => markError(type, '无权限或网络错误'));
      });

      // 预警汇总：真实告警（优先待处理，空则取最近）
      dashboardApi.getAlerts({ page: 1, pageSize: 5, status: 'pending' })
        .then(async (res) => {
          if (cancelled) return;
          let items = res.code === 200 && res.data ? (res.data.items || []) : [];
          if (items.length === 0) {
            try {
              const fb = await dashboardApi.getAlerts({ page: 1, pageSize: 5 });
              items = fb.code === 200 && fb.data ? (fb.data.items || []) : [];
            } catch { /* 保持空 */ }
          }
          if (!cancelled) { setAlerts(items.map(toAlertView)); clearError('alerts'); }
        })
        .catch(() => markError('alerts', '无权限或网络错误'));
    };

    load();
    const timer = setInterval(load, 60000);
    return () => { cancelled = true; clearInterval(timer); };
  }, [selectedYear, selectedMonth, selectedAreaId]);
  void user; void logout; void currentCustomer;

  // 可选年份列表（2020-2030）
  const availableYears = Array.from({ length: 11 }, (_, i) => 2020 + i);
  
  // 可选月份列表
  const availableMonths = Array.from({ length: 12 }, (_, i) => i + 1);

  // 统计设备状态（真实设备列表）
  const deviceStats = useMemo(() => {
    const total = devices.length;
    const online = devices.filter(d => d.status === 'online').length;
    const warning = devices.filter(d => d.status === 'warning').length;
    const offline = devices.filter(d => d.status === 'offline').length;
    return { total, online, warning, offline };
  }, [devices]);

  // 当前选中能源类型的真实分析数据
  const analysis = analysisByType[selectedEnergyType];
  const dailyData = analysis?.daily ?? [];
  const monthlyData = analysis?.monthly ?? [];
  const yearlyData = analysis?.yearly ?? [];
  const currentUnit = analysis?.unit ?? (selectedEnergyType === 'electric' ? 'kW' : 'm³/h');
  const hasChartData = (analysis?.sampleCount ?? 0) > 0;

  // 用能卡片：三种能源各自的真实当日平均 + 累计采样
  const energyCards = ENERGY_TYPES.map((type) => {
    const a = analysisByType[type];
    return {
      type,
      current: a?.summary?.today ?? 0,
      total: a?.summary?.total ?? 0,
      todayLabel: a?.summary?.todayLabel ?? '当日平均',
      totalLabel: a?.summary?.totalLabel ?? '累计采样',
      unit: a?.unit ?? (type === 'electric' ? 'kW' : 'm³/h'),
      hasData: (a?.sampleCount ?? 0) > 0,
    };
  });

  // 选中区域的真实设备列表（替代原假 3D 场景 + 假设备标注）
  const areaDevices = useMemo(() => {
    if (!selectedArea) return [];
    return devices.filter((d: any) => d.area === selectedArea || d.areaName === selectedArea);
  }, [selectedArea, devices]);

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
          {/* 用能卡片（真实：设备上报采样平均功率/流量 + 累计采样数） */}
          <div className="grid grid-cols-3 gap-4">
            {([
              { key: 'water' as EnergyType, title: '用水', Icon: Droplets, border: 'border-cyan-500/30', glow: 'bg-cyan-500/10', chip: 'bg-cyan-500/20', text: 'text-cyan-400', label: 'text-cyan-400' },
              { key: 'electric' as EnergyType, title: '用电', Icon: Zap, border: 'border-yellow-500/30', glow: 'bg-yellow-500/10', chip: 'bg-yellow-500/20', text: 'text-yellow-400', label: 'text-yellow-400' },
              { key: 'gas' as EnergyType, title: '用气', Icon: Flame, border: 'border-orange-500/30', glow: 'bg-orange-500/10', chip: 'bg-orange-500/20', text: 'text-orange-400', label: 'text-orange-400' },
            ]).map((cfg) => {
              const card = energyCards.find(c => c.type === cfg.key)!;
              const err = panelError[cfg.key];
              return (
                <motion.div
                  key={cfg.key}
                  whileHover={{ scale: 1.02 }}
                  className={`bg-black/40 backdrop-blur-sm border ${cfg.border} rounded-lg p-4 relative overflow-hidden`}
                >
                  <div className={`absolute top-0 right-0 w-24 h-24 ${cfg.glow} rounded-full blur-2xl`} />
                  <div className="relative flex items-center gap-3 mb-2">
                    <div className={`p-2 ${cfg.chip} rounded-lg`}>
                      <cfg.Icon className={`w-6 h-6 ${cfg.text}`} />
                    </div>
                    <div>
                      <p className="text-xs text-gray-400">{cfg.title} · 平均功率</p>
                      <p className="text-2xl font-bold text-white">
                        {card.hasData ? card.current : '—'}
                        <span className="text-sm text-gray-400 ml-0.5">{card.unit}</span>
                      </p>
                    </div>
                  </div>
                  <p className={`text-xs ${cfg.label}`}>
                    {card.todayLabel} {card.hasData ? `${card.current}${card.unit}` : '暂无数据'}
                  </p>
                  <p className="text-xs text-gray-500">
                    {card.totalLabel} {card.total} 条
                  </p>
                  {(err || !card.hasData) && (
                    <p className="text-xs mt-1" style={{ color: err ? '#ffaa00' : '#5a7a90' }}>
                      {err || '未接入该类型计量设备'}
                    </p>
                  )}
                </motion.div>
              );
            })}
          </div>

          {/* 区域设备（真实设备列表，替代原假 3D 场景 + WebGL 白屏） */}
          <div className="flex-1 bg-black/40 backdrop-blur-sm border border-cyan-500/30 rounded-lg p-4 relative overflow-hidden flex flex-col">
            {!selectedArea ? (
              <div className="absolute inset-0 flex items-center justify-center">
                <div className="text-center">
                  <MapPin className="w-16 h-16 text-gray-600 mx-auto mb-4" />
                  <p className="text-gray-400 text-sm">请从左侧区域导航中选择一个区域</p>
                  <p className="text-gray-500 text-xs mt-2">查看该区域的真实设备</p>
                </div>
              </div>
            ) : (
              <div className="w-full h-full flex flex-col">
                <div className="flex items-center justify-between mb-3 pb-2 border-b border-cyan-500/20">
                  <div className="flex items-center gap-2">
                    <Cpu className="w-4 h-4 text-cyan-400" />
                    <h3 className="text-sm font-bold text-cyan-300">{selectedArea}</h3>
                  </div>
                  <span className="text-xs text-gray-400">{areaDevices.length} 台设备</span>
                </div>
                <div className="flex-1 relative bg-gray-900/50 rounded-lg border border-white/10 overflow-y-auto p-3">
                  {areaDevices.length > 0 ? (
                    <div className="grid grid-cols-2 gap-2">
                      {areaDevices.map((d: any) => {
                        const stColor = d.status === 'online' ? '#00ff9f'
                          : d.status === 'warning' ? '#ffaa00'
                          : d.status === 'offline' ? '#ff5555' : '#8aa';
                        return (
                          <div key={d.id} className="rounded-lg p-3 flex items-center gap-3"
                            style={{ background: 'rgba(0,30,60,0.5)', border: '1px solid rgba(0,195,255,0.2)' }}>
                            <div className="w-2 h-2 rounded-full flex-shrink-0"
                              style={{ background: stColor, boxShadow: `0 0 6px ${stColor}` }} />
                            <div className="min-w-0 flex-1">
                              <p className="text-xs font-medium text-white truncate">{d.name}</p>
                              <p className="text-xs text-gray-500 truncate">
                                {d.model || d.category || d.serialNumber || '—'}
                              </p>
                            </div>
                            <span className="text-xs flex-shrink-0" style={{ color: stColor }}>
                              {d.status === 'online' ? '在线' : d.status === 'warning' ? '告警' : d.status === 'offline' ? '离线' : d.status}
                            </span>
                          </div>
                        );
                      })}
                    </div>
                  ) : (
                    <div className="h-full flex flex-col items-center justify-center gap-2">
                      <Activity className="w-10 h-10" style={{ color: '#3a5a70' }} />
                      <p className="text-xs text-gray-500">该区域暂无真实设备</p>
                      <p className="text-xs" style={{ color: '#5a7a90' }}>
                        3D 场景需 CAD/场景配置数据，当前以设备列表展示
                      </p>
                    </div>
                  )}
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
                    <span className="text-xs font-medium text-white">{alert.type}</span>
                  </div>
                  <Clock className="w-3 h-3 text-gray-500" />
                </div>
                <p className="text-xs text-gray-400 mb-1">{alert.deviceName}</p>
                <p className="text-xs text-gray-500 truncate">{alert.description}</p>
                <p className="text-xs text-cyan-400 mt-2">{alert.time || '-'}</p>
              </motion.div>
            ))}
            {alerts.length === 0 && (
              <div className="h-full flex flex-col items-center justify-center gap-2 py-8">
                <Bell className="w-10 h-10" style={{ color: '#3a5a70' }} />
                <p className="text-xs" style={{ color: panelError.alerts ? '#ffaa00' : '#5a7a90' }}>
                  {panelError.alerts || '暂无预警'}
                </p>
              </div>
            )}
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
            <div className="flex-1 bg-black/40 border border-cyan-500/20 rounded-lg p-3 flex flex-col">
              <div className="flex items-center justify-between mb-1 px-1">
                <span className="text-[10px] text-gray-500">单位：{currentUnit}（日平均）</span>
                <span className="text-[10px] text-gray-500">{analysis?.source || '设备真实上报'}</span>
              </div>
              <div className="grid grid-cols-7 gap-1 text-xs flex-1">
                {/* 星期标题 */}
                {['日', '一', '二', '三', '四', '五', '六'].map(w => (
                  <div key={w} className="text-center text-gray-500 flex items-center justify-center">{w}</div>
                ))}
                {/* 日期网格：真实每日聚合 */}
                {dailyData.map((pt) => (
                  <div
                    key={pt.label}
                    title={`${pt.label} 日 · ${pt.sampleCount} 条采样`}
                    className="bg-gradient-to-br from-cyan-500/10 to-cyan-500/5 border border-cyan-500/20 rounded flex flex-col items-center justify-center hover:bg-cyan-500/20 transition-all"
                  >
                    <span className="text-[10px] text-gray-400">{pt.label}</span>
                    <span className="text-[11px] text-cyan-300 font-medium">
                      {pt.sampleCount > 0 ? pt.value : '—'}
                    </span>
                  </div>
                ))}
                {dailyData.length === 0 && (
                  <div className="col-span-7 flex items-center justify-center text-xs" style={{ color: '#5a7a90' }}>
                    {panelError[selectedEnergyType] || '暂无该能源类型上报数据'}
                  </div>
                )}
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
              {hasChartData ? (
                <ResponsiveContainer width="100%" height="100%">
                  <BarChart data={monthlyData}>
                    <CartesianGrid strokeDasharray="3 3" stroke="rgba(6,182,212,0.15)" vertical={false} />
                    <XAxis
                      dataKey="label"
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
                      formatter={(v: number) => [`${Number(v).toFixed(4)} ${currentUnit}`, '平均']}
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
              ) : (
                <div className="h-full flex items-center justify-center text-xs" style={{ color: panelError[selectedEnergyType] ? '#ffaa00' : '#5a7a90' }}>
                  {panelError[selectedEnergyType] || '暂无该能源类型上报数据'}
                </div>
              )}
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
              {hasChartData ? (
                <ResponsiveContainer width="100%" height="100%">
                  <LineChart data={yearlyData}>
                    <CartesianGrid strokeDasharray="3 3" stroke="rgba(6,182,212,0.15)" />
                    <XAxis
                      dataKey="label"
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
                      formatter={(v: number) => [`${Number(v).toFixed(4)} ${currentUnit}`, '平均']}
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
              ) : (
                <div className="h-full flex items-center justify-center text-xs" style={{ color: panelError[selectedEnergyType] ? '#ffaa00' : '#5a7a90' }}>
                  {panelError[selectedEnergyType] || '暂无该能源类型上报数据'}
                </div>
              )}
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}