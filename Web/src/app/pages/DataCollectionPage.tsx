import React from 'react';
import { useState, useEffect } from 'react';
import { AnimatePresence, motion } from 'motion/react';
import {
  Tag,
  Plus,
  Wifi,
  WifiOff,
  Play,
  Pause,
  Edit,
  Trash2,
  X,
  Globe,
  Server,
  Shield,
  Link,
  Upload,
  Code,
  RotateCw,
  Activity,
  Search,
  Eye,
  Zap,
  Droplet,
  Flame,
  Database,
  Key,
  Settings,
  Users,
  CheckCircle,
} from 'lucide-react';
import { useDevices } from '@/app/contexts/DeviceContext';
import type { PageType } from '@/app/components/Sidebar';
import {
  RuleEnginePage as RuleEnginePageApi,
  DatabaseConfigPage as DatabaseConfigPageApi,
} from './DataCollectionApiSubPages';
import { DataExportPage } from './DataCollectionSubPages';
import {
  ProtocolGatewayPage as ProtocolGatewayPageApi,
  NetworkTunnelPage as NetworkTunnelPageApi,
  PluginSystemPage as PluginSystemPageApi,
} from './DataCollectionApiPages';
import { DataTransformPageNew } from './DataTransformPageNew';
import { DataCenterPageEnhanced } from './DataCenterPageEnhanced';
import {
  protocolApi,
  ProtocolConfig,
  CreateProtocolConfigRequest,
  UpdateProtocolConfigRequest,
} from '@/app/services/api/protocolApi';

interface DataCollectionPageProps {
  activePage: PageType;
}

// 协议配置接口
interface ProtocolConfig {
  id: number;
  name: string;
  type: string;
  status: string;
  isActive?: boolean;
  deviceIds?: number[]; // 关联的设备ID列表
  config?: Record<string, unknown>;
  description?: string;
  appCode?: string;
  createdAt?: string | Date;
  updatedAt?: string | Date;
  lastSync?: string;
  host?: string;
  port?: number;
  endpoint?: string;
  topic?: string;
  clientId?: string;
  username?: string;
  password?: string;
  qos?: 0 | 1 | 2;
  useSsl?: boolean;
  keepalive?: number;
}

// 网关配置接口
interface Gateway {
  id: string;
  name: string;
  type: string;
  status: 'online' | 'offline';
  sourceProtocol: string;
  targetProtocol: string;
  throughput: number;
  lastUpdate: string;
}

// 隧道配置接口
interface Tunnel {
  id: string;
  name: string;
  type: 'P2P' | 'Proxy' | 'VPN';
  status: 'connected' | 'disconnected';
  localPort: number;
  remotePort: number;
  remoteHost: string;
  encryption: boolean;
  bandwidth: string;
}

// 插件接口
interface Plugin {
  id: string;
  name: string;
  version: string;
  status: 'running' | 'stopped';
  description: string;
  author: string;
  installDate: string;
}

export function DataCollectionPage({ activePage }: DataCollectionPageProps) {
  const { devices } = useDevices();

  // 从设备列表中筛选支持数据采集的设备
  const collectionDevices = devices.filter(d =>
    d.meterInstalled && d.energyType && d.energyType.length > 0
  );

  const getEnergyIcon = (types: ('electric' | 'gas' | 'water')[]) => {
    if (!types || types.length === 0) return null;
    const type = types[0];
    switch (type) {
      case 'electric': return <Zap className="w-4 h-4 text-yellow-500" />;
      case 'water': return <Droplet className="w-4 h-4 text-blue-500" />;
      case 'gas': return <Flame className="w-4 h-4 text-orange-500" />;
    }
  };

  // 根据 activePage 渲染对应的独立页面
  switch (activePage) {
    case 'data-collection-protocol':
      return <ProtocolConfigPage devices={collectionDevices} />;
    case 'data-collection-gateway':
      return <ProtocolGatewayPageApi />;
    case 'data-collection-tunnel':
      return <NetworkTunnelPageApi />;
    case 'data-collection-plugin':
      return <PluginSystemPageApi />;
    case 'data-collection-center':
      return <DataCenterPageEnhanced devices={collectionDevices} getEnergyIcon={getEnergyIcon} />;
    case 'data-collection-rules':
      return <RuleEnginePageApi />;
    case 'data-collection-transform':
      return <DataTransformPageNew />;
    case 'data-collection-database':
      return <DatabaseConfigPageApi />;
    case 'data-collection-export':
      return <DataExportPage devices={collectionDevices} getEnergyIcon={getEnergyIcon} />;
    default:
      return <ProtocolConfigPage devices={collectionDevices} />;
  }
}

