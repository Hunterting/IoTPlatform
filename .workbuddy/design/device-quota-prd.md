# 租户设备数量配额需求 PRD

> 文档状态：待架构设计与用户评估
> 产品经理：许清楚
> 说明：本文为简单 PRD，结论均落到可验收行为；引用的文件行号来自两路只读侦察（已抽样复核）。

## 一、项目信息

- **Language**：中文
- **Programming Language**：前端 Vite + React + MUI + Tailwind CSS；后端 C# / .NET（沿用现有栈，不引入新技术）
- **Project Name**：`device_quota_limit`
- **原始需求复述**：
  > 只能超级管理员创建租户或者客户，限定这个租户或者客户的设备数量；超出这个数量时，在设备列表添加设备要提示"已超出设备数量"，认领设备时也要做限制；超级管理员能修改这个设备数量限制；其他用户不能看见这个字段。

- **本轮已拍板、不再变更的 4 项决策**：
  1. 仅 `super_admin` 可创建租户/客户：移除 `admin`、`customer_admin` 的 `CREATE_CUSTOMERS`，后端控制器加 role 硬校验（复用 `DevicesController.cs:132-143` 写法），不能只做前端藏按钮。
  2. 限额语义：`MaxDeviceCount=0` 表示不限量；`>0` 表示硬上限。上限改小后若存量已超标，不影响存量、只阻止新增。
  3. 超限行为：硬拒绝、不建档；返回 `Code=409` + 文案「已超出设备数量上限（当前 X/上限 Y）」；两个入口弹窗显示红色错误条。
  4. 字段可见性：非超管的 `CustomerDto` 不下发 `MaxDeviceCount`（后端裁剪、防抓包）；前端表单仅在 `isSuperAdmin` 时渲染。

## 二、产品定义

### 产品目标

**一句话**：以租户为粒度为设备建档数量设置硬上限，从"创建租户权限"和"两个建档入口"两端堵住超配额建档，并让超管可配置、其他角色无感。

| # | 可度量目标 | 验收口径 |
|---|---|---|
| G1 | **收权**：创建租户/客户能力仅 `super_admin` 持有 | 非超管直调创建客户 API（绕过前端）一律返回 `Code=403` 且无新客户入库；前端无入口。覆盖率 100% |
| G2 | **零漏守卫**：两个建档入口建档前均执行配额校验 | 已达上限时，直接新增与安圣认领两个入口 100% 不产生 `Device` 行；认领入口在 5~10s 强制探测**之前**拦截 |
| G3 | **配置闭环**：超管可改、他人无感 | 超管改 `MaxDeviceCount` 即时生效；非超管接口响应体无该字段、页面无该控件 |

### 用户故事

**角色 A：超级管理员**

- **US-1**：作为超管，我希望只有我能创建租户/客户并为其设定设备上限，以便资源分配可控。
  - 验收标准：
    - 以 `super_admin` 登录，可见"添加客户"按钮并能成功创建；客户表单含"设备数量上限"输入框（选填，默认 0=不限）。
    - 创建/编辑时填入的上限值正确入库，重新打开表单回显一致。
    - 以 `admin`、`customer_admin` 账号**直接调用**创建客户接口，返回 `Code=403`，数据库无新增客户。
- **US-2**：作为超管，我希望随时调整某租户的设备上限，以便业务灵活变更。
  - 验收标准：可将上限从 100 改为 200 或改为 0（不限）；若把上限改到低于该租户当前设备数，保存**成功**，存量设备不受影响、不删除，仅阻止后续新增。

**角色 B：租户管理员（admin）**

- **US-3**：作为租户管理员，我希望在配额内正常添加/认领设备，日常运营不中断。
  - 验收标准：当前设备数 < 上限（或上限为 0）时，直接新增与认领两个入口均能成功建档。
  - 验收标准：当前设备数已达上限时，两个入口提交均被拒，弹窗出现红色错误条「已超出设备数量上限（当前 X/上限 Y）」，且不产生任何设备档案、待认领记录不置为已认领。
  - 验收标准：账号下**看不到**"设备数量上限"字段——前端不渲染，且抓包查看客户接口响应体中也无 `MaxDeviceCount` 字段。
  - 验收标准：看不到"添加客户"入口，账号无 `CREATE_CUSTOMERS` 权限。

**角色 C：普通用户（如 operator/user、设备操作员）**

- **US-4**：作为普通用户，我希望界面不受配额字段干扰，同时超限行为与管理员一致。
  - 验收标准：设备列表、客户相关页面均不出现配额字段；不具备创建客户权限。
  - 验收标准：若其角色具备添加/认领设备权限，超限时的拦截与提示与租户管理员完全一致（同样的 `Code=409` 与红色错误条）。

