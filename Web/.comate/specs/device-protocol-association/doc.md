# 协议配置设备关联功能需求

## 需求背景
当前协议配置页面只能显示接入设备的数量（如"8台"），但无法查看具体是哪些设备接入。用户需要清楚地知道每个协议配置对应哪些设备，以便进行设备管理、故障排查和协议优化。

## 需求场景
1. 管理员需要查看"Modbus TCP主站"协议具体接入了哪些设备
2. 需要能够将设备关联到协议配置
3. 需要能够从协议配置中移除设备
4. 需要查看每个设备在协议中的状态（在线/离线/采集异常）

## 架构技术方案

### 数据模型扩展

**扩展 ProtocolConfig 接口：**
```typescript
interface ProtocolConfig {
  id: string;
  name: string;
  type: string;
  status: 'running' | 'stopped' | 'error';
  deviceCount: number;
  deviceIds: string[];  // 新增：关联的设备ID列表
  host?: string;
  port?: number;
  endpoint?: string;
  topic?: string;
  lastSync: string;
}
```

**扩展 DeviceItem 接口（如果需要存储协议信息）：**
```typescript
interface DeviceItem {
  // ... 现有字段
  protocolId?: string;  // 新增：关联的协议ID（可选，视业务需求）
  protocolType?: string;  // 新增：协议类型
}
```

### 功能实现方案

#### 方案A：在协议卡片中展开查看设备列表
- 每个协议卡片添加"查看设备"按钮
- 点击后展开显示该协议关联的设备列表
- 设备列表显示：设备名称、序列号、状态、最后采集时间

#### 方案B：在协议配置弹窗中添加设备选择器
- 编辑/添加协议时，提供多选设备列表
- 支持按能源类型、区域、状态筛选设备
- 已选设备实时显示在配置中

#### 方案C：独立的协议设备管理视图
- 为每个协议提供详细的设备管理页面
- 支持设备的批量关联/移除
- 显示设备采集状态和数据质量指标

**推荐方案：方案A + 方案B 的组合**
- 方案A满足快速查看需求
- 方案B满足设备关联配置需求

## 影响文件

### 需要修改的文件
1. **src/app/pages/DataCollectionPage.tsx**
   - 修改 `ProtocolConfig` 接口定义
   - 修改协议卡片UI，添加设备列表展示
   - 修改协议配置弹窗，添加设备选择器
   - 添加设备关联/移除的逻辑处理

2. **src/app/contexts/DeviceContext.tsx**
   - 可能需要扩展 `DeviceItem` 接口（如果选择存储协议信息）

### 不需要修改的文件
- 其他页面和组件不受影响

## 实现细节

### 1. 协议卡片UI改进

```typescript
// 在协议卡片底部添加设备列表展开功能
<div className="mt-4 pt-4 border-t border-gray-700">
  <button 
    onClick={() => toggleDeviceList(protocol.id)}
    className="flex items-center gap-2 text-sm text-blue-400 hover:text-blue-300"
  >
    <Eye className="w-4 h-4" />
    查看关联设备
    <ChevronDown className={`w-4 h-4 transition-transform ${showDeviceList ? 'rotate-180' : ''}`} />
  </button>
  
  <AnimatePresence>
    {showDeviceList && (
      <motion.div 
        initial={{ opacity: 0, height: 0 }}
        animate={{ opacity: 1, height: 'auto' }}
        exit={{ opacity: 0, height: 0 }}
        className="mt-3 space-y-2"
      >
        {protocolDevices.map(device => (
          <div key={device.id} className="bg-gray-900/50 rounded-lg p-3 flex items-center justify-between">
            <div className="flex items-center gap-3">
              <div>
                <p className="text-white text-sm font-medium">{device.name}</p>
                <p className="text-gray-400 text-xs">{device.serialNumber}</p>
              </div>
              <span className={`px-2 py-0.5 rounded text-xs ${
                device.status === 'online' 
                  ? 'bg-green-500/20 text-green-300' 
                  : 'bg-gray-500/20 text-gray-300'
              }`}>
                {device.status === 'online' ? '在线' : '离线'}
              </span>
            </div>
            <button 
              onClick={() => handleRemoveDevice(protocol.id, device.id)}
              className="p-1.5 hover:bg-red-500/20 text-red-400 rounded-lg"
            >
              <X className="w-3.5 h-3.5" />
            </button>
          </div>
        ))}
        {protocolDevices.length === 0 && (
          <p className="text-gray-500 text-sm text-center py-4">暂无关联设备</p>
        )}
      </motion.div>
    )}
  </AnimatePresence>
</div>
```

### 2. 协议配置弹窗添加设备选择器