// ========== 1. 协议配置 ==========
function ProtocolConfigPage({ devices }: { devices: any[] }) {
  const [loading, setLoading] = useState(false);
  const [protocols, setProtocols] = useState<ProtocolConfig[]>([]);
  const [showModal, setShowModal] = useState(false);
  const [editingProtocol, setEditingProtocol] = useState<ProtocolConfig | null>(null);
  const [showDeviceModal, setShowDeviceModal] = useState(false);
  const [managingProtocol, setManagingProtocol] = useState<ProtocolConfig | null>(null);
  const [formData, setFormData] = useState({
    name: '',
    type: 'Modbus TCP',
    host: '',
    port: 502,
    endpoint: '',
    topic: '',
    clientId: '',
    username: '',
    password: '',
    qos: 0,
    useSsl: false,
    keepalive: 60,
    description: '',
  });

  // 加载协议列表
  const loadProtocols = async () => {
    try {
      setLoading(true);
      const response = await protocolApi.getProtocolConfigs({ pageSize: 100 });
      if (response.code === 200 && response.data) {
        setProtocols(response.data.items);
      }
    } catch (error) {
      console.error('加载协议列表失败:', error);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadProtocols();
  }, []);

  // 格式化日期
  const formatDate = (dateStr: string | Date | undefined) => {
    if (!dateStr) return '-';
    try {
      const date = new Date(dateStr);
      if (isNaN(date.getTime())) return String(dateStr);
      return date.toLocaleString('zh-CN');
    } catch {
      return String(dateStr);
    }
  };

  // 从config中提取配置信息
  const getConfigValue = (config: Record<string, unknown> | undefined, key: string): string => {
    if (!config) return '';
    return (config[key] as string) || '';
  };

  const getConfigNumber = (config: Record<string, unknown> | undefined, key: string, defaultValue: number): number => {
    if (!config) return defaultValue;
    const val = config[key];
    return typeof val === 'number' ? val : defaultValue;
  };

  const getConfigBool = (config: Record<string, unknown> | undefined, key: string, defaultValue: boolean): boolean => {
    if (!config) return defaultValue;
    const val = config[key];
    return typeof val === 'boolean' ? val : defaultValue;
  };

  // 构建config对象
  const buildConfig = () => {
    const config: Record<string, unknown> = {};
    if (formData.host) config.host = formData.host;
    if (formData.port) config.port = formData.port;
    if (formData.endpoint) config.endpoint = formData.endpoint;
    if (formData.topic) config.topic = formData.topic;
    if (formData.clientId) config.clientId = formData.clientId;
    if (formData.username) config.username = formData.username;
    if (formData.password) config.password = formData.password;
    if (formData.qos !== undefined) config.qos = formData.qos;
    config.useSsl = formData.useSsl;
    if (formData.keepalive) config.keepalive = formData.keepalive;
    return config;
  };

  const handleAdd = () => {
    setEditingProtocol(null);
    setFormData({ name: '', type: 'Modbus TCP', host: '', port: 502, endpoint: '', topic: '', clientId: '', username: '', password: '', qos: 0, useSsl: false, keepalive: 60, description: '' });
    setShowModal(true);
  };

  const handleEdit = (protocol: ProtocolConfig) => {
    setEditingProtocol(protocol);
    const config = protocol.config || {};
    setFormData({
      name: protocol.name,
      type: protocol.type,
      host: getConfigValue(config, 'host'),
      port: getConfigNumber(config, 'port', 502),
      endpoint: getConfigValue(config, 'endpoint'),
      topic: getConfigValue(config, 'topic'),
      clientId: getConfigValue(config, 'clientId'),
      username: getConfigValue(config, 'username'),
      password: getConfigValue(config, 'password'),
      qos: getConfigNumber(config, 'qos', 0),
      useSsl: getConfigBool(config, 'useSsl', false),
      keepalive: getConfigNumber(config, 'keepalive', 60),
      description: protocol.description || '',
    });
    setShowModal(true);
  };

  const handleSave = async () => {
    try {
      if (editingProtocol) {
        // 更新
        const request: UpdateProtocolConfigRequest = {
          name: formData.name,
          description: formData.description,
          config: buildConfig(),
          deviceIds: editingProtocol.deviceIds,
        };
        await protocolApi.updateProtocolConfig(editingProtocol.id, request);
      } else {
        // 创建
        const request: CreateProtocolConfigRequest = {
          name: formData.name,
          type: formData.type,
          description: formData.description,
          config: buildConfig(),
          deviceIds: [],
          isActive: true,
        };
        await protocolApi.createProtocolConfig(request);
      }
      setShowModal(false);
      loadProtocols();
    } catch (error) {
      console.error('保存协议失败:', error);
      alert('保存失败，请重试');
    }
  };

  const handleToggle = async (id: number) => {
    const protocol = protocols.find(p => p.id === id);
    if (!protocol) return;
    try {
      // 根据 isActive 或 status 判断运行状态
      const isRunning = protocol.isActive || protocol.status === 'running';
      if (isRunning) {
        await protocolApi.stopProtocol(id);
      } else {
        await protocolApi.startProtocol(id);
      }
      loadProtocols();
    } catch (error) {
      console.error('操作失败:', error);
      alert('操作失败，请重试');
    }
  };

  const handleDelete = async (id: number) => {
    if (!confirm('确定要删除此协议配置吗？')) return;
    try {
      await protocolApi.deleteProtocolConfig(id);
      loadProtocols();
    } catch (error) {
      console.error('删除失败:', error);
      alert('删除失败，请重试');
    }
  };

  const handleManageDevices = (protocol: ProtocolConfig) => {
    setManagingProtocol(protocol);
    setShowDeviceModal(true);
  };

  const handleAddDevice = async (deviceId: number) => {
    if (!managingProtocol) return;
    const newDeviceIds = [...(managingProtocol.deviceIds || []), deviceId];
    try {
      await protocolApi.updateProtocolConfig(managingProtocol.id, {
        name: managingProtocol.name,
        description: managingProtocol.description,
        deviceIds: newDeviceIds,
        config: managingProtocol.config,
        isActive: managingProtocol.isActive,
      });
      setManagingProtocol({ ...managingProtocol, deviceIds: newDeviceIds });
      setProtocols(protocols.map(p =>
        p.id === managingProtocol.id ? { ...p, deviceIds: newDeviceIds } : p
      ));
    } catch (error) {
      console.error('添加设备失败:', error);
      alert('添加失败，请重试');
    }
  };

  const handleRemoveDevice = async (deviceId: number) => {
    if (!managingProtocol) return;
    const newDeviceIds = (managingProtocol.deviceIds || []).filter(id => id !== deviceId);
    try {
      await protocolApi.updateProtocolConfig(managingProtocol.id, {
        name: managingProtocol.name,
        description: managingProtocol.description,
        deviceIds: newDeviceIds,
        config: managingProtocol.config,
        isActive: managingProtocol.isActive,
      });
      setManagingProtocol({ ...managingProtocol, deviceIds: newDeviceIds });
      setProtocols(protocols.map(p =>
        p.id === managingProtocol.id ? { ...p, deviceIds: newDeviceIds } : p
      ));
    } catch (error) {
      console.error('移除设备失败:', error);
      alert('移除失败，请重试');
    }
  };

  // 获取协议关联的设备列表
  const getProtocolDevices = (protocol: ProtocolConfig) => {
    const deviceIds = (protocol.deviceIds || []).map(id => String(id));
    return devices.filter(d => deviceIds.includes(d.id));
  };

  // 获取未关联的设备列表
  const getAvailableDevices = (protocol: ProtocolConfig) => {
    const deviceIds = (protocol.deviceIds || []).map(id => String(id));
    return devices.filter(d => !deviceIds.includes(d.id));
  };

  return (
    <div className="p-6 space-y-6">
      {/* 页面标题 */}
      <div>
        <h1 className="text-2xl font-bold text-white flex items-center gap-2">
          <Tag className="w-7 h-7 text-blue-500" />
          协议配置
        </h1>
        <p className="text-sm text-gray-400 mt-1">
          配置工业协议，支持 Modbus TCP/RTU/ASCII、OPC UA、MQTT、HTTP、WebSocket、CoAP 等
        </p>
      </div>

      {/* 操作栏 */}
      <div className="flex justify-between items-center">
        <div className="text-sm text-gray-400">
          已配置 {protocols.length} 个协议，接入设备 {devices.length} 台
          {loading && <span className="ml-2 text-blue-400">(加载中...)</span>}
        </div>
        <button
          onClick={handleAdd}
          className="px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded-lg flex items-center gap-2 transition-colors"
          disabled={loading}
        >
          <Plus className="w-4 h-4" />
          <span>添加协议</span>
        </button>
      </div>

      {/* 加载状态 */}
      {loading && protocols.length === 0 && (
        <div className="flex justify-center items-center py-12">
          <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-blue-500"></div>
          <span className="ml-3 text-gray-400">加载协议配置中...</span>
        </div>
      )}

      {/* 空状态 */}
      {!loading && protocols.length === 0 && (
        <div className="text-center py-12 bg-gray-800/50 rounded-lg border border-gray-700">
          <Tag className="w-12 h-12 mx-auto text-gray-500 mb-3" />
          <p className="text-gray-400">暂无协议配置</p>
          <p className="text-sm text-gray-500 mt-1">点击上方按钮添加第一个协议配置</p>
        </div>
      )}

      {/* 协议列表 */}
      <div className="space-y-4">
        {protocols.map((protocol) => {
          const config = protocol.config || {};
          const host = getConfigValue(config, 'host');
          const port = getConfigNumber(config, 'port', 0);
          const endpoint = getConfigValue(config, 'endpoint');
          // 根据 isActive 或 status 判断运行状态
          const isRunning = protocol.isActive || protocol.status === 'running';
          const deviceIds = (protocol.deviceIds || []).map(id => String(id));
          const protocolDevices = devices.filter(d => deviceIds.includes(d.id));
          return (
            <div key={protocol.id} className="bg-gray-800 border border-gray-700 rounded-lg p-4">
              <div className="flex items-start justify-between mb-3">
                <div className="flex-1">
                  <div className="flex items-center gap-3 mb-2">
                    {isRunning ? (
                      <Wifi className="w-5 h-5 text-green-500" />
                    ) : (
                      <WifiOff className="w-5 h-5 text-gray-500" />
                    )}
                    <h3 className="text-lg font-semibold text-white">{protocol.name}</h3>
                    <span className={`px-2 py-1 rounded text-xs font-medium ${
                      isRunning
                        ? 'bg-green-500/20 text-green-300'
                        : 'bg-gray-500/20 text-gray-300'
                    }`}>
                      {isRunning ? '运行中' : '已停止'}
                    </span>
                  </div>

                  <div className="grid grid-cols-2 md:grid-cols-4 gap-4 text-sm">
                    <div>
                      <span className="text-gray-400">协议类型：</span>
                      <span className="text-white ml-1">{protocol.type}</span>
                    </div>
                    <div>
                      <span className="text-gray-400">接入设备：</span>
                      <span className="text-white ml-1 font-semibold">{deviceIds.length} 台</span>
                    </div>
                    <div>
                      <span className="text-gray-400">更新时间：</span>
                      <span className="text-white ml-1">{formatDate(protocol.updatedAt)}</span>
                    </div>
                    <div>
                      <span className="text-gray-400">配置：</span>
                      <span className="text-white ml-1">
                        {host && port && `${host}:${port}`}
                        {endpoint && endpoint}
                      </span>
                    </div>
                  </div>
                </div>

                <div className="flex items-center gap-2 ml-4">
                  <button
                    onClick={() => handleManageDevices(protocol)}
                    className="p-2 bg-purple-500/20 hover:bg-purple-500/30 text-purple-300 rounded-lg transition-colors"
                    title="管理设备"
                  >
                    <Users className="w-4 h-4" />
                  </button>
                  <button
                    onClick={() => handleToggle(protocol.id)}
                    className={`p-2 rounded-lg transition-colors ${
                      isRunning
                        ? 'bg-orange-500/20 hover:bg-orange-500/30 text-orange-300'
                        : 'bg-green-500/20 hover:bg-green-500/30 text-green-300'
                    }`}
                    title={isRunning ? '停止' : '启动'}
                  >
                    {isRunning ? <Pause className="w-4 h-4" /> : <Play className="w-4 h-4" />}
                  </button>
                  <button
                    onClick={() => handleEdit(protocol)}
                    className="p-2 bg-blue-500/20 hover:bg-blue-500/30 text-blue-300 rounded-lg transition-colors"
                    title="编辑"
                  >
                    <Edit className="w-4 h-4" />
                  </button>
                  <button
                    onClick={() => handleDelete(protocol.id)}
                    className="p-2 bg-red-500/20 hover:bg-red-500/30 text-red-300 rounded-lg transition-colors"
                    title="删除"
                  >
                    <Trash2 className="w-4 h-4" />
                  </button>
                </div>
              </div>

              {/* 显示关联的设备 */}
              {protocolDevices.length > 0 && (
                <div className="mt-3 pt-3 border-t border-gray-700">
                  <div className="flex items-center gap-2 mb-2">
                    <Database className="w-4 h-4 text-blue-400" />
                    <span className="text-sm font-medium text-gray-400">关联设备：</span>
                  </div>
                  <div className="flex flex-wrap gap-2">
                    {protocolDevices.map((device) => (
                      <div
                        key={device.id}
                        className="px-3 py-1.5 bg-blue-500/10 border border-blue-500/30 rounded-lg text-sm text-blue-300 flex items-center gap-2"
                      >
                        {device.energyType && device.energyType.length > 0 && (
                          <>
                            {device.energyType[0] === 'electric' && <Zap className="w-3 h-3" />}
                            {device.energyType[0] === 'water' && <Droplet className="w-3 h-3" />}
                            {device.energyType[0] === 'gas' && <Flame className="w-3 h-3" />}
                          </>
                        )}
                        <span>{device.name}</span>
                        <span className="text-blue-400/60">({device.serialNumber})</span>
                      </div>
                    ))}
                  </div>
                </div>
              )}
            </div>
          );
        })}
      </div>

      {/* 添加/编辑协议弹窗 */}
      <AnimatePresence>
        {showModal && (
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            className="fixed inset-0 bg-black/60 backdrop-blur-sm flex items-center justify-center z-50 p-4"
            onClick={() => setShowModal(false)}
          >
            <motion.div
              initial={{ scale: 0.9, y: 20 }}
              animate={{ scale: 1, y: 0 }}
              exit={{ scale: 0.9, y: 20 }}
              onClick={(e) => e.stopPropagation()}
              className="bg-gray-800 border border-gray-700 rounded-xl max-w-md w-full p-6 max-h-[90vh] overflow-y-auto"
            >
              <div className="flex items-center justify-between mb-6">
                <h3 className="text-xl font-bold text-white">
                  {editingProtocol ? '编辑协议' : '添加协议配置'}
                </h3>
                <button
                  onClick={() => setShowModal(false)}
                  className="p-2 hover:bg-gray-700 rounded-lg transition-colors"
                >
                  <X className="w-5 h-5 text-gray-400" />
                </button>
              </div>

              <div className="space-y-4">
                <div>
                  <label className="block text-sm font-medium text-gray-400 mb-2">协议名称</label>
                  <input
                    type="text"
                    value={formData.name}
                    onChange={(e) => setFormData({ ...formData, name: e.target.value })}
                    placeholder="例如：Modbus主站"
                    className="w-full px-4 py-2 bg-gray-900 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-blue-500"
                  />
                </div>

                <div>
                  <label className="block text-sm font-medium text-gray-400 mb-2">协议类型</label>
                  <select
                    value={formData.type}
                    onChange={(e) => setFormData({ ...formData, type: e.target.value })}
                    className="w-full px-4 py-2 bg-gray-900 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-blue-500"
                  >
                    <option>Modbus TCP</option>
                    <option>Modbus RTU</option>
                    <option>OPC UA</option>
                    <option>MQTT</option>
                    <option>HTTP</option>
                    <option>WebSocket</option>
                    <option>CoAP</option>
                  </select>
                </div>

                {['Modbus TCP', 'MQTT', 'HTTP', 'WebSocket'].includes(formData.type) && (
                  <>
                    <div>
                      <label className="block text-sm font-medium text-gray-400 mb-2">主机地址</label>
                      <input
                        type="text"
                        value={formData.host}
                        onChange={(e) => setFormData({ ...formData, host: e.target.value })}
                        placeholder="192.168.1.100"
                        className="w-full px-4 py-2 bg-gray-900 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-blue-500"
                      />
                    </div>

                    <div>
                      <label className="block text-sm font-medium text-gray-400 mb-2">端口号</label>
                      <input
                        type="number"
                        value={formData.port}
                        onChange={(e) => setFormData({ ...formData, port: parseInt(e.target.value) })}
                        placeholder="502"
                        className="w-full px-4 py-2 bg-gray-900 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-blue-500"
                      />
                    </div>
                  </>
                )}

                {formData.type === 'OPC UA' && (
                  <div>
                    <label className="block text-sm font-medium text-gray-400 mb-2">端点URL</label>
                    <input
                      type="text"
                      value={formData.endpoint}
                      onChange={(e) => setFormData({ ...formData, endpoint: e.target.value })}
                      placeholder="opc.tcp://192.168.1.200:4840"
                      className="w-full px-4 py-2 bg-gray-900 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-blue-500"
                    />
                  </div>
                )}

                {formData.type === 'MQTT' && (
                  <>
                    <div>
                      <label className="block text-sm font-medium text-gray-400 mb-2">主题</label>
                      <input
                        type="text"
                        value={formData.topic}
                        onChange={(e) => setFormData({ ...formData, topic: e.target.value })}
                        placeholder="devices/energy/#"
                        className="w-full px-4 py-2 bg-gray-900 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-blue-500"
                      />
                    </div>

                    <div>
                      <label className="block text-sm font-medium text-gray-400 mb-2">客户端ID</label>
                      <input
                        type="text"
                        value={formData.clientId}
                        onChange={(e) => setFormData({ ...formData, clientId: e.target.value })}
                        placeholder="client123"
                        className="w-full px-4 py-2 bg-gray-900 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-blue-500"
                      />
                    </div>

                    <div>
                      <label className="block text-sm font-medium text-gray-400 mb-2">用户名</label>
                      <input
                        type="text"
                        value={formData.username}
                        onChange={(e) => setFormData({ ...formData, username: e.target.value })}
                        placeholder="user"
                        className="w-full px-4 py-2 bg-gray-900 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-blue-500"
                      />
                    </div>

                    <div>
                      <label className="block text-sm font-medium text-gray-400 mb-2">密码</label>
                      <input
                        type="password"
                        value={formData.password}
                        onChange={(e) => setFormData({ ...formData, password: e.target.value })}
                        placeholder="password"
                        className="w-full px-4 py-2 bg-gray-900 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-blue-500"
                      />
                    </div>

                    <div>
                      <label className="block text-sm font-medium text-gray-400 mb-2">QoS级别</label>
                      <select
                        value={formData.qos}
                        onChange={(e) => setFormData({ ...formData, qos: parseInt(e.target.value) as 0 | 1 | 2 })}
                        className="w-full px-4 py-2 bg-gray-900 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-blue-500"
                      >
                        <option value="0">0</option>
                        <option value="1">1</option>
                        <option value="2">2</option>
                      </select>
                    </div>

                    <div>
                      <label className="flex items-center gap-2 text-white cursor-pointer">
                        <input
                          type="checkbox"
                          checked={formData.useSsl}
                          onChange={(e) => setFormData({ ...formData, useSsl: e.target.checked })}
                          className="rounded"
                        />
                        <span>启用SSL/TLS加密</span>
                      </label>
                    </div>

                    <div>
                      <label className="block text-sm font-medium text-gray-400 mb-2">心跳间隔（秒）</label>
                      <input
                        type="number"
                        value={formData.keepalive}
                        onChange={(e) => setFormData({ ...formData, keepalive: parseInt(e.target.value) })}
                        placeholder="60"
                        className="w-full px-4 py-2 bg-gray-900 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-blue-500"
                      />
                    </div>
                  </>
                )}

                <div className="flex gap-3 pt-4">
                  <button
                    onClick={() => setShowModal(false)}
                    className="flex-1 px-6 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded-lg transition-colors"
                  >
                    取消
                  </button>
                  <button
                    onClick={handleSave}
                    className="flex-1 px-6 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded-lg transition-colors"
                  >
                    {editingProtocol ? '保存' : '添加'}
                  </button>
                </div>
              </div>
            </motion.div>
          </motion.div>
        )}
      </AnimatePresence>

      {/* 设备管理弹窗 */}
      <AnimatePresence>
        {showDeviceModal && managingProtocol && (
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            className="fixed inset-0 bg-black/60 backdrop-blur-sm flex items-center justify-center z-50 p-4"
            onClick={() => setShowDeviceModal(false)}
          >
            <motion.div
              initial={{ scale: 0.9, y: 20 }}
              animate={{ scale: 1, y: 0 }}
              exit={{ scale: 0.9, y: 20 }}
              onClick={(e) => e.stopPropagation()}
              className="bg-gray-800 border border-gray-700 rounded-xl max-w-4xl w-full p-6 max-h-[90vh] overflow-y-auto"
            >
              <div className="flex items-center justify-between mb-6">
                <div>
                  <h3 className="text-xl font-bold text-white flex items-center gap-2">
                    <Users className="w-6 h-6 text-purple-500" />
                    管理设备关联
                  </h3>
                  <p className="text-sm text-gray-400 mt-1">
                    协议：{managingProtocol.name} - 已关联 {managingProtocol.deviceIds.length} 台设备
                  </p>
                </div>
                <button
                  onClick={() => setShowDeviceModal(false)}
                  className="p-2 hover:bg-gray-700 rounded-lg transition-colors"
                >
                  <X className="w-5 h-5 text-gray-400" />
                </button>
              </div>

              <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
                {/* 已关联设备 */}
                <div>
                  <h4 className="text-lg font-semibold text-white mb-3 flex items-center gap-2">
                    <CheckCircle className="w-5 h-5 text-green-500" />
                    已关联设备 ({managingProtocol.deviceIds.length})
                  </h4>
                  <div className="space-y-2 max-h-[500px] overflow-y-auto">
                    {managingProtocol.deviceIds.length === 0 ? (
                      <div className="text-center py-12 text-gray-500">
                        <Database className="w-12 h-12 mx-auto mb-3 opacity-50" />
                        <p>暂无关联设备</p>
                        <p className="text-sm mt-1">从右侧添加设备</p>
                      </div>
                    ) : (
                      getProtocolDevices(managingProtocol).map((device) => (
                        <div
                          key={device.id}
                          className="bg-gray-900 border border-gray-700 rounded-lg p-3 flex items-center justify-between hover:bg-gray-700/30 transition-colors"
                        >
                          <div className="flex items-center gap-3 flex-1">
                            {device.energyType && device.energyType.length > 0 && (
                              <>
                                {device.energyType[0] === 'electric' && <Zap className="w-5 h-5 text-yellow-500" />}
                                {device.energyType[0] === 'water' && <Droplet className="w-5 h-5 text-blue-500" />}
                                {device.energyType[0] === 'gas' && <Flame className="w-5 h-5 text-orange-500" />}
                              </>
                            )}
                            <div className="flex-1">
                              <p className="text-white font-medium">{device.name}</p>
                              <p className="text-xs text-gray-400">{device.serialNumber} · {device.location}</p>
                            </div>
                          </div>
                          <button
                            onClick={() => handleRemoveDevice(device.id as number)}
                            className="p-2 bg-red-500/20 hover:bg-red-500/30 text-red-300 rounded-lg transition-colors"
                            title="移除"
                          >
                            <X className="w-4 h-4" />
                          </button>
                        </div>
                      ))
                    )}
                  </div>
                </div>

                {/* 可用设备 */}
                <div>
                  <h4 className="text-lg font-semibold text-white mb-3 flex items-center gap-2">
                    <Database className="w-5 h-5 text-blue-500" />
                    可用设备 ({getAvailableDevices(managingProtocol).length})
                  </h4>
                  <div className="space-y-2 max-h-[500px] overflow-y-auto">
                    {getAvailableDevices(managingProtocol).length === 0 ? (
                      <div className="text-center py-12 text-gray-500">
                        <Database className="w-12 h-12 mx-auto mb-3 opacity-50" />
                        <p>暂无可用设备</p>
                        <p className="text-sm mt-1">所有设备已关联</p>
                      </div>
                    ) : (
                      getAvailableDevices(managingProtocol).map((device) => (
                        <div
                          key={device.id}
                          className="bg-gray-900 border border-gray-700 rounded-lg p-3 flex items-center justify-between hover:bg-gray-700/30 transition-colors"
                        >
                          <div className="flex items-center gap-3 flex-1">
                            {device.energyType && device.energyType.length > 0 && (
                              <>
                                {device.energyType[0] === 'electric' && <Zap className="w-5 h-5 text-yellow-500" />}
                                {device.energyType[0] === 'water' && <Droplet className="w-5 h-5 text-blue-500" />}
                                {device.energyType[0] === 'gas' && <Flame className="w-5 h-5 text-orange-500" />}
                              </>
                            )}
                            <div className="flex-1">
                              <p className="text-white font-medium">{device.name}</p>
                              <p className="text-xs text-gray-400">{device.serialNumber} · {device.location}</p>
                            </div>
                          </div>
                          <button
                            onClick={() => handleAddDevice(device.id as number)}
                            className="p-2 bg-green-500/20 hover:bg-green-500/30 text-green-300 rounded-lg transition-colors"
                            title="添加"
                          >
                            <Plus className="w-4 h-4" />
                          </button>
                        </div>
                      ))
                    )}
                  </div>
                </div>
              </div>

              <div className="mt-6 pt-6 border-t border-gray-700">
                <button
                  onClick={() => setShowDeviceModal(false)}
                  className="w-full px-6 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded-lg transition-colors"
                >
                  完成
                </button>
              </div>
            </motion.div>
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}

// ========== 2. 协议网关 ==========
function ProtocolGatewayPage() {
  const [gateways, setGateways] = useState<Gateway[]>([
    {
      id: 'gw-001',
      name: 'Modbus到MQTT网关',
      type: '协议转换',
      status: 'online',
      sourceProtocol: 'Modbus TCP',
      targetProtocol: 'MQTT',
      throughput: 1250,
      lastUpdate: '2026-03-24 14:30:25',
    },
  ]);

  const [showModal, setShowModal] = useState(false);
  const [formData, setFormData] = useState({
    name: '',
    sourceProtocol: 'Modbus TCP',
    targetProtocol: 'MQTT',
  });

  const handleAdd = () => {
    const newGateway: Gateway = {
      id: `gw-${Date.now()}`,
      ...formData,
      type: '协议转换',
      status: 'offline',
      throughput: 0,
      lastUpdate: new Date().toLocaleString(),
    };
    setGateways([...gateways, newGateway]);
    setShowModal(false);
    setFormData({ name: '', sourceProtocol: 'Modbus TCP', targetProtocol: 'MQTT' });
  };

  const handleDelete = (id: string) => {
    if (confirm('确定要删除此网关吗？')) {
      setGateways(gateways.filter(g => g.id !== id));
    }
  };

  return (
    <div className="p-6 space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-white flex items-center gap-2">
          <Globe className="w-7 h-7 text-blue-500" />
          协议网关
        </h1>
        <p className="text-sm text-gray-400 mt-1">
          协议转换和网关管理，支持异构设备接入，提供二次开发SDK
        </p>
      </div>

      <div className="flex justify-between items-center">
        <div className="bg-blue-500/10 border border-blue-500/30 rounded-lg p-4 flex-1 mr-4">
          <div className="flex items-start gap-3">
            <Globe className="w-5 h-5 text-blue-400 flex-shrink-0 mt-0.5" />
            <div className="text-sm text-blue-300">
              <p className="font-medium mb-2">协议网关功能</p>
              <ul className="space-y-1 text-blue-300/80 text-xs">
                <li>• 协议转换：支持异构设备间协议转换</li>
                <li>• 网关管理：统一管理多个协议网关节点</li>
                <li>• SDK支持：提供二次开发SDK快速接入新协议</li>
                <li>• 负载均衡：多网关节点自动负载均衡</li>
              </ul>
            </div>
          </div>
        </div>
        <button
          onClick={() => setShowModal(true)}
          className="px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded-lg flex items-center gap-2 transition-colors"
        >
          <Plus className="w-4 h-4" />
          <span>添加网关</span>
        </button>
      </div>

      <div className="space-y-4">
        {gateways.map((gateway) => (
          <div key={gateway.id} className="bg-gray-800 border border-gray-700 rounded-lg p-4">
            <div className="flex items-start justify-between">
              <div className="flex-1">
                <div className="flex items-center gap-3 mb-2">
                  <Server className="w-5 h-5 text-blue-500" />
                  <h3 className="text-lg font-semibold text-white">{gateway.name}</h3>
                  <span className={`px-2 py-1 rounded text-xs font-medium ${
                    gateway.status === 'online'
                      ? 'bg-green-500/20 text-green-300'
                      : 'bg-gray-500/20 text-gray-300'
                  }`}>
                    {gateway.status === 'online' ? '在线' : '离线'}
                  </span>
                </div>

                <div className="grid grid-cols-2 md:grid-cols-4 gap-4 text-sm">
                  <div>
                    <span className="text-gray-400">源协议：</span>
                    <span className="text-white ml-1">{gateway.sourceProtocol}</span>
                  </div>
                  <div>
                    <span className="text-gray-400">目标协议：</span>
                    <span className="text-white ml-1">{gateway.targetProtocol}</span>
                  </div>
                  <div>
                    <span className="text-gray-400">吞吐量：</span>
                    <span className="text-white ml-1">{gateway.throughput} msg/s</span>
                  </div>
                  <div>
                    <span className="text-gray-400">最后更新：</span>
                    <span className="text-white ml-1">{gateway.lastUpdate}</span>
                  </div>
                </div>
              </div>

              <div className="flex items-center gap-2 ml-4">
                <button className="p-2 bg-blue-500/20 hover:bg-blue-500/30 text-blue-300 rounded-lg transition-colors">
                  <Settings className="w-4 h-4" />
                </button>
                <button
                  onClick={() => handleDelete(gateway.id)}
                  className="p-2 bg-red-500/20 hover:bg-red-500/30 text-red-300 rounded-lg transition-colors"
                >
                  <Trash2 className="w-4 h-4" />
                </button>
              </div>
            </div>
          </div>
        ))}
      </div>

      {/* 添加网关弹窗 */}
      <AnimatePresence>
        {showModal && (
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            className="fixed inset-0 bg-black/60 backdrop-blur-sm flex items-center justify-center z-50 p-4"
            onClick={() => setShowModal(false)}
          >
            <motion.div
              initial={{ scale: 0.9, y: 20 }}
              animate={{ scale: 1, y: 0 }}
              exit={{ scale: 0.9, y: 20 }}
              onClick={(e) => e.stopPropagation()}
              className="bg-gray-800 border border-gray-700 rounded-xl max-w-md w-full p-6"
            >
              <div className="flex items-center justify-between mb-6">
                <h3 className="text-xl font-bold text-white">添加协议网关</h3>
                <button onClick={() => setShowModal(false)} className="p-2 hover:bg-gray-700 rounded-lg transition-colors">
                  <X className="w-5 h-5 text-gray-400" />
                </button>
              </div>

              <div className="space-y-4">
                <div>
                  <label className="block text-sm font-medium text-gray-400 mb-2">网关名称</label>
                  <input
                    type="text"
                    value={formData.name}
                    onChange={(e) => setFormData({ ...formData, name: e.target.value })}
                    placeholder="例如：Modbus到MQTT网关"
                    className="w-full px-4 py-2 bg-gray-900 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-blue-500"
                  />
                </div>

                <div>
                  <label className="block text-sm font-medium text-gray-400 mb-2">源协议</label>
                  <select
                    value={formData.sourceProtocol}
                    onChange={(e) => setFormData({ ...formData, sourceProtocol: e.target.value })}
                    className="w-full px-4 py-2 bg-gray-900 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-blue-500"
                  >
                    <option>Modbus TCP</option>
                    <option>OPC UA</option>
                    <option>MQTT</option>
                    <option>HTTP</option>
                  </select>
                </div>

                <div>
                  <label className="block text-sm font-medium text-gray-400 mb-2">目标协议</label>
                  <select
                    value={formData.targetProtocol}
                    onChange={(e) => setFormData({ ...formData, targetProtocol: e.target.value })}
                    className="w-full px-4 py-2 bg-gray-900 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-blue-500"
                  >
                    <option>MQTT</option>
                    <option>HTTP</option>
                    <option>WebSocket</option>
                    <option>Kafka</option>
                  </select>
                </div>

                <div className="flex gap-3 pt-4">
                  <button
                    onClick={() => setShowModal(false)}
                    className="flex-1 px-6 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded-lg transition-colors"
                  >
                    取消
                  </button>
                  <button
                    onClick={handleAdd}
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
    </div>
  );
}

// ========== 3. 网络隧道 ==========
function NetworkTunnelPage() {
  const [tunnels, setTunnels] = useState<Tunnel[]>([
    {
      id: 'tun-001',
      name: '工厂内网隧道',
      type: 'P2P',
      status: 'connected',
      localPort: 8080,
      remotePort: 80,
      remoteHost: 'factory.example.com',
      encryption: true,
      bandwidth: '10 Mbps',
    },
  ]);

  const [showModal, setShowModal] = useState(false);
  const [formData, setFormData] = useState({
    name: '',
    type: 'P2P' as 'P2P' | 'Proxy' | 'VPN',
    localPort: 8080,
    remotePort: 80,
    remoteHost: '',
    encryption: true,
  });

  const handleAdd = () => {
    const newTunnel: Tunnel = {
      id: `tun-${Date.now()}`,
      ...formData,
      status: 'disconnected',
      bandwidth: '0 Mbps',
    };
    setTunnels([...tunnels, newTunnel]);
    setShowModal(false);
    setFormData({ name: '', type: 'P2P', localPort: 8080, remotePort: 80, remoteHost: '', encryption: true });
  };

  const handleToggle = (id: string) => {
    setTunnels(tunnels.map(t =>
      t.id === id ? { ...t, status: t.status === 'connected' ? 'disconnected' : 'connected' } : t
    ));
  };

  const handleDelete = (id: string) => {
    if (confirm('确定要删除此隧道吗？')) {
      setTunnels(tunnels.filter(t => t.id !== id));
    }
  };

  return (
    <div className="p-6 space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-white flex items-center gap-2">
          <Zap className="w-7 h-7 text-purple-500" />
          网络隧道
        </h1>
        <p className="text-sm text-gray-400 mt-1">
          内网穿透和安全加密，支持 P2P 连接和代理转发
        </p>
      </div>

      <div className="flex justify-between items-center">
        <div className="bg-purple-500/10 border border-purple-500/30 rounded-lg p-4 flex-1 mr-4">
          <div className="flex items-start gap-3">
            <Shield className="w-5 h-5 text-purple-400 flex-shrink-0 mt-0.5" />
            <div className="text-sm text-purple-300">
              <p className="font-medium mb-2">网络隧道功能</p>
              <ul className="space-y-1 text-purple-300/80 text-xs">
                <li>• 内网穿透：无需公网IP即可远程访问设备</li>
                <li>• 安全加密：SSL/TLS加密通道保障数据安全</li>
                <li>• P2P连接：支持点对点直连降低延迟</li>
                <li>• 代理转发：智能路由选择最优传输路径</li>
              </ul>
            </div>
          </div>
        </div>
        <button
          onClick={() => setShowModal(true)}
          className="px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded-lg flex items-center gap-2 transition-colors"
        >
          <Plus className="w-4 h-4" />
          <span>添加隧道</span>
        </button>
      </div>

      <div className="space-y-4">
        {tunnels.map((tunnel) => (
          <div key={tunnel.id} className="bg-gray-800 border border-gray-700 rounded-lg p-4">
            <div className="flex items-start justify-between">
              <div className="flex-1">
                <div className="flex items-center gap-3 mb-2">
                  <Shield className={`w-5 h-5 ${tunnel.status === 'connected' ? 'text-green-500' : 'text-gray-500'}`} />
                  <h3 className="text-lg font-semibold text-white">{tunnel.name}</h3>
                  <span className={`px-2 py-1 rounded text-xs font-medium ${
                    tunnel.status === 'connected'
                      ? 'bg-green-500/20 text-green-300'
                      : 'bg-gray-500/20 text-gray-300'
                  }`}>
                    {tunnel.status === 'connected' ? '已连接' : '未连接'}
                  </span>
                  {tunnel.encryption && (
                    <span className="px-2 py-1 rounded text-xs font-medium bg-blue-500/20 text-blue-300">
                      已加密
                    </span>
                  )}
                </div>

                <div className="grid grid-cols-2 md:grid-cols-4 gap-4 text-sm">
                  <div>
                    <span className="text-gray-400">类型：</span>
                    <span className="text-white ml-1">{tunnel.type}</span>
                  </div>
                  <div>
                    <span className="text-gray-400">端口映射：</span>
                    <span className="text-white ml-1">{tunnel.localPort} → {tunnel.remotePort}</span>
                  </div>
                  <div>
                    <span className="text-gray-400">远程主机：</span>
                    <span className="text-white ml-1">{tunnel.remoteHost}</span>
                  </div>
                  <div>
                    <span className="text-gray-400">带宽：</span>
                    <span className="text-white ml-1">{tunnel.bandwidth}</span>
                  </div>
                </div>
              </div>

              <div className="flex items-center gap-2 ml-4">
                <button
                  onClick={() => handleToggle(tunnel.id)}
                  className={`p-2 rounded-lg transition-colors ${
                    tunnel.status === 'connected'
                      ? 'bg-orange-500/20 hover:bg-orange-500/30 text-orange-300'
                      : 'bg-green-500/20 hover:bg-green-500/30 text-green-300'
                  }`}
                >
                  <Link className="w-4 h-4" />
                </button>
                <button
                  onClick={() => handleDelete(tunnel.id)}
                  className="p-2 bg-red-500/20 hover:bg-red-500/30 text-red-300 rounded-lg transition-colors"
                >
                  <Trash2 className="w-4 h-4" />
                </button>
              </div>
            </div>
          </div>
        ))}
      </div>

      {/* 添加隧道弹窗 */}
      <AnimatePresence>
        {showModal && (
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            className="fixed inset-0 bg-black/60 backdrop-blur-sm flex items-center justify-center z-50 p-4"
            onClick={() => setShowModal(false)}
          >
            <motion.div
              initial={{ scale: 0.9, y: 20 }}
              animate={{ scale: 1, y: 0 }}
              exit={{ scale: 0.9, y: 20 }}
              onClick={(e) => e.stopPropagation()}
              className="bg-gray-800 border border-gray-700 rounded-xl max-w-md w-full p-6"
            >
              <div className="flex items-center justify-between mb-6">
                <h3 className="text-xl font-bold text-white">添加网络隧道</h3>
                <button onClick={() => setShowModal(false)} className="p-2 hover:bg-gray-700 rounded-lg transition-colors">
                  <X className="w-5 h-5 text-gray-400" />
                </button>
              </div>

              <div className="space-y-4">
                <div>
                  <label className="block text-sm font-medium text-gray-400 mb-2">隧道名称</label>
                  <input
                    type="text"
                    value={formData.name}
                    onChange={(e) => setFormData({ ...formData, name: e.target.value })}
                    placeholder="例如：工厂内网隧道"
                    className="w-full px-4 py-2 bg-gray-900 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-blue-500"
                  />
                </div>

                <div>
                  <label className="block text-sm font-medium text-gray-400 mb-2">隧道类型</label>
                  <select
                    value={formData.type}
                    onChange={(e) => setFormData({ ...formData, type: e.target.value as any })}
                    className="w-full px-4 py-2 bg-gray-900 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-blue-500"
                  >
                    <option>P2P</option>
                    <option>Proxy</option>
                    <option>VPN</option>
                  </select>
                </div>

                <div className="grid grid-cols-2 gap-4">
                  <div>
                    <label className="block text-sm font-medium text-gray-400 mb-2">本地端口</label>
                    <input
                      type="number"
                      value={formData.localPort}
                      onChange={(e) => setFormData({ ...formData, localPort: parseInt(e.target.value) })}
                      className="w-full px-4 py-2 bg-gray-900 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-blue-500"
                    />
                  </div>
                  <div>
                    <label className="block text-sm font-medium text-gray-400 mb-2">远程端口</label>
                    <input
                      type="number"
                      value={formData.remotePort}
                      onChange={(e) => setFormData({ ...formData, remotePort: parseInt(e.target.value) })}
                      className="w-full px-4 py-2 bg-gray-900 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-blue-500"
                    />
                  </div>
                </div>

                <div>
                  <label className="block text-sm font-medium text-gray-400 mb-2">远程主机</label>
                  <input
                    type="text"
                    value={formData.remoteHost}
                    onChange={(e) => setFormData({ ...formData, remoteHost: e.target.value })}
                    placeholder="factory.example.com"
                    className="w-full px-4 py-2 bg-gray-900 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-blue-500"
                  />
                </div>

                <div>
                  <label className="flex items-center gap-2 text-white cursor-pointer">
                    <input
                      type="checkbox"
                      checked={formData.encryption}
                      onChange={(e) => setFormData({ ...formData, encryption: e.target.checked })}
                      className="rounded"
                    />
                    <span>启用SSL/TLS加密</span>
                  </label>
                </div>

                <div className="flex gap-3 pt-4">
                  <button
                    onClick={() => setShowModal(false)}
                    className="flex-1 px-6 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded-lg transition-colors"
                  >
                    取消
                  </button>
                  <button
                    onClick={handleAdd}
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
    </div>
  );
}

// ========== 4. 插件系统 ==========
function PluginSystemPage() {
  const [plugins, setPlugins] = useState<Plugin[]>([
    {
      id: 'plugin-001',
      name: 'Modbus解析插件',
      version: '1.2.0',
      status: 'running',
      description: '支持Modbus TCP/RTU/ASCII协议解析',
      author: 'System',
      installDate: '2026-01-15',
    },
    {
      id: 'plugin-002',
      name: 'OPC UA客户端',
      version: '2.0.1',
      status: 'running',
      description: 'OPC UA协议客户端插件',
      author: 'System',
      installDate: '2026-02-10',
    },
  ]);

  const [showUpload, setShowUpload] = useState(false);

  const handleToggle = (id: string) => {
    setPlugins(plugins.map(p =>
      p.id === id ? { ...p, status: p.status === 'running' ? 'stopped' : 'running' } : p
    ));
  };

  const handleDelete = (id: string) => {
    if (confirm('确定要卸载此插件吗？')) {
      setPlugins(plugins.filter(p => p.id !== id));
    }
  };

  return (
    <div className="p-6 space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-white flex items-center gap-2">
          <Key className="w-7 h-7 text-green-500" />
          插件系统
        </h1>
        <p className="text-sm text-gray-400 mt-1">
          跨进程通信（gRPC）确保隔离性，插件生命周期管理和热更新
        </p>
      </div>

      <div className="flex justify-between items-center">
        <div className="bg-green-500/10 border border-green-500/30 rounded-lg p-4 flex-1 mr-4">
          <div className="flex items-start gap-3">
            <Key className="w-5 h-5 text-green-400 flex-shrink-0 mt-0.5" />
            <div className="text-sm text-green-300">
              <p className="font-medium mb-2">插件系统功能</p>
              <ul className="space-y-1 text-green-300/80 text-xs">
                <li>• 跨进程通信：gRPC确保插件隔离性</li>
                <li>• 生命周期管理：插件启动、停止、重启管理</li>
                <li>• 热更新：无需重启主程序即可更新插件</li>
                <li>• 版本控制：插件版本管理和回滚</li>
              </ul>
            </div>
          </div>
        </div>
        <button
          onClick={() => setShowUpload(true)}
          className="px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded-lg flex items-center gap-2 transition-colors"
        >
          <Upload className="w-4 h-4" />
          <span>上传插件</span>
        </button>
      </div>

      <div className="space-y-4">
        {plugins.map((plugin) => (
          <div key={plugin.id} className="bg-gray-800 border border-gray-700 rounded-lg p-4">
            <div className="flex items-start justify-between">
              <div className="flex-1">
                <div className="flex items-center gap-3 mb-2">
                  <Code className="w-5 h-5 text-green-500" />
                  <h3 className="text-lg font-semibold text-white">{plugin.name}</h3>
                  <span className="px-2 py-1 rounded text-xs font-medium bg-blue-500/20 text-blue-300">
                    v{plugin.version}
                  </span>
                  <span className={`px-2 py-1 rounded text-xs font-medium ${
                    plugin.status === 'running'
                      ? 'bg-green-500/20 text-green-300'
                      : 'bg-gray-500/20 text-gray-300'
                  }`}>
                    {plugin.status === 'running' ? '运行中' : '已停止'}
                  </span>
                </div>

                <p className="text-gray-400 text-sm mb-2">{plugin.description}</p>

                <div className="grid grid-cols-2 gap-4 text-sm">
                  <div>
                    <span className="text-gray-400">作者：</span>
                    <span className="text-white ml-1">{plugin.author}</span>
                  </div>
                  <div>
                    <span className="text-gray-400">安装日期：</span>
                    <span className="text-white ml-1">{plugin.installDate}</span>
                  </div>
                </div>
              </div>

              <div className="flex items-center gap-2 ml-4">
                <button
                  onClick={() => handleToggle(plugin.id)}
                  className={`p-2 rounded-lg transition-colors ${
                    plugin.status === 'running'
                      ? 'bg-orange-500/20 hover:bg-orange-500/30 text-orange-300'
                      : 'bg-green-500/20 hover:bg-green-500/30 text-green-300'
                  }`}
                >
                  {plugin.status === 'running' ? <Pause className="w-4 h-4" /> : <Play className="w-4 h-4" />}
                </button>
                <button className="p-2 bg-blue-500/20 hover:bg-blue-500/30 text-blue-300 rounded-lg transition-colors">
                  <RotateCw className="w-4 h-4" />
                </button>
                <button
                  onClick={() => handleDelete(plugin.id)}
                  className="p-2 bg-red-500/20 hover:bg-red-500/30 text-red-300 rounded-lg transition-colors"
                >
                  <Trash2 className="w-4 h-4" />
                </button>
              </div>
            </div>
          </div>
        ))}
      </div>

      {/* 上传插件弹窗 */}
      <AnimatePresence>
        {showUpload && (
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            className="fixed inset-0 bg-black/60 backdrop-blur-sm flex items-center justify-center z-50 p-4"
            onClick={() => setShowUpload(false)}
          >
            <motion.div
              initial={{ scale: 0.9, y: 20 }}
              animate={{ scale: 1, y: 0 }}
              exit={{ scale: 0.9, y: 20 }}
              onClick={(e) => e.stopPropagation()}
              className="bg-gray-800 border border-gray-700 rounded-xl max-w-md w-full p-6"
            >
              <div className="flex items-center justify-between mb-6">
                <h3 className="text-xl font-bold text-white">上传插件</h3>
                <button onClick={() => setShowUpload(false)} className="p-2 hover:bg-gray-700 rounded-lg transition-colors">
                  <X className="w-5 h-5 text-gray-400" />\n                </button>
              </div>

              <div className="space-y-4">
                <div className="border-2 border-dashed border-gray-600 rounded-lg p-8 text-center hover:border-blue-500 transition-colors cursor-pointer">
                  <Upload className="w-12 h-12 text-gray-500 mx-auto mb-3" />
                  <p className="text-white mb-1">点击或拖拽文件到此处</p>
                  <p className="text-sm text-gray-400">支持 .jar, .dll, .so 等插件文件</p>
                </div>

                <div className="bg-blue-500/10 border border-blue-500/30 rounded-lg p-3">
                  <p className="text-sm text-blue-300">
                    插件将通过gRPC与主程序通信，确保隔离性和安全性
                  </p>
                </div>

                <div className="flex gap-3">
                  <button
                    onClick={() => setShowUpload(false)}
                    className="flex-1 px-6 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded-lg transition-colors"
                  >
                    取消
                  </button>
                  <button
                    onClick={() => {
                      alert('插件上传功能开发中...');
                      setShowUpload(false);
                    }}
                    className="flex-1 px-6 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded-lg transition-colors"
                  >
                    上传
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
