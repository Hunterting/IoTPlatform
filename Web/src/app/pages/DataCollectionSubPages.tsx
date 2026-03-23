import { useState } from 'react';
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
  ArrowRight,
  Code2,
  Filter,
  Calculator,
  GitMerge,
  FileType,
} from 'lucide-react';

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
  transformRules?: TransformRule[];
  fieldMapping?: FieldMapping[];
}

// 数据转换规则接口
interface TransformRule {
  id: string;
  type: 'format' | 'calculate' | 'filter' | 'merge';
  name: string;
  description: string;
  config: any;
}

// 字段映射接口
interface FieldMapping {
  id: string;
  sourceField: string;
  targetField: string;
  dataType: string;
  transform?: string;
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

// ========== 6. 规则引擎 ==========
export function RuleEnginePage() {
  const [rules, setRules] = useState<DataRule[]>([
    {
      id: 'rule-001',
      name: '用电量异常告警',
      description: '当小时用电量超过200kWh时触发告警',
      type: 'threshold',
      enabled: true,
      condition: 'value > 200 && energyType === "电"',
      action: '发送邮件通知',
    },
  ]);

  const [showModal, setShowModal] = useState(false);
  const [editingRule, setEditingRule] = useState<DataRule | null>(null);
  const [formData, setFormData] = useState({
    name: '',
    description: '',
    type: 'threshold' as 'threshold' | 'anomaly' | 'pattern',
    condition: '',
    action: '发送邮件通知',
  });

  const handleAdd = () => {
    setEditingRule(null);
    setFormData({ name: '', description: '', type: 'threshold', condition: '', action: '发送邮件通知' });
    setShowModal(true);
  };

  const handleEdit = (rule: DataRule) => {
    setEditingRule(rule);
    setFormData({
      name: rule.name,
      description: rule.description,
      type: rule.type,
      condition: rule.condition,
      action: rule.action,
    });
    setShowModal(true);
  };

  const handleSave = () => {
    if (editingRule) {
      setRules(rules.map(r =>
        r.id === editingRule.id ? { ...r, ...formData } : r
      ));
    } else {
      const newRule: DataRule = {
        id: `rule-${Date.now()}`,
        ...formData,
        enabled: true,
      };
      setRules([...rules, newRule]);
    }
    setShowModal(false);
  };

  const handleToggle = (id: string) => {
    setRules(rules.map(r => r.id === id ? { ...r, enabled: !r.enabled } : r));
  };

  const handleDelete = (id: string) => {
    if (confirm('确定要删除此规则吗？')) {
      setRules(rules.filter(r => r.id !== id));
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
          配置数据处理规则和告警条件，支持JavaScript表达式
        </p>
        <button
          onClick={handleAdd}
          className="px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded-lg flex items-center gap-2 transition-colors"
        >
          <Plus className="w-4 h-4" />
          <span>添加规则</span>
        </button>
      </div>

      <div className="space-y-4">
        {rules.map((rule) => (
          <div key={rule.id} className="bg-gray-800 border border-gray-700 rounded-lg p-4">
            <div className="flex items-start justify-between">
              <div className="flex-1">
                <div className="flex items-center gap-3 mb-2">
                  <h3 className="text-lg font-semibold text-white">{rule.name}</h3>
                  <span className={`px-2 py-1 rounded text-xs font-medium ${
                    rule.enabled ? 'bg-green-500/20 text-green-300' : 'bg-gray-500/20 text-gray-300'
                  }`}>
                    {rule.enabled ? '已启用' : '已禁用'}
                  </span>
                  <span className={`px-2 py-1 rounded text-xs font-medium ${
                    rule.type === 'threshold'
                      ? 'bg-orange-500/20 text-orange-300'
                      : rule.type === 'anomaly'
                      ? 'bg-red-500/20 text-red-300'
                      : 'bg-purple-500/20 text-purple-300'
                  }`}>
                    {rule.type === 'threshold' ? '阈值' : rule.type === 'anomaly' ? '异常检测' : '模式识别'}
                  </span>
                </div>
                <p className="text-gray-400 text-sm mb-2">{rule.description}</p>
                <div className="grid grid-cols-2 gap-4 text-sm">
                  <div>
                    <span className="text-gray-400">触发条件：</span>
                    <code className="text-blue-300 ml-1 bg-blue-500/10 px-2 py-0.5 rounded">
                      {rule.condition}
                    </code>
                  </div>
                  <div>
                    <span className="text-gray-400">执行动作：</span>
                    <span className="text-white ml-1">{rule.action}</span>
                  </div>
                </div>
              </div>

              <div className="flex items-center gap-2 ml-4">
                <label className="relative inline-flex items-center cursor-pointer">
                  <input
                    type="checkbox"
                    checked={rule.enabled}
                    onChange={() => handleToggle(rule.id)}
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

      {/* 添加/编辑规则弹窗 */}
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

                <div>
                  <label className="block text-sm font-medium text-gray-400 mb-2">规则类型</label>
                  <select
                    value={formData.type}
                    onChange={(e) => setFormData({ ...formData, type: e.target.value as any })}
                    className="w-full px-4 py-2 bg-gray-900 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-blue-500"
                  >
                    <option value="threshold">阈值规则</option>
                    <option value="anomaly">异常检测</option>
                    <option value="pattern">模式识别</option>
                  </select>
                </div>

                <div>
                  <label className="block text-sm font-medium text-gray-400 mb-2">触发条件（JavaScript表达式）</label>
                  <textarea
                    value={formData.condition}
                    onChange={(e) => setFormData({ ...formData, condition: e.target.value })}
                    placeholder='例如：value > 200 && energyType === "电"'
                    rows={3}
                    className="w-full px-4 py-2 bg-gray-900 border border-gray-700 rounded-lg text-white font-mono text-sm focus:outline-none focus:border-blue-500"
                  />
                </div>

                <div>
                  <label className="block text-sm font-medium text-gray-400 mb-2">执行动作</label>
                  <select
                    value={formData.action}
                    onChange={(e) => setFormData({ ...formData, action: e.target.value })}
                    className="w-full px-4 py-2 bg-gray-900 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-blue-500"
                  >
                    <option>发送邮件通知</option>
                    <option>发送短信 + 系统通知</option>
                    <option>生成分析报告</option>
                    <option>触发Webhook</option>
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

// ========== 7. 数据转换 ==========
export function DataTransformPage() {
  const [tasks, setTasks] = useState<ETLTask[]>([
    {
      id: 'etl-001',
      name: '能耗数据ETL',
      source: 'Modbus TCP',
      target: 'MySQL',
      status: 'running',
      lastRun: '2026-03-23 14:30:00',
      recordsProcessed: 15847,
    },
  ]);

  const [showModal, setShowModal] = useState(false);
  const [formData, setFormData] = useState({
    name: '',
    source: 'Modbus TCP',
    target: 'MySQL',
  });

  const handleAdd = () => {
    const newTask: ETLTask = {
      id: `etl-${Date.now()}`,
      ...formData,
      status: 'stopped',
      lastRun: new Date().toLocaleString(),
      recordsProcessed: 0,
    };
    setTasks([...tasks, newTask]);
    setShowModal(false);
    setFormData({ name: '', source: 'Modbus TCP', target: 'MySQL' });
  };

  const handleToggle = (id: string) => {
    setTasks(tasks.map(t =>
      t.id === id ? { ...t, status: t.status === 'running' ? 'stopped' : 'running' } : t
    ));
  };

  const handleDelete = (id: string) => {
    if (confirm('确定要删除此ETL任务吗？')) {
      setTasks(tasks.filter(t => t.id !== id));
    }
  };

  return (
    <div className="p-6 space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-white flex items-center gap-2">
          <BookOpen className="w-7 h-7 text-orange-500" />
          数据转换
        </h1>
        <p className="text-sm text-gray-400 mt-1">
          ETL 管道、数据格式转换、字段映射
        </p>
      </div>

      <div className="flex justify-between items-center">
        <div className="bg-orange-500/10 border border-orange-500/30 rounded-lg p-4 flex-1 mr-4">
          <div className="flex items-start gap-3">
            <BookOpen className="w-5 h-5 text-orange-400 flex-shrink-0 mt-0.5" />
            <div className="text-sm text-orange-300">
              <p className="font-medium mb-2">数据转换（ETL）功能</p>
              <ul className="space-y-1 text-orange-300/80 text-xs">
                <li>• ETL管道：提取(Extract)、转换(Transform)、加载(Load)</li>
                <li>• 数据格式转换：JSON、XML、CSV等格式互转</li>
                <li>• 字段映射：灵活配置源字段到目标字段的映射关系</li>
                <li>• 数据清洗：去重、过滤、校验</li>
              </ul>
            </div>
          </div>
        </div>
        <button
          onClick={() => setShowModal(true)}
          className="px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded-lg flex items-center gap-2 transition-colors"
        >
          <Plus className="w-4 h-4" />
          <span>创建任务</span>
        </button>
      </div>

      <div className="space-y-4">
        {tasks.map((task) => (
          <div key={task.id} className="bg-gray-800 border border-gray-700 rounded-lg p-4">
            <div className="flex items-start justify-between">
              <div className="flex-1">
                <div className="flex items-center gap-3 mb-2">
                  <BookOpen className="w-5 h-5 text-orange-500" />
                  <h3 className="text-lg font-semibold text-white">{task.name}</h3>
                  <span className={`px-2 py-1 rounded text-xs font-medium ${
                    task.status === 'running'
                      ? 'bg-green-500/20 text-green-300'
                      : task.status === 'failed'
                      ? 'bg-red-500/20 text-red-300'
                      : 'bg-gray-500/20 text-gray-300'
                  }`}>
                    {task.status === 'running' ? '运行中' : task.status === 'failed' ? '失败' : '已停止'}
                  </span>
                </div>

                <div className="grid grid-cols-2 md:grid-cols-4 gap-4 text-sm">
                  <div>
                    <span className="text-gray-400">数据源：</span>
                    <span className="text-white ml-1">{task.source}</span>
                  </div>
                  <div>
                    <span className="text-gray-400">目标：</span>
                    <span className="text-white ml-1">{task.target}</span>
                  </div>
                  <div>
                    <span className="text-gray-400">最后运行：</span>
                    <span className="text-white ml-1">{task.lastRun}</span>
                  </div>
                  <div>
                    <span className="text-gray-400">处理记录：</span>
                    <span className="text-white ml-1">{task.recordsProcessed.toLocaleString()}</span>
                  </div>
                </div>
              </div>

              <div className="flex items-center gap-2 ml-4">
                <button
                  onClick={() => handleToggle(task.id)}
                  className={`p-2 rounded-lg transition-colors ${
                    task.status === 'running'
                      ? 'bg-orange-500/20 hover:bg-orange-500/30 text-orange-300'
                      : 'bg-green-500/20 hover:bg-green-500/30 text-green-300'
                  }`}
                >
                  {task.status === 'running' ? <Pause className="w-4 h-4" /> : <Play className="w-4 h-4" />}
                </button>
                <button className="p-2 bg-blue-500/20 hover:bg-blue-500/30 text-blue-300 rounded-lg transition-colors">
                  <Edit className="w-4 h-4" />
                </button>
                <button
                  onClick={() => handleDelete(task.id)}
                  className="p-2 bg-red-500/20 hover:bg-red-500/30 text-red-300 rounded-lg transition-colors"
                >
                  <Trash2 className="w-4 h-4" />
                </button>
              </div>
            </div>
          </div>
        ))}
      </div>

      {/* 创建ETL任务弹窗 */}
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
                <h3 className="text-xl font-bold text-white">创建ETL任务</h3>
                <button onClick={() => setShowModal(false)} className="p-2 hover:bg-gray-700 rounded-lg transition-colors">
                  <X className="w-5 h-5 text-gray-400" />
                </button>
              </div>

              <div className="space-y-4">
                <div>
                  <label className="block text-sm font-medium text-gray-400 mb-2">任务名称</label>
                  <input
                    type="text"
                    value={formData.name}
                    onChange={(e) => setFormData({ ...formData, name: e.target.value })}
                    placeholder="例如：能耗数据ETL"
                    className="w-full px-4 py-2 bg-gray-900 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-blue-500"
                  />
                </div>

                <div>
                  <label className="block text-sm font-medium text-gray-400 mb-2">数据源</label>
                  <select
                    value={formData.source}
                    onChange={(e) => setFormData({ ...formData, source: e.target.value })}
                    className="w-full px-4 py-2 bg-gray-900 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-blue-500"
                  >
                    <option>Modbus TCP</option>
                    <option>OPC UA</option>
                    <option>MQTT</option>
                    <option>HTTP API</option>
                  </select>
                </div>

                <div>
                  <label className="block text-sm font-medium text-gray-400 mb-2">目标</label>
                  <select
                    value={formData.target}
                    onChange={(e) => setFormData({ ...formData, target: e.target.value })}
                    className="w-full px-4 py-2 bg-gray-900 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-blue-500"
                  >
                    <option>MySQL</option>
                    <option>TDengine</option>
                    <option>InfluxDB</option>
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
                    创建
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

// ========== 8. 数据库配置 ==========
export function DatabaseConfigPage() {
  const [databases, setDatabases] = useState<DatabaseConfig[]>([
    {
      id: 'db-001',
      name: 'MySQL主库',
      type: 'MySQL',
      status: 'connected',
      host: '192.168.1.10',
      port: 3306,
      database: 'smart_kitchen',
      username: 'admin',
    },
    {
      id: 'db-002',
      name: 'TDengine时序库',
      type: 'TDengine',
      status: 'connected',
      host: '192.168.1.11',
      port: 6030,
      database: 'energy_data',
      username: 'root',
    },
  ]);

  const [showModal, setShowModal] = useState(false);
  const [editingDb, setEditingDb] = useState<DatabaseConfig | null>(null);
  const [formData, setFormData] = useState({
    name: '',
    type: 'MySQL' as 'MySQL' | 'TDengine' | 'InfluxDB',
    host: '',
    port: 3306,
    database: '',
    username: '',
    password: '',
  });

  const handleAdd = () => {
    setEditingDb(null);
    setFormData({ name: '', type: 'MySQL', host: '', port: 3306, database: '', username: '', password: '' });
    setShowModal(true);
  };

  const handleEdit = (db: DatabaseConfig) => {
    setEditingDb(db);
    setFormData({
      name: db.name,
      type: db.type,
      host: db.host,
      port: db.port,
      database: db.database,
      username: db.username,
      password: '',
    });
    setShowModal(true);
  };

  const handleSave = () => {
    if (editingDb) {
      // 编辑现有数据库
      setDatabases(databases.map(db =>
        db.id === editingDb.id
          ? {
              ...db,
              name: formData.name,
              type: formData.type,
              host: formData.host,
              port: formData.port,
              database: formData.database,
              username: formData.username,
            }
          : db
      ));
    } else {
      // 添加新数据库
      const newDb: DatabaseConfig = {
        id: `db-${Date.now()}`,
        name: formData.name,
        type: formData.type,
        status: 'disconnected',
        host: formData.host,
        port: formData.port,
        database: formData.database,
        username: formData.username,
      };
      setDatabases([...databases, newDb]);
    }
    setShowModal(false);
    setFormData({ name: '', type: 'MySQL', host: '', port: 3306, database: '', username: '', password: '' });
  };

  const handleTest = (id: string) => {
    alert('连接测试成功！');
  };

  const handleDelete = (id: string) => {
    if (confirm('确定要删除此数据库配置吗？')) {
      setDatabases(databases.filter(db => db.id !== id));
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
          onClick={() => setShowModal(true)}
          className="px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded-lg flex items-center gap-2 transition-colors"
        >
          <Plus className="w-4 h-4" />
          <span>添加数据库</span>
        </button>
      </div>

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
                    <span className="text-white ml-1">{db.type}</span>
                  </div>
                  <div>
                    <span className="text-gray-400">主机：</span>
                    <span className="text-white ml-1">{db.host}:{db.port}</span>
                  </div>
                  <div>
                    <span className="text-gray-400">数据库：</span>
                    <span className="text-white ml-1">{db.database}</span>
                  </div>
                  <div>
                    <span className="text-gray-400">用户名：</span>
                    <span className="text-white ml-1">{db.username}</span>
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

      {/* 添加数据库弹窗 */}
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
                    value={formData.type}
                    onChange={(e) => {
                      const type = e.target.value as any;
                      setFormData({
                        ...formData,
                        type,
                        port: type === 'MySQL' ? 3306 : type === 'TDengine' ? 6030 : 8086
                      });
                    }}
                    className="w-full px-4 py-2 bg-gray-900 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-blue-500"
                  >
                    <option>MySQL</option>
                    <option>TDengine</option>
                    <option>InfluxDB</option>
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
                    value={formData.database}
                    onChange={(e) => setFormData({ ...formData, database: e.target.value })}
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
                  <label className="block text-sm font-medium text-gray-400 mb-2">密码</label>
                  <input
                    type="password"
                    value={formData.password}
                    onChange={(e) => setFormData({ ...formData, password: e.target.value })}
                    placeholder="••••••••"
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

// ========== 9. 数据导出 ==========
export function DataExportPage({ devices, getEnergyIcon }: { devices: any[], getEnergyIcon: (type: any) => JSX.Element }) {
  const [exportHistory] = useState([
    { id: 1, name: '2026年3月能耗数据', date: '2026-03-23', format: 'Excel', size: '2.3 MB' },
    { id: 2, name: '2026年2月能耗数据', date: '2026-02-28', format: 'CSV', size: '1.8 MB' },
  ]);

  const handleExport = () => {
    alert('数据导出功能开发中...\n导出参数已配置完成，即将生成文件。');
  };

  return (
    <div className="p-6 space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-white flex items-center gap-2">
          <ClipboardCheck className="w-7 h-7 text-green-500" />
          数据导出
        </h1>
        <p className="text-sm text-gray-400 mt-1">
          数据导出（Excel、CSV、JSON），已接入 {devices.length} 台设备
        </p>
      </div>

      <div className="bg-gray-800 border border-gray-700 rounded-lg p-6">
        <h3 className="text-lg font-semibold text-white mb-4">导出配置</h3>
        
        <div className="space-y-4">
          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-sm font-medium text-gray-400 mb-2">开始日期</label>
              <input
                type="date"
                defaultValue="2026-03-01"
                className="w-full px-4 py-2 bg-gray-900 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-blue-500"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-400 mb-2">结束日期</label>
              <input
                type="date"
                defaultValue="2026-03-23"
                className="w-full px-4 py-2 bg-gray-900 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-blue-500"
              />
            </div>
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-400 mb-2">能源类型</label>
            <div className="flex gap-4">
              <label className="flex items-center gap-2 text-white cursor-pointer">
                <input type="checkbox" defaultChecked className="rounded" />
                <Zap className="w-4 h-4 text-yellow-500" />
                <span>电</span>
              </label>
              <label className="flex items-center gap-2 text-white cursor-pointer">
                <input type="checkbox" defaultChecked className="rounded" />
                <Droplet className="w-4 h-4 text-blue-500" />
                <span>水</span>
              </label>
              <label className="flex items-center gap-2 text-white cursor-pointer">
                <input type="checkbox" defaultChecked className="rounded" />
                <Flame className="w-4 h-4 text-orange-500" />
                <span>气</span>
              </label>
            </div>
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-400 mb-2">导出格式</label>
            <div className="flex gap-4">
              <label className="flex items-center gap-2 text-white cursor-pointer">
                <input type="radio" name="format" value="excel" defaultChecked />
                <span>Excel (.xlsx)</span>
              </label>
              <label className="flex items-center gap-2 text-white cursor-pointer">
                <input type="radio" name="format" value="csv" />
                <span>CSV (.csv)</span>
              </label>
              <label className="flex items-center gap-2 text-white cursor-pointer">
                <input type="radio" name="format" value="json" />
                <span>JSON (.json)</span>
              </label>
            </div>
          </div>

          <button
            onClick={handleExport}
            className="w-full px-6 py-3 bg-gradient-to-r from-blue-600 to-purple-600 hover:from-blue-700 hover:to-purple-700 text-white rounded-lg flex items-center justify-center gap-2 transition-all"
          >
            <Download className="w-5 h-5" />
            <span>导出数据</span>
          </button>
        </div>
      </div>

      {/* 导出历史 */}
      <div className="bg-gray-800 border border-gray-700 rounded-lg p-6">
        <h3 className="text-lg font-semibold text-white mb-4">导出历史</h3>
        <div className="space-y-2">
          {exportHistory.map((item) => (
            <div key={item.id} className="flex items-center justify-between p-3 bg-gray-900/50 rounded-lg hover:bg-gray-700/30 transition-colors">
              <div className="flex items-center gap-3">
                <FileJson className="w-5 h-5 text-green-500" />
                <div>
                  <p className="text-white font-medium">{item.name}</p>
                  <p className="text-sm text-gray-400">{item.date} • {item.format} • {item.size}</p>
                </div>
              </div>
              <button className="p-2 bg-blue-500/20 hover:bg-blue-500/30 text-blue-300 rounded-lg transition-colors">
                <Download className="w-4 h-4" />
              </button>
            </div>
          ))}
        </div>
      </div>

      <div className="bg-blue-500/10 border border-blue-500/30 rounded-lg p-4">
        <div className="flex items-start gap-3">
          <AlertCircle className="w-5 h-5 text-blue-400 flex-shrink-0 mt-0.5" />
          <div className="text-sm text-blue-300">
            <p className="font-medium mb-1">导出说明</p>
            <ul className="space-y-1 text-blue-300/80">
              <li>• 当前系统已接入 {devices.length} 台设备</li>
              <li>• Excel格式支持数据透视表和图表</li>
              <li>• CSV格式适合二次数据分析</li>
              <li>• JSON格式适合程序化处理</li>
            </ul>
          </div>
        </div>
      </div>
    </div>
  );
}