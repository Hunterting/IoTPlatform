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
} from 'lucide-react';
import { useDevices } from '@/app/contexts/DeviceContext';
import type { PageType } from '@/app/components/Sidebar';
import {
  RuleEnginePage,
  DatabaseConfigPage,
  DataExportPage,
} from './DataCollectionSubPages';
import { DataTransformPageNew } from './DataTransformPageNew';
import { DataCenterPageEnhanced } from './DataCenterPageEnhanced';

interface DataCollectionPageProps {
  activePage: PageType;
}

// 协议配置接口
interface ProtocolConfig {
  id: string;
  name: string;
  type: string;
  status: 'running' | 'stopped' | 'error';
  deviceCount: number;
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
  lastSync: string;
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

// 规则接口
interface DataRule {
  id: string;
  name: string;
  description: string;
  type: 'threshold' | 'anomaly' | 'pattern';
  enabled: boolean;
  condition: string;
  action: string;
}

// ETL任务接口
interface ETLTask {
  id: string;
  name: string;
  source: string;
  target: string;
  status: 'running' | 'stopped' | 'failed';
  lastRun: string;
  recordsProcessed: number;
}

// 数据库配置接口
interface DatabaseConfig {
  id: string;
  name: string;
  type: 'MySQL' | 'TDengine' | 'InfluxDB';
  status: 'connected' | 'disconnected';
  host: string;
  port: number;
  database: string;
  username: string;
}

export function DataCollectionPage({ activePage }: DataCollectionPageProps) {
  const { devices } = useDevices();

  // 从设备列表中筛选支持数据采集的设备
  const collectionDevices = devices.filter(d =>
    d.energyType === '电' || d.energyType === '水' || d.energyType === '气'
  );

  const getEnergyIcon = (type: '电' | '水' | '气') => {
    switch (type) {
      case '电': return <Zap className="w-4 h-4 text-yellow-500" />;
      case '水': return <Droplet className="w-4 h-4 text-blue-500" />;
      case '气': return <Flame className="w-4 h-4 text-orange-500" />;
    }
  };

  // 根据 activePage 渲染对应的独立页面
  switch (activePage) {
    case 'data-collection-protocol':
      return <ProtocolConfigPage devices={collectionDevices} />;
    case 'data-collection-gateway':
      return <ProtocolGatewayPage />;
    case 'data-collection-tunnel':
      return <NetworkTunnelPage />;
    case 'data-collection-plugin':
      return <PluginSystemPage />;
    case 'data-collection-center':
      return <DataCenterPageEnhanced devices={collectionDevices} getEnergyIcon={getEnergyIcon} />;
    case 'data-collection-rules':
      return <RuleEnginePage />;
    case 'data-collection-transform':
      return <DataTransformPageNew />;
    case 'data-collection-database':
      return <DatabaseConfigPage />;
    case 'data-collection-export':
      return <DataExportPage devices={collectionDevices} getEnergyIcon={getEnergyIcon} />;
    default:
      return <ProtocolConfigPage devices={collectionDevices} />;
  }
}

// ========== 1. 协议配置 ==========
function ProtocolConfigPage({ devices }: { devices: any[] }) {
  const [protocols, setProtocols] = useState<ProtocolConfig[]>([
    {
      id: 'proto-001',
      name: 'Modbus TCP主站',
      type: 'Modbus TCP',
      status: 'running',
      deviceCount: 8,
      host: '192.168.1.100',
      port: 502,
      lastSync: '2026-03-23 14:30:25',
    },
    {
      id: 'proto-002',
      name: 'OPC UA服务器',
      type: 'OPC UA',
      status: 'running',
      deviceCount: 6,
      endpoint: 'opc.tcp://192.168.1.200:4840',
      lastSync: '2026-03-23 14:30:20',
    },
    {
      id: 'proto-003',
      name: 'MQTT能源采集',
      type: 'MQTT',
      status: 'running',
      deviceCount: 12,
      host: 'mqtt.example.com',
      port: 1883,
      topic: 'devices/energy/#',
      clientId: 'energy_collector_001',
      username: 'admin',
      qos: 1,
      useSsl: false,
      keepalive: 60,
      lastSync: '2026-03-23 14:30:15',
    },
  ]);

  const [showModal, setShowModal] = useState(false);
  const [editingProtocol, setEditingProtocol] = useState<ProtocolConfig | null>(null);
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
  });

  const handleAdd = () => {
    setEditingProtocol(null);
    setFormData({ name: '', type: 'Modbus TCP', host: '', port: 502, endpoint: '', topic: '', clientId: '', username: '', password: '', qos: 0, useSsl: false, keepalive: 60 });
    setShowModal(true);
  };

  const handleEdit = (protocol: ProtocolConfig) => {
    setEditingProtocol(protocol);
    setFormData({
      name: protocol.name,
      type: protocol.type,
      host: protocol.host || '',
      port: protocol.port || 502,
      endpoint: protocol.endpoint || '',
      topic: protocol.topic || '',
      clientId: protocol.clientId || '',
      username: protocol.username || '',
      password: protocol.password || '',
      qos: protocol.qos || 0,
      useSsl: protocol.useSsl || false,
      keepalive: protocol.keepalive || 60,
    });
    setShowModal(true);
  };

  const handleSave = () => {
    if (editingProtocol) {
      setProtocols(protocols.map(p =>
        p.id === editingProtocol.id
          ? { ...p, ...formData, lastSync: new Date().toLocaleString() }
          : p
      ));
    } else {
      const newProtocol: ProtocolConfig = {
        id: `proto-${Date.now()}`,
        ...formData,
        status: 'stopped',
        deviceCount: 0,
        lastSync: new Date().toLocaleString(),
      };
      setProtocols([...protocols, newProtocol]);
    }
    setShowModal(false);
  };

  const handleToggle = (id: string) => {
    setProtocols(protocols.map(p =>
      p.id === id ? { ...p, status: p.status === 'running' ? 'stopped' : 'running' } : p
    ));
  };

  const handleDelete = (id: string) => {
    if (confirm('确定要删除此协议配置吗？')) {
      setProtocols(protocols.filter(p => p.id !== id));
    }
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
        </div>
        <button
          onClick={handleAdd}
          className="px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded-lg flex items-center gap-2 transition-colors"
        >
          <Plus className="w-4 h-4" />
          <span>添加协议</span>
        </button>
      </div>

      {/* 协议列表 */}
      <div className="space-y-4">
        {protocols.map((protocol) => (
          <div key={protocol.id} className="bg-gray-800 border border-gray-700 rounded-lg p-4">
            <div className="flex items-start justify-between">
              <div className="flex-1">
                <div className="flex items-center gap-3 mb-2">
                  {protocol.status === 'running' ? (
                    <Wifi className="w-5 h-5 text-green-500" />
                  ) : (
                    <WifiOff className="w-5 h-5 text-gray-500" />
                  )}
                  <h3 className="text-lg font-semibold text-white">{protocol.name}</h3>
                  <span className={`px-2 py-1 rounded text-xs font-medium ${
                    protocol.status === 'running'
                      ? 'bg-green-500/20 text-green-300'
                      : 'bg-gray-500/20 text-gray-300'
                  }`}>
                    {protocol.status === 'running' ? '运行中' : '已停止'}
                  </span>
                </div>

                <div className="grid grid-cols-2 md:grid-cols-4 gap-4 text-sm">
                  <div>
                    <span className="text-gray-400">协议类型：</span>
                    <span className="text-white ml-1">{protocol.type}</span>
                  </div>
                  <div>
                    <span className="text-gray-400">接入设备：</span>
                    <span className="text-white ml-1">{protocol.deviceCount} 台</span>
                  </div>
                  <div>
                    <span className="text-gray-400">最后同步：</span>
                    <span className="text-white ml-1">{protocol.lastSync}</span>
                  </div>
                  <div>
                    <span className="text-gray-400">配置：</span>
                    <span className="text-white ml-1">
                      {protocol.host && `${protocol.host}:${protocol.port}`}
                      {protocol.endpoint && protocol.endpoint}
                    </span>
                  </div>
                </div>
              </div>

              <div className="flex items-center gap-2 ml-4">
                <button
                  onClick={() => handleToggle(protocol.id)}
                  className={`p-2 rounded-lg transition-colors ${
                    protocol.status === 'running'
                      ? 'bg-orange-500/20 hover:bg-orange-500/30 text-orange-300'
                      : 'bg-green-500/20 hover:bg-green-500/30 text-green-300'
                  }`}
                  title={protocol.status === 'running' ? '停止' : '启动'}
                >
                  {protocol.status === 'running' ? <Pause className="w-4 h-4" /> : <Play className="w-4 h-4" />}
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
          </div>
        ))}
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
              className="bg-gray-800 border border-gray-700 rounded-xl max-w-md w-full p-6"
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
                )}

                {formData.type === 'MQTT' && (
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
                )}

                {formData.type === 'MQTT' && (
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
                )}

                {formData.type === 'MQTT' && (
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
                )}

                {formData.type === 'MQTT' && (
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
                )}

                {formData.type === 'MQTT' && (
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
                )}

                {formData.type === 'MQTT' && (
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
      lastUpdate: '2026-03-23 14:30:25',
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
                    {gateway.status === 'online' ? '在线' : '离���'}
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
      name: '工内网隧道',
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
                  <X className="w-5 h-5 text-gray-400" />
                </button>
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

// ========== 5. 数据中心 ==========
export function DataCenterPage({ devices, getEnergyIcon }: { devices: any[], getEnergyIcon: (type: any) => JSX.Element }) {
  const [selectedDevice, setSelectedDevice] = React.useState<any>(null);
  const [dataPoints] = React.useState(
    devices.map((device) => ({
      id: `dp-${device.id}`,
      deviceId: device.id,
      deviceName: device.name,
      energyType: device.energyType,
      value: Math.random() * 100 + 50,
      unit: device.energyType === '电' ? 'kWh' : device.energyType === '水' ? 'm³' : 'm³',
      timestamp: new Date().toISOString(),
      quality: 'good' as const,
    }))
  );

  return (
    <div className="p-6 space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-white flex items-center gap-2">
          <Database className="w-7 h-7 text-blue-500" />
          数据中心
        </h1>
        <p className="text-sm text-gray-400 mt-1">
          构建自定义数据模型、第三方 API 集成、数据库直连、数据管理
        </p>
      </div>

      <div className="flex items-center gap-4">
        <div className="flex-1 relative">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-400" />
          <input
            type="text"
            placeholder="搜索设备或数据点..."
            className="w-full pl-10 pr-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white placeholder-gray-500 focus:outline-none focus:border-blue-500"
          />
        </div>
        <button className="px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded-lg flex items-center gap-2 transition-colors">
          <Activity className="w-4 h-4" />
          <span>实时监控</span>
        </button>
      </div>

      <div className="bg-gray-800 border border-gray-700 rounded-lg overflow-hidden">
        <div className="overflow-x-auto">
          <table className="w-full">
            <thead className="bg-gray-900/50">
              <tr className="border-b border-gray-700">
                <th className="text-left py-3 px-4 text-sm font-medium text-gray-400">设备名称</th>
                <th className="text-left py-3 px-4 text-sm font-medium text-gray-400">能源类型</th>
                <th className="text-left py-3 px-4 text-sm font-medium text-gray-400">当前值</th>
                <th className="text-left py-3 px-4 text-sm font-medium text-gray-400">单位</th>
                <th className="text-left py-3 px-4 text-sm font-medium text-gray-400">时间戳</th>
                <th className="text-left py-3 px-4 text-sm font-medium text-gray-400">质量</th>
                <th className="text-left py-3 px-4 text-sm font-medium text-gray-400">操作</th>
              </tr>
            </thead>
            <tbody>
              {dataPoints.map((point) => (
                <tr key={point.id} className="border-b border-gray-800 hover:bg-gray-700/30">
                  <td className="py-3 px-4 text-white">{point.deviceName}</td>
                  <td className="py-3 px-4">
                    <div className="flex items-center gap-2">
                      {getEnergyIcon(point.energyType)}
                      <span className="text-white">{point.energyType}</span>
                    </div>
                  </td>
                  <td className="py-3 px-4 text-white font-mono">{point.value.toFixed(2)}</td>
                  <td className="py-3 px-4 text-gray-400">{point.unit}</td>
                  <td className="py-3 px-4 text-gray-400">{new Date(point.timestamp).toLocaleString()}</td>
                  <td className="py-3 px-4">
                    <span className="px-2 py-1 rounded text-xs font-medium bg-green-500/20 text-green-300">
                      良好
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
              ))}
            </tbody>
          </table>
        </div>
      </div>

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