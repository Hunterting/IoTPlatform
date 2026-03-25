# 3D场景可视化需求文档

## 需求概述
在智能分析、空气质量、环境监测、工作台四个模块中实现基于CAD图纸的三维可视化场景。用户可以在场景中查看设备的实时位置,点击设备查看并操作设备的关键信息。先从智能分析模块实现,验证效果后推广到其他模块。

## 需求场景具体处理逻辑

### 智能分析模块(优先实现)
1. 用户选择区域后,系统加载该区域的CAD图纸
2. CAD图纸转换为3D场景进行渲染
3. 场景中标注所有设备位置,显示设备类型和状态
4. 用户点击设备标注,弹出设备关键信息面板
5. 设备信息面板支持查看状态、控制开关机等操作

### 推广到其他模块
- 空气质量模块: 场景中叠加空气质量热力图
- 环境监测模块: 场景中叠加环境数据(温度、湿度等)
- 工作台模块: 场景中作为区域导航入口

## 架构技术方案

### 技术栈选型
- **3D渲染引擎**: Three.js (已包含在项目中)
- **CAD解析**: 三方库(需要选择,如autodesk-viewer或opencascade.js)
- **状态管理**: React Context (使用现有DeviceContext)
- **UI组件**: Radix UI + Tailwind CSS (项目现有技术栈)

### 架构分层
```
┌─────────────────────────────────────┐
│     Page Layer (AnalyticsPage)      │  页面层 - 业务逻辑
├─────────────────────────────────────┤
│    3D Scene Component Layer        │  3D场景组件层 - 可复用
│  - SceneViewer                     │
│  - DeviceMarker3D                   │
│  - DeviceInfoPanel                  │
├─────────────────────────────────────┤
│     Service Layer                    │  服务层 - CAD解析
│  - CADParserService                 │
│  - SceneBuilderService              │
├─────────────────────────────────────┤
│     Data Layer                       │  数据层
│  - archivesData (CAD文件路径)      │
│  - DeviceContext (设备信息)         │
│  - AreaContext (区域信息)           │
└─────────────────────────────────────┘
```

## 影响文件

### 新增文件
- `src/app/components/3d-scene/SceneViewer.tsx` - 3D场景查看器核心组件
- `src/app/components/3d-scene/DeviceMarker3D.tsx` - 3D设备标注点组件
- `src/app/components/3d-scene/DeviceInfoPanel.tsx` - 设备信息面板组件
- `src/app/services/cad/CADParserService.ts` - CAD文件解析服务
- `src/app/services/scene/SceneBuilderService.ts` - 3D场景构建服务
- `src/app/types/3d-scene.ts` - 3D场景相关类型定义

### 修改文件
- `src/app/pages/AnalyticsPage.tsx` - 集成3D场景组件,替换现有2D视图
- `src/app/data/archivesData.ts` - 扩展Archive接口,添加CAD文件路径和设备位置信息
- `package.json` - 添加Three.js和CAD解析相关依赖

### 影响的函数/模块
- `AnalyticsPage.tsx`的3D场景区域渲染逻辑
- `archivesData.ts`的数据结构
- `DeviceContext.tsx`的设备数据获取

## 实现细节

### 1. 数据结构扩展
```typescript
// src/app/types/3d-scene.ts
export interface DeviceMarker3D {
  id: string;
  deviceId: string;  // 关联DeviceContext中的设备ID
  name: string;
  position: {
    x: number;  // 3D世界坐标
    y: number;
    z: number;
  };
  rotation?: {
    x: number;
    y: number;
    z: number;
  };
  modelPath?: string;  // 3D模型路径(可选)
}

export interface SceneConfig {
  areaId: string;
  areaName: string;
  cadFilePath: string;
  devices: DeviceMarker3D[];
  camera?: {
    position: [number, number, number];
    lookAt: [number, number, number];
  };
}

// 扩展 archivesData.ts
interface Archive {
  // ... 现有字段
  sceneConfig?: SceneConfig;  // 新增: 3D场景配置
}
```

