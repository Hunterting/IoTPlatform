---
name: air-quality-3d-visualization
overview: 将 AirQualityPage 中间面板的静态图片占位替换为 SceneViewer 3D 场景渲染，利用已有的 current3DArchive.sceneConfig 数据，实现区域选择→自动加载3D模型的闭环。
todos:
  - id: fix-sceneviewer-resize
    content: 修复 SceneViewer.tsx 的 handleResize，使用容器尺寸替代 window 尺寸
    status: completed
  - id: integrate-scene-viewer
    content: 在 AirQualityPage.tsx 中引入 SceneViewer，替换静态 img 为3D场景渲染，添加 loading 状态和清除按钮
    status: completed
    dependencies:
      - fix-sceneviewer-resize
  - id: verify-and-test
    content: 验证编译无报错，检查区域切换时3D场景正确重建和清除功能
    status: completed
    dependencies:
      - integrate-scene-viewer
---

## 产品概述

在空气质量页面（AirQualityPage.tsx）中，将当前静态图片+假加载动画的中间面板替换为真实的3D场景渲染组件（SceneViewer），实现"用户选择区域 -> 自动查找匹配的3D模型档案 -> 渲染3D场景"的完整闭环。

## 核心功能

- **区域选择状态管理**：利用已有的 `useArea()` Hook 和 `selectedArea` 状态，监听区域选择变化
- **3D模型档案查找**：利用已有的 `current3DArchive`（useMemo），从中提取 `sceneConfig` 供3D渲染使用
- **3D场景动态渲染**：将 `sceneConfig` 传递给已有的 `SceneViewer` 组件，替换当前 `<img>` 静态图片和假的"3D场景渲染中..."动画
- **加载状态与异常处理**：添加 `loading` 状态变量，用 `try/catch` 包裹档案查找逻辑，异常时记录日志并重置
- **用户体验优化**：添加"清除选择"按钮，使用 `key` 属性确保 SceneViewer 组件在切换区域时完全重建，保留空气质量指标覆盖层

## 技术栈

- **前端框架**：React + TypeScript
- **3D渲染**：Three.js（已有 SceneViewer 组件封装）
- **UI组件**：Tailwind CSS + lucide-react 图标库
- **状态管理**：React Context（useArea、useArchive）+ useState/useEffect/useMemo

## 实现方案

### 核心策略

任务1和2实际上已在现有代码中部分实现（`useArea()` 已引入、`current3DArchive` 已通过 useMemo 查找）。关键改动集中在**任务3**（替换 `<img>` 为 `SceneViewer`）和**任务4-5**（加载状态、清除按钮、key属性）。

### 关键技术决策

1. **保留现有区域导航方式**：当前 `AirQualityPage` 使用内部自建的 `AreaTreeNode` 组件（而非 `AreaTreeSelect`），已能正常工作且样式与页面主题一致。任务描述中提到的 `AreaTreeSelect` 接口与现有组件不匹配（现有用 `onSelect(areaName: string)` 而非 `onChange(name, id)`）。为减少改动风险，保留当前区域导航不变。

2. **从 `current3DArchive` 提取 `sceneConfig`**：已有的 `current3DArchive`（第268-277行）已实现了按 `category === '空气质量'` 优先、再回退到其他3D图纸的查找逻辑。直接从中读取 `sceneConfig` 字段即可，无需额外定义 `findCADArchiveByArea` 函数。

3. **SceneViewer 容器尺寸适配**：当前 `SceneViewer` 的 `handleResize` 使用 `window.innerWidth/innerHeight` 计算相机宽高比，但在 AirQualityPage 中 SceneViewer 并非全屏而是嵌入中间面板。需修改 `SceneViewer` 使用容器尺寸（`containerRef.current.clientWidth/clientHeight`）替代 window 尺寸。

4. **key 属性确保组件重建**：在 SceneViewer 上设置 `key={current3DArchive.id}` 或 `key={sceneConfig.areaId}`，确保切换区域时 SceneViewer 完全卸载重建，避免 Three.js 场景缓存残留。

5. **保留空气质量指标覆盖层**：将当前覆盖在 `<img>` 上的空气质量指标卡片（新风风量、排风风量、烟雾浓度、联动效率）改为覆盖在 `SceneViewer` 容器之上，通过 `absolute` 定位保持原有布局。

### 数据流

```
用户点击区域 → selectedArea 更新 → current3DArchive useMemo 重算
→ sceneConfig 提取 → SceneViewer 渲染3D场景（+ 覆盖层显示指标卡片）
```

### 性能考量

- `current3DArchive` 使用 `useMemo` 缓存，仅在 `selectedArea` 变化时重算，时间复杂度 O(n)
- `SceneViewer` 通过 `key` 属性完全重建而非复用，确保无GPU资源泄漏（组件卸载时已有 `disposeScene` 清理逻辑）
- 切换区域时的 Three.js 场景重建有一定开销，但在数据量级（2-3个设备标注）下可忽略

### 实现注意事项

- **SceneViewer handleResize 修改**：将 `window.innerWidth / window.innerHeight` 改为基于 `containerRef.current` 的 `clientWidth / clientHeight`，同时 `renderer.setSize` 也需使用容器尺寸。这对 SceneViewer 是全局性的修改，但属于bug修复——当前在任何非全屏场景下宽高比都会错误。
- **保留异常处理**：`current3DArchive` 当前为 `null` 的情况已有处理（显示"请选择区域"提示）。对于 `sceneConfig` 存在但加载失败的情况，SceneViewer 内部已有 error 状态展示（含重试按钮）。
- **向后兼容**：不修改 AreaTreeNode 组件的行为和接口，不修改 archivesData 数据结构，不修改 useArea/useArchive 的逻辑。

## 目录结构

```
Web/src/app/
├── pages/
│   └── AirQualityPage.tsx         # [MODIFY] 核心修改：引入SceneViewer，替换img为3D渲染，添加loading状态和清除按钮
├── components/
│   └── 3d-scene/
│       └── SceneViewer.tsx        # [MODIFY] 修复handleResize使用容器尺寸替代window尺寸
├── data/
│   └── archivesData.ts            # [NO CHANGE] 数据源不变
└── contexts/
    ├── AreaContext.tsx             # [NO CHANGE]
    └── ArchiveContext.tsx          # [NO CHANGE]
```