# IoT Platform 后端API实现文档

## 项目概述

基于.NET 8构建的物联网SaaS平台后端API系统，支持多租户管理和RBAC权限控制。

## 技术栈

- **后端框架**: .NET 8 Web API
- **ORM**: Entity Framework Core 8.0
- **数据库**: MySQL 5.7 (Pomelo.EntityFrameworkCore.MySql 8.0.2)
- **缓存**: Redis (StackExchange.Redis 2.8.16, 可选)
- **认证**: JWT Bearer Token (Microsoft.AspNetCore.Authentication.JwtBearer 8.0.11)
- **物联网协议**: MQTTnet 4.3.7.1207
- **实时通信**: SignalR (Microsoft.AspNetCore.SignalR 1.1.0)
- **对象映射**: AutoMapper 12.0.1
- **参数验证**: FluentValidation.AspNetCore 11.3.0
- **日志**: Serilog.AspNetCore 8.0.3
- **API文档**: Swashbuckle.AspNetCore 6.6.2
- **密码哈希**: BCrypt.Net-Next 4.0.3

## 项目结构

```
IoTPlatform/
├── Controllers/              # API控制器
│   ├── AuthController.cs     # 认证控制器（已实现）
│   ├── CustomersController.cs
│   ├── ProjectsController.cs
│   ├── DevicesController.cs
│   ├── AreasController.cs
│   ├── AlertsController.cs
│   ├── WorkOrdersController.cs
│   ├── MonitoringController.cs
│   ├── ArchivesController.cs
│   ├── LogsController.cs
│   ├── UsersController.cs
│   ├── RolesController.cs
│   ├── DictionariesController.cs
│   ├── DataCollectionController.cs
│   ├── SettingsController.cs
│   └── AnalyticsController.cs
├── Services/                # 业务服务层
│   ├── Interfaces/          # 服务接口
│   ├── AuthService.cs       # 认证服务（已实现）
│   ├── CustomerService.cs
│   ├── ProjectService.cs
│   ├── DeviceService.cs
│   ├── AreaService.cs
│   ├── AlertService.cs
│   ├── WorkOrderService.cs
│   ├── MonitoringService.cs
│   ├── ArchiveService.cs
│   ├── LogService.cs
│   ├── UserService.cs
│   ├── RoleService.cs
│   ├── DictionaryService.cs
│   ├── DataCollectionService.cs
│   ├── SettingsService.cs
│   └── AnalyticsService.cs
├── Data/                   # 数据层
│   ├── AppDbContext.cs      # EF Core上下文（已实现）
│   ├── Repositories/        # 仓储接口和实现
│   ├── SeedData/           # 初始化数据
│   └── Migrations/         # 数据库迁移
├── DTOs/                   # 数据传输对象
│   ├── Requests/           # 请求DTO
│   │   ├── LoginRequest.cs # 已实现
│   │   └── ...
│   ├── Responses/          # 响应DTO
│   │   ├── LoginResponse.cs # 已实现
│   │   └── ...
│   └── Profiles/          # AutoMapper映射配置
│       └── MappingProfile.cs
├── Models/                 # 实体模型（已实现）
├── Infrastructure/          # 基础设施
│   ├── Cache/             # Redis缓存服务（已实现）
│   ├── JWT/              # JWT工具类（已实现）
│   ├── MQTT/             # MQTT服务
│   ├── SignalR/          # SignalR Hub
│   └── Middleware/        # 中间件（已实现）
├── Filters/              # 过滤器（已实现）
│   ├── PermissionFilter.cs
│   └── TenantFilter.cs
├── Helpers/              # 辅助类（已实现）
│   ├── ApiResponse.cs
│   ├── PagedResponse.cs
│   └── PermissionHelper.cs
├── Configuration/         # 配置类（已实现）
│   ├── PermissionConfig.cs
│   └── RoleConfig.cs
└── Program.cs            # 程序入口（已配置）
```

## 已实现的功能

### 1. 基础设施层 ✅

