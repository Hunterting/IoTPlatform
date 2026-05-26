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

## 数据采集模块（2026-05-18 全面升级完成）
### 主采集链路（双轨合一）
- **链路 A (MQTT)**：设备 → MQTT Broker → MqttClientService(Singleton) → OnDataReceived → MqttHostedService(BgSvc) → DataCollectionService(Scoped) → MySQL
- **链路 B (协议适配器)** ✅ **P2 已接入**：设备 → ModbusTCP/RTU/OPC UA/MQTT(适配器) → IProtocolAdapter.DataReceived ↑ → ProtocolConfigService 事件桥接 → IServiceScopeFactory → DataCollectionService.ProcessDeviceDataAsync() → MySQL
- **桥接实现位置**：`ProtocolConfigService.SubscribeAdapterDataReceived()` / `OnProtocolAdapterDataReceived()`
- **生命周期绑定**：Start 时订阅，Stop 时反注册，通过 `_activeSubscriptions` 字典追踪

### DataCollectionService（核心处理引擎）
- **JSON 解析** ✅：SensorFieldMappings 策略模式映射 35+ 物理量字段
- **能耗差异化** ✅：EnergyTypeStrategy 按 water/electric/gas 做范围校验+关键字段聚焦
- **DeviceDataRecord** ✅：含 6 个能耗字段（WaterFlow/Total, ElectricPower/KWh, GasFlow/Total）
- **后处理链路**：保存记录 → 更新 DeviceSensor.LastValue → 执行 DataRule 规则引擎 → 触发告警

### 时序数据存储层 ✅ **P2 新增**
- **接口**：`ITimeSeriesStore`（Services/Interfaces/）— Write/QueryRange/GetLatest/Aggregate/DeleteOlderThan/Statistics
- **实现**：`MySqlTimeSeriesStore` — 基于 DeviceDataRecord 表 + EF Core
- **聚合**：支持 Avg/Max/Min/Sum/Count 按时间窗口降采样
- **清理策略**：`DataRetentionHostedService`(BgSvc) 定时调用 DeleteOlderThanAsync 分批删除
- **配置**：appsettings.json `DataRetention` 节（DetailRetentionDays=30, CleanupIntervalHours=24）
- **扩展点**：切换到 InfluxDB/TimescaleDB 只需新增实现类，零业务代码改动

## 已知风险
1. ~~协议适配器层未接入主链路~~ ✅ **P2-A 已修复**
2. ~~无时序数据库抽象~~ ✅ **P2-B/C 已补齐**
3. `Global Query Filter` 在 `DbContext` 构造时生成静态表达式，AppCode 变更后不会更新，需改为动态 Lambda
4. `MqttClientService` 注册为 `Singleton`，与 `SignalR` 共进程，高流量时存在资源争抢
5. OPCUA 适配器为骨架代码（20%），所有 OPC Foundation 调用为 TODO
6. **待办**：消息队列(RabbitMQ)、限流熔断、软删除+审计

## 演进规划
- **P0-P2** ✅ 全部完成（P0: JSON解析+配置键 | P1: 能耗字段+差异化 | P2: 协议集成+时序抽象）
- **下一阶段**：消息队列(RabbitMQ)、限流熔断、软删除+审计日志、OPC UA 完善
- **远期**：多节点部署、读写分离、K8s 容器化、微服务拆分

## 项目路径
`H:\IoTPlatform`
