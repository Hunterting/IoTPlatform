# 空气质量页面区域3D场景可视化需求

## 需求场景具体处理逻辑
在空气质量页面（AirQualityPage.tsx），当用户在区域导航中选择一个区域时，系统应自动查找该区域关联的CAD图纸档案，并调用3D场景服务进行可视化展示。具体流程如下：
1. 用户在区域树选择器（AreaTreeSelect）中点击一个区域节点。
2. 系统通过`useArea()` Hook获取当前选中的区域信息。
3. 根据区域的`id`（如`L2-001`）和`appCode`（如`HDLAPP001`），从`archivesData.ts`中查找所有`is3DModel: true`且`areaId`匹配的档案。
4. 找到第一个符合条件的档案后，提取其`sceneConfig`配置。
5. 将`sceneConfig`传递给`SceneViewer`组件，渲染3D场景。
6. 若未找到相关档案，则显示“暂无3D模型”提示。

## 架构技术方案
采用**状态驱动+事件监听**的架构模式：
- **状态管理**：使用`useArea()` Context读取当前选中的区域状态。
- **数据查找**：在`AirQualityPage.tsx`中定义`findCADArchiveByArea`函数，利用`archivesData`作为数据源进行过滤。
- **组件集成**：将`SceneViewer`嵌入`AirQualityPage.tsx`的主布局中，通过条件渲染控制其显示。
- **依赖注入**：`SceneBuilderService`负责构建3D场景，`CADParserService`负责解析CAD文件。

## 影响文件
- `src/pages/data-center/AirQualityPage.tsx`：核心页面逻辑，需新增区域选择监听和3D场景渲染。
- `src/app/data/archivesData.ts`：提供3D模型档案数据，已包含示例数据。
- `src/app/components/3d-scene/SceneViewer.tsx`：3D场景渲染组件，需确保支持动态配置更新。
- `src/app/services/scene/SceneBuilderService.ts`：场景构建服务，需支持热重载场景配置。

## 实现细节
```tsx
// src/pages/data-center/AirQualityPage.tsx
import { useEffect, useState } from 'react';
import { useArea } from '@/app/contexts/AreaContext';
import { SceneViewer } from '@/app/components/3d-scene/SceneViewer';
import { archivesData } from '@/app/data/archivesData';

// 查找与区域关联的CAD档案
function findCADArchiveByArea(areaId: string, appCode: string): any {
  return archivesData.find(
    a => a.is3DModel && a.areaId === areaId && a.appCode === appCode
  );
}

export function AirQualityPage() {
  const { areas, accessibleAreaIds } = useArea();
  const [selectedAreaId, setSelectedAreaId] = useState<string | null>(null);
  const [sceneConfig, setSceneConfig] = useState<any>(null);
  const [loading, setLoading] = useState<boolean>(false);

  // 监听区域选择变化
  useEffect(() => {
    if (!selectedAreaId) return;
    
    const area = areas.find(a => a.id === selectedAreaId);
    if (!area) return;

    setLoading(true);
    try {
      const archive = findCADArchiveByArea(area.id, area.appCode || '');
      if (archive && archive.sceneConfig) {
        setSceneConfig(archive.sceneConfig);
      } else {
        setSceneConfig(null);
      }
    } catch (error) {
      console.error('查找3D模型失败:', error);
      setSceneConfig(null);
    } finally {
      setLoading(false);
    }
  }, [selectedAreaId, areas]);

  return (
    <div className="flex h-full bg-gray-900">
      {/* 区域导航 */}
      <div className="w-64 p-4 border-r border-gray-700 bg-gray-800">
        <h2 className="text-lg font-semibold mb-4 text-cyan-400">区域导航</h2>
        <AreaTreeSelect 
          areas={areas} 
          selectedAreaId={selectedAreaId}
          onSelect={(id) => setSelectedAreaId(id)}
        />
      </div>

      {/* 主内容区 */}
      <div className="flex-1 p-4 relative overflow-hidden">
        <div className="absolute top-4 right-4 z-10">
          <button
            onClick={() => setSelectedAreaId(null)}
            className="px-3 py-1 bg-gray-600 text-white rounded text-sm hover:bg-gray-500 transition-colors"
          >
            清除选择
          </button>
        </div>

        <h2 className="text-xl font-bold mb-6 text-white">空气质量监测</h2>
        
        {/* 3D场景视图 */}
        {sceneConfig ? (
          <div className="relative w-full h-[calc(100vh-100px)] rounded-lg overflow-hidden shadow-2xl">
            <SceneViewer sceneConfig={sceneConfig} />
          </div>
        ) : (
          <div className="flex items-center justify-center w-full h-[calc(100vh-100px)] bg-gray-800 rounded-lg border-2 border-dashed border-gray-600">
            <p className="text-gray-400 text-lg">暂无3D模型数据</p>
          </div>
        )}
      </div>
    </div>
  );
}
```

## 边界条件与异常处理
- 当前区域无权限访问时，`accessibleAreaIds`为空，不触发查找。
- 档案数据缺失或`sceneConfig`字段无效时，显示默认提示。
- `findCADArchiveByArea`返回`undefined`时，`sceneConfig`置为`null`。
- 使用`try/catch`包裹异步操作，避免应用崩溃。

## 数据流动路径
用户选择 → `selectedAreaId`变更 → `findCADArchiveByArea`查询 → `sceneConfig`更新 → `SceneViewer`渲染

## 预期成果
完成空气质量页面的交互式3D场景可视化功能，实现“区域选择→自动加载对应CAD模型”的闭环，提升用户体验和数据洞察力。