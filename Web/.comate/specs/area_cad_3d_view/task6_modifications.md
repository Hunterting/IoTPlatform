# 任务6：实现设备点击处理和详情面板功能 - 手动修改指南

## 修改位置
文件：`src/app/pages/MonitoringPage.tsx`
插入位置：第554行后（右侧实时报警面板结束后）

## 检查点：已完成的子任务
- ✅ 6.1: handleDeviceClick函数已创建（第275-278行）
- ✅ 6.2: 函数内部已更新selectedDevice和showDeviceDetail状态

## 需要添加的代码
在第554行 `</div>` 后、第555行 `</div>` 前插入以下代码：

```tsx
        {/* 设备详情面板 */}
        {showDeviceDetail && selectedDevice && (
          <div className="w-64 bg-gradient-to-br from-[#1a1f35] to-[#0f1321] rounded-lg border border-cyan-500/30 p-3 flex flex-col overflow-hidden">
            <div className="flex items-center justify-between mb-3 flex-shrink-0">
              <h3 className="text-sm font-bold text-white">设备详情</h3>
              <button
                onClick={() => setShowDeviceDetail(false)}
                className="p-1 hover:bg-white/10 rounded-lg transition-colors"
              >
                <X className="w-4 h-4 text-gray-400" />
              </button>
            </div>

            {/* 设备信息 */}
            <div className="flex-1 overflow-y-auto custom-scrollbar space-y-3">
              {/* 设备名称 */}
              <div>
                <p className="text-xs text-gray-400 mb-1">设备名称</p>
                <p className="text-white font-medium">{selectedDevice.name || '未知'}</p>
              </div>

              {/* 设备类型 */}
              <div>
                <p className="text-xs text-gray-400 mb-1">设备类型</p>
                <p className="text-white">{selectedDevice.type || '未知'}</p>
              </div>

              {/* 设备型号 */}
              {selectedDevice.model && (
                <div>
                  <p className="text-xs text-gray-400 mb-1">设备型号</p>
                  <p className="text-white">{selectedDevice.model}</p>
                </div>
              )}

              {/* 序列号 */}
              {selectedDevice.serialNumber && (
                <div>
                  <p className="text-xs text-gray-400 mb-1">序列号</p>
                  <p className="text-white font-mono text-sm">{selectedDevice.serialNumber}</p>
                </div>
              )}

              {/* 关联传感器 */}
              {selectedDevice.sensors && selectedDevice.sensors.length > 0 && (
                <div>
                  <p className="text-xs text-gray-400 mb-1">关联传感器</p>
                  <div className="space-y-1">
                    {selectedDevice.sensors.map((sensor, index) => (
                      <div
                        key={index}
                        className="bg-white/5 border border-white/10 rounded p-2"
                      >
                        <p className="text-xs text-white">{sensor.name || '未知传感器'}</p>
                        {sensor.value !== undefined && (
                          <p className="text-[10px] text-gray-400">数值: {sensor.value}</p>
                        )}
                        {sensor.status && (
                          <div className="flex items-center gap-1 mt-1">
                            <div className={`w-2 h-2 rounded-full ${
                              sensor.status === 'normal' ? 'bg-green-500' : 'bg-red-500'
                            }`} />
                            <span className="text-[10px] text-gray-500">{sensor.status}</span>
                          </div>
                        )}
                      </div>
                    ))}
                  </div>
                </div>
              )}
            </div>

            {/* 操作按钮 */}
            <div className="mt-3 space-y-2 flex-shrink-0">
              <button
                onClick={() => setShowDeviceDetail(false)}
                className="w-full py-2 bg-cyan-500/20 hover:bg-cyan-500/30 border border-cyan-500/40 rounded text-xs text-cyan-300 transition-colors"
              >
                查看完整设备信息
              </button>
            </div>
          </div>
        )}
```

## 修改说明

### 6.3: 在页面右侧添加设备详情面板（与实时报警面板并列）
- 面板使用条件渲染：`{showDeviceDetail && selectedDevice && (...)}`
- 位置：在实时报警面板结束后，放在主内容区域内
- 宽度：w-64（与实时报警面板一致）

### 6.4: 面板显示设备信息
- 设备名称：始终显示，缺失时显示"未知"
- 设备类型：始终显示，缺失时显示"未知"
- 设备型号：条件显示（selectedDevice.model存在时）
- 序列号：条件显示（selectedDevice.serialNumber存在时），使用等宽字体
- 关联传感器：条件显示（selectedDevice.sensors存在且非空时）
  - 每个传感器显示名称、数值、状态
  - 传感器状态用颜色指示器（绿色/红色）

### 6.5: 添加关闭按钮和"查看完整设备信息"按钮
- 关闭按钮：右上角X图标，点击设置showDeviceDetail为false
- "查看完整设备信息"按钮：底部按钮，点击也关闭面板（可后续扩展跳转逻辑）

### 6.6: 样式使用渐变背景和边框
- 背景：bg-gradient-to-br from-[#1a1f35] to-[#0f1321]（与现有面板一致）
- 边框：border border-cyan-500/30（使用青色以区分实时报警的蓝色）
- 标题：text-sm font-bold text-white
- 信息项：text-xs text-gray-400（标签）、text-white（值）
- 按钮：bg-cyan-500/20 hover:bg-cyan-500/30 border border-cyan-500/40
- 自定义滚动条：custom-scrollbar

## 注意事项
1. 确保代码插入在第554行后，保持正确的缩进
2. 设备详情面板与实时报警面板并列显示在页面右侧区域
3. 使用青色（cyan）主题以与蓝色（blue）的实时报警面板区分
4. 处理设备数据缺失的情况，显示"未知"或跳过该字段
5. 传感器状态指示器颜色：normal=green，其他=red