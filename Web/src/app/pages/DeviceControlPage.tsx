import { useState, useEffect, useCallback } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import {
  Cpu, Terminal, Play, X, RefreshCw, Clock, CheckCircle2,
  XCircle, Send, RotateCcw, Ban, ChevronRight, ChevronDown,
  Zap, Power, Wrench, Upload, Hash, List, Filter, Search,
  Activity, AlertCircle, History, Loader2,
} from 'lucide-react';
import { useAuth } from '@/app/contexts/AuthContext';
import { PERMISSIONS } from '@/app/config/permissions';
import { deviceCommandApi } from '@/app/services/api/deviceCommandApi';
import { deviceApi } from '@/app/services/api/deviceApi';
import type {
  DeviceCommandDto,
  CommandHistoryDto,
  CommandStatus,
  SendCommandRequest,
} from '@/app/services/api/types/deviceCommand.types';
import type { DeviceDto } from '@/app/services/api/types/device.types';

// ── 设备类型映射 ──────────────────────────────────────────────────────────────
// 使用后端 DeviceDto 类型，映射为前端需要的格式
interface SimpleDevice {
  id: number;
  name: string;
  serialNumber: string;
  model: string;
  status: 'online' | 'offline' | 'warning';
  location: string;
  protocolType: string;
}

// 将 DeviceDto 映射为 SimpleDevice
function mapDeviceToSimple(d: DeviceDto): SimpleDevice {
  return {
    id: Number(d.id),
    name: d.name,
    serialNumber: d.serialNumber || d.id,
    model: d.model || '-',
    status: (d.status === 'online' || d.status === 'offline' || d.status === 'warning')
      ? d.status
      : (d.status === 'error' ? 'warning' : 'offline'),
    location: d.location || d.areaName || '-',
    protocolType: d.category || '未知',
  };
}

// ── 指令类型 ──────────────────────────────────────────────────────────────────
interface CommandTemplate {
  type: string;
  label: string;
  icon: React.ReactNode;
  description: string;
  params: ParamField[];
  color: string;
}

interface ParamField {
  key: string;
  label: string;
  type: 'text' | 'number' | 'select' | 'boolean';
  options?: string[];
  defaultValue?: string | number | boolean;
  placeholder?: string;
}

const COMMAND_TEMPLATES: CommandTemplate[] = [
  {
    type: 'switch',
    label: '开关控制',
    icon: <Power className="w-4 h-4" />,
    description: '控制设备开启或关闭',
    color: 'blue',
    params: [
      { key: 'action', label: '操作', type: 'select', options: ['on', 'off'], defaultValue: 'on' },
    ],
  },
  {
    type: 'setParam',
    label: '参数设置',
    icon: <Wrench className="w-4 h-4" />,
    description: '设置设备运行参数',
    color: 'amber',
    params: [
      { key: 'paramName', label: '参数名称', type: 'text', placeholder: '例如：temperature_threshold' },
      { key: 'paramValue', label: '参数值', type: 'text', placeholder: '例如：25.5' },
    ],
  },
  {
    type: 'restart',
    label: '重启设备',
    icon: <RotateCcw className="w-4 h-4" />,
    description: '远程重启设备',
    color: 'orange',
    params: [
      { key: 'delaySeconds', label: '延迟秒数', type: 'number', defaultValue: 0, placeholder: '0' },
    ],
  },
  {
    type: 'firmwareUpgrade',
    label: '固件升级',
    icon: <Upload className="w-4 h-4" />,
    description: '触发设备固件升级',
    color: 'purple',
    params: [
      { key: 'firmwareVersion', label: '目标版本', type: 'text', placeholder: '例如：v2.1.0' },
      { key: 'downloadUrl', label: '下载地址', type: 'text', placeholder: 'http://...' },
    ],
  },
  {
    type: 'custom',
    label: '自定义指令',
    icon: <Terminal className="w-4 h-4" />,
    description: '发送自定义 JSON 参数',
    color: 'slate',
    params: [
      { key: 'payload', label: 'JSON 参数', type: 'text', placeholder: '{"key": "value"}' },
    ],
  },
];



