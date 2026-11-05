# 安圣设备注册双路径收敛设计

> 作者：架构师 高见远（software-architect）
> 范围：仅设计，不写业务代码；含现状写实、风险分析、收敛方案、实现任务清单、待确认事项。
> 代码实查基准：`H:\IoTPlatform`（.NET 8 + EF Core 8 + MQTTnet + SignalR）、`H:\IoTPlatform\Web`（React18 + TS + Vite + Tailwind + MUI + axios）。

---

## 1. 现状写实（两条路径 + 多租户模型）

### 1.1 路径①：发现 → 待认领池 → 认领（超级管理员主用）

| 环节 | 前端入口 | 后端入口（接口） | 服务端处理类 / 方法 | 落库表 | 权限要求 | 是否去重 / 校验 |
|---|---|---|---|---|---|---|
| 触发发现 | `Web/src/app/pages/AnShengManagementPage.tsx`（"待认领"Tab、"触发发现扫描"按钮 L142-152） | `POST /api/v1/ansheng/discover` | `AnShengController.TriggerDiscovery` L65-79 → `IAnShengCommandService.TriggerDiscoveryAsync` | — | `CREATE_DEVICES` | — |
| 拉取待认领列表 | 同上页（L117-140 加载列表） | `GET /api/v1/ansheng/discovered` | `AnShengController.GetDiscoveredDevices` L84-175 | `discovered_ansheng_devices` | `VIEW_DEVICES`（类级）+ `AppCode` 租户过滤 L98-101 | **仅按 AppCode 过滤可见性，无去重**（唯一索引兜底见 §1.5） |
| 设备连上 MQTT 写入待认领池 | —（被动） | 经 `AnShengUplinkHub` 总线 | `AnShengUplinkPipeline.OnUplink` L137-169 → `ConnectedEventHandler.OnHandleAsync` L46-62 → `AnShengDiscoveryService.OnDeviceOnlineAsync` L264-315 → `UpsertDiscoveredAsync` L343-438 | `discovered_ansheng_devices` | 后台线程，无鉴权 | **按 (IMEI, AppCode) 幂等 upsert**；**不按设备品类筛选** |
| 数据桥接二次写入 | —（被动） | `ProtocolConfigService.OnProtocolAdapterDataReceived` | 未注册设备分支 L786-804 调 `OnDeviceOnlineAsync`；已注册设备 L814-819 通知上线 | `discovered_ansheng_devices` / `devices` | 后台线程，无鉴权 | 同上；仅按 `ProtocolType == "ANSHENG_MQTT"` 判定 |
| 认领 | 认领弹窗 L90-101，`claimDevice` `anshengApi.ts` L56-59 → `POST /ansheng/claim` | `POST /api/v1/ansheng/claim` | `AnShengController.ClaimDevice` L193-268 → `AnShengDiscoveryService.ClaimAsync` L535-760 | `discovered_ansheng_devices`（置 `IsClaimed` / `ClaimedDeviceId`）、`devices`、`ansheng_device_profiles`、`ansheng_device_configs` | `CREATE_DEVICES` | **认领前查 IMEI 冲突** L588-600；探测失败不建 `Device` L643-653；事务内四步写入 L664-728 |

**关键文件与行号：**
- 发现服务：`Services/AnShengDiscoveryService.cs`（upsert L343-438；ClaimAsync L535-760）
- 连接事件：`Services/AnShengEventHandlers/ConnectedEventHandler.cs`（Method="connected" L43，调 `OnDeviceOnlineAsync` L53）
- 数据桥接：`Services/ProtocolConfigService.cs` L786-819
- 上报总线：`Infrastructure/Protocol/AnSheng/AnShengUplinkPipeline.cs`（BuildContextAsync AppCode 五级兜底 L204-286，`DefaultAppCode` 在 L264-267）
- 适配器：`Infrastructure/Protocol/Adapters/AnShengMqttProtocolAdapter.cs`（`OnMessageReceivedAsync` L626-696，L654 把**所有**上行发布到总线，无品类门控）
- 待认领模型：`Models/DiscoveredAnShengDevice.cs`（表 `discovered_ansheng_devices`）
- 唯一索引迁移：`Migrations/20260806033128_AddDiscoveredAnShengUniqueImeiAppCode.cs`（UNIQUE(Imei, AppCode)）

