import { useState, useCallback } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import {
  Globe, Plus, Edit2, Trash2, Play, Square,
  RefreshCw, CheckCircle2, XCircle, Clock, Activity,
  ChevronDown, ChevronRight, Settings, Wifi, WifiOff,
  Tag, Cpu, X, Save, AlertCircle, Server,
} from 'lucide-react';
import { useAuth } from '@/app/contexts/AuthContext';
import { PERMISSIONS } from '@/app/config/permissions';

// ── Types ──────────────────────────────────────────────────────────────────────
type ProtocolType = 'MQTT' | 'ModbusTCP' | 'ModbusRTU' | 'OpcUA';
type ConnectionStatus = 'Connected' | 'Disconnected' | 'Connecting' | 'Error';

interface ProtocolConfig {
  id: string;
  name: string;
  protocolType: ProtocolType;
  status: ConnectionStatus;
  host?: string;
  port?: number;
  serialPort?: string;
  baudRate?: number;
  endpoint?: string;
  deviceCount: number;
  lastConnectedAt?: string;
  errorMessage?: string;
  enabled: boolean;
}

// ── Mock 数据 ──────────────────────────────────────────────────────────────────
const INITIAL_PROTOCOLS: ProtocolConfig[] = [
  {
    id: '1', name: 'MQTT 主服务器', protocolType: 'MQTT',
    status: 'Connected', host: '192.168.1.100', port: 1883,
    deviceCount: 42, lastConnectedAt: '2026-04-30 09:15:22', enabled: true,
  },
  {
    id: '2', name: 'Modbus TCP 控制器', protocolType: 'ModbusTCP',
    status: 'Connected', host: '192.168.1.200', port: 502,
    deviceCount: 8, lastConnectedAt: '2026-04-30 08:30:10', enabled: true,
  },
  {
    id: '3', name: 'Modbus RTU 串口设备', protocolType: 'ModbusRTU',
    status: 'Disconnected', serialPort: 'COM3', baudRate: 9600,
    deviceCount: 3, enabled: false,
  },
  {
    id: '4', name: 'OPC UA 工业网关', protocolType: 'OpcUA',
    status: 'Error', endpoint: 'opc.tcp://192.168.1.50:4840',
    deviceCount: 5, errorMessage: '连接超时，请检查网络', enabled: true,
  },
];

const PROTOCOL_OPTIONS: { value: ProtocolType; label: string; icon: string }[] = [
  { value: 'MQTT', label: 'MQTT', icon: '📡' },
  { value: 'ModbusTCP', label: 'Modbus TCP', icon: '🌐' },
  { value: 'ModbusRTU', label: 'Modbus RTU', icon: '🔌' },
  { value: 'OpcUA', label: 'OPC UA', icon: '🏭' },
];

// ── 辅助函数 ───────────────────────────────────────────────────────────────────
function getStatusBadge(status: ConnectionStatus) {
  switch (status) {
    case 'Connected':
      return (
        <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-medium bg-green-500/20 text-green-400 border border-green-500/30">
          <CheckCircle2 className="w-3 h-3" /> 已连接
        </span>
      );
    case 'Disconnected':
      return (
        <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-medium bg-slate-500/20 text-slate-400 border border-slate-500/30">
          <WifiOff className="w-3 h-3" /> 已断开
        </span>
      );
    case 'Connecting':
      return (
        <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-medium bg-blue-500/20 text-blue-400 border border-blue-500/30">
          <RefreshCw className="w-3 h-3 animate-spin" /> 连接中
        </span>
      );
    case 'Error':
      return (
        <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-medium bg-red-500/20 text-red-400 border border-red-500/30">
          <XCircle className="w-3 h-3" /> 错误
        </span>
      );
  }
}

function getProtocolIcon(type: ProtocolType) {
  const found = PROTOCOL_OPTIONS.find(p => p.value === type);
  return found?.icon ?? '❓';
}

// ── 表单初始值 ─────────────────────────────────────────────────────────────────
const EMPTY_FORM: Partial<ProtocolConfig> = {
  name: '', protocolType: 'MQTT', host: '', port: 1883,
  serialPort: '', baudRate: 9600, endpoint: '', enabled: true,
};

