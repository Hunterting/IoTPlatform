---
name: device-control-enhancement
overview: 为IoT平台设备控制页面添加"添加到指令控制"功能，允许用户从设备列表选择设备并注册到指令控制系统，之后可快速发送各种指令。
todos:
  - id: create-model
    content: 创建 ControlledDevice.cs 受控设备数据模型
    status: completed
  - id: update-dbcontext
    content: 更新 AppDbContext.cs 添加 DbSet 和配置
    status: completed
  - id: create-interface
    content: 创建 IControlledDeviceService.cs 服务接口
    status: completed
  - id: create-service
    content: 创建 ControlledDeviceService.cs 服务实现
    status: completed
  - id: create-controller
    content: 创建 ControlledDevicesController.cs API控制器
    status: completed
  - id: register-service
    content: 注册服务到 Program.cs
    status: completed
  - id: add-api-types
    content: 前端添加受控设备相关类型定义
    status: completed
  - id: add-api-functions
    content: 前端添加 API 函数
    status: completed
  - id: update-ui
    content: 更新 DeviceControlPage.tsx 添加订阅功能
    status: completed
---

## 用户需求

在设备控制页面添加功能：通过弹窗从系统设备列表中选择设备，将设备添加到"指令控制系统"（受控设备），方便后续快速发送各种指令。

## 核心功能

1. **受控设备注册** - 从设备列表选择设备，保存到受控设备表
2. **受控设备管理** - 查看、编辑备注/优先级、取消注册
3. **快速发送指令** - 对已注册的受控设备快速发送控制指令
4. **设备状态同步** - 显示设备当前在线/离线状态

## 技术方案

### 后端

1. 创建 `ControlledDevice.cs` - 受控设备数据模型
2. 更新 `AppDbContext.cs` - 添加 DbSet 和配置
3. 创建 `IControlledDeviceService.cs` - 服务接口
4. 创建 `ControlledDeviceService.cs` - 服务实现
5. 创建 `ControlledDevicesController.cs` - API 控制器
6. 注册服务到 Program.cs

### 前端

1. 添加 API 函数到 `deviceCommandApi.ts`
2. 更新 `DeviceControlPage.tsx` - 添加设备订阅功能

## 修改文件清单

```
Models/ControlledDevice.cs           [NEW] 受控设备数据模型
Services/Interfaces/IControlledDeviceService.cs  [NEW] 服务接口
Services/ControlledDeviceService.cs   [NEW] 服务实现
Controllers/ControlledDevicesController.cs  [NEW] API控制器
Data/AppDbContext.cs                  [MODIFY] 添加 DbSet
Program.cs                            [MODIFY] 注册服务
Web/src/app/services/api/deviceCommandApi.ts  [MODIFY] 添加API函数
Web/src/app/pages/DeviceControlPage.tsx  [MODIFY] 添加订阅功能
```

## 技术栈

- 后端：ASP.NET Core + Entity Framework Core
- 前端：React + TypeScript + Tailwind CSS
- 数据库：SQL Server

## 实现方案

### 1. 数据模型 (ControlledDevice.cs)

- 存储已添加到指令控制系统的设备
- 包含设备信息冗余存储（便于显示）
- 支持备注、优先级、启用/禁用状态

### 2. 服务层

- `IControlledDeviceService` 接口定义注册/查询/取消等方法
- `ControlledDeviceService` 实现，支持租户隔离

### 3. API 端点

```
POST   /api/v1/controlled-devices/register      - 注册受控设备
POST   /api/v1/controlled-devices/register/batch - 批量注册
DELETE /api/v1/controlled-devices/{id}          - 取消注册
GET    /api/v1/controlled-devices               - 获取受控设备列表
GET    /api/v1/controlled-devices/{id}           - 获取单个受控设备
PUT    /api/v1/controlled-devices/{id}           - 更新受控设备
```

### 4. 前端功能

- 设备卡片添加"添加到控制"按钮
- 已注册设备显示快速发送指令入口
- 设备列表按"是否已注册"分类显示