## 三、需求池

| ID | 需求 | 优先级 | 验收标准 |
|---|---|---|---|
| R01 | **创建租户/客户收权**：移除 `admin`、`customer_admin` 的 `CREATE_CUSTOMERS` 权限（后端 `PermissionConfig.cs`/`RoleConfig.cs`、前端 `permissions.ts`）；`CustomersController` 创建端点加 `Roles.IsSuperAdmin()` 硬校验（复用 `DevicesController.cs:132-143` 写法） | P0 | 非超管直调创建接口返回 `Code=403` 且无数据入库；超管正常创建；权限清单派生自 `Permissions.All` 不漂移 |
| R02 | **直接新增设备配额守卫**：在 `DeviceService.CreateDeviceAsync` 的 `DeviceRegistrationGuard.CheckAsync`（`:275`）之后、建档 `Devices.Add`（`:495`）之前插入配额校验；超限抛带 `StatusCode=409` 的 `DeviceRegistrationException` | P0 | 达上限（非 0）时新增被拒、无 `Device` 行；上限为 0 时不拦截；存量已超标时新增加仍被拒 |
| R03 | **认领设备配额守卫**：在 `AnShengDiscoveryService` 的 `:626` 之后、`:628`（解析协议配置）之前插入配额校验，即在强制探测之前；超限返回 `Fail(DEVICE_QUOTA_EXCEEDED, 文案)` | P0 | 超限请求不进入探测、不置 `Probing`、不建档、待认领池不置已认领；上限为 0 时放行 |
| R04 | **超限错误码与文案**：新增 `AnShengClaimErrorCodes.DEVICE_QUOTA_EXCEEDED` 并在 `MapClaimFailure` 映射 `Code=409`（仿 `ProbeConflict`）；直接新增经 `DeviceRegistrationException` 映射 `Code=409`；文案统一「已超出设备数量上限（当前 X/上限 Y）」 | P0 | 两入口响应体 `Code=409`，文案含当前值与上限值（X、Y 为真实数字）；HTTP 状态仍为 200 |
| R05 | **两个入口红色错误条**：设备列表添加弹窗、认领弹窗在收到 409 时显示红色错误条并展示后端文案 | P0 | 超限提交后弹窗内出现红色错误条，文案与后端一致，弹窗不关闭；成功时错误条消失 |
| R06 | **超管改限额**：`Customer` 创建/编辑入参与 `CustomerDto` 增加 `MaxDeviceCount`，仅超管可读写 | P0 | 超管可增改该值并回显一致；改小致存量超标时保存成功、存量不动 |
| R07 | **字段隐藏**：后端非超管 `CustomerDto` 裁剪掉 `MaxDeviceCount`；前端表单/表格仅在 `isSuperAdmin` 时渲染 | P0 | 非超管抓包响应体无该字段、页面无该控件；超管可见可改 |
| R08 | **并发原子性**：`count→insert` 必须防并发超卖——认领路径的 count 放进现有事务；直接新增路径用行锁 Customer 行或包进事务 | P0 | 并发 N 个请求在上限剩余 1 时，最多 1 个成功建档，其余返回 409 |
| R09 | **修复认领失败文案丢失**：`AnShengManagementPage.tsx:281-282` 的 catch 改从业务响应体读取文案，不再读 `err.response.data.message` | P1 | 超限及其他业务失败时，认领弹窗显示后端真实文案而非空/"认领失败" |
| R10 | **客户列表显示配额列**：超管在客户列表可见"设备上限"列 | P1 | 超管列表出现该列并显示数值（0 显示为"不限"）；非超管无该列 |
| R11 | `MaxUserCount` / `MaxAreaCount` 一并启用配额校验 | P2 | 本轮不做；字段已存在但同样无校验，留待后续需求 |
| R12 | 配额用量可视化（已用/上限进度条、接近上限预警） | P2 | 本轮不做 |

## 四、关键业务流程

### 4.1 直接新增设备

{{★}} 为配额校验插入点，位于唯一性校验之后、建档之前。