### 2. CAD解析服务实现
```typescript
// src/app/services/cad/CADParserService.ts
import * as THREE from 'three';

export class CADParserService {
  /**
   * 解析CAD文件并提取3D几何体
   * @param cadFileUrl CAD文件URL
   * @returns THREE.Group CAD文件转换为的3D对象
   */
  static async parseCADFile(cadFileUrl: string): Promise<THREE.Group> {
    // 实现CAD文件解析逻辑
    // 方案1: 使用autodesk-forge(需要API密钥)
    // 方案2: 使用opencascade.js(开源,体积大)
    // 方案3: 使用three-dxf(轻量级,仅支持DXF)
    
    // 临时实现: 返回一个基础的3D场景
    const group = new THREE.Group();
    const geometry = new THREE.BoxGeometry(10, 10, 0.5);
    const material = new THREE.MeshBasicMaterial({ 
      color: 0x00c3ff,
      transparent: true,
      opacity: 0.3 
    });
    const mesh = new THREE.Mesh(geometry, material);
    group.add(mesh);
    
    return group;
  }

  /**
   * 提取CAD文件中的设备位置标注
   * @param cadGroup CAD 3D对象
   * @returns 设备位置标注数组
   */
  static extractDeviceMarkers(cadGroup: THREE.Group): DeviceMarker3D[] {
    // 从CAD文件中提取包含特定图层的设备标注
    // 实现图层层级识别和位置提取
    return [];
  }
}
```

### 3. 场景构建服务实现
```typescript
// src/app/services/scene/SceneBuilderService.ts
import * as THREE from 'three';
import { CADParserService } from './CADParserService';

export class SceneBuilderService {
  /**
   * 构建3D场景
   * @param config 场景配置
   * @returns 场景对象
   */
  static async buildScene(config: SceneConfig): Promise<{
    scene: THREE.Scene;
    camera: THREE.PerspectiveCamera;
    renderer: THREE.WebGLRenderer;
  }> {
    // 创建场景
    const scene = new THREE.Scene();
    scene.background = new THREE.Color(0x0a1628);

    // 添加灯光
    const ambientLight = new THREE.AmbientLight(0xffffff, 0.6);
    scene.add(ambientLight);

    const directionalLight = new THREE.DirectionalLight(0xffffff, 0.8);
    directionalLight.position.set(10, 20, 10);
    scene.add(directionalLight);

    // 加载CAD文件
    const cadGroup = await CADParserService.parseCADFile(config.cadFilePath);
    scene.add(cadGroup);

    // 添加设备标注
    config.devices.forEach(device => {
      const marker = this.createDeviceMarker(device);
      marker.userData = { deviceData: device };
      scene.add(marker);
    });

    // 创建相机
    const camera = new THREE.PerspectiveCamera(
      60,
      window.innerWidth / window.innerHeight,
      0.1,
      1000
    );
    
    if (config.camera) {
      camera.position.set(...config.camera.position);
      camera.lookAt(...config.camera.lookAt);
    } else {
      camera.position.set(15, 15, 15);
      camera.lookAt(0, 0, 0);
    }

    // 创建渲染器
    const renderer = new THREE.WebGLRenderer({ antialias: true });
    renderer.setSize(window.innerWidth, window.innerHeight);
    renderer.setPixelRatio(window.devicePixelRatio);

    return { scene, camera, renderer };
  }

  /**
   * 创建设备标注
   */
  private static createDeviceMarker(device: DeviceMarker3D): THREE.Group {
    const group = new THREE.Group();
    group.position.set(device.position.x, device.position.y, device.position.z);

    // 创建设备图标(用3D几何体表示)
    const geometry = new THREE.CylinderGeometry(0.3, 0.3, 0.5, 16);
    const material = new THREE.MeshPhongMaterial({ 
      color: 0x00c3ff,
      emissive: 0x00c3ff,
      emissiveIntensity: 0.3
    });
    const mesh = new THREE.Mesh(geometry, material);
    group.add(mesh);

    // 添加光圈动画效果
    const ringGeometry = new THREE.RingGeometry(0.4, 0.5, 32);
    const ringMaterial = new THREE.MeshBasicMaterial({ 
      color: 0x00c3ff,
      transparent: true,
      opacity: 0.5,
      side: THREE.DoubleSide
    });
    const ring = new THREE.Mesh(ringGeometry, ringMaterial);
    ring.rotation.x = -Math.PI / 2;
    ring.position.y = -0.2;
    group.add(ring);

    return group;
  }
}
```

