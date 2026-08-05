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

## 安圣 MQTT 设备接入 ✅ Phase 1-3 完成 (2026-06-27)

### Phase 1 — 基础数据模型与 MQTT 桥接 ✅ (2026-05-08)
- **模型层**：AnShengDeviceConfig + DiscoveredAnShengDevice，Device 新增 ProtocolConfigId
- **适配器层**：AnShengMqttProtocolAdapter（实现 IProtocolAdapter），协议类型 "ANSHENG_MQTT"
- **解析层**：AnShengMessageParser（Topic IMEI 提取→JSON 解析→EMdata 聚合标准化） + AnShengCommandBuilder
- **链路集成**：DataCollectionService 新增 total_power→ElectricPower / total_energy→ElectricKWh 映射；ProtocolConfigService 新增 IMEI→DeviceId 查询
- **配置**：appsettings.json 新增 AnShengMqtt 节（Host/Port/Topic 模式/自动上报默认间隔）
- **数据库**：Migration AddAnShengIntegration 已生成，新增 2 张表 + devices.ProtocolConfigId 列

### Phase 2 — 命令下发与自动上报 ✅ (2026-06-27)
- **命令服务**：IAnShengCommandService → AnShengCommandService（适配器工厂→命令构建→MQTT 下发 /sertodev/{imei}）
- **命令路由**：DeviceCommandService.SendCommandAsync 按 device.ProtocolConfigId 分流 → SendViaAnShengAsync / 原 MQTT 路径
- **frameId↔commandId 映射**：ConcurrentDictionary 维护
- **DTO**：AnShengRequests.cs + AnShengResponses.cs

### Phase 3 — 设备发现与离线检测 ✅ (2026-06-27)
- **发现服务**：AnShengDiscoveryService（BackgroundService）定时扫描未认领设备，通过适配器发送 getDevInfo
- **待认领池**：未知 IMEI 自动进入 DiscoveredAnShengDevice 表，提取 model/netType，记录 DiscoveredAt/LastSeenAt
- **Will 离线检测**：AnShengMqttProtocolAdapter.DeviceWill 静态事件 → DiscoveryService 监听 → 更新 Device.Status="offline"
- **在线同步**：ProtocolConfigService 收到数据时 fire-and-forget 通知 DiscoveryService → 更新 LastSeenAt + Device.Status="online"
- **内存缓存**：ConcurrentDictionary 维护在线状态，支持快速 IsOnline(imei) 查询

### 核心设计
- **Topic 约定**：上行 /devtoser/pub/+，下行 /sertodev/{imei}，will /devtoser/will/+
- **IMEI 策略**：复用 Device.SerialNumber，不新增独立字段
- **发现阈值**：扫描间隔 5min，离线判定 10min 无数据

### Phase 4 — API 控制器 + 前端页面 ✅ (2026-06-27)
- **后端 AnShengController**（5 个端点）：discover → discovered 分页列表 → claim 认领 → auto-report 配置 → command 下发
- **认领流程**：DiscoveredAnShengDevice → 验证无 IMEI 冲突 → 创建 Device（ProtocolConfigId 关联）→ 标记 IsClaimed
- **TriggerDiscoveryAsync**：从占位升级为完整实现，通过适配器向未认领设备发送 getDevInfo
- **前端 AnShengManagementPage**：双 Tab（待认领设备分页表格 + 命令面板 8 个模板 + 认领弹窗）
- **前端路由**：`ansheng-management`（设备管理子菜单「安圣设备」）
- **权限**：VIEW_DEVICES（查看）、CREATE_DEVICES（发现/认领）、UPDATE_DEVICES（配置/命令）

### 四阶段总览
| Phase | 内容 | 新增 | 修改 | 状态 |
|-------|------|------|------|------|
| P1 | 模型+适配器+解析+链路 | 8 | 7 | ✅ |
| P2 | 命令下发+路由 | 4 | 2 | ✅ |
| P3 | 设备发现+Will离线 | 3 | 3 | ✅ |
| P4 | API控制器+前端页面 | 4 | 5 | ✅ |
| **合计** | | **16** | **14** | ✅ |
1. ~~协议适配器层未接入主链路~~ ✅ **P2-A 已修复**
2. ~~无时序数据库抽象~~ ✅ **P2-B/C 已补齐**
3. `Global Query Filter` 在 `DbContext` 构造时生成静态表达式，AppCode 变更后不会更新，需改为动态 Lambda
4. `MqttClientService` 注册为 `Singleton`，与 `SignalR` 共进程，高流量时存在资源争抢
5. OPCUA 适配器为骨架代码（20%），所有 OPC Foundation 调用为 TODO
6. **待办**：消息队列(RabbitMQ)、限流熔断、软删除+审计