```mermaid
flowchart TD
    A[设备列表点击"添加设备"] --> B[提交建档请求 POST]
    B --> C[DeviceService.CreateDeviceAsync :245]
    C --> D[解析目标租户 TargetAppCode :262-272]
    D --> E[DeviceRegistrationGuard.CheckAsync :275<br/>唯一性/存在性校验]
    E -->|不通过| X[既有异常 → 对应 Code]
    E -->|通过| F{{★ 配额校验插入点<br/>effectiveAppCode = TargetAppCode<br/>count = Devices.Where AppCode==effectiveAppCode .Count}}
    F --> G{MaxDeviceCount==0<br/>或 count < 上限 ?}
    G -->|是| H[建档 Devices.Add :495<br/>count 与 insert 同事务/行锁]
    G -->|否| I[抛 DeviceRegistrationException 409<br/>已超出设备数量上限 当前X/上限Y]
    H --> J[成功 Code=200]
    I --> K[前端添加弹窗红色错误条]
    X --> K
```

### 4.2 安圣认领设备

配额校验必须在步骤 5（解析协议配置）与步骤 7（5~10s 强制探测）**之前**，避免超限请求白占协议与探测资源。

```mermaid
flowchart TD
    A[待认领列表点击"认领"] --> B[POST /ansheng/claim]
    B --> C[AnShengDiscoveryService.ClaimAsync :547]
    C --> D[步骤1-4 品类/标识/待认领/已认领/跨租户/IMEI冲突校验]
    D --> E[DeviceRegistrationGuard.CheckAsync :620]
    E -->|冲突| X[既有错误码 → 对应 Code]
    E -->|通过| F{{★ 配额校验插入点 :626后/:628前<br/>effectiveAppCode = request.AppCode<br/>count 放入步骤11同事务}}
    F --> G{MaxDeviceCount==0<br/>或 count < 上限 ?}
    G -->|否| H[Fail DEVICE_QUOTA_EXCEEDED → Code=409<br/>不探测/不置Probing/不建档<br/>前端认领弹窗红色错误条]
    G -->|是| I[步骤5 解析协议配置 :628]
    I --> J[步骤6 置 Probing :640]
    J --> K[步骤7 强制探测 5-10s :650]
    K --> L[步骤8-10 写档案快照/能力字段/探测失败终止]
    L --> M[步骤11 事务内建 Device :712]
    M --> N[成功 Code=200]
    X --> H2[前端认领弹窗红色错误条]
```

## 五、UI 设计要点

### 5.1 客户管理页（`Web/src/app/pages/CustomersPage.tsx`）

- **表单**（`CustomerFormModal`，`:842-900`）：在现有 6 字段（name/code/contact/phone/address/appCode）基础上，新增"设备数量上限"数字输入框，**仅 `isSuperAdmin`（`user?.role === 'super_admin'`）时渲染**；标注 0=不限。非超管表单完全不出现该字段。
- **表头**（`:416-421`）：仅超管增加"设备上限"列，0 显示为"不限"。
- **添加按钮**（`:370-378`）：门控从 `hasPermission(CREATE_CUSTOMERS)` 收敛为仅超管可见（权限本身也已对 admin/customer_admin 移除，双保险）。
- **角色判据**：`:21-24` 的 `isAdminLike`（含 admin）**不可**用于配额字段显隐，须用真超管判据（前端 `AuthContext.tsx:405-412` 的短路逻辑）。
- **DTO/适配联动**：`customer.types.ts:4-39`、`authAdapter.ts:277-325`（三个适配函数）、`customerApi.ts:49-51` 需补 `MaxDeviceCount` 映射，但仅超管场景填充/提交；非超管即便伪造前端请求，后端也不接受（见 R06/R07 后端裁剪与入参校验）。

### 5.2 两个错误条

- **设备列表添加弹窗**（`DevicesPage.tsx`）：复用 `actionError` 状态（`:65`）与 `getErrorMessage`（`:67-76`）、`handleAddDevice`（`:147-164`）；红色错误条位置沿用 `:639-644`。收到 `Code=409` 时显示「已超出设备数量上限（当前 X/上限 Y）」，红色文字/底色，弹窗保持打开。
- **认领弹窗**（`AnShengManagementPage.tsx`）：复用 `claimResult`（`:111`）、`handleClaimSubmit`（`:241-286`）、结果处理（`:268-282`）、通知条（`:1162-1183`）。失败分支须从业务响应体取文案（见 R09），超限文案同 5.2 上条。

## 六、边界与约束

