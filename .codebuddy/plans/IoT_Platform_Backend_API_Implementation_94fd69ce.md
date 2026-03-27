---
name: IoT Platform Backend API Implementation
overview: 基于前端React代码实现.NET 8后端API，支持多租户SaaS架构，包括认证授权、设备管理、区域管理、数据采集、监控告警等核心功能模块
todos:
  - id: setup-infrastructure
    content: 配置基础设施层：实现JWT工具类、Redis缓存封装、统一响应格式、异常处理中间件
    status: completed
  - id: create-dbcontext
    content: 创建AppDbContext和数据迁移：完善Model关系，添加索引，生成MySQL迁移脚本
    status: completed
    dependencies:
      - setup-infrastructure
  - id: implement-auth
    content: 实现认证授权：AuthService、JWT认证配置、RBAC权限验证过滤器、登录/登出API
    status: completed
    dependencies:
      - create-dbcontext
  - id: implement-user-role
    content: 实现用户和角色管理：UserService、RoleService、UsersController、RolesController，支持CRUD和权限分配
    status: completed
    dependencies:
      - implement-auth
  - id: implement-customer-project
    content: 实现租户和项目管理：CustomerService、ProjectService、CustomersController、ProjectsController，支持多租户数据隔离
    status: completed
    dependencies:
      - implement-user-role
  - id: implement-area-device
    content: 实现区域和设备管理：AreaService、DeviceService、AreasController、DevicesController，支持区域树和设备CRUD
    status: completed
    dependencies:
      - implement-customer-project
  - id: implement-monitoring
    content: 实现监控和告警：MonitoringService、AlertService、MonitoringController、AlertsController，告警规则引擎
    status: completed
    dependencies:
      - implement-area-device
  - id: implement-workorder
    content: 实现工单管理：WorkOrderService、WorkOrdersController，工单流程和附件管理
    status: completed
    dependencies:
      - implement-monitoring
  - id: implement-others
    content: 实现其他模块：档案、日志、字典、数据采集、设置、统计分析的Service和Controller
    status: completed
    dependencies:
      - implement-workorder
  - id: implement-iot
    content: 实现IoT协议：MQTT服务订阅设备数据、SignalR Hub推送实时数据、告警触发
    status: completed
    dependencies:
      - implement-others
---

## 产品概述

基于.NET 8构建的物联网SaaS平台后端API系统，支持多租户管理和RBAC权限控制。

## 核心功能

### 认证与授权

- JWT令牌认证
- 多租户用户登录（超级管理员可切换租户）
- 基于RBAC的细粒度权限控制（60+权限点）
- 角色管理（超级管理员、租户管理员、运维主管、厨师长、普通员工）

### 租户与用户管理

- 客户（租户）CRUD操作
- 项目管理（关联客户）
- 用户管理（支持区域权限限制）
- 登录日志和操作日志记录

### 设备管理

- 设备CRUD操作（支持分页、筛选）
- 区域树形结构管理
- 设备传感器配置
- 设备数据记录存储

### 监控与告警

- 实时设备监控数据接口
- 空气质量数据查询
- 环境监测数据查询
- 告警记录管理（状态流转）
- 告警处理日志

### 工单管理

- 工单创建、分配、处理、关闭流程
- 工单日志和附件管理
- 支持多种工单类型（维护、维修、巡检、安装）

### 数据采集

- MQTT协议网关支持
- 协议配置管理
- 网关和隧道配置
- 数据规则引擎
- 数据转换和导出

### 档案与设置

- 档案上传下载
- 字典管理
- 系统配置管理
- API密钥管理（超级管理员）

### 实时通信

- SignalR Hub推送告警和监控数据
- MQTT消息订阅和发布

### 统计分析

- 汇总统计接口
- 能耗统计
- 告警趋势分析
- 设备状态统计

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

## 实现方式

### 系统架构

采用分层架构模式：

- **表现层** (Controllers): API接口，处理HTTP请求
- **应用层** (Services): 业务逻辑处理
- **数据层** (Repositories/DbContext): 数据访问
- **基础设施层** (Infrastructure): 跨切面关注点（缓存、日志、MQTT、SignalR）

### 核心设计决策

1. **多租户隔离**: 通过AppCode字段实现租户数据隔离，超级管理员可查看所有租户数据，普通用户只能访问所属租户数据
2. **RBAC权限控制**: 实现基于声明(Claims)的权限验证，结合Action过滤器自动检查操作权限和数据范围
3. **区域权限**: 用户通过allowedAreaIds字段限制可访问的区域，支持父子区域继承
4. **MQTT集成**: 使用MQTTnet作为服务端订阅设备消息，存储设备数据并触发告警
5. **实时推送**: 通过SignalR Hub向客户端推送实时告警和监控数据
6. **缓存策略**: 使用Redis缓存频繁访问数据（字典、用户信息、设备状态）
7. **审计日志**: 通过中间件自动记录用户操作日志，登录日志独立记录

