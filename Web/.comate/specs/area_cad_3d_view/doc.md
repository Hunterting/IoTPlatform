# 区域CAD图纸3D场景查看功能需求文档

## 需求概述
在智能分析页面(监控页面)中，实现点击区域后显示对应区域的CAD文件3D场景的功能。用户在区域导航中选择一个区域，系统自动查找该区域关联的CAD图纸档案，并调用3D场景服务进行可视化展示。

## 需求场景具体处理逻辑

### 1. 区域选择与CAD关联查找
- 用户在智能分析页面的左侧"区域导航"中点击选择一个区域
- 系统根据选中区域的ID或名称，在档案列表中查找分类为"图纸资料"且关联该区域的CAD档案
- 查找条件：
  - `archive.category === '图纸资料'`
  - `archive.areaId === selectedAreaId` 或 `archive.areaName === selectedAreaName`
  - `archive.type === 'blueprint'`
  - 优先选择最新的CAD档案（按上传日期排序）

### 2. 3D场景展示触发
- 找到匹配的CAD档案后，系统检查该档案是否具备3D场景配置：
  - `archive.is3DModel === true`
  - `archive.sceneConfig` 存在且完整（包含cadFilePath、devices等必要字段）
- 如果档案支持3D展示，在页面中央的"场景区域图"区域渲染3D场景组件
- 如果找不到匹配的CAD档案或档案不支持3D展示，显示提示信息

### 3. 3D场景渲染
- 使用现有的 `SceneViewer` 组件渲染3D场景
- 场景配置来自CAD档案的 `sceneConfig` 字段
- 场景包含：
  - CAD图纸解析后的3D几何体
  - 设备标注点（可交互）
  - 区域布局可视化
- 支持用户交互：
  - 鼠标拖拽旋转场景
  - 滚轮缩放视图
  - 点击设备标记显示设备详情
  - 悬停设备显示设备信息

### 4. 视图切换机制
- 提供"视频监控视图"和"3D场景视图"两种模式切换
- 默认显示视频监控视图（摄像头网格）
- 点击"切换到3D场景"按钮后，隐藏摄像头网格，显示3D场景
- 点击"返回视频监控"按钮后，隐藏3D场景，恢复摄像头网格
- 切换按钮状态和视图内容保持同步

### 5. 设备详情展示
- 用户点击3D场景中的设备标记时，弹出设备详情面板
- 设备详情面板显示：
  - 设备名称
  - 设备类型
  - 设备型号
  - 序列号
  - 关联的传感器列表
  - 当前运行状态
- 提供"查看完整设备信息"按钮，可跳转到设备管理页面

## 架构技术方案

### 1. 数据流程架构
```
用户点击区域导航
  ↓
获取选中区域ID/名称
  ↓
查询Archive档案数据（过滤条件：category='图纸资料' & areaId匹配）
  ↓
验证档案是否支持3D展示（is3DModel=true & sceneConfig存在）
  ↓
提取sceneConfig（包含cadFilePath、devices配置）
  ↓
渲染SceneViewer组件，传入sceneConfig
  ↓
用户交互（旋转、缩放、点击设备）
```

### 2. 视图切换架构
```
MonitoringPage主页面
  ├── 视图模式状态（viewMode: 'video' | '3d'）
  ├── 视频监控视图组件（摄像头网格）
  ├── 3D场景视图组件（SceneViewer）
  └── 切换控制（按钮或Tab）
```

### 3. 状态管理
- `selectedArea`: 当前选中的区域对象
- `viewMode`: 当前视图模式（'video' | '3d'）
- `selectedCADArchive`: 找到的CAD档案对象
- `selectedDevice`: 当前选中的设备对象
- `showDeviceDetail`: 是否显示设备详情面板

## 影响文件

### 修改的文件

#### 1. **src/app/pages/MonitoringPage.tsx**
   - 修改类型：功能增强
   - 影响范围：
     - 新增状态管理（viewMode、selectedCADArchive、selectedDevice、showDeviceDetail）
     - 新增函数：`findCADArchiveByArea`（根据区域查找CAD档案）
     - 新增函数：`handleViewModeToggle`（切换视图模式）
     - 新增函数：`handleDeviceClick`（处理设备点击事件）
     - 修改"场景区域图"区域：条件渲染视频监控和3D场景
     - 新增视图切换按钮
     - 新增设备详情面板