### 1.2 路径②：设备管理-直接新增（超级管理员 + 租户均可）

| 环节 | 前端入口 | 后端入口（接口） | 服务端处理类 / 方法 | 落库表 | 权限要求 | 是否去重 / 校验 |
|---|---|---|---|---|---|---|
| 直接新增设备 | `Web/src/app/pages/DevicesPage.tsx`（"添加设备"按钮 L181 → `DeviceFormModal` L457）→ `deviceApi.ts` `createDevice` L77-79 → `POST /devices` | `POST /api/v1/devices` | `DevicesController.CreateDevice` L128-147 → `DeviceService.CreateDeviceAsync` L235-445 | `devices` | `CREATE_DEVICES` | **无 IMEI/SerialNumber 去重**；**无在线/IMEI 存在性校验**；**无调用者 AppCode == 请求 AppCode 校验**；仅校验 Area/Project 与请求 AppCode 从属一致（L251、L343） |

**关键文件与行号：**
- 控制器：`Controllers/DevicesController.cs`（`CreateDevice` L128-147，仅 `[PermissionAuthorize(Permissions.CREATE_DEVICES)]`）
- 服务：`Services/DeviceService.cs` `CreateDeviceAsync` L235-445（AppCode 直接取 `request.AppCode` L409；`_dbContext.Devices.Add` L444）
- 入参：`DTOs/Requests/CreateDeviceRequest.cs`（`AppCode` 必填 L14-16；`SerialNumber` 可空 L21-22）
- 关联：`DeviceSensor` / `ProtocolConfig` 在"直接新增"链路中**不自动关联**——新增后设备仅为一条 `devices` 行，无 `AnShengDeviceProfile`、`AnShengDeviceConfig`，也无法被 `getDevStatus`/命令链路识别（与认领路径产出的"完整设备"结构不同）。

### 1.3 多租户与权限模型

- **超级管理员判定**：`Filters/PermissionFilter.cs` L37-39 — `ClaimTypes.Role == Roles.SUPER_ADMIN` 直接放行，跳过权限与（全局查询过滤外的）其余校验。
- **全局行级隔离**：`Data/AppDbContext.cs` `ConfigureGlobalQueryFilters` L167-201 — 对实现 `IHasAppCode` 的实体加 `WHERE AppCode = ?`；`IsSuperAdmin == true` 时整体跳过（L174-177）。`discovered_ansheng_devices`、`devices` 均实现 `IHasAppCode`（L13、L188）。
- **角色权限**：`Configuration/RoleConfig.cs` — `GetSuperAdminPermissions` 与 `GetAdminPermissions`（租户管理员 = 超级管理员去掉 API 配置权限，L133-140）**都包含 `CREATE_DEVICES`（L46-48 / 继承）**。即**租户管理员同样拥有"直接新增设备"权限**。
- **AppCode 注入**：控制器多从 `User.FindFirst("AppCode")?.Value` 取值（如 `DevicesController` L39、`AnShengController` L93）。超级管理员 token 通常无 `AppCode` claim（以 Role 标识平台级身份）。

### 1.4 ★ 核心问题回答：二开设备（4G/WiFi 喇叭、4G/WiFi 开关）当前是否进入认领池？

**结论：在代码层面，二开设备"应该"被纳入待认领池——发现写入逻辑无任何品类/型号门控；但前提是它在平台订阅的 `ANSHENG_MQTT` 通道上真的发出了上行帧。**

证据链：
1. 写入入口 `UpsertDiscoveredAsync`（`AnShengDiscoveryService.cs` L343-438）与 `OnDeviceOnlineAsync`（L264-315）**只认 IMEI**，**不判断设备品类（Kind）/ 型号 / 是否为二开**。
2. 两条触发线都不区分品类：
   - `ConnectedEventHandler` 监听 `connected` 事件（L43），任何连上并上报 `connected` 的安圣/二开设备都会进入；
   - `ProtocolConfigService.OnProtocolAdapterDataReceived` 在 `ProtocolType == "ANSHENG_MQTT"` 时（L786），把"未匹配到已注册设备"的上行一律送进待认领池（L802-804）。