1. **统计口径坑（必须遵守）**：`AppDbContext.cs:171-204` 的全局租户过滤器在"超管"或"AppCode 为空"时**直接不生效**，裸 `CountAsync()` 会数全表。必须显式 `Where(d => d.AppCode == effectiveAppCode)` 后再 Count；**不要复用** `CustomerRepository.GetCustomerStatsAsync`（它按 `Project.CustomerId` 统计，口径不同）。超管代租户新增/认领时用**目标租户 AppCode**：直接新增取 `DeviceService.cs:262-272` 的 `request.TargetAppCode`；认领取 `AnShengController.cs:240-247` 的 `request.AppCode`。
2. **并发原子性**：`count→insert` 非原子。认领路径 `:692-739` 已在事务内，把 count 放进同一事务；直接新增路径 `:485-497` 无显式事务，需对 Customer 行加锁或包进事务（具体方案由架构师定）。注意 EF Core 启用了 `EnableRetryOnFailure`，用户发起的事务必须包在 `Database.CreateExecutionStrategy()` 内（范式见 `AnShengDiscoveryService.cs:681-686`），否则抛"不支持用户事务"。
3. **HTTP 200 约定**：全站 HTTP 状态恒为 200，业务成败看响应体 `Code`（`Helpers/ApiResponse.cs:7-27`）。业务错误走 HTTP 200 + 非 200 `Code`，让页面自行检查。`httpClient.ts:53-117` 拦截器成功分支不检查 `code`、错误分支 reject 的普通对象无 `response` 字段——因此超限必须靠 `Code=409` 传递，不能依赖 HTTP 状态码。
4. **认领 catch 文案丢失**：`AnShengManagementPage.tsx:281-282` 的 catch 读 `err.response.data.message` 会丢文案（对象无 `response`），列为 R09 顺带修复。
5. **错误码映射**：认领侧新增 `AnShengClaimErrorCodes.DEVICE_QUOTA_EXCEEDED`（常量定义 `Services/Interfaces/IAnShengDiscoveryService.cs:17-42`），在 `MapClaimFailure`（`AnShengController.cs:300-323`）映射 `Code=409`（仿 `ProbeConflict` 的 `:318-319` 直填 `Fail(409, ...)`）；直接新增侧用 `DeviceRegistrationException(StatusCode=409)` 由 `DevicesController.cs:150-166` 映射。
6. **超管判据统一**：后端权威为 JWT `ClaimTypes.Role == "super_admin"`，统一用 `Configuration/RoleConfig.cs:20-23` 的 `Roles.IsSuperAdmin()`；**不可**用 `CustomersController.cs:31-35` 的 `IsAdminLike()`（含 admin）。前端用 `user?.role === 'super_admin'`。
7. **权限配置同步**：`PermissionConfig.cs:131-139` 的 `All` 是权限清单唯一派生源，新增/删除权限必须同步它以避免"清单数量漂移"回归。移除 `admin`/`customer_admin` 的 `CREATE_CUSTOMERS` 需同时改 `RoleConfig.cs:56-63`（admin）与 `:156-160`（customer_admin 复用）及前端 `permissions.ts:344`。
8. **探测资源保护**：认领配额校验必须在 5~10s 强制探测（`:650`）之前，超限请求不得占用协议配置解析与探测资源。
9. **限额语义与迁移**：`MaxDeviceCount=0` 不限、`>0` 硬上限；上限改小致存量超标时不影响存量、只阻止新增。字段已是初始 Migration `20260330025826_Initial.cs:45-47` 的真实 `int NOT NULL` 列，**大概率无需新迁移**（架构师最终确认）。
10. **配置缺失不放行（用户 11-22 追加决策，fail-closed）**：`effectiveAppCode` 找不到对应 Customer 记录时，**不得**按「0=不限」放行。守卫返回 `ConfigMissing`，直接新增返回 `Code=400`「目标租户配额配置缺失，已拒绝建档」，认领返回 `Code=400` + `ErrorCode=DEVICE_QUOTA_CONFIG_MISSING`，两入口均不建档、认领不探测，并记含 AppCode 的 Error 日志。详见架构设计 §3.1 / §8 清单 #13。

## 七、待确认问题

> **本节问题已全部由用户拍板（2026-11-22）**，保留原问题与结论以便追溯。

1. ~~创建客户时 `MaxDeviceCount` 默认值~~ → **已定：默认 0（不限）**；实现采用 `CustomersController.CreateCustomer` 显式赋 0，不改实体初始化器（见设计 §5.1）。
2. ~~`MaxUserCount` / `MaxAreaCount` 是否一并启用~~ → **已定：本轮只做设备数，其余维持 P2**（见设计 §5.6）。
3. ~~超管代租户建档是否计入该租户配额~~ → **已定：计入，用目标租户 AppCode 统计**（见设计 §5.2）。
4. **配置缺失时是否放行**（架构评审提出）→ **已定：不放行（fail-closed）**，见第六章约束 10。
