import { useState } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import {
  BookOpen,
  Plus,
  Play,
  Pause,
  Edit,
  Trash2,
  X,
  Settings,
  ArrowRight,
  Code2,
  Filter,
  Calculator,
  GitMerge,
  FileType,
} from 'lucide-react';

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

export function DataTransformPageNew() {
  const [tasks, setTasks] = useState<ETLTask[]>([
    {
      id: 'etl-001',
      name: '能耗数据ETL',
      source: 'Modbus TCP',
      target: 'MySQL',
      status: 'running',
      lastRun: '2026-03-23 14:30:00',
      recordsProcessed: 15847,
      fieldMapping: [
        { id: 'fm-1', sourceField: 'register_40001', targetField: 'power_consumption', dataType: 'FLOAT', transform: 'value / 1000' },
        { id: 'fm-2', sourceField: 'register_40002', targetField: 'voltage', dataType: 'INT', transform: 'value' },
      ],
      transformRules: [
        { id: 'tr-1', type: 'calculate', name: '功率计算', description: '根据电压和电流计算功率', config: { formula: 'voltage * current' } },
        { id: 'tr-2', type: 'filter', name: '数据过滤', description: '过滤异常数据', config: { condition: 'value > 0 && value < 10000' } },
      ],
    },
    {
      id: 'etl-002',
      name: 'MQTT传感器数据ETL',
      source: 'MQTT',
      target: 'MySQL',
      status: 'running',
      lastRun: '2026-03-23 14:35:10',
      recordsProcessed: 23456,
      fieldMapping: [
        { id: 'fm-3', sourceField: 'payload.temperature', targetField: 'temp', dataType: 'FLOAT', transform: 'value' },
        { id: 'fm-4', sourceField: 'payload.humidity', targetField: 'humidity', dataType: 'FLOAT', transform: 'value' },
        { id: 'fm-5', sourceField: 'payload.deviceId', targetField: 'device_id', dataType: 'STRING', transform: 'value' },
        { id: 'fm-6', sourceField: 'timestamp', targetField: 'ts', dataType: 'INT', transform: 'Date.parse(value)' },
      ],
      transformRules: [
        { id: 'tr-3', type: 'format', name: 'JSON解析', description: '解析MQTT消息体JSON格式', config: { inputFormat: 'JSON', outputFormat: 'TABLE' } },
        { id: 'tr-4', type: 'filter', name: '温度范围过滤', description: '过滤温度异常数据', config: { condition: 'temperature >= -40 && temperature <= 125' } },
        { id: 'tr-5', type: 'calculate', name: '时间戳转换', description: '转换为Unix时间戳', config: { formula: 'Date.now()' } },
      ],
    },
  ]);

  const [showModal, setShowModal] = useState(false);
  const [showConfigModal, setShowConfigModal] = useState(false);
  const [editingTask, setEditingTask] = useState<ETLTask | null>(null);
  const [selectedTask, setSelectedTask] = useState<ETLTask | null>(null);
  
  const [formData, setFormData] = useState({
    name: '',
    source: 'Modbus TCP',
    target: 'MySQL',
  });

  // 字段映射表单
  const [mappingForm, setMappingForm] = useState({
    sourceField: '',
    targetField: '',
    dataType: 'STRING',
    transform: 'value',
  });

  // 转换规则表单
  const [ruleForm, setRuleForm] = useState({
    type: 'filter' as 'format' | 'calculate' | 'filter' | 'merge',
    name: '',
    description: '',
    config: '',
  });

  const handleAdd = () => {
    const newTask: ETLTask = {
      id: `etl-${Date.now()}`,
      ...formData,
      status: 'stopped',
      lastRun: new Date().toLocaleString('zh-CN'),
      recordsProcessed: 0,
      fieldMapping: [],
      transformRules: [],
    };
    setTasks([...tasks, newTask]);
    setShowModal(false);
    setFormData({ name: '', source: 'Modbus TCP', target: 'MySQL' });
  };

  const handleEdit = (task: ETLTask) => {
    setEditingTask(task);
    setFormData({
      name: task.name,
      source: task.source,
      target: task.target,
    });
    setShowModal(true);
  };

  const handleUpdate = () => {
    if (editingTask) {
      setTasks(tasks.map(t => 
        t.id === editingTask.id 
          ? { ...t, ...formData }
          : t
      ));
      setShowModal(false);
      setEditingTask(null);
      setFormData({ name: '', source: 'Modbus TCP', target: 'MySQL' });
    }
  };

  const handleConfigure = (task: ETLTask) => {
    setSelectedTask(task);
    setShowConfigModal(true);
  };

  const handleAddMapping = () => {
    if (selectedTask && mappingForm.sourceField && mappingForm.targetField) {
      const newMapping: FieldMapping = {
        id: `fm-${Date.now()}`,
        ...mappingForm,
      };
      
      const updatedTask = {
        ...selectedTask,
        fieldMapping: [...(selectedTask.fieldMapping || []), newMapping],
      };
      
      setTasks(tasks.map(t => t.id === selectedTask.id ? updatedTask : t));
      setSelectedTask(updatedTask);
      setMappingForm({ sourceField: '', targetField: '', dataType: 'STRING', transform: 'value' });
    }
  };

  const handleDeleteMapping = (mappingId: string) => {
    if (selectedTask) {
      const updatedTask = {
        ...selectedTask,
        fieldMapping: selectedTask.fieldMapping?.filter(m => m.id !== mappingId),
      };
      setTasks(tasks.map(t => t.id === selectedTask.id ? updatedTask : t));
      setSelectedTask(updatedTask);
    }
  };

  const handleAddRule = () => {
    if (selectedTask && ruleForm.name && ruleForm.config) {
      let config;
      try {
        config = JSON.parse(ruleForm.config);
      } catch {
        config = { value: ruleForm.config };
      }
      
      const newRule: TransformRule = {
        id: `tr-${Date.now()}`,
        type: ruleForm.type,
        name: ruleForm.name,
        description: ruleForm.description,
        config,
      };
      
      const updatedTask = {
        ...selectedTask,
        transformRules: [...(selectedTask.transformRules || []), newRule],
      };
      
      setTasks(tasks.map(t => t.id === selectedTask.id ? updatedTask : t));
      setSelectedTask(updatedTask);
      setRuleForm({ type: 'filter', name: '', description: '', config: '' });
    }
  };

  const handleDeleteRule = (ruleId: string) => {
    if (selectedTask) {
      const updatedTask = {
        ...selectedTask,
        transformRules: selectedTask.transformRules?.filter(r => r.id !== ruleId),
      };
      setTasks(tasks.map(t => t.id === selectedTask.id ? updatedTask : t));
      setSelectedTask(updatedTask);
    }
  };

  const handleToggle = (id: string) => {
    setTasks(tasks.map(t =>
      t.id === id ? { ...t, status: t.status === 'running' ? 'stopped' : 'running' as any } : t
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
          onClick={() => {
            setEditingTask(null);
            setFormData({ name: '', source: 'Modbus TCP', target: 'MySQL' });
            setShowModal(true);
          }}
          className="px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded-lg flex items-center gap-2 transition-colors"
        >
          <Plus className="w-4 h-4" />
          <span>创建任务</span>
        </button>
      </div>

      <div className="space-y-4">
        {tasks.map((task) => (
          <div key={task.id} className="bg-gray-800 border border-gray-700 rounded-lg p-4">
            <div className="flex items-start justify-between mb-3">
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
                  title={task.status === 'running' ? '暂停' : '启动'}
                >
                  {task.status === 'running' ? <Pause className="w-4 h-4" /> : <Play className="w-4 h-4" />}
                </button>
                <button
                  onClick={() => handleConfigure(task)}
                  className="p-2 bg-purple-500/20 hover:bg-purple-500/30 text-purple-300 rounded-lg transition-colors"
                  title="配置转换规则"
                >
                  <Settings className="w-4 h-4" />
                </button>
                <button
                  onClick={() => handleEdit(task)}
                  className="p-2 bg-blue-500/20 hover:bg-blue-500/30 text-blue-300 rounded-lg transition-colors"
                  title="编辑"
                >
                  <Edit className="w-4 h-4" />
                </button>
                <button
                  onClick={() => handleDelete(task.id)}
                  className="p-2 bg-red-500/20 hover:bg-red-500/30 text-red-300 rounded-lg transition-colors"
                  title="删除"
                >
                  <Trash2 className="w-4 h-4" />
                </button>
              </div>
            </div>

            {/* 转换规则预览 */}
            {task.transformRules && task.transformRules.length > 0 && (
              <div className="mt-3 pt-3 border-t border-gray-700">
                <p className="text-sm text-gray-400 mb-2">转换规则：</p>
                <div className="flex flex-wrap gap-2">
                  {task.transformRules.map((rule) => (
                    <div key={rule.id} className="flex items-center gap-2 px-3 py-1.5 bg-gray-900/50 rounded-lg">
                      {rule.type === 'format' && <FileType className="w-3.5 h-3.5 text-blue-400" />}
                      {rule.type === 'calculate' && <Calculator className="w-3.5 h-3.5 text-green-400" />}
                      {rule.type === 'filter' && <Filter className="w-3.5 h-3.5 text-yellow-400" />}
                      {rule.type === 'merge' && <GitMerge className="w-3.5 h-3.5 text-purple-400" />}
                      <span className="text-xs text-gray-300">{rule.name}</span>
                    </div>
                  ))}
                </div>
              </div>
            )}

            {/* 字段映射预览 */}
            {task.fieldMapping && task.fieldMapping.length > 0 && (
              <div className="mt-3 pt-3 border-t border-gray-700">
                <p className="text-sm text-gray-400 mb-2">字段映射：</p>
                <div className="grid grid-cols-1 md:grid-cols-2 gap-2">
                  {task.fieldMapping.slice(0, 4).map((mapping) => (
                    <div key={mapping.id} className="flex items-center gap-2 px-3 py-1.5 bg-gray-900/50 rounded-lg text-xs">
                      <span className="text-blue-300">{mapping.sourceField}</span>
                      <ArrowRight className="w-3 h-3 text-gray-500" />
                      <span className="text-green-300">{mapping.targetField}</span>
                      {mapping.transform && mapping.transform !== 'value' && (
                        <code className="ml-auto text-orange-300 bg-orange-500/10 px-2 py-0.5 rounded">
                          {mapping.transform}
                        </code>
                      )}
                    </div>
                  ))}
                  {task.fieldMapping.length > 4 && (
                    <div className="text-xs text-gray-500 px-3 py-1.5">
                      还有 {task.fieldMapping.length - 4} 个映射...
                    </div>
                  )}
                </div>
              </div>
            )}
          </div>
        ))}
      </div>

      {/* 创建/编辑ETL任务弹窗 */}
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
                  {editingTask ? '编辑ETL任务' : '创建ETL任务'}
                </h3>
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
                    <option>CSV 文件</option>
                    <option>Excel 文件</option>
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
                    <option>JSON 文件</option>
                    <option>CSV 文件</option>
                  </select>
                </div>

                {!editingTask && (
                  <div className="bg-blue-500/10 border border-blue-500/30 rounded-lg p-3">
                    <p className="text-sm text-blue-300">
                      💡 创建后可通过"配置"按钮设置字段映射和转换规则
                    </p>
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
                    onClick={editingTask ? handleUpdate : handleAdd}
                    className="flex-1 px-6 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded-lg transition-colors"
                  >
                    {editingTask ? '保存' : '创建'}
                  </button>
                </div>
              </div>
            </motion.div>
          </motion.div>
        )}
      </AnimatePresence>

      {/* 配置转换规则弹窗 */}
      <AnimatePresence>
        {showConfigModal && selectedTask && (
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            className="fixed inset-0 bg-black/60 backdrop-blur-sm flex items-center justify-center z-50 p-4"
            onClick={() => setShowConfigModal(false)}
          >
            <motion.div
              initial={{ scale: 0.9, y: 20 }}
              animate={{ scale: 1, y: 0 }}
              exit={{ scale: 0.9, y: 20 }}
              onClick={(e) => e.stopPropagation()}
              className="bg-gray-800 border border-gray-700 rounded-xl max-w-5xl w-full max-h-[90vh] overflow-hidden flex flex-col"
            >
              <div className="flex items-center justify-between p-6 border-b border-gray-700">
                <h3 className="text-xl font-bold text-white">配置转换规则 - {selectedTask.name}</h3>
                <button onClick={() => setShowConfigModal(false)} className="p-2 hover:bg-gray-700 rounded-lg transition-colors">
                  <X className="w-5 h-5 text-gray-400" />
                </button>
              </div>

              <div className="p-6 overflow-y-auto flex-1 space-y-6">
                {/* 字段映射配置 */}
                <div>
                  <h4 className="text-lg font-semibold text-white mb-4 flex items-center gap-2">
                    <ArrowRight className="w-5 h-5 text-blue-500" />
                    字段映射
                  </h4>

                  {/* 添加字段映射表单 */}
                  <div className="bg-gray-900/50 border border-gray-700 rounded-lg p-4 mb-4">
                    <div className="grid grid-cols-4 gap-3">
                      <input
                        type="text"
                        value={mappingForm.sourceField}
                        onChange={(e) => setMappingForm({ ...mappingForm, sourceField: e.target.value })}
                        placeholder="源字段 (如: register_40001)"
                        className="px-3 py-2 bg-gray-800 border border-gray-700 rounded text-sm text-white focus:outline-none focus:border-blue-500"
                      />
                      <input
                        type="text"
                        value={mappingForm.targetField}
                        onChange={(e) => setMappingForm({ ...mappingForm, targetField: e.target.value })}
                        placeholder="目标字段 (如: power)"
                        className="px-3 py-2 bg-gray-800 border border-gray-700 rounded text-sm text-white focus:outline-none focus:border-blue-500"
                      />
                      <select
                        value={mappingForm.dataType}
                        onChange={(e) => setMappingForm({ ...mappingForm, dataType: e.target.value })}
                        className="px-3 py-2 bg-gray-800 border border-gray-700 rounded text-sm text-white focus:outline-none focus:border-blue-500"
                      >
                        <option>STRING</option>
                        <option>INT</option>
                        <option>FLOAT</option>
                        <option>BOOLEAN</option>
                      </select>
                      <div className="flex gap-2">
                        <input
                          type="text"
                          value={mappingForm.transform}
                          onChange={(e) => setMappingForm({ ...mappingForm, transform: e.target.value })}
                          placeholder="转换 (如: value/1000)"
                          className="flex-1 px-3 py-2 bg-gray-800 border border-gray-700 rounded text-sm text-orange-300 font-mono focus:outline-none focus:border-blue-500"
                        />
                        <button
                          onClick={handleAddMapping}
                          className="px-3 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded text-sm transition-colors"
                        >
                          <Plus className="w-4 h-4" />
                        </button>
                      </div>
                    </div>
                  </div>

                  {/* 字段映射列表 */}
                  <div className="space-y-2">
                    {selectedTask.fieldMapping && selectedTask.fieldMapping.length > 0 ? (
                      selectedTask.fieldMapping.map((mapping) => (
                        <div key={mapping.id} className="bg-gray-900/50 border border-gray-700 rounded-lg p-3 flex items-center justify-between">
                          <div className="grid grid-cols-4 gap-3 flex-1">
                            <div>
                              <span className="text-xs text-gray-400 block mb-1">源字段</span>
                              <span className="text-sm text-blue-300">{mapping.sourceField}</span>
                            </div>
                            <div>
                              <span className="text-xs text-gray-400 block mb-1">目标字段</span>
                              <span className="text-sm text-green-300">{mapping.targetField}</span>
                            </div>
                            <div>
                              <span className="text-xs text-gray-400 block mb-1">数据类型</span>
                              <span className="text-sm text-white">{mapping.dataType}</span>
                            </div>
                            <div>
                              <span className="text-xs text-gray-400 block mb-1">转换表达式</span>
                              <code className="text-sm text-orange-300">{mapping.transform}</code>
                            </div>
                          </div>
                          <button
                            onClick={() => handleDeleteMapping(mapping.id)}
                            className="ml-3 p-2 hover:bg-red-500/20 text-red-400 rounded transition-colors"
                          >
                            <Trash2 className="w-4 h-4" />
                          </button>
                        </div>
                      ))
                    ) : (
                      <div className="text-center py-6 text-gray-500">
                        暂无字段映射，请添加
                      </div>
                    )}
                  </div>
                </div>

                {/* 转换规则配置 */}
                <div>
                  <h4 className="text-lg font-semibold text-white mb-4 flex items-center gap-2">
                    <Code2 className="w-5 h-5 text-orange-500" />
                    转换规则
                  </h4>

                  {/* 添加转换规则表单 */}
                  <div className="bg-gray-900/50 border border-gray-700 rounded-lg p-4 mb-4">
                    <div className="grid grid-cols-2 gap-3 mb-3">
                      <select
                        value={ruleForm.type}
                        onChange={(e) => setRuleForm({ ...ruleForm, type: e.target.value as any })}
                        className="px-3 py-2 bg-gray-800 border border-gray-700 rounded text-sm text-white focus:outline-none focus:border-blue-500"
                      >
                        <option value="filter">过滤规则</option>
                        <option value="calculate">计算规则</option>
                        <option value="format">格式转换</option>
                        <option value="merge">数据合并</option>
                      </select>
                      <input
                        type="text"
                        value={ruleForm.name}
                        onChange={(e) => setRuleForm({ ...ruleForm, name: e.target.value })}
                        placeholder="规则名称"
                        className="px-3 py-2 bg-gray-800 border border-gray-700 rounded text-sm text-white focus:outline-none focus:border-blue-500"
                      />
                    </div>
                    <input
                      type="text"
                      value={ruleForm.description}
                      onChange={(e) => setRuleForm({ ...ruleForm, description: e.target.value })}
                      placeholder="规则描述"
                      className="w-full px-3 py-2 bg-gray-800 border border-gray-700 rounded text-sm text-white mb-3 focus:outline-none focus:border-blue-500"
                    />
                    <div className="flex gap-2">
                      <input
                        type="text"
                        value={ruleForm.config}
                        onChange={(e) => setRuleForm({ ...ruleForm, config: e.target.value })}
                        placeholder='配置 JSON (如: {"condition": "value > 0"})'
                        className="flex-1 px-3 py-2 bg-gray-800 border border-gray-700 rounded text-sm text-orange-300 font-mono focus:outline-none focus:border-blue-500"
                      />
                      <button
                        onClick={handleAddRule}
                        className="px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded text-sm transition-colors"
                      >
                        <Plus className="w-4 h-4" />
                      </button>
                    </div>
                  </div>

                  {/* 转换规则列表 */}
                  <div className="grid grid-cols-2 gap-3">
                    {selectedTask.transformRules && selectedTask.transformRules.length > 0 ? (
                      selectedTask.transformRules.map((rule) => (
                        <div key={rule.id} className="bg-gray-900/50 border border-gray-700 rounded-lg p-4">
                          <div className="flex items-start justify-between mb-2">
                            <div className="flex items-center gap-2">
                              {rule.type === 'format' && <FileType className="w-4 h-4 text-blue-400" />}
                              {rule.type === 'calculate' && <Calculator className="w-4 h-4 text-green-400" />}
                              {rule.type === 'filter' && <Filter className="w-4 h-4 text-yellow-400" />}
                              {rule.type === 'merge' && <GitMerge className="w-4 h-4 text-purple-400" />}
                              <h5 className="font-semibold text-white text-sm">{rule.name}</h5>
                            </div>
                            <button
                              onClick={() => handleDeleteRule(rule.id)}
                              className="p-1 hover:bg-gray-800 rounded transition-colors"
                            >
                              <Trash2 className="w-3.5 h-3.5 text-gray-400" />
                            </button>
                          </div>
                          <p className="text-xs text-gray-400 mb-2">{rule.description}</p>
                          <div className="bg-gray-800 border border-gray-700 rounded px-2 py-1.5">
                            <code className="text-xs text-orange-300">
                              {JSON.stringify(rule.config)}
                            </code>
                          </div>
                        </div>
                      ))
                    ) : (
                      <div className="col-span-2 text-center py-6 text-gray-500">
                        暂无转换规则，请添加
                      </div>
                    )}
                  </div>
                </div>

                {/* 数据流预览 */}
                <div className="bg-gray-900/50 border border-gray-700 rounded-lg p-4">
                  <h4 className="text-sm font-semibold text-white mb-3 flex items-center gap-2">
                    <ArrowRight className="w-4 h-4 text-blue-500" />
                    数据流预览
                  </h4>
                  <div className="flex items-center justify-between gap-4">
                    <div className="flex-1 bg-blue-500/10 border border-blue-500/30 rounded-lg p-3">
                      <p className="text-xs text-blue-400 mb-1">数据���</p>
                      <p className="text-sm text-white font-medium">{selectedTask.source}</p>
                      <p className="text-xs text-gray-400 mt-1">{selectedTask.fieldMapping?.length || 0} 个字段</p>
                    </div>
                    <ArrowRight className="w-6 h-6 text-gray-500" />
                    <div className="flex-1 bg-orange-500/10 border border-orange-500/30 rounded-lg p-3">
                      <p className="text-xs text-orange-400 mb-1">转换处理</p>
                      <p className="text-sm text-white font-medium">
                        {selectedTask.transformRules?.length || 0} 条规则
                      </p>
                      <p className="text-xs text-gray-400 mt-1">映射 + 转换</p>
                    </div>
                    <ArrowRight className="w-6 h-6 text-gray-500" />
                    <div className="flex-1 bg-green-500/10 border border-green-500/30 rounded-lg p-3">
                      <p className="text-xs text-green-400 mb-1">目标</p>
                      <p className="text-sm text-white font-medium">{selectedTask.target}</p>
                      <p className="text-xs text-gray-400 mt-1">已处理 {selectedTask.recordsProcessed.toLocaleString()} 条</p>
                    </div>
                  </div>
                </div>
              </div>

              <div className="flex gap-3 p-6 border-t border-gray-700">
                <button
                  onClick={() => setShowConfigModal(false)}
                  className="flex-1 px-6 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded-lg transition-colors"
                >
                  完成配置
                </button>
              </div>
            </motion.div>
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}