#### 2. **src/app/data/archivesData.ts**
   - 修改类型：数据补充（如需要）
   - 说明：确保Archive数据结构包含必要的字段（areaId、areaName、is3DModel、sceneConfig）
   - 预期：现有数据已满足要求，无需修改

### 不修改的文件
- `src/app/components/3d-scene/SceneViewer.tsx` - 已支持3D场景渲染
- `src/app/services/cad/CADParserService.ts` - 已支持CAD文件解析
- `src/app/services/scene/SceneBuilderService.ts` - 已支持场景构建
- `src/app/types/3d-scene.ts` - 类型定义已完善
- `src/app/contexts/AreaContext.tsx` - 区域数据管理已完善

## 实现细节

### 1. 查找CAD档案函数
```typescript
// 在MonitoringPage.tsx中新增
import { archivesData, Archive } from '@/app/data/archivesData';

// 根据区域查找CAD档案
const findCADArchiveByArea = (areaId: string | null, areaName: string | null): Archive | null => {
  if (!areaId && !areaName) return null;

  const cadArchives = archivesData.filter(archive => 
    archive.category === '图纸资料' &&
    archive.type === 'blueprint' &&
    archive.is3DModel === true &&
    archive.sceneConfig
  );

  // 优先按areaId匹配
  if (areaId) {
    const matchedByAreaId = cadArchives.find(archive => archive.areaId === areaId);
    if (matchedByAreaId) return matchedByAreaId;
  }

  // 其次按areaName匹配
  if (areaName) {
    const matchedByAreaName = cadArchives.find(archive => archive.areaName === areaName);
    if (matchedByAreaName) return matchedByAreaName;
  }

  return null;
};

// 监听区域选择变化
useEffect(() => {
  const selectedAreaData = flattenAreas(areas).find(a => a.id === selectedArea);
  if (selectedAreaData) {
    const cadArchive = findCADArchiveByArea(selectedAreaData.id, selectedAreaData.name);
    setSelectedCADArchive(cadArchive);
    
    // 如果找到CAD档案且有3D配置，自动切换到3D视图
    if (cadArchive && cadArchive.sceneConfig) {
      setViewMode('3d');
    } else {
      setViewMode('video');
    }
  }
}, [selectedArea, areas, flattenAreas]);
```

### 2. 视图模式切换UI
```typescript
// 在场景区域图标题区域添加切换按钮
<div className="flex items-center justify-between mb-2 flex-shrink-0">
  <h3 className="text-sm font-bold text-white">场景区域图</h3>
  <button
    onClick={handleViewModeToggle}
    className={`px-3 py-1 rounded text-xs transition-colors ${
      viewMode === '3d'
        ? 'bg-cyan-500/20 text-cyan-400 border border-cyan-500/40'
        : 'bg-blue-500/20 text-blue-400 border border-blue-500/40'
    }`}
  >
    {viewMode === 'video' ? '切换到3D场景' : '返回视频监控'}
  </button>
</div>

// 在场景区域图内容区域条件渲染
<div className="flex-1 bg-gradient-to-br from-[#1a1f35] to-[#0f1321] rounded-lg border border-blue-500/30 p-3 overflow-hidden flex flex-col">
  {/* 视频监控视图 */}
  {viewMode === 'video' && (
    <>
      <h3 className="text-sm font-bold text-white mb-2 flex-shrink-0">视频监控</h3>
      <div className="flex-1 grid grid-cols-4 grid-rows-4 gap-1 overflow-hidden">
        {displayCameras.map((camera, index) => (
          // 摄像头网格渲染逻辑...
        ))}
      </div>
    </>
  )}

  {/* 3D场景视图 */}
  {viewMode === '3d' && selectedCADArchive?.sceneConfig && (
    <>
      <h3 className="text-sm font-bold text-white mb-2 flex-shrink-0">
        3D场景 - {selectedCADArchive.name}
      </h3>
      <div className="flex-1 relative">
        <SceneViewer
          sceneConfig={selectedCADArchive.sceneConfig}
          onDeviceClick={handleDeviceClick}
          onHoverDevice={(deviceId, deviceData) => {
            // 处理设备悬停
          }}
          className="w-full h-full"
        />
      </div>
    </>
  )}

  {/* 无CAD档案提示 */}
  {viewMode === '3d' && !selectedCADArchive?.sceneConfig && (
    <div className="flex-1 flex items-center justify-center">
      <div className="text-center">
        <div className="text-gray-400 text-sm mb-2">当前区域暂无3D场景</div>
        <div className="text-gray-500 text-xs">请先在档案管理中上传该区域的CAD图纸</div>
      </div>
    </div>
  )}
</div>
```

