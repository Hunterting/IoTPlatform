import { useState, useCallback, useEffect } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import {
  Globe, Plus, Edit2, Trash2, Play, Square,
  RefreshCw, CheckCircle2, XCircle, Clock, Activity,
  ChevronDown, ChevronRight, Settings, Wifi, WifiOff,
  X, Save, AlertCircle, Server, Loader2, Cpu, Monitor,
  Folder, MapPin, Check,
} from 'lucide-react';
import { useAuth } from '@/app/contexts/AuthContext';
import { PERMISSIONS } from '@/app/config/permissions';
import { protocolApi, ProtocolConfig, CreateProtocolConfigRequest, UpdateProtocolConfigRequest } from '@/app/services/api/protocolApi';
import { deviceApi } from '@/app/services/api/deviceApi';
import { adaptDateFromBackend, adaptIdFromBackend } from '@/app/services/adapters';
import type { DeviceDto } from '@/app/services/api/types/device.types';

// ── 类型定义 ──────────────────────────────────────────────────────────────────
type ProtocolType = 'MQTT' | 'ModbusTCP' | 'ModbusRTU' | 'OpcUA' | 'ANSHENG_MQTT' | 'modbus' | 'mqtt' | 'opcua' | 'http' | 'tcp' | 'bacnet' | 'ansheng_mqtt';

type ConnectionStatus = 'Connected' | 'Disconnected' | 'Connecting' | 'Error' | 'active' | 'inactive';

// ── 常量 ─────────────────────────────────────────────────────────────────────
/**
 * 安圣 MQTT 协议类型标识（小写）。
 * 后端 ProtocolAdapterFactory 会先做 ToUpperInvariant() 归一，
 * 归一后落到 "ANSHENG_MQTT" 分支，因此前端用小写 value 即可。
 */
const ANSHENG_MQTT_TYPE = 'ansheng_mqtt';

const PROTOCOL_OPTIONS: { value: string; label: string; icon: string }[] = [
  { value: 'mqtt', label: 'MQTT', icon: 'M' },
  { value: 'modbus', label: 'Modbus TCP', icon: 'T' },
  { value: 'opcua', label: 'OPC UA', icon: 'O' },
  { value: 'http', label: 'HTTP', icon: 'H' },
  { value: 'tcp', label: 'TCP', icon: 'C' },
  { value: ANSHENG_MQTT_TYPE, label: '安圣 MQTT', icon: 'A' },
];

/**
 * 安圣 MQTT 配置项默认值。
 *
 * ⚠️ 键名必须是 PascalCase：后端 AnShengMqttProtocolAdapter.ConnectAsync 使用
 * `JsonSerializer.Deserialize<AnShengMqttProtocolOptions>(connectionString)` 且**未传
 * JsonSerializerOptions**，System.Text.Json 默认大小写敏感（PropertyNameCaseInsensitive=false）；
 * 而 ProtocolConfigService 保存时直接 `JsonSerializer.Serialize(request.Config)`
 * （Dictionary 的 key 原样落库，无 DictionaryKeyPolicy）。
 * 因此只有 PascalCase 键才能正确绑定到 C# 属性。
 * 参照 appsettings.json:70 与 docs/system_design.md:325 的官方样例。
 */
const ANSHENG_DEFAULT_CONFIG: Record<string, unknown> = {
  Host: '',
  Port: 1883,
  // 匿名接入时保持 null（对应 C# string? = null），避免下发空字符串凭据被 Broker 拒绝
  Username: null,
  Password: null,
  ClientIdPrefix: 'iot_platform_ansheng',
  PublishTopicPattern: '/iot/server/iot-board/+',
  WillTopicPattern: '/iot/server/iot-board/+',
  SubscribeTopicTemplate: '/iot/client/iot-board/{imei}',
  CleanSession: true,
  QosLevel: 1,
  TimeoutSeconds: 30,
  KeepAliveSeconds: 60,
  CommandMinIntervalMs: 100,
  AutoConfigureAutoReport: true,
};

/** 安圣协议详情区展示字段（key 与后端 AnShengMqttProtocolOptions 属性同名） */
const ANSHENG_DETAIL_FIELDS: { key: string; label: string }[] = [
  { key: 'Host', label: 'Broker 地址' },
  { key: 'Port', label: 'Broker 端口' },
  { key: 'Username', label: '用户名' },
  { key: 'ClientIdPrefix', label: '客户端 ID 前缀' },
  { key: 'PublishTopicPattern', label: '上行数据主题' },
  { key: 'WillTopicPattern', label: 'Will 遗愿主题' },
  { key: 'SubscribeTopicTemplate', label: '下行命令主题模板' },
];

/** 判断某个协议类型是否为安圣 MQTT（忽略大小写，兼容后端可能返回的 ANSHENG_MQTT） */
function isAnShengType(type: string | undefined): boolean {
  return (type ?? '').toLowerCase() === ANSHENG_MQTT_TYPE;
}

// ── 辅助函数 ──────────────────────────────────────────────────────────────────
function getStatusFromString(status: string): ConnectionStatus {
  if (status === 'active' || status === 'Connected') return 'Connected';
  if (status === 'inactive' || status === 'Disconnected') return 'Disconnected';
  return status as ConnectionStatus;
}

function getStatusBadge(status: string) {
  const connectionStatus = getStatusFromString(status);
  
  switch (connectionStatus) {
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
    default:
      return (
        <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-medium bg-slate-500/20 text-slate-400 border border-slate-500/30">
          {status}
        </span>
      );
  }
}

function getProtocolIcon(type: string) {
  const found = PROTOCOL_OPTIONS.find(p => p.value === type?.toLowerCase());
  return found?.icon ?? 'P';
}

function getProtocolLabel(type: string) {
  const found = PROTOCOL_OPTIONS.find(p => p.value === type?.toLowerCase());
  return found?.label ?? type;
}

// ── 表单初始值 ─────────────────────────────────────────────────────────────────
const EMPTY_FORM = {
  name: '',
  type: 'mqtt',
  description: '',
  config: {},
  isActive: true,
};

