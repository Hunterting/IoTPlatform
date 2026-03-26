# 任务7：优化3D场景切换逻辑和用户体验 - 修改指南

## 检查点：已完成的子任务
- ✅ 7.1: 在区域选择的useEffect中，找到CAD档案后自动切换到3D视图（第258-259行）
- ✅ 7.2: 未找到CAD档案时保持视频视图（第260-262行）
- ✅ 7.3: 在3D场景标题显示当前CAD档案名称（第361行）
- ✅ 7.5: 处理边界情况：区域切换时清理之前的选择状态（第263-266行）

## 需要完成的子任务
- ❌ 7.4: 添加视图切换时的过渡动画效果
- ⚠️ 7.5: 补充清理设备详情状态

## 修改1：补充设备详情状态清理（7.5补充）

**位置：** 第263-266行的else块中

**当前代码：**
```typescript
    } else {
      setSelectedCADArchive(null);
      setViewMode('video');
    }
```

**修改为：**
```typescript
    } else {
      setSelectedCADArchive(null);
      setViewMode('video');
      setSelectedDevice(null);
      setShowDeviceDetail(false);
    }
```

**说明：** 当区域选择被清除时，同时清理设备详情面板状态

---

## 修改2：添加视图切换时的过渡动画效果（7.4）

由于代码块较大，建议分两步修改：

### 步骤2.1：为视频监控视图添加AnimatePresence和motion动画

**位置：** 第374-424行

**查找内容：**
```typescript
            {/* 视频监控视图 */}
            {viewMode === 'video' && (
              <>
            {/* 摄像头网格 */}
            <div className="flex-1 grid grid-cols-4 grid-rows-4 gap-1 overflow-hidden">
```

**替换为：**
```typescript
            <AnimatePresence mode="wait">
            {/* 视频监控视图 */}
            {viewMode === 'video' && (
              <motion.div
                initial={{ opacity: 0, x: -20 }}
                animate={{ opacity: 1, x: 0 }}
                exit={{ opacity: 0, x: 20 }}
                transition={{ duration: 0.3 }}
                className="flex-1"
              >
            {/* 摄像头网格 */}
            <div className="h-full grid grid-cols-4 grid-rows-4 gap-1 overflow-hidden">
```

**步骤2.2：修改视频视图的结束标签**

**查找内容：**
```typescript
            </div>
              </>
            )}

            {/* 3D场景视图 */}
```

**替换为：**
```typescript
            </div>
              </motion.div>
            )}
            </AnimatePresence>

            {/* 3D场景视图 */}
```

### 步骤2.3：为3D场景视图添加动画

**查找内容：**
```typescript
            {/* 3D场景视图 */}
            {viewMode === '3d' && selectedCADArchive?.sceneConfig && (
              <div className="flex-1 relative">
```

**替换为：**
```typescript
            {/* 3D场景视图 */}
            {viewMode === '3d' && selectedCADArchive?.sceneConfig && (
              <motion.div
                initial={{ opacity: 0, scale: 0.95 }}
                animate={{ opacity: 1, scale: 1 }}
                exit={{ opacity: 0, scale: 0.95 }}
                transition={{ duration: 0.3 }}
                className="flex-1 relative"
              >
```

### 步骤2.4：修改3D场景视图的结束标签

**查找内容：**
```typescript
                />
              </div>
            )}

            {/* 无CAD档案提示 */}
```

**替换为：**
```typescript
                />
              </motion.div>
            )}

            {/* 无CAD档案提示 */}
```

### 步骤2.5：为无CAD档案提示添加动画

**查找内容：**
```typescript
            {/* 无CAD档案提示 */}
            {viewMode === '3d' && !selectedCADArchive?.sceneConfig && (
              <div className="flex-1 flex items-center justify-center">
```

**替换为：**
```typescript
            {/* 无CAD档案提示 */}
            {viewMode === '3d' && !selectedCADArchive?.sceneConfig && (
              <motion.div
                initial={{ opacity: 0, y: 20 }}
                animate={{ opacity: 1, y: 0 }}
                exit={{ opacity: 0, y: -20 }}
                transition={{ duration: 0.3 }}
                className="flex-1 flex items-center justify-center"
              >
```

### 步骤2.6：修改无提示的结束标签

**查找内容：**
```typescript
                </div>
              </div>
            )}
          </div>

          {/* 底部工具栏 */}
```

**替换为：**
```typescript
                </div>
              </motion.div>
            )}
          </div>

          {/* 底部工具栏 */}
```

## 动画效果说明

### 视频监控视图
- 入场：从左侧滑入（x: -20 → 0），淡入（opacity: 0 → 1）
- 出场：向右侧滑出（x: 0 → 20），淡出（opacity: 1 → 0）
- 持续时间：0.3秒

### 3D场景视图
- 入场：缩放入场（scale: 0.95 → 1），淡入（opacity: 0 → 1）
- 出场：缩放出场（scale: 1 → 0.95），淡出（opacity: 1 → 0）
- 持续时间：0.3秒

### 无CAD档案提示
- 入场：从下方滑入（y: 20 → 0），淡入（opacity: 0 → 1）
- 出场：向上方滑出（y: 0 → -20），淡出（opacity: 1 → 0）
- 持续时间：0.3秒

## 注意事项

1. **AnimatePresence mode="wait"**：确保新视图在旧视图完全离开后才开始进入，避免动画重叠
2. **motion.div**：使用Framer Motion的motion组件包裹内容
3. **className调整**：视频视图的flex-1从内部div移到motion.div，h-full添加到内部div以保持高度
4. **过渡时长统一**：所有视图使用0.3秒的过渡时间，保持一致的动画节奏
5. **不同的动画方向**：
   - 视频：横向滑动（模拟切换）
   - 3D场景：缩放（模拟进入/退出3D空间）
   - 提示：纵向滑动（模拟提示消息弹出）