- **统一响应格式** (`Helpers/ApiResponse.cs`): 标准API响应格式
- **分页响应** (`Helpers/PagedResponse.cs`): 支持分页的数据响应
- **JWT工具类** (`Infrastructure/JWT/JwtHelper.cs`): JWT令牌生成和验证
- **Redis缓存服务** (`Infrastructure/Cache/RedisCacheService.cs`): 缓存封装，支持开关
- **异常处理中间件** (`Infrastructure/Middleware/ExceptionHandlingMiddleware.cs`): 全局异常捕获
- **操作日志中间件** (`Infrastructure/Middleware/OperationLoggingMiddleware.cs`): 自动记录操作日志
- **权限配置** (`Configuration/PermissionConfig.cs`): 权限常量定义
- **角色配置** (`Configuration/RoleConfig.cs`): 角色和权限映射

### 2. 数据库层 ✅

- **AppDbContext** (`Data/AppDbContext.cs`): EF Core上下文，配置所有实体和索引
- **Models**: 完整的实体模型定义（29个模型类）
- **数据库配置**: MySQL连接和重试策略

### 3. 认证授权 ✅

- **AuthService** (`Services/AuthService.cs`): 用户登录、获取用户信息、切换客户
- **AuthController** (`Controllers/AuthController.cs`): 认证相关API接口
- **权限验证过滤器** (`Filters/PermissionFilter.cs`): RBAC权限验证
- **租户过滤器** (`Filters/TenantFilterAttribute`): 自动租户数据隔离

## 剩余功能实现指南

### 4. 用户和角色管理

#### 待实现文件：
- `Services/UserService.cs`
- `Services/RoleService.cs`
- `Controllers/UsersController.cs`
- `Controllers/RolesController.cs`

#### 实现要点：
1. **UserService**:
   - 用户CRUD操作
   - 密码哈希（使用BCrypt.Net-Next）
   - 区域权限管理
   - 用户角色分配

2. **RoleService**:
   - 角色CRUD操作
   - 权限分配
   - 数据范围配置（ALL/CUSTOM）
   - 默认角色初始化

3. **API端点**:
   ```
   GET    /api/v1/users              - 获取用户列表
   GET    /api/v1/users/{id}          - 获取用户详情
   POST   /api/v1/users              - 创建用户
   PUT    /api/v1/users/{id}          - 更新用户
   DELETE /api/v1/users/{id}          - 删除用户
   POST   /api/v1/users/{id}/password - 修改密码
   GET    /api/v1/roles              - 获取角色列表
   POST   /api/v1/roles              - 创建角色
   PUT    /api/v1/roles/{code}       - 更新角色
   DELETE /api/v1/roles/{code}       - 删除角色
   ```

### 5. 客户和项目管理

#### 待实现文件：
- `Services/CustomerService.cs`
- `Services/ProjectService.cs`
- `Controllers/CustomersController.cs`
- `Controllers/ProjectsController.cs`

#### 实现要点：
1. **多租户数据隔离**:
   - 所有查询通过`AppCode`字段过滤
   - 超级管理员可以查看所有租户数据
   - 普通用户只能访问所属租户数据

2. **CustomerService**:
   - 客户CRUD操作
   - 自动生成`Code`和`AppCode`
   - 设备数量统计

3. **ProjectService**:
   - 项目CRUD操作
   - 合同和工作纪要管理
   - 设备关联

4. **API端点**:
   ```
   GET    /api/v1/customers                  - 获取客户列表
   GET    /api/v1/customers/{id}              - 获取客户详情
   POST   /api/v1/customers                  - 创建客户
   PUT    /api/v1/customers/{id}              - 更新客户
   DELETE /api/v1/customers/{id}              - 删除客户
   GET    /api/v1/customers/{id}/projects     - 获取客户项目列表
   POST   /api/v1/customers/{id}/projects     - 创建项目
   PUT    /api/v1/projects/{id}              - 更新项目
   DELETE /api/v1/projects/{id}              - 删除项目
   GET    /api/v1/projects/{id}/contracts    - 获取项目合同列表
   ```

### 6. 区域和设备管理

#### 待实现文件：
- `Services/AreaService.cs`
- `Services/DeviceService.cs`
- `Controllers/AreasController.cs`
- `Controllers/DevicesController.cs`

#### 实现要点：
1. **AreaService**:
   - 区域树形结构管理
   - 父子区域关系
   - 区域权限过滤
   - 设备数量统计

2. **DeviceService**:
   - 设备CRUD操作
   - 设备状态管理（online/offline/warning）
   - 设备传感器配置
   - 设备数据记录
   - 区域权限过滤

