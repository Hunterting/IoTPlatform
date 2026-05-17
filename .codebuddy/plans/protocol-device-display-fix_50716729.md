---
name: protocol-device-display-fix
overview: 修复协议管理中添加设备后设备卡片不显示的问题，添加缺失的 status 字段
todos:
  - id: fix-handle-add-devices-status
    content: 修改 handleAddDevices 函数，添加 status 字段传递
    status: completed
  - id: fix-handle-remove-device-status
    content: 修改 handleRemoveDevice 函数，添加 status 字段传递
    status: completed
    dependencies:
      - fix-handle-add-devices-status
---

## 用户需求

修复协议管理展开详情里面的设备卡片信息不显示的问题

## 问题分析

1. 前端 `handleAddDevices` 调用 `updateProtocolConfig` 时缺少 `status` 字段
2. 虽然后端成功保存了 deviceIds（返回 items 有 deviceIds: [2]），但由于 status 字段缺失可能导致数据更新异常
3. 需要在调用时传递 status 字段（从协议当前状态获取）

## 核心功能

在 `handleAddDevices` 和 `handleRemoveDevice` 中添加 `status` 字段传递，从协议当前状态获取（protocol.status 或 'active'）

## 技术方案

### 修改位置

- `h:/IoTPlatform/Web/src/app/pages/ProtocolManagementPage.tsx`

### 修改内容

#### 1. 修改 `handleAddDevices` 函数（第380行附近）

**原代码**：

```typescript
const response = await protocolApi.updateProtocolConfig(selectingProtocolId, {
  name: protocol.name,
  type: protocol.type,
  description: protocol.description,
  isActive: protocol.isActive,
  config: protocol.config,
  deviceIds: allIds,
});
```

**修改为**：

```typescript
const response = await protocolApi.updateProtocolConfig(selectingProtocolId, {
  name: protocol.name,
  type: protocol.type,
  status: protocol.status || 'active',
  description: protocol.description,
  isActive: protocol.isActive,
  config: protocol.config,
  deviceIds: allIds,
});
```

#### 2. 修改 `handleRemoveDevice` 函数（第417行附近）

**原代码**：

```typescript
const response = await protocolApi.updateProtocolConfig(protocol.id, {
  name: protocol.name,
  type: protocol.type,
  description: protocol.description,
  isActive: protocol.isActive,
  config: protocol.config,
  deviceIds: newIds,
});
```

**修改为**：

```typescript
const response = await protocolApi.updateProtocolConfig(protocol.id, {
  name: protocol.name,
  type: protocol.type,
  status: protocol.status || 'active',
  description: protocol.description,
  isActive: protocol.isActive,
  config: protocol.config,
  deviceIds: newIds,
});
```

### 修改原因

- 后端 `UpdateProtocolConfigRequest` 定义了 `Status` 字段
- 前端调用时缺少此字段可能导致后端数据处理异常
- 添加 status 字段后，后端能正确更新协议状态，设备关联数据也能正确保存和返回