```typescript
// 在协议配置表单中添加设备多选区域
<div className="mt-6 pt-6 border-t border-gray-700">
  <div className="flex items-center justify-between mb-4">
    <label className="block text-sm font-medium text-gray-400">关联设备</label>
    <button 
      type="button"
      onClick={() => setDeviceSelectorOpen(true)}
      className="text-sm text-blue-400 hover:text-blue-300"
    >
      选择设备 ({selectedDevices.length})
    </button>
  </div>
  
  {/* 已选设备预览 */}
  <div className="flex flex-wrap gap-2">
    {selectedDevices.map(deviceId => {
      const device = devices.find(d => d.id === deviceId);
      return (
        <span 
          key={deviceId} 
          className="inline-flex items-center gap-1 px-3 py-1.5 bg-blue-500/20 text-blue-300 rounded-lg text-sm"
        >
          {device?.name}
          <button 
            type="button"
            onClick={() => handleRemoveDevice(deviceId)}
            className="hover:text-red-400"
          >
            <X className="w-3.5 h-3.5" />
          </button>
        </span>
      );
    })}
    {selectedDevices.length === 0 && (
      <span className="text-gray-500 text-sm">未选择设备</span>
    )}
  </div>
</div>

// 设备选择器弹窗
<AnimatePresence>
  {deviceSelectorOpen && (
    <DeviceSelectorModal
      devices={availableDevices}
      selectedDevices={selectedDevices}
      onSelect={setSelectedDevices}
      onClose={() => setDeviceSelectorOpen(false)}
    />
  )}
</AnimatePresence>
```

### 3. 设备选择器组件

```typescript
function DeviceSelectorModal({ 
  devices, 
  selectedDevices, 
  onSelect, 
  onClose 
}: DeviceSelectorModalProps) {
  const [searchTerm, setSearchTerm] = useState('');
  const [filterStatus, setFilterStatus] = useState('all');
  
  const filteredDevices = devices.filter(d => {
    const matchesSearch = d.name.toLowerCase().includes(searchTerm.toLowerCase());
    const matchesStatus = filterStatus === 'all' || d.status === filterStatus;
    return matchesSearch && matchesStatus;
  });
  
  const handleToggleDevice = (deviceId: string) => {
    if (selectedDevices.includes(deviceId)) {
      onSelect(selectedDevices.filter(id => id !== deviceId));
    } else {
      onSelect([...selectedDevices, deviceId]);
    }
  };
  
  return (
    <motion.div className="fixed inset-0 bg-black/60 backdrop-blur-sm flex items-center justify-center z-50 p-4">
      <div className="bg-gray-800 border border-gray-700 rounded-xl max-w-4xl w-full max-h-[80vh] overflow-hidden flex flex-col">
        {/* Header */}
        <div className="p-6 border-b border-gray-700">
          <h3 className="text-xl font-bold text-white">选择设备</h3>
        </div>
        
        {/* Search & Filter */}
        <div className="p-4 space-y-3">
          <input
            type="text"
            placeholder="搜索设备名称..."
            value={searchTerm}
            onChange={(e) => setSearchTerm(e.target.value)}
            className="w-full px-4 py-2 bg-gray-900 border border-gray-700 rounded-lg text-white"
          />
          <select
            value={filterStatus}
            onChange={(e) => setFilterStatus(e.target.value)}
            className="w-full px-4 py-2 bg-gray-900 border border-gray-700 rounded-lg text-white"
          >
            <option value="all">全部状态</option>
            <option value="online">在线</option>
            <option value="offline">离线</option>
          </select>
        </div>
        
        {/* Device List */}
        <div className="flex-1 overflow-y-auto p-4 space-y-2">
          {filteredDevices.map(device => (
            <div 
              key={device.id}
              onClick={() => handleToggleDevice(device.id)}
              className={`p-4 rounded-lg cursor-pointer border-2 transition-colors ${
                selectedDevices.includes(device.id)
                  ? 'bg-blue-500/20 border-blue-500'
                  : 'bg-gray-900 border-transparent hover:border-gray-600'
              }`}
            >
              <div className="flex items-center justify-between">
                <div className="flex items-center gap-3">
                  <input
                    type="checkbox"
                    checked={selectedDevices.includes(device.id)}
                    onChange={() => handleToggleDevice(device.id)}
                    className="rounded"
                  />
                  <div>
                    <p className="text-white font-medium">{device.name}</p>
                    <p className="text-gray-400 text-xs">{device.serialNumber}</p>
                  </div>
                </div>
                <span className={`px-2 py-1 rounded text-xs ${
                  device.status === 'online' 
                    ? 'bg-green-500/20 text-green-300' 
                    : 'bg-gray-500/20 text-gray-300'
                }`}>
                  {device.status === 'online' ? '在线' : '离线'}
                </span>
              </div>
            </div>
          ))}
        </div>
        
        {/* Footer */}
        <div className="p-4 border-t border-gray-700 flex gap-3">
          <button 
            onClick={onClose}
            className="flex-1 px-4 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded-lg"
          >
            取消
          </button>
          <button 
            onClick={onClose}
            className="flex-1 px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded-lg"
          >
            确认选择 ({selectedDevices.length}台)
          </button>
        </div>
      </div>
    </motion.div>
  );
}
```

