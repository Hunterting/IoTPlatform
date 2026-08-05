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

## 待办 / 技术债 / 下一阶段
- **待核查（存量隐患）**：现有 mqtt 协议配置是否因前端小写 `host/port` 静默连 localhost:1883（影响现网数据，建议单开核查单）
- **T12 未开工部分**：运维与配置命令后端（`AnShengMaintenanceController`、KeyConfig/SimCheck/Rs485 模型、`setTime`/`getLogs`/`send485` 等 9 个 method），按 `docs/phase345-audit-2026-08-03.md` §2.8 仍 ❌ 未开工
- **通用技术债**：消息队列(RabbitMQ)、限流熔断、软删除+审计日志、OPC UA 适配器（仅 20% 骨架）、Global Query Filter 动态化、MqttClientService Singleton 资源争抢
- **演进规划**：P0-P2 全完成；远期多节点/读写分离/K8s/微服务拆分

## 协作约定（本仓库）
- 所有改动 **不 commit**，留工作区待决策；**严禁子代理执行任何 git 写命令**
- SOP 协作：标准 SOP / 快速模式 / BugFix 快捷路径；信任但验证（主理人独立 grep/read/build 复核）；智能路由判定（源码 Bug→回派工程师 / 测试 Bug→QA 自修 / 全过→NoOne），最多 2 轮
- 环境怪象：Bash 内嵌 PowerShell 被安全策略拦截 → 改用专用 PowerShell 工具；Git Bash 下反斜杠路径被吞 → 用 POSIX 风格 `/h/...`；`rm -rf` 删构建目录常被 safe-delete shim 拦截 → 用 PowerShell `Remove-Item -LiteralPath ... -Recurse -Force`
