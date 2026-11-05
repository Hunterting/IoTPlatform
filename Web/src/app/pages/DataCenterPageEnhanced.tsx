import React from 'react';
import { AnimatePresence, motion } from 'motion/react';
import {
  Database,
  Search,
  Activity,
  Eye,
  X,
  Plus,
  Globe,
  Server,
  Play,
  Edit,
  Trash2,
  RotateCw,
  Settings,
} from 'lucide-react';
import { monitoringApi, type MonitoringDataDto } from '../services/api/monitoringApi';

interface DataCenterPageProps {
  devices: any[];
  getEnergyIcon: (type: any) => JSX.Element;
}

/** 监控数据点经扁平化后的单行展示模型 */
interface DataPointRow {
  id: string;
  deviceId?: number;
  deviceName: string;
  areaName?: string;
  energyType?: string;
  metricKey: string; // 原始 sensor key，如 temperature
  metricLabel: string; // 中文展示名
  value: number | string | boolean | null;
  unit: string;
  quality: string; // 良好 / 警告 / 异常 / 正常
  timestamp: string;
}

// 常见指标的中文标签与单位（命中则用，未命中则回退到原始 key 名）
const METRIC_LABEL: Record<string, string> = {
  temperature: '温度',
  humidity: '湿度',
  co2: 'CO₂',
  pm25: 'PM2.5',
  pm10: 'PM10',
  co: 'CO',
  pressure: '气压',
  signal: '信号',
  voltage: '电压',
  current: '电流',
  power: '功率',
  energy: '电量',
  flow: '流量',
  water: '水',
  gas: '燃气',
  electric: '电',
  slot1_voltage: '插槽1电压',
  slot2_voltage: '插槽2电压',
  slot3_voltage: '插槽3电压',
};

const METRIC_UNIT: Record<string, string> = {
  temperature: '°C',
  humidity: '%',
  co2: 'ppm',
  pm25: 'µg/m³',
  pm10: 'µg/m³',
  co: 'ppm',
  pressure: 'hPa',
  signal: 'dBm',
  voltage: 'V',
  current: 'A',
  power: 'W',
  energy: 'kWh',
  flow: 'm³/h',
  water: 'm³',
  gas: 'm³',
  electric: 'kWh',
};

function labelOf(key: string): string {
  return METRIC_LABEL[key.toLowerCase()] ?? key;
}

function unitOf(key: string): string {
  return METRIC_UNIT[key.toLowerCase()] ?? '';
}

function formatValue(v: number | string | boolean | null): string {
  if (v === null || v === undefined) return '—';
  if (typeof v === 'boolean') return v ? '是' : '否';
  if (typeof v === 'number') return v.toFixed(2);
  return String(v);
}

function tryParseJson(s: unknown): unknown | null {
  if (typeof s !== 'string') return null;
  const t = s.trim();
  if (!(t.startsWith('[') || t.startsWith('{'))) return null;
  try {
    return JSON.parse(t);
  } catch {
    return null;
  }
}

/**
 * 把嵌套 JSON 字符串（如 tasks/EMdata）压成紧凑可读摘要，避免超长字符串撑爆表格宽度。
 * 例：tasks=[{"voltage":"227.064","status":"idle","power":"0.161","slotNum":1,"current":"0.008"}]
 *   → "槽1 227.064V 0.161W 0.008A idle"
 */
function summarizeJson(raw: string): string {
  const parsed = tryParseJson(raw);
  if (parsed == null) return raw;
  if (Array.isArray(parsed)) {
    if (parsed.length === 0) return '[]';
    return parsed
      .map((item) => {
        if (item && typeof item === 'object') {
          const segs: string[] = [];
          if (item.slotNum != null) segs.push(`槽${item.slotNum}`);
          if (item.voltage != null) segs.push(`${item.voltage}V`);
          if (item.v != null) segs.push(`${item.v}V`);
          if (item.power != null) segs.push(`${item.power}W`);
          if (item.p != null) segs.push(`${item.p}W`);
          if (item.current != null) segs.push(`${item.current}A`);
          if (item.c != null) segs.push(`${item.c}A`);
          if (item.status != null) segs.push(`${item.status}`);
          return segs.length ? segs.join(' ') : JSON.stringify(item);
        }
        return String(item);
      })
      .join('；');
  }
  if (typeof parsed === 'object') return JSON.stringify(parsed);
  return String(parsed);
}