### 4. SceneViewer组件实现
```typescript
// src/app/components/3d-scene/SceneViewer.tsx
import { useEffect, useRef, useState } from 'react';
import * as THREE from 'three';
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls';
import { SceneBuilderService } from '@/app/services/scene/SceneBuilderService';
import { SceneConfig } from '@/app/types/3d-scene';
import { Cpu } from 'lucide-react';

interface SceneViewerProps {
  sceneConfig: SceneConfig;
  onDeviceClick?: (deviceId: string) => void;
  onHoverDevice?: (deviceId: string | null) => void;
}

export function SceneViewer({ 
  sceneConfig, 
  onDeviceClick,
  onHoverDevice 
}: SceneViewerProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!containerRef.current) return;

    let animationId: number;
    let controls: OrbitControls;

    const initScene = async () => {
      try {
        setIsLoading(true);
        setError(null);

        // 构建场景
        const { scene, camera, renderer } = await SceneBuilderService.buildScene(sceneConfig);
        
        containerRef.current!.appendChild(renderer.domElement);

        // 添加轨道控制器
        controls = new OrbitControls(camera, renderer.domElement);
        controls.enableDamping = true;
        controls.dampingFactor = 0.05;
        controls.maxPolarAngle = Math.PI / 2;

        // 鼠标交互
        const raycaster = new THREE.Raycaster();
        const mouse = new THREE.Vector2();

        const onMouseMove = (event: MouseEvent) => {
          const rect = renderer.domElement.getBoundingClientRect();
          mouse.x = ((event.clientX - rect.left) / rect.width) * 2 - 1;
          mouse.y = -((event.clientY - rect.top) / rect.height) * 2 + 1;

          raycaster.setFromCamera(mouse, camera);
          const intersects = raycaster.intersectObjects(scene.children, true);

          if (intersects.length > 0) {
            const object = intersects[0].object;
            if (object.userData?.deviceData) {
              onHoverDevice?.(object.userData.deviceData.id);
            } else {
              onHoverDevice?.(null);
            }
          }
        };

        const onClick = (event: MouseEvent) => {
          raycaster.setFromCamera(mouse, camera);
          const intersects = raycaster.intersectObjects(scene.children, true);

          if (intersects.length > 0) {
            const object = intersects[0].object;
            if (object.userData?.deviceData) {
              onDeviceClick?.(object.userData.deviceData.id);
            }
          }
        };

        renderer.domElement.addEventListener('mousemove', onMouseMove);
        renderer.domElement.addEventListener('click', onClick);

        // 动画循环
        const animate = () => {
          animationId = requestAnimationFrame(animate);
          controls.update();
          renderer.render(scene, camera);
        };

        animate();
        setIsLoading(false);

      } catch (err) {
        console.error('场景初始化失败:', err);
        setError('场景加载失败');
        setIsLoading(false);
      }
    };

    initScene();

    return () => {
      if (animationId) {
        cancelAnimationFrame(animationId);
      }
    };
  }, [sceneConfig, onDeviceClick, onHoverDevice]);

  if (error) {
    return (
      <div className="flex items-center justify-center h-full bg-gray-900/50">
        <div className="text-center">
          <p className="text-red-400">{error}</p>
        </div>
      </div>
    );
  }

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-full bg-gray-900/50">
        <div className="text-center">
          <div className="w-16 h-16 mx-auto mb-4 border-4 border-cyan-500/30 border-t-cyan-400 rounded-full animate-spin" />
          <p className="text-cyan-400">3D场景加载中...</p>
        </div>
      </div>
    );
  }

  return <div ref={containerRef} className="w-full h-full" />;
}
```