3. **API端点**:
   ```
   GET    /api/v1/areas              - 获取区域树
   GET    /api/v1/areas/{id}          - 获取区域详情
   POST   /api/v1/areas              - 创建区域
   PUT    /api/v1/areas/{id}          - 更新区域
   DELETE /api/v1/areas/{id}          - 删除区域
   GET    /api/v1/devices            - 获取设备列表
   GET    /api/v1/devices/{id}        - 获取设备详情
   POST   /api/v1/devices            - 创建设备
   PUT    /api/v1/devices/{id}        - 更新设备
   DELETE /api/v1/devices/{id}        - 删除设备
   PUT    /api/v1/devices/{id}/status - 更新设备状态
   GET    /api/v1/devices/{id}/sensors - 获取设备传感器列表
   ```

### 7. 监控和告警

#### 待实现文件：
- `Services/MonitoringService.cs`
- `Services/AlertService.cs`
- `Controllers/MonitoringController.cs`
- `Controllers/AlertsController.cs`

#### 实现要点：
1. **MonitoringService**:
   - 实时监控数据查询
   - 历史数据查询
   - 数据聚合统计
   - 空气质量和环境监测数据

2. **AlertService**:
   - 告警规则引擎
   - 告警记录管理
   - 告警状态流转（pending -> processing -> resolved/ignored）
   - 告警处理日志

3. **API端点**:
   ```
   GET    /api/v1/monitoring/devices/{deviceId}/data    - 获取设备监控数据
   GET    /api/v1/monitoring/areas/{areaId}/data        - 获取区域监控数据
   GET    /api/v1/monitoring/air-quality               - 获取空气质量数据
   GET    /api/v1/monitoring/environment               - 获取环境监测数据
   GET    /api/v1/alerts                                - 获取告警列表
   GET    /api/v1/alerts/{id}                            - 获取告警详情
   POST   /api/v1/alerts                                - 创建告警
   PUT    /api/v1/alerts/{id}/status                     - 更新告警状态
   GET    /api/v1/alerts/{id}/logs                       - 获取告警处理日志
   ```

### 8. 工单管理

#### 待实现文件：
- `Services/WorkOrderService.cs`
- `Controllers/WorkOrdersController.cs`

#### 实现要点：
1. **WorkOrderService**:
   - 工单CRUD操作
   - 工单状态流转（pending -> assigned -> in_progress -> resolved -> closed）
   - 工单日志记录
   - 工单附件管理
   - 工单分配和指派

2. **API端点**:
   ```
   GET    /api/v1/work-orders              - 获取工单列表
   GET    /api/v1/work-orders/{id}          - 获取工单详情
   POST   /api/v1/work-orders              - 创建工单
   PUT    /api/v1/work-orders/{id}          - 更新工单
   DELETE /api/v1/work-orders/{id}          - 删除工单
   PUT    /api/v1/work-orders/{id}/assign   - 分配工单
   PUT    /api/v1/work-orders/{id}/status  - 更新工单状态
   POST   /api/v1/work-orders/{id}/logs    - 添加工单日志
   POST   /api/v1/work-orders/{id}/attachments - 上传附件
   ```

### 9. 档案管理

#### 待实现文件：
- `Services/ArchiveService.cs`
- `Controllers/ArchivesController.cs`

#### 实现要点：
1. **文件存储**:
   - 本地存储（`uploads/`目录）
   - 或云存储（OSS、MinIO）
   - 文件类型和大小限制

2. **ArchiveService**:
   - 档案上传下载
   - 档案CRUD操作
   - 档案设备标记管理
   - CAD文件解析（前端已实现）

3. **API端点**:
   ```
   GET    /api/v1/archives              - 获取档案列表
   GET    /api/v1/archives/{id}          - 获取档案详情
   POST   /api/v1/archives              - 上传档案
   PUT    /api/v1/archives/{id}          - 更新档案
   DELETE /api/v1/archives/{id}          - 删除档案
   GET    /api/v1/archives/{id}/download - 下载档案
   POST   /api/v1/archives/{id}/markers - 添加设备标记
   ```

### 10. 日志管理

#### 待实现文件：
- `Services/LogService.cs`
- `Controllers/LogsController.cs`

#### 实现要点：
1. **LogService**:
   - 登录日志查询
   - 操作日志查询
   - 日志统计和分析

