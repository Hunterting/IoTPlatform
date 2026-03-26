# 区域CAD图纸3D场景查看功能任务计划

- [x] 任务 1：在MonitoringPage中添加视图模式和CAD档案状态管理
    - 1.1: 新增viewMode状态，类型为'video' | '3d'，默认值为'video'
    - 1.2: 新增selectedCADArchive状态，类型为Archive | null，初始值为null
    - 1.3: 新增selectedDevice状态，类型为DeviceMarker3D | null，初始值为null
    - 1.4: 新增showDeviceDetail状态，类型为boolean，初始值为false
    - 1.5: 导入Archive和DeviceMarker3D类型定义

- [x] 任务 2：实现根据区域查找CAD档案的功能
    - 2.1: 创建findCADArchiveByArea函数，接收areaId和areaName参数
    - 2.2: 函数内部过滤archivesData，筛选条件包括category='图纸资料'、type='blueprint'、is3DModel=true、sceneConfig存在
    - 2.3: 优先按areaId匹配，其次按areaName匹配
    - 2.4: 返回匹配到的第一个CAD档案，无匹配返回null
    - 2.5: 添加useEffect监听selectedArea变化，调用findCADArchiveByArea并更新selectedCADArchive状态

- [x] 任务 3：实现视图模式切换功能和UI
    - 3.1: 创建handleViewModeToggle函数，在'video'和'3d'之间切换viewMode状态
    - 3.2: 在场景区域图标题区域添加切换按钮
    - 3.3: 按钮根据viewMode显示不同文字（'切换到3D场景'或'返回视频监控'）
    - 3.4: 按钮样式根据viewMode使用不同颜色（3d模式用青色，video模式用蓝色）
    - 3.5: 导入必要的图标组件（Grid3x3等）

- [x] 任务 4：修改场景区域图内容区域，实现条件渲染
    - 4.1: 在场景区域图内容区域添加条件渲染逻辑
    - 4.2: viewMode为'video'时，保持原有的摄像头网格渲染逻辑
    - 4.3: viewMode为'3d'且selectedCADArchive存在时，渲染SceneViewer组件
    - 4.4: viewMode为'3d'但selectedCADArchive不存在时，显示"当前区域暂无3D场景"提示
    - 4.5: 提示信息引导用户在档案管理中上传CAD图纸

- [x] 任务 5：集成SceneViewer组件并配置场景参数
    - 5.1: 导入SceneViewer组件
    - 5.2: 在3D视图渲染SceneViewer，传入selectedCADArchive.sceneConfig作为sceneConfig属性
    - 5.3: 设置onDeviceClick回调，调用handleDeviceClick处理设备点击
    - 5.4: 设置onHoverDevice回调，处理设备悬停（可暂时留空或输出日志）
    - 5.5: 设置className为'w-full h-full'以占满容器

- [x] 任务 6：实现设备点击处理和详情面板功能
    - 6.1: 创建handleDeviceClick函数，接收deviceId和deviceData参数
    - 6.2: 函数内部更新selectedDevice和showDeviceDetail状态
    - 6.3: 在页面右侧添加设备详情面板（与实时报警面板并列）
    - 6.4: 面板显示设备名称、类型、型号、序列号、关联传感器等信息
    - 6.5: 添加关闭按钮和"查看完整设备信息"按钮
    - 6.6: 样式使用渐变背景和边框，保持与现有UI一致

- [x] 任务 7：优化3D场景切换逻辑和用户体验
    - 7.1: 在区域选择的useEffect中，找到CAD档案后自动切换到3D视图
    - 7.2: 未找到CAD档案时保持视频视图
    - 7.3: 在3D场景标题显示当前CAD档案名称
    - 7.4: 添加视图切换时的过渡动画效果
    - 7.5: 处理边界情况：区域切换时清理之前的选择状态

- [x] 任务 8：添加错误处理和加载状态
    - 8.1: SceneViewer组件已内置加载和错误状态，确保正确传递props
    - 8.2: 在3D视图添加加载提示（SceneViewer的loading状态覆盖）
    - 8.3: 添加无场景数据的友好提示信息
    - 8.4: 处理设备数据缺失的降级显示（显示可用字段，缺失显示"未知"）
    - 8.5: 添加必要的注释说明代码逻辑

- [x] 任务 9：验证功能完整性和代码质量
    - 9.1: 测试选择有CAD档案的区域，验证自动切换到3D视图
    - 9.2: 测试选择无CAD档案的区域，验证显示友好提示
    - 9.3: 测试视图切换按钮，验证视频和3D视图正常切换
    - 9.4: 测试3D场景中的设备交互，验证点击设备显示详情面板
    - 9.5: 测试设备详情面板的关闭和跳转功能
    - 9.6: 检查代码风格是否符合项目规范
    - 9.7: 确认类型定义正确，无TypeScript错误
