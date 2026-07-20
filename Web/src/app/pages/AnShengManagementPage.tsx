import { useState, useEffect, useCallback, useMemo } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import {
  Search, RefreshCw, CheckCircle, XCircle, Clock,
  Eye, Power, Wrench, RotateCcw, Upload, Terminal,
  Zap, Radio, RadioTower, Smartphone, Hash,
  ChevronLeft, ChevronRight, AlertCircle, Loader2,
  Send, Play, Ban, Plus, Cpu, Activity,
  Settings, Info, Filter, X, Download,
} from 'lucide-react';
import { useAuth } from '@/app/contexts/AuthContext';
import { PERMISSIONS } from '@/app/config/permissions';
import { anshengApi } from '@/app/services/api/anshengApi';
import { deviceApi } from '@/app/services/api/deviceApi';
import type {
  DiscoveredAnShengDeviceDto,
  AnShengCommandResponse,
} from '@/app/services/api/types/ansheng.types';
import type { DeviceDto } from '@/app/services/api/types/device.types';

// ── 安圣命令模板 ──────────────────────────────────────────────────
interface CommandTemplate {
  method: string;
  label: string;
  icon: React.ReactNode;
  description: string;
  color: string;
  params: ParamField[];
}

interface ParamField {
  key: string;
  label: string;
  type: 'text' | 'number' | 'select';
  options?: string[];
  defaultValue?: string;
  placeholder?: string;
}

const ANSHENG_COMMAND_TEMPLATES: CommandTemplate[] = [
  {
    method: 'getDevStatus',
    label: '查询设备状态',
    icon: <Activity className="w-4 h-4" />,
    description: '获取设备温度、电量计等实时状态',
    color: 'blue',
    params: [],
  },
  {
    method: 'getDevInfo',
    label: '查询设备信息',
    icon: <Info className="w-4 h-4" />,
    description: '获取设备型号、网络类型等基础信息',
    color: 'cyan',
    params: [],
  },
  {
    method: 'getEMRealtime',
    label: '实时电量查询',
    icon: <Zap className="w-4 h-4" />,
    description: '获取各插槽实时电压、电流、功率',
    color: 'amber',
    params: [],
  },
  {
    method: 'orderStart',
    label: '开始充电',
    icon: <Play className="w-4 h-4" />,
    description: '启动指定插槽充电',
    color: 'green',
    params: [
      { key: 'slot', label: '插槽编号', type: 'number', defaultValue: '1', placeholder: '1' },
    ],
  },
  {
    method: 'orderEnd',
    label: '停止充电',
    icon: <Ban className="w-4 h-4" />,
    description: '停止指定插槽充电',
    color: 'red',
    params: [
      { key: 'slot', label: '插槽编号', type: 'number', defaultValue: '1', placeholder: '1' },
    ],
  },
  {
    method: 'orderUp',
    label: '订单推送',
    icon: <Upload className="w-4 h-4" />,
    description: '向设备推送完整的充电订单',
    color: 'purple',
    params: [
      { key: 'orderId', label: '订单号', type: 'text', placeholder: '例如：ORD123456' },
      { key: 'slot', label: '插槽编号', type: 'number', defaultValue: '1', placeholder: '1' },
      { key: 'durationMin', label: '充电时长(分)', type: 'number', defaultValue: '60', placeholder: '60' },
    ],
  },
  {
    method: 'setAutoReport',
    label: '设置自动上报',
    icon: <Settings className="w-4 h-4" />,
    description: '配置设备定时上报间隔',
    color: 'slate',
    params: [
      { key: 'getDevStatusSec', label: '状态上报间隔(秒)', type: 'number', defaultValue: '60', placeholder: '60' },
      { key: 'orderUpSec', label: '订单推送间隔(秒)', type: 'number', defaultValue: '300', placeholder: '300' },
    ],
  },
  {
    method: 'reboot',
    label: '重启设备',
    icon: <RotateCcw className="w-4 h-4" />,
    description: '远程重启设备',
    color: 'orange',
    params: [],
  },
];

// ── 二开设备开关命令模板 ────────────────────────────────────────────
const OPEN_DEVICE_COMMAND_TEMPLATES: CommandTemplate[] = [
  {
    method: 'setSwitch',
    label: '控制开关',
    icon: <Power className="w-4 h-4" />,
    description: '控制指定开关通断',
    color: 'green',
    params: [
      { key: 'switch', label: '开关编号', type: 'number', defaultValue: '1', placeholder: '1' },
      { key: 'on', label: '动作', type: 'select', options: ['开', '关'], defaultValue: '开' },
    ],
  },
  {
    method: 'getSwitchStatus',
    label: '查询开关状态',
    icon: <Activity className="w-4 h-4" />,
    description: '查询开关当前通断状态',
    color: 'blue',
    params: [
      { key: 'switch', label: '开关编号', type: 'number', defaultValue: '1', placeholder: '1' },
    ],
  },
  {
    method: 'setSwitchConfig',
    label: '配置开关',
    icon: <Settings className="w-4 h-4" />,
    description: '配置开关定时/参数',
    color: 'amber',
    params: [
      { key: 'switch', label: '开关编号', type: 'number', defaultValue: '1', placeholder: '1' },
      { key: 'name', label: '开关名称', type: 'text', placeholder: '如：路灯1' },
    ],
  },
  {
    method: 'getSwitchConfig',
    label: '查询开关配置',
    icon: <Info className="w-4 h-4" />,
    description: '查询开关详细配置',
    color: 'cyan',
    params: [
      { key: 'switch', label: '开关编号', type: 'number', defaultValue: '1', placeholder: '1' },
    ],
  },
  {
    method: 'reboot',
    label: '重启设备',
    icon: <RotateCcw className="w-4 h-4" />,
    description: '远程重启二开设备',
    color: 'orange',
    params: [],
  },
  {
    method: 'getDevInfo',
    label: '查询设备信息',
    icon: <Info className="w-4 h-4" />,
    description: '获取设备型号、网络类型等',
    color: 'slate',
    params: [],
  },
  {
    method: 'getDevStatus',
    label: '查询设备状态',
    icon: <Activity className="w-4 h-4" />,
    description: '获取设备实时状态',
    color: 'purple',
    params: [],
  },
];

// ── 状态徽章 ─────────────────────────────────────────────────────
function getClaimStatusBadge(isClaimed: boolean) {
  if (isClaimed) {
    return (
      <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-medium bg-green-500/20 text-green-400 border border-green-500/30">
        <CheckCircle className="w-3 h-3" /> 已认领
      </span>
    );
  }
  return (
    <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-medium bg-amber-500/20 text-amber-400 border border-amber-500/30">
      <Clock className="w-3 h-3" /> 待认领
    </span>
  );
}