### 5. DeviceInfoPanel组件实现
```typescript
// src/app/components/3d-scene/DeviceInfoPanel.tsx
import { DeviceItem } from '@/app/contexts/DeviceContext';
import { Cpu, Power, Settings } from 'lucide-react';
import { useState } from 'react';
import { motion, AnimatePresence } from 'motion/react';

interface DeviceInfoPanelProps {
  device: DeviceItem;
  onClose: () => void;
  onTogglePower?: (deviceId: string, status: 'online' | 'offline') => void;
}

export function DeviceInfoPanel({ device, onClose, onTogglePower }: DeviceInfoPanelProps) {
  const [isOn, setIsOn] = useState(device.status === 'online');

  const handleTogglePower = () => {
    const newStatus: 'online' | 'offline' = isOn ? 'offline' : 'online';
    setIsOn(!isOn);
    onTogglePower?.(device.id, newStatus);
  };

  return (
    <motion.div
      initial={{ x: 300, opacity: 0 }}
      animate={{ x: 0, opacity: 1 }}
      exit={{ x: 300, opacity: 0 }}
      className="absolute right-4 top-4 bottom-4 w-72 bg-black/90 backdrop-blur-xl border border-cyan-500/30 rounded-lg p-4 overflow-y-auto"
    >
      <div className="flex items-center justify-between mb-4">
        <h3 className="text-lg font-bold text-white">设备详情</h3>
        <button
          onClick={onClose}
          className="p-1 hover:bg-white/10 rounded transition-colors"
        >
          <Settings className="w-4 h-4 text-gray-400" />
        </button>
      </div>

      {/* 设备图标 */}
      <div className="flex items-center justify-center p-4 bg-cyan-500/20 rounded-lg mb-4">
        <Cpu className="w-12 h-12 text-cyan-400" />
      </div>

      {/* 设备信息 */}
      <div className="space-y-4">
        {/* 设备名称 */}
        <div>
          <p className="text-xs text-gray-400 mb-1">设备名称</p>
          <p className="text-white text-sm font-medium">{device.name}</p>
        </div>

        {/* 设备类型 */}
        <div>
          <p className="text-xs text-gray-400 mb-1">设备类型</p>
          <p className="text-white text-sm">{device.category}</p>
        </div>

        {/* 设备型号 */}
        <div>
          <p className="text-xs text-gray-400 mb-1">设备型号</p>
          <p className="text-white text-sm">{device.model}</p>
        </div>

        {/* 设备状态 */}
        <div>
          <p className="text-xs text-gray-400 mb-1">运行状态</p>
          <div className="flex items-center gap-2">
            <div className={`w-2 h-2 rounded-full ${device.status === 'online' ? 'bg-green-400 animate-pulse' : 'bg-red-400'}`} />
            <span className={`text-sm ${device.status === 'online' ? 'text-green-400' : 'text-red-400'}`}>
              {device.status === 'online' ? '在线' : '离线'}
            </span>
          </div>
        </div>

        {/* 设备位置 */}
        <div>
          <p className="text-xs text-gray-400 mb-1">安装位置</p>
          <p className="text-white text-sm">{device.location}</p>
        </div>

        {/* 最后维护 */}
        <div>
          <p className="text-xs text-gray-400 mb-1">最后维护</p>
          <p className="text-white text-sm">{device.lastMaintenance || '-'}</p>
        </div>

        {/* 供应商 */}
        {device.supplier && (
          <div>
            <p className="text-xs text-gray-400 mb-1">供应商</p>
            <p className="text-white text-sm">{device.supplier}</p>
          </div>
        )}

        {/* 能耗类型 */}
        <div>
          <p className="text-xs text-gray-400 mb-1">能耗类型</p>
          <div className="flex gap-2">
            {device.energyType.map(type => (
              <span key={type} className="px-2 py-1 text-xs bg-cyan-500/20 text-cyan-400 rounded">
                {type === 'electric' ? '电' : type === 'gas' ? '气' : '水'}
              </span>
            ))}
          </div>
        </div>
      </div>

      {/* 操作按钮 */}
      <div className="mt-6 space-y-2">
        <button
          onClick={handleTogglePower}
          className={`w-full flex items-center justify-center gap-2 px-4 py-2.5 rounded-lg font-medium transition-colors ${
            isOn
              ? 'bg-red-500 hover:bg-red-600 text-white'
              : 'bg-green-500 hover:bg-green-600 text-white'
          }`}
        >
          <Power className="w-4 h-4" />
          {isOn ? '关闭设备' : '启动设备'}
        </button>
      </div>
    </motion.div>
  );
}
```

### 6. 集成到AnalyticsPage
```typescript
// 在 AnalyticsPage.tsx 中集成SceneViewer
import { SceneViewer } from '@/app/components/3d-scene/SceneViewer';
import { DeviceInfoPanel } from '@/app/components/3d-scene/DeviceInfoPanel';

// 在组件中添加状态
const [selectedDevice, setSelectedDevice] = useState<DeviceItem | null>(null);
const [hoveredDevice, setHoveredDevice] = useState<string | null>(null);

// 构建场景配置
const sceneConfig = useMemo(() => {
  if (!current3DArchive || !current3DArchive.sceneConfig) return null;
  return current3DArchive.sceneConfig;
}, [current3DArchive]);

// 处理设备点击
const handleDeviceClick = (deviceId: string) => {
  const device = devices.find(d => d.id === deviceId);
  if (device) {
    setSelectedDevice(device);
  }
};

// 在3D场景区域替换为SceneViewer
{sceneConfig && (
  <SceneViewer
    sceneConfig={sceneConfig}
    onDeviceClick={handleDeviceClick}
    onHoverDevice={setHoveredDevice}
  />
)}

{/* 添加设备信息面板 */}
<AnimatePresence>
  {selectedDevice && (
    <DeviceInfoPanel
      device={selectedDevice}
      onClose={() => setSelectedDevice(null)}
      onTogglePower={(deviceId, status) => {
        updateDevice({
          ...selectedDevice,
          status: status
        });
      }}
    />
  )}
</AnimatePresence>
```

