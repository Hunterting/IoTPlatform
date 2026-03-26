# 空气质量页面区域3D场景可视化任务计划

- [ ] 任务 1：在空气质量页面中集成区域选择状态管理与监听
    - 1.1: 在`AirQualityPage.tsx`中引入`useArea` Hook，获取当前区域列表和可访问区域ID
    - 1.2: 添加`selectedAreaId`状态变量，用于跟踪用户选中的区域
    - 1.3: 实现`useEffect`监听`selectedAreaId`变化，触发档案查找逻辑
    - 1.4: 在区域树选择器（`AreaTreeSelect`）中添加`onSelect`回调，更新`selectedAreaId`

- [ ] 任务 2：实现查找关联3D模型档案的函数
    - 2.1: 在`AirQualityPage.tsx`中定义`findCADArchiveByArea`函数
    - 2.2: 函数参数为`areaId`和`appCode`，返回匹配的`archive`对象
    - 2.3: 使用`archivesData`数组进行过滤，条件为`is3DModel === true`且`areaId === areaId`
    - 2.4: 返回第一个匹配项或`undefined`

- [ ] 任务 3：动态加载并渲染3D场景视图
    - 3.1: 在页面主布局中添加`SceneViewer`组件容器
    - 3.2: 根据`sceneConfig`是否存在，使用条件渲染控制显示
    - 3.3: 当`sceneConfig`存在时，将配置传递给`SceneViewer`
    - 3.4: 当`sceneConfig`不存在时，显示“暂无3D模型”占位符

- [ ] 任务 4：处理数据加载状态与异常
    - 4.1: 添加`loading`状态变量，表示正在查找档案
    - 4.2: 在`try/catch`块中包裹档案查找逻辑，防止崩溃
    - 4.3: 异常时记录日志，并将`sceneConfig`置为`null`
    - 4.4: `finally`块中关闭`loading`状态

- [ ] 任务 5：优化用户体验与交互
    - 5.1: 在3D视图右上角添加“清除选择”按钮
    - 5.2: 按钮点击后重置`selectedAreaId`和`sceneConfig`
    - 5.3: 确保`SceneViewer`组件能响应`sceneConfig`变化而重新渲染
    - 5.4: 添加`key`属性确保组件重建，避免缓存问题