3. 适配器 `AnShengMqttProtocolAdapter.OnMessageReceivedAsync`（L626-696）对**所有**上行帧 `Publish` 到 `AnShengUplinkHub`（L654），未做品类过滤；`AnShengController` 注释（L404-412）亦表明二开设备（喇叭/开关）复用同一 `ANSHENG_MQTT` 协议与 `action / actions / getDevStatus` 指令。

**因此"二开设备不被发现"不是代码显式排除，而是以下现实原因之一：**
- (a) 固件"连上 MQTT"但**从不主动上行** `connected` / `getDevStatus` / 自动上报（TCP/MQTT 层在线 but silent）→ 两条触发线都不触发，池子为空；
- (b) 该设备的上行**未落在平台已订阅的 `ANSHENG_MQTT` topic/协议配置**上（topic 模板、`ProtocolConfig` 未配或配错）→ 平台根本收不到；
- (c) 租户视角下，待认领列表按 `AppCode` 过滤（L98-101），若记录的 `AppCode` 解析到别的租户/默认值（如 `DefaultAppCode` 配置的 `system`），非超级管理员看不到。

### 1.5 为什么"认领设备记录表"会空？——根因判定

对**超级管理员**而言（`GetDiscoveredDevices` 中 `appCode` 为空 → L98 过滤不生效 → 可见全部行），"池子空"=**确实没有写入任何行**。最可能是 §1.4 的 (a)/(b)：设备已连 MQTT，但平台 `ANSHENG_MQTT` 摄取路径**从未收到它的上行帧**，发现写入器从来没跑过。

次要隐患（影响租户视角与数据质量）：
- 列表 `AppCode` 过滤 L98-101 与 pipeline 的 AppCode 五级兜底（`AnShengUplinkPipeline` L204-286，落到 `DefaultAppCode` 或空串）耦合，易产生"记录存在但某角色看不到"的假空。
- `UNIQUE(Imei, AppCode)`（`AppDbContext` L449）**不约束 AppCode 为 NULL 的行**（MySQL 语义，`DiscoveredAnShengDevice` 注释 L447 已自陈），多实例下 AppCode 为 NULL 的重复行仍可能钻空子（由进程内闸门 L355 兜底，但仅单实例有效）。

---

## 2. 风险与问题（双路径并存）

| # | 风险 | 机理（文件/行号） | 影响 |
|---|---|---|---|
| R1 | **重复设备** | 路径①发现写入 + 路径②手动 `CreateDevice` 互不感知；手动新增 **不查 `discovered_ansheng_devices`、不查 `devices.SerialNumber`**（`DeviceService.CreateDeviceAsync` L235-445 无唯一校验） | 同一 IMEI 既在待认领池、又有一条手动 `Device`，认领时 `ClaimAsync` L588-600 虽能拦"已存在 Device"，但**反向**（先认领后手动加）会造重复 |
| R2 | **孤儿设备 / 结构残缺** | 路径②产出纯 `devices` 行，无 `AnShengDeviceProfile` / `AnShengDeviceConfig`（`CreateDeviceRequest` 无这些字段） | 该设备无法被 `getDevStatus`、命令链路、`AnShengUplinkPipeline.BuildContextAsync`（L221-237 按 IMEI 查 Device/Profile）正确识别，状态/指令异常 |
| R3 | **权限越界（租户可任意建设备）** | `DevicesController.CreateDevice` 仅 `[PermissionAuthorize(CREATE_DEVICES)]`（`RoleConfig` 中**租户管理员也持有**该权限）；且 `AppCode` 取自请求体（L409）**无"请求 AppCode == 调用者 AppCode"服务端校验** | 租户管理员可把设备建到**任意 AppCode（别的租户）**下；多租户隔离被绕过 |
| R4 | **绕过认领流程** | 路径②完全不经过发现/探测/品类判定 | 设备未经验证即"存在"，认领页的"待认领→探测→建档"质控形同虚设 |
| R5 | **可见性假空 / 数据一致性** | `GetDiscoveredDevices` 的 AppCode 过滤 L98-101 + 解析兜底耦合；NULL AppCode 行唯一索引不约束 | 同一设备在不同角色视角下"有时有有时无"，运维误判"设备丢失" |
| R6 | **双入口维护成本** | 两套页面、两套校验、两套关联逻辑长期并行 | 后续每次设备模型变更都要改两处，回归面翻倍 |