/** 判断一个值是否为未格式化的 JSON 字符串（需压缩展示） */
function isJsonBlob(v: unknown): v is string {
  return typeof v === 'string' && (v.trim().startsWith('[') || v.trim().startsWith('{'));
}

function qualityClass(q?: string): string {
  switch (q) {
    case '良好':
    case '正常':
      return 'bg-green-500/20 text-green-300';
    case '警告':
      return 'bg-yellow-500/20 text-yellow-300';
    case '异常':
      return 'bg-red-500/20 text-red-300';
    default:
      return 'bg-gray-500/20 text-gray-300';
  }
}

/** 将后端监控记录扁平化为「设备 × 指标」行，便于表格逐点展示当前值 */
function flattenRecords(records: MonitoringDataDto[]): DataPointRow[] {
  const rows: DataPointRow[] = [];
  for (const rec of records) {
    const sensorValues = (rec.sensorValues ?? {}) as Record<string, unknown>;
    const keys = Object.keys(sensorValues);
    const base = {
      deviceId: rec.deviceId,
      deviceName: rec.deviceName ?? `设备#${rec.deviceId ?? rec.id}`,
      areaName: rec.areaName,
      energyType: rec.energyType,
      quality: rec.quality ?? '正常',
      timestamp: rec.timestamp,
    };
    if (keys.length === 0) {
      rows.push({ ...base, id: `dp-${rec.id}`, metricKey: '—', metricLabel: '无数据点', value: null, unit: '' });
    } else {
      for (const key of keys) {
        rows.push({
          ...base,
          id: `dp-${rec.id}-${key}`,
          metricKey: key,
          metricLabel: labelOf(key),
          value: sensorValues[key] as number | string | boolean | null,
          unit: unitOf(key),
        });
      }
    }
  }
  return rows;
}