// ── 主组件 ─────────────────────────────────────────────────────────────────────
export function ProtocolManagementPage() {
  const { hasPermission } = useAuth();
  const canManage = hasPermission(PERMISSIONS.MANAGE_PROTOCOLS);

  const [protocols, setProtocols] = useState<ProtocolConfig[]>(INITIAL_PROTOCOLS);
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [showModal, setShowModal] = useState(false);
  const [editTarget, setEditTarget] = useState<ProtocolConfig | null>(null);
  const [form, setForm] = useState<Partial<ProtocolConfig>>(EMPTY_FORM);
  const [showDeleteConfirm, setShowDeleteConfirm] = useState<string | null>(null);
  const [connectingId, setConnectingId] = useState<string | null>(null);

  // ── 开始 / 停止协议 ──────────────────────────────────────────────────────────
  const handleToggle = useCallback(async (protocol: ProtocolConfig) => {
    if (connectingId) return;

    if (protocol.status === 'Connected') {
      // 停止
      setProtocols(prev =>
        prev.map(p => p.id === protocol.id
          ? { ...p, status: 'Disconnected', enabled: false, lastConnectedAt: undefined }
          : p
        )
      );
    } else {
      // 连接
      setConnectingId(protocol.id);
      setProtocols(prev =>
        prev.map(p => p.id === protocol.id ? { ...p, status: 'Connecting', enabled: true } : p)
      );
      // 模拟连接延迟
      await new Promise(r => setTimeout(r, 1500));
      setProtocols(prev =>
        prev.map(p => p.id === protocol.id
          ? { ...p, status: 'Connected', lastConnectedAt: new Date().toLocaleString('zh-CN') }
          : p
        )
      );
      setConnectingId(null);
    }
  }, [connectingId]);

  // ── 打开新增/编辑弹窗 ───────────────────────────────────────────────────────
  const openAdd = () => {
    setEditTarget(null);
    setForm(EMPTY_FORM);
    setShowModal(true);
  };

  const openEdit = (p: ProtocolConfig) => {
    setEditTarget(p);
    setForm({ ...p });
    setShowModal(true);
  };

  // ── 保存 ────────────────────────────────────────────────────────────────────
  const handleSave = () => {
    if (!form.name || !form.protocolType) return;
    if (editTarget) {
      setProtocols(prev =>
        prev.map(p => p.id === editTarget.id ? { ...p, ...form } as ProtocolConfig : p)
      );
    } else {
      const newProtocol: ProtocolConfig = {
        id: String(Date.now()),
        status: 'Disconnected',
        deviceCount: 0,
        ...(form as ProtocolConfig),
      };
      setProtocols(prev => [...prev, newProtocol]);
    }
    setShowModal(false);
  };

  // ── 删除 ────────────────────────────────────────────────────────────────────
  const handleDelete = (id: string) => {
    setProtocols(prev => prev.filter(p => p.id !== id));
    setShowDeleteConfirm(null);
  };

  const connectedCount = protocols.filter(p => p.status === 'Connected').length;
  const errorCount = protocols.filter(p => p.status === 'Error').length;

  return (
    <div className="p-6 space-y-6">
      {/* ── 页头 ─────────────────────────────────────────────────────────── */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-white flex items-center gap-2">
            <Globe className="w-6 h-6 text-blue-400" />
            协议管理
          </h1>
          <p className="text-slate-400 text-sm mt-1">管理物联网协议接入配置及连接状态</p>
        </div>
        {canManage && (
          <button
            onClick={openAdd}
            className="flex items-center gap-2 px-4 py-2 bg-blue-600 hover:bg-blue-500 text-white rounded-lg text-sm font-medium transition-colors"
          >
            <Plus className="w-4 h-4" />
            新增协议
          </button>
        )}
      </div>

      {/* ── 统计卡片 ──────────────────────────────────────────────────────── */}
      <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
        {[
          { label: '协议总数', value: protocols.length, icon: <Server className="w-5 h-5" />, color: 'text-blue-400', bg: 'bg-blue-500/10 border-blue-500/20' },
          { label: '已连接', value: connectedCount, icon: <Wifi className="w-5 h-5" />, color: 'text-green-400', bg: 'bg-green-500/10 border-green-500/20' },
          { label: '已断开', value: protocols.filter(p => p.status === 'Disconnected').length, icon: <WifiOff className="w-5 h-5" />, color: 'text-slate-400', bg: 'bg-slate-500/10 border-slate-500/20' },
          { label: '连接异常', value: errorCount, icon: <AlertCircle className="w-5 h-5" />, color: 'text-red-400', bg: 'bg-red-500/10 border-red-500/20' },
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

      {/* ── 协议列表 ──────────────────────────────────────────────────────── */}
      <div className="space-y-3">
        {protocols.map(protocol => (
          <motion.div
            key={protocol.id}
            layout
            className="rounded-xl border border-slate-700 bg-slate-800/60 overflow-hidden"
          >
            {/* 行 */}
            <div
              className="flex items-center gap-4 px-5 py-4 cursor-pointer hover:bg-slate-700/40 transition-colors"
              onClick={() => setExpandedId(expandedId === protocol.id ? null : protocol.id)}
            >
              <span className="text-2xl flex-shrink-0">{getProtocolIcon(protocol.protocolType)}</span>

              <div className="flex-1 min-w-0">
                <div className="flex items-center gap-2">
                  <span className="text-white font-medium truncate">{protocol.name}</span>
                  <span className="px-2 py-0.5 rounded-md text-xs bg-slate-700 text-slate-300">
                    {protocol.protocolType}
                  </span>
                </div>
                <div className="flex items-center gap-4 mt-1 text-xs text-slate-400">
                  <span className="flex items-center gap-1">
                    <Cpu className="w-3 h-3" /> {protocol.deviceCount} 台设备
                  </span>
                  {protocol.lastConnectedAt && (
                    <span className="flex items-center gap-1">
                      <Clock className="w-3 h-3" /> {protocol.lastConnectedAt}
                    </span>
                  )}
                  {protocol.errorMessage && (
                    <span className="text-red-400 flex items-center gap-1">
                      <AlertCircle className="w-3 h-3" /> {protocol.errorMessage}
                    </span>
                  )}
                </div>
              </div>

              <div className="flex items-center gap-3 flex-shrink-0">
                {getStatusBadge(protocol.status)}

                {canManage && (
                  <>
                    <button
                      onClick={e => { e.stopPropagation(); handleToggle(protocol); }}
                      disabled={connectingId === protocol.id}
                      className={`flex items-center gap-1 px-3 py-1.5 rounded-lg text-xs font-medium transition-colors
                        ${protocol.status === 'Connected'
                          ? 'bg-red-500/20 text-red-400 hover:bg-red-500/30 border border-red-500/30'
                          : 'bg-green-500/20 text-green-400 hover:bg-green-500/30 border border-green-500/30'
                        } disabled:opacity-50 disabled:cursor-not-allowed`}
                    >
                      {connectingId === protocol.id
                        ? <RefreshCw className="w-3 h-3 animate-spin" />
                        : protocol.status === 'Connected'
                          ? <Square className="w-3 h-3" />
                          : <Play className="w-3 h-3" />
                      }
                      {connectingId === protocol.id ? '连接中' : protocol.status === 'Connected' ? '停止' : '启动'}
                    </button>

                    <button
                      onClick={e => { e.stopPropagation(); openEdit(protocol); }}
                      className="p-1.5 text-slate-400 hover:text-white hover:bg-slate-700 rounded-lg transition-colors"
                    >
                      <Edit2 className="w-4 h-4" />
                    </button>

                    <button
                      onClick={e => { e.stopPropagation(); setShowDeleteConfirm(protocol.id); }}
                      className="p-1.5 text-slate-400 hover:text-red-400 hover:bg-red-500/10 rounded-lg transition-colors"
                    >
                      <Trash2 className="w-4 h-4" />
                    </button>
                  </>
                )}

                {expandedId === protocol.id
                  ? <ChevronDown className="w-4 h-4 text-slate-400" />
                  : <ChevronRight className="w-4 h-4 text-slate-400" />
                }
              </div>
            </div>

            {/* 展开详情 */}
            <AnimatePresence>
              {expandedId === protocol.id && (
                <motion.div
                  initial={{ height: 0, opacity: 0 }}
                  animate={{ height: 'auto', opacity: 1 }}
                  exit={{ height: 0, opacity: 0 }}
                  transition={{ duration: 0.2 }}
                  className="border-t border-slate-700 px-5 py-4 bg-slate-900/40"
                >
                  <div className="grid grid-cols-2 md:grid-cols-4 gap-4 text-sm">
                    {protocol.host && (
                      <div>
                        <span className="text-slate-400 block text-xs mb-1">主机地址</span>
                        <span className="text-white">{protocol.host}</span>
                      </div>
                    )}
                    {protocol.port && (
                      <div>
                        <span className="text-slate-400 block text-xs mb-1">端口</span>
                        <span className="text-white">{protocol.port}</span>
                      </div>
                    )}
                    {protocol.serialPort && (
                      <div>
                        <span className="text-slate-400 block text-xs mb-1">串口</span>
                        <span className="text-white">{protocol.serialPort}</span>
                      </div>
                    )}
                    {protocol.baudRate && (
                      <div>
                        <span className="text-slate-400 block text-xs mb-1">波特率</span>
                        <span className="text-white">{protocol.baudRate}</span>
                      </div>
                    )}
                    {protocol.endpoint && (
                      <div className="col-span-2">
                        <span className="text-slate-400 block text-xs mb-1">端点地址</span>
                        <span className="text-white">{protocol.endpoint}</span>
                      </div>
                    )}
                    <div>
                      <span className="text-slate-400 block text-xs mb-1">状态</span>
                      {getStatusBadge(protocol.status)}
                    </div>
                    <div>
                      <span className="text-slate-400 block text-xs mb-1">是否启用</span>
                      <span className={protocol.enabled ? 'text-green-400' : 'text-slate-400'}>
                        {protocol.enabled ? '已启用' : '已禁用'}
                      </span>
                    </div>
                  </div>
                </motion.div>
              )}
            </AnimatePresence>
          </motion.div>
        ))}

        {protocols.length === 0 && (
          <div className="text-center py-16 text-slate-400">
            <Globe className="w-12 h-12 mx-auto mb-3 opacity-30" />
            <p>暂无协议配置</p>
            {canManage && (
              <button onClick={openAdd} className="mt-3 text-blue-400 hover:underline text-sm">
                + 新增协议
              </button>
            )}
          </div>
        )}
      </div>

      {/* ── 新增/编辑弹窗 ──────────────────────────────────────────────────── */}
      <AnimatePresence>
        {showModal && (
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            className="fixed inset-0 bg-black/60 backdrop-blur-sm z-50 flex items-center justify-center p-4"
            onClick={() => setShowModal(false)}
          >
            <motion.div
              initial={{ scale: 0.95, opacity: 0 }}
              animate={{ scale: 1, opacity: 1 }}
              exit={{ scale: 0.95, opacity: 0 }}
              className="bg-slate-800 border border-slate-700 rounded-2xl p-6 w-full max-w-lg shadow-2xl"
              onClick={e => e.stopPropagation()}
            >
              <div className="flex items-center justify-between mb-5">
                <h2 className="text-lg font-semibold text-white flex items-center gap-2">
                  <Settings className="w-5 h-5 text-blue-400" />
                  {editTarget ? '编辑协议' : '新增协议'}
                </h2>
                <button onClick={() => setShowModal(false)} className="text-slate-400 hover:text-white">
                  <X className="w-5 h-5" />
                </button>
              </div>

              <div className="space-y-4">
                <div>
                  <label className="text-sm text-slate-300 mb-1 block">协议名称 *</label>
                  <input
                    value={form.name ?? ''}
                    onChange={e => setForm(f => ({ ...f, name: e.target.value }))}
                    placeholder="请输入协议名称"
                    className="w-full bg-slate-900 border border-slate-600 text-white rounded-lg px-3 py-2 text-sm focus:outline-none focus:border-blue-500"
                  />
                </div>

                <div>
                  <label className="text-sm text-slate-300 mb-1 block">协议类型 *</label>
                  <div className="grid grid-cols-2 gap-2">
                    {PROTOCOL_OPTIONS.map(opt => (
                      <button
                        key={opt.value}
                        onClick={() => setForm(f => ({ ...f, protocolType: opt.value }))}
                        className={`flex items-center gap-2 px-3 py-2 rounded-lg border text-sm transition-colors
                          ${form.protocolType === opt.value
                            ? 'border-blue-500 bg-blue-500/20 text-blue-300'
                            : 'border-slate-600 bg-slate-900 text-slate-300 hover:border-slate-500'
                          }`}
                      >
                        <span>{opt.icon}</span> {opt.label}
                      </button>
                    ))}
                  </div>
                </div>

                {/* MQTT / ModbusTCP 字段 */}
                {(form.protocolType === 'MQTT' || form.protocolType === 'ModbusTCP') && (
                  <div className="grid grid-cols-2 gap-3">
                    <div>
                      <label className="text-sm text-slate-300 mb-1 block">主机地址</label>
                      <input
                        value={form.host ?? ''}
                        onChange={e => setForm(f => ({ ...f, host: e.target.value }))}
                        placeholder="192.168.1.100"
                        className="w-full bg-slate-900 border border-slate-600 text-white rounded-lg px-3 py-2 text-sm focus:outline-none focus:border-blue-500"
                      />
                    </div>
                    <div>
                      <label className="text-sm text-slate-300 mb-1 block">端口</label>
                      <input
                        type="number"
                        value={form.port ?? ''}
                        onChange={e => setForm(f => ({ ...f, port: Number(e.target.value) }))}
                        placeholder={form.protocolType === 'MQTT' ? '1883' : '502'}
                        className="w-full bg-slate-900 border border-slate-600 text-white rounded-lg px-3 py-2 text-sm focus:outline-none focus:border-blue-500"
                      />
                    </div>
                  </div>
                )}

                {/* ModbusRTU 字段 */}
                {form.protocolType === 'ModbusRTU' && (
                  <div className="grid grid-cols-2 gap-3">
                    <div>
                      <label className="text-sm text-slate-300 mb-1 block">串口</label>
                      <input
                        value={form.serialPort ?? ''}
                        onChange={e => setForm(f => ({ ...f, serialPort: e.target.value }))}
                        placeholder="COM3 / /dev/ttyUSB0"
                        className="w-full bg-slate-900 border border-slate-600 text-white rounded-lg px-3 py-2 text-sm focus:outline-none focus:border-blue-500"
                      />
                    </div>
                    <div>
                      <label className="text-sm text-slate-300 mb-1 block">波特率</label>
                      <select
                        value={form.baudRate ?? 9600}
                        onChange={e => setForm(f => ({ ...f, baudRate: Number(e.target.value) }))}
                        className="w-full bg-slate-900 border border-slate-600 text-white rounded-lg px-3 py-2 text-sm focus:outline-none focus:border-blue-500"
                      >
                        {[2400, 4800, 9600, 19200, 38400, 57600, 115200].map(b => (
                          <option key={b} value={b}>{b}</option>
                        ))}
                      </select>
                    </div>
                  </div>
                )}

                {/* OpcUA 字段 */}
                {form.protocolType === 'OpcUA' && (
                  <div>
                    <label className="text-sm text-slate-300 mb-1 block">端点地址</label>
                    <input
                      value={form.endpoint ?? ''}
                      onChange={e => setForm(f => ({ ...f, endpoint: e.target.value }))}
                      placeholder="opc.tcp://192.168.1.50:4840"
                      className="w-full bg-slate-900 border border-slate-600 text-white rounded-lg px-3 py-2 text-sm focus:outline-none focus:border-blue-500"
                    />
                  </div>
                )}

                <div className="flex items-center gap-2">
                  <input
                    id="enabled-toggle"
                    type="checkbox"
                    checked={form.enabled ?? true}
                    onChange={e => setForm(f => ({ ...f, enabled: e.target.checked }))}
                    className="w-4 h-4 accent-blue-500"
                  />
                  <label htmlFor="enabled-toggle" className="text-sm text-slate-300">创建后立即启用</label>
                </div>
              </div>

              <div className="flex justify-end gap-3 mt-6">
                <button
                  onClick={() => setShowModal(false)}
                  className="px-4 py-2 text-sm text-slate-300 hover:text-white bg-slate-700 hover:bg-slate-600 rounded-lg transition-colors"
                >
                  取消
                </button>
                <button
                  onClick={handleSave}
                  disabled={!form.name}
                  className="flex items-center gap-2 px-4 py-2 text-sm bg-blue-600 hover:bg-blue-500 text-white rounded-lg transition-colors disabled:opacity-50"
                >
                  <Save className="w-4 h-4" />
                  {editTarget ? '保存修改' : '创建协议'}
                </button>
              </div>
            </motion.div>
          </motion.div>
        )}
      </AnimatePresence>

      {/* ── 删除确认弹窗 ────────────────────────────────────────────────────── */}
      <AnimatePresence>
        {showDeleteConfirm && (
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            className="fixed inset-0 bg-black/60 backdrop-blur-sm z-50 flex items-center justify-center p-4"
            onClick={() => setShowDeleteConfirm(null)}
          >
            <motion.div
              initial={{ scale: 0.95 }}
              animate={{ scale: 1 }}
              exit={{ scale: 0.95 }}
              className="bg-slate-800 border border-slate-700 rounded-2xl p-6 w-full max-w-sm shadow-2xl"
              onClick={e => e.stopPropagation()}
            >
              <div className="flex items-center gap-3 mb-4">
                <div className="p-2 bg-red-500/20 rounded-xl">
                  <Trash2 className="w-5 h-5 text-red-400" />
                </div>
                <h3 className="text-white font-semibold">删除协议</h3>
              </div>
              <p className="text-slate-400 text-sm mb-5">
                确认删除此协议配置？已连接的设备将断开连接，此操作不可撤销。
              </p>
              <div className="flex justify-end gap-3">
                <button
                  onClick={() => setShowDeleteConfirm(null)}
                  className="px-4 py-2 text-sm text-slate-300 hover:text-white bg-slate-700 hover:bg-slate-600 rounded-lg transition-colors"
                >
                  取消
                </button>
                <button
                  onClick={() => handleDelete(showDeleteConfirm)}
                  className="px-4 py-2 text-sm bg-red-600 hover:bg-red-500 text-white rounded-lg transition-colors"
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
