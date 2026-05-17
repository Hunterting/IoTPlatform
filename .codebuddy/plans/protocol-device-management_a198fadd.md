---
name: protocol-device-management
overview: 在协议管理页面展开详情中添加关联设备的添加（多选）和删除功能，后端通过 updateProtocolConfig 接口更新 DeviceIds 字段
todos:
  - id: add-device-selector-state
    content: 添加设备选择弹窗相关状态
    status: completed
  - id: create-device-select-modal
    content: 创建设备选择弹窗组件（支持多选搜索）
    status: completed
  - id: add-device-card-delete
    content: 为关联设备卡片添加删除按钮
    status: completed
  - id: implement-delete-device
    content: 实现删除设备功能（调用 updateProtocolConfig）
    status: completed
    dependencies:
      - add-device-card-delete
  - id: implement-add-device
    content: 实现添加设备功能（打开弹窗→选择→调用 API）
    status: completed
    dependencies:
      - create-device-select-modal
      - add-device-selector-state
  - id: test-and-verify
    content: 测试验证功能完整性
    status: completed
    dependencies:
      - implement-delete-device
      - implement-add-device
---

# 协议管理 - 关联设备管理功能

## 用户需求

为协议管理页面展开详情的关联设备区域添加"添加设备"和"删除设备"功能，后端通过更新 `DeviceIds` 字段实现设备关联管理。

## UI 设计

### 设备卡片显示

```
┌──────────────────────────────────────────────────────────┐
│ [设备图标]  设备名称                                       │
│            项目: 所属项目名称                              │
│            区域: 区域名称                       [删除×]  │
│            状态: [在线/离线/警告]                          │
└──────────────────────────────────────────────────────────┘
```

### 字段说明

| 字段 | 来源 | 说明 |
| --- | --- | --- |
| 设备名称 | `device.name` | 设备主名称 |
| 所属项目 | `device.projectName` | 所属项目名称，无则显示"未分配" |
| 区域 | `device.areaName` | 所属区域名称，无则显示"未分配" |
| 状态 | `device.status` | 在线/离线/警告，带颜色标签 |
| 序列号 | `device.serialNumber` | 设备序列号（可选显示） |


### 删除按钮

- 位于卡片右上角
- 点击后直接删除（无确认）
- 仅 `canManage` 权限用户可见

---

## 实施步骤

| # | 任务 | 说明 |
| --- | --- | --- |
| 1 | 添加设备选择弹窗状态 | `showDeviceSelector`, `updatingDevices` 等 |
| 2 | 创建设备选择弹窗 | 多选列表、搜索过滤、已选设备高亮 |
| 3 | 更新设备卡片UI | 显示：名称、项目名、区域名、状态、删除按钮 |
| 4 | 实现删除设备功能 | 调用 `updateProtocolConfig` 移除设备 |
| 5 | 实现添加设备功能 | 选择设备 → 调用 `updateProtocolConfig` 添加 |


---

## 后端状态

- ✅ `UpdateProtocolConfigRequest` 已包含 `List<long>? DeviceIds` 字段
- ✅ `ProtocolConfigService.UpdateProtocolConfigAsync` 已处理 deviceIds 更新
- ✅ 前端 `protocolApi.updateProtocolConfig` 已支持传递 `deviceIds`

---

## 文件修改清单

- `ProtocolManagementPage.tsx` - 主要修改文件