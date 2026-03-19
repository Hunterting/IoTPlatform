import { useState } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import { X, Plus, Trash2, Upload, Zap, List, Check, FileText, AlertCircle } from 'lucide-react';

interface DeviceInventoryItem {
  id: string;
  name: string;
  type: string;
  model: string;
  specification: string;
  serialNumber: string;
  sensors: string[];
}

interface DeviceInventoryModalProps {
  isOpen: boolean;
  onClose: () => void;
  onSubmit: (devices: DeviceInventoryItem[]) => void;
}

const availableSensors = [
  '用气传感器',
  '用水传感器',
  '温度传感器',
  '湿度传感器',
  '烟雾传感器',
  '压力传感器',
];

const deviceTypes = [
  '冷藏设备',
  '烹饪设备',
  '清洁设备',
  '饮品设备',
  '加工设备',
  '存储设备',
];

export function DeviceInventoryModal({ isOpen, onClose, onSubmit }: DeviceInventoryModalProps) {
  const [devices, setDevices] = useState<DeviceInventoryItem[]>([
    {
      id: '1',
      name: '',
      type: '',
      model: '',
      specification: '',
      serialNumber: '',
      sensors: [],
    },
  ]);
  const [activeTab, setActiveTab] = useState<'single' | 'batch'>('single');
  const [batchText, setBatchText] = useState('');

  const addDevice = () => {
    setDevices([
      ...devices,
      {
        id: Date.now().toString(),
        name: '',
        type: '',
        model: '',
        specification: '',
        serialNumber: '',
        sensors: [],
      },
    ]);
  };

  const removeDevice = (id: string) => {
    if (devices.length > 1) {
      setDevices(devices.filter((d) => d.id !== id));
    }
  };

  const updateDevice = (id: string, field: keyof DeviceInventoryItem, value: any) => {
    setDevices(
      devices.map((d) =>
        d.id === id
          ? { ...d, [field]: value }
          : d
      )
    );
  };

  const toggleSensor = (deviceId: string, sensor: string) => {
    setDevices(
      devices.map((d) => {
        if (d.id === deviceId) {
          const sensors = d.sensors.includes(sensor)
            ? d.sensors.filter((s) => s !== sensor)
            : [...d.sensors, sensor];
          return { ...d, sensors };
        }
        return d;
      })
    );
  };

  const handleSubmit = () => {
    const validDevices = devices.filter(
      (d) => d.name && d.type && d.model && d.serialNumber
    );
    
    if (validDevices.length === 0) {
      alert('请至少填写一个完整的设备信息');
      return;
    }
    
    onSubmit(validDevices);
  };

  const handleParseBatch = () => {
    if (!batchText.trim()) {
      alert('请输入设备清单数据');
      return;
    }

    try {
      const lines = batchText.trim().split('\n');
      const newDevices: DeviceInventoryItem[] = lines
        .filter(line => line.trim())
        .map((line, index) => {
          // Format: Name, Type, Model, Spec, SN, Sensors
          const parts = line.split(/[,，]/).map(p => p.trim());
          
          // Basic validation/mapping
          let type = parts[1] || '';
          // Try to match type to known types
          const matchedType = deviceTypes.find(t => type.includes(t.replace('设备', ''))) || type || '烹饪设备';

          // Parse sensors (space separated in last field if available)
          const potentialSensors = parts[5] ? parts[5].split(/[ ;、]/) : [];
          const matchedSensors = potentialSensors.filter(s => 
            availableSensors.some(as => as.includes(s) || s.includes(as.replace('传感器', '')))
          ).map(s => {
             // Normalize sensor name
             return availableSensors.find(as => as.includes(s) || s.includes(as.replace('传感器', ''))) || s;
          });

          return {
            id: Date.now().toString() + index,
            name: parts[0] || `设备-${index + 1}`,
            type: matchedType,
            model: parts[2] || '',
            specification: parts[3] || '',
            serialNumber: parts[4] || `SN${Date.now()}${index}`,
            sensors: matchedSensors,
          };
        });

      if (newDevices.length > 0) {
        setDevices(newDevices);
        setActiveTab('single');
        setBatchText(''); // Clear input
      } else {
        alert('无法解析数据，请检查格式');
      }
    } catch (e) {
      alert('解析出错，请检查数据格式');
    }
  };

  const loadSampleData = () => {
    const sample = `智能冰箱, 冷藏设备, SR-F520BX, 520L 双门, SN20250101001, 温度传感器 湿度传感器
智能烤箱, 烹饪设备, OV-X8000, 80L 嵌入式, SN20250101002, 温度传感器
智能炉灶, 烹饪设备, GS-P4000, 四口燃气灶, SN20250101003, 用气传感器 温度传感器`;
    setBatchText(sample);
  };

  if (!isOpen) return null;

  return (
    <motion.div
      initial={{ opacity: 0 }}
      animate={{ opacity: 1 }}
      exit={{ opacity: 0 }}
      className="fixed inset-0 bg-black/80 backdrop-blur-sm flex items-center justify-center z-50 p-4"
      onClick={onClose}
    >
      <motion.div
        initial={{ scale: 0.9, y: 20 }}
        animate={{ scale: 1, y: 0 }}
        exit={{ scale: 0.9, y: 20 }}
        onClick={(e) => e.stopPropagation()}
        className="bg-gray-800 border border-white/10 rounded-xl max-w-6xl w-full max-h-[90vh] flex flex-col"
      >
        {/* Header */}
        <div className="flex items-center justify-between p-6 border-b border-white/10">
          <div className="flex items-center gap-3">
            <List className="w-6 h-6 text-green-400" />
            <div>
              <h3 className="text-2xl font-bold text-white">设备清单录入</h3>
              <p className="text-sm text-gray-400">批量或单个录入厨房设备清单信息</p>
            </div>
          </div>
          <button
            onClick={onClose}
            className="p-2 hover:bg-white/10 rounded-lg transition-colors"
          >
            <X className="w-5 h-5 text-gray-400" />
          </button>
        </div>

        {/* Tabs */}
        <div className="flex gap-2 px-6 pt-4">
          <button
            onClick={() => setActiveTab('single')}
            className={`px-4 py-2 rounded-lg transition-all ${
              activeTab === 'single'
                ? 'bg-gradient-to-r from-blue-500 to-purple-500 text-white'
                : 'bg-white/5 text-gray-400 hover:bg-white/10'
            }`}
          >
            单个录入
          </button>
          <button
            onClick={() => setActiveTab('batch')}
            className={`px-4 py-2 rounded-lg transition-all ${
              activeTab === 'batch'
                ? 'bg-gradient-to-r from-blue-500 to-purple-500 text-white'
                : 'bg-white/5 text-gray-400 hover:bg-white/10'
            }`}
          >
            批量导入
          </button>
        </div>

        {/* Content */}
        <div className="flex-1 overflow-y-auto p-6">
          {activeTab === 'single' ? (
            <div className="space-y-6">
              {devices.map((device, index) => (
                <motion.div
                  key={device.id}
                  initial={{ opacity: 0, y: 20 }}
                  animate={{ opacity: 1, y: 0 }}
                  transition={{ delay: index * 0.1 }}
                  className="bg-white/5 border border-white/10 rounded-xl p-6"
                >
                  <div className="flex items-center justify-between mb-4">
                    <h4 className="text-lg font-bold text-white">设备 #{index + 1}</h4>
                    {devices.length > 1 && (
                      <button
                        onClick={() => removeDevice(device.id)}
                        className="p-2 hover:bg-red-500/20 text-red-400 rounded-lg transition-colors"
                      >
                        <Trash2 className="w-4 h-4" />
                      </button>
                    )}
                  </div>

                  <div className="grid grid-cols-2 gap-4">
                    {/* Device Name */}
                    <div>
                      <label className="text-sm text-gray-400 mb-1 block">设备名称 *</label>
                      <input
                        type="text"
                        value={device.name}
                        onChange={(e) => updateDevice(device.id, 'name', e.target.value)}
                        placeholder="例如：智能冰箱"
                        className="w-full px-4 py-2 bg-white/5 border border-white/10 rounded-lg text-white placeholder-gray-500 focus:outline-none focus:border-blue-500 transition-colors"
                      />
                    </div>

                    {/* Device Type */}
                    <div>
                      <label className="text-sm text-gray-400 mb-1 block">设备类型 *</label>
                      <select
                        value={device.type}
                        onChange={(e) => updateDevice(device.id, 'type', e.target.value)}
                        className="w-full px-4 py-2 bg-white/5 border border-white/10 rounded-lg text-white focus:outline-none focus:border-blue-500 transition-colors [&>option]:bg-gray-800 [&>option]:text-white"
                      >
                        <option value="" className="bg-gray-800 text-white">请选择类型</option>
                        {deviceTypes.map((type) => (
                          <option key={type} value={type} className="bg-gray-800 text-white">
                            {type}
                          </option>
                        ))}
                      </select>
                    </div>

                    {/* Model */}
                    <div>
                      <label className="text-sm text-gray-400 mb-1 block">型号 *</label>
                      <input
                        type="text"
                        value={device.model}
                        onChange={(e) => updateDevice(device.id, 'model', e.target.value)}
                        placeholder="例如：SR-F520BX"
                        className="w-full px-4 py-2 bg-white/5 border border-white/10 rounded-lg text-white placeholder-gray-500 focus:outline-none focus:border-blue-500 transition-colors"
                      />
                    </div>

                    {/* Specification */}
                    <div>
                      <label className="text-sm text-gray-400 mb-1 block">规格</label>
                      <input
                        type="text"
                        value={device.specification}
                        onChange={(e) => updateDevice(device.id, 'specification', e.target.value)}
                        placeholder="例如：520L 双门"
                        className="w-full px-4 py-2 bg-white/5 border border-white/10 rounded-lg text-white placeholder-gray-500 focus:outline-none focus:border-blue-500 transition-colors"
                      />
                    </div>

                    {/* Serial Number */}
                    <div className="col-span-2">
                      <label className="text-sm text-gray-400 mb-1 block">设备编号 *</label>
                      <input
                        type="text"
                        value={device.serialNumber}
                        onChange={(e) => updateDevice(device.id, 'serialNumber', e.target.value)}
                        placeholder="例如：SN20250101001"
                        className="w-full px-4 py-2 bg-white/5 border border-white/10 rounded-lg text-white placeholder-gray-500 focus:outline-none focus:border-blue-500 transition-colors"
                      />
                    </div>

                    {/* Sensors */}
                    <div className="col-span-2">
                      <label className="text-sm text-gray-400 mb-2 block">关联传感器</label>
                      <div className="grid grid-cols-3 gap-2">
                        {availableSensors.map((sensor) => (
                          <button
                            key={sensor}
                            onClick={() => toggleSensor(device.id, sensor)}
                            className={`px-3 py-2 rounded-lg text-sm transition-all ${
                              device.sensors.includes(sensor)
                                ? 'bg-green-500/20 text-green-400 border border-green-500/50'
                                : 'bg-white/5 text-gray-400 border border-white/10 hover:bg-white/10'
                            }`}
                          >
                            <div className="flex items-center gap-2">
                              {device.sensors.includes(sensor) && (
                                <Check className="w-3 h-3" />
                                
                              )}
                              <span>{sensor}</span>
                            </div>
                          </button>
                        ))}
                      </div>
                    </div>
                  </div>
                </motion.div>
              ))}

              {/* Add Device Button */}
              <button
                onClick={addDevice}
                className="w-full py-3 border-2 border-dashed border-white/20 rounded-xl text-gray-400 hover:border-blue-500 hover:text-blue-400 transition-all flex items-center justify-center gap-2"
              >
                <Plus className="w-5 h-5" />
                <span>添加设备</span>
              </button>
            </div>
          ) : (
            <div className="space-y-6">
              <div className="bg-white/5 border border-white/10 rounded-xl p-6">
                <div className="flex items-center justify-between mb-4">
                   <h4 className="text-lg font-bold text-white flex items-center gap-2">
                     <FileText className="w-5 h-5 text-blue-400" />
                     <span>文本批量导入</span>
                   </h4>
                   <button 
                     onClick={loadSampleData}
                     className="text-sm text-blue-400 hover:text-blue-300 transition-colors"
                   >
                     加载示例数据
                   </button>
                </div>
                
                <textarea
                  value={batchText}
                  onChange={(e) => setBatchText(e.target.value)}
                  placeholder="每行一个设备，格式：名称, 类型, 型号, 规格, 编号, 传感器(可选)"
                  className="w-full h-64 p-4 bg-gray-900/50 border border-white/10 rounded-lg text-white placeholder-gray-500 focus:outline-none focus:border-blue-500 transition-colors font-mono text-sm leading-relaxed resize-none"
                />
                
                <div className="mt-4 flex justify-end">
                  <button
                    onClick={handleParseBatch}
                    className="px-6 py-3 bg-gradient-to-r from-blue-500 to-purple-500 hover:from-blue-600 hover:to-purple-600 rounded-lg text-white transition-all flex items-center gap-2"
                  >
                    <Zap className="w-4 h-4" />
                    <span>解析并生成清单</span>
                  </button>
                </div>
              </div>

              <div className="bg-blue-500/10 border border-blue-500/30 rounded-xl p-4">
                <h5 className="text-white font-bold mb-2 flex items-center gap-2">
                  <AlertCircle className="w-4 h-4 text-blue-400" />
                  导入格式说明
                </h5>
                <ul className="text-sm text-gray-300 space-y-1">
                  <li>• 每行代表一个设备</li>
                  <li>• 使用逗号分隔字段：名称, 类型, 型号, 规格, 编号, 传感器</li>
                  <li>• 传感器可以多个，使用空格分隔</li>
                  <li>• 例如：智能冰箱, 冷藏设备, SR-F520BX, 520L, SN001, 温度传感器</li>
                </ul>
              </div>
            </div>
          )}
        </div>

        {/* Footer */}
        <div className="flex items-center justify-between p-6 border-t border-white/10">
          <button
            onClick={onClose}
            className="px-6 py-3 bg-white/5 hover:bg-white/10 border border-white/10 rounded-lg text-white transition-all"
          >
            取消
          </button>
          <div className="flex items-center gap-3">
            <div className="text-sm text-gray-400">
              共 {devices.filter((d) => d.name && d.type && d.model && d.serialNumber).length} 个有效设备
            </div>
            <button
              onClick={handleSubmit}
              className="px-6 py-3 bg-gradient-to-r from-green-500 to-emerald-500 hover:from-green-600 hover:to-emerald-600 rounded-lg text-white transition-all flex items-center gap-2"
            >
              <Zap className="w-4 h-4" />
              <span>生成3D模型</span>
            </button>
          </div>
        </div>
      </motion.div>
    </motion.div>
  );
}