---

## 3. 收敛设计方案

### 3.1 唯一规范的设备注册入口（目标态）

```
              ┌─────────────────────────────────────────────┐
   安圣/二开设备  │   MQTT 连上 / 上行(connected·getDevStatus·自动上报)  │
   上行帧        └──────────────────────┬──────────────────────┘
                                        │ AnShengUplinkHub
                                        ▼
                    发现服务 OnDeviceOnlineAsync → UpsertDiscoveredAsync
                                        │  (按 IMEI 幂等写 discovered_ansheng_devices)
                                        ▼
                        ┌──────── 待认领池（唯一来源）────────┐
                        │  AppCode 解析 + 探测能力快照         │
                        └───────────────┬─────────────────────┘
                  ┌─────────────────────┴─────────────────────┐
          超级管理员认领（选租户/区域/品类）          租户在自己的待认领记录上认领
                  └─────────────────────┬─────────────────────┘
                                        ▼
                        ClaimAsync：IMEI 冲突校验 → 探测 → 建 Device+Profile+Config
                                        │
                                        ▼
                              正式 Device（结构完整）
```

- **所有安圣/二开设备一律先经发现进入待认领池**，再经认领落为正式 `Device`。认领是"把待认领记录 → 正式设备"的**唯一**受控通道。
- **"直接新增"降级为超级管理员的兜底/批量导入手段**：
  - 仅 `super_admin` 可见、可调用；**租户账号隐藏入口、接口拒绝非超级管理员**。
  - 创建时**强制去重校验 + 在线/IMEI 校验**：若 `discovered_ansheng_devices` 已有同 IMEI 记录，提示"请走认领"；若 `devices` 已有同 `SerialNumber`，直接冲突拒绝；并要求在有效 `ANSHENG_MQTT` 协议配置存在时才允许。

### 3.2 权限边界（明确可执行动作）

| 动作 | 超级管理员 | 租户（admin/operator…） |
|---|---|---|
| 查看待认领池 | ✅ 全部租户 | ✅ 仅本租户（`AppCode` 过滤） |
| 认领设备（指定归属租户/区域/品类） | ✅（核心职责） | ✅ 仅认领**归属本租户**的待认领记录；**不可跨租户** |
| 直接新增设备（兜底/批量） | ✅（限超级管理员） | ❌ **禁用**（入口隐藏 + 接口 403） |
| 设备归属变更 | ✅ | ❌（设备一经认领归属固定） |

> 关键改动：`DevicesController.CreateDevice` 增加 `IsSuperAdmin` 强校验（`PermissionFilter` 已能在服务端取 `ClaimTypes.Role`），并把 `AppCode` 改为**由服务端按调用者身份确定**（超级管理员显式指定目标租户；移除请求体 `AppCode` 的可信度）。

### 3.3 二开设备"未被发现"的取舍

| 方案 | 代价 | 建议 |
|---|---|---|
| **A. 补齐发现（推荐）**：确认二开固件上行落到 `ANSHENG_MQTT` 订阅；如固件不主动发 `connected`，在 `ProtocolConfigService` 数据桥接（L786）已能兜住 `getDevStatus` 上行——**确保二开设备有周期性自动上报** | 低；主要依赖固件/协议配置正确性，平台侧无需改发现逻辑（已品类无关） | ✅ 主方案：平台发现逻辑已覆盖二开，重点放在"保证上行可达 + 监控待认领池写入" |
| **B. 统一在新增时校验**：若二开确实无法走发现，则要求"直接新增"必须填 IMEI 且在线校验，并反向回写待认领池 | 中；需新增"在线探测+池子回写"逻辑，且仍受 R3 权限约束 | 作为 A 的兜底，不单独采用 |

**结论**：当前平台发现逻辑**已包含**二开设备（无品类门控）。若现场仍"二开不在池"，优先排查 §1.4 的 (a)/(b)（固件是否上行、ProtocolConfig/topic 是否匹配），而非改发现代码。

### 3.4 去重 / 防孤儿策略

