# IoTPlatform 项目长期记忆

## 项目概况
- **类型**：IoT SaaS 多租户平台
- **技术栈**：.NET 8 / EF Core 8 / MySQL / Redis / MQTTnet / SignalR / Serilog / AutoMapper
- **框架**：ASP.NET Core Web API，单体模块化分层架构

## 多租户设计
- **隔离策略**：AppCode 行级隔离（共享 DB、共享 Schema）
- **实现方式**：实体实现 `IHasAppCode` 接口，EF Core `HasQueryFilter` 自动过滤
- **Token 传递**：JWT Claim 中携带 `AppCode`、`CustomerId`、`UserId`
- **SuperAdmin 豁免**：`IsSuperAdmin=true` 跳过过滤
- **MQTT 隔离**：主题格式 `{appCode}/{deviceId}/data`

## 已知风险
1. `Global Query Filter` 在 `DbContext` 构造时生成静态表达式，AppCode 变更后不会更新，需改为动态 Lambda
2. `MqttClientService` 注册为 `Singleton`，与 `SignalR` 共进程，高流量时存在资源争抢

## 演进规划
- P1：时序数据库(InfluxDB/TimescaleDB)、消息队列(RabbitMQ)、限流熔断、软删除+审计
- P2：多节点部署、读写分离、独立 Schema 隔离选项、K8s 容器化
- P3：微服务拆分（设备、告警、数采、分析、网关）

## 项目路径
`G:\IoTPlatform`