### 7. 扩展archivesData数据
```typescript
// 在 archivesData.ts 中添加场景配置示例
{
  id: '9',
  name: '一层大厅区-3D场景',
  appCode: 'HDLAPP001',
  type: 'blueprint',
  size: '12.5 MB',
  date: '2025-02-01',
  category: '图纸资料',
  is3DModel: true,
  areaId: 'L2-001',
  areaName: '一层大厅区',
  sceneConfig: {
    areaId: 'L2-001',
    areaName: '一层大厅区',
    cadFilePath: '/assets/cad/level1-hall.dxf',
    devices: [
      {
        id: 'marker-d1',
        deviceId: '1',
        name: '智能冰箱-A01',
        position: { x: -5, y: 0, z: -3 },
        rotation: { x: 0, y: 0, z: 0 }
      },
      {
        id: 'marker-d2',
        deviceId: '2',
        name: '智能烤箱-B01',
        position: { x: 2, y: 0, z: 1 },
        rotation: { x: 0, y: Math.PI / 4, z: 0 }
      },
    ],
    camera: {
      position: [15, 15, 15],
      lookAt: [0, 0, 0]
    }
  }
}
```

## 边界条件与异常处理

### CAD文件加载失败
- 显示友好的错误提示
- 提供重试按钮
- 降级到2D图片展示

### 设备信息缺失
- 如果CAD文件中缺少设备标注,使用archivesData中的devices数据
- 如果设备在DeviceContext中不存在,显示"设备信息未找到"提示

### 性能优化
- 大型CAD文件使用懒加载
- 设备标注点超过50个时使用LOD(Level of Detail)技术
- 离屏渲染优化

### 浏览器兼容性
- 检测WebGL支持
- 不支持WebGL时降级到Canvas2D渲染

### 移动端适配
- 触摸手势支持(旋转、缩放、平移)
- 响应式布局调整
- 性能降级(减少面数、简化效果)

## 数据流动路径

```
用户选择区域
    ↓
AnalyticsPage 获取 archivesData
    ↓
根据 areaName 查找对应的 sceneConfig
    ↓
SceneViewer 接收 sceneConfig
    ↓
SceneBuilderService.buildScene()
    ↓
CADParserService.parseCADFile() 解析CAD文件
    ↓
创建THREE.Scene、相机、渲染器
    ↓
加载CAD几何体到场景
    ↓
添加设备标注点
    ↓
用户交互(点击/悬停)
    ↓
触发 onDeviceClick / onHoverDevice 回调
    ↓
AnalyticsPage 更新 selectedDevice / hoveredDevice 状态
    ↓
DeviceInfoPanel 显示设备信息
    ↓
用户操作设备(开关机)
    ↓
调用 DeviceContext.updateDevice() 更新设备状态
```

## 预期成果

### 功能成果
1. **智能分析模块**实现3D场景可视化
2. 支持CAD文件解析和3D渲染
3. 设备标注点可交互显示
4. 点击设备弹出关键信息面板
5. 支持设备开关机等基本操作
6. 场景支持鼠标拖拽旋转、滚轮缩放

### 性能指标
- 场景加载时间 < 3秒
- 60fps流畅渲染
- 支持50+设备标注点
- 移动端响应时间 < 500ms

### 可复用性
- SceneViewer组件可在其他模块复用
- DeviceInfoPanel组件可在其他模块复用
- CAD解析服务可作为独立模块
- 场景构建服务支持自定义配置

### 后续扩展
- 支持更多CAD格式(DWG、STEP、IGES等)
- 添加设备动画(运行、故障状态动画)
- 支持热力图叠加
- 支持多场景切换
- 支持路径规划和设备巡检