1. **唯一约束**：保留并强化 `UNIQUE(Imei, AppCode)`（`AppDbContext` L449）；对 `devices.SerialNumber` 增加**跨租户唯一**约束（或应用层在 `CreateDevice` 与 `ClaimAsync` 两端都查 `devices` 已有同 `SerialNumber`，如 `ClaimAsync` L588-600 已有）。
2. **认领前先查已存在 Device**：`ClaimAsync` 已做（L588-600），保持并复用同一校验到 `CreateDevice`。
3. **手动新增若认领池已有同 IMEI：提示走认领**，不重复建行；若 `devices` 已有同 IMEI：直接 409 冲突拒绝。
4. **AppCode 解析收敛**：待认领记录的 `AppCode` 解析统一从 pipeline 五级兜底改为"首次发现即按设备上报/协议配置确定且不再被空值回填覆盖"，消除 NULL 行的可见性歧义；列表过滤对超级管理员始终返回全部。
5. **结构完整性**：任何新增/认领产出的 `Device` 必须带 `AnShengDeviceProfile` + `AnShengDeviceConfig`（认领路径已满足；直接新增路径须补齐或禁止产生无档案设备）。

### 3.5 前后端改造点

| 页面 / 接口 | 现状 | 改造 |
|---|---|---|
| `AnShengManagementPage.tsx` 待认领 Tab | 超级管理员与租户都可见 | 保留；租户仅见本租户记录（后端已过滤） |
| `DevicesPage.tsx` "添加设备"按钮 L181 + `DeviceFormModal` L457 | 所有角色可见 | **超级管理员可见**；租户**隐藏**（前端按 `Role==super_admin` 置灰/隐藏） |
| `POST /devices` | 任何 `CREATE_DEVICES` 角色可调用 | 加 `IsSuperAdmin` 服务端校验；`AppCode` 改由服务端定；加 IMEI/在线去重校验 |
| `POST /ansheng/claimed`-类认领 | 已较完善 | 租户认领时强制 `AppCode == 调用者 AppCode`（后端 `ClaimAsync` 现已用 `User.FindFirst("AppCode")` L237，需确保非超级管理员不可传别的 AppCode） |
| 待认领列表过滤 | L98-101 | 超级管理员不过滤；租户严格 `AppCode == 自身`；处理 NULL AppCode 行的语义 |

---

## 4. 实现任务清单

> 估人天为前端+后端协作口径，含单测与联调；验收标准可量化。

### T1 — 补齐发现可达性核查 + 待认领写入可观测
- **标题**：确认二开/安圣上行落入 `ANSHENG_MQTT` 订阅，并为发现写入补埋点
- **涉及文件**：`Services/AnShengDiscoveryService.cs`（L264-315、L343-438 增日志）、`Services/ProtocolConfigService.cs`（L786-819 增日志）、`Infrastructure/Protocol/Adapters/AnShengMqttProtocolAdapter.cs`（L626-696 增 topic/imei 日志）、`docs/ansheng-claim-consolidation.md`（补核查清单）
- **依赖**：无
- **预估人天**：1.5
- **验收**：① 在测试环境用一台 4G 喇叭/4G 开关连 MQTT，确认 `discovered_ansheng_devices` 出现对应 IMEI 行；② 若未出现，定位是固件未上行还是 ProtocolConfig/topic 不匹配，输出核查报告。

### T2 — 收敛"直接新增"权限与去重校验（后端）
- **标题**：`CreateDevice` 仅限超级管理员 + 强制 IMEI/在线去重 + AppCode 服务端定
- **涉及文件**：`Controllers/DevicesController.cs`（L128-147 加 `IsSuperAdmin` 校验）、`Services/DeviceService.cs`（`CreateDeviceAsync` L235-445 增 `devices.SerialNumber` 唯一查、在线/IMEI 校验、`AppCode` 改由身份决定）、`DTOs/Requests/CreateDeviceRequest.cs`（移除/弱化 `AppCode` 必填可信度）、`Configuration/RoleConfig.cs`（不改，仅确认租户无此权限后由 filter 拦截）
- **依赖**：T1
- **预估人天**：2.5
- **验收**：① 租户 token 调 `POST /devices` 返回 403；② 同一 `SerialNumber` 二次新增返回 409；③ 超级管理员指定目标 `AppCode` 创建设备，落库 `AppCode` 正确且与调用者身份一致；④ 若待认领池已有同 IMEI，返回"请走认领"提示。

