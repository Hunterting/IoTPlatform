---
name: data-collection-api-integration
overview: 将前端数据采集模块的9个子模块改为调用真实API，后端缺失的API（网关、隧道、插件、数据库配置）需要新建。
todos:
  - id: backend-gateway
    content: 创建Gateway模型、Service、Controller（协议网关）
    status: completed
  - id: backend-tunnel
    content: 创建Tunnel模型、Service、Controller（网络隧道）
    status: completed
  - id: backend-plugin
    content: 创建Plugin模型、Service、Controller（插件）
    status: completed
  - id: backend-database-config
    content: 创建DatabaseConfig模型、Service、Controller（数据库配置）
    status: completed
  - id: backend-registration
    content: 更新AppDbContext和Program.cs注册服务
    status: completed
    dependencies:
      - backend-gateway
      - backend-tunnel
      - backend-plugin
      - backend-database-config
  - id: frontend-apis
    content: 创建6个前端API服务文件
    status: completed
  - id: frontend-protocol-config
    content: 对接协议配置页面（ProtocolConfigPage）
    status: completed
    dependencies:
      - frontend-apis
  - id: frontend-rule-engine
    content: 对接规则引擎页面（RuleEnginePage）
    status: completed
    dependencies:
      - frontend-apis
  - id: frontend-etl
    content: 对接ETL任务页面（DataTransformPageNew）
    status: completed
    dependencies:
      - frontend-apis
  - id: frontend-gateway
    content: 对接协议网关页面（ProtocolGatewayPage）
    status: completed
    dependencies:
      - frontend-apis
  - id: frontend-tunnel
    content: 对接网络隧道页面（NetworkTunnelPage）
    status: completed
    dependencies:
      - frontend-apis
  - id: frontend-plugin
    content: 对接插件系统页面（PluginSystemPage）
    status: completed
    dependencies:
      - frontend-apis
  - id: frontend-database-config
    content: 对接数据库配置页面（DatabaseConfigPage）
    status: completed
    dependencies:
      - frontend-apis
---

## 产品概述

数据采集模块是IoT平台的核心功能页面，包含9个子模块。目前所有子模块使用本地useState模拟数据，需要改为调用真实API。

## 核心功能

### 后端已有API（直接对接）

1. **协议配置** - ProtocolConfigsController（CRUD + 启动/停止）
2. **数据规则** - DataRulesController（CRUD）
3. **ETL任务** - ETLTasksController（CRUD + 启动/停止）

### 后端需要新建API

1. **协议网关** - Gateway模型、GatewayService、GatewaysController
2. **网络隧道** - Tunnel模型、TunnelService、TunnelsController
3. **插件系统** - Plugin模型、PluginService、PluginsController
4. **数据库配置** - DatabaseConfig模型、DatabaseConfigService、DatabaseConfigsController

### 前端需要对接

1. **协议配置** - 已完成protocolApi.ts，对接ProtocolConfigPage
2. **数据规则** - 新建dataRuleApi.ts，对接RuleEnginePage
3. **ETL任务** - 新建etlTaskApi.ts，对接DataTransformPageNew
4. **协议网关** - 新建gatewayApi.ts，对接ProtocolGatewayPage
5. **网络隧道** - 新建tunnelApi.ts，对接NetworkTunnelPage
6. **插件系统** - 新建pluginApi.ts，对接PluginSystemPage
7. **数据库配置** - 新建databaseConfigApi.ts，对接DatabaseConfigPage
8. **数据采集中心** - 已部分实现，保持现状
9. **数据导出** - 保持现状（导出为后端生成文件下载）

## 技术方案

### 后端实现（4个新模块）

每个模块需要创建：

- Models/xxx.cs - 数据模型
- Services/Interfaces/IxxxService.cs - 服务接口
- Services/xxxService.cs - 服务实现
- Controllers/xxxController.cs - API控制器
- DTOs/Requests/xxxRequests.cs - 请求DTO
- DTOs/Responses/xxxResponse.cs - 响应DTO
- AppDbContext.cs - 添加DbSet和配置

### 前端实现（6个新API服务）

- dataRuleApi.ts - 数据规则API
- etlTaskApi.ts - ETL任务API
- gatewayApi.ts - 网关API
- tunnelApi.ts - 隧道API
- pluginApi.ts - 插件API
- databaseConfigApi.ts - 数据库配置API

## 修改文件清单