// ── 辅助函数 ───────────────────────────────────────────────────────────────────
function getStatusBadge(status: CommandStatus) {
  const map: Record<CommandStatus, { label: string; cls: string; icon: React.ReactNode }> = {
    Pending:   { label: '待发送', cls: 'bg-yellow-500/20 text-yellow-400 border-yellow-500/30', icon: <Clock className="w-3 h-3" /> },
    Sent:      { label: '已发送', cls: 'bg-blue-500/20 text-blue-400 border-blue-500/30', icon: <Send className="w-3 h-3" /> },
    Delivered: { label: '已接收', cls: 'bg-cyan-500/20 text-cyan-400 border-cyan-500/30', icon: <CheckCircle2 className="w-3 h-3" /> },
    Success:   { label: '成功', cls: 'bg-green-500/20 text-green-400 border-green-500/30', icon: <CheckCircle2 className="w-3 h-3" /> },
    Failed:    { label: '失败', cls: 'bg-red-500/20 text-red-400 border-red-500/30', icon: <XCircle className="w-3 h-3" /> },
    Timeout:   { label: '超时', cls: 'bg-slate-500/20 text-slate-400 border-slate-500/30', icon: <Clock className="w-3 h-3" /> },
  };
  const s = map[status] ?? map['Pending'];
  return (
    <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-medium border ${s.cls}`}>
      {s.icon} {s.label}
    </span>
  );
}

function getDeviceStatusDot(status: SimpleDevice['status']) {
  const map = {
    online:  'bg-green-400',
    offline: 'bg-slate-500',
    warning: 'bg-yellow-400',
  };
  return <span className={`inline-block w-2 h-2 rounded-full ${map[status]}`} />;
}

function getCommandTypeLabel(type: string) {
  return COMMAND_TEMPLATES.find(t => t.type === type)?.label ?? type;
}

// ── 主组件 ─────────────────────────────────────────────────────────────────────
export function DeviceControlPage() {
  const { hasPermission } = useAuth();
  const canSend   = hasPermission(PERMISSIONS.SEND_DEVICE_COMMANDS);
  const canCancel = hasPermission(PERMISSIONS.CANCEL_DEVICE_COMMANDS);
  const canView   = hasPermission(PERMISSIONS.VIEW_DEVICE_COMMANDS);

  // 视图状态
  const [activeTab, setActiveTab] = useState<'devices' | 'history'>('devices');
  const [searchTerm, setSearchTerm] = useState('');
  const [statusFilter, setStatusFilter] = useState<string>('all');
  const [selectedDevice, setSelectedDevice] = useState<SimpleDevice | null>(null);
  const [commandsForDevice, setCommandsForDevice] = useState<DeviceCommandDto[]>([]);

  // 设备和指令数据
  const [devices, setDevices] = useState<SimpleDevice[]>([]);
  const [devicesLoading, setDevicesLoading] = useState(true);
  const [devicesError, setDevicesError] = useState<string | null>(null);
  const [allCommands, setAllCommands] = useState<DeviceCommandDto[]>([]);
  const [commandsLoading, setCommandsLoading] = useState(false);

  // 指令历史
  const [historySearch, setHistorySearch] = useState('');
  const [historyStatusFilter, setHistoryStatusFilter] = useState<string>('all');
  const [expandedCommandId, setExpandedCommandId] = useState<number | null>(null);
  const [commandHistories, setCommandHistories] = useState<Record<number, CommandHistoryDto[]>>({});
  const [loadingHistoryId, setLoadingHistoryId] = useState<number | null>(null);

  // 发送指令弹窗
  const [showSendModal, setShowSendModal] = useState(false);
  const [selectedTemplate, setSelectedTemplate] = useState<CommandTemplate>(COMMAND_TEMPLATES[0]);
  const [paramValues, setParamValues] = useState<Record<string, string>>({});
  const [timeoutSec, setTimeoutSec] = useState(30);
  const [sending, setSending] = useState(false);
  const [sendResult, setSendResult] = useState<{ success: boolean; message: string } | null>(null);

  // 批量发送
  const [selectedDeviceIds, setSelectedDeviceIds] = useState<number[]>([]);
  const [batchMode, setBatchMode] = useState(false);

  // ── 加载设备列表 ─────────────────────────────────────────────────────────────
  const loadDevices = useCallback(async () => {
    setDevicesLoading(true);
    setDevicesError(null);
    try {
      const response = await deviceApi.getDevices(1, 100);
      if (response.data.success && response.data.data) {
        const deviceList = response.data.data.items || [];
        setDevices(deviceList.map(mapDeviceToSimple));
      }
    } catch (err) {
      console.error('加载设备列表失败:', err);
      setDevicesError('加载设备列表失败');
    } finally {
      setDevicesLoading(false);
    }
  }, []);

  // ── 加载指令历史 ─────────────────────────────────────────────────────────────
  const loadCommands = useCallback(async () => {
    setCommandsLoading(true);
    try {
      const response = await deviceCommandApi.getCommands({ page: 1, pageSize: 50 });
      if (response.data.success && response.data.data) {
        const items = response.data.data.items || [];
        setAllCommands(items);
      }
    } catch (err) {
      console.error('加载指令历史失败:', err);
    } finally {
      setCommandsLoading(false);
    }
  }, []);

  // 初始加载
  useEffect(() => {
    loadDevices();
    if (canView) {
      loadCommands();
    }
  }, [loadDevices, loadCommands, canView]);

  // 切换到历史 Tab 时刷新
  useEffect(() => {
    if (activeTab === 'history' && allCommands.length === 0) {
      loadCommands();
    }
  }, [activeTab, loadCommands, allCommands.length]);

  // ── 筛选设备 ─────────────────────────────────────────────────────────────────
  const filteredDevices = devices.filter(d => {
    const matchSearch = d.name.toLowerCase().includes(searchTerm.toLowerCase()) ||
      d.serialNumber.toLowerCase().includes(searchTerm.toLowerCase());
    const matchStatus = statusFilter === 'all' || d.status === statusFilter;
    return matchSearch && matchStatus;
  });

  // ── 筛选历史 ─────────────────────────────────────────────────────────────────
  const filteredCommands = allCommands.filter(c => {
    const matchSearch = (c.deviceName ?? '').toLowerCase().includes(historySearch.toLowerCase()) ||
      getCommandTypeLabel(c.commandType).includes(historySearch);
    const matchStatus = historyStatusFilter === 'all' || c.status === historyStatusFilter;
    return matchSearch && matchStatus;
  });

  // ── 打开发送弹窗 ─────────────────────────────────────────────────────────────
  const openSendModal = (device: SimpleDevice) => {
    setSelectedDevice(device);
    setSelectedTemplate(COMMAND_TEMPLATES[0]);
    setParamValues({});
    setSendResult(null);
    setShowSendModal(true);
  };

  // ── 切换模板 ─────────────────────────────────────────────────────────────────
  const selectTemplate = (tpl: CommandTemplate) => {
    setSelectedTemplate(tpl);
    const defaults: Record<string, string> = {};
    tpl.params.forEach(p => {
      if (p.defaultValue !== undefined) defaults[p.key] = String(p.defaultValue);
    });
    setParamValues(defaults);
    setSendResult(null);
  };

  // ── 发送指令 ─────────────────────────────────────────────────────────────────
  const handleSend = useCallback(async () => {
    if (!selectedDevice) return;
    setSending(true);
    setSendResult(null);

    try {
      let parameters: Record<string, unknown> = {};
      if (selectedTemplate.type === 'custom') {
        try { parameters = JSON.parse(paramValues['payload'] || '{}'); }
        catch { parameters = { payload: paramValues['payload'] }; }
      } else {
        selectedTemplate.params.forEach(p => {
          parameters[p.key] = paramValues[p.key] ?? '';
        });
      }

      const request: SendCommandRequest = {
        deviceId: selectedDevice.id,
        commandType: selectedTemplate.type,
        parameters,
        timeoutSeconds: timeoutSec,
      };

      // 调用真实 API
      const response = await deviceCommandApi.sendCommand(request);
      if (response.data.success) {
        // 刷新指令列表
        await loadCommands();
        setSendResult({ success: true, message: `指令已发送成功` });
      } else {
        setSendResult({ success: false, message: response.data.message || '发送失败' });
      }
    } catch (err: any) {
      setSendResult({ success: false, message: err?.response?.data?.message || err?.message || '发送失败' });
    } finally {
      setSending(false);
    }
  }, [selectedDevice, selectedTemplate, paramValues, timeoutSec, loadCommands]);

  // ── 取消指令 ─────────────────────────────────────────────────────────────────
  const handleCancel = async (commandId: number) => {
    try {
      const response = await deviceCommandApi.cancelCommand(commandId);
      if (response.data.success) {
        // 更新本地状态
        setAllCommands(prev =>
          prev.map(c => c.id === commandId
            ? { ...c, status: 'Failed' as CommandStatus, errorMessage: '已手动取消' }
            : c
          )
        );
      }
    } catch (err) {
      console.error('取消指令失败:', err);
    }
  };

  // ── 重试指令 ─────────────────────────────────────────────────────────────────
  const handleRetry = async (commandId: number) => {
    try {
      const response = await deviceCommandApi.retryCommand(commandId);
      if (response.data.success) {
        // 刷新指令列表
        await loadCommands();
      }
    } catch (err) {
      console.error('重试指令失败:', err);
    }
  };

  // ── 展开历史详情 ─────────────────────────────────────────────────────────────
  const toggleCommandExpand = async (commandId: number) => {
    if (expandedCommandId === commandId) {
      setExpandedCommandId(null);
      return;
    }
    setExpandedCommandId(commandId);
    if (!commandHistories[commandId]) {
      setLoadingHistoryId(commandId);
      try {
        const response = await deviceCommandApi.getCommandHistory(commandId);
        if (response.data.success && response.data.data) {
          setCommandHistories(prev => ({ ...prev, [commandId]: response.data.data }));
        }
      } catch (err) {
        console.error('加载指令历史失败:', err);
      } finally {
        setLoadingHistoryId(null);
      }
    }
  };

  const totalCommands = allCommands.length;
  const successCommands = allCommands.filter(c => c.status === 'Success').length;
  const pendingCommands = allCommands.filter(c => c.status === 'Pending' || c.status === 'Sent').length;
  const failedCommands = allCommands.filter(c => c.status === 'Failed' || c.status === 'Timeout').length;

  return (
    <div className="p-6 space-y-6">
      {/* ── 页头 ─────────────────────────────────────────────────────────── */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-white flex items-center gap-2">
            <Terminal className="w-6 h-6 text-blue-400" />
            设备控制
          </h1>
          <p className="text-slate-400 text-sm mt-1">远程下发指令，实时监控执行状态</p>
        </div>
      </div>

      {/* ── 统计卡片 ──────────────────────────────────────────────────────── */}
      <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
        {[
          { label: '指令总数', value: totalCommands, icon: <Hash className="w-5 h-5" />, color: 'text-blue-400', bg: 'bg-blue-500/10 border-blue-500/20' },
          { label: '执行成功', value: successCommands, icon: <CheckCircle2 className="w-5 h-5" />, color: 'text-green-400', bg: 'bg-green-500/10 border-green-500/20' },
          { label: '执行中', value: pendingCommands, icon: <Activity className="w-5 h-5" />, color: 'text-yellow-400', bg: 'bg-yellow-500/10 border-yellow-500/20' },
          { label: '失败/超时', value: failedCommands, icon: <AlertCircle className="w-5 h-5" />, color: 'text-red-400', bg: 'bg-red-500/10 border-red-500/20' },
        ].map(stat => (
          <div key={stat.label} className={`rounded-xl border p-4 ${stat.bg}`}>
            <div className={`flex items-center gap-2 ${stat.color}`}>
              {stat.icon}
              <span className="text-sm">{stat.label}</span>
            </div>
            <p className={`text-2xl font-bold mt-2 ${stat.color}`}>{stat.value}</p>
          </div>
        ))}
      </div>

      {/* ── Tab ───────────────────────────────────────────────────────────── */}
      <div className="flex gap-1 bg-slate-800/60 p-1 rounded-xl w-fit border border-slate-700">
        <button
          onClick={() => setActiveTab('devices')}
          className={`flex items-center gap-2 px-4 py-2 rounded-lg text-sm font-medium transition-colors
            ${activeTab === 'devices' ? 'bg-blue-600 text-white' : 'text-slate-400 hover:text-white'}`}
        >
          <Cpu className="w-4 h-4" /> 设备列表
        </button>
        {canView && (
          <button
            onClick={() => setActiveTab('history')}
            className={`flex items-center gap-2 px-4 py-2 rounded-lg text-sm font-medium transition-colors
              ${activeTab === 'history' ? 'bg-blue-600 text-white' : 'text-slate-400 hover:text-white'}`}
          >
            <History className="w-4 h-4" /> 指令历史
          </button>
        )}
      </div>

      {/* ── 设备列表 Tab ─────────────────────────────────────────────────── */}
      {activeTab === 'devices' && (
        <div className="space-y-4">
          {/* 搜索/筛选栏 */}
          <div className="flex flex-wrap gap-3">
            <div className="relative flex-1 min-w-48">
              <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-400" />
              <input
                value={searchTerm}
                onChange={e => setSearchTerm(e.target.value)}
                placeholder="搜索设备名称或序列号..."
                className="w-full pl-9 pr-3 py-2 bg-slate-800 border border-slate-700 text-white rounded-lg text-sm focus:outline-none focus:border-blue-500"
              />
            </div>
            <select
              value={statusFilter}
              onChange={e => setStatusFilter(e.target.value)}
              className="bg-slate-800 border border-slate-700 text-slate-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:border-blue-500"
            >
              <option value="all">全部状态</option>
              <option value="online">在线</option>
              <option value="offline">离线</option>
              <option value="warning">告警</option>
            </select>
          </div>

          {/* 设备卡片 */}
          <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-4">
            {/* 加载状态 */}
            {devicesLoading && (
              <div className="col-span-3 flex flex-col items-center justify-center py-16 text-slate-400">
                <Loader2 className="w-8 h-8 animate-spin mb-3 text-blue-400" />
                <p>加载设备列表...</p>
              </div>
            )}

            {/* 错误状态 */}
            {!devicesLoading && devicesError && (
              <div className="col-span-3 flex flex-col items-center justify-center py-16 text-slate-400">
                <AlertCircle className="w-8 h-8 mb-3 text-red-400" />
                <p className="mb-3">{devicesError}</p>
                <button
                  onClick={loadDevices}
                  className="px-4 py-2 bg-blue-600 hover:bg-blue-500 text-white rounded-lg text-sm"
                >
                  重试
                </button>
              </div>
            )}

            {/* 设备列表 */}
            {!devicesLoading && !devicesError && filteredDevices.map(device => (
              <motion.div
                key={device.id}
                layout
                className="rounded-xl border border-slate-700 bg-slate-800/60 p-4 hover:border-slate-600 transition-colors"
              >
                <div className="flex items-start justify-between">
                  <div className="flex items-center gap-3">
                    {getDeviceStatusDot(device.status)}
                    <div>
                      <p className="text-white font-medium text-sm">{device.name}</p>
                      <p className="text-slate-400 text-xs">{device.serialNumber}</p>
                    </div>
                  </div>
                  <span className="px-2 py-0.5 rounded-md text-xs bg-slate-700 text-slate-300">
                    {device.protocolType}
                  </span>
                </div>

                <div className="mt-3 grid grid-cols-2 gap-2 text-xs text-slate-400">
                  <span>型号：{device.model}</span>
                  <span>位置：{device.location}</span>
                </div>

                <div className="mt-4 flex items-center gap-2">
                  <span className={`text-xs px-2 py-0.5 rounded-full ${
                    device.status === 'online' ? 'bg-green-500/20 text-green-400' :
                    device.status === 'warning' ? 'bg-yellow-500/20 text-yellow-400' :
                    'bg-slate-600/40 text-slate-400'
                  }`}>
                    {device.status === 'online' ? '在线' : device.status === 'warning' ? '告警' : '离线'}
                  </span>
                  <div className="flex-1" />
                  {canSend && (
                    <button
                      onClick={() => openSendModal(device)}
                      disabled={device.status === 'offline'}
                      className="flex items-center gap-1.5 px-3 py-1.5 bg-blue-600 hover:bg-blue-500 disabled:opacity-40 disabled:cursor-not-allowed text-white rounded-lg text-xs font-medium transition-colors"
                    >
                      <Send className="w-3 h-3" />
                      发送指令
                    </button>
                  )}
                </div>
              </motion.div>
            ))}

            {!devicesLoading && !devicesError && filteredDevices.length === 0 && (
              <div className="col-span-3 text-center py-16 text-slate-400">
                <Cpu className="w-12 h-12 mx-auto mb-3 opacity-30" />
                <p>没有匹配的设备</p>
              </div>
            )}
          </div>
        </div>
      )}

      {/* ── 指令历史 Tab ────────────────────────────────────────────────── */}
      {activeTab === 'history' && canView && (
        <div className="space-y-4">
          {/* 搜索/筛选 */}
          <div className="flex flex-wrap gap-3">
            <div className="relative flex-1 min-w-48">
              <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-400" />
              <input
                value={historySearch}
                onChange={e => setHistorySearch(e.target.value)}
                placeholder="搜索设备名称或指令类型..."
                className="w-full pl-9 pr-3 py-2 bg-slate-800 border border-slate-700 text-white rounded-lg text-sm focus:outline-none focus:border-blue-500"
              />
            </div>
            <select
              value={historyStatusFilter}
              onChange={e => setHistoryStatusFilter(e.target.value)}
              className="bg-slate-800 border border-slate-700 text-slate-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:border-blue-500"
            >
              <option value="all">全部状态</option>
              <option value="Pending">待发送</option>
              <option value="Sent">已发送</option>
              <option value="Success">成功</option>
              <option value="Failed">失败</option>
              <option value="Timeout">超时</option>
            </select>
          </div>

          {/* 指令列表 */}
          <div className="space-y-2">
            {/* 加载状态 */}
            {commandsLoading && (
              <div className="flex flex-col items-center justify-center py-16 text-slate-400">
                <Loader2 className="w-8 h-8 animate-spin mb-3 text-blue-400" />
                <p>加载指令历史...</p>
              </div>
            )}

            {/* 指令列表 */}
            {!commandsLoading && filteredCommands.map(cmd => (
              <motion.div
                key={cmd.id}
                layout
                className="rounded-xl border border-slate-700 bg-slate-800/60 overflow-hidden"
              >
                <div
                  className="flex items-center gap-4 px-4 py-3 cursor-pointer hover:bg-slate-700/40 transition-colors"
                  onClick={() => toggleCommandExpand(cmd.id)}
                >
                  <div className="flex-1 min-w-0">
                    <div className="flex items-center gap-2 flex-wrap">
                      <span className="text-white text-sm font-medium">{cmd.deviceName}</span>
                      <span className="px-2 py-0.5 rounded-md text-xs bg-slate-700 text-slate-300">
                        {getCommandTypeLabel(cmd.commandType)}
                      </span>
                      {getStatusBadge(cmd.status)}
                      {cmd.retryCount > 0 && (
                        <span className="text-xs text-amber-400">重试 {cmd.retryCount} 次</span>
                      )}
                    </div>
                    <div className="flex items-center gap-4 mt-1 text-xs text-slate-400">
                      <span className="flex items-center gap-1">
                        <Clock className="w-3 h-3" /> {cmd.createdAt}
                      </span>
                      {cmd.result && <span className="text-green-400">{cmd.result}</span>}
                      {cmd.errorMessage && <span className="text-red-400">{cmd.errorMessage}</span>}
                    </div>
                  </div>

                  <div className="flex items-center gap-2 flex-shrink-0">
                    {canCancel && (cmd.status === 'Pending' || cmd.status === 'Sent') && (
                      <button
                        onClick={e => { e.stopPropagation(); handleCancel(cmd.id); }}
                        className="flex items-center gap-1 px-2 py-1 text-xs text-red-400 hover:text-red-300 bg-red-500/10 hover:bg-red-500/20 rounded-lg border border-red-500/20 transition-colors"
                      >
                        <Ban className="w-3 h-3" /> 取消
                      </button>
                    )}
                    {canSend && (cmd.status === 'Failed' || cmd.status === 'Timeout') && (
                      <button
                        onClick={e => { e.stopPropagation(); handleRetry(cmd.id); }}
                        className="flex items-center gap-1 px-2 py-1 text-xs text-amber-400 hover:text-amber-300 bg-amber-500/10 hover:bg-amber-500/20 rounded-lg border border-amber-500/20 transition-colors"
                      >
                        <RefreshCw className="w-3 h-3" /> 重试
                      </button>
                    )}
                    {expandedCommandId === cmd.id
                      ? <ChevronDown className="w-4 h-4 text-slate-400" />
                      : <ChevronRight className="w-4 h-4 text-slate-400" />
                    }
                  </div>
                </div>

                {/* 展开：执行历史 */}
                <AnimatePresence>
                  {expandedCommandId === cmd.id && (
                    <motion.div
                      initial={{ height: 0, opacity: 0 }}
                      animate={{ height: 'auto', opacity: 1 }}
                      exit={{ height: 0, opacity: 0 }}
                      transition={{ duration: 0.2 }}
                      className="border-t border-slate-700 px-4 py-3 bg-slate-900/40"
                    >
                      <p className="text-slate-400 text-xs mb-2 flex items-center gap-1">
                        <History className="w-3 h-3" /> 执行轨迹
                      </p>
                      {loadingHistoryId === cmd.id ? (
                        <div className="flex items-center gap-2 text-slate-500 text-xs">
                          <RefreshCw className="w-3 h-3 animate-spin" /> 加载中...
                        </div>
                      ) : commandHistories[cmd.id] ? (
                        <div className="space-y-1">
                          {commandHistories[cmd.id].map((h, idx) => (
                            <div key={h.id} className="flex items-start gap-3 text-xs">
                              <span className="text-slate-500 flex-shrink-0 w-40">{h.createdAt}</span>
                              <span className="px-1.5 py-0.5 rounded bg-slate-700 text-slate-300 flex-shrink-0">{h.type}</span>
                              <span className="text-slate-300">{h.description}</span>
                            </div>
                          ))}
                        </div>
                      ) : (
                        <div className="text-slate-500 text-xs">暂无执行轨迹</div>
                      )}
                      <div className="mt-3 pt-3 border-t border-slate-700">
                        <p className="text-slate-400 text-xs mb-1">指令参数</p>
                        <pre className="text-xs text-slate-300 bg-slate-950 rounded p-2 overflow-x-auto">
                          {(() => { try { return JSON.stringify(JSON.parse(cmd.parameters), null, 2); } catch { return cmd.parameters; } })()}
                        </pre>
                      </div>
                    </motion.div>
                  )}
                </AnimatePresence>
              </motion.div>
            ))}

            {!commandsLoading && filteredCommands.length === 0 && (
              <div className="text-center py-16 text-slate-400">
                <List className="w-12 h-12 mx-auto mb-3 opacity-30" />
                <p>暂无指令记录</p>
              </div>
            )}
          </div>
        </div>
      )}

      {/* ── 发送指令弹窗 ────────────────────────────────────────────────── */}
      <AnimatePresence>
        {showSendModal && selectedDevice && (
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            className="fixed inset-0 bg-black/60 backdrop-blur-sm z-50 flex items-center justify-center p-4"
            onClick={() => setShowSendModal(false)}
          >
            <motion.div
              initial={{ scale: 0.95, opacity: 0 }}
              animate={{ scale: 1, opacity: 1 }}
              exit={{ scale: 0.95, opacity: 0 }}
              className="bg-slate-800 border border-slate-700 rounded-2xl p-6 w-full max-w-xl shadow-2xl max-h-[90vh] overflow-y-auto"
              onClick={e => e.stopPropagation()}
            >
              {/* 弹窗头 */}
              <div className="flex items-center justify-between mb-5">
                <div>
                  <h2 className="text-lg font-semibold text-white flex items-center gap-2">
                    <Send className="w-5 h-5 text-blue-400" />
                    发送指令
                  </h2>
                  <p className="text-slate-400 text-xs mt-0.5">
                    目标设备：<span className="text-blue-300">{selectedDevice.name}</span>
                  </p>
                </div>
                <button onClick={() => setShowSendModal(false)} className="text-slate-400 hover:text-white">
                  <X className="w-5 h-5" />
                </button>
              </div>

              {/* 指令类型选择 */}
              <div className="mb-4">
                <label className="text-sm text-slate-300 mb-2 block">指令类型</label>
                <div className="grid grid-cols-2 gap-2">
                  {COMMAND_TEMPLATES.map(tpl => (
                    <button
                      key={tpl.type}
                      onClick={() => selectTemplate(tpl)}
                      className={`flex items-center gap-2 px-3 py-2.5 rounded-lg border text-sm text-left transition-colors
                        ${selectedTemplate.type === tpl.type
                          ? 'border-blue-500 bg-blue-500/20 text-blue-300'
                          : 'border-slate-600 bg-slate-900 text-slate-300 hover:border-slate-500'
                        }`}
                    >
                      {tpl.icon}
                      <div>
                        <p className="font-medium">{tpl.label}</p>
                        <p className="text-xs opacity-60">{tpl.description}</p>
                      </div>
                    </button>
                  ))}
                </div>
              </div>

              {/* 参数填写 */}
              {selectedTemplate.params.length > 0 && (
                <div className="mb-4 space-y-3">
                  <label className="text-sm text-slate-300 block">指令参数</label>
                  {selectedTemplate.params.map(param => (
                    <div key={param.key}>
                      <label className="text-xs text-slate-400 mb-1 block">{param.label}</label>
                      {param.type === 'select' ? (
                        <select
                          value={paramValues[param.key] ?? String(param.defaultValue ?? '')}
                          onChange={e => setParamValues(prev => ({ ...prev, [param.key]: e.target.value }))}
                          className="w-full bg-slate-900 border border-slate-600 text-white rounded-lg px-3 py-2 text-sm focus:outline-none focus:border-blue-500"
                        >
                          {param.options?.map(opt => (
                            <option key={opt} value={opt}>{opt}</option>
                          ))}
                        </select>
                      ) : (
                        <input
                          type={param.type === 'number' ? 'number' : 'text'}
                          value={paramValues[param.key] ?? String(param.defaultValue ?? '')}
                          onChange={e => setParamValues(prev => ({ ...prev, [param.key]: e.target.value }))}
                          placeholder={param.placeholder}
                          className="w-full bg-slate-900 border border-slate-600 text-white rounded-lg px-3 py-2 text-sm focus:outline-none focus:border-blue-500"
                        />
                      )}
                    </div>
                  ))}
                </div>
              )}

              {/* 超时设置 */}
              <div className="mb-5">
                <label className="text-sm text-slate-300 mb-1 block">超时时间（秒）</label>
                <input
                  type="number"
                  value={timeoutSec}
                  onChange={e => setTimeoutSec(Number(e.target.value))}
                  min={5}
                  max={300}
                  className="w-full bg-slate-900 border border-slate-600 text-white rounded-lg px-3 py-2 text-sm focus:outline-none focus:border-blue-500"
                />
              </div>

              {/* 发送结果 */}
              <AnimatePresence>
                {sendResult && (
                  <motion.div
                    initial={{ opacity: 0, y: -8 }}
                    animate={{ opacity: 1, y: 0 }}
                    exit={{ opacity: 0 }}
                    className={`mb-4 flex items-center gap-2 px-3 py-2.5 rounded-lg text-sm border
                      ${sendResult.success
                        ? 'bg-green-500/10 border-green-500/30 text-green-400'
                        : 'bg-red-500/10 border-red-500/30 text-red-400'
                      }`}
                  >
                    {sendResult.success ? <CheckCircle2 className="w-4 h-4 flex-shrink-0" /> : <XCircle className="w-4 h-4 flex-shrink-0" />}
                    {sendResult.message}
                  </motion.div>
                )}
              </AnimatePresence>

              {/* 操作按钮 */}
              <div className="flex justify-end gap-3">
                <button
                  onClick={() => setShowSendModal(false)}
                  className="px-4 py-2 text-sm text-slate-300 hover:text-white bg-slate-700 hover:bg-slate-600 rounded-lg transition-colors"
                >
                  {sendResult?.success ? '关闭' : '取消'}
                </button>
                {!sendResult?.success && (
                  <button
                    onClick={handleSend}
                    disabled={sending}
                    className="flex items-center gap-2 px-4 py-2 text-sm bg-blue-600 hover:bg-blue-500 text-white rounded-lg transition-colors disabled:opacity-50"
                  >
                    {sending ? <RefreshCw className="w-4 h-4 animate-spin" /> : <Send className="w-4 h-4" />}
                    {sending ? '发送中...' : '确认发送'}
                  </button>
                )}
              </div>
            </motion.div>
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}