### T3 — 认领路径权限与一致性加固
- **标题**：认领强制租户边界 + 复用统一去重校验
- **涉及文件**：`Controllers/AnShengController.cs`（`ClaimDevice` L193-268 校验非超级管理员时 `AppCode == 调用者`）、`Services/AnShengDiscoveryService.cs`（`ClaimAsync` L588-600 去重校验抽为公共方法，供 T2 复用）、`Data/AppDbContext.cs`（L449 评估 `devices.SerialNumber` 跨租户唯一约束）
- **依赖**：T2
- **预估人天**：2
- **验收**：① 租户 A 不能认领归属租户 B 的待认领记录（传 B 的 AppCode 被拒）；② 认领与直接新增走同一 IMEI 冲突校验，互不复现 R1 重复；③ 探测失败不建 Device（既有 L643-653 保持）。

### T4 — 待认领池可见性与 AppCode 解析收敛
- **标题**：超级管理员列表不过滤、租户严格自见、消除 NULL AppCode 歧义
- **涉及文件**：`Controllers/AnShengController.cs`（`GetDiscoveredDevices` L98-101 改为超级管理员不过滤、租户按自身 AppCode）、`Infrastructure/Protocol/AnSheng/AnShengUplinkPipeline.cs`（`BuildContextAsync` L204-286 收敛 AppCode 解析，减少空值回填）、`Models/DiscoveredAnShengDevice.cs` + 迁移（评估给 `AppCode` 设默认值或 NOT NULL 以匹配唯一索引语义）
- **依赖**：T1
- **预估人天**：2
- **验收**：① 超级管理员看到全部待认领记录（含历史 AppCode 行），不再假空；② 租户只看到本租户记录；③ 同一 IMEI 在池中不产生 NULL AppCode 重复行。

### T5 — 前端入口治理 + 回归测试
- **标题**："添加设备"按角色显隐 + 双路径联调回归
- **涉及文件**：`Web/src/app/pages/DevicesPage.tsx`（L181 按钮、`DeviceFormModal` L457 按 `Role` 隐藏）、`Web/src/app/config/permissions.ts`（角色→可见性映射）、`Web/src/app/services/api/deviceApi.ts`（L77-79 调用层容错 403/409）、`tests/IoTPlatform.AnSheng.Tests/*`、`tests/IoTPlatform.IntegrationTests/*`（新增双路径去重/权限用例）
- **依赖**：T2、T3、T4
- **预估人天**：3
- **验收**：① 租户登录 `DevicesPage` 无"添加设备"入口；② 端到端：设备上行→待认领→认领→正式 Device（结构含 Profile/Config）全链路通过；③ 重复/越界用例全红→绿；④ 现有设备列表、控制页功能无回归。

**依赖关系**：T1 → T2 → T3 → T5；T1 → T4 → T5。合计约 **11 人天**。

---

## 5. 待确认事项（需用户拍板）

1. **是否完全禁用租户"直接新增设备"？**（本方案建议：禁用，仅超级管理员兜底。）还是有特定租户场景必须保留？
2. **二开设备是否启用自动发现？**（本方案判定平台逻辑已包含，建议优先核查固件上行/ProtocolConfig 匹配；若确实无法上行，是否接受"直接新增 + 在线校验"兜底？）
3. **认领池空时租户如何加设备？** 若禁用租户直接新增，是否接受"租户上报 IMEI → 超级管理员/系统发现后由租户在待认领页认领"的流程？
4. **`devices.SerialNumber` 是否要做跨租户唯一约束？**（影响 R1 去重强度与既有数据迁移，需评估历史脏数据。）
5. **超级管理员"直接新增"的目标租户由谁指定、如何防误操作？**（界面选择 vs 参数；是否需要二次确认/审计日志。）

---

### 一句话总结