2. **API端点**:
   ```
   GET    /api/v1/logs/login              - 获取登录日志
   GET    /api/v1/logs/operation          - 获取操作日志
   GET    /api/v1/logs/login/{id}         - 获取登录日志详情
   ```

### 11. 字典管理

#### 待实现文件：
- `Services/DictionaryService.cs`
- `Controllers/DictionariesController.cs`

#### 实现要点：
1. **DictionaryService**:
   - 字典类型管理
   - 字典项管理
   - 字典缓存

2. **API端点**:
   ```
   GET    /api/v1/dictionaries              - 获取字典类型列表
   POST   /api/v1/dictionaries              - 创建字典类型
   PUT    /api/v1/dictionaries/{id}          - 更新字典类型
   DELETE /api/v1/dictionaries/{id}          - 删除字典类型
   GET    /api/v1/dictionaries/{typeId}/items - 获取字典项列表
   POST   /api/v1/dictionaries/{typeId}/items - 创建字典项
   ```

### 12. 数据采集

#### 待实现文件：
- `Services/DataCollectionService.cs`
- `Controllers/DataCollectionController.cs`

#### 实现要点：
1. **协议支持**:
   - TCP
   - UDP
   - HTTP
   - WebSocket
   - MQTT (TCP/RTU/ASCII)
   - CoAP
   - IEC61850
   - OPC UA
   - ICE104

2. **DataCollectionService**:
   - 协议配置管理
   - 网关配置
   - 隧道配置
   - 数据规则引擎
   - 数据转换
   - 数据导出

3. **API端点**:
   ```
   GET    /api/v1/data-collection/protocols       - 获取协议配置列表
   POST   /api/v1/data-collection/protocols       - 创建协议配置
   PUT    /api/v1/data-collection/protocols/{id}   - 更新协议配置
   DELETE /api/v1/data-collection/protocols/{id}   - 删除协议配置
   GET    /api/v1/data-collection/gateways         - 获取网关列表
   POST   /api/v1/data-collection/gateways         - 创建网关
   GET    /api/v1/data-collection/tunnels          - 获取隧道列表
   POST   /api/v1/data-collection/tunnels          - 创建隧道
   GET    /api/v1/data-collection/rules             - 获取数据规则列表
   POST   /api/v1/data-collection/rules             - 创建数据规则
   ```

### 13. 系统设置

#### 待实现文件：
- `Services/SettingsService.cs`
- `Controllers/SettingsController.cs`

#### 实现要点：
1. **SettingsService**:
   - 系统配置管理
   - API密钥管理（超级管理员）
   - 配置缓存

2. **API端点**:
   ```
   GET    /api/v1/settings              - 获取系统配置
   PUT    /api/v1/settings              - 更新系统配置
   GET    /api/v1/settings/api-keys     - 获取API密钥列表（super_admin）
   POST   /api/v1/settings/api-keys     - 创建API密钥（super_admin）
   DELETE /api/v1/settings/api-keys/{id} - 删除API密钥（super_admin）
   ```

### 14. 统计分析

#### 待实现文件：
- `Services/AnalyticsService.cs`
- `Controllers/AnalyticsController.cs`

#### 实现要点：
1. **AnalyticsService**:
   - 汇总统计
   - 能耗统计
   - 告警趋势分析
   - 设备状态统计
   - 数据聚合

2. **API端点**:
   ```
   GET    /api/v1/analytics/summary             - 获取汇总统计
   GET    /api/v1/analytics/energy-consumption - 获取能耗统计
   GET    /api/v1/analytics/alert-trends        - 获取告警趋势
   GET    /api/v1/analytics/device-status       - 获取设备状态统计
   ```

### 15. IoT协议实现

#### 待实现文件：
- `Infrastructure/MQTT/MqttService.cs`
- `Infrastructure/SignalR/MonitoringHub.cs`

#### 实现要点：
1. **MQTT服务**:
   - 使用MQTTnet实现MQTT Broker
   - 订阅设备数据主题: `iot/{appCode}/{deviceId}/+/data`
   - 解析JSON格式传感器数据
   - 存储到`device_data_records`表
   - 触发告警规则检查

2. **SignalR Hub**:
   - 实时推送告警通知
   - 实时推送设备状态变化
   - 实时推送监控数据

3. **其他协议**:
   - 使用第三方库实现：
     - Modbus (Modbus4Net)
     - OPC UA (OPCFoundation.NetStandard.Opc.Ua)
     - CoAP (CoAP.NET)
     - ICE104 (需要自定义实现)