function getOnlineBadge(lastSeenAt: string | null | undefined) {
  if (!lastSeenAt) {
    return (
      <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-medium bg-gray-500/20 text-gray-400 border border-gray-500/30">
        <XCircle className="w-3 h-3" /> 离线
      </span>
    );
  }
  const lastSeen = new Date(lastSeenAt);
  const now = new Date();
  const diffMin = (now.getTime() - lastSeen.getTime()) / 60000;
  if (diffMin < 10) {
    return (
      <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-medium bg-green-500/20 text-green-400 border border-green-500/30">
        <CheckCircle className="w-3 h-3" /> 在线
      </span>
    );
  }
  return (
    <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-medium bg-amber-500/20 text-amber-400 border border-amber-500/30">
      <Clock className="w-3 h-3" /> 离线
    </span>
  );
}

// ── 主组件 ───────────────────────────────────────────────────────
export function AnShengManagementPage() {
  const { hasPermission } = useAuth();
  const canView   = hasPermission(PERMISSIONS.VIEW_DEVICES);
  const canCreate = hasPermission(PERMISSIONS.CREATE_DEVICES);
  const canUpdate = hasPermission(PERMISSIONS.UPDATE_DEVICES);
  const canSend   = hasPermission(PERMISSIONS.SEND_DEVICE_COMMANDS);

  // Tab 状态
  const [activeTab, setActiveTab] = useState<'discovered' | 'commands' | 'opendevice'>('discovered');

  // ── 待认领设备状态 ─────────────────────────────────────────────
  const [discoveredDevices, setDiscoveredDevices] = useState<DiscoveredAnShengDeviceDto[]>([]);
  const [discoveredLoading, setDiscoveredLoading] = useState(false);
  const [discoveredError, setDiscoveredError] = useState<string | null>(null);
  const [discoveredPage, setDiscoveredPage] = useState(1);
  const [discoveredTotal, setDiscoveredTotal] = useState(0);
  const [discoveredSearch, setDiscoveredSearch] = useState('');
  const [claimedFilter, setClaimedFilter] = useState<boolean | null>(null);
  const [scanning, setScanning] = useState(false);

  // 认领弹窗
  const [showClaimModal, setShowClaimModal] = useState(false);
  const [claimingDevice, setClaimingDevice] = useState<DiscoveredAnShengDeviceDto | null>(null);
  const [claimForm, setClaimForm] = useState({
    deviceName: '',
    model: '',
    category: 'power',
    location: '',
    energyTypes: 'electric',
  });
  const [claiming, setClaiming] = useState(false);
  const [claimResult, setClaimResult] = useState<{ success: boolean; message: string } | null>(null);

  // ── 命令面板状态 ──────────────────────────────────────────────
  const [anshengDevices, setAnshengDevices] = useState<DeviceDto[]>([]);
  const [anshengDevicesLoading, setAnshengDevicesLoading] = useState(false);
  const [selectedDeviceId, setSelectedDeviceId] = useState<number | null>(null);
  const [selectedTemplate, setSelectedTemplate] = useState<CommandTemplate>(ANSHENG_COMMAND_TEMPLATES[0]);
  const [paramValues, setParamValues] = useState<Record<string, string>>({});
  const [sending, setSending] = useState(false);
  const [sendResult, setSendResult] = useState<{ success: boolean; message: string } | null>(null);
  const [commandLog, setCommandLog] = useState<Array<{ time: Date; method: string; success: boolean; message: string }>>([]);

  // ── 分页 ──────────────────────────────────────────────────────
  const pageSize = 10;
  const discoveredTotalPages = Math.ceil(discoveredTotal / pageSize);

  // ── 加载待认领设备 ───────────────────────────────────────────
  const loadDiscoveredDevices = useCallback(async () => {
    setDiscoveredLoading(true);
    setDiscoveredError(null);
    try {
      const params: any = { page: discoveredPage, pageSize };
      if (discoveredSearch) params.keyword = discoveredSearch;
      if (claimedFilter !== null) params.claimed = claimedFilter;

      const response = await anshengApi.getDiscoveredDevices(params);
      if (response.data.success && response.data.data) {
        setDiscoveredDevices(response.data.data.items || []);
        setDiscoveredTotal(response.data.data.totalCount || 0);
      } else {
        setDiscoveredDevices([]);
        setDiscoveredTotal(0);
      }
    } catch (err: any) {
      setDiscoveredError(err?.response?.data?.message || '加载待认领设备列表失败');
      setDiscoveredDevices([]);
    } finally {
      setDiscoveredLoading(false);
    }
  }, [discoveredPage, discoveredSearch, claimedFilter]);

  // ── 触发发现扫描 ────────────────────────────────────────────
  const handleTriggerDiscovery = async () => {
    setScanning(true);
    try {
      await anshengApi.triggerDiscovery();
      await loadDiscoveredDevices();
      setCommandLog(prev => [...prev, {
        time: new Date(),
        method: 'triggerDiscovery',
        success: true,
        message: '设备发现扫描已触发',
      }]);
    } catch (err: any) {
      setCommandLog(prev => [...prev, {
        time: new Date(),
        method: 'triggerDiscovery',
        success: false,
        message: err?.response?.data?.message || '扫描触发失败',
      }]);
    } finally {
      setScanning(false);
    }
  };

  // ── 加载安圣设备列表（用于命令面板）─────────────────────────
  const loadAnShengDevices = useCallback(async () => {
    setAnshengDevicesLoading(true);
    try {
      const response = await deviceApi.getDevices(1, 200);
      if (response.data.success && response.data.data) {
        const all = response.data.data.items || [];
        // 筛选出安圣设备（SerialNumber 为纯数字 IMEI 格式）
        const ansheng = all.filter(d => d.serialNumber && /^\d{10,20}$/.test(d.serialNumber));
        setAnshengDevices(ansheng);
      }
    } catch (err) {
      console.error('加载安圣设备列表失败:', err);
    } finally {
      setAnshengDevicesLoading(false);
    }
  }, []);

  // ── 初始加载 ─────────────────────────────────────────────────
  useEffect(() => {
    loadDiscoveredDevices();
    loadAnShengDevices();
  }, [loadDiscoveredDevices, loadAnShengDevices]);

  // ── 搜索时重置分页 ──────────────────────────────────────────
  useEffect(() => {
    setDiscoveredPage(1);
  }, [discoveredSearch, claimedFilter]);

  // ── 认领设备 ─────────────────────────────────────────────────
  const handleClaimClick = (device: DiscoveredAnShengDeviceDto) => {
    setClaimingDevice(device);
    setClaimForm({
      deviceName: `安圣-${device.imei.substring(device.imei.length - 6)}`,
      model: device.model || '安圣充电桩',
      category: 'power',
      location: '',
      energyTypes: 'electric',
    });
    setClaimResult(null);
    setShowClaimModal(true);
  };

  const handleClaimSubmit = async () => {
    if (!claimingDevice || !claimForm.deviceName.trim()) {
      setClaimResult({ success: false, message: '请输入设备名称' });
      return;
    }
    setClaiming(true);
    setClaimResult(null);
    try {
      const response = await anshengApi.claimDevice({
        discoveredDeviceId: claimingDevice.id,
        deviceName: claimForm.deviceName.trim(),
        model: claimForm.model || undefined,
        category: claimForm.category,
        location: claimForm.location || undefined,
        energyTypes: claimForm.energyTypes,
      });
      if (response.data.success && response.data.data?.success) {
        setClaimResult({ success: true, message: `设备 "${response.data.data.deviceName}" 认领成功` });
        await loadDiscoveredDevices();
        await loadAnShengDevices();
        // 自动关闭弹窗
        setTimeout(() => {
          setShowClaimModal(false);
          setClaimingDevice(null);
        }, 1500);
      } else {
        setClaimResult({ success: false, message: response.data.data?.errorMessage || response.data.message || '认领失败' });
      }
    } catch (err: any) {
      setClaimResult({ success: false, message: err?.response?.data?.message || '认领请求失败' });
    } finally {
      setClaiming(false);
    }
  };

  // ── 选择命令模板 ────────────────────────────────────────────
  const selectTemplate = (tpl: CommandTemplate) => {
    setSelectedTemplate(tpl);
    const defaults: Record<string, string> = {};
    tpl.params.forEach(p => {
      if (p.defaultValue !== undefined) defaults[p.key] = String(p.defaultValue);
    });
    setParamValues(defaults);
    setSendResult(null);
  };

  // ── 发送安圣命令 ────────────────────────────────────────────
  const handleSendCommand = async () => {
    if (!selectedDeviceId) return;
    setSending(true);
    setSendResult(null);

    try {
      // 二开设备命令 — 按 method 路由到专用 API
      const isOpenDevice = activeTab === 'opendevice';
      const method = selectedTemplate.method;
      let response: { data: { success: boolean; message?: string; data?: AnShengCommandResponse } };

      if (isOpenDevice && method === 'setSwitch') {
        const switchId = Number(paramValues['switch'] || '1');
        const isOn = paramValues['on'] !== '关'; // 默认"开"=true
        response = await anshengApi.controlSwitch({ deviceId: selectedDeviceId, switchId, on: isOn }) as any;
      } else if (isOpenDevice && method === 'getSwitchStatus') {
        const switchId = paramValues['switch'] ? Number(paramValues['switch']) : undefined;
        response = await anshengApi.getSwitchStatus(selectedDeviceId, switchId) as any;
      } else if (isOpenDevice && method === 'setSwitchConfig') {
        const switchId = Number(paramValues['switch'] || '1');
        const config: Record<string, unknown> = {};
        if (paramValues['name']) config['name'] = paramValues['name'];
        response = await anshengApi.configureSwitch({ deviceId: selectedDeviceId, switchId, config }) as any;
      } else if (isOpenDevice && method === 'reboot') {
        response = await anshengApi.rebootDevice(selectedDeviceId) as any;
      } else {
        // 通用命令（getDevInfo、getSwitchConfig、getDevStatus 等）
        let params: Record<string, unknown> = {};
        selectedTemplate.params.forEach(p => {
          const val = paramValues[p.key];
          if (val !== undefined && val !== '') {
            params[p.key] = p.type === 'number' ? Number(val) : val;
          }
        });
        response = await anshengApi.sendCommand({
          deviceId: selectedDeviceId,
          method: selectedTemplate.method,
          params: Object.keys(params).length > 0 ? params : undefined,
        }) as any;
      }

      if (response.data.success) {
        setSendResult({ success: true, message: `指令 ${selectedTemplate.method} 已下发` });
        setCommandLog(prev => [{
          time: new Date(),
          method: selectedTemplate.method,
          success: true,
          message: `指令已下发 (deviceId=${selectedDeviceId})`,
        }, ...prev].slice(0, 50));
      } else {
        setSendResult({ success: false, message: response.data.message || '命令发送失败' });
        setCommandLog(prev => [{
          time: new Date(),
          method: selectedTemplate.method,
          success: false,
          message: response.data.message || '失败',
        }, ...prev].slice(0, 50));
      }
    } catch (err: any) {
      const msg = err?.response?.data?.message || '网络请求失败';
      setSendResult({ success: false, message: msg });
      setCommandLog(prev => [{
        time: new Date(),
        method: selectedTemplate.method,
        success: false,
        message: msg,
      }, ...prev].slice(0, 50));
    } finally {
      setSending(false);
    }
  };

  // ── 时间格式化 ────────────────────────────────────────────────
  const formatTime = (timeStr: string | null | undefined) => {
    if (!timeStr) return '—';
    return new Date(timeStr).toLocaleString('zh-CN', {
      month: '2-digit', day: '2-digit',
      hour: '2-digit', minute: '2-digit',
    });
  };

  const formatLogTime = (date: Date) => {
    return date.toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit', second: '2-digit' });
  };

  // 选中的安圣设备
  const selectedDevice = useMemo(
    () => anshengDevices.find(d => Number(d.id) === selectedDeviceId),
    [anshengDevices, selectedDeviceId]
  );

  return (
    <div className="p-6 space-y-6">
      {/* 页面标题 */}
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-3">
          <div className="p-2 rounded-lg bg-blue-500/20 border border-blue-500/30">
            <Radio className="w-6 h-6 text-blue-400" />
          </div>
          <div>
            <h1 className="text-2xl font-bold text-white">安圣设备管理</h1>
            <p className="text-sm text-slate-400">MQTT 充电设备接入 · 设备发现与命令控制</p>
          </div>
        </div>
        <button
          onClick={handleTriggerDiscovery}
          disabled={scanning}
          className="inline-flex items-center gap-2 px-4 py-2 rounded-lg bg-blue-600/20 border border-blue-500/30 
                     text-blue-400 hover:bg-blue-600/30 disabled:opacity-50 disabled:cursor-not-allowed transition-all"
        >
          {scanning ? (
            <><Loader2 className="w-4 h-4 animate-spin" /> 扫描中...</>
          ) : (
            <><RadioTower className="w-4 h-4" /> 发现设备</>
          )}
        </button>
      </div>

      {/* Tab 切换 */}
      <div className="flex gap-1 p-1 rounded-lg bg-slate-800/50 border border-slate-700/50 w-fit">
        <button
          onClick={() => setActiveTab('discovered')}
          className={`px-4 py-2 rounded-md text-sm font-medium transition-all ${
            activeTab === 'discovered'
              ? 'bg-blue-600/30 text-blue-400 border border-blue-500/30'
              : 'text-slate-400 hover:text-slate-200'
          }`}
        >
          <Smartphone className="w-4 h-4 inline mr-1.5" />
          待认领设备
        </button>
        <button
          onClick={() => setActiveTab('commands')}
          disabled={!canSend}
          className={`px-4 py-2 rounded-md text-sm font-medium transition-all ${
            activeTab === 'commands'
              ? 'bg-blue-600/30 text-blue-400 border border-blue-500/30'
              : 'text-slate-400 hover:text-slate-200'
          } ${!canSend ? 'opacity-40 cursor-not-allowed' : ''}`}
        >
          <Terminal className="w-4 h-4 inline mr-1.5" />
          命令面板
        </button>
        <button
          onClick={() => setActiveTab('opendevice')}
          disabled={!canSend}
          className={`px-4 py-2 rounded-md text-sm font-medium transition-all ${
            activeTab === 'opendevice'
              ? 'bg-emerald-600/30 text-emerald-400 border border-emerald-500/30'
              : 'text-slate-400 hover:text-slate-200'
          } ${!canSend ? 'opacity-40 cursor-not-allowed' : ''}`}
        >
          <Power className="w-4 h-4 inline mr-1.5" />
          二开设备命令
        </button>
      </div>

      {/* ────────────── 待认领设备 Tab ────────────── */}
      {activeTab === 'discovered' && (
        <div className="space-y-4">
          {/* 搜索 & 筛选 */}
          <div className="flex items-center gap-3 flex-wrap">
            <div className="relative flex-1 min-w-[260px] max-w-md">
              <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-400" />
              <input
                type="text"
                placeholder="搜索 IMEI / 型号..."
                value={discoveredSearch}
                onChange={e => setDiscoveredSearch(e.target.value)}
                className="w-full pl-10 pr-4 py-2 rounded-lg bg-slate-800/80 border border-slate-700/50 text-sm text-slate-200
                           placeholder:text-slate-500 focus:outline-none focus:border-blue-500/50 transition-all"
              />
            </div>
            <div className="flex gap-1 p-0.5 rounded-lg bg-slate-800/50 border border-slate-700/50">
              <button
                onClick={() => setClaimedFilter(null)}
                className={`px-3 py-1.5 rounded text-xs font-medium transition-all ${
                  claimedFilter === null ? 'bg-slate-700 text-slate-200' : 'text-slate-400 hover:text-slate-200'
                }`}
              >全部</button>
              <button
                onClick={() => setClaimedFilter(false)}
                className={`px-3 py-1.5 rounded text-xs font-medium transition-all ${
                  claimedFilter === false ? 'bg-amber-600/30 text-amber-400' : 'text-slate-400 hover:text-slate-200'
                }`}
              >待认领</button>
              <button
                onClick={() => setClaimedFilter(true)}
                className={`px-3 py-1.5 rounded text-xs font-medium transition-all ${
                  claimedFilter === true ? 'bg-green-600/30 text-green-400' : 'text-slate-400 hover:text-slate-200'
                }`}
              >已认领</button>
            </div>
            <button
              onClick={loadDiscoveredDevices}
              className="inline-flex items-center gap-1.5 px-3 py-2 rounded-lg text-xs text-slate-400 
                         hover:text-slate-200 hover:bg-slate-800/50 transition-all"
            >
              <RefreshCw className={`w-3.5 h-3.5 ${discoveredLoading ? 'animate-spin' : ''}`} />
              刷新
            </button>
          </div>

          {/* 错误提示 */}
          {discoveredError && (
            <div className="flex items-center gap-2 px-4 py-3 rounded-lg bg-red-500/10 border border-red-500/30 text-red-400 text-sm">
              <AlertCircle className="w-4 h-4 flex-shrink-0" />
              {discoveredError}
            </div>
          )}

          {/* 设备表格 */}
          <div className="rounded-xl bg-slate-800/40 border border-slate-700/50 overflow-hidden">
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b border-slate-700/50 bg-slate-800/60">
                    <th className="text-left px-4 py-3 text-slate-400 font-medium">IMEI</th>
                    <th className="text-left px-4 py-3 text-slate-400 font-medium">型号</th>
                    <th className="text-left px-4 py-3 text-slate-400 font-medium">网络类型</th>
                    <th className="text-left px-4 py-3 text-slate-400 font-medium">状态</th>
                    <th className="text-left px-4 py-3 text-slate-400 font-medium">在线</th>
                    <th className="text-left px-4 py-3 text-slate-400 font-medium">首次发现</th>
                    <th className="text-left px-4 py-3 text-slate-400 font-medium">最后活跃</th>
                    <th className="text-right px-4 py-3 text-slate-400 font-medium">操作</th>
                  </tr>
                </thead>
                <tbody>
                  {discoveredLoading ? (
                    <tr>
                      <td colSpan={8} className="px-4 py-12 text-center">
                        <Loader2 className="w-6 h-6 animate-spin text-slate-400 mx-auto mb-2" />
                        <span className="text-slate-400 text-sm">加载中...</span>
                      </td>
                    </tr>
                  ) : discoveredDevices.length === 0 ? (
                    <tr>
                      <td colSpan={8} className="px-4 py-12 text-center">
                        <div className="inline-flex flex-col items-center gap-2">
                          <Radio className="w-10 h-10 text-slate-600" />
                          <span className="text-slate-500 text-sm">暂无发现的安圣设备</span>
                          <span className="text-slate-600 text-xs">点击"发现设备"按钮触发扫描，或等待设备自动上线</span>
                        </div>
                      </td>
                    </tr>
                  ) : (
                    discoveredDevices.map((device, idx) => (
                      <motion.tr
                        key={device.id}
                        initial={{ opacity: 0, y: 8 }}
                        animate={{ opacity: 1, y: 0 }}
                        transition={{ delay: idx * 0.03 }}
                        className="border-b border-slate-800/50 hover:bg-slate-800/30 transition-colors"
                      >
                        <td className="px-4 py-3 font-mono text-slate-200">{device.imei}</td>
                        <td className="px-4 py-3 text-slate-300">{device.model || '—'}</td>
                        <td className="px-4 py-3 text-slate-300">{device.netType || '—'}</td>
                        <td className="px-4 py-3">{getClaimStatusBadge(device.isClaimed)}</td>
                        <td className="px-4 py-3">{getOnlineBadge(device.lastSeenAt)}</td>
                        <td className="px-4 py-3 text-slate-400 text-xs">{formatTime(device.firstSeenAt)}</td>
                        <td className="px-4 py-3 text-slate-400 text-xs">{formatTime(device.lastSeenAt)}</td>
                        <td className="px-4 py-3 text-right">
                          {!device.isClaimed && canCreate && (
                            <button
                              onClick={() => handleClaimClick(device)}
                              className="inline-flex items-center gap-1 px-3 py-1.5 rounded-md text-xs font-medium
                                         bg-blue-600/20 text-blue-400 border border-blue-500/30
                                         hover:bg-blue-600/30 transition-all"
                            >
                              <Plus className="w-3 h-3" />
                              认领
                            </button>
                          )}
                          {device.isClaimed && (
                            <span className="text-xs text-slate-500">已认领</span>
                          )}
                        </td>
                      </motion.tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>

            {/* 分页 */}
            {discoveredTotalPages > 1 && (
              <div className="flex items-center justify-between px-4 py-3 border-t border-slate-700/50">
                <span className="text-xs text-slate-500">
                  共 {discoveredTotal} 条，第 {discoveredPage}/{discoveredTotalPages} 页
                </span>
                <div className="flex items-center gap-1">
                  <button
                    onClick={() => setDiscoveredPage(p => Math.max(1, p - 1))}
                    disabled={discoveredPage <= 1}
                    className="p-1.5 rounded text-slate-400 hover:text-slate-200 disabled:opacity-30 disabled:cursor-not-allowed"
                  >
                    <ChevronLeft className="w-4 h-4" />
                  </button>
                  {Array.from({ length: Math.min(5, discoveredTotalPages) }, (_, i) => {
                    let pageNum: number;
                    if (discoveredTotalPages <= 5) {
                      pageNum = i + 1;
                    } else if (discoveredPage <= 3) {
                      pageNum = i + 1;
                    } else if (discoveredPage >= discoveredTotalPages - 2) {
                      pageNum = discoveredTotalPages - 4 + i;
                    } else {
                      pageNum = discoveredPage - 2 + i;
                    }
                    return (
                      <button
                        key={pageNum}
                        onClick={() => setDiscoveredPage(pageNum)}
                        className={`w-8 h-8 rounded text-xs font-medium transition-all ${
                          pageNum === discoveredPage
                            ? 'bg-blue-600/30 text-blue-400'
                            : 'text-slate-400 hover:text-slate-200 hover:bg-slate-700/50'
                        }`}
                      >
                        {pageNum}
                      </button>
                    );
                  })}
                  <button
                    onClick={() => setDiscoveredPage(p => Math.min(discoveredTotalPages, p + 1))}
                    disabled={discoveredPage >= discoveredTotalPages}
                    className="p-1.5 rounded text-slate-400 hover:text-slate-200 disabled:opacity-30 disabled:cursor-not-allowed"
                  >
                    <ChevronRight className="w-4 h-4" />
                  </button>
                </div>
              </div>
            )}
          </div>
        </div>
      )}

      {/* ────────────── 命令面板 Tab ────────────── */}
      {activeTab === 'commands' && (
        <div className="grid grid-cols-1 xl:grid-cols-3 gap-6">
          {/* 左侧：设备选择 + 命令模板 */}
          <div className="space-y-4">
            {/* 设备选择 */}
            <div className="rounded-xl bg-slate-800/40 border border-slate-700/50 p-4 space-y-3">
              <h3 className="text-sm font-medium text-slate-300 flex items-center gap-2">
                <Cpu className="w-4 h-4 text-blue-400" />
                选择安圣设备
              </h3>
              {anshengDevicesLoading ? (
                <div className="flex items-center gap-2 text-slate-400 text-sm">
                  <Loader2 className="w-4 h-4 animate-spin" />
                  加载设备...
                </div>
              ) : anshengDevices.length === 0 ? (
                <div className="text-center py-6 text-slate-500 text-sm">
                  <Radio className="w-8 h-8 mx-auto mb-2 text-slate-600" />
                  暂未发现已认领的安圣设备
                </div>
              ) : (
                <div className="space-y-1 max-h-[300px] overflow-y-auto">
                  {anshengDevices.map(d => (
                    <button
                      key={d.id}
                      onClick={() => setSelectedDeviceId(Number(d.id))}
                      className={`w-full text-left px-3 py-2.5 rounded-lg text-sm transition-all ${
                        selectedDeviceId === Number(d.id)
                          ? 'bg-blue-600/20 border border-blue-500/30 text-blue-300'
                          : 'border border-transparent text-slate-300 hover:bg-slate-700/50'
                      }`}
                    >
                      <div className="font-medium">{d.name}</div>
                      <div className="text-xs text-slate-500 mt-0.5">
                        <span className="font-mono">IMEI: {d.serialNumber || d.id}</span>
                        {d.model && <span className="ml-2">型号: {d.model}</span>}
                      </div>
                    </button>
                  ))}
                </div>
              )}
              <div className="flex gap-1.5">
                <button
                  onClick={loadAnShengDevices}
                  className="flex-1 inline-flex items-center justify-center gap-1 px-3 py-1.5 rounded-md text-xs
                             text-slate-400 border border-slate-700/50 hover:text-slate-200 hover:bg-slate-700/50 transition-all"
                >
                  <RefreshCw className="w-3 h-3" /> 刷新
                </button>
              </div>
            </div>

            {/* 命令模板 */}
            {selectedDeviceId && (
              <div className="rounded-xl bg-slate-800/40 border border-slate-700/50 p-4 space-y-3">
                <h3 className="text-sm font-medium text-slate-300 flex items-center gap-2">
                  <Terminal className="w-4 h-4 text-purple-400" />
                  命令模板
                </h3>
                <div className="space-y-1">
                  {ANSHENG_COMMAND_TEMPLATES.map(tpl => (
                    <button
                      key={tpl.method}
                      onClick={() => selectTemplate(tpl)}
                      className={`w-full text-left px-3 py-2 rounded-lg text-sm transition-all ${
                        selectedTemplate.method === tpl.method
                          ? 'bg-purple-600/20 border border-purple-500/30'
                          : 'border border-transparent hover:bg-slate-700/50'
                      }`}
                    >
                      <div className="flex items-center gap-2">
                        <span className={`text-${tpl.color}-400`}>{tpl.icon}</span>
                        <span className="font-medium text-slate-200">{tpl.label}</span>
                      </div>
                      <div className="text-xs text-slate-500 mt-0.5 ml-6">{tpl.description}</div>
                    </button>
                  ))}
                </div>
              </div>
            )}
          </div>

          {/* 右侧：命令参数 + 发送 */}
          <div className="xl:col-span-2 space-y-4">
            {!selectedDeviceId ? (
              <div className="rounded-xl bg-slate-800/40 border border-slate-700/50 p-12 text-center">
                <Cpu className="w-12 h-12 text-slate-600 mx-auto mb-3" />
                <p className="text-slate-500 text-sm">请先选择一个安圣设备</p>
                <p className="text-slate-600 text-xs mt-1">在左侧设备列表中选择设备后，即可下发命令</p>
              </div>
            ) : (
              <>
                {/* 命令发送面板 */}
                <div className="rounded-xl bg-slate-800/40 border border-slate-700/50 p-5 space-y-4">
                  <div className="flex items-center gap-3">
                    <div className={`p-2 rounded-lg bg-${selectedTemplate.color}-500/20 border border-${selectedTemplate.color}-500/30`}>
                      {selectedTemplate.icon}
                    </div>
                    <div>
                      <h3 className="font-medium text-slate-200">{selectedTemplate.label}</h3>
                      <p className="text-xs text-slate-500">{selectedTemplate.description}</p>
                    </div>
                  </div>

                  {/* 选中设备信息 */}
                  {selectedDevice && (
                    <div className="flex items-center gap-3 px-3 py-2 rounded-lg bg-slate-700/30 border border-slate-700/50">
                      <Hash className="w-4 h-4 text-slate-500" />
                      <span className="text-sm text-slate-300">{selectedDevice.name}</span>
                      <span className="text-xs font-mono text-slate-500">IMEI: {selectedDevice.serialNumber}</span>
                    </div>
                  )}

                  {/* 参数表单 */}
                  {selectedTemplate.params.length > 0 && (
                    <div className="space-y-3">
                      <div className="text-xs font-medium text-slate-500 uppercase tracking-wider">命令参数</div>
                      <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                        {selectedTemplate.params.map(p => (
                          <div key={p.key}>
                            <label className="block text-xs text-slate-400 mb-1">{p.label}</label>
                            {p.type === 'select' ? (
                              <select
                                value={paramValues[p.key] || ''}
                                onChange={e => setParamValues(prev => ({ ...prev, [p.key]: e.target.value }))}
                                className="w-full px-3 py-2 rounded-lg bg-slate-700/50 border border-slate-600/50 text-sm text-slate-200
                                           focus:outline-none focus:border-blue-500/50 transition-all"
                              >
                                <option value="" disabled>请选择</option>
                                {p.options?.map(o => (
                                  <option key={o} value={o}>{o}</option>
                                ))}
                              </select>
                            ) : (
                              <input
                                type={p.type}
                                value={paramValues[p.key] || ''}
                                onChange={e => setParamValues(prev => ({ ...prev, [p.key]: e.target.value }))}
                                placeholder={p.placeholder}
                                className="w-full px-3 py-2 rounded-lg bg-slate-700/50 border border-slate-600/50 text-sm text-slate-200
                                           placeholder:text-slate-500 focus:outline-none focus:border-blue-500/50 transition-all"
                              />
                            )}
                          </div>
                        ))}
                      </div>
                    </div>
                  )}

                  {/* 发送按钮 */}
                  <div className="flex items-center justify-between pt-2 border-t border-slate-700/50">
                    {sendResult && (
                      <div className={`flex items-center gap-2 text-sm ${
                        sendResult.success ? 'text-green-400' : 'text-red-400'
                      }`}>
                        {sendResult.success ? <CheckCircle className="w-4 h-4" /> : <AlertCircle className="w-4 h-4" />}
                        {sendResult.message}
                      </div>
                    )}
                    <button
                      onClick={handleSendCommand}
                      disabled={sending || !canSend}
                      className="inline-flex items-center gap-2 px-5 py-2.5 rounded-lg text-sm font-medium
                                 bg-blue-600/30 text-blue-400 border border-blue-500/30
                                 hover:bg-blue-600/40 disabled:opacity-40 disabled:cursor-not-allowed
                                 transition-all ml-auto"
                    >
                      {sending ? (
                        <><Loader2 className="w-4 h-4 animate-spin" /> 发送中...</>
                      ) : (
                        <><Send className="w-4 h-4" /> 下发命令</>
                      )}
                    </button>
                  </div>
                </div>

                {/* 命令日志 */}
                <div className="rounded-xl bg-slate-800/40 border border-slate-700/50 p-4">
                  <h3 className="text-sm font-medium text-slate-300 flex items-center gap-2 mb-3">
                    <Activity className="w-4 h-4 text-cyan-400" />
                    命令日志
                    <span className="text-xs text-slate-500">（最近 50 条）</span>
                  </h3>
                  {commandLog.length === 0 ? (
                    <p className="text-sm text-slate-600 text-center py-4">暂无命令记录</p>
                  ) : (
                    <div className="space-y-1.5 max-h-[300px] overflow-y-auto">
                      {commandLog.map((log, idx) => (
                        <div
                          key={idx}
                          className="flex items-center gap-2 px-3 py-1.5 rounded text-xs bg-slate-700/20"
                        >
                          <span className="text-slate-500 font-mono w-16">{formatLogTime(log.time)}</span>
                          <span className={`w-2 h-2 rounded-full ${log.success ? 'bg-green-400' : 'bg-red-400'}`} />
                          <span className="text-slate-300 font-medium">{log.method}</span>
                          <span className="text-slate-500">—</span>
                          <span className={log.success ? 'text-green-400/80' : 'text-red-400/80'}>{log.message}</span>
                        </div>
                      ))}
                    </div>
                  )}
                </div>
              </>
            )}
          </div>
        </div>
      )}

      {/* ────────────── 二开设备命令 Tab ────────────── */}
      {activeTab === 'opendevice' && (
        <div className="grid grid-cols-1 xl:grid-cols-3 gap-6">
          {/* 左侧：设备选择 + 命令模板 */}
          <div className="space-y-4">
            {/* 设备选择 */}
            <div className="p-4 rounded-xl bg-slate-800/40 border border-slate-700/30">
              <div className="flex items-center gap-2 mb-3">
                <Radio className="w-4 h-4 text-emerald-400" />
                <h3 className="text-sm font-semibold text-slate-200">选择二开设备</h3>
              </div>
              {anshengDevicesLoading ? (
                <div className="flex items-center justify-center py-4">
                  <Loader2 className="w-5 h-5 animate-spin text-slate-500" />
                </div>
              ) : (
                <div className="space-y-1 max-h-64 overflow-y-auto">
                  {anshengDevices
                    .filter(d => d.model && (
                      d.model.includes('喇叭') || d.model.includes('开关') ||
                      d.model.includes('Speaker') || d.model.includes('Switch')
                    ))
                    .map(d => (
                      <button
                        key={d.id}
                        onClick={() => { setSelectedDeviceId(d.id); setSendResult(null); }}
                        className={`w-full text-left px-3 py-2 rounded-lg text-sm transition-all ${
                          selectedDeviceId === d.id
                            ? 'bg-emerald-600/20 border border-emerald-500/30 text-emerald-300'
                            : 'hover:bg-slate-700/30 text-slate-400 border border-transparent'
                        }`}
                      >
                        <div className="flex items-center justify-between">
                          <span className="font-medium">{d.name}</span>
                          <span className="text-xs text-slate-500">{d.model}</span>
                        </div>
                        <div className="text-xs text-slate-500 mt-0.5">
                          IMEI: {d.serialNumber} · {d.status}
                        </div>
                      </button>
                    ))}
                  {anshengDevices.filter(d => d.model && (
                    d.model.includes('喇叭') || d.model.includes('开关') ||
                    d.model.includes('Speaker') || d.model.includes('Switch')
                  )).length === 0 && (
                    <p className="text-xs text-slate-500 py-2 text-center">暂无可控二开设备</p>
                  )}
                </div>
              )}
            </div>

            {/* 命令模板列表 */}
            <div className="p-4 rounded-xl bg-slate-800/40 border border-slate-700/30">
              <div className="flex items-center gap-2 mb-3">
                <Wrench className="w-4 h-4 text-emerald-400" />
                <h3 className="text-sm font-semibold text-slate-200">命令模板</h3>
              </div>
              <div className="space-y-1.5">
                {OPEN_DEVICE_COMMAND_TEMPLATES.map(tpl => (
                  <button
                    key={tpl.method}
                    onClick={() => { setSelectedTemplate(tpl); setParamValues({}); setSendResult(null); }}
                    className={`w-full text-left px-3 py-2 rounded-lg transition-all ${
                      selectedTemplate.method === tpl.method
                        ? `bg-${tpl.color}-500/15 border border-${tpl.color}-500/30`
                        : 'hover:bg-slate-700/20 border border-transparent'
                    }`}
                  >
                    <div className="flex items-center gap-2">
                      <span className={`text-${tpl.color}-400`}>{tpl.icon}</span>
                      <div>
                        <div className="text-sm font-medium text-slate-200">{tpl.label}</div>
                        <div className="text-xs text-slate-500">{tpl.description}</div>
                      </div>
                    </div>
                  </button>
                ))}
              </div>
            </div>
          </div>

          {/* 右侧：参数表单 + 发送 + 日志 */}
          <div className="xl:col-span-2 space-y-4">
            {/* 参数表单 */}
            <div className="p-5 rounded-xl bg-slate-800/40 border border-slate-700/30">
              <div className="flex items-center gap-2 mb-4">
                <div className={`p-1.5 rounded-lg bg-${selectedTemplate.color}-500/20 border border-${selectedTemplate.color}-500/30`}>
                  <span className={`text-${selectedTemplate.color}-400`}>{selectedTemplate.icon}</span>
                </div>
                <div>
                  <h3 className="font-semibold text-slate-200">{selectedTemplate.label}</h3>
                  <p className="text-xs text-slate-500">{selectedTemplate.description}</p>
                </div>
              </div>

              {selectedTemplate.params.length > 0 ? (
                <div className="space-y-3">
                  {selectedTemplate.params.map(field => (
                    <div key={field.key}>
                      <label className="block text-xs font-medium text-slate-400 mb-1">
                        {field.label}
                      </label>
                      {field.type === 'select' ? (
                        <select
                          value={paramValues[field.key] || field.defaultValue || ''}
                          onChange={e => setParamValues(prev => ({ ...prev, [field.key]: e.target.value }))}
                          className="w-full px-3 py-2 rounded-lg bg-slate-900/60 border border-slate-700 text-slate-200
                                     text-sm focus:outline-none focus:border-emerald-500/50 transition-colors"
                        >
                          {field.options?.map(opt => (
                            <option key={opt} value={opt}>{opt}</option>
                          ))}
                        </select>
                      ) : (
                        <input
                          type={field.type}
                          value={paramValues[field.key] || field.defaultValue || ''}
                          onChange={e => setParamValues(prev => ({ ...prev, [field.key]: e.target.value }))}
                          placeholder={field.placeholder}
                          className="w-full px-3 py-2 rounded-lg bg-slate-900/60 border border-slate-700 text-slate-200
                                     text-sm focus:outline-none focus:border-emerald-500/50 transition-colors
                                     placeholder:text-slate-600"
                        />
                      )}
                    </div>
                  ))}
                </div>
              ) : (
                <p className="text-sm text-slate-500">该命令无需参数</p>
              )}

              {/* 发送按钮 */}
              <button
                onClick={handleSendCommand}
                disabled={!selectedDeviceId || sending}
                className="mt-4 w-full inline-flex items-center justify-center gap-2 px-4 py-2.5 rounded-lg
                           bg-emerald-600/30 text-emerald-400 border border-emerald-500/30
                           hover:bg-emerald-600/40 disabled:opacity-40 disabled:cursor-not-allowed
                           text-sm font-medium transition-all"
              >
                {sending ? (
                  <><Loader2 className="w-4 h-4 animate-spin" /> 发送中...</>
                ) : (
                  <><Send className="w-4 h-4" /> 下发命令</>
                )}
              </button>

              {/* 发送结果 */}
              <AnimatePresence>
                {sendResult && (
                  <motion.div
                    initial={{ opacity: 0, y: 8 }}
                    animate={{ opacity: 1, y: 0 }}
                    exit={{ opacity: 0, y: 8 }}
                    className={`mt-3 p-3 rounded-lg text-sm ${
                      sendResult.success
                        ? 'bg-green-500/10 border border-green-500/30 text-green-400'
                        : 'bg-red-500/10 border border-red-500/30 text-red-400'
                    }`}
                  >
                    {sendResult.message}
                  </motion.div>
                )}
              </AnimatePresence>
            </div>

            {/* 命令日志 */}
            <div className="p-5 rounded-xl bg-slate-800/40 border border-slate-700/30">
              <div className="flex items-center gap-2 mb-3">
                <Clock className="w-4 h-4 text-slate-400" />
                <h3 className="text-sm font-semibold text-slate-200">命令日志</h3>
              </div>
              {commandLog.length === 0 ? (
                <p className="text-sm text-slate-500 py-2 text-center">暂无命令记录</p>
              ) : (
                <div className="space-y-1.5 max-h-48 overflow-y-auto">
                  {commandLog.map((log, i) => (
                    <div key={i} className="flex items-center gap-2 text-xs text-slate-400">
                      <span className="text-slate-600 flex-shrink-0">
                        {log.time.toLocaleTimeString()}
                      </span>
                      <span className={log.success ? 'text-green-400' : 'text-red-400'}>
                        {log.method}
                      </span>
                      <span className="text-slate-500 truncate">{log.message}</span>
                    </div>
                  ))}
                </div>
              )}
            </div>
          </div>
        </div>
      )}

      {/* ──────── 认领弹窗 ──────── */}
      <AnimatePresence>
        {showClaimModal && claimingDevice && (
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/60 backdrop-blur-sm"
            onClick={() => !claiming && setShowClaimModal(false)}
          >
            <motion.div
              initial={{ scale: 0.95, y: 20 }}
              animate={{ scale: 1, y: 0 }}
              exit={{ scale: 0.95, y: 20 }}
              className="bg-slate-900 border border-slate-700/50 rounded-2xl shadow-2xl w-full max-w-lg overflow-hidden"
              onClick={e => e.stopPropagation()}
            >
              {/* Header */}
              <div className="flex items-center justify-between px-6 py-4 border-b border-slate-700/50">
                <div className="flex items-center gap-3">
                  <div className="p-2 rounded-lg bg-blue-500/20 border border-blue-500/30">
                    <Plus className="w-5 h-5 text-blue-400" />
                  </div>
                  <div>
                    <h3 className="font-semibold text-slate-200">认领安圣设备</h3>
                    <p className="text-xs text-slate-500">IMEI: {claimingDevice.imei}</p>
                  </div>
                </div>
                <button
                  onClick={() => setShowClaimModal(false)}
                  disabled={claiming}
                  className="p-1.5 rounded-lg text-slate-400 hover:text-slate-200 hover:bg-slate-800 transition-all"
                >
                  <X className="w-5 h-5" />
                </button>
              </div>

              {/* Body */}
              <div className="px-6 py-4 space-y-4">
                {/* 设备信息卡片 */}
                <div className="p-3 rounded-lg bg-slate-800/50 border border-slate-700/50 space-y-1.5">
                  <div className="flex items-center justify-between">
                    <span className="text-xs text-slate-500">IMEI</span>
                    <span className="text-sm font-mono text-slate-200">{claimingDevice.imei}</span>
                  </div>
                  <div className="flex items-center justify-between">
                    <span className="text-xs text-slate-500">型号</span>
                    <span className="text-sm text-slate-300">{claimingDevice.model || '—'}</span>
                  </div>
                  <div className="flex items-center justify-between">
                    <span className="text-xs text-slate-500">网络</span>
                    <span className="text-sm text-slate-300">{claimingDevice.netType || '—'}</span>
                  </div>
                  <div className="flex items-center justify-between">
                    <span className="text-xs text-slate-500">首次发现</span>
                    <span className="text-sm text-slate-300">{formatTime(claimingDevice.firstSeenAt)}</span>
                  </div>
                </div>

                {/* 认领表单 */}
                <div className="space-y-3">
                  <div>
                    <label className="block text-xs font-medium text-slate-400 mb-1">
                      设备名称 <span className="text-red-400">*</span>
                    </label>
                    <input
                      type="text"
                      value={claimForm.deviceName}
                      onChange={e => setClaimForm(prev => ({ ...prev, deviceName: e.target.value }))}
                      placeholder="输入设备名称"
                      className="w-full px-3 py-2 rounded-lg bg-slate-800 border border-slate-700/50 text-sm text-slate-200
                                 placeholder:text-slate-500 focus:outline-none focus:border-blue-500/50 transition-all"
                    />
                  </div>

                  <div className="grid grid-cols-2 gap-3">
                    <div>
                      <label className="block text-xs font-medium text-slate-400 mb-1">型号</label>
                      <input
                        type="text"
                        value={claimForm.model}
                        onChange={e => setClaimForm(prev => ({ ...prev, model: e.target.value }))}
                        className="w-full px-3 py-2 rounded-lg bg-slate-800 border border-slate-700/50 text-sm text-slate-200
                                   placeholder:text-slate-500 focus:outline-none focus:border-blue-500/50 transition-all"
                      />
                    </div>
                    <div>
                      <label className="block text-xs font-medium text-slate-400 mb-1">类别</label>
                      <select
                        value={claimForm.category}
                        onChange={e => setClaimForm(prev => ({ ...prev, category: e.target.value }))}
                        className="w-full px-3 py-2 rounded-lg bg-slate-800 border border-slate-700/50 text-sm text-slate-200
                                   focus:outline-none focus:border-blue-500/50 transition-all"
                      >
                        <option value="power">充电桩</option>
                        <option value="sensor">传感器</option>
                        <option value="controller">控制器</option>
                        <option value="gateway">网关</option>
                      </select>
                    </div>
                  </div>

                  <div className="grid grid-cols-2 gap-3">
                    <div>
                      <label className="block text-xs font-medium text-slate-400 mb-1">能源类型</label>
                      <select
                        value={claimForm.energyTypes}
                        onChange={e => setClaimForm(prev => ({ ...prev, energyTypes: e.target.value }))}
                        className="w-full px-3 py-2 rounded-lg bg-slate-800 border border-slate-700/50 text-sm text-slate-200
                                   focus:outline-none focus:border-blue-500/50 transition-all"
                      >
                        <option value="electric">电</option>
                        <option value="water">水</option>
                        <option value="gas">气</option>
                        <option value="electric,water">水 + 电</option>
                        <option value="electric,gas">电 + 气</option>
                      </select>
                    </div>
                    <div>
                      <label className="block text-xs font-medium text-slate-400 mb-1">位置</label>
                      <input
                        type="text"
                        value={claimForm.location}
                        onChange={e => setClaimForm(prev => ({ ...prev, location: e.target.value }))}
                        placeholder="可选"
                        className="w-full px-3 py-2 rounded-lg bg-slate-800 border border-slate-700/50 text-sm text-slate-200
                                   placeholder:text-slate-500 focus:outline-none focus:border-blue-500/50 transition-all"
                      />
                    </div>
                  </div>
                </div>

                {/* 结果通知 */}
                <AnimatePresence>
                  {claimResult && (
                    <motion.div
                      initial={{ opacity: 0, height: 0 }}
                      animate={{ opacity: 1, height: 'auto' }}
                      exit={{ opacity: 0, height: 0 }}
                      className={`flex items-center gap-2 px-4 py-3 rounded-lg text-sm ${
                        claimResult.success
                          ? 'bg-green-500/10 border border-green-500/30 text-green-400'
                          : 'bg-red-500/10 border border-red-500/30 text-red-400'
                      }`}
                    >
                      {claimResult.success ? (
                        <CheckCircle className="w-4 h-4 flex-shrink-0" />
                      ) : (
                        <AlertCircle className="w-4 h-4 flex-shrink-0" />
                      )}
                      {claimResult.message}
                    </motion.div>
                  )}
                </AnimatePresence>
              </div>

              {/* Footer */}
              <div className="flex justify-end gap-3 px-6 py-4 border-t border-slate-700/50">
                <button
                  onClick={() => setShowClaimModal(false)}
                  disabled={claiming}
                  className="px-4 py-2 rounded-lg text-sm text-slate-400 hover:text-slate-200 hover:bg-slate-800
                             disabled:opacity-40 transition-all"
                >
                  取消
                </button>
                <button
                  onClick={handleClaimSubmit}
                  disabled={claiming || !claimForm.deviceName.trim()}
                  className="inline-flex items-center gap-2 px-5 py-2 rounded-lg text-sm font-medium
                             bg-blue-600/30 text-blue-400 border border-blue-500/30
                             hover:bg-blue-600/40 disabled:opacity-40 disabled:cursor-not-allowed transition-all"
                >
                  {claiming ? (
                    <><Loader2 className="w-4 h-4 animate-spin" /> 认领中...</>
                  ) : (
                    <><CheckCircle className="w-4 h-4" /> 确认认领</>
                  )}
                </button>
              </div>
            </motion.div>
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}

export default AnShengManagementPage;