两条路径现状：路径①（发现→待认领池→认领）对所有 `ANSHENG_MQTT` 上行设备**品类无关地**写入，路径②（`DevicesController.CreateDevice`）仅做 `CREATE_DEVICES` 鉴权、无 IMEI 去重、无在线校验、且 `AppCode` 取自请求体——导致租户可越界建任意设备并产生孤儿/重复。最关键的两个发现：**(1) 二开设备（4G/WiFi 喇叭/开关）在代码层面"应"被纳入待认领池（发现逻辑无品类门控），"不在池"多半是固件未上行或 ProtocolConfig/topic 不匹配，而非平台排除；"认领表空"对超级管理员而言即确无写入行，根因是平台从未收到该设备的上行帧。** 推荐收敛口径：**所有安圣/二开设备统一先经发现进入待认领池→认领为唯一正式注册通道；"直接新增"降级为超级管理员专属兜底，创建时强制 IMEI/在线去重与 AppCode 服务端确定，彻底消除双录入带来的重复、孤儿与权限越界。**

---

## 6. 实现记录与核查（工程师落地，T1–T5）

> 本节由实现工程师寇豆码补充，覆盖代码落地与**诚实的**核查状态。设计决策（§5 待确认事项）中用户已拍板：① 完全禁用租户"直接新增设备"（入口隐藏 + 接口 403）；② 二开设备沿用既有发现逻辑，重点保证上行可达。

### 6.1 T1 核查记录（★ 诚实，非伪造）

**代码埋点（已实现）：**

| 文件 | 新增可观测点 |
|---|---|
| `Services/AnShengDiscoveryService.cs` | `OnDeviceOnlineAsync` 入口记 IMEI/AppCode/Model/NetType；`UpsertDiscoveredAsync` 命中待认领池时记 DEBUG（IMEI + 是否回填 AppCode） |
| `Services/ProtocolConfigService.cs` | 未注册设备→待认领池分支记 INFO：`安圣数据桥接→待认领池: IMEI=…, ProtocolType=…, AppCode=…` |
| `Infrastructure/Protocol/Adapters/AnShengMqttProtocolAdapter.cs` | 上行到达记 INFO `安圣 MQTT 上行到达: Topic=…, IMEI=…`（**不打印 payload 明文**，满足脱敏）；未知 IMEI 告警不打印 payload |

**运行时核查状态：未执行（Pending-Env）。**

- 原因（诚实说明）：本离线构建/CI 环境中**没有 4G 喇叭 / 4G 开关实机**，也**没有可达的 `ANSHENG_MQTT` 订阅**，因此无法实跑 §T1 验收①「连 MQTT 确认 `discovered_ansheng_devices` 出现对应 IMEI 行」。本次**未伪造任何发现数据或日志**。
- 这与设计 §1.5 的根因判定一致：对超级管理员而言"池子空 = 确无写入行"，根因是平台从未收到该设备上行的帧（固件未上行 / ProtocolConfig·topic 不匹配），属**环境/配置问题，非代码缺失**。平台侧发现逻辑本身品类无关（§1.4），无需改。
- **留给联调环境的验证方法**（建议录入测试报告，不在本提交内）：
  1. 取一台 4G 喇叭/4G 开关，确保其固件周期上行 `connected`/`getDevStatus`/自动上报，且落在平台已订阅的 `ANSHENG_MQTT` ProtocolConfig 上；
  2. 在后端日志 grep 关键字依次出现即证明链路打通：
     - `安圣 MQTT 上行到达` → `安圣数据桥接→待认领池` 或 `OnDeviceOnlineAsync` 的 IMEI 行；
  3. 确认 `discovered_ansheng_devices` 出现该 IMEI 行；若未出现，按 §1.4 (a)/(b) 定位（固件静默 / topic·ProtocolConfig 不匹配）。
- 应用层去重已在 T2/T3 落地（`DeviceRegistrationGuard`，下文 6.2），与"发现是否真的写入"解耦——即使发现写入正常，去重逻辑也已独立覆盖 R1。

### 6.2 T3 `devices.SerialNumber` 跨租户唯一约束 — 评估结论

**语义结论：** 一个物理设备的 IMEI（= `SerialNumber`）在全球范围内唯一，因此 `devices.SerialNumber` 做**跨租户唯一**在语义上是正确的（同一台设备不应同时归属两个租户）。这与 §3.4①的意图一致。

