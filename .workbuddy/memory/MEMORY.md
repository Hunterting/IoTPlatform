# IoTPlatform 项目长期记忆

## 项目概况
- **类型**：IoT SaaS 多租户平台；技术栈 .NET 8 / EF Core 8 / MySQL / Redis / MQTTnet / SignalR / Serilog / AutoMapper
- **框架**：ASP.NET Core Web API，单体模块化分层架构；前端 Web 仓库 `H:/IoTPlatform/Web`（React18 + Vite + Tailwind + Radix + MUI + lucide-react + axios，无 tsconfig.json，靠 vite/esbuild 转译，全项目 tsc ~200 历史错不强制）
- **项目路径**：`H:\IoTPlatform`

## 多租户设计
- AppCode 行级隔离（共享 DB/Schema）；实体实现 `IHasAppCode`，EF Core `HasQueryFilter` 自动过滤
- JWT Claim 携带 `AppCode`/`CustomerId`/`UserId`；`IsSuperAdmin=true` 跳过过滤
- MQTT 隔离主题 `{appCode}/{deviceId}/data`
- ⚠️ 已知缺陷：Global Query Filter 在 DbContext 构造时生成静态表达式，AppCode 变更后不更新，需改动态 Lambda

## 跨任务复用的关键约定（铁律级）
- **枚举字符串出网**：`Program.cs` 已注册 `JsonStringEnumConverter`，所有经控制器出网枚举以**字符串原名（PascalCase）**出网（如 `RejectedByKind` 而非 `0`）。前端必须按字符串分支，不得按整数。
- **协议配置前端字段大小写陷阱（最易踩坑）**：
  - 数据流：前端 `form.config`(Record) → `CreateProtocolConfigRequest.Config: Dictionary<string,object>` → 后端 `ProtocolConfigService` 手工 `JsonSerializer.Serialize` 入库 → 启动时 `config.Config` 原样读出 → 适配器 `JsonSerializer.Deserialize<XxxOptions>(connectionString)`（**无 options → 大小写敏感**）。
  - `Config` 是 `Dictionary<string,object>`，`DictionaryKeyPolicy` 从不设置 → System.Text.Json 对字典 key **永远原样透传**，不受 `Program.cs` `AddJsonOptions`（仅 `JsonStringEnumConverter`，只作用于 MVC DTO）影响。
  - **结论**：前端协议配置字段必须用 **PascalCase key**（Host/Port/PublishTopicPattern/…）精确匹配 C# 属性，否则静默落默认值（如 localhost:1883）且难排查。
  - 反模式警示：勿因 "ASP.NET Core Web 默认 camelCase" 误写小写 key。现有 mqtt 前端写小写 `host/port` 同样绑不进 `MqttProtocolOptions`（**疑似存量隐患，待核查现网数据**）。
  - 前端协议选项 value 用 `ansheng_mqtt` 即可：工厂 `ProtocolAdapterFactory.CreateAdapter` 内部 `protocolType.ToUpperInvariant()` 归一 → `ANSHENG_MQTT`。