#### API端点:
```
POST   /api/v1/iot/mqtt/start          - 启动MQTT服务
POST   /api/v1/iot/mqtt/stop           - 停止MQTT服务
GET    /api/v1/iot/mqtt/status          - 获取MQTT服务状态
```

## 数据库迁移

### 生成初始迁移
```bash
dotnet ef migrations add InitialCreate --project IoTPlatform
```

### 应用迁移
```bash
dotnet ef database update --project IoTPlatform
```

### 生成迁移脚本
```bash
dotnet ef migrations script --project IoTPlatform
```

## 初始化数据

创建`Data/SeedData/`目录，添加种子数据：
- `SeedRoles.cs` - 初始化默认角色
- `SeedUsers.cs` - 初始化超级管理员
- `SeedCustomers.cs` - 初始化示例客户
- `SeedDictionaries.cs` - 初始化数据字典

## 配置说明

### appsettings.json
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Port=3306;Database=iot_platform;User=root;Password=root;charset=utf8mb4;"
  },
  "Redis": {
    "ConnectionString": "localhost:6379,defaultDatabase=0",
    "Enabled": false
  },
  "Jwt": {
    "Issuer": "IoTPlatform",
    "Audience": "IoTPlatform",
    "SecretKey": "IoTPlatform_SuperSecretKey_2024_!@#$%^&*",
    "ExpirationMinutes": 1440
  },
  "MQTT": {
    "Server": "localhost",
    "Port": 1883,
    "Username": "",
    "Password": ""
  },
  "FileStorage": {
    "UploadPath": "uploads",
    "MaxFileSizeMB": 50,
    "AllowedExtensions": [".jpg", ".jpeg", ".png", ".gif", ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".zip", ".rar", ".csv"]
  }
}
```

## 运行项目

### 开发环境
```bash
dotnet run
```

访问Swagger文档：`http://localhost:5000/swagger`

### 生产环境
```bash
dotnet publish -c Release -o ./publish
```

## API测试

使用提供的`IoTPlatform.http`文件进行API测试，或使用Postman。

## 注意事项

1. **密码哈希**: 所有密码必须使用BCrypt.Net-Next进行哈希
2. **权限验证**: 所有需要权限的Controller或Action必须使用`[PermissionAuthorize]`特性
3. **租户隔离**: 所有数据查询必须通过`AppCode`字段进行租户过滤
4. **区域权限**: 用户通过`AllowedAreaIds`字段限制可访问的区域
5. **异常处理**: 所有异常必须被捕获并返回标准API响应格式
6. **日志记录**: 所有关键操作必须记录日志
7. **缓存策略**: 频繁访问的数据使用Redis缓存
8. **分页查询**: 所有列表接口支持分页

## 扩展建议

1. **API版本管理**: 已配置`/api/v1/`前缀，未来可添加`/api/v2/`
2. **限流**: 使用ASP.NET Core内置限流中间件
3. **API文档**: 使用Swagger自动生成API文档
4. **单元测试**: 使用xUnit和Moq进行单元测试
5. **集成测试**: 使用TestServer进行集成测试
6. **容器化**: 创建Dockerfile和docker-compose.yml
7. **CI/CD**: 配置GitHub Actions或Azure DevOps
8. **监控**: 使用Application Insights或Prometheus
9. **分布式追踪**: 使用OpenTelemetry
10. **文档**: 使用Swashbuckle自动生成API文档

## 常见问题

### Q: 如何添加新的权限？
A: 在`Configuration/PermissionConfig.cs`中添加新的权限常量，然后在`Configuration/RoleConfig.cs`中为角色分配该权限。

### Q: 如何添加新的协议支持？
A: 在`Infrastructure/`目录下创建新的协议服务类，并在`DataCollectionService`中集成。

### Q: 如何实现数据导出？
A: 在`DataCollectionService`中添加导出方法，使用EPPlus或NPOI库生成Excel文件。

### Q: 如何优化查询性能？
A: 使用EF Core的Include()预加载关联数据，避免N+1查询问题；添加适当的数据库索引。

### Q: 如何处理并发冲突？
A: 使用EF Core的并发令牌（ConcurrencyCheck特性和RowVersion字段）。

## 联系方式

如有问题，请联系开发团队。