**应用层已覆盖（本次实现选择）：** 不强行加库约束，而是用 `DeviceRegistrationGuard.CheckAsync` 在 `CreateDevice`（直接新增）与 `ClaimAsync`（认领，`includePendingPool:false`）两处共用同一套 `devices.SerialNumber` 冲突校验（命中→ 409 / `AlreadyClaimed`）。足以在写入前拦住 R1 双向重复。原因：**既有 `devices` 数据历史上有大量 `SerialNumber` 为 NULL / 空 / 重复**的存量行，直接 `ADD UNIQUE` 会令迁移失败或破坏存量，故不强制。

**可选迁移脚本（暂缓、需评估后由 DBA 执行，本提交未落地迁移文件）：**
```sql
-- 0) 前置：先清理/回填存量 NULL 或空 SerialNumber（否则 ADD UNIQUE 必失败）
--    例：为无 SerialNumber 的设备补默认占位（需业务确认占位规则，避免引入假唯一）
UPDATE devices
   SET SerialNumber = CONCAT('LEGACY-', Id)
 WHERE SerialNumber IS NULL OR SerialNumber = '';

-- 1) 先查重复，确认无跨行冲突后再加约束
SELECT SerialNumber, COUNT(*) AS cnt
  FROM devices
 WHERE SerialNumber IS NOT NULL AND SerialNumber <> ''
 GROUP BY SerialNumber HAVING COUNT(*) > 1;

-- 2) 重复清零后，加跨租户唯一索引（IMEI 全局唯一，不拼 AppCode）
CREATE UNIQUE INDEX ux_devices_serialnumber
    ON devices (SerialNumber);
```
> 注意：步骤 1 若返回行，说明存量已存在跨/同租户重复，须先人工去重再执行步骤 2；该脚本仅作参考，**未纳入 EF 迁移**以免在存量脏数据上中断部署。

### 6.3 T2 / T3 / T4 / T5 代码落地摘要

| 任务 | 关键改动文件 | 要点 |
|---|---|---|
| T2 | `Controllers/DevicesController.cs`、`Services/DeviceService.cs`、`Services/DeviceRegistrationGuard.cs`（新增）、`DTOs/Requests/CreateDeviceRequest.cs` | 非超级管理员 `CreateDevice` 返回 403；`AppCode` 服务端定（超级管理员须 `TargetAppCode`）；抽出公共 `DeviceRegistrationGuard` 做去重/在线闸门（pending-pool→"走认领"、devices 同 IMEI→409、无活跃 `ANSHENG_MQTT` 配置→拒孤儿） |
| T3 | `Controllers/AnShengController.cs`、`Services/AnShengDiscoveryService.cs`、`Services/Interfaces/IAnShengClaimCommand`（`IsSuperAdmin`）、`AnShengClaimErrorCodes`（`CrossTenant`） | 非超级管理员认领非本租户记录→ `CrossTenant`(403)；`ClaimAsync` 复用 `DeviceRegistrationGuard`（`includePendingPool:false`） |
| T4 | `Controllers/AnShengController.cs`（`GetDiscoveredDevices`）、`Infrastructure/Protocol/AnSheng/AnShengUplinkPipeline.cs` | 超级管理员列表不过滤（含历史 AppCode 行与 NULL 行）；租户严格 `AppCode == 自身`（NULL 行不可见，消假空）；pipeline AppCode 收敛、减少空值回填 |
| T5 | `Web/.../DevicesPage.tsx`、`Web/.../permissions.ts`、`Web/.../deviceApi.ts`、`Web/.../device.types.ts`、`Web/.../deviceAdapter.ts`、`tests/IoTPlatform.AnSheng.Tests/DeviceClaimConsolidationTests.cs`（新增） | 租户按 `Role==super_admin` 隐藏"添加设备"/"批量导入"；`createDevice` 容错 403/409；新增双路径单测（租户 403、dup 409、pending 提示、超管指定 AppCode 落库、跨租户认领被拒） |

**回归测试：** `dotnet build` 0 错误；`DeviceClaimConsolidationTests` 12 项全绿（Guard 去重、CreateDevice 租户 403 / 冲突 409 / 超管放行、GetDiscovered 超管全见·租户自见、Claim 跨租户拒·超管放行至 AdapterUnavailable）。集成测试（`tests/IoTPlatform.IntegrationTests/*`）需 MySQL，本离线环境跳过，建议联调环境补齐双路径联调用例。
