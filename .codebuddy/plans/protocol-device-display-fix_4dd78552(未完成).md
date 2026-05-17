---
name: protocol-device-display-fix
overview: 修复协议管理页面展开详情时设备卡片不显示的问题。添加设备成功后清除协议设备缓存，确保下次展开时重新加载最新数据。
todos:
  - id: fix-handle-add-devices
    content: 修改 handleAddDevices 函数，清除 protocolDevices 缓存
    status: pending
  - id: fix-handle-remove-device
    content: 修改 handleRemoveDevice 函数，清除 protocolDevices 缓存
    status: pending
---

## 用户需求

修复协议管理展开详情里面的设备卡片信息不显示的问题

## 问题分析

1. 添加设备成功后调用 `loadProtocols()` 重新获取协议列表
2. 但 `protocolDevices[protocol.id]` 缓存了旧数据
3. 再次展开详情时，`loadProtocolDevices` 发现已有缓存就直接返回
4. 导致显示的是旧数据或空数据

## 核心功能

在 `handleAddDevices` 和 `handleRemoveDevice` 成功回调中，清除对应协议的 `protocolDevices` 缓存，而不是尝试合并数据。这样下次展开时会正确重新加载。

## 技术方案

### 修改位置

- `h:/IoTPlatform/Web/src/app/pages/ProtocolManagementPage.tsx`

### 修改内容

#### 1. 修改 `handleAddDevices` 函数

**原逻辑（第394-397行）**：

```typescript
setProtocolDevices(prev => ({
  ...prev,
  [selectingProtocolId]: [...(prev[selectingProtocolId] || []), ...allDevices.filter(d => selectedDeviceIds.has(d.id))]
}));
```

**修改为**：直接清除该协议的缓存，让 `loadProtocolDevices` 重新加载

```typescript
// 清除缓存，下次展开时会重新加载
setProtocolDevices(prev => {
  const next = { ...prev };
  delete next[selectingProtocolId];
  return next;
});
```

#### 2. 修改 `handleRemoveDevice` 函数

**原逻辑（第429-432行）**：

```typescript
setProtocolDevices(prev => ({
  ...prev,
  [protocol.id]: (prev[protocol.id] || []).filter(d => d.id !== deviceId)
}));
```

**修改为**：直接清除该协议的缓存，让 `loadProtocolDevices` 重新加载

```typescript
// 清除缓存，下次展开时会重新加载
setProtocolDevices(prev => {
  const next = { ...prev };
  delete next[protocol.id];
  return next;
});
```

### 修改原因

当前的合并逻辑存在问题：

1. `loadProtocolDevices` 有缓存检查 `if (protocolDevices[protocol.id]) return`，如果已有数据就不重新加载
2. 合并时 `allDevices.filter()` 可能找不到对应的设备信息
3. 清除缓存是最简单可靠的方案，下次展开详情时会正确重新加载