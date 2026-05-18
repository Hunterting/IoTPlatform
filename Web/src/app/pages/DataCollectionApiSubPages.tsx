import { useState, useEffect, useCallback } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import {
  Database,
  List,
  BookOpen,
  ClipboardCheck,
  Plus,
  Play,
  Pause,
  Edit,
  Trash2,
  X,
  Download,
  AlertCircle,
  Droplet,
  Flame,
  Zap,
  Settings,
  CheckCircle,
  FileJson,
  Key,
} from 'lucide-react';
import { dataRuleApi, type DataRule, type CreateDataRuleRequest } from '@/app/services/api/dataRuleApi';
import { databaseConfigApi, type DatabaseConfig, type CreateDatabaseConfigRequest } from '@/app/services/api/databaseConfigApi';

// ========== 6. 规则引擎 (真实API) ==========
export function RuleEnginePage() {
  const [rules, setRules] = useState<DataRule[]>([]);
  const [loading, setLoading] = useState(true);
  const [showModal, setShowModal] = useState(false);
  const [editingRule, setEditingRule] = useState<DataRule | null>(null);
  const [formData, setFormData] = useState({
    name: '',
    description: '',
    ruleType: 'threshold' as 'threshold' | 'anomaly' | 'pattern',
    ruleExpression: '',
    level: 'warning',
    priority: 1,
  });

  const fetchRules = useCallback(async () => {
    try {
      setLoading(true);
      const res = await dataRuleApi.getDataRules({ pageSize: 100 });
      if (res.success && res.data) {
        setRules(res.data.items);
      }
    } catch (err) {
      console.error('获取规则列表失败:', err);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchRules();
  }, [fetchRules]);

  const handleAdd = () => {
    setEditingRule(null);
    setFormData({ name: '', description: '', ruleType: 'threshold', ruleExpression: '', level: 'warning', priority: 1 });
    setShowModal(true);
  };

  const handleEdit = (rule: DataRule) => {
    setEditingRule(rule);
    setFormData({
      name: rule.name,
      description: rule.description || '',
      ruleType: (rule.ruleType as any) || 'threshold',
      ruleExpression: rule.ruleExpression || '',
      level: rule.level || 'warning',
      priority: rule.priority || 1,
    });
    setShowModal(true);
  };

  const handleSave = async () => {
    try {
      if (editingRule) {
        await dataRuleApi.updateDataRule(editingRule.id, {
          name: formData.name,
          description: formData.description,
          ruleType: formData.ruleType,
          ruleExpression: formData.ruleExpression,
          level: formData.level,
          priority: formData.priority,
        });
      } else {
        await dataRuleApi.createDataRule({
          name: formData.name,
          description: formData.description,
          ruleType: formData.ruleType,
          ruleExpression: formData.ruleExpression,
          level: formData.level,
          priority: formData.priority,
          isActive: true,
        } as CreateDataRuleRequest);
      }
      setShowModal(false);
      fetchRules();
    } catch (err) {
      console.error('保存规则失败:', err);
    }
  };

  const handleToggle = async (rule: DataRule) => {
    try {
      await dataRuleApi.updateDataRule(rule.id, { isActive: !rule.isActive });
      fetchRules();
    } catch (err) {
      console.error('切换规则状态失败:', err);
    }
  };

  const handleDelete = async (id: number) => {
    if (confirm('确定要删除此规则吗？')) {
      try {
        await dataRuleApi.deleteDataRule(id);
        fetchRules();
      } catch (err) {
        console.error('删除规则失败:', err);
      }
    }
  };

  return (
    <div className="p-6 space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-white flex items-center gap-2">
          <List className="w-7 h-7 text-purple-500" />
          规则引擎
        </h1>
        <p className="text-sm text-gray-400 mt-1">
          可视化规则编辑器、支持 JavaScript 表达式、灵活的条件组合
        </p>
      </div>

      <div className="flex justify-between items-center">
        <p className="text-sm text-gray-400">
          已配置 {rules.length} 条规则
        </p>
        <button
          onClick={handleAdd}
          className="px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded-lg flex items-center gap-2 transition-colors"
        >
          <Plus className="w-4 h-4" />
          <span>添加规则</span>
        </button>
      </div>

      {loading ? (
        <div className="text-center py-12 text-gray-400">加载中...</div>
      ) : rules.length === 0 ? (
        <div className="text-center py-12 text-gray-500">
          暂无规则配置
        </div>
      ) : (
        <div className="space-y-4">
          {rules.map((rule) => (
            <div key={rule.id} className="bg-gray-800 border border-gray-700 rounded-lg p-4">
              <div className="flex items-start justify-between">
                <div className="flex-1">
                  <div className="flex items-center gap-3 mb-2">
                    <h3 className="text-lg font-semibold text-white">{rule.name}</h3>
                    <span className={`px-2 py-1 rounded text-xs font-medium ${
                      rule.isActive ? 'bg-green-500/20 text-green-300' : 'bg-gray-500/20 text-gray-300'
                    }`}>
                      {rule.isActive ? '已启用' : '已禁用'}
                    </span>
                    <span className={`px-2 py-1 rounded text-xs font-medium ${
                      rule.ruleType === 'threshold'
                        ? 'bg-orange-500/20 text-orange-300'
                        : rule.ruleType === 'anomaly'
                        ? 'bg-red-500/20 text-red-300'
                        : 'bg-purple-500/20 text-purple-300'
                    }`}>
                      {rule.ruleType === 'threshold' ? '阈值' : rule.ruleType === 'anomaly' ? '异常检测' : '模式识别'}
                    </span>
                  </div>
                  <p className="text-gray-400 text-sm mb-2">{rule.description || '-'}</p>
                  <div className="grid grid-cols-2 gap-4 text-sm">
                    <div>
                      <span className="text-gray-400">触发条件：</span>
                      <code className="text-blue-300 ml-1 bg-blue-500/10 px-2 py-0.5 rounded">
                        {rule.ruleExpression || '-'}
                      </code>
                    </div>
                    <div>
                      <span className="text-gray-400">优先级：</span>
                      <span className="text-white ml-1">{rule.priority}</span>
                    </div>
                  </div>
                </div>

                <div className="flex items-center gap-2 ml-4">
                  <label className="relative inline-flex items-center cursor-pointer">
                    <input
                      type="checkbox"
                      checked={rule.isActive}
                      onChange={() => handleToggle(rule)}
                      className="sr-only peer"
                    />
                    <div className="w-11 h-6 bg-gray-700 peer-focus:outline-none rounded-full peer peer-checked:after:translate-x-full peer-checked:after:border-white after:content-[''] after:absolute after:top-[2px] after:left-[2px] after:bg-white after:border-gray-300 after:border after:rounded-full after:h-5 after:w-5 after:transition-all peer-checked:bg-green-600"></div>
                  </label>
                  <button
                    onClick={() => handleEdit(rule)}
                    className="p-2 bg-blue-500/20 hover:bg-blue-500/30 text-blue-300 rounded-lg transition-colors"
                  >
                    <Edit className="w-4 h-4" />
                  </button>
                  <button
                    onClick={() => handleDelete(rule.id)}
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
              className="bg-gray-800 border border-gray-700 rounded-xl max-w-2xl w-full p-6"
            >
              <div className="flex items-center justify-between mb-6">
                <h3 className="text-xl font-bold text-white">
                  {editingRule ? '编辑规则' : '添加数据处理规则'}
                </h3>
                <button onClick={() => setShowModal(false)} className="p-2 hover:bg-gray-700 rounded-lg transition-colors">
                  <X className="w-5 h-5 text-gray-400" />
                </button>
              </div>

              <div className="space-y-4">
                <div>
                  <label className="block text-sm font-medium text-gray-400 mb-2">规则名称</label>
                  <input
                    type="text"
                    value={formData.name}
                    onChange={(e) => setFormData({ ...formData, name: e.target.value })}
                    placeholder="例如：用电量异常告警"
                    className="w-full px-4 py-2 bg-gray-900 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-blue-500"
                  />
                </div>

                <div>
                  <label className="block text-sm font-medium text-gray-400 mb-2">规则描述</label>
                  <textarea
                    value={formData.description}
                    onChange={(e) => setFormData({ ...formData, description: e.target.value })}
                    placeholder="描述该规则的作用和触发条件"
                    rows={3}
                    className="w-full px-4 py-2 bg-gray-900 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-blue-500"
                  />
                </div>

                <div className="grid grid-cols-2 gap-4">
                  <div>
                    <label className="block text-sm font-medium text-gray-400 mb-2">规则类型</label>
                    <select
                      value={formData.ruleType}
                      onChange={(e) => setFormData({ ...formData, ruleType: e.target.value as any })}
                      className="w-full px-4 py-2 bg-gray-900 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-blue-500"
                    >
                      <option value="threshold">阈值规则</option>
                      <option value="anomaly">异常检测</option>
                      <option value="pattern">模式识别</option>
                    </select>
                  </div>
                  <div>
                    <label className="block text-sm font-medium text-gray-400 mb-2">优先级</label>
                    <input
                      type="number"
                      value={formData.priority}
                      onChange={(e) => setFormData({ ...formData, priority: parseInt(e.target.value) })}
                      className="w-full px-4 py-2 bg-gray-900 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-blue-500"
                    />
                  </div>
                </div>

                <div>
                  <label className="block text-sm font-medium text-gray-400 mb-2">触发条件（JavaScript表达式）</label>
                  <textarea
                    value={formData.ruleExpression}
                    onChange={(e) => setFormData({ ...formData, ruleExpression: e.target.value })}
                    placeholder='例如：value > 200 && energyType === "电"'
                    rows={3}
                    className="w-full px-4 py-2 bg-gray-900 border border-gray-700 rounded-lg text-white font-mono text-sm focus:outline-none focus:border-blue-500"
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
                    {editingRule ? '保存' : '添加'}
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

// ========== 8. 数据库配置 (真实API) ==========
export function DatabaseConfigPage() {
  const [databases, setDatabases] = useState<DatabaseConfig[]>([]);
  const [loading, setLoading] = useState(true);
  const [showModal, setShowModal] = useState(false);
  const [editingDb, setEditingDb] = useState<DatabaseConfig | null>(null);
  const [formData, setFormData] = useState({
    name: '',
    databaseType: 'MySQL' as 'MySQL' | 'TDengine' | 'InfluxDB' | 'PostgreSQL' | 'MongoDB',
    host: '',
    port: 3306,
    databaseName: '',
    username: '',
    password: '',
    description: '',
  });

  const fetchDatabases = useCallback(async () => {
    try {
      setLoading(true);
      const res = await databaseConfigApi.getDatabaseConfigs({ pageSize: 100 });
      if (res.success && res.data) {
        setDatabases(res.data.items);
      }
    } catch (err) {
      console.error('获取数据库配置列表失败:', err);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchDatabases();
  }, [fetchDatabases]);

  const handleAdd = () => {
    setEditingDb(null);
    setFormData({ name: '', databaseType: 'MySQL', host: '', port: 3306, databaseName: '', username: '', password: '', description: '' });
    setShowModal(true);
  };

  const handleEdit = (db: DatabaseConfig) => {
    setEditingDb(db);
    setFormData({
      name: db.name,
      databaseType: db.databaseType as any,
      host: db.host,
      port: db.port,
      databaseName: db.databaseName,
      username: db.username || '',
      password: '',
      description: db.description || '',
    });
    setShowModal(true);
  };

  const handleSave = async () => {
    try {
      if (editingDb) {
        await databaseConfigApi.updateDatabaseConfig(editingDb.id, {
          name: formData.name,
          databaseType: formData.databaseType,
          host: formData.host,
          port: formData.port,
          databaseName: formData.databaseName,
          username: formData.username,
          password: formData.password || undefined,
          description: formData.description,
        });
      } else {
        await databaseConfigApi.createDatabaseConfig({
          name: formData.name,
          databaseType: formData.databaseType,
          host: formData.host,
          port: formData.port,
          databaseName: formData.databaseName,
          username: formData.username,
          password: formData.password,
          description: formData.description,
        } as CreateDatabaseConfigRequest);
      }
      setShowModal(false);
      fetchDatabases();
    } catch (err) {
      console.error('保存数据库配置失败:', err);
    }
  };

  const handleTest = async (id: number) => {
    try {
      const res = await databaseConfigApi.testDatabaseConnectionById(id);
      if (res.success && res.data) {
        alert('连接测试成功！');
      } else {
        alert('连接测试失败：' + (res.message || '未知错误'));
      }
    } catch (err) {
      alert('连接测试失败');
    }
  };

  const handleDelete = async (id: number) => {
    if (confirm('确定要删除此数据库配置吗？')) {
      try {
        await databaseConfigApi.deleteDatabaseConfig(id);
        fetchDatabases();
      } catch (err) {
        console.error('删除数据库配置失败:', err);
      }
    }
  };

  return (
    <div className="p-6 space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-white flex items-center gap-2">
          <Database className="w-7 h-7 text-blue-500" />
          数据库配置
        </h1>
        <p className="text-sm text-gray-400 mt-1">
          配置MySQL、TDengine、InfluxDB等数据库连接
        </p>
      </div>

      <div className="flex justify-between items-center">
        <p className="text-sm text-gray-400">
          已配置 {databases.length} 个数据库连接
        </p>
        <button
          onClick={handleAdd}
          className="px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded-lg flex items-center gap-2 transition-colors"
        >
          <Plus className="w-4 h-4" />
          <span>添加数据库</span>
        </button>
      </div>

      {loading ? (
        <div className="text-center py-12 text-gray-400">加载中...</div>
      ) : databases.length === 0 ? (
        <div className="text-center py-12 text-gray-500">
          暂无数据库配置
        </div>
      ) : (
        <div className="space-y-4">
          {databases.map((db) => (
            <div key={db.id} className="bg-gray-800 border border-gray-700 rounded-lg p-4">
              <div className="flex items-center justify-between">
                <div className="flex-1">
                  <div className="flex items-center gap-3 mb-2">
                    <Database className="w-5 h-5 text-blue-500" />
                    <h3 className="text-lg font-semibold text-white">{db.name}</h3>
                    <span className={`px-2 py-1 rounded text-xs font-medium ${
                      db.status === 'connected'
                        ? 'bg-green-500/20 text-green-300'
                        : 'bg-red-500/20 text-red-300'
                    }`}>
                      {db.status === 'connected' ? '已连接' : '未连接'}
                    </span>
                  </div>
                  <div className="grid grid-cols-4 gap-4 text-sm">
                    <div>
                      <span className="text-gray-400">类型：</span>
                      <span className="text-white ml-1">{db.databaseType}</span>
                    </div>
                    <div>
                      <span className="text-gray-400">主机：</span>
                      <span className="text-white ml-1">{db.host}:{db.port}</span>
                    </div>
                    <div>
                      <span className="text-gray-400">数据库：</span>
                      <span className="text-white ml-1">{db.databaseName}</span>
                    </div>
                    <div>
                      <span className="text-gray-400">用户名：</span>
                      <span className="text-white ml-1">{db.username || '-'}</span>
                    </div>
                  </div>
                </div>

                <div className="flex items-center gap-2 ml-4">
                  <button
                    onClick={() => handleTest(db.id)}
                    className="p-2 bg-green-500/20 hover:bg-green-500/30 text-green-300 rounded-lg transition-colors"
                    title="测试连接"
                  >
                    <CheckCircle className="w-4 h-4" />
                  </button>
                  <button
                    onClick={() => handleEdit(db)}
                    className="p-2 bg-blue-500/20 hover:bg-blue-500/30 text-blue-300 rounded-lg transition-colors"
                    title="编辑"
                  >
                    <Edit className="w-4 h-4" />
                  </button>
                  <button
                    onClick={() => handleDelete(db.id)}
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
                <h3 className="text-xl font-bold text-white">{editingDb ? '编辑数据库' : '添加数据库'}</h3>
                <button onClick={() => setShowModal(false)} className="p-2 hover:bg-gray-700 rounded-lg transition-colors">
                  <X className="w-5 h-5 text-gray-400" />
                </button>
              </div>

              <div className="space-y-4">
                <div>
                  <label className="block text-sm font-medium text-gray-400 mb-2">数据库名称</label>
                  <input
                    type="text"
                    value={formData.name}
                    onChange={(e) => setFormData({ ...formData, name: e.target.value })}
                    placeholder="例如：MySQL主库"
                    className="w-full px-4 py-2 bg-gray-900 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-blue-500"
                  />
                </div>

                <div>
                  <label className="block text-sm font-medium text-gray-400 mb-2">数据库类型</label>
                  <select
                    value={formData.databaseType}
                    onChange={(e) => {
                      const type = e.target.value as any;
                      setFormData({
                        ...formData,
                        databaseType: type,
                        port: type === 'MySQL' ? 3306 : type === 'TDengine' ? 6030 : type === 'InfluxDB' ? 8086 : 5432,
                      });
                    }}
                    className="w-full px-4 py-2 bg-gray-900 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-blue-500"
                  >
                    <option value="MySQL">MySQL</option>
                    <option value="PostgreSQL">PostgreSQL</option>
                    <option value="TDengine">TDengine</option>
                    <option value="InfluxDB">InfluxDB</option>
                    <option value="MongoDB">MongoDB</option>
                  </select>
                </div>

                <div className="grid grid-cols-2 gap-4">
                  <div>
                    <label className="block text-sm font-medium text-gray-400 mb-2">主机地址</label>
                    <input
                      type="text"
                      value={formData.host}
                      onChange={(e) => setFormData({ ...formData, host: e.target.value })}
                      placeholder="192.168.1.10"
                      className="w-full px-4 py-2 bg-gray-900 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-blue-500"
                    />
                  </div>
                  <div>
                    <label className="block text-sm font-medium text-gray-400 mb-2">端口</label>
                    <input
                      type="number"
                      value={formData.port}
                      onChange={(e) => setFormData({ ...formData, port: parseInt(e.target.value) })}
                      className="w-full px-4 py-2 bg-gray-900 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-blue-500"
                    />
                  </div>
                </div>

                <div>
                  <label className="block text-sm font-medium text-gray-400 mb-2">数据库名</label>
                  <input
                    type="text"
                    value={formData.databaseName}
                    onChange={(e) => setFormData({ ...formData, databaseName: e.target.value })}
                    placeholder="smart_kitchen"
                    className="w-full px-4 py-2 bg-gray-900 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-blue-500"
                  />
                </div>

                <div>
                  <label className="block text-sm font-medium text-gray-400 mb-2">用户名</label>
                  <input
                    type="text"
                    value={formData.username}
                    onChange={(e) => setFormData({ ...formData, username: e.target.value })}
                    placeholder="admin"
                    className="w-full px-4 py-2 bg-gray-900 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-blue-500"
                  />
                </div>

                <div>
                  <label className="block text-sm font-medium text-gray-400 mb-2">密码 {editingDb && '(留空保持原密码)'}</label>
                  <input
                    type="password"
                    value={formData.password}
                    onChange={(e) => setFormData({ ...formData, password: e.target.value })}
                    placeholder={editingDb ? '••••••••' : '密码'}
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
                    {editingDb ? '保存' : '添加'}
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