### 性能与可靠性

- **数据库索引**: 为AppCode、CustomerId、AreaId、Status、AlertTime等高频查询字段添加索引
- **分页查询**: 所有列表接口支持分页，避免全表扫描
- **批量操作**: 设备数据批量插入，告警批量处理
- **异步处理**: MQTT消息处理、告警触发、数据推送使用异步方式
- **连接池**: EF Core和Redis使用连接池
- **数据归档**: 历史设备数据定期归档到历史表，控制主表数据量

### 避免技术债务

- 遵循SOLID原则，保持单一职责
- 使用依赖注入解耦各层
- 统一异常处理和响应格式
- 统一日志记录规范
- 使用FluentValidation进行参数验证，避免验证逻辑散落在Controller
- 使用AutoMapper处理DTO转换，避免手动映射代码

## 实现说明

### 目录结构

```
IoTPlatform/
├── Controllers/              # API控制器
│   ├── AuthController.cs
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
│   ├── AuthService.cs
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
│   ├── AppDbContext.cs      # EF Core上下文
│   ├── Repositories/        # 仓储接口和实现
│   │   ├── Interfaces/
│   │   ├── CustomerRepository.cs
│   │   ├── UserRepository.cs
│   │   └── ...
│   ├── SeedData/           # 初始化数据
│   └── Migrations/         # 数据库迁移
├── DTOs/                   # 数据传输对象
│   ├── Requests/           # 请求DTO
│   │   ├── LoginRequest.cs
│   │   ├── CreateDeviceRequest.cs
│   │   └── ...
│   ├── Responses/          # 响应DTO
│   │   ├── UserResponse.cs
│   │   ├── DeviceResponse.cs
│   │   └── ...
│   └── Profiles/          # AutoMapper映射配置
│       └── MappingProfile.cs
├── Models/                 # 实体模型（已存在）
├── Infrastructure/          # 基础设施
│   ├── Cache/             # Redis缓存封装
│   ├── JWT/              # JWT工具类
│   ├── MQTT/             # MQTT服务
│   ├── SignalR/          # SignalR Hub
│   ├── Middleware/        # 中间件（审计日志）
│   └── Validators/       # FluentValidation验证器
│       ├── LoginRequestValidator.cs
│       └── ...
├── Filters/              # 过滤器
│   ├── PermissionFilter.cs    # 权限验证过滤器
│   └── TenantFilter.cs       # 租户过滤过滤器
├── Helpers/              # 辅助类
│   ├── ApiResponse.cs        # 统一响应格式
│   └── PermissionHelper.cs   # 权限辅助类
├── Configuration/         # 配置类
│   ├── PermissionConfig.cs   # 权限配置
│   └── RoleConfig.cs       # 角色配置
└── Program.cs            # 程序入口
```

### 数据库设计

基于现有Models完善表结构和关系：

- users: 添加角色关联、密码哈希、最后登录时间
- roles: 角色定义表
- user_roles: 用户角色多对多关联表
- customers: 租户表，已有基础字段
- projects: 项目表，关联客户
- areas: 区域树形表，支持父子层级
- devices: 设备表，关联项目和区域
- device_sensors: 设备传感器配置
- device_data_records: 设备实时数据
- alert_records: 告警记录
- alert_process_logs: 告警处理日志
- work_orders: 工单
- work_order_logs: 工单日志
- work_order_attachments: 工单附件
- archives: 档案
- archive_device_markers: 档案中的设备标记
- protocol_configs: MQTT协议配置
- dictionary_types: 字典类型
- dictionary_items: 字典项
- login_logs: 登录日志
- operation_logs: 操作日志
- system_settings: 系统配置
- air_quality_data: 空气质量数据
- environment_data: 环境监测数据

### API接口设计

遵循RESTful规范，统一响应格式：

- 成功响应: `{ code: 200, message: "success", data: ... }`
- 错误响应: `{ code: 400/401/403/404/500, message: "error message", data: null }`

### 安全考虑

- 密码使用BCrypt哈希存储
- JWT令牌设置过期时间（默认24小时）
- 敏感操作记录操作日志
- 文件上传限制大小和类型
- SQL注入防护（EF Core参数化查询）
- XSS防护（输出编码）
- CORS配置

### IoT协议实现

使用MQTTnet实现MQTT Broker和Client：

- MQTT Broker服务监听设备上报数据
- 订阅主题: `iot/{appCode}/{deviceId}/+/data`
- 消息格式: JSON格式传感器数据
- 数据解析后存储到device_data_records
- 触发告警规则检查
- 通过SignalR推送实时数据到前端

本任务为后端API实现，不涉及前端UI设计，因此不生成设计内容。

## Agent Extensions

### SubAgent: code-explorer

- **Purpose**: 探索现有Models和Controllers结构，验证数据模型完整性，定位需要修改或新增的文件
- **Expected outcome**: 确认现有29个Model类的完整结构，识别缺失的关联关系和字段，为数据库设计和API实现提供准确依据