### 3. 设备详情面板
```typescript
// 在页面右侧添加设备详情面板（在实时报警面板下方或作为独立弹窗）
{showDeviceDetail && selectedDevice && (
  <div className="w-64 bg-gradient-to-br from-[#1a1f35] to-[#0f1321] rounded-lg border border-cyan-500/30 p-3 flex flex-col overflow-hidden">
    <div className="flex items-center justify-between mb-3">
      <h3 className="text-sm font-bold text-white">设备详情</h3>
      <button
        onClick={() => setShowDeviceDetail(false)}
        className="p-1 hover:bg-white/10 rounded transition-colors"
      >
        <X className="w-4 h-4 text-gray-400" />
      </button>
    </div>

    <div className="space-y-3">
      <div className="bg-white/5 rounded-lg p-3">
        <div className="text-xs text-gray-400 mb-1">设备名称</div>
        <div className="text-sm font-medium text-white">{selectedDevice.name}</div>
      </div>

      <div className="bg-white/5 rounded-lg p-3">
        <div className="text-xs text-gray-400 mb-1">设备类型</div>
        <div className="text-sm text-white">{selectedDevice.type}</div>
      </div>

      {selectedDevice.model && (
        <div className="bg-white/5 rounded-lg p-3">
          <div className="text-xs text-gray-400 mb-1">设备型号</div>
          <div className="text-sm text-white">{selectedDevice.model}</div>
        </div>
      )}

      {selectedDevice.serialNumber && (
        <div className="bg-white/5 rounded-lg p-3">
          <div className="text-xs text-gray-400 mb-1">序列号</div>
          <div className="text-sm text-white">{selectedDevice.serialNumber}</div>
        </div>
      )}

      {selectedDevice.sensors && selectedDevice.sensors.length > 0 && (
        <div className="bg-white/5 rounded-lg p-3">
          <div className="text-xs text-gray-400 mb-2">关联传感器</div>
          <div className="flex flex-wrap gap-1">
            {selectedDevice.sensors.map((sensor, index) => (
              <span
                key={index}
                className="px-2 py-1 bg-cyan-500/20 text-cyan-400 text-xs rounded"
              >
                {sensor}
              </span>
            ))}
          </div>
        </div>
      )}
    </div>

    <button
      onClick={() => {
        // 跳转到设备管理页面（待实现）
        console.log('跳转到设备管理页面，设备ID:', selectedDevice.id);
      }}
      className="mt-3 w-full py-2 bg-gradient-to-r from-cyan-500 to-blue-500 hover:from-cyan-600 hover:to-blue-600 rounded text-xs text-white transition-all font-medium"
    >
      查看完整设备信息
    </button>
  </div>
)}
```

### 4. 状态初始化和清理
```typescript
// 在MonitoringPage组件顶部初始化新状态
const [viewMode, setViewMode] = useState<'video' | '3d'>('video');
const [selectedCADArchive, setSelectedCADArchive] = useState<Archive | null>(null);
const [selectedDevice, setSelectedDevice] = useState<DeviceMarker3D | null>(null);
const [showDeviceDetail, setShowDeviceDetail] = useState(false);

// 视图切换处理函数
const handleViewModeToggle = () => {
  setViewMode(prev => prev === 'video' ? '3d' : 'video');
};

// 设备点击处理函数
const handleDeviceClick = (deviceId: string, deviceData: any) => {
  setSelectedDevice(deviceData);
  setShowDeviceDetail(true);
};

// 组件卸载时清理3D场景资源
useEffect(() => {
  return () => {
    // SceneViewer组件内部会自动清理资源
    // 这里可以添加额外的清理逻辑
  };
}, []);
```

