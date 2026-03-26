# 任务3：实现视图模式切换功能和UI - 手动修改指南

## 修改位置
文件：`src/app/pages/MonitoringPage.tsx`
行号：359-361

## 原始代码（第359-361行）
```tsx
            <h3 className="text-sm font-bold text-white mb-2 flex-shrink-0">场景区域图</h3>
            
            {/* 摄像头网格 */}
```

## 替换为以下代码
```tsx
            <div className="flex items-center justify-between mb-2 flex-shrink-0">
              <h3 className="text-sm font-bold text-white">
                {viewMode === 'video' ? '场景区域图' : `3D场景 - ${selectedCADArchive?.name || '未知'}`}
              </h3>
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

            {/* 摄像头网格 */}
```

## 修改说明
1. 将单一的`<h3>`标签改为flex布局容器，包含标题和按钮
2. 标题根据`viewMode`状态动态显示：视频模式显示"场景区域图"，3D模式显示"3D场景 - {CAD档案名称}"
3. 添加切换按钮，点击调用`handleViewModeToggle`函数
4. 按钮样式根据`viewMode`动态变化：3D模式用青色(cyan)，视频模式用蓝色(blue)
5. 按钮文字根据状态显示："切换到3D场景"或"返回视频监控"

## 状态检查
- [x] 3.1: handleViewModeToggle函数已创建（第270-272行）
- [ ] 3.2: 在场景区域图标题区域添加切换按钮（待手动完成）
- [ ] 3.3: 按钮根据viewMode显示不同文字（待手动完成）
- [ ] 3.4: 按钮样式根据viewMode使用不同颜色（待手动完成）
- [x] 3.5: 图标组件已导入（Box等，第21行；Grid3x3已在文件中）