### 契约约定（2026-07-31 起生效）
- **全局枚举字符串出网**：`Program.cs` 已注册 `JsonStringEnumConverter`，所有经控制器出网的枚举以**字符串原名（PascalCase）**出网（如 `RejectedByKind` 而非 `0`）。前端必须按字符串分支，不得按整数。T7 遗留的 `AnShengCommandRejectReason`/`AnShengCommandStatus` 同样生效。

### T8 开关动作与延时任务（后端）✅ 验收 (2026-07-31)
- 新增 `AnShengSwitchController`(5 端点: action/actions/delay-tasks 读/start/stop) + `AnShengScheduleService` + `AnShengDelayTask` 模型 + 迁移 `20260804112806_T8DelayTask`。
- QA 集成验收 5/5 通过（零源缺陷）：字节级报文一致、slotNums JSON 数组、乐观镜像+自动回读 bump SyncedAt、delayEvent→Enable=false+快照更新、喇叭类 200+code=400+RejectedByKind+零出网。
- **三条铁律**：①后台作用域写 `IgnoreQueryFilters()`+显式 AppCode；②拒绝走 `ApiResponse<T>.BadRequest` 信封（HTTP200/零裸400）；③报文全走 `AnShengCommandBuilder`+`SendCommandAsync`+`Guard`。

### T9 开关控制面板（前端）✅ 验收 (2026-08-04)
- 消费 T8 五端点 + `GET /profile`（插槽矩阵权威源）的前端页面 `Web/src/app/pages/SwitchControlPage.tsx`。
- 5 文件改动：`SwitchControlPage.tsx`(新增) + `anshengApi.ts`(6方法) + `ansheng.types.ts`(T9类型) + `App.tsx`(路由) + `Sidebar.tsx`(菜单子项「开关控制」归「设备管理」下)。
- QA 第2轮回归 9/9 通过、43/43 断言、tsc T9 文件 0 错误、vite build 通过。T9 引入遗留 0（仅 3 个预存无关类型错误：dictionary/settings-database/VIEW_DATABASE）。
- **关键偏差已闭环**：①插槽矩阵改用 `GET /profile` 的 `SlotsSnapshot`（delay-tasks 只返配置不含通断态）；②F1 菜单权限 `SEND_DEVICE_COMMANDS`→`VIEW_DEVICES`（否则只读用户进不了页）；③F2 路数用 `max(slotNum)` 而非 `delayTasks.length`（稀疏镜像会算错），`slotCountKnown` 只认 profile/slots。
- 契约核查脚本留存 `.qa-logs/t9_contract_check.js`（可复跑，退出码0=全绿）。
- 前端栈：React18 + Vite + Tailwind + Radix + MUI + lucide-react + axios；`Web` 仓库无 tsconfig.json、全项目 tsc 约 200 错历史债（前端不强制 tsc，靠 vite/esbuild 转译）。

### T10 定时任务（后端）✅ 验收 (2026-08-05)
- 新增 `AnShengScheduleController`(4端点: GET/POST `/time-tasks` 整表 + GET/POST `/time-tasks/{slotNum}` 单插槽; 类级VIEW_DEVICES, 写端点SEND_DEVICE_COMMANDS) + `AnShengTimeTask` 模型(IHasAppCode+RowVersion+复合唯一键(DeviceId,SlotNum,TaskKind,TaskIndex)) + `TimeEventHandler`(timeEvent就地更新, 不发命令) + `AnShengCommandBuilder` 4方法(getTimeTasks/setTimeTasks/getSlotTimeTasks/setSlotTimeTasks) + 迁移 `20260804161127_T10TimeTask`。
- 六验收全过: ①仅Switch4G放行(其他品类RejectedByKind+零出网) ②set需confirm=true(RejectedByConfirm) ③保存后自动回读(ScheduleTimeTaskReadback→ApplyTimeTasksReadbackAsync, SyncedAt bump) ④timeEvent就地更新 ⑤RowVersion冲突→409 ⑥SyncedAt>24h→IsStale。
- QA 第2轮 `AnShengTimeTaskAcceptanceTests` 14/14 通过, 安圣全量 IntegrationTests 35/35 零回归。2 源缺陷闭环: A 单插槽回读写进幽灵插槽0(改从请求侧按RecordId/(Imei,FrameId)反查slotNum, 取不到跳过写回) B 整表并发漏409(补ConcurrencyConflict→StatusCode(409), 与单插槽同构)。
- `TaskKind` 枚举经 `JsonStringEnumConverter` 字符串出网("Normal"/"Loop")。