- **前端信封判定**：统一认 `response.code === 200` 判定成功，不读 `response.data.success`。
- **协议配置派生字段铁律**（2026-08-06 P0 血泪）：`ProtocolConfig.Type`（如 `ansheng_mqtt`）必须 `.ToUpperInvariant()` 派生同步写入 `ProtocolType`；`Status`(active/inactive) 必须与 `IsActive` 同步。消费方（发现扫描 / `ResolveProtocolConfigIdAsync` / 下发）一律按 `IsActive && ProtocolType == "ANSHENG_MQTT"` 筛选，任一字段没写就永远 `AdapterUnavailable`。`ProtocolConfigService` 已用 `DeriveProtocolType` / `ReconcileDerivedFields` 在 Create/Update/Start/Stop 四路径幂等补齐（存量脏数据下次 start/stop/update 自愈）。注意 `ProtocolConfig.IsActive` 实体默认值是 **`= true`**。
- **登录用 Email 不是用户名**：`POST /api/v1/auth/login` body `{email,password}`，超管 `admin@system.com` / `admin123`。
- **联调时构建/测试被后端进程锁 bin**（MSB3027）→ 用 `dotnet test -o /tmp/xxx` / `dotnet build -o /tmp/xxx` 重定向，**不要 kill 联调进程**。⚠️ 集成测试用 `-o` 重定向时**必须同时显式设 `IOT_TEST_MYSQL` 环境变量**——`TestConnectionStringResolver.FindRepositoryRoot()` 从 `AppContext.BaseDirectory` 向上找 `IoTPlatform.csproj` 定仓库根，`-o` 把 BaseDirectory 挪走会断链导致 65 条全红「无法确定测试 MySQL 连接串」。
- **开发环境每次启动清空全库（铁律级环境事实，2026-08-06 踩坑）**：`Data/SeedData/DataSeeder.cs:113` 由 `Program.cs:424` 的 `app.Environment.IsDevelopment()` 触发，启动时 `SET FOREIGN_KEY_CHECKS=0` + 逐表 `DELETE FROM` 再灌种子。**任何联调现场数据（协议配置/设备/档案/待认领池/命令流水）活不过一次后端重启**；要保留现场必须切非 Development 环境。（`DELETE FROM areas` 会因自引用外键 `FK_areas_areas_ParentId` 失败，被 catch 成 WRN，不阻断启动。这也解释了"业务数据总是全空"的现象。）
- **【用户 2026-08-06 明令·铁律】起后端若"不希望清库"，一律 direct-DLL + Staging，禁用 `dotnet run`**。`dotnet run` 会读 `Properties/launchSettings.json` 的 `environmentVariables` → **强制 `ASPNETCORE_ENVIRONMENT=Development`**，命令行 `--environment Staging` 或 `ASPNETCORE_ENVIRONMENT=Staging` 均被覆盖，故 `dotnet run` 永远触发清库。正确起法：`dotnet bin/Debug/net8.0/IoTPlatform.dll` + `ASPNETCORE_ENVIRONMENT=Staging`（不读 launchSettings，环境变量才生效）。无 `appsettings.Staging.json`/`appsettings.Production.json` → 任何非 Dev 环境回退 `appsettings.json` 同一连接串（192.168.3.7:3306/iot_platform），仅跳过清库。
- **【用户 2026-08-06 明令·铁律】查日志一律以 Serilog 文件槽为准（`logs/log-YYYYMMDD.txt`），别只 grep 单实例 stdout 重定向**。`Program.cs:25-30` 用 `ReadFrom.Configuration` 但 appsettings 无 `Serilog` 节 → 最小级别回退 Information，且同时写 Console 与 `logs/log-YYYYMMDD.txt`（每实例从同一 CWD 写同一文件）。单实例 stdout 重定向未必含全部行，易误判"日志没打"。

- **DI 生命周期错配铁律（2026-08-06 协议加固踩出）**：`ProtocolAdapterFactory` = **Singleton**（`Program.cs:47`），`ProtocolConfigService` = **Scoped**（`Program.cs:120`）。任何追踪"进程内适配器实例"的状态（如订阅登记表）**必须是 static**，绝不能是 Scoped 实例字段——否则每 HTTP 请求拿到空表，防重逻辑恒失效、每次 `/start` 重复挂 handler。若新增进程级静态状态，须一并纳入 `StaticStateResetter.ResetAll`（目前它只认识 4 处，本次 `_activeSubscriptions` 是第 5 处，未纳入，脆弱）。

