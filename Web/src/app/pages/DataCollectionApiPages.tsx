import { useState, useEffect, useCallback } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import {
  Globe,
  Server,
  Shield,
  Link,
  Upload,
  Code,
  RotateCw,
  X,
  Plus,
  Play,
  Pause,
  Edit,
  Trash2,
  Settings,
  Key,
  CheckCircle,
} from 'lucide-react';
import { gatewayApi, type Gateway, type CreateGatewayRequest } from '@/app/services/api/gatewayApi';
import { tunnelApi, type Tunnel, type CreateTunnelRequest } from '@/app/services/api/tunnelApi';
import { pluginApi, type Plugin, type CreatePluginRequest } from '@/app/services/api/pluginApi';
import { databaseConfigApi, type DatabaseConfig, type CreateDatabaseConfigRequest } from '@/app/services/api/databaseConfigApi';
import { dataRuleApi, type DataRule, type CreateDataRuleRequest } from '@/app/services/api/dataRuleApi';
import { etlTaskApi, type ETLTask, type CreateETLTaskRequest } from '@/app/services/api/etlTaskApi';

// ========== 2. 协议网关 (真实API) ==========
export function ProtocolGatewayPage() {
  const [gateways, setGateways] = useState<Gateway[]>([]);
  const [loading, setLoading] = useState(true);
  const [showModal, setShowModal] = useState(false);
  const [editingGateway, setEditingGateway] = useState<Gateway | null>(null);
  const [formData, setFormData] = useState({
    name: '',
    sourceProtocol: 'Modbus TCP',
    targetProtocol: 'MQTT',
    gatewayType: 'protocol_conversion',
    description: '',
  });

  const fetchGateways = useCallback(async () => {
    try {
      setLoading(true);
      const res = await gatewayApi.getGateways({ pageSize: 100 });
      if (res.success && res.data) {
        setGateways(res.data.items);
      }
    } catch (err) {
      console.error('获取网关列表失败:', err);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchGateways();
  }, [fetchGateways]);

  const handleAdd = () => {
    setEditingGateway(null);
    setFormData({ name: '', sourceProtocol: 'Modbus TCP', targetProtocol: 'MQTT', gatewayType: 'protocol_conversion', description: '' });
    setShowModal(true);
  };

  const handleEdit = (gateway: Gateway) => {
    setEditingGateway(gateway);
    setFormData({
      name: gateway.name,
      sourceProtocol: gateway.sourceProtocol,
      targetProtocol: gateway.targetProtocol,
      gatewayType: gateway.gatewayType || 'protocol_conversion',
      description: gateway.description || '',
    });
    setShowModal(true);
  };

  const handleSave = async () => {
    try {
      if (editingGateway) {
        await gatewayApi.updateGateway(editingGateway.id, {
          name: formData.name,
          sourceProtocol: formData.sourceProtocol,
          targetProtocol: formData.targetProtocol,
          gatewayType: formData.gatewayType,
          description: formData.description,
        });
      } else {
        await gatewayApi.createGateway(formData as CreateGatewayRequest);
      }
      setShowModal(false);
      fetchGateways();
    } catch (err) {
      console.error('保存网关失败:', err);
    }
  };

  const handleDelete = async (id: number) => {
    if (confirm('确定要删除此网关吗？')) {
      try {
        await gatewayApi.deleteGateway(id);
        fetchGateways();
      } catch (err) {
        console.error('删除网关失败:', err);
      }
    }
  };

  const handleToggle = async (gateway: Gateway) => {
    try {
      if (gateway.status === 'online') {
        await gatewayApi.stopGateway(gateway.id);
      } else {
        await gatewayApi.startGateway(gateway.id);
      }
      fetchGateways();
    } catch (err) {
      console.error('切换网关状态失败:', err);
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
          协议转换和网关管理，支持异构设备接入
        </p>
      </div>

      <div className="flex justify-between items-center">
        <p className="text-sm text-gray-400">
          已配置 {gateways.length} 个网关
        </p>
        <button
          onClick={handleAdd}
          className="px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded-lg flex items-center gap-2 transition-colors"
        >
          <Plus className="w-4 h-4" />
          <span>添加网关</span>
        </button>
      </div>

      {loading ? (
        <div className="text-center py-12 text-gray-400">加载中...</div>
      ) : gateways.length === 0 ? (
        <div className="text-center py-12 text-gray-500">
          暂无网关配置
        </div>
      ) : (
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
                      <span className="text-white ml-1">
                        {gateway.updatedAt ? new Date(gateway.updatedAt).toLocaleString() : '-'}
                      </span>
                    </div>
                  </div>
                </div>

                <div className="flex items-center gap-2 ml-4">
                  <button
                    onClick={() => handleToggle(gateway)}
                    className={`p-2 rounded-lg transition-colors ${
                      gateway.status === 'online'
                        ? 'bg-orange-500/20 hover:bg-orange-500/30 text-orange-300'
                        : 'bg-green-500/20 hover:bg-green-500/30 text-green-300'
                    }`}
                    title={gateway.status === 'online' ? '停止' : '启动'}
                  >
                    {gateway.status === 'online' ? <Pause className="w-4 h-4" /> : <Play className="w-4 h-4" />}
                  </button>
                  <button
                    onClick={() => handleEdit(gateway)}
                    className="p-2 bg-blue-500/20 hover:bg-blue-500/30 text-blue-300 rounded-lg transition-colors"
                  >
                    <Edit className="w-4 h-4" />
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
      )}

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
                  {editingGateway ? '编辑网关' : '添加协议网关'}
                </h3>
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
                    onClick={handleSave}
                    className="flex-1 px-6 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded-lg transition-colors"
                  >
                    {editingGateway ? '保存' : '添加'}
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

// ========== 3. 网络隧道 (真实API) ==========
export function NetworkTunnelPage() {
  const [tunnels, setTunnels] = useState<Tunnel[]>([]);
  const [loading, setLoading] = useState(true);
  const [showModal, setShowModal] = useState(false);
  const [formData, setFormData] = useState({
    name: '',
    tunnelType: 'P2P' as 'P2P' | 'Proxy' | 'VPN',
    localPort: 8080,
    remotePort: 80,
    remoteHost: '',
    encryption: true,
    description: '',
  });

  const fetchTunnels = useCallback(async () => {
    try {
      setLoading(true);
      const res = await tunnelApi.getTunnels({ pageSize: 100 });
      if (res.success && res.data) {
        setTunnels(res.data.items);
      }
    } catch (err) {
      console.error('获取隧道列表失败:', err);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchTunnels();
  }, [fetchTunnels]);

  const handleAdd = () => {
    setFormData({ name: '', tunnelType: 'P2P', localPort: 8080, remotePort: 80, remoteHost: '', encryption: true, description: '' });
    setShowModal(true);
  };

  const handleSave = async () => {
    try {
      await tunnelApi.createTunnel(formData as CreateTunnelRequest);
      setShowModal(false);
      fetchTunnels();
    } catch (err) {
      console.error('创建隧道失败:', err);
    }
  };

  const handleDelete = async (id: number) => {
    if (confirm('确定要删除此隧道吗？')) {
      try {
        await tunnelApi.deleteTunnel(id);
        fetchTunnels();
      } catch (err) {
        console.error('删除隧道失败:', err);
      }
    }
  };

  const handleToggle = async (tunnel: Tunnel) => {
    try {
      if (tunnel.status === 'connected') {
        await tunnelApi.disconnectTunnel(tunnel.id);
      } else {
        await tunnelApi.connectTunnel(tunnel.id);
      }
      fetchTunnels();
    } catch (err) {
      console.error('切换隧道状态失败:', err);
    }
  };

  return (
    <div className="p-6 space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-white flex items-center gap-2">
          <Shield className="w-7 h-7 text-purple-500" />
          网络隧道
        </h1>
        <p className="text-sm text-gray-400 mt-1">
          内网穿透和安全加密，支持 P2P 连接和代理转发
        </p>
      </div>

      <div className="flex justify-between items-center">
        <p className="text-sm text-gray-400">
          已配置 {tunnels.length} 个隧道
        </p>
        <button
          onClick={handleAdd}
          className="px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded-lg flex items-center gap-2 transition-colors"
        >
          <Plus className="w-4 h-4" />
          <span>添加隧道</span>
        </button>
      </div>

      {loading ? (
        <div className="text-center py-12 text-gray-400">加载中...</div>
      ) : tunnels.length === 0 ? (
        <div className="text-center py-12 text-gray-500">
          暂无隧道配置
        </div>
      ) : (
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
                      <span className="text-white ml-1">{tunnel.tunnelType}</span>
                    </div>
                    <div>
                      <span className="text-gray-400">端口映射：</span>
                      <span className="text-white ml-1">{tunnel.localPort} → {tunnel.remotePort}</span>
                    </div>
                    <div>
                      <span className="text-gray-400">远程主机：</span>
                      <span className="text-white ml-1">{tunnel.remoteHost || '-'}</span>
                    </div>
                    <div>
                      <span className="text-gray-400">带宽：</span>
                      <span className="text-white ml-1">{tunnel.bandwidth || '0 Mbps'}</span>
                    </div>
                  </div>
                </div>

                <div className="flex items-center gap-2 ml-4">
                  <button
                    onClick={() => handleToggle(tunnel)}
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
      )}

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
                    value={formData.tunnelType}
                    onChange={(e) => setFormData({ ...formData, tunnelType: e.target.value as any })}
                    className="w-full px-4 py-2 bg-gray-900 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-blue-500"
                  >
                    <option value="P2P">P2P</option>
                    <option value="Proxy">Proxy</option>
                    <option value="VPN">VPN</option>
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
                    onClick={handleSave}
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

// ========== 4. 插件系统 (真实API) ==========
export function PluginSystemPage() {
  const [plugins, setPlugins] = useState<Plugin[]>([]);
  const [loading, setLoading] = useState(true);
  const [showModal, setShowModal] = useState(false);
  const [formData, setFormData] = useState({
    name: '',
    version: '1.0.0',
    pluginType: 'protocol',
    description: '',
    author: '',
  });

  const fetchPlugins = useCallback(async () => {
    try {
      setLoading(true);
      const res = await pluginApi.getPlugins({ pageSize: 100 });
      if (res.success && res.data) {
        setPlugins(res.data.items);
      }
    } catch (err) {
      console.error('获取插件列表失败:', err);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchPlugins();
  }, [fetchPlugins]);

  const handleAdd = () => {
    setFormData({ name: '', version: '1.0.0', pluginType: 'protocol', description: '', author: '' });
    setShowModal(true);
  };

  const handleSave = async () => {
    try {
      await pluginApi.createPlugin(formData as CreatePluginRequest);
      setShowModal(false);
      fetchPlugins();
    } catch (err) {
      console.error('创建插件失败:', err);
    }
  };

  const handleDelete = async (id: number) => {
    if (confirm('确定要卸载此插件吗？')) {
      try {
        await pluginApi.deletePlugin(id);
        fetchPlugins();
      } catch (err) {
        console.error('删除插件失败:', err);
      }
    }
  };

  const handleToggle = async (plugin: Plugin) => {
    try {
      if (plugin.status === 'running') {
        await pluginApi.stopPlugin(plugin.id);
      } else {
        await pluginApi.startPlugin(plugin.id);
      }
      fetchPlugins();
    } catch (err) {
      console.error('切换插件状态失败:', err);
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
          插件生命周期管理和热更新
        </p>
      </div>

      <div className="flex justify-between items-center">
        <p className="text-sm text-gray-400">
          已安装 {plugins.length} 个插件
        </p>
        <button
          onClick={handleAdd}
          className="px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded-lg flex items-center gap-2 transition-colors"
        >
          <Upload className="w-4 h-4" />
          <span>添加插件</span>
        </button>
      </div>

      {loading ? (
        <div className="text-center py-12 text-gray-400">加载中...</div>
      ) : plugins.length === 0 ? (
        <div className="text-center py-12 text-gray-500">
          暂无插件
        </div>
      ) : (
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

                  <p className="text-gray-400 text-sm mb-2">{plugin.description || '-'}</p>

                  <div className="grid grid-cols-2 gap-4 text-sm">
                    <div>
                      <span className="text-gray-400">作者：</span>
                      <span className="text-white ml-1">{plugin.author || '-'}</span>
                    </div>
                    <div>
                      <span className="text-gray-400">安装日期：</span>
                      <span className="text-white ml-1">
                        {plugin.installedAt ? new Date(plugin.installedAt).toLocaleDateString() : '-'}
                      </span>
                    </div>
                  </div>
                </div>

                <div className="flex items-center gap-2 ml-4">
                  <button
                    onClick={() => handleToggle(plugin)}
                    className={`p-2 rounded-lg transition-colors ${
                      plugin.status === 'running'
                        ? 'bg-orange-500/20 hover:bg-orange-500/30 text-orange-300'
                        : 'bg-green-500/20 hover:bg-green-500/30 text-green-300'
                    }`}
                  >
                    {plugin.status === 'running' ? <Pause className="w-4 h-4" /> : <Play className="w-4 h-4" />}
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
      )}

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
                <h3 className="text-xl font-bold text-white">添加插件</h3>
                <button onClick={() => setShowModal(false)} className="p-2 hover:bg-gray-700 rounded-lg transition-colors">
                  <X className="w-5 h-5 text-gray-400" />
                </button>
              </div>

              <div className="space-y-4">
                <div>
                  <label className="block text-sm font-medium text-gray-400 mb-2">插件名称</label>
                  <input
                    type="text"
                    value={formData.name}
                    onChange={(e) => setFormData({ ...formData, name: e.target.value })}
                    placeholder="例如：Modbus解析插件"
                    className="w-full px-4 py-2 bg-gray-900 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-blue-500"
                  />
                </div>

                <div className="grid grid-cols-2 gap-4">
                  <div>
                    <label className="block text-sm font-medium text-gray-400 mb-2">版本</label>
                    <input
                      type="text"
                      value={formData.version}
                      onChange={(e) => setFormData({ ...formData, version: e.target.value })}
                      placeholder="1.0.0"
                      className="w-full px-4 py-2 bg-gray-900 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-blue-500"
                    />
                  </div>
                  <div>
                    <label className="block text-sm font-medium text-gray-400 mb-2">类型</label>
                    <select
                      value={formData.pluginType}
                      onChange={(e) => setFormData({ ...formData, pluginType: e.target.value })}
                      className="w-full px-4 py-2 bg-gray-900 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-blue-500"
                    >
                      <option value="protocol">协议</option>
                      <option value="parser">解析器</option>
                      <option value="transform">转换器</option>
                    </select>
                  </div>
                </div>

                <div>
                  <label className="block text-sm font-medium text-gray-400 mb-2">作者</label>
                  <input
                    type="text"
                    value={formData.author}
                    onChange={(e) => setFormData({ ...formData, author: e.target.value })}
                    placeholder="插件作者"
                    className="w-full px-4 py-2 bg-gray-900 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-blue-500"
                  />
                </div>

                <div>
                  <label className="block text-sm font-medium text-gray-400 mb-2">描述</label>
                  <textarea
                    value={formData.description}
                    onChange={(e) => setFormData({ ...formData, description: e.target.value })}
                    placeholder="插件功能描述"
                    rows={3}
                    className="w-full px-4 py-2 bg-gray-900 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-blue-500"
                  />
                </div>

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