## 边界条件与异常处理

### 1. 区域选择异常
- **场景**：用户选择了一个没有关联CAD档案的区域
- **处理**：显示"当前区域暂无3D场景"提示，引导用户上传CAD图纸
- **UI反馈**：提示信息友好清晰，包含操作建议

### 2. CAD档案格式异常
- **场景**：找到的CAD档案没有sceneConfig或is3DModel为false
- **处理**：回退到视频监控视图，显示"该档案不支持3D展示"提示
- **UI反馈**：Toast消息提示用户

### 3. 3D场景加载失败
- **场景**：CAD文件解析失败或场景构建错误
- **处理**：SceneViewer组件内部已实现错误处理和重试机制
- **UI反馈**：显示错误信息和重试按钮

### 4. 设备详情缺失
- **场景**：点击设备标记时，deviceData不完整
- **处理**：显示基本信息（名称、ID），缺失字段显示"未知"
- **UI反馈**：友好降级，不影响功能使用

### 5. 视图切换冲突
- **场景**：用户在3D场景加载过程中频繁切换视图
- **处理**：添加视图切换状态锁，防止重复渲染
- **UI反馈**：切换按钮显示加载状态

### 6. 多CAD档案关联同一区域
- **场景**：一个区域有多个CAD档案
- **处理**：优先选择最新的档案（按date字段降序排序）
- **扩展**：未来可提供档案选择下拉框

## 数据流动路径

### 1. 区域选择数据流
```
用户点击区域树节点
  → setSelectedArea(areaId)
  → useEffect监听selectedArea变化
  → 调用findCADArchiveByArea(areaId)
  → 过滤archivesData获取匹配档案
  → setSelectedCADArchive(cadArchive)
  → 判断cadArchive?.sceneConfig是否存在
  → 自动切换到3D视图或保持视频视图
```

### 2. 3D场景渲染数据流
```
viewMode === '3d' && selectedCADArchive?.sceneConfig
  → 渲染SceneViewer组件
  → 传入sceneConfig对象
  → SceneBuilderService.buildScene(sceneConfig)
  → CADParserService.parseCADFile(sceneConfig.cadFilePath)
  → 创建3D场景（场景、相机、灯光、设备标记）
  → 渲染到DOM
  → 用户交互（旋转、缩放、点击设备）
```

### 3. 设备交互数据流
```
用户点击3D场景中的设备标记
  → SceneViewer触发onDeviceClick回调
  → MonitoringPage.handleDeviceClick(deviceId, deviceData)
  → setSelectedDevice(deviceData)
  → setShowDeviceDetail(true)
  → 渲染设备详情面板
  → 用户查看设备信息
  → 用户点击"查看完整设备信息"
  → 跳转到设备管理页面（待实现）
```

### 4. 视图切换数据流
```
用户点击切换按钮
  → handleViewModeToggle()
  → setViewMode(prev => prev === 'video' ? '3d' : 'video')
  → 条件渲染：
    - video: 显示摄像头网格
    - 3d: 显示SceneViewer或无场景提示
```

## 预期成果

### 1. 功能完整性
- 用户在智能分析页面选择区域后，自动显示该区域的3D场景
- 3D场景展示正确，包含CAD图纸和设备标记
- 支持视图模式切换（视频监控 ↔ 3D场景）
- 支持设备交互（点击、悬停、查看详情）

### 2. 用户体验改进
- 流畅的视图切换动画和过渡效果
- 清晰的视觉反馈和状态提示
- 友好的错误处理和引导信息
- 直观的设备详情展示

### 3. 性能优化
- 按需加载3D场景组件（仅在3D视图时加载）
- 避免重复渲染场景（合理使用React.memo和useMemo）
- 及时释放3D场景资源（组件卸载时清理）

### 4. 代码质量
- 遵循现有项目架构和代码风格
- 充分利用已有组件和服务（SceneViewer、CADParserService、SceneBuilderService）
- 模块化设计，便于后续维护和扩展
- 完善的类型定义和错误处理

### 5. 兼容性
- 不影响现有视频监控功能
- 向后兼容现有的Archive数据结构
- 支持未来扩展（多档案选择、自定义场景配置等）