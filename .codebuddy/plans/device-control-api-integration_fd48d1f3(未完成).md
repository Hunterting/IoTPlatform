---
name: device-control-api-integration
overview: 将设备控制页面 DeviceControlPage.tsx 从 Mock 数据改为对接真实后端 API，包括设备列表查询、指令发送、指令历史查询、取消和重试等功能。
todos:
  - id: add-api-imports
    content: 添加 deviceApi 和设备类型导入
    status: pending
  - id: remove-mock-data
    content: 删除 MOCK_DEVICES 和 MOCK_COMMANDS 静态数据
    status: pending
  - id: add-state-management
    content: 添加设备列表状态和加载状态
    status: pending
    dependencies:
      - add-api-imports
  - id: implement-device-loading
    content: 实现 useEffect 加载设备列表
    status: pending
    dependencies:
      - add-state-management
  - id: implement-command-loading
    content: 实现 useEffect 加载指令历史列表
    status: pending
  - id: implement-send-command
    content: 修改 handleSend 调用 deviceCommandApi.sendCommand()
    status: pending
  - id: implement-cancel-command
    content: 修改 handleCancel 调用 deviceCommandApi.cancelCommand()
    status: pending
  - id: implement-retry-command
    content: 修改 handleRetry 调用 deviceCommandApi.retryCommand()
    status: pending
  - id: implement-command-history
    content: 修改 toggleCommandExpand 调用 deviceCommandApi.getCommandHistory()
    status: pending
  - id: add-loading-ui
    content: 添加加载状态和错误提示 UI
    status: pending
    dependencies:
      - implement-device-loading
      - implement-command-loading
---

## 用户需求

将前端设备控制页面 (DeviceControlPage.tsx) 根据后端接口改成使用真实 API

## 现状分析

### Mock 数据需要替换

1. **设备列表**：第30-36行 `MOCK_DEVICES` 静态数据 → 需用 `deviceApi.getDevices()` 替换
2. **指令历史**：第113-137行 `MOCK_COMMANDS` 静态数据 → 需用 `deviceCommandApi.getCommands()` 替换
3. **发送指令**：`handleSend` 中 mock 延迟和本地状态更新 → 需用 `deviceCommandApi.sendCommand()` 替换
4. **取消指令**：`handleCancel` 中注释掉的 API 调用 → 需用 `deviceCommandApi.cancelCommand()` 替换
5. **重试指令**：`handleRetry` 中注释掉的 API 调用 → 需用 `deviceCommandApi.retryCommand()` 替换
6. **指令历史详情**：`toggleCommandExpand` 中 mock 历史记录 → 需用 `deviceCommandApi.getCommandHistory()` 替换

### 后端 API 已就绪

- `GET /api/v1/devices` - 设备列表
- `POST /api/v1/device-commands/send` - 发送指令
- `GET /api/v1/device-commands` - 指令列表
- `GET /api/v1/device-commands/{commandId}/history` - 指令历史
- `POST /api/v1/device-commands/{commandId}/cancel` - 取消指令
- `POST /api/v1/device-commands/{commandId}/retry` - 重试指令

### 前端 API 服务已准备好

- `deviceApi` (deviceApi.ts) - 设备列表接口
- `deviceCommandApi` (deviceCommandApi.ts) - 指令接口

## 核心功能

- 设备列表从后端加载，支持搜索和状态筛选
- 指令历史从后端加载，支持搜索和状态筛选
- 发送指令调用真实 API 并显示结果
- 取消/重试指令调用真实 API
- 展开指令详情时加载真实历史记录
- 添加加载状态和错误处理 UI

## 技术方案

### 技术栈

- React + TypeScript
- Axios HTTP 客户端
- Tailwind CSS 样式
- Lucide React 图标库

### 实现策略

1. **保留现有 UI 结构和样式**，仅替换数据源
2. **添加加载状态**：设备列表和指令列表加载时显示 loading
3. **错误处理**：API 调用失败时显示错误提示
4. **实时刷新**：操作成功后自动刷新列表

### API 路径映射

| 功能 | HTTP 方法 | 路径 | 前端方法 |
| --- | --- | --- | --- |
| 设备列表 | GET | `/api/v1/devices` | `deviceApi.getDevices()` |
| 发送指令 | POST | `/api/v1/device-commands/send` | `deviceCommandApi.sendCommand()` |
| 指令列表 | GET | `/api/v1/device-commands` | `deviceCommandApi.getCommands()` |
| 指令历史 | GET | `/api/v1/device-commands/{id}/history` | `deviceCommandApi.getCommandHistory()` |
| 取消指令 | POST | `/api/v1/device-commands/{id}/cancel` | `deviceCommandApi.cancelCommand()` |
| 重试指令 | POST | `/api/v1/device-commands/{id}/retry` | `deviceCommandApi.retryCommand()` |


### 数据类型适配

- 后端 `DeviceDto.id` 是 `long`，前端转换为 `string`
- 后端 `DeviceCommandDto.id` 是 `long`，前端保留为 `number`
- 统一使用前端已定义的类型接口