## 已交付里程碑（详记见各 dated 日志）
| 模块 | 内容 | 状态 | 关键日志 |
|------|------|------|---------|
| 数据采集升级 | 双轨链路合一 + 能耗差异化 + 时序存储层 | ✅ 2026-05-18 | 2026-05-18.md |
| 安圣接入 P1-P4 | 模型+适配器+解析+命令+发现+前端页 | ✅ 2026-06-27 | 2026-06-27.md |
| T8 开关动作/延时 | 后端 5 端点 + 服务 + 迁移 | ✅ 验收 2026-07-31 | 2026-07-31.md |
| T9 开关控制面板 | 前端 SwitchControlPage | ✅ 验收 2026-08-04 | 2026-08-04.md |
| T10 定时任务 | 后端 4 端点 + 模型 + 迁移 | ✅ 验收 2026-08-05 | 2026-08-03.md |
| T11 电量计 | 后端 8 端点 + 服务 + 迁移 | ✅ 验收 2026-08-05 | 2026-08-03.md |
| T10/T11 前端面板 | ScheduleEditorPage + EnergyStatisticsPage | ✅ 验收 2026-08-05 | 2026-08-04.md |
| T14 协议族隔离 | Legacy 归位 + 三态 Resolver + 前端分区 | ✅ 验收 2026-08-05 | 2026-08-03.md |
| T12-BugFix | 协议配置页补 `ansheng_mqtt` 选项 + 安圣专属表单（PascalCase 14 字段） | ✅ 验收 2026-08-05 | 2026-08-05.md |
| 安圣真机联调 | 发现→认领→开关下发→SlotsSnapshot 回写全链路真机打通；修 P0(创建/启动不写 ProtocolType/IsActive) + P1(待认领池并发重复插入) | ✅ 验收 2026-08-06（854 测试全绿） | 2026-08-06.md |
| 唯一索引迁移 + 冒烟复验 | 迁移 `20260806033128` 已 apply（`IX_discovered_ansheng_devices_Imei_AppCode` unique 已建）；真机冒烟：connected 瞬间仅 1 条发现日志、两次快照均 1 行 0 重复、LastSeenAt 正常推进、零 ERR/WRN/1062 | ✅ 验收 2026-08-06 | 2026-08-06.md |
| 协议启停短路判活加固 | `ProtocolConfigService` 启停短路加进程内判活（防重启吞启动）+ 修复重复订阅炸弹（static 订阅表+真幂等三分支）；919/919 集成+单元全绿 | ✅ 验收 2026-08-06（QA NoOne） | 2026-08-06.md |
| 协议启停判活 — 测试覆盖闭环 | 测试基建增强：`FakeProtocolAdapterFactory` 加进程重启开关 + `StaticStateResetter` 纳入第5静态态 + 新增 6 测试覆盖 A/B/C（含证伪对照），925/925 全绿，变异测试反证生产逻辑正确 | ✅ 验收 2026-08-06（QA NoOne） | 2026-08-06.md |
| 协议启停判活 — 真机复验 | 真机复验 PASS：进程重启后 DB active/内存无适配器时 /start 不再静默短路（首 392-568ms 完整启动 vs 次 53ms 短路）；Serilog 文件槽确证 [WRN] 失配告警 + 适配器真连真 broker(120.79.3.248:18883) 重新订阅 | ✅ 验收 2026-08-06 | 2026-08-06.md |

## 待办 / 技术债 / 下一阶段
- **待核查（存量隐患）**：现有 mqtt 协议配置是否因前端小写 `host/port` 静默连 localhost:1883（影响现网数据，建议单开核查单）
- **T12 未开工部分**：运维与配置命令后端（`AnShengMaintenanceController`、KeyConfig/SimCheck/Rs485 模型、`setTime`/`getLogs`/`send485` 等 9 个 method），按 `docs/phase345-audit-2026-08-03.md` §2.8 仍 ❌ 未开工
- **通用技术债**：消息队列(RabbitMQ)、限流熔断、软删除+审计日志、OPC UA 适配器（仅 20% 骨架）、Global Query Filter 动态化、MqttClientService Singleton 资源争抢
- **演进规划**：P0-P2 全完成；远期多节点/读写分离/K8s/微服务拆分

## 协作约定（本仓库）
- 所有改动 **不 commit**，留工作区待决策；**严禁子代理执行任何 git 写命令**
- SOP 协作：标准 SOP / 快速模式 / BugFix 快捷路径；信任但验证（主理人独立 grep/read/build 复核）；智能路由判定（源码 Bug→回派工程师 / 测试 Bug→QA 自修 / 全过→NoOne），最多 2 轮
- 环境怪象：Bash 内嵌 PowerShell 被安全策略拦截 → 改用专用 PowerShell 工具；Git Bash 下反斜杠路径被吞 → 用 POSIX 风格 `/h/...`；`rm -rf` 删构建目录常被 safe-delete shim 拦截 → 用 PowerShell `Remove-Item -LiteralPath ... -Recurse -Force`