// ── 主组件 ─────────────────────────────────────────────────────────────────────
export function ProtocolManagementPage() {
  const { hasPermission, currentCustomer } = useAuth();
  const canManage = hasPermission(PERMISSIONS.MANAGE_PROTOCOLS);

  // 获取当前租户代码
  const appCode = currentCustomer?.appCode;

  // 数据状态
  const [protocols, setProtocols] = useState<ProtocolConfig[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [totalCount, setTotalCount] = useState(0);

  // UI 状态
  const [expandedId, setExpandedId] = useState<number | null>(null);
  const [showModal, setShowModal] = useState(false);
  const [editTarget, setEditTarget] = useState<ProtocolConfig | null>(null);
  const [form, setForm] = useState<Partial<CreateProtocolConfigRequest>>(EMPTY_FORM);
  const [showDeleteConfirm, setShowDeleteConfirm] = useState<number | null>(null);
  const [connectingId, setConnectingId] = useState<number | null>(null);
  const [saving, setSaving] = useState(false);
  const [deleting, setDeleting] = useState(false);

  // 关联设备状态
  const [protocolDevices, setProtocolDevices] = useState<Record<number, DeviceDto[]>>({});
  const [loadingDevices, setLoadingDevices] = useState<number | null>(null);

  // 设备选择弹窗状态
  const [showDeviceSelector, setShowDeviceSelector] = useState(false);
  const [selectingProtocolId, setSelectingProtocolId] = useState<number | null>(null);
  const [selectedDeviceIds, setSelectedDeviceIds] = useState<Set<string>>(new Set());
  const [allDevices, setAllDevices] = useState<DeviceDto[]>([]);
  const [loadingAllDevices, setLoadingAllDevices] = useState(false);
  const [deviceSearchKeyword, setDeviceSearchKeyword] = useState('');
  const [updatingDevices, setUpdatingDevices] = useState(false);

  // 搜索状态
  const [searchKeyword, setSearchKeyword] = useState('');

  // ── 加载数据 ─────────────────────────────────────────────────────────────────
  const loadProtocols = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const response = await protocolApi.getProtocolConfigs({
        page: 1,
        pageSize: 100,
        keyword: searchKeyword || undefined,
      });
      if (response.code === 200 && response.data) {
        setProtocols(response.data.items || []);
        setTotalCount(response.data.totalCount || 0);
      } else {
        setError(response.message || '加载协议配置失败');
      }
    } catch (err: any) {
      setError(err?.message || '加载协议配置失败');
      console.error('加载协议配置失败:', err);
    } finally {
      setLoading(false);
    }
  }, [searchKeyword]);

  useEffect(() => {
    loadProtocols();
  }, [loadProtocols]);

  // ── 启动/停止协议 ──────────────────────────────────────────────────────────
  const handleToggle = useCallback(async (protocol: ProtocolConfig) => {
    if (connectingId) return;
    const isActive = protocol.status === 'active' || protocol.status === 'Connected';

    try {
      if (isActive) {
        // 停止
        setConnectingId(protocol.id);
        const response = await protocolApi.stopProtocol(protocol.id);
        if (response.code === 200) {
          setProtocols(prev =>
            prev.map(p => p.id === protocol.id ? { ...p, status: 'inactive' } : p)
          );
        }
      } else {
        // 启动
        setConnectingId(protocol.id);
        const response = await protocolApi.startProtocol(protocol.id);
        if (response.code === 200) {
          setProtocols(prev =>
            prev.map(p => p.id === protocol.id ? { ...p, status: 'active' } : p)
          );
        }
      }
    } catch (err) {
      console.error('操作协议失败:', err);
    } finally {
      setConnectingId(null);
    }
  }, [connectingId]);

  // ── 打开新增/编辑弹窗 ───────────────────────────────────────────────────────
  const openAdd = () => {
    setEditTarget(null);
    setForm({ ...EMPTY_FORM, config: {} });
    setShowModal(true);
  };

  const openEdit = (p: ProtocolConfig) => {
    setEditTarget(p);
    // 安圣类型：用默认值补齐后端可能缺失的字段（已有值优先）
    const nextConfig = isAnShengType(p.type)
      ? { ...ANSHENG_DEFAULT_CONFIG, ...(p.config || {}) }
      : (p.config || {});
    setForm({
      name: p.name,
      type: p.type,
      description: p.description,
      isActive: p.isActive,
      status: p.status,
      config: nextConfig,
    });
    setShowModal(true);
  };

  // ── 保存 ────────────────────────────────────────────────────────────────────
  const handleSave = async () => {
    if (!form.name || !form.type) return;
    setSaving(true);

    try {
      if (editTarget) {
        // 更新
        const request: UpdateProtocolConfigRequest = {
          name: form.name!,
          status: form.status,
          isActive: form.isActive,
          description: form.description,
          deviceIds: form.deviceIds,
          config: form.config,
        };
        const response = await protocolApi.updateProtocolConfig(editTarget.id, request);
        if (response.code === 200) {
          setShowModal(false);
          loadProtocols();
        }
      } else {
        // 创建 - 传递 appCode
        const request: CreateProtocolConfigRequest = {
          name: form.name!,
          type: form.type!,
          description: form.description,
          deviceIds: form.deviceIds,
          config: form.config,
          isActive: form.isActive,
          appCode: appCode, // 传递租户代码
        };
        const response = await protocolApi.createProtocolConfig(request);
        if (response.code === 200) {
          setShowModal(false);
          loadProtocols();
        }
      }
    } catch (err) {
      console.error('保存协议配置失败:', err);
    } finally {
      setSaving(false);
    }
  };

  // ── 删除 ────────────────────────────────────────────────────────────────────
  const handleDelete = async (id: number) => {
    setDeleting(true);
    try {
      const response = await protocolApi.deleteProtocolConfig(id);
      if (response.code === 200) {
        setShowDeleteConfirm(null);
        loadProtocols();
      }
    } catch (err) {
      console.error('删除协议配置失败:', err);
    } finally {
      setDeleting(false);
    }
  };

  // ── 计算统计数据 ────────────────────────────────────────────────────────────
  const connectedCount = protocols.filter(p => p.status === 'active' || p.status === 'Connected').length;
  const errorCount = protocols.filter(p => p.status === 'Error').length;

  // ── 获取配置信息 ────────────────────────────────────────────────────────────
  const getConfigValue = (config: Record<string, unknown> | undefined, key: string): string => {
    if (!config) return '';
    const value = config[key];
    if (value === null || value === undefined) return '';
    return String(value);
  };

  /** 读取表单 config 中的布尔值（缺省时回落到 fallback） */
  const getConfigBool = (key: string, fallback: boolean): boolean => {
    const value = (form.config as Record<string, unknown> | undefined)?.[key];
    if (value === null || value === undefined) return fallback;
    return Boolean(value);
  };

  /** 写入表单 config 的单个字段 */
  const setConfigValue = (key: string, value: unknown) => {
    setForm(f => ({ ...f, config: { ...(f.config ?? {}), [key]: value } }));
  };

  /**
   * 写入数字型配置字段。
   * 输入框被清空或非法时回落到 fallback，避免把空字符串写进 int 字段
   * 导致后端 JsonSerializer.Deserialize 抛 JsonException。
   */
  const setNumberConfigValue = (key: string, raw: string, fallback: number) => {
    const parsed = Number.parseInt(raw, 10);
    setConfigValue(key, Number.isNaN(parsed) ? fallback : parsed);
  };

  // ── 加载关联设备 ────────────────────────────────────────────────────────────
  const loadProtocolDevices = useCallback(async (protocol: ProtocolConfig) => {
    if (!protocol.deviceIds || protocol.deviceIds.length === 0) {
      setProtocolDevices(prev => ({ ...prev, [protocol.id]: [] }));
      return;
    }
    if (protocolDevices[protocol.id]) return; // 已加载过

    setLoadingDevices(protocol.id);
    try {
      const response = await deviceApi.getDevices(1, 100);
      if (response.data.code === 200 && response.data.data) {
        // 筛选出关联的设备（支持 number 和 string 类型匹配）
        const protocolDeviceIds = new Set(protocol.deviceIds.map(id => String(id)));
        const relatedDevices = response.data.data.items.filter(d => 
          protocolDeviceIds.has(String(d.id)) || protocolDeviceIds.has(String(d.id))
        );
        setProtocolDevices(prev => ({ ...prev, [protocol.id]: relatedDevices }));
      }
    } catch (err) {
      console.error('加载关联设备失败:', err);
    } finally {
      setLoadingDevices(null);
    }
  }, [protocolDevices]);

  // ── 展开/收起详情 ────────────────────────────────────────────────────────────
  const handleExpand = useCallback((protocol: ProtocolConfig) => {
    if (expandedId === protocol.id) {
      setExpandedId(null);
    } else {
      setExpandedId(protocol.id);
      loadProtocolDevices(protocol);
    }
  }, [expandedId, loadProtocolDevices]);

  // ── 打开设备选择弹窗 ─────────────────────────────────────────────────────────
  const openDeviceSelector = useCallback((protocol: ProtocolConfig) => {
    setSelectingProtocolId(protocol.id);
    setSelectedDeviceIds(new Set());
    setDeviceSearchKeyword('');
    setShowDeviceSelector(true);
    setLoadingAllDevices(true);

    // 加载所有设备
    deviceApi.getDevices(1, 1000).then(response => {
      if (response.data.code === 200 && response.data.data) {
        setAllDevices(response.data.data.items);
      }
    }).catch(err => {
      console.error('加载设备列表失败:', err);
    }).finally(() => {
      setLoadingAllDevices(false);
    });
  }, []);

  // ── 关闭设备选择弹窗 ─────────────────────────────────────────────────────────
  const closeDeviceSelector = useCallback(() => {
    setShowDeviceSelector(false);
    setSelectingProtocolId(null);
    setSelectedDeviceIds(new Set());
    setDeviceSearchKeyword('');
    setAllDevices([]);
  }, []);

  // ── 切换设备选择 ─────────────────────────────────────────────────────────────
  const toggleDeviceSelection = useCallback((deviceId: string) => {
    setSelectedDeviceIds(prev => {
      const next = new Set(prev);
      if (next.has(deviceId)) {
        next.delete(deviceId);
      } else {
        next.add(deviceId);
      }
      return next;
    });
  }, []);

  // ── 添加设备到协议 ───────────────────────────────────────────────────────────
  const handleAddDevices = useCallback(async () => {
    if (!selectingProtocolId || selectedDeviceIds.size === 0) {
      closeDeviceSelector();
      return;
    }

    const protocol = protocols.find(p => p.id === selectingProtocolId);
    if (!protocol) return;

    setUpdatingDevices(true);
    try {
      // 合并现有设备ID和新选设备ID
      const existingIds = protocol.deviceIds || [];
      const newIds = Array.from(selectedDeviceIds).map(id => parseInt(id));
      const allIds = [...new Set([...existingIds.map(id => Number(id)), ...newIds])];

      const response = await protocolApi.updateProtocolConfig(selectingProtocolId, {
        name: protocol.name,
        type: protocol.type,
        status: protocol.status || 'active',
        description: protocol.description,
        isActive: protocol.isActive,
        config: protocol.config,
        deviceIds: allIds,
      });

      if (response.code === 200) {
        // 更新协议列表
        const updatedProtocol = { ...protocol, deviceIds: allIds };
        setProtocols(prev =>
          prev.map(p => p.id === selectingProtocolId ? updatedProtocol : p)
        );
        // 清除设备缓存并自动重新加载（如果协议详情是展开状态）
        setProtocolDevices(prev => {
          const next = { ...prev };
          delete next[selectingProtocolId];
          return next;
        });
        // 如果协议详情已展开，自动重新加载设备列表
        if (expandedId === selectingProtocolId) {
          loadProtocolDevices(updatedProtocol);
        }
        closeDeviceSelector();
        loadProtocols();
      }
    } catch (err) {
      console.error('添加设备失败:', err);
    } finally {
      setUpdatingDevices(false);
    }
  }, [selectingProtocolId, selectedDeviceIds, protocols, closeDeviceSelector, loadProtocols, allDevices]);

  // ── 从协议删除设备 ───────────────────────────────────────────────────────────
  const handleRemoveDevice = useCallback(async (protocol: ProtocolConfig, deviceId: string) => {
    const currentIds = protocol.deviceIds || [];
    const newIds = currentIds.filter(id => String(id) !== deviceId);

    setUpdatingDevices(true);
    try {
      const response = await protocolApi.updateProtocolConfig(protocol.id, {
        name: protocol.name,
        type: protocol.type,
        status: protocol.status || 'active',
        description: protocol.description,
        isActive: protocol.isActive,
        config: protocol.config,
        deviceIds: newIds,
      });

      if (response.code === 200) {
        // 更新协议列表
        const updatedProtocol = { ...protocol, deviceIds: newIds };
        setProtocols(prev =>
          prev.map(p => p.id === protocol.id ? updatedProtocol : p)
        );
        // 清除设备缓存并自动重新加载（如果协议详情是展开状态）
        setProtocolDevices(prev => {
          const next = { ...prev };
          delete next[protocol.id];
          return next;
        });
        // 如果协议详情已展开，自动重新加载设备列表
        if (expandedId === protocol.id) {
          loadProtocolDevices(updatedProtocol);
        }
        loadProtocols();
      }
    } catch (err) {
      console.error('删除设备失败:', err);
    } finally {
      setUpdatingDevices(false);
    }
  }, [loadProtocols]);

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
        <div className="flex items-center gap-3">
          {/* 搜索框 */}
          <div className="relative">
            <input
              type="text"
              placeholder="搜索协议名称..."
              value={searchKeyword}
              onChange={(e) => setSearchKeyword(e.target.value)}
              className="w-64 bg-slate-800 border border-slate-700 text-white rounded-lg px-4 py-2 pl-10 text-sm focus:outline-none focus:border-blue-500"
            />
            <Globe className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-400" />
          </div>
          <button
            onClick={loadProtocols}
            className="p-2 bg-slate-700 hover:bg-slate-600 text-white rounded-lg transition-colors"
            title="刷新"
          >
            <RefreshCw className={`w-4 h-4 ${loading ? 'animate-spin' : ''}`} />
          </button>
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
      </div>

      {/* ── 统计卡片 ──────────────────────────────────────────────────────── */}
      <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
        {[
          { label: '协议总数', value: totalCount, icon: <Server className="w-5 h-5" />, color: 'text-blue-400', bg: 'bg-blue-500/10 border-blue-500/20' },
          { label: '已连接', value: connectedCount, icon: <Wifi className="w-5 h-5" />, color: 'text-green-400', bg: 'bg-green-500/10 border-green-500/20' },
          { label: '已断开', value: protocols.filter(p => p.status === 'inactive' || p.status === 'Disconnected').length, icon: <WifiOff className="w-5 h-5" />, color: 'text-slate-400', bg: 'bg-slate-500/10 border-slate-500/20' },
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

      {/* ── 错误提示 ──────────────────────────────────────────────────────── */}
      {error && (
        <div className="bg-red-500/10 border border-red-500/30 rounded-lg p-4 text-red-400">
          {error}
          <button onClick={() => setError(null)} className="ml-4 underline">关闭</button>
        </div>
      )}

      {/* ── 加载状态 ──────────────────────────────────────────────────────── */}
      {loading && (
        <div className="flex items-center justify-center py-16">
          <Loader2 className="w-8 h-8 text-blue-400 animate-spin" />
          <span className="ml-3 text-slate-400">加载中...</span>
        </div>
      )}

      {/* ── 协议列表 ──────────────────────────────────────────────────────── */}
      {!loading && (
        <div className="space-y-3">
          {protocols.length === 0 ? (
            <div className="text-center py-16 text-slate-400">
              <Globe className="w-12 h-12 mx-auto mb-3 opacity-30" />
              <p>暂无协议配置</p>
              {canManage && (
                <button onClick={openAdd} className="mt-3 text-blue-400 hover:underline text-sm">
                  + 新增协议
                </button>
              )}
            </div>
          ) : (
            protocols.map(protocol => (
              <motion.div
                key={protocol.id}
                layout
                className="rounded-xl border border-slate-700 bg-slate-800/60 overflow-hidden"
              >
                {/* 行 */}
                <div
                  className="flex items-center gap-4 px-5 py-4 cursor-pointer hover:bg-slate-700/40 transition-colors"
                  onClick={() => handleExpand(protocol)}
                >
                  {/* 协议图标 */}
                  <div className="w-10 h-10 rounded-lg bg-blue-500/20 text-blue-400 flex items-center justify-center font-bold flex-shrink-0">
                    {getProtocolIcon(protocol.type)}
                  </div>

                  <div className="flex-1 min-w-0">
                    <div className="flex items-center gap-2">
                      <span className="text-white font-medium truncate">{protocol.name}</span>
                      <span className="px-2 py-0.5 rounded-md text-xs bg-slate-700 text-slate-300">
                        {getProtocolLabel(protocol.type)}
                      </span>
                    </div>
                    <div className="flex items-center gap-4 mt-1 text-xs text-slate-400">
                      <span className="flex items-center gap-1">
                        <Clock className="w-3 h-3" /> {protocol.deviceIds?.length || 0} 台设备
                      </span>
                      {protocol.updatedAt && (
                        <span className="flex items-center gap-1">
                          <Clock className="w-3 h-3" /> {adaptDateFromBackend(protocol.updatedAt)}
                        </span>
                      )}
                      {protocol.description && (
                        <span className="truncate max-w-xs">{protocol.description}</span>
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
                            ${protocol.status === 'active' || protocol.status === 'Connected'
                              ? 'bg-red-500/20 text-red-400 hover:bg-red-500/30 border border-red-500/30'
                              : 'bg-green-500/20 text-green-400 hover:bg-green-500/30 border border-green-500/30'
                            } disabled:opacity-50 disabled:cursor-not-allowed`}
                        >
                          {connectingId === protocol.id
                            ? <RefreshCw className="w-3 h-3 animate-spin" />
                            : protocol.status === 'active' || protocol.status === 'Connected'
                              ? <Square className="w-3 h-3" />
                              : <Play className="w-3 h-3" />
                          }
                          {connectingId === protocol.id ? '处理中' : (protocol.status === 'active' || protocol.status === 'Connected') ? '停止' : '启动'}
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
                        {protocol.config && (
                          <>
                            {getConfigValue(protocol.config as Record<string, unknown>, 'host') && (
                              <div>
                                <span className="text-slate-400 block text-xs mb-1">主机地址</span>
                                <span className="text-white">{getConfigValue(protocol.config as Record<string, unknown>, 'host')}</span>
                              </div>
                            )}
                            {getConfigValue(protocol.config as Record<string, unknown>, 'port') && (
                              <div>
                                <span className="text-slate-400 block text-xs mb-1">端口</span>
                                <span className="text-white">{getConfigValue(protocol.config as Record<string, unknown>, 'port')}</span>
                              </div>
                            )}
                            {getConfigValue(protocol.config as Record<string, unknown>, 'endpoint') && (
                              <div>
                                <span className="text-slate-400 block text-xs mb-1">端点地址</span>
                                <span className="text-white">{getConfigValue(protocol.config as Record<string, unknown>, 'endpoint')}</span>
                              </div>
                            )}
                            {getConfigValue(protocol.config as Record<string, unknown>, 'serialPort') && (
                              <div>
                                <span className="text-slate-400 block text-xs mb-1">串口</span>
                                <span className="text-white">{getConfigValue(protocol.config as Record<string, unknown>, 'serialPort')}</span>
                              </div>
                            )}
                            {getConfigValue(protocol.config as Record<string, unknown>, 'baudRate') && (
                              <div>
                                <span className="text-slate-400 block text-xs mb-1">波特率</span>
                                <span className="text-white">{getConfigValue(protocol.config as Record<string, unknown>, 'baudRate')}</span>
                              </div>
                            )}
                            {/* 安圣 MQTT 专属配置展示（键名为 PascalCase，与后端 AnShengMqttProtocolOptions 一致） */}
                            {isAnShengType(protocol.type) && ANSHENG_DETAIL_FIELDS.map(field => {
                              const fieldValue = getConfigValue(protocol.config as Record<string, unknown>, field.key);
                              if (!fieldValue) return null;
                              return (
                                <div key={field.key}>
                                  <span className="text-slate-400 block text-xs mb-1">{field.label}</span>
                                  <span className="text-white break-all">{fieldValue}</span>
                                </div>
                              );
                            })}
                          </>
                        )}
                        <div>
                          <span className="text-slate-400 block text-xs mb-1">状态</span>
                          {getStatusBadge(protocol.status)}
                        </div>
                        <div>
                          <span className="text-slate-400 block text-xs mb-1">是否启用</span>
                          <span className={protocol.isActive ? 'text-green-400' : 'text-slate-400'}>
                            {protocol.isActive ? '已启用' : '已禁用'}
                          </span>
                        </div>
                        {protocol.description && (
                          <div className="col-span-2">
                            <span className="text-slate-400 block text-xs mb-1">描述</span>
                            <span className="text-white">{protocol.description}</span>
                          </div>
                        )}
                      </div>

                      {/* 关联设备列表 */}
                      <div className="mt-4 pt-4 border-t border-slate-700">
                        <div className="flex items-center justify-between mb-3">
                          <div className="flex items-center gap-2">
                            <Cpu className="w-4 h-4 text-blue-400" />
                            <span className="text-sm text-slate-300 font-medium">关联设备</span>
                            <span className="text-xs text-slate-500">
                              ({protocol.deviceIds?.length || 0} 台)
                            </span>
                          </div>
                          {canManage && (
                            <button
                              onClick={() => openDeviceSelector(protocol)}
                              disabled={updatingDevices}
                              className="flex items-center gap-1.5 px-3 py-1.5 text-xs bg-blue-600/20 hover:bg-blue-600/30 text-blue-400 border border-blue-500/30 rounded-lg transition-colors disabled:opacity-50"
                            >
                              <Plus className="w-3 h-3" />
                              添加设备
                            </button>
                          )}
                        </div>
                        {loadingDevices === protocol.id || updatingDevices ? (
                          <div className="flex items-center gap-2 py-3">
                            <Loader2 className="w-4 h-4 text-blue-400 animate-spin" />
                            <span className="text-sm text-slate-400">加载关联设备...</span>
                          </div>
                        ) : protocolDevices[protocol.id]?.length ? (
                          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-2">
                            {protocolDevices[protocol.id].map(device => (
                              <div
                                key={device.id}
                                className="relative px-3 py-2.5 bg-slate-800/60 rounded-lg border border-slate-700/50 hover:border-slate-600 transition-colors group"
                              >
                                {canManage && (
                                  <button
                                    onClick={() => handleRemoveDevice(protocol, device.id)}
                                    disabled={updatingDevices}
                                    className="absolute top-2 right-2 p-1 text-slate-500 hover:text-red-400 hover:bg-red-500/10 rounded opacity-0 group-hover:opacity-100 transition-all disabled:opacity-50"
                                    title="移除设备"
                                  >
                                    <X className="w-3.5 h-3.5" />
                                  </button>
                                )}
                                <div className="flex items-start gap-3">
                                  <div className="w-8 h-8 rounded-md bg-slate-700 flex items-center justify-center flex-shrink-0">
                                    <Monitor className="w-4 h-4 text-slate-400" />
                                  </div>
                                  <div className="flex-1 min-w-0">
                                    <p className="text-sm text-white font-medium truncate pr-6">{device.name}</p>
                                    <div className="flex items-center gap-1 mt-0.5">
                                      <Folder className="w-3 h-3 text-slate-500" />
                                      <span className="text-xs text-slate-500 truncate">
                                        {device.projectName || '未分配项目'}
                                      </span>
                                    </div>
                                    <div className="flex items-center gap-1 mt-0.5">
                                      <MapPin className="w-3 h-3 text-slate-500" />
                                      <span className="text-xs text-slate-500 truncate">
                                        {device.areaName || '未分配区域'}
                                      </span>
                                    </div>
                                    <div className="flex items-center gap-2 mt-1.5">
                                      <span className={`px-1.5 py-0.5 rounded text-xs ${
                                        device.status === 'online'
                                          ? 'bg-green-500/20 text-green-400'
                                          : device.status === 'warning'
                                          ? 'bg-yellow-500/20 text-yellow-400'
                                          : 'bg-slate-600/50 text-slate-400'
                                      }`}>
                                        {device.status === 'online' ? '在线' : device.status === 'warning' ? '警告' : device.status === 'offline' ? '离线' : device.status}
                                      </span>
                                    </div>
                                  </div>
                                </div>
                              </div>
                            ))}
                          </div>
                        ) : (
                          <div className="text-sm text-slate-500 py-4 text-center">
                            {protocol.deviceIds?.length ? '无法加载设备信息' : '暂无关联设备'}
                          </div>
                        )}
                      </div>
                    </motion.div>
                  )}
                </AnimatePresence>
              </motion.div>
            ))
          )}
        </div>
      )}

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
              className="bg-slate-800 border border-slate-700 rounded-2xl p-6 w-full max-w-lg max-h-[85vh] overflow-y-auto shadow-2xl"
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
                {/* 协议名称 */}
                <div>
                  <label className="text-sm text-slate-300 mb-1 block">协议名称 *</label>
                  <input
                    value={form.name ?? ''}
                    onChange={e => setForm(f => ({ ...f, name: e.target.value }))}
                    placeholder="请输入协议名称"
                    className="w-full bg-slate-900 border border-slate-600 text-white rounded-lg px-3 py-2 text-sm focus:outline-none focus:border-blue-500"
                  />
                </div>

                {/* 协议类型 */}
                <div>
                  <label className="text-sm text-slate-300 mb-1 block">协议类型 *</label>
                  <div className="grid grid-cols-3 gap-2">
                    {PROTOCOL_OPTIONS.map(opt => (
                      <button
                        key={opt.value}
                        onClick={() => setForm(f => ({
                          ...f,
                          type: opt.value,
                          // 切到安圣类型时补齐默认值（已有值优先，不覆盖用户/后端已填内容）
                          config: opt.value === ANSHENG_MQTT_TYPE
                            ? { ...ANSHENG_DEFAULT_CONFIG, ...f.config, protocolType: opt.value }
                            : { ...f.config, protocolType: opt.value }
                        }))}
                        className={`flex items-center justify-center gap-2 px-3 py-2 rounded-lg border text-sm transition-colors
                          ${form.type === opt.value
                            ? 'border-blue-500 bg-blue-500/20 text-blue-300'
                            : 'border-slate-600 bg-slate-900 text-slate-300 hover:border-slate-500'
                          }`}
                      >
                        <span className="w-6 h-6 rounded bg-slate-700 flex items-center justify-center text-xs">{opt.icon}</span>
                        {opt.label}
                      </button>
                    ))}
                  </div>
                </div>

                {/* 通用连接配置（安圣类型使用下方专属表单，避免小写 host/port 与 PascalCase 键冲突） */}
                {!isAnShengType(form.type) && (
                  <>
                    <div className="grid grid-cols-2 gap-3">
                      <div>
                        <label className="text-sm text-slate-300 mb-1 block">主机地址</label>
                        <input
                          value={getConfigValue(form.config as Record<string, unknown> | undefined, 'host')}
                          onChange={e => setConfigValue('host', e.target.value)}
                          placeholder="192.168.1.100"
                          className="w-full bg-slate-900 border border-slate-600 text-white rounded-lg px-3 py-2 text-sm focus:outline-none focus:border-blue-500"
                        />
                      </div>
                      <div>
                        <label className="text-sm text-slate-300 mb-1 block">端口</label>
                        <input
                          type="number"
                          value={getConfigValue(form.config as Record<string, unknown> | undefined, 'port')}
                          onChange={e => setConfigValue('port', e.target.value)}
                          placeholder="1883"
                          className="w-full bg-slate-900 border border-slate-600 text-white rounded-lg px-3 py-2 text-sm focus:outline-none focus:border-blue-500"
                        />
                      </div>
                    </div>

                    {/* 端点地址 */}
                    <div>
                      <label className="text-sm text-slate-300 mb-1 block">端点地址</label>
                      <input
                        value={getConfigValue(form.config as Record<string, unknown> | undefined, 'endpoint')}
                        onChange={e => setConfigValue('endpoint', e.target.value)}
                        placeholder="opc.tcp://192.168.1.50:4840"
                        className="w-full bg-slate-900 border border-slate-600 text-white rounded-lg px-3 py-2 text-sm focus:outline-none focus:border-blue-500"
                      />
                    </div>
                  </>
                )}

                {/* ── 安圣 MQTT 专属配置 ─────────────────────────────────────
                    键名统一 PascalCase，与后端 AnShengMqttProtocolOptions 属性一一对应，
                    后端反序列化默认大小写敏感，切勿改成小写。 */}
                {isAnShengType(form.type) && (
                  <div className="space-y-3 rounded-lg border border-blue-500/30 bg-blue-500/5 p-3">
                    <div className="flex items-center gap-2 text-sm text-blue-300 font-medium">
                      <Settings className="w-4 h-4" />
                      安圣 MQTT 连接配置
                    </div>

                    {/* Broker 地址 / 端口 */}
                    <div className="grid grid-cols-2 gap-3">
                      <div>
                        <label className="text-sm text-slate-300 mb-1 block">Broker 地址 *</label>
                        <input
                          value={getConfigValue(form.config as Record<string, unknown> | undefined, 'Host')}
                          onChange={e => setConfigValue('Host', e.target.value)}
                          placeholder="120.79.3.248"
                          className="w-full bg-slate-900 border border-slate-600 text-white rounded-lg px-3 py-2 text-sm focus:outline-none focus:border-blue-500"
                        />
                      </div>
                      <div>
                        <label className="text-sm text-slate-300 mb-1 block">Broker 端口 *</label>
                        <input
                          type="number"
                          value={getConfigValue(form.config as Record<string, unknown> | undefined, 'Port')}
                          onChange={e => setNumberConfigValue('Port', e.target.value, 1883)}
                          placeholder="1883"
                          className="w-full bg-slate-900 border border-slate-600 text-white rounded-lg px-3 py-2 text-sm focus:outline-none focus:border-blue-500"
                        />
                      </div>
                    </div>

                    {/* 用户名 / 密码 */}
                    <div className="grid grid-cols-2 gap-3">
                      <div>
                        <label className="text-sm text-slate-300 mb-1 block">用户名</label>
                        <input
                          value={getConfigValue(form.config as Record<string, unknown> | undefined, 'Username')}
                          onChange={e => setConfigValue('Username', e.target.value === '' ? null : e.target.value)}
                          placeholder="admin（留空表示匿名）"
                          autoComplete="off"
                          className="w-full bg-slate-900 border border-slate-600 text-white rounded-lg px-3 py-2 text-sm focus:outline-none focus:border-blue-500"
                        />
                      </div>
                      <div>
                        <label className="text-sm text-slate-300 mb-1 block">密码</label>
                        <input
                          type="password"
                          value={getConfigValue(form.config as Record<string, unknown> | undefined, 'Password')}
                          onChange={e => setConfigValue('Password', e.target.value === '' ? null : e.target.value)}
                          placeholder="留空表示无密码"
                          autoComplete="new-password"
                          className="w-full bg-slate-900 border border-slate-600 text-white rounded-lg px-3 py-2 text-sm focus:outline-none focus:border-blue-500"
                        />
                      </div>
                    </div>

                    {/* 客户端 ID 前缀 */}
                    <div>
                      <label className="text-sm text-slate-300 mb-1 block">客户端 ID 前缀</label>
                      <input
                        value={getConfigValue(form.config as Record<string, unknown> | undefined, 'ClientIdPrefix')}
                        onChange={e => setConfigValue('ClientIdPrefix', e.target.value)}
                        placeholder="iot_platform_ansheng"
                        className="w-full bg-slate-900 border border-slate-600 text-white rounded-lg px-3 py-2 text-sm focus:outline-none focus:border-blue-500"
                      />
                    </div>

                    {/* 上行数据主题（平台订阅，含通配符） */}
                    <div>
                      <label className="text-sm text-slate-300 mb-1 block">上行数据主题（平台订阅）</label>
                      <input
                        value={getConfigValue(form.config as Record<string, unknown> | undefined, 'PublishTopicPattern')}
                        onChange={e => setConfigValue('PublishTopicPattern', e.target.value)}
                        placeholder="/iot/server/iot-board/+"
                        className="w-full bg-slate-900 border border-slate-600 text-white rounded-lg px-3 py-2 text-sm focus:outline-none focus:border-blue-500"
                      />
                      <p className="text-xs text-slate-500 mt-1">设备 publish 的主题通配符，平台据此订阅并从主题中提取 IMEI</p>
                    </div>

                    {/* Will 遗愿主题 */}
                    <div>
                      <label className="text-sm text-slate-300 mb-1 block">Will 遗愿主题</label>
                      <input
                        value={getConfigValue(form.config as Record<string, unknown> | undefined, 'WillTopicPattern')}
                        onChange={e => setConfigValue('WillTopicPattern', e.target.value)}
                        placeholder="/iot/server/iot-board/+"
                        className="w-full bg-slate-900 border border-slate-600 text-white rounded-lg px-3 py-2 text-sm focus:outline-none focus:border-blue-500"
                      />
                      <p className="text-xs text-slate-500 mt-1">安圣二开协议与上行主题相同，相同时适配器只订阅一次，避免重复投递</p>
                    </div>

                    {/* 下行命令主题模板 */}
                    <div>
                      <label className="text-sm text-slate-300 mb-1 block">下行命令主题模板</label>
                      <input
                        value={getConfigValue(form.config as Record<string, unknown> | undefined, 'SubscribeTopicTemplate')}
                        onChange={e => setConfigValue('SubscribeTopicTemplate', e.target.value)}
                        placeholder="/iot/client/iot-board/{imei}"
                        className="w-full bg-slate-900 border border-slate-600 text-white rounded-lg px-3 py-2 text-sm focus:outline-none focus:border-blue-500"
                      />
                      <p className="text-xs text-slate-500 mt-1">平台下发命令的主题模板，必须包含 {'{imei}'} 占位符</p>
                    </div>

                    {/* 高级参数 */}
                    <div className="grid grid-cols-3 gap-3">
                      <div>
                        <label className="text-sm text-slate-300 mb-1 block">QoS 级别</label>
                        <input
                          type="number"
                          min={0}
                          max={2}
                          value={getConfigValue(form.config as Record<string, unknown> | undefined, 'QosLevel')}
                          onChange={e => setNumberConfigValue('QosLevel', e.target.value, 1)}
                          placeholder="1"
                          className="w-full bg-slate-900 border border-slate-600 text-white rounded-lg px-3 py-2 text-sm focus:outline-none focus:border-blue-500"
                        />
                      </div>
                      <div>
                        <label className="text-sm text-slate-300 mb-1 block">连接超时(秒)</label>
                        <input
                          type="number"
                          value={getConfigValue(form.config as Record<string, unknown> | undefined, 'TimeoutSeconds')}
                          onChange={e => setNumberConfigValue('TimeoutSeconds', e.target.value, 30)}
                          placeholder="30"
                          className="w-full bg-slate-900 border border-slate-600 text-white rounded-lg px-3 py-2 text-sm focus:outline-none focus:border-blue-500"
                        />
                      </div>
                      <div>
                        <label className="text-sm text-slate-300 mb-1 block">心跳间隔(秒)</label>
                        <input
                          type="number"
                          value={getConfigValue(form.config as Record<string, unknown> | undefined, 'KeepAliveSeconds')}
                          onChange={e => setNumberConfigValue('KeepAliveSeconds', e.target.value, 60)}
                          placeholder="60"
                          className="w-full bg-slate-900 border border-slate-600 text-white rounded-lg px-3 py-2 text-sm focus:outline-none focus:border-blue-500"
                        />
                      </div>
                    </div>

                    {/* 命令最小间隔 */}
                    <div>
                      <label className="text-sm text-slate-300 mb-1 block">命令最小间隔(毫秒)</label>
                      <input
                        type="number"
                        min={0}
                        value={getConfigValue(form.config as Record<string, unknown> | undefined, 'CommandMinIntervalMs')}
                        onChange={e => setNumberConfigValue('CommandMinIntervalMs', e.target.value, 100)}
                        placeholder="100"
                        className="w-full bg-slate-900 border border-slate-600 text-white rounded-lg px-3 py-2 text-sm focus:outline-none focus:border-blue-500"
                      />
                      <p className="text-xs text-slate-500 mt-1">协议要求同一 IMEI 两次下发间隔 ≥ 100ms，防止命令粘连</p>
                    </div>

                    {/* 开关项 */}
                    <div className="flex items-center gap-6">
                      <div className="flex items-center gap-2">
                        <input
                          id="ansheng-clean-session"
                          type="checkbox"
                          checked={getConfigBool('CleanSession', true)}
                          onChange={e => setConfigValue('CleanSession', e.target.checked)}
                          className="w-4 h-4 accent-blue-500"
                        />
                        <label htmlFor="ansheng-clean-session" className="text-sm text-slate-300">清理会话</label>
                      </div>
                      <div className="flex items-center gap-2">
                        <input
                          id="ansheng-auto-report"
                          type="checkbox"
                          checked={getConfigBool('AutoConfigureAutoReport', true)}
                          onChange={e => setConfigValue('AutoConfigureAutoReport', e.target.checked)}
                          className="w-4 h-4 accent-blue-500"
                        />
                        <label htmlFor="ansheng-auto-report" className="text-sm text-slate-300">上线自动配置上报</label>
                      </div>
                    </div>
                  </div>
                )}

                {/* 描述 */}
                <div>
                  <label className="text-sm text-slate-300 mb-1 block">描述</label>
                  <textarea
                    value={form.description ?? ''}
                    onChange={e => setForm(f => ({ ...f, description: e.target.value }))}
                    placeholder="请输入协议描述"
                    rows={2}
                    className="w-full bg-slate-900 border border-slate-600 text-white rounded-lg px-3 py-2 text-sm focus:outline-none focus:border-blue-500 resize-none"
                  />
                </div>

                {/* 启用状态 */}
                <div className="flex items-center gap-2">
                  <input
                    id="status-toggle"
                    type="checkbox"
                    checked={form.isActive ?? true}
                    onChange={e => setForm(f => ({ ...f, isActive: e.target.checked }))}
                    className="w-4 h-4 accent-blue-500"
                  />
                  <label htmlFor="status-toggle" className="text-sm text-slate-300">启用此协议</label>
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
                  disabled={!form.name || !form.type || saving}
                  className="flex items-center gap-2 px-4 py-2 text-sm bg-blue-600 hover:bg-blue-500 text-white rounded-lg transition-colors disabled:opacity-50"
                >
                  {saving ? (
                    <>
                      <Loader2 className="w-4 h-4 animate-spin" />
                      保存中...
                    </>
                  ) : (
                    <>
                      <Save className="w-4 h-4" />
                      {editTarget ? '保存修改' : '创建协议'}
                    </>
                  )}
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
                  disabled={deleting}
                  className="flex items-center gap-2 px-4 py-2 text-sm bg-red-600 hover:bg-red-500 text-white rounded-lg transition-colors disabled:opacity-50"
                >
                  {deleting && <Loader2 className="w-4 h-4 animate-spin" />}
                  确认删除
                </button>
              </div>
            </motion.div>
          </motion.div>
        )}
      </AnimatePresence>

      {/* ── 设备选择弹窗 ──────────────────────────────────────────────────────── */}
      <AnimatePresence>
        {showDeviceSelector && (
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            className="fixed inset-0 bg-black/60 backdrop-blur-sm z-50 flex items-center justify-center p-4"
            onClick={closeDeviceSelector}
          >
            <motion.div
              initial={{ scale: 0.95, opacity: 0 }}
              animate={{ scale: 1, opacity: 1 }}
              exit={{ scale: 0.95, opacity: 0 }}
              className="bg-slate-800 border border-slate-700 rounded-2xl w-full max-w-2xl max-h-[80vh] flex flex-col shadow-2xl"
              onClick={e => e.stopPropagation()}
            >
              {/* 弹窗头部 */}
              <div className="flex items-center justify-between px-5 py-4 border-b border-slate-700">
                <h2 className="text-lg font-semibold text-white flex items-center gap-2">
                  <Cpu className="w-5 h-5 text-blue-400" />
                  选择设备
                </h2>
                <button onClick={closeDeviceSelector} className="text-slate-400 hover:text-white">
                  <X className="w-5 h-5" />
                </button>
              </div>

              {/* 搜索框 */}
              <div className="px-5 py-3 border-b border-slate-700">
                <div className="relative">
                  <input
                    type="text"
                    placeholder="搜索设备名称..."
                    value={deviceSearchKeyword}
                    onChange={(e) => setDeviceSearchKeyword(e.target.value)}
                    className="w-full bg-slate-900 border border-slate-600 text-white rounded-lg px-4 py-2 pl-10 text-sm focus:outline-none focus:border-blue-500"
                  />
                  <Monitor className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-400" />
                </div>
              </div>

              {/* 设备列表 */}
              <div className="flex-1 overflow-y-auto px-5 py-3">
                {loadingAllDevices ? (
                  <div className="flex items-center justify-center py-12">
                    <Loader2 className="w-6 h-6 text-blue-400 animate-spin" />
                    <span className="ml-2 text-slate-400">加载设备中...</span>
                  </div>
                ) : allDevices.length === 0 ? (
                  <div className="text-center py-12 text-slate-500">
                    <Monitor className="w-10 h-10 mx-auto mb-2 opacity-30" />
                    <p>暂无可用设备</p>
                  </div>
                ) : (
                  <div className="space-y-1">
                    {allDevices
                      .filter(device => {
                        if (!deviceSearchKeyword) return true;
                        const keyword = deviceSearchKeyword.toLowerCase();
                        return (
                          device.name?.toLowerCase().includes(keyword) ||
                          device.serialNumber?.toLowerCase().includes(keyword) ||
                          device.projectName?.toLowerCase().includes(keyword) ||
                          device.areaName?.toLowerCase().includes(keyword)
                        );
                      })
                      .map(device => {
                        const isSelected = selectedDeviceIds.has(device.id);
                        const isAlreadyLinked = selectingProtocolId !== null &&
                          protocols.find(p => p.id === selectingProtocolId)?.deviceIds?.map(id => String(id)).includes(device.id);

                        return (
                          <div
                            key={device.id}
                            onClick={() => !isAlreadyLinked && toggleDeviceSelection(device.id)}
                            className={`flex items-center gap-3 px-3 py-2.5 rounded-lg cursor-pointer transition-colors ${
                              isAlreadyLinked
                                ? 'opacity-50 cursor-not-allowed bg-slate-800/30'
                                : isSelected
                                ? 'bg-blue-500/20 border border-blue-500/40'
                                : 'hover:bg-slate-700/50'
                            }`}
                          >
                            {/* checkbox */}
                            <div className={`w-5 h-5 rounded border-2 flex items-center justify-center flex-shrink-0 transition-colors ${
                              isAlreadyLinked
                                ? 'border-slate-600 bg-slate-700'
                                : isSelected
                                ? 'border-blue-500 bg-blue-500'
                                : 'border-slate-500'
                            }`}>
                              {(isSelected || isAlreadyLinked) && <Check className="w-3 h-3 text-white" />}
                            </div>

                            {/* 设备信息 */}
                            <div className="w-8 h-8 rounded-md bg-slate-700 flex items-center justify-center flex-shrink-0">
                              <Monitor className="w-4 h-4 text-slate-400" />
                            </div>
                            <div className="flex-1 min-w-0">
                              <div className="flex items-center gap-2">
                                <p className="text-sm text-white truncate">{device.name}</p>
                                {isAlreadyLinked && (
                                  <span className="px-1.5 py-0.5 rounded text-xs bg-slate-600/50 text-slate-400">
                                    已关联
                                  </span>
                                )}
                              </div>
                              <div className="flex items-center gap-3 text-xs text-slate-500 mt-0.5">
                                {device.projectName && (
                                  <span className="flex items-center gap-1">
                                    <Folder className="w-3 h-3" />
                                    {device.projectName}
                                  </span>
                                )}
                                {device.areaName && (
                                  <span className="flex items-center gap-1">
                                    <MapPin className="w-3 h-3" />
                                    {device.areaName}
                                  </span>
                                )}
                              </div>
                            </div>

                            {/* 状态 */}
                            <span className={`px-1.5 py-0.5 rounded text-xs flex-shrink-0 ${
                              device.status === 'online'
                                ? 'bg-green-500/20 text-green-400'
                                : device.status === 'warning'
                                ? 'bg-yellow-500/20 text-yellow-400'
                                : 'bg-slate-600/50 text-slate-400'
                            }`}>
                              {device.status === 'online' ? '在线' : device.status === 'warning' ? '警告' : device.status === 'offline' ? '离线' : device.status}
                            </span>
                          </div>
                        );
                      })}
                  </div>
                )}
              </div>

              {/* 底部按钮 */}
              <div className="flex items-center justify-between px-5 py-4 border-t border-slate-700">
                <div className="text-sm text-slate-400">
                  已选择 <span className="text-blue-400 font-medium">{selectedDeviceIds.size}</span> 台设备
                </div>
                <div className="flex items-center gap-3">
                  <button
                    onClick={closeDeviceSelector}
                    className="px-4 py-2 text-sm text-slate-300 hover:text-white bg-slate-700 hover:bg-slate-600 rounded-lg transition-colors"
                  >
                    取消
                  </button>
                  <button
                    onClick={handleAddDevices}
                    disabled={selectedDeviceIds.size === 0 || updatingDevices}
                    className="flex items-center gap-2 px-4 py-2 text-sm bg-blue-600 hover:bg-blue-500 text-white rounded-lg transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
                  >
                    {updatingDevices ? (
                      <>
                        <Loader2 className="w-4 h-4 animate-spin" />
                        添加中...
                      </>
                    ) : (
                      <>
                        <Plus className="w-4 h-4" />
                        确认添加 ({selectedDeviceIds.size})
                      </>
                    )}
                  </button>
                </div>
              </div>
            </motion.div>
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}