### T11 电量计（后端）✅ 验收 (2026-08-05)
- 新增 `AnShengEnergyController`(8端点: POST `energy/realtime` / `energy/statistics/refresh` / `energy/statistics/clear`, GET `energy/statistics` / `energy/cal-params`, POST `energy/cal-params` / `energy/cal-params/reset` / `energy/cal-params/auto; GET statistics 走 VIEW_DEVICES, GET cal-params 与 realtime/refresh/clear/setCalParams/reset/auto 均走 SEND_DEVICE_COMMANDS) + `AnShengEnergyStatistics`/`AnShengEmStatistic` 模型 + `AnShengEnergyService` + 迁移 T11 电量计表。
- QA 16/16 通过: ①聚合表唯一键 `(deviceId,slotNum,granularity,periodKey)` 幂等 UPSERT ②`hourSum` 仅当设备回 48 项(`periodKey` 00:00~23:30) ③无空洞行、清零后平台保留 ④实时→DeviceDataRecord ⑤校准仅开关类放行。
- **电量计无 409, 全部 HTTP 200**; `granularity`/`rejectReason` 枚举经 `JsonStringEnumConverter` 字符串出网("Total"/"HourSum"/"Hour"/"Day"/"Month")。

### T10/T11 前端面板 ✅ 验收 (2026-08-05)
- T10 前端 `Web/src/app/pages/ScheduleEditorPage.tsx` 消费 T10 4 端点(getTimeTasks/setTimeTasks/getSlotTimeTasks/setSlotTimeTasks); T11 前端 `Web/src/app/pages/EnergyStatisticsPage.tsx` 消费 T11 8 端点(realtime/refresh/clear/getStatistics/getCalParams/setCalParams/reset/auto)。
- 数据层: `anshengApi.ts` 新增 12 方法 + `ansheng.types.ts` 全部 T10/T11 类型(taskKind/granularity 枚举字符串分支)。
- 接线: `App.tsx` 路由 `schedule-editor`/`energy-statistics` + `Sidebar.tsx` 菜单子项(定时任务=Clock、电量统计=Zap, 均 VIEW_DEVICES)。
- QA 验收: 契约核查脚本 `.qa-logs/t10t11_contract_check.js` 74 断言全绿(端点URL/camelCase/枚举/信封/409/权限/命令日志/路由菜单/伪命令), 代码复核无功能性缺陷, vite build EXIT=0。
- 验收后主理人修正 EnergyStatisticsPage 头部注释(cal-params GET 实走 SEND_DEVICE_COMMANDS, 非 VIEW_DEVICES), QA 重新闭环确认(纯文档改动, 已加防回归静态断言)。
- 铁律对齐: 信封认 `code` 不认 `success`; T10 有 409+concurrencyConflict 分支, T11 全 200 无 409; 枚举按字符串分支; 权限门控(只读用户进页看只读 UI)。

### T14 协议族隔离（后端 C# + React 前端）✅ 验收 (2026-08-05)
- 基于 `docs/phase345-audit-2026-08-03.md` §2.10: 充电桩 Legacy 命令归位(原 50% 内联于通用 Builder, 本次补全剩余 50%)。设计文档即权威, 走快速模式/增量开发(跳过 PRD 与独立架构)。
- 交付: 🆕 `Infrastructure/Protocol/AnSheng/Legacy/AnShengLegacyCommandBuilder.cs`(归位 Legacy 逻辑 + `EnsureChargingPileMethod` 闸门, 非充电桩方法抛 NotSupportedException) + 🆕 `AnShengProtocolFamilyResolver.cs`(三态判定: 认识二开/认识充电桩/不认识即拒绝) + ✏️ `AnShengCommandSpec.cs`(`AnShengProtocolFamily` 枚举 OpenProtocol=0/ChargingPile=1 + ProtocolFamily 字段) + ✏️ `AnShengMessageParser.cs`/`Services/AnShengCommandService.cs`(显式协议族分流) + 🆕 `Web/src/app/components/ansheng/CommandConsole.tsx` + ✏️ `AnShengManagementPage.tsx`(前端协议族分区)。
- QA 验收: `IoTPlatform.AnSheng.Tests` 727/0 全绿(基线 680 + 新增 47, 含 `AnShengProtocolFamilyTests` 47 例) + 前端 `vite build` EXIT=0。3 条验收全过: ①二开面板结构性不出现 order* ②orderStart 报文结构完全一致 ③close 遗嘱两族正确。
- **架构价值(铁律级)**: 根除「不在二开目录 ⇒ 按 Legacy 静默下发」的隐式兜底放行(改造前任何拼写错误/协议外 method 曾被当 Legacy 真实外发); 改为 `Resolver.Resolve(method)==ChargingPile` 显式判定 + `Resolve_UnknownMethods_AreRejected`(9 种输入全拒)。验收报告存 `.qa-logs/t14-acceptance-report.md`。

## 演进规划
- **P0-P2** ✅ 全部完成（P0: JSON解析+配置键 | P1: 能耗字段+差异化 | P2: 协议集成+时序抽象）
- **下一阶段**：消息队列(RabbitMQ)、限流熔断、软删除+审计日志、OPC UA 完善
- **远期**：多节点部署、读写分离、K8s 容器化、微服务拆分

## 项目路径
`H:\IoTPlatform`