### 4. 数据处理逻辑

```typescript
// 在协议配置组件中添加状态管理
const [expandedProtocols, setExpandedProtocols] = useState<Set<string>>(new Set());
const [showDeviceSelector, setShowDeviceSelector] = useState(false);
const [editingProtocolDevices, setEditingProtocolDevices] = useState<string[]>([]);

// 初始化示例数据（添加deviceIds）
const [protocols, setProtocols] = useState<ProtocolConfig[]>([
  {
    id: 'proto-001',
    name: 'Modbus TCP主站',
    type: 'Modbus TCP',
    status: 'running',
    deviceCount: 2,
    deviceIds: ['1', '2'],  // 关联设备ID
    host: '192.168.1.100',
    port: 502,
    lastSync: '2026-03-23 14:30:25',
  },
  // ... 其他协议
]);

// 获取协议关联的设备
const getProtocolDevices = (protocol: ProtocolConfig) => {
  return devices.filter(d => protocol.deviceIds?.includes(d.id));
};

// 切换设备列表展开状态
const toggleDeviceList = (protocolId: string) => {
  setExpandedProtocols(prev => {
    const newSet = new Set(prev);
    if (newSet.has(protocolId)) {
      newSet.delete(protocolId);
    } else {
      newSet.add(protocolId);
    }
    return newSet;
  });
};

// 从协议中移除设备
const handleRemoveDevice = (protocolId: string, deviceId: string) => {
  setProtocols(protocols.map(p => 
    p.id === protocolId 
      ? {
          ...p,
          deviceIds: p.deviceIds?.filter(id => id !== deviceId) || [],
          deviceCount: (p.deviceIds?.filter(id => id !== deviceId).length || 0)
        }
      : p
  ));
};

// 在编辑协议时加载设备列表
const handleEdit = (protocol: ProtocolConfig) => {
  setEditingProtocol(protocol);
  setEditingProtocolDevices(protocol.deviceIds || []);
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

// 保存协议时更新设备关联
const handleSave = () => {
  if (editingProtocol) {
    setProtocols(protocols.map(p =>
      p.id === editingProtocol.id
        ? {
            ...p,
            ...formData,
            deviceIds: editingProtocolDevices,
            deviceCount: editingProtocolDevices.length,
            lastSync: new Date().toLocaleString()
          }
        : p
    ));
  } else {
    const newProtocol: ProtocolConfig = {
      id: `proto-${Date.now()}`,
      ...formData,
      status: 'stopped',
      deviceIds: editingProtocolDevices,
      deviceCount: editingProtocolDevices.length,
      lastSync: new Date().toLocaleString(),
    };
    setProtocols([...protocols, newProtocol]);
  }
  setShowModal(false);
  setEditingProtocolDevices([]);
};
```

## 边界条件与异常处理

### 1. 设备状态同步
- 设备离线时，在协议列表中仍显示关联关系，但状态显示为离线
- 设备被删除后，自动从所有协议的设备列表中移除

### 2. 协议删除
- 删除协议时，确认对话框提示"将移除该协议的所有设备关联"
- 设备记录不受影响，只解除关联关系

### 3. 设备重复关联
- 同一设备只能关联到一个协议（如果业务需要可改为多协议支持）
- 在设备选择器中自动过滤已关联到其他协议的设备

### 4. 权限控制
- 根据用户权限限制可操作的设备范围
- 显示时根据区域权限过滤设备列表

### 5. 数据一致性
- `deviceCount` 字段根据 `deviceIds` 数组长度自动计算
- 保存时验证 `deviceIds` 是否有效（设备是否存在）

## 数据流动路径

```
用户操作
  ↓
协议卡片点击"查看设备"
  ↓
展开设备列表（根据 protocol.deviceIds 过滤）
  ↓
显示设备名称、序列号、状态
  
  或
  
用户操作
  ↓
点击"编辑协议"
  ↓
加载已关联设备到 editingProtocolDevices
  ↓
点击"选择设备"
  ↓
打开设备选择器
  ↓
用户选择/取消选择设备
  ↓
更新 editingProtocolDevices
  ↓
点击"保存"
  ↓
更新协议的 deviceIds 和 deviceCount
  ↓
数据持久化到状态管理
```

## 预期成果

### 用户体验改进
1. **透明度提升**：管理员可以清楚看到每个协议关联的设备列表
2. **操作便捷性**：可以在协议配置界面直接管理设备关联
3. **故障排查**：快速定位某个协议的设备采集问题
4. **批量管理**：支持批量关联设备到协议

### 功能完整性
- 协议卡片展示设备数量和设备列表
- 协议配置弹窗支持设备选择器
- 支持设备的搜索和筛选
- 支持实时设备状态显示
- 支持从协议中移除设备

### 技术特性
- 响应式设计，适配不同屏幕尺寸
- 平滑的动画过渡效果
- 数据实时同步
- 异常状态友好提示