export function DataCenterPageEnhanced({ devices, getEnergyIcon }: DataCenterPageProps) {
  const [activeTab, setActiveTab] = React.useState<'datapoints' | 'api' | 'connections'>('datapoints');
  const [selectedDevice, setSelectedDevice] = React.useState<any>(null);
  const [searchTerm, setSearchTerm] = React.useState('');
  const [showApiModal, setShowApiModal] = React.useState(false);

  // 真实监控数据（来自 /api/v1/monitoring/data）
  const [dataPoints, setDataPoints] = React.useState<DataPointRow[]>([]);
  const [loading, setLoading] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);
  const [lastUpdated, setLastUpdated] = React.useState<string | null>(null);

  // 分页状态
  const [page, setPage] = React.useState(1);
  const [pageSize, setPageSize] = React.useState(30);
  const [totalPages, setTotalPages] = React.useState(1);
  const [totalCount, setTotalCount] = React.useState(0);

  const loadMonitoringData = React.useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const resp = await monitoringApi.getMonitoringData({ page, pageSize });
      if (resp.code === 200 && resp.data) {
        setDataPoints(flattenRecords(resp.data.items || []));
        setTotalCount(resp.data.totalCount ?? 0);
        setTotalPages(resp.data.totalPages ?? 1);
        setLastUpdated(new Date().toLocaleString());
      } else {
        setError(resp.message || '获取监控数据失败');
      }
    } catch (e: any) {
      setError(e?.message || '网络错误，无法获取监控数据');
    } finally {
      setLoading(false);
    }
  }, [page, pageSize]);

  React.useEffect(() => {
    loadMonitoringData();
  }, [loadMonitoringData]);

  const [apiIntegrations, setApiIntegrations] = React.useState([
    {
      id: 'api-001',
      name: '天气数据API',
      endpoint: 'https://api.weather.com/v1/data',
      method: 'GET',
      status: 'active' as const,
      lastCall: '2026-03-23 14:35:00',
      callCount: 1245,
    },
    {
      id: 'api-002',
      name: '电价查询API',
      endpoint: 'https://api.power.com/v2/price',
      method: 'POST',
      status: 'active' as const,
      lastCall: '2026-03-23 14:30:00',
      callCount: 856,
    },
  ]);

  const [dbConnections] = React.useState([
    {
      id: 'conn-001',
      name: 'MongoDB数据源',
      type: 'MongoDB',
      host: '192.168.1.20',
      status: 'connected',
      recordCount: 45678,
    },
    {
      id: 'conn-002',
      name: 'PostgreSQL数据源',
      type: 'PostgreSQL',
      host: '192.168.1.21',
      status: 'connected',
      recordCount: 23456,
    },
  ]);

  const [apiFormData, setApiFormData] = React.useState({
    name: '',
    endpoint: '',
    method: 'GET',
    headers: '',
    body: '',
  });

  const filteredDataPoints = dataPoints.filter(point =>
    point.deviceName.toLowerCase().includes(searchTerm.toLowerCase()) ||
    (point.energyType || '').toLowerCase().includes(searchTerm.toLowerCase()) ||
    point.metricKey.toLowerCase().includes(searchTerm.toLowerCase()) ||
    point.metricLabel.toLowerCase().includes(searchTerm.toLowerCase())
  );

  // 数据质量：良好 / 正常 视为达标
  const qualityPct = React.useMemo(() => {
    if (dataPoints.length === 0) return 0;
    const ok = dataPoints.filter(d => d.quality === '良好' || d.quality === '正常').length;
    return Math.round((ok / dataPoints.length) * 100);
  }, [dataPoints]);

  const handleAddApi = () => {
    const newApi = {
      id: `api-${Date.now()}`,
      name: apiFormData.name,
      endpoint: apiFormData.endpoint,
      method: apiFormData.method,
      status: 'inactive' as const,
      lastCall: '-',
      callCount: 0,
    };
    setApiIntegrations([...apiIntegrations, newApi]);
    setShowApiModal(false);
    setApiFormData({ name: '', endpoint: '', method: 'GET', headers: '', body: '' });
  };

  const handleTestApi = (id: string) => {
    alert('API测试成功！\n响应时间: 125ms\n状态码: 200');
  };

  const handleDeleteApi = (id: string) => {
    if (confirm('确定要删除此API集成吗？')) {
      setApiIntegrations(apiIntegrations.filter(api => api.id !== id));
    }
  };

  const handleRefreshData = () => {
    loadMonitoringData();
  };

  return (
    <div className="p-6 space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-white flex items-center gap-2">
          <Database className="w-7 h-7 text-blue-500" />
          数据中心
        </h1>
        <p className="text-sm text-gray-400 mt-1">
          实时采集数据、第三方 API 集成、数据库直连、数据管理
        </p>
      </div>

      {/* 标签页导航 */}
      <div className="flex gap-2 border-b border-gray-700">
        <button
          onClick={() => setActiveTab('datapoints')}
          className={`px-4 py-2 text-sm font-medium transition-colors ${
            activeTab === 'datapoints'
              ? 'text-blue-400 border-b-2 border-blue-400'
              : 'text-gray-400 hover:text-gray-300'
          }`}
        >
          <div className="flex items-center gap-2">
            <Database className="w-4 h-4" />
            <span>数据点管理</span>
          </div>
        </button>
        <button
          onClick={() => setActiveTab('api')}
          className={`px-4 py-2 text-sm font-medium transition-colors ${
            activeTab === 'api'
              ? 'text-blue-400 border-b-2 border-blue-400'
              : 'text-gray-400 hover:text-gray-300'
          }`}
        >
          <div className="flex items-center gap-2">
            <Globe className="w-4 h-4" />
            <span>API集成</span>
          </div>
        </button>
        <button
          onClick={() => setActiveTab('connections')}
          className={`px-4 py-2 text-sm font-medium transition-colors ${
            activeTab === 'connections'
              ? 'text-blue-400 border-b-2 border-blue-400'
              : 'text-gray-400 hover:text-gray-300'
          }`}
        >
          <div className="flex items-center gap-2">
            <Server className="w-4 h-4" />
            <span>数据库连接</span>
          </div>
        </button>
      </div>

      {/* 数据点管理标签页 */}
      {activeTab === 'datapoints' && (
        <>
          <div className="flex items-center gap-4">
            <div className="flex-1 relative">
              <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-400" />
              <input
                type="text"
                placeholder="搜索设备、能源类型或数据点..."
                value={searchTerm}
                onChange={(e) => setSearchTerm(e.target.value)}
                className="w-full pl-10 pr-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white placeholder-gray-500 focus:outline-none focus:border-blue-500"
              />
            </div>
            <button
              onClick={handleRefreshData}
              disabled={loading}
              className="px-4 py-2 bg-green-600 hover:bg-green-700 disabled:opacity-50 text-white rounded-lg flex items-center gap-2 transition-colors"
            >
              <RotateCw className={`w-4 h-4 ${loading ? 'animate-spin' : ''}`} />
              <span>{loading ? '加载中...' : '刷新数据'}</span>
            </button>
            <button className="px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded-lg flex items-center gap-2 transition-colors">
              <Activity className="w-4 h-4" />
              <span>实时监控</span>
            </button>
          </div>

          {error && (
            <div className="bg-red-500/10 border border-red-500/30 rounded-lg p-4 text-sm text-red-300">
              {error}
            </div>
          )}

          <div className="bg-blue-500/10 border border-blue-500/30 rounded-lg p-4">
            <div className="flex items-start gap-3">
              <Database className="w-5 h-5 text-blue-400 flex-shrink-0 mt-0.5" />
              <div className="text-sm text-blue-300">
                <p className="font-medium mb-2">数据点统计（实时）</p>
                <div className="grid grid-cols-4 gap-4 text-blue-300/80">
                  <div>
                    <p className="text-xs text-blue-400/60">总数据点</p>
                    <p className="text-lg font-bold text-blue-300">{totalCount}</p>
                  </div>
                  <div>
                    <p className="text-xs text-blue-400/60">采集设备</p>
                    <p className="text-lg font-bold text-blue-300">{devices.length}</p>
                  </div>
                  <div>
                    <p className="text-xs text-blue-400/60">数据质量</p>
                    <p className="text-lg font-bold text-green-300">{qualityPct}%</p>
                  </div>
                  <div>
                    <p className="text-xs text-blue-400/60">最近更新</p>
                    <p className="text-lg font-bold text-blue-300">{lastUpdated || '—'}</p>
                  </div>
                </div>
              </div>
            </div>
          </div>

          <div className="bg-gray-800 border border-gray-700 rounded-lg overflow-hidden">
            <div className="overflow-auto max-h-[55vh]">
              <table className="w-full">
                <thead>
                  <tr className="border-b border-gray-700 bg-gray-900/50 sticky top-0 z-10">
                    <th className="text-left py-3 px-4 text-sm font-medium text-gray-400">设备名称</th>
                    <th className="text-left py-3 px-4 text-sm font-medium text-gray-400">能源类型</th>
                    <th className="text-left py-3 px-4 text-sm font-medium text-gray-400">数据点</th>
                    <th className="text-left py-3 px-4 text-sm font-medium text-gray-400">当前值</th>
                    <th className="text-left py-3 px-4 text-sm font-medium text-gray-400">单位</th>
                    <th className="text-left py-3 px-4 text-sm font-medium text-gray-400">时间戳</th>
                    <th className="text-left py-3 px-4 text-sm font-medium text-gray-400">质量</th>
                    <th className="text-left py-3 px-4 text-sm font-medium text-gray-400">操作</th>
                  </tr>
                </thead>
                <tbody>
                  {filteredDataPoints.length === 0 && !loading && (
                    <tr>
                      <td colSpan={8} className="py-8 text-center text-gray-500">
                        {error ? '加载失败，请点击「刷新数据」重试' : '暂无监控数据，设备上报后将自动出现在此处'}
                      </td>
                    </tr>
                  )}
                  {filteredDataPoints.map((point) => {
                    const displayValue = isJsonBlob(point.value)
                      ? summarizeJson(point.value)
                      : formatValue(point.value);
                    const titleValue =
                      typeof point.value === 'string' ? point.value : String(point.value ?? '');
                    return (
                    <tr key={point.id} className="border-b border-gray-800 hover:bg-gray-700/30">
                      <td className="py-3 px-4 text-white whitespace-nowrap">{point.deviceName}</td>
                      <td className="py-3 px-4">
                        <div className="flex items-center gap-2 whitespace-nowrap">
                          {getEnergyIcon(point.energyType)}
                          <span className="text-white">{point.energyType || '—'}</span>
                        </div>
                      </td>
                      <td className="py-3 px-4 text-white whitespace-nowrap">{point.metricLabel}</td>
                      <td
                        className="py-3 px-4 text-white font-mono max-w-[360px] truncate align-top"
                        title={titleValue}
                      >
                        {displayValue}
                      </td>
                      <td className="py-3 px-4 text-gray-400 whitespace-nowrap">{point.unit}</td>
                      <td className="py-3 px-4 text-gray-400 whitespace-nowrap">{new Date(point.timestamp).toLocaleString()}</td>
                      <td className="py-3 px-4">
                        <span className={`px-2 py-1 rounded text-xs font-medium ${qualityClass(point.quality)}`}>
                          {point.quality || '正常'}
                        </span>
                      </td>
                      <td className="py-3 px-4">
                        <button
                          onClick={() => setSelectedDevice(devices.find(d => d.id === point.deviceId))}
                          className="p-1 bg-blue-500/20 hover:bg-blue-500/30 text-blue-300 rounded transition-colors"
                        >
                          <Eye className="w-4 h-4" />
                        </button>
                      </td>
                    </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          </div>

          {/* 分页控制 */}
          <div className="flex flex-wrap items-center justify-between gap-3 mt-4">
            <span className="text-sm text-gray-400">
              共 {totalCount} 条 · 第 {page} / {totalPages} 页
              {filteredDataPoints.length !== dataPoints.length && (
                <span className="text-gray-500"> （当前页匹配 {filteredDataPoints.length} 条）</span>
              )}
            </span>
            <div className="flex items-center gap-2">
              <select
                value={pageSize}
                onChange={(e) => {
                  setPageSize(Number(e.target.value));
                  setPage(1);
                }}
                className="px-2 py-1.5 bg-gray-800 border border-gray-700 rounded-lg text-white text-sm focus:outline-none focus:border-blue-500"
                title="每页条数"
              >
                {[10, 20, 30, 50, 100].map((n) => (
                  <option key={n} value={n}>{n} 条/页</option>
                ))}
              </select>
              <button
                onClick={() => setPage(1)}
                disabled={page <= 1}
                className="px-3 py-1.5 bg-gray-800 border border-gray-700 rounded-lg text-white text-sm hover:bg-gray-700 disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
              >
                首页
              </button>
              <button
                onClick={() => setPage((p) => Math.max(1, p - 1))}
                disabled={page <= 1}
                className="px-3 py-1.5 bg-gray-800 border border-gray-700 rounded-lg text-white text-sm hover:bg-gray-700 disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
              >
                上一页
              </button>
              <button
                onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
                disabled={page >= totalPages}
                className="px-3 py-1.5 bg-gray-800 border border-gray-700 rounded-lg text-white text-sm hover:bg-gray-700 disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
              >
                下一页
              </button>
              <button
                onClick={() => setPage(totalPages)}
                disabled={page >= totalPages}
                className="px-3 py-1.5 bg-gray-800 border border-gray-700 rounded-lg text-white text-sm hover:bg-gray-700 disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
              >
                末页
              </button>
            </div>
          </div>
        </>
      )}

      {/* API集成标签页 */}
      {activeTab === 'api' && (
        <>
          <div className="flex justify-between items-center">
            <p className="text-sm text-gray-400">
              已配置 {apiIntegrations.length} 个第三方API接口
            </p>
            <button
              onClick={() => setShowApiModal(true)}
              className="px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded-lg flex items-center gap-2 transition-colors"
            >
              <Plus className="w-4 h-4" />
              <span>添加API</span>
            </button>
          </div>

          <div className="space-y-4">
            {apiIntegrations.map((api) => (
              <div key={api.id} className="bg-gray-800 border border-gray-700 rounded-lg p-4">
                <div className="flex items-center justify-between">
                  <div className="flex-1">
                    <div className="flex items-center gap-3 mb-2">
                      <Globe className="w-5 h-5 text-green-500" />
                      <h3 className="text-lg font-semibold text-white">{api.name}</h3>
                      <span className={`px-2 py-1 rounded text-xs font-medium ${
                        api.status === 'active'
                          ? 'bg-green-500/20 text-green-300'
                          : 'bg-gray-500/20 text-gray-300'
                      }`}>
                        {api.status === 'active' ? '活跃' : '未激活'}
                      </span>
                      <span className="px-2 py-1 rounded text-xs font-medium bg-blue-500/20 text-blue-300">
                        {api.method}
                      </span>
                    </div>
                    <div className="grid grid-cols-3 gap-4 text-sm">
                      <div>
                        <span className="text-gray-400">接口地址：</span>
                        <span className="text-white ml-1 font-mono text-xs">{api.endpoint}</span>
                      </div>
                      <div>
                        <span className="text-gray-400">最后调用：</span>
                        <span className="text-white ml-1">{api.lastCall}</span>
                      </div>
                      <div>
                        <span className="text-gray-400">调用次数：</span>
                        <span className="text-white ml-1">{api.callCount.toLocaleString()}</span>
                      </div>
                    </div>
                  </div>

                  <div className="flex items-center gap-2 ml-4">
                    <button
                      onClick={() => handleTestApi(api.id)}
                      className="p-2 bg-green-500/20 hover:bg-green-500/30 text-green-300 rounded-lg transition-colors"
                      title="测试API"
                    >
                      <Play className="w-4 h-4" />
                    </button>
                    <button className="p-2 bg-blue-500/20 hover:bg-blue-500/30 text-blue-300 rounded-lg transition-colors">
                      <Edit className="w-4 h-4" />
                    </button>
                    <button
                      onClick={() => handleDeleteApi(api.id)}
                      className="p-2 bg-red-500/20 hover:bg-red-500/30 text-red-300 rounded-lg transition-colors"
                    >
                      <Trash2 className="w-4 h-4" />
                    </button>
                  </div>
                </div>
              </div>
            ))}
          </div>

          {/* 添加API弹窗 */}
          <AnimatePresence>
            {showApiModal && (
              <motion.div
                initial={{ opacity: 0 }}
                animate={{ opacity: 1 }}
                exit={{ opacity: 0 }}
                className="fixed inset-0 bg-black/60 backdrop-blur-sm flex items-center justify-center z-50 p-4"
                onClick={() => setShowApiModal(false)}
              >
                <motion.div
                  initial={{ scale: 0.9, y: 20 }}
                  animate={{ scale: 1, y: 0 }}
                  exit={{ scale: 0.9, y: 20 }}
                  onClick={(e) => e.stopPropagation()}
                  className="bg-gray-800 border border-gray-700 rounded-xl max-w-2xl w-full p-6"
                >
                  <div className="flex items-center justify-between mb-6">
                    <h3 className="text-xl font-bold text-white">添加API集成</h3>
                    <button onClick={() => setShowApiModal(false)} className="p-2 hover:bg-gray-700 rounded-lg transition-colors">
                      <X className="w-5 h-5 text-gray-400" />
                    </button>
                  </div>

                  <div className="space-y-4">
                    <div>
                      <label className="block text-sm font-medium text-gray-400 mb-2">API名称</label>
                      <input
                        type="text"
                        value={apiFormData.name}
                        onChange={(e) => setApiFormData({ ...apiFormData, name: e.target.value })}
                        placeholder="例如：天气数据API"
                        className="w-full px-4 py-2 bg-gray-900 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-blue-500"
                      />
                    </div>

                    <div>
                      <label className="block text-sm font-medium text-gray-400 mb-2">接口地址</label>
                      <input
                        type="text"
                        value={apiFormData.endpoint}
                        onChange={(e) => setApiFormData({ ...apiFormData, endpoint: e.target.value })}
                        placeholder="https://api.example.com/v1/data"
                        className="w-full px-4 py-2 bg-gray-900 border border-gray-700 rounded-lg text-white font-mono text-sm focus:outline-none focus:border-blue-500"
                      />
                    </div>

                    <div>
                      <label className="block text-sm font-medium text-gray-400 mb-2">请求方法</label>
                      <select
                        value={apiFormData.method}
                        onChange={(e) => setApiFormData({ ...apiFormData, method: e.target.value })}
                        className="w-full px-4 py-2 bg-gray-900 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-blue-500"
                      >
                        <option>GET</option>
                        <option>POST</option>
                        <option>PUT</option>
                        <option>DELETE</option>
                      </select>
                    </div>

                    <div>
                      <label className="block text-sm font-medium text-gray-400 mb-2">请求头 (JSON)</label>
                      <textarea
                        value={apiFormData.headers}
                        onChange={(e) => setApiFormData({ ...apiFormData, headers: e.target.value })}
                        placeholder='{"Authorization": "Bearer token", "Content-Type": "application/json"}'
                        rows={3}
                        className="w-full px-4 py-2 bg-gray-900 border border-gray-700 rounded-lg text-white font-mono text-sm focus:outline-none focus:border-blue-500"
                      />
                    </div>

                    {(apiFormData.method === 'POST' || apiFormData.method === 'PUT') && (
                      <div>
                        <label className="block text-sm font-medium text-gray-400 mb-2">请求体 (JSON)</label>
                        <textarea
                          value={apiFormData.body}
                          onChange={(e) => setApiFormData({ ...apiFormData, body: e.target.value })}
                          placeholder='{"key": "value"}'
                          rows={4}
                          className="w-full px-4 py-2 bg-gray-900 border border-gray-700 rounded-lg text-white font-mono text-sm focus:outline-none focus:border-blue-500"
                        />
                      </div>
                    )}

                    <div className="flex gap-3 pt-4">
                      <button
                        onClick={() => setShowApiModal(false)}
                        className="flex-1 px-6 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded-lg transition-colors"
                      >
                        取消
                      </button>
                      <button
                        onClick={handleAddApi}
                        className="flex-1 px-6 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded-lg transition-colors"
                      >
                        添加
                      </button>
                    </div>
                  </div>
                </motion.div>
              </motion.div>
            )}
          </AnimatePresence>
        </>
      )}

      {/* 数据库连接标签页 */}
      {activeTab === 'connections' && (
        <>
          <div className="flex justify-between items-center">
            <p className="text-sm text-gray-400">
              已配置 {dbConnections.length} 个外部数据库连接
            </p>
            <button className="px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded-lg flex items-center gap-2 transition-colors">
              <Plus className="w-4 h-4" />
              <span>添加连接</span>
            </button>
          </div>

          <div className="space-y-4">
            {dbConnections.map((conn) => (
              <div key={conn.id} className="bg-gray-800 border border-gray-700 rounded-lg p-4">
                <div className="flex items-center justify-between">
                  <div className="flex-1">
                    <div className="flex items-center gap-3 mb-2">
                      <Server className="w-5 h-5 text-purple-500" />
                      <h3 className="text-lg font-semibold text-white">{conn.name}</h3>
                      <span className="px-2 py-1 rounded text-xs font-medium bg-green-500/20 text-green-300">
                        {conn.status === 'connected' ? '已连接' : '未连接'}
                      </span>
                      <span className="px-2 py-1 rounded text-xs font-medium bg-purple-500/20 text-purple-300">
                        {conn.type}
                      </span>
                    </div>
                    <div className="grid grid-cols-3 gap-4 text-sm">
                      <div>
                        <span className="text-gray-400">主机地址：</span>
                        <span className="text-white ml-1">{conn.host}</span>
                      </div>
                      <div>
                        <span className="text-gray-400">状态：</span>
                        <span className="text-green-300 ml-1">正常</span>
                      </div>
                      <div>
                        <span className="text-gray-400">记录数：</span>
                        <span className="text-white ml-1">{conn.recordCount.toLocaleString()}</span>
                      </div>
                    </div>
                  </div>

                  <div className="flex items-center gap-2 ml-4">
                    <button className="p-2 bg-green-500/20 hover:bg-green-500/30 text-green-300 rounded-lg transition-colors">
                      <RotateCw className="w-4 h-4" />
                    </button>
                    <button className="p-2 bg-blue-500/20 hover:bg-blue-500/30 text-blue-300 rounded-lg transition-colors">
                      <Settings className="w-4 h-4" />
                    </button>
                    <button className="p-2 bg-red-500/20 hover:bg-red-500/30 text-red-300 rounded-lg transition-colors">
                      <Trash2 className="w-4 h-4" />
                    </button>
                  </div>
                </div>
              </div>
            ))}
          </div>

          <div className="bg-purple-500/10 border border-purple-500/30 rounded-lg p-4">
            <div className="flex items-start gap-3">
              <Server className="w-5 h-5 text-purple-400 flex-shrink-0 mt-0.5" />
              <div className="text-sm text-purple-300">
                <p className="font-medium mb-2">支持的数据库类型</p>
                <div className="grid grid-cols-2 gap-2 text-purple-300/80 text-xs">
                  <div>• MongoDB - 文档数据库</div>
                  <div>• PostgreSQL - 关系型数据库</div>
                  <div>• Redis - 缓存数据库</div>
                  <div>• Elasticsearch - 搜索引擎</div>
                  <div>• Cassandra - 列式数据库</div>
                  <div>• Neo4j - 图数据库</div>
                </div>
              </div>
            </div>
          </div>
        </>
      )}

      {/* 设备详情弹窗 */}
      <AnimatePresence>
        {selectedDevice && (
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            className="fixed inset-0 bg-black/60 backdrop-blur-sm flex items-center justify-center z-50 p-4"
            onClick={() => setSelectedDevice(null)}
          >
            <motion.div
              initial={{ scale: 0.9, y: 20 }}
              animate={{ scale: 1, y: 0 }}
              exit={{ scale: 0.9, y: 20 }}
              onClick={(e) => e.stopPropagation()}
              className="bg-gray-800 border border-gray-700 rounded-xl max-w-2xl w-full p-6"
            >
              <div className="flex items-center justify-between mb-6">
                <h3 className="text-xl font-bold text-white">设备详情</h3>
                <button onClick={() => setSelectedDevice(null)} className="p-2 hover:bg-gray-700 rounded-lg transition-colors">
                  <X className="w-5 h-5 text-gray-400" />
                </button>
              </div>

              <div className="grid grid-cols-2 gap-4">
                <div className="bg-gray-900/50 p-4 rounded-lg">
                  <p className="text-sm text-gray-400 mb-1">设备名称</p>
                  <p className="text-white">{selectedDevice.name}</p>
                </div>
                <div className="bg-gray-900/50 p-4 rounded-lg">
                  <p className="text-sm text-gray-400 mb-1">能源类型</p>
                  <div className="flex items-center gap-2">
                    {getEnergyIcon(selectedDevice.energyType)}
                    <p className="text-white">{selectedDevice.energyType}</p>
                  </div>
                </div>
                <div className="bg-gray-900/50 p-4 rounded-lg">
                  <p className="text-sm text-gray-400 mb-1">设备型号</p>
                  <p className="text-white">{selectedDevice.model || 'N/A'}</p>
                </div>
                <div className="bg-gray-900/50 p-4 rounded-lg">
                  <p className="text-sm text-gray-400 mb-1">所属区域</p>
                  <p className="text-white">{selectedDevice.area}</p>
                </div>
              </div>
            </motion.div>
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}