### 后端新建文件

```
Models/Gateway.cs           [NEW] 协议网关模型
Models/Tunnel.cs           [NEW] 网络隧道模型
Models/Plugin.cs           [NEW] 插件模型
Models/DatabaseConfig.cs   [NEW] 数据库配置模型
Services/Interfaces/IGatewayService.cs   [NEW] 网关服务接口
Services/Interfaces/ITunnelService.cs    [NEW] 隧道服务接口
Services/Interfaces/IPluginService.cs    [NEW] 插件服务接口
Services/Interfaces/IDatabaseConfigService.cs [NEW] 数据库配置服务接口
Services/GatewayService.cs      [NEW] 网关服务实现
Services/TunnelService.cs       [NEW] 隧道服务实现
Services/PluginService.cs       [NEW] 插件服务实现
Services/DatabaseConfigService.cs [NEW] 数据库配置服务实现
Controllers/GatewaysController.cs    [NEW] 网关API
Controllers/TunnelsController.cs     [NEW] 隧道API
Controllers/PluginsController.cs     [NEW] 插件API
Controllers/DatabaseConfigsController.cs [NEW] 数据库配置API
DTOs/Requests/GatewayRequests.cs    [NEW] 网关请求DTO
DTOs/Requests/TunnelRequests.cs     [NEW] 隧道请求DTO
DTOs/Requests/PluginRequests.cs     [NEW] 插件请求DTO
DTOs/Requests/DatabaseConfigRequests.cs [NEW] 数据库配置请求DTO
DTOs/Responses/GatewayResponse.cs    [NEW] 网关响应DTO
DTOs/Responses/TunnelResponse.cs     [NEW] 隧道响应DTO
DTOs/Responses/PluginResponse.cs     [NEW] 插件响应DTO
DTOs/Responses/DatabaseConfigResponse.cs [NEW] 数据库配置响应DTO
Data/AppDbContext.cs          [MODIFY] 添加DbSet
Program.cs                    [MODIFY] 注册服务
```

### 前端新建/修改文件

```
Web/src/app/services/api/dataRuleApi.ts     [NEW] 数据规则API
Web/src/app/services/api/etlTaskApi.ts     [NEW] ETL任务API
Web/src/app/services/api/gatewayApi.ts      [NEW] 网关API
Web/src/app/services/api/tunnelApi.ts      [NEW] 隧道API
Web/src/app/services/api/pluginApi.ts      [NEW] 插件API
Web/src/app/services/api/databaseConfigApi.ts [NEW] 数据库配置API
Web/src/app/pages/DataCollectionPage.tsx   [MODIFY] 对接协议配置
Web/src/app/pages/DataCollectionSubPages.tsx [MODIFY] 对接规则引擎、ETL、数据库配置
```

## 实现顺序

1. 后端：创建4个新模块（Gateway、Tunnel、Plugin、DatabaseConfig）
2. 后端：更新AppDbContext和Program.cs
3. 前端：创建6个API服务文件
4. 前端：更新DataCollectionPage和DataCollectionSubPages

## 技术栈

- 后端：ASP.NET Core + Entity Framework Core
- 前端：React + TypeScript + Tailwind CSS
- 数据库：SQL Server

## 数据模型设计

### Gateway（协议网关）

- Id, Name, Type, Status, SourceProtocol, TargetProtocol, Config, Throughput, IsEnabled, CreatedAt, UpdatedAt

### Tunnel（网络隧道）

- Id, Name, Type, Status, LocalPort, RemotePort, RemoteHost, Encryption, Bandwidth, IsEnabled, CreatedAt, UpdatedAt

### Plugin（插件）

- Id, Name, Version, Status, Description, Author, Config, IsEnabled, InstallDate, CreatedAt, UpdatedAt

### DatabaseConfig（数据库配置）

- Id, Name, Type, Status, Host, Port, Database, Username, Password, ConnectionString, IsEnabled, CreatedAt, UpdatedAt

## API设计

每个模块包含标准CRUD操作：

- GET /api/v1/xxx - 获取列表
- GET /api/v1/xxx/{id} - 获取详情
- POST /api/v1/xxx - 创建
- PUT /api/v1/xxx/{id} - 更新
- DELETE /api/v1/xxx/{id} - 删除
- POST /api/v1/xxx/{id}/start - 启动
- POST /api/v1/xxx/{id}/stop - 停止