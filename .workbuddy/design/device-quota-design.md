# 租户设备数量配额 — 增量系统架构设计与任务分解

> 文档状态：**供用户评估，确认后再由工程师实现**（本轮不写任何业务代码）
> 架构师：高见远（Bob）
> 输入：`device-quota-prd.md`（PM 许清楚）＋ 用户拍板 7 项决策 ＋ 主理人 3 条补充裁决 + 两路只读侦察事实
> 工程根：`/h/IoTPlatform`（POSIX）；后端 C#/.NET 8 EF Core（Pomelo MySQL），前端 Vite + React + MUI + Tailwind
> 关联图：`device-quota-class.mermaid`、`device-quota-sequence-direct.mermaid`、`device-quota-sequence-claim.mermaid`

---

## 〇、决策与裁决落地速查（评估重点）

| # | 来源 | 最终结论 | 落地章节 |
|---|---|---|---|
| 1 | 用户决策 | 仅 `super_admin` 可创建客户；后端 `Roles.IsSuperAdmin()` 硬校验 + 前端藏按钮双保险 | §3.5 / §5.4 / T01 |
| 2 | 用户决策 | `MaxDeviceCount=0` 不限、`>0` 硬上限；改小上限不影响存量、只挡新增 | §5.2 / §8 |
| 3 | 用户决策 | 超限硬拒不建档；`Code=409` + 文案「已超出设备数量上限（当前 X/上限 Y）」 | §5.5 / T02/T05 |
| 4 | 用户决策 | 非超管 `CustomerDto` 不下发该字段（后端裁剪防抓包）；前端仅超管渲染 | §5.3 / T04 |
| 5 | 用户决策 | **新建客户默认 0（不限）**：采用「`CustomersController.CreateCustomer` 显式赋 0」——不改实体默认值（详见 §5.1 论证） | §5.1 / T04 |
| 6 | 用户决策 | 本轮只启用设备数配额；`MaxUserCount`/`MaxAreaCount` 维持 P2，不做校验、不做 UI | §5.6 |
| 7 | 用户决策 | 超管代租户建档计入配额，用目标租户 AppCode（直接新增取 `TargetAppCode`，认领取 `request.AppCode`） | §5.2 / §4 |
| 8 | 用户决策（11-22 追加） | **找不到 Customer 配置时不放行（fail-closed）**：守卫返回 `ConfigMissing`，两入口均拒绝建档；不得按「0=不限」静默放行 | §3.1 / §4 / §8 |
| A | 主理人裁决 | R10 合并进 R07，删除 R10 独立条目（客户列表配额列并入 R07 一起验收） | §6 / 需求池映射 |
| B | 主理人裁决 | 非超管伪造写请求携带 `MaxDeviceCount`：**静默忽略该字段、其他字段照常保存**，不返回 403 | §5.3 / §8 |
| C | 主理人裁决 | 认领超限校验在 `AnShengDiscoveryService.cs:626` 后 / `:628` 前（探测之前）；直接新增在 `:275` Guard 后 / `:495` 建档前 | §4 / §5.2 |

---

## 一、实现方案总览

### 1.1 一句话思路

**不新增表、不新增迁移**——复用现有 `MaxDeviceCount` 列与 `DeviceRegistrationGuard`「集中裁决 + 统一异常映射」范式，新增一个**无状态静态守卫 `DeviceQuotaGuard`**，在「直接新增」「安圣认领」两个建档入口的**建档动作之前**插入「按目标租户 AppCode 显式 count + 上限判定」；超限按既有 HTTP 200 约定回 `Code=409` + 统一文案，两个弹窗复用现有 `actionError`/`claimResult` 红条展示；同时从 `Roles` 权限集移除 `admin`/`customer_admin` 的建客权限并在控制器加超管硬校验；`CustomerDto` 的配额字段仅超管下发。

### 1.2 架构原则（强约束）

1. **后端为准、前端为辅**：前端藏按钮/藏字段只是体验，`.CreateCustomer` 与两个建档守卫的服务端校验才是安全边界；前端即便被绕过直调 API 也被后端拦下。
2. **复用既有范式，最小侵入**：
   - 复用 `DeviceRegistrationGuard` 的「静态无状态裁决 + `DeviceRegistrationException(StatusCode)` 由 Controller 映射」范式；
   - 复用 `AnShengClaimErrorCodes` + `MapClaimFailure` 的「错误码 → Code」链路（仿 `ProbeConflict`）；
   - 复用两个弹窗已有的 `actionError`/`claimResult` 红条组件，不引入新 UI 机制。
3. **HTTP 状态恒 200**：业务成败只看响应体 `Code`（`Helpers/ApiResponse.cs:7-27`）；超限走 HTTP 200 + `Code=409`，**绝不**依赖 HTTP 409（前端拦截器 `httpClient.ts:53-117` 成功分支不检查 code，错误分支 reject 的普通对象无 `response` 字段）。
4. **超管判据唯一化**：后端统一用 `Configuration/RoleConfig.cs:20-23` 的 `Roles.IsSuperAdmin()`（JWT `ClaimTypes.Role == "super_admin"`）；**禁止**用 `CustomersController.cs:31-35` 的 `IsAdminLike()`（含 admin）判配额字段显隐。前端统一 `user?.role === 'super_admin'`（`AuthContext.tsx:405-412` 短路逻辑）。
5. **显式租户口径，绝不裸 Count**：`AppDbContext.cs:171-204` 全局过滤器在「超管 / AppCode 为空」时**不生效**，裸 `CountAsync()` 会数全表。任何配额 count 必须 `Where(d => d.AppCode == effectiveAppCode)`（细节 §5.2）。
6. **并发 count→insert 原子**：EF Core 启用 `EnableRetryOnFailure`，用户事务**必须**包在 `Database.CreateExecutionStrategy().ExecuteAsync(...)` 内（范式 `AnShengDiscoveryService.cs:692-755`），否则抛「不支持用户事务」。方案 §5.2.3 / §7.1。
7. **不碰数据库 schema**：`MaxDeviceCount` 已是初始迁移 `20260330025826_Initial.cs:45-47` 的真实 `int NOT NULL` 列，全迁移链与 `AppDbContextModelSnapshot.cs:1366` 均在册（详见 §2.5）。

---

## 二、改动文件清单

> 路径相对 `/h/IoTPlatform`。「新增」= 新文件；其余为就地修改。行号为侦察核对值，实现时以当前内容为准。

### 2.1 后端 — 核心守卫与配额逻辑

| 文件 | 改什么 | 为什么 |
|---|---|---|
| `Services/DeviceQuotaGuard.cs`（新增） | 静态无状态守卫：`QuotaSnapshot ReadAsync(...)`（早检/非事务）、`QuotaSnapshot ReadLockedAsync(...)`（事务内行锁+count）、`string BuildExceededMessage(current, limit)`、`const string MESSAGE_TEMPLATE`。内部**显式** `Where(AppCode)`，并提供行锁钩子 | 把「按租户 count + 上限判定」收敛为单一入口，两个建档入口共用，杜绝口径漂移；静态无状态与 `DeviceRegistrationGuard` 同范式，易单测 |
| `Services/DeviceRegistrationGuard.cs` | 在 `Conflict` 枚举追加 `QuotaExceeded`（可选，仅用于异常分类/日志；不改变既有 3 值行为） | 让超限异常可被测试与日志稳定归类，`.Conflict` 字段才有语义；非必需但推荐 |
| `Services/DeviceService.cs` | 在 `CreateDeviceAsync` 的 `:286`（Guard switch 结束）之后、`:492-497`（`Devices.Add`/`SaveChanges`）之前插入配额校验；把 `:485-497` 建档段包进 `CreateExecutionStrategy().ExecuteAsync` + 事务；超限抛 `DeviceRegistrationException(409, 文案, QuotaExceeded)` | 直接新增入口的配额闸门；count 与 insert 同事务闭并发窗口（R02/R08） |
| `Services/AnShengDiscoveryService.cs` | ① 在 `:626` 后、`:628` 前插入**配额早检**（`ReadAsync`，超限 `Fail(DEVICE_QUOTA_EXCEEDED, 文案)`）；② 在步骤 11 事务 `:694` 内、`db.Devices.Add(device)`（`:712`）之前插入**事务内配额复核**（`ReadLockedAsync`），超限用局部标志位在事务块外转 `Fail(DEVICE_QUOTA_EXCEEDED)`（见 §5.5） | 认领入口闸门；早检保护探测资源（R03/约束8），事务内核对闭合 count→insert 并发窗口（R08） |
| `Services/Interfaces/IAnShengDiscoveryService.cs` | `AnShengClaimErrorCodes` 追加 `public const string DEVICE_QUOTA_EXCEEDED = "DEVICE_QUOTA_EXCEEDED";`（`:17-42` 块末） | 机器可读错误码，前端/测试按码判断而非中文文案 |

### 2.2 后端 — 控制器 / 权限 / DTO

| 文件 | 改什么 | 为什么 |
|---|---|---|
| `Controllers/CustomersController.cs` | ① `CreateCustomer`（`:207-282`）顶部加 `Roles.IsSuperAdmin()` 硬校验（非超管 Fail(403)，仿 `DevicesController.cs:132-143`）；② `CreateCustomerRequest`/`UpdateCustomerRequest` 各加 `int? MaxDeviceCount`；③ 创建时**显式** `MaxDeviceCount = request.MaxDeviceCount ?? 0`（决策 5）；④ 更新时**仅超管**写入该字段，非超管**静默忽略**（裁决 B）；⑤ 所有 `CustomerDto` 投影（`:79`、`:126`、`:178`、`:260`、`:319`）按超管与否填充 `MaxDeviceCount` | 收权（R01）、默认 0（决策 5）、改限额（R06）、字段裁剪与入参静默忽略（R07/裁决 B） |
| `Controllers/DevicesController.cs` | **无需修改** | 既有 `catch (DeviceRegistrationException ex) → Fail(ex.StatusCode, ex.Message)`（`:150-156`）已能把 409 正确映射，直接新增侧零改动 |
| `Controllers/AnShengController.cs` | `MapClaimFailure`（`:300-323`）switch 追加 `AnShengClaimErrorCodes.DEVICE_QUOTA_EXCEEDED → Fail(409, message, payload)`（仿 `ProbeConflict` `:318-319`） | 认领侧 409 映射（R04） |
| `Configuration/RoleConfig.cs` | `GetAdminPermissions()`（`:56-63`）追加 `permissions.Remove(Permissions.CREATE_CUSTOMERS)`；`GetCustomerAdminPermissions()`（`:156-160`）复用之，**无需改** | 收权 R01：`admin`/`customer_admin` 去掉建客权限；超管仍由 `Permissions.All` 持有 |
| `Configuration/PermissionConfig.cs` | **无需修改**（`CREATE_CUSTOMERS` 常量保留、`Permissions.All` 保留该常量） | `All` 是权限清单唯一派生源，删除常量会让超管也失去建客权限并破坏 R01「仅超管可建」；详见 §5.4 |
| `Services/AuthService.cs` | **无需修改**（`BuildCustomerDto` `:310-327` 也构造 `CustomerDto` 用于登录响应 `CurrentCustomer`）；因新字段为 `int?` 且此处不赋值，天然为 `null`，不会泄漏数值 | 登录响应同样是"非超管不下发"链路的一环；字段裁剪靠 nullable 默认值自动覆盖，无需额外改动（QA 抓包 #7 亦覆盖登录响应） |

### 2.3 前端

| 文件 | 改什么 | 为什么 |
|---|---|---|
| `Web/src/app/config/permissions.ts` | `admin.permissions`（`:336` 起，`:344` 为 `CREATE_CUSTOMERS`）移除 `PERMISSIONS.CREATE_CUSTOMERS`；常量 `PERMISSIONS.CREATE_CUSTOMERS`（`:7`）/ 权限清单条目（`:146`）保留 | 前端权限表与后端角色权限集对齐（R01）；保留常量避免引用编译错误 |
| `Web/src/app/services/api/types/customer.types.ts` | `CustomerDto`（`:4-15`）加 `maxDeviceCount?: number`；`CreateCustomerRequest`（`:23-30`）/ `UpdateCustomerRequest`（`:33-39`）加 `maxDeviceCount?: number` | TS 契约与后端字段对齐 |
| `Web/src/app/services/adapters/authAdapter.ts` | `adaptCustomerDtoToCustomer`（`:277`）、`adaptCreateCustomerRequest`（`:297`）、`adaptUpdateCustomerRequest`（`:313`）三处补 `maxDeviceCount` 映射，仅超管场景填充/提交（组件按 `isSuperAdmin` 控制） | DTO 适配联动（PRD §5.1）；非超管字段后端不下发，前端自然无值 |
| `Web/src/app/pages/CustomersPage.tsx` | ① 新增 `const isSuperAdmin = user?.role === 'super_admin'`（勿复用 `:24` 的 `isAdminLike`）；② 添加按钮（`:370-378`）门控收敛为 `isSuperAdmin`（权限已对 admin/customer_admin 移除，双保险）；③ 表头（`:416-421`）仅超管加「设备上限」列（0 显示"不限"）——**R10 合并进此**；④ `CustomerFormModal`（`:842-900`）加 `maxDeviceCount` 数字输入（默认 0、标注 0=不限），仅超管渲染；`initialData`/`defaultForm` 补字段 | R06/R07 前端部分 + 合并 R10；真超管判据（PRD §5.1） |
| `Web/src/app/pages/DevicesPage.tsx` | `handleAddDevice`（`:147-164`）catch 已用 `getErrorMessage` 从 `err.message` 取文案；确认 `actionError`（`:65`）红条（`:639-644`、`:484-489`）能显示后端 `Code=409` 文案即可（通常零改动，仅验证） | R05 直接新增弹窗红条；后端文案经 `DeviceContext.addDevice` 的 `throw new Error(response.data.message)`（`:134`）已能到达 `actionError` |
| `Web/src/app/pages/AnShengManagementPage.tsx` | ① `handleClaimSubmit`（`:241-286`）result 处理（`:268-282`）已读 `response.data.data?.errorMessage \|\| response.data.message`，超限文案可达；② **R09**：catch（`:281-282`）改为从业务响应体取文案，不再读 `err.response.data.message`（拦截器 reject 的是普通对象 `{code,message}`，无 `response`）；③ 确认认领通知条（`:1162-1183`）红条展示 | R05 认领弹窗红条 + R09 顺带修 |

### 2.4 测试

| 文件 | 改什么 | 为什么 |
|---|---|---|
| `tests/IoTPlatform.AnSheng.Tests/DeviceQuotaGuardTests.cs`（新增） | `QuotaSnapshot` 判定（0 不限、>0 边界、存量超标）、`BuildExceededMessage` 文案、显式 `Where(AppCode)` 口径（构造跨租户 Device 验证只数目标租户）、`limit` 改小不影响存量 | 配额组件纯逻辑单测，不依赖真实 MySQL |
| `tests/IoTPlatform.AnSheng.Tests/RoleConfigViewLogsTests.cs`（修改） | 追加断言：`GetAdminPermissions()` 与 `GetCustomerAdminPermissions()` **不含** `CREATE_CUSTOMERS`；`GetSuperAdminPermissions() == Permissions.All` 仍含 | 防权限漂移回归（历史上曾漏 5 个权限） |
| `tests/IoTPlatform.IntegrationTests/Customers/CustomerAdminBoundaryTests.cs`（修改） | 追加：以 admin / customer_admin 直调 `POST /customers` 返回 `Code=403` 且无入库；超管正常建；非超管响应体无 `maxDeviceCount`；非超管伪造 `maxDeviceCount` 写入被静默忽略但其它字段更新成功 | G1/G3、R01/R07、裁决 B 的端到端证据 |
| `tests/IoTPlatform.IntegrationTests/DeviceQuota/DeviceQuotaTests.cs`（新增） | 两入口超限 409 + 文案、0 不限、改小上限存量不动、并发 N 抢 1 台只放 1 个、超管代建按目标租户计数、绕过前端直调 API | R02/R03/R04/R08 + 决策 7 的集成证据（如测试基建不支持真实并发，则列入 QA 手工清单 §8） |

### 2.5 无需新增数据库迁移（结论 + 依据）

**结论：本轮不新增任何 EF Migration、不改列定义、不改实体字段类型。**

依据（已逐项核对）：
1. `Models/Customer.cs:25` 的 `public int MaxDeviceCount { get; set; } = 100;` 是实体属性，`=100` 是 C# 初始化器（仅影响 `new Customer()` 的初值），**不是** DB default 约束；列本身早已存在。
2. `Migrations/20260330025826_Initial.cs:45-47`：`MaxDeviceCount = table.Column<int>(type: "int", nullable: false)` —— 真实 `int NOT NULL` 列，自初始迁移即在。
3. 其后全部增量迁移的 `*.Designer.cs` 与 `AppDbContextModelSnapshot.cs:1366` 均持续在册该列（已 grep 全链），无类型/可空性变更。
4. 本轮所有改动都落在**读写逻辑与 DTO 层**：存 0（决策 5）只是往既有列写值；超管改限额只是 `UpdateCustomer` 多写一个字段。既不需要加列，也不需要改约束。
5. 前端 `maxDeviceCount` 只是 JSON 字段名映射（System.Text.Json 默认 camelCase；`Program.cs:330-337` 仅追加枚举与 DateTime 转换器，未改命名策略）。

> 关于实体默认值 `=100`：见 §5.1——**不改**。改它虽不触发迁移（初始化器非 DB default），但会影响所有 `new Customer()` 路径的初值（种子/测试），属不必要的外溢，故采用控制器显式赋 0。

---

## 三、核心数据结构与接口

完整类图见 `device-quota-class.mermaid`。此处给关键签名与契约。

### 3.1 `DeviceQuotaGuard`（新增，静态无状态）

```csharp
public static class DeviceQuotaGuard
{
    public const string MESSAGE_TEMPLATE = "已超出设备数量上限（当前 {0}/上限 {1}）";

    // 非事务版本：用于认领“早检”（探测之前）；不做 FOR UPDATE
    // 输入：AppDbContext db（作用域内）、effectiveAppCode（目标租户，非空）
    // 输出：快照 current（该租户 devices 数）、limit（该租户 MaxDeviceCount）
    public static async Task<QuotaSnapshot> ReadAsync(
        AppDbContext db, string effectiveAppCode, CancellationToken ct = default);

    // 事务内版本：调用方已在 CreateExecutionStrategy 事务中；
    // 先对 customers 行取锁，再 count，闭合 count→insert 并发窗口
    public static async Task<QuotaSnapshot> ReadLockedAsync(
        AppDbContext db, string effectiveAppCode, CancellationToken ct = default);

    public static string BuildExceededMessage(int current, int limit)
        => string.Format(MESSAGE_TEMPLATE, current, limit);
}

public sealed record QuotaSnapshot(int Current, int Limit, bool ConfigMissing = false)
{
    public bool IsUnlimited => !ConfigMissing && Limit == 0;                    // 0=不限（配置存在时）
    public bool Exceeded => !ConfigMissing && !IsUnlimited && Current >= Limit; // >0 硬上限，含存量已超标
    public bool ShouldReject => ConfigMissing || Exceeded;                      // 不放行的统一判定（决策 8）
}
```

**输入/输出契约**
- 输入：`effectiveAppCode` —— 直接新增取 `request.TargetAppCode`（`DeviceService.cs:263-265`），认领取 `AnShengController.cs:245-247` 的 `effectiveAppCode`（超管请求体 `request.AppCode`；否则 JWT `AppCode`）。**决策 7：超管代建也用目标租户码，不豁免**。
- 输出 `QuotaSnapshot`：供调用方判定 `Exceeded`；文案由 `BuildExceededMessage(current, limit)` 统一生成，两个入口共享同一文案格式。
- 异常/结果类型：守卫本身**不抛业务异常**（纯查询）；结果用 `QuotaSnapshot` 表达三态：`IsUnlimited`（配置存在且 limit=0）、`Exceeded`（配置存在且已达/超限）、`ConfigMissing`（配置缺失）。超限由调用方表达——直接新增抛 `DeviceRegistrationException(409, ...)`，认领返回 `AnShengClaimResult.Fail(DEVICE_QUOTA_EXCEEDED, ...)`。
- **决策 8（fail-closed，覆盖原放行方案）**：`effectiveAppCode` 为空或找不到 Customer 记录时，守卫返回 `ConfigMissing=True`，**不放行**。直接新增抛 `DeviceRegistrationException(400, "目标租户配额配置缺失，已拒绝建档")`；认领早检返回 `Fail(DEVICE_QUOTA_CONFIG_MISSING, "目标租户配额配置缺失，已拒绝认领")`（映射 Code=400，探测前即拒）。记 Error 日志（含 AppCode）便于排查。

**计数实现要点（伪码，工程师按此落地）**
```csharp
// 关键：显式 Where，绕开全局过滤器“超管/空 AppCode 不生效”的坑（约束 1）
var customer = await db.Customers.AsNoTracking()
    .Where(c => c.AppCode == effectiveAppCode)
    .Select(c => new { c.MaxDeviceCount })
    .FirstOrDefaultAsync(ct);
if (customer is null)
    return new QuotaSnapshot(0, 0, ConfigMissing: true);   // 决策 8：不放行

var current = await db.Devices.AsNoTracking()
    .CountAsync(d => d.AppCode == effectiveAppCode, ct);  // 绝不可裸 CountAsync()

return new QuotaSnapshot(current, customer.MaxDeviceCount);
```
> 不复用 `CustomerRepository.GetCustomerStatsAsync`：它按 `Project.CustomerId` 统计，口径不同（约束 1）。

### 3.2 `CustomerDto` 字段与裁剪策略

```csharp
public class CustomerDto
{
    // ...既有字段（Id/Name/Code/AppCode/Contact/Phone/Address/Status/CreatedAt/DeviceCount/ProjectCount）
    public int? MaxDeviceCount { get; set; }   // 新增：nullable 是裁剪关键
}
```

**裁剪策略（结论：Controller 投影时按超管手工赋值；用 nullable 保证非超管"字段值为 null"）**

| 场景 | 做法 | 抓包结果 |
|---|---|---|
| 超管（`Roles.IsSuperAdmin(role)`） | 投影中 `MaxDeviceCount = c.MaxDeviceCount` | 响应体含 `maxDeviceCount: N` |
| 非超管（admin/customer_admin/普通用户） | 投影中**不赋值**（保持 `int?` 默认 `null`） | 响应体 `maxDeviceCount: null` 或经 JSON 选项省略——**无真实数值**，满足"不下发"语义 |

> 若 QA 要求"字段名完全不出现"，见 §9.2 第 1 条（需全局 JSON 选项，属口径确认）。本设计以"非超管拿不到数值"为最小侵入验收口径。

**入参处理（裁决 B）**
- `CreateCustomerRequest.MaxDeviceCount`、`UpdateCustomerRequest.MaxDeviceCount` 均为 `int?`。
- `CreateCustomer`：仅超管可达（决策 1 硬校验）；`MaxDeviceCount = request.MaxDeviceCount ?? 0`（决策 5 默认 0）。
- `UpdateCustomer`：超管 `if (request.MaxDeviceCount.HasValue) customer.MaxDeviceCount = request.MaxDeviceCount.Value;`；**非超管即使伪造该字段也静默忽略**，其余字段（名称/联系人/电话/地址/状态）照常保存，**不返回 403**（裁决 B）。

### 3.3 新增错误码 `DEVICE_QUOTA_EXCEEDED` 定义位置

- 定义：`Services/Interfaces/IAnShengDiscoveryService.cs` 的 `AnShengClaimErrorCodes` 静态类（`:17-42`），追加常量 `DEVICE_QUOTA_EXCEEDED = "DEVICE_QUOTA_EXCEEDED"` 与 `DEVICE_QUOTA_CONFIG_MISSING = "DEVICE_QUOTA_CONFIG_MISSING"`。
- 映射：`Controllers/AnShengController.cs` 的 `MapClaimFailure`（`:300-323`）追加两个 case：超限 → `Fail(409, message, payload)`；配置缺失 → `Fail(400, message, payload)`。
- 直接新增侧**不需要**新错误码：复用 `DeviceRegistrationException(StatusCode)`，由 `DevicesController.cs:150-156` 映射——超限 409、配置缺失 400。

### 3.4 权限清单 `Permissions.All` 的同步方式

- `Permissions.All`（`PermissionConfig.cs:131-230`）是超管全量权限的**唯一派生源**，`Roles.GetSuperAdminPermissions()` 直接 `return Permissions.All`（`RoleConfig.cs:48-51`）。
- 收权**只做减法、在角色函数做**：`GetAdminPermissions()` 内 `permissions.Remove(Permissions.CREATE_CUSTOMERS)`；`GetCustomerAdminPermissions()` 复用 `GetAdminPermissions()`（`:156-160`）。
- **不动** `Permissions.All` 与 `CREATE_CUSTOMERS` 常量本身——超管仍持有建客权限（R01「仅超管可建」的前提）。`All` 不变即不会触发"清单数量漂移"回归（历史教训见 `RoleConfig.cs:44-47` 注释）。
- 前端 `permissions.ts` 同步移除 admin 条目，但保留 `PERMISSIONS.CREATE_CUSTOMERS` 常量（超管有该权限，`hasPermission` 对超管短路放行）。

---

## 四、程序调用流（两个时序图）

完整 Mermaid 见 `device-quota-sequence-direct.mermaid` 与 `device-quota-sequence-claim.mermaid`。此处摘要关键插入点与原子性边界。

### 4.1 直接新增设备（成功 / 超限两分支）

```
DevicesPage.handleAddDevice
  → DevicesController.CreateDevice（超管硬校验 :132-143，非超管 403）
  → DeviceService.CreateDeviceAsync
      → 解析 effectiveAppCode = TargetAppCode（:263-265）        〔决策7：目标租户〕
      → DeviceRegistrationGuard.CheckAsync + ApplyOnlinePresenceGate（:275-286） 〔既有闸门〕
      → ★ 插入点 A（:286 之后 / :495 之前）：
          CreateExecutionStrategy().ExecuteAsync(…) {
            BeginTransaction();
            QuotaSnapshot s = DeviceQuotaGuard.ReadLockedAsync(db, effectiveAppCode);
            if (s.ConfigMissing) { Rollback(); throw DeviceRegistrationException(400, "目标租户配额配置缺失，已拒绝建档"); } 〔决策8〕
            if (s.Exceeded) { Rollback(); throw DeviceRegistrationException(409, 文案, QuotaExceeded); }
            Devices.Add(newDevice); SaveChangesAsync();   // :495-497，事务内
            Commit();
          }
      → DevicesController catch(DeviceRegistrationException) → Fail(ex.StatusCode, ex.Message) 〔既有映射，400/409 均可〕
      → HTTP 200 + Code=409 → 前端 actionError 红条，弹窗保持打开
```
- **原子性边界**：`ReadLockedAsync`（含行锁 count）与 `Devices.Add + SaveChanges` 在**同一事务**内，`count→insert` 无窗口（R08）。
- 超限分支：Rollback 释放锁、不建档、无 Device 行。

### 4.2 安圣认领设备（早检 + 事务内复核，两分支）

```
AnShengController.Claim（:240-247 解析 effectiveAppCode，超管取 request.AppCode）〔决策7〕
  → AnShengDiscoveryService.ClaimAsync
      → 步骤1-4 品类/标识/待认领/已认领/跨租户/IMEI 冲突（:557-626）
      → ★ 插入点 B（:626 后 / :628 前，探测之前）：
          QuotaSnapshot s = DeviceQuotaGuard.ReadAsync(db, appCode);          〔早检，无锁〕
          if (s.ConfigMissing) return Fail(DEVICE_QUOTA_CONFIG_MISSING, "目标租户配额配置缺失，已拒绝认领"); 〔决策8，不探测〕
          if (s.Exceeded) return Fail(DEVICE_QUOTA_EXCEEDED, 文案);            〔不探测/不置Probing/不建档〕
      → 步骤5 解析协议（:628） → 步骤6 置 Probing（:640） → 步骤7 探测 5~10s（:650，事务外）
      → 步骤8-10 档案/能力字段；探测失败即返回（不建 Device）
      → 步骤11 事务（:692-739，包在 CreateExecutionStrategy 内）：
          ★ 插入点 C（:694 后 / :712 之前）：
            QuotaSnapshot s2 = DeviceQuotaGuard.ReadLockedAsync(db, appCode); 〔复核，行锁〕
            if (s2.ShouldReject) { 置 failCode = s2.ConfigMissing ? DEVICE_QUOTA_CONFIG_MISSING : DEVICE_QUOTA_EXCEEDED; } 〔事务块外转 Fail(failCode)〕
          Devices.Add + 回写待认领池 + 挂档案 + 自动上报配置 → Commit（:739）
  → MapClaimFailure：DEVICE_QUOTA_EXCEEDED → Fail(409,...)；DEVICE_QUOTA_CONFIG_MISSING → Fail(400,...)
  → HTTP 200 + Code=409 → catch 读业务文案（R09）→ 认领弹窗红条
```
- **为什么要两次校验**：早检（B）保护探测资源、满足"探测之前拦截"（约束 8）；事务内核对（C）闭合 `count→insert` 并发窗口（R08）。探测耗时 5~10s 期间并发可能占满配额，故 C 不可省。
- **原子性边界**：C 与 `Devices.Add` 在步骤 11 同一事务（已符合 `CreateExecutionStrategy` 范式，`AnShengDiscoveryService.cs:681-686` 注释），无需新建事务。

### 4.3 超限文案链路（统一）

后端 `BuildExceededMessage(current, limit)` →（直接新增）`DeviceRegistrationException.Message` → `DevicesController` `Fail(409, ex.Message)` /（认领）`AnShengClaimResult.Fail(..., message)` → `MapClaimFailure` `Fail(409, message, payload)` → HTTP 200 响应体 `Message`（及认领侧 `Data.ErrorMessage`）→ 前端 `getErrorMessage(err)` / `response.data.data?.errorMessage || response.data.message` → 红色错误条。**X、Y 为真实数字**（当前 count、上限 limit）。

---

## 五、关键决策详述（结论 + 理由 + 备选为何不选）

### 5.1 决策 5：新建客户 `MaxDeviceCount` 默认值 —— 采用「控制器显式赋 0」

**结论**：`CustomersController.CreateCustomer` 中 `MaxDeviceCount = request.MaxDeviceCount ?? 0`；**不改** `Models/Customer.cs:25` 的实体初始化器 `= 100`。

**理由**：
1. 实体初始化器 `=100` 只影响 `new Customer()` 的 C# 初值；生产建档唯一路径是 `CreateCustomer`（`new Models.Customer` 在 `CustomersController.cs:246`），显式赋 0 即可精确控制本需求。
2. 改实体默认值会外溢到测试与种子：`tests/.../SeedData.cs:61`、`AuthServiceTenantContextTests.cs:433`、`LoginLogAppCodePersistenceTests.cs:151`、`CustomerAdminBoundaryTests.cs:539` 等 `new Customer()` 路径的初值都会变，属无谓回归面。
3. 种子数据 `SeedCustomers.cs:55/75/95` 的 1000/500/200 是显式赋值，本就不受初始化器影响——保持不动（符合决策 5「种子不动」）。
4. 显式赋 0 与 PRD US-1「默认 0=不限」表述一致，落在一处、可审可测。

**备选为何不选**：改实体默认值 `= 0`——虽同样不触发迁移（初始化器非 DB default），但影响面更大（所有 new 路径 + 测试基线），且语义上"实体默认"应留作兜底而非承载业务决策。

### 5.2 统计口径与目标租户

**结论**：显式 `Where(d => d.AppCode == effectiveAppCode)` 后 `CountAsync()`；不复用任何全局过滤器 / `GetCustomerStatsAsync`。目标租户：直接新增 `request.TargetAppCode`（`DeviceService.cs:263-265`），认领 `AnShengController.cs:245-247` 的 `effectiveAppCode`。**超管代建计入配额**（决策 7）。

**理由**：
- `AppDbContext.cs:171-204`：超管或 AppCode 为空时**根本不注册**查询过滤器，裸 `CountAsync()` 数全表——已侦察确认的坑（约束 1）。显式 Where 是唯一可靠口径。
- `CustomerRepository.GetCustomerStatsAsync` 按 `Project.CustomerId` 归集，与"设备按 AppCode 归属"口径不同，复用会错数（约束 1）。
- 超管 token 通常不带 AppCode claim，若用 JWT 归属会数错；用目标租户码才能让"代租户建档计入该租户配额"（决策 7）。

**备选为何不选**：
- 全局过滤器：超管场景失效，且过滤器在 DbContext 构造时冻结（`_tenantContextAccessor.Current.AppCode`），超管代为该租户时上下文未必匹配。
- `GetCustomerStatsAsync`：口径不符（按 Project）。

### 5.3 非超管字段裁剪落点 —— 采用「Controller 手工不赋值 + nullable DTO」

**结论**：`CustomerDto.MaxDeviceCount` 声明为 `int?`；所有投影处仅超管赋值，非超管保持 `null`。入参（裁决 B）：非超管携带该字段**静默忽略**，其他字段照常保存，不返回 403。

**理由**：
- 改动最小：只碰 DTO 定义 + 5 处投影，不动序列化全局配置。
- 与现有 `CustomersController` 手写投影风格一致（`:79/126/178/260/319`），无新抽象。
- nullable 让"非超管拿不到数值"有明确表达；配合 QA 抓包（§8 清单 #7）可证伪。

**备选为何不选**：
- **独立 DTO（SuperAdminCustomerDto / CustomerDto）**：要新增类型并在 `GetCustomers`/`GetAccessibleCustomers`/`GetCustomer`/`Create`/`Update` 五个返回点分流，还要改 `AuthService.BuildCustomerDto`（`:310-327`，登录响应也返回 CustomerDto）——5+ 文件扩散，超出最小侵入。
- **自定义 JsonConverter / 全局 `WhenWritingNull`**：`Program.cs:330-337` 的 JsonOptions 是全站级，改 `DefaultIgnoreCondition` 会影响所有 DTO 的 null 字段（contact/phone/address 等），回归面不可控；单字段 converter 又要处理"按角色动态裁剪"，复杂度远超收益。

### 5.4 权限收权 —— 采用「从 Roles 权限集移除」，不动常量与 `All`

**结论**：`GetAdminPermissions()` 追加 `permissions.Remove(Permissions.CREATE_CUSTOMERS)`；`customer_admin` 复用之；`Permissions.All` 与常量保留；`CustomersController.CreateCustomer` 加 `Roles.IsSuperAdmin()` 硬校验。

**理由**：
- `Permissions.All` 是超管全量的唯一派生源（`RoleConfig.cs:48-51`）。**删常量 = 超管也失去建客权限**，直接违背 R01「仅超管可建」。
- 从角色函数移除是"精准减法"：admin/customer_admin 丢权限，超管不受影响，`All` 不变 → 不触发清单数量漂移回归。
- 后端 Controller 硬校验（仿 `DevicesController.cs:132-143`）保证绕过前端直调也被拒（G1）。

**备选为何不选**：删 `CREATE_CUSTOMERS` 常量——破坏 `All` 派生源与超管建客能力，且前端 `permissions.ts` 仍引用该常量会编译失败。

### 5.5 错误码与文案链路 —— HTTP 200 + Code=409，两弹窗复用现有红条

**结论**：
- 直接新增：`DeviceRegistrationException(StatusCode=409)` → `DevicesController.cs:150-156` `Fail(409, ex.Message)`。
- 认领：`AnShengClaimResult.Fail(DEVICE_QUOTA_EXCEEDED, 文案)` → `MapClaimFailure` 追加 case → `Fail(409, message, payload)`（仿 `ProbeConflict` `:318-319`）。
- 认领步骤 11 事务内复核的处理：**用局部标志位**，避免被既有 catch 统一映射成 `PersistFailed`(500)。做法：在事务内判定 `s2.ShouldReject` 时置 `quotaFailCode = s2.ConfigMissing ? DEVICE_QUOTA_CONFIG_MISSING : DEVICE_QUOTA_EXCEEDED` 并停止后续写入（可提前中断事务体），事务块外 `return AnShengClaimResult.Fail(quotaFailCode, 文案)`；对外仍是 400（配置缺失）/ 409（超限）而非 500。
- R09 顺带修：认领 catch（`:281-282`）改从 `err?.message`（拦截器 reject 的普通对象）或 `response.data` 取文案，不再读 `err.response.data.message`。

**理由**：全站 HTTP 200 约定；两个弹窗已有红条组件，复用即可满足 R05，零新 UI。

**备选为何不选**：直接返回 HTTP 409——前端拦截器走错误分支 reject 普通对象（无 `response`），反而拿不到文案；且破坏全站约定。

### 5.6 范围裁剪（决策 6）

**结论**：`MaxUserCount`/`MaxAreaCount` 维持 P2——不新增校验、不新增 UI、不写入配额组件。R12（用量可视化）本轮不做。R10 合并进 R07（裁决 A），需求池不再保留 R10 独立条目。

---

## 六、任务列表（有序，按依赖排列）

> 任务粒度按 team-lead 建议编排（本项目 8 项）。**我是架构师，仅产出此清单供评估；不执行实现。**
> 依赖关系：T02 可并行于 T01（均只依赖 PRD）；T03 依赖 T02；T04 依赖 T01；T05 依赖 T04；T06 依赖 T02/T03；T07 依赖 T05/T06；T08 依赖 T07。

| ID | 任务名 | 文件范围 | 验收标准 | 依赖 |
|---|---|---|---|---|
| **T01** | 后端权限收权（超管建客双保险） | `Configuration/RoleConfig.cs`、`Controllers/CustomersController.cs`（仅 CreateCustomer 硬校验）、`tests/.../RoleConfigViewLogsTests.cs`、`Web/src/app/config/permissions.ts` | admin/customer_admin 权限集不含 `CREATE_CUSTOMERS`，超管仍含；非超管直调 `POST /customers` 返回 `Code=403` 且无入库；前端 admin 无建客入口；`Permissions.All` 数量不漂移（单测通过） | — |
| **T02** | 配额组件 + 单测 | `Services/DeviceQuotaGuard.cs`（新增）、`Services/DeviceRegistrationGuard.cs`（可选枚举）、`Services/Interfaces/IAnShengDiscoveryService.cs`（错误码）、`tests/.../DeviceQuotaGuardTests.cs`（新增） | `QuotaSnapshot` 判定正确（0 不限 / >0 边界 / 存量超标）；文案含真实 X/Y；显式 `Where(AppCode)` 只数目标租户（跨租户样本验证）；`DEVICE_QUOTA_EXCEEDED` 常量就位；build + 单测通过 | — |
| **T03** | 两个入口守卫接线 | `Services/DeviceService.cs`、`Services/AnShengDiscoveryService.cs`、`Controllers/AnShengController.cs`（MapClaimFailure） | 直接新增在 `:286` 后 / `:495` 前插入守卫且 count+insert 同事务；认领在 `:626` 后 / `:628` 前早检、步骤11事务内复核；两入口超限均 409 + 文案；认领超限不触发探测/不置 Probing/待认领池不变 | T02 |
| **T04** | DTO 裁剪与改限额（含默认 0、裁决 B） | `Controllers/CustomersController.cs`（DTO/请求/投影全量）、`Web/.../types/customer.types.ts`、`Web/.../adapters/authAdapter.ts`、`Web/.../pages/CustomersPage.tsx` | 超管可增改并回显 `MaxDeviceCount`；新建默认 0；改小上限保存成功且存量不动；非超管响应体无数值、页面无控件；非超管伪造字段静默忽略、其它字段照存（不 403） | T01 |
| **T05** | 前端两个错误条（含 R09） | `Web/.../pages/DevicesPage.tsx`、`Web/.../pages/AnShengManagementPage.tsx` | 两弹窗收到 409 显示红条、文案与后端一致、弹窗不关；成功时红条消失；认领 catch 不再读 `err.response.data.message`；前端 build 通过 | T04 |
| **T06** | 集成测试补全 | `tests/.../Customers/CustomerAdminBoundaryTests.cs`、`tests/.../DeviceQuota/DeviceQuotaTests.cs`（新增） | 403/409/0 不限/存量不动/并发 N 抢 1/超管代建计数/伪造字段忽略 均有断言；测试全绿 | T02, T03 |
| **T07** | 全量验证（QA 按 §8 清单） | 无代码改动（验证产物记入 `.qa-logs/`） | §8 验证清单逐条实证，三身份、两入口、并发、抓包全部通过；无 944 回归 | T05, T06 |
| **T08** | 文档与收口 | `.workbuddy/design/device-quota-design.md`、两份时序图、`docs/` 运行说明（如需） | 设计文档与实际实现一致；错误码/权限/默认值变更写入操作手册（如需）；无遗留 TODO | T07 |

### 6.1 需求池 → 任务 / 文件 映射（避免重复验收）

| 需求 | 归属任务 | 备注 |
|---|---|---|
| R01 建客收权 | T01 | — |
| R02 直接新增守卫 | T03 | — |
| R03 认领守卫（探测前） | T03 | — |
| R04 错误码 + 409 文案 | T02（码）+ T03（映射）+ T05（展示） | — |
| R05 两弹窗红条 | T05 | — |
| R06 超管改限额 | T04 | — |
| R07 字段隐藏（含 **R10 客户列表配额列**） | T04 | **裁决 A：R10 合并进 R07，不再独立验收** |
| R08 并发原子性 | T03（实现）+ T06/T07（验证） | — |
| R09 认领 catch 文案 | T05 | P1 顺带修 |
| R11/R12 | 不做 | 决策 6（P2） |

---

## 七、依赖包与共享知识

### 7.1 依赖包

**无需新增任何 NuGet / npm 包。** 全部复用既有栈：
- 后端：EF Core / Pomelo MySQL、`Microsoft.AspNetCore.Authorization`（既有）、`DeviceRegistrationException`/`AnShengClaimResult`（既有）。
- 前端：React / MUI / Tailwind（既有）。
- 行锁用 EF Core 原生事务 API（`CreateExecutionStrategy` + `BeginTransactionAsync`），无需引入 Dapper 或第三方锁库。

> 若实现时为 `FOR UPDATE` 选择原始 SQL，可用 EF Core 既有 `FromSqlRaw`/`ExecuteSqlRaw`（项目已使用），仍无需新包。

**并发原子性 C# 写法要点（补充给工程师）**
```csharp
// 直接新增：整套包在重试执行策略内（必须，否则 EnableRetryOnFailure 下抛"不支持用户事务"）
await _dbContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
{
    await using var tx = await _dbContext.Database.BeginTransactionAsync(ct);
    try
    {
        // MySQL 行锁：对目标租户 customer 行加排他锁，串行化同租户并发 count→insert
        // EF Core 无 WITH(UPDLOCK)（那是 SQL Server 语法）；MySQL 等价为 SELECT ... FOR UPDATE
        var locked = await _dbContext.Customers
            .FromSqlRaw("SELECT * FROM customers WHERE AppCode = {0} FOR UPDATE", effectiveAppCode)
            .AsTracking()
            .FirstOrDefaultAsync(ct);
        var limit = locked?.MaxDeviceCount ?? 0;
        var current = await _dbContext.Devices.CountAsync(d => d.AppCode == effectiveAppCode, ct);
        if (limit > 0 && current >= limit)
            throw new DeviceRegistrationException(409, DeviceQuotaGuard.BuildExceededMessage(current, limit),
                DeviceRegistrationGuard.Conflict.QuotaExceeded);
        // ... 建档 newDevice ...
        await _dbContext.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
    catch { await tx.RollbackAsync(ct); throw; }
});
```
- 认领路径：步骤 11 已有同款策略包裹（`:692-755`），把同样的 `FOR UPDATE + count` 放进该事务即可，不要另起事务。
- 行锁粒度：**只锁目标租户的 customer 行**，不同租户互不阻塞；锁在 `Commit`/`Rollback` 时释放。
- 备选：若担心 `FromSqlRaw` 与全局过滤器叠加，可让 `ReadLockedAsync` 两个查询显式 `AsNoTracking()` + 手动 WHERE（仍显式 AppCode），靠事务隔离级别兜底——但 MySQL `FOR UPDATE` 已足够，优先方案。
- 注意：`DeviceService.CreateDeviceAsync` 在 `:488` 有 `_dbContext.ChangeTracker.Clear()`，行锁查询（`AsTracking`）要放在此 Clear **之后**，避免被清空导致锁定实体脱离跟踪。

### 7.2 跨文件共享约定（工程师必读）

1. **AppCode 取值规则**：配额 count 的目标租户码——直接新增 = `CreateDeviceRequest.TargetAppCode`（`DeviceService.cs:263-265`，超管必填）；认领 = `AnShengController.cs:245-247` 的 `effectiveAppCode`（超管取请求体 `AppCode`，非超管取 JWT）；两者都**不得**用调用者自身 JWT AppCode 代替目标租户（决策 7）。
2. **409 文案格式（全站统一）**：`已超出设备数量上限（当前 {X}/上限 {Y}）`，X=`QuotaSnapshot.Current`（真实 count），Y=`QuotaSnapshot.Limit`（真实上限）。由 `DeviceQuotaGuard.BuildExceededMessage` 单一出口生成，两入口不得自行拼串。
3. **超管判据统一**：后端 `Roles.IsSuperAdmin(role)`（`RoleConfig.cs:20-23`）；前端 `user?.role === 'super_admin'`。**禁止**用 `CustomersController.IsAdminLike()` 判配额字段。
4. **HTTP 200 约定**：所有业务响应 HTTP 200，`Code` 语义 200/400/403/404/409/500；前端按 `Code` 分支，不按 HTTP status。
5. **错误码**：认领失败码集中在 `AnShengClaimErrorCodes`（`:17-42`），断言用 `ErrorCode` 不用 `Message`（该文件注释约定）。
6. **字段裁剪语义**：非超管 `CustomerDto.MaxDeviceCount == null` / 响应体无有效数值；超管为 int 数值。写入入参为 `int?`。
7. **权限清单派生**：任何权限增删先想 `Permissions.All`——它是超管全量的唯一源；角色级"减法"在 `GetXxxPermissions()` 内做。
8. **测试约定**：断言 `ErrorCode`/`Code`，不断言具体中文文案（除专门文案测试）。

---

## 八、验证方案（QA 在"功能开启"状态证明正确性）

> 前置：以超管把某测试租户 `MaxDeviceCount` 设为可控小值（如 1 或 2）；准备三身份账号：`super_admin`、`admin`（租户管理员）、普通用户（如 operator/user）。所有请求 HTTP 状态应为 200，看响应体 `Code`。

### 8.1 可执行验证清单

| # | 场景 | 操作 / API 请求 | 期望响应与副作用 |
|---|---|---|---|
| 1 | 收权-非超管建客 | `admin`/`customer_admin` token `POST /api/v1/customers`（带合法 body）绕过前端 | `Code=403`，DB 无新增 customer；超管同请求 `Code=200` |
| 2 | 配额-直接新增超限 | 设 `MaxDeviceCount=1` 且该租户已有 1 台；超管 `POST /devices`（`TargetAppCode=该租户`，唯一 IMEI） | `Code=409`，`Message`=「已超出设备数量上限（当前 1/上限 1）」，无 Device 行 |
| 3 | 配额-认领超限（探测前） | 同租户满额；对待认领设备 `POST /api/v1/ansheng/claim` | `Code=409` + `ErrorCode=DEVICE_QUOTA_EXCEEDED` + 文案；**待认领记录仍 NotProbed/未置已认领**、无 Device 行、无探测日志（证明早检在 :628 前） |
| 4 | 0=不限 | 把上限改为 0；对已有 N 台的租户连续新增/认领 | 全部成功 `Code=200`，不因数量拦截 |
| 5 | 改小上限存量不动 | 租户已有 3 台；超管把上限改为 1 | 保存 `Code=200`；3 台存量仍在；第 4 台新增/认领被 409（当前 3/上限 1） |
| 6 | 并发只放 1 个 | 上限剩 1；N（如 10）个并发 `POST /devices`（不同 IMEI）与/或认领同租户不同设备 | 恰好 1 个 `Code=200`，其余 `Code=409`；DB 该租户设备数 = 上限，无超卖 |
| 7 | 字段裁剪抓包 | 分别用超管、admin、普通用户调 `GET /customers` / `GET /customers/{id}` / 登录响应 CurrentCustomer | 超管响应含有效 `maxDeviceCount`；非超管三身份响应体**无该字段数值**（null 或缺失），页面无"设备上限"控件与列 |
| 8 | 伪造字段静默忽略（裁决 B） | 非超管 `PUT /customers/{id}`：body 含合法 `name/contact` + 伪造 `maxDeviceCount=999` | `Code=200`；`name/contact` 已更新；DB 的 `MaxDeviceCount` **未被改成 999**（保持原值）；不返回 403 |
| 9 | 超管改限额回显 | 超管把上限 100→200→0；重开表单/客户列表 | 回显一致；列表超管可见"设备上限"列，0 显示"不限"（R10 合并进 R07） |
| 10 | 两弹窗红条 | 触发 #2、#3 的超限 | 添加弹窗/认领弹窗出现红色错误条，文案与响应体一致，弹窗保持打开；成功后红条消失 |
| 11 | 绕过前端直调 | 用 curl/Postman 直接打两个建档 API 超限 | 与 UI 行为一致（409 + 文案），证明后端为准 |
| 12 | 回归 | 944 全量回归脚本 + 三身份菜单/权限复验 | 无新增回归；非超管菜单无"添加客户" |
| 13 | **配置缺失不放行（决策 8）** | 造一个无 Customer 行的 `AppCode`（如 `orphan_tenant`）：①超管 `POST /devices` 带 `TargetAppCode=orphan_tenant`；②`POST /ansheng/claim` 带 `AppCode=orphan_tenant` | ① `Code=400`「目标租户配额配置缺失，已拒绝建档」；② `Code=400` + `ErrorCode=DEVICE_QUOTA_CONFIG_MISSING`；两入口均无 Device 行、认领无探测日志；日志有含 AppCode 的 Error |

### 8.2 并发测试要点（#6）

- 用并发工具（如 `ab`/JMeter/自写脚本）同时发 N 个请求，确保 IMEI 各不相同（避开去重闸门干扰，纯测配额）。
- 判定"恰好 1 个成功"以 DB 最终 `COUNT(*)` 为准，而非只看响应——防止"响应 409 但实际入库"的假阴性。
- 分别覆盖两个入口，以及"直接新增 + 认领混合并发同租户"的最严场景。
- 关注日志有无 `MySqlRetryingExecutionStrategy does not support user-initiated transactions`——出现即说明事务未包在 `CreateExecutionStrategy` 内（违反约束 2）。

---

## 九、风险与待明确事项

### 9.1 真实风险

| 风险 | 说明 | 缓解 |
|---|---|---|
| 并发窗口（R08） | `count→insert` 非原子；缺行锁时 N 并发会超卖 | 两入口均 `FOR UPDATE` 行锁 + 同事务（§7.1）；并发测试 #6 实证 |
| 全局过滤器冻结缺陷 | 过滤器在 DbContext 构造时按 `_tenantContextAccessor.Current` 冻结，超管/空 AppCode 不生效 | 守卫内**显式 `Where(AppCode)`**，不信任过滤器（§5.2） |
| 权限漂移 | 改权限易漏 `Permissions.All` 或前端清单，历史上曾漏 5 个权限 | 只做角色级减法、`All` 不动；单测断言三角色权限集（T01）+ 前端清单同步 |
| 认领事务内超限/配置缺失被误映射为 500 | 步骤11 现有 catch 统一转 `PersistFailed`(500)，若在事务内直接抛会盖掉 400/409 | 采用局部标志位，事务块外转 `Fail(quotaFailCode)`（§5.5） |
| 认领探测期间配额被占满 | 早检通过后 5~10s 探测期间并发占满配额 | 步骤11 事务内**复核**（插入点 C），不可省（§4.2） |
| 字段裁剪误伤其它 DTO | 若改全局 JSON 忽略条件会波及 contact/phone 等 null 字段 | 采用 nullable + Controller 不赋值，不动全局 JsonOptions（§5.3） |
| 前端 R09 catch 兼容 | 拦截器 reject 普通对象无 `response`，旧 catch 丢文案 | T05 改从 `err.message`/业务响应体取（§5.5） |
| 缺失 Customer 配置 | `effectiveAppCode` 无对应 customer 行 | **决策 8：fail-closed**——守卫返回 `ConfigMissing`，两入口均拒绝（新增 400 / 认领 400 + `DEVICE_QUOTA_CONFIG_MISSING`），不探测、不入库；Error 日志含 AppCode 便于排查（§3.1 / §8 #13） |

### 9.2 待明确事项（尽量为空）

1. **非超管响应体的"下游"字段形态**：本设计采用"非超管 `maxDeviceCount=null`（或 JSON 省略）"。若 QA 要求"字段名完全不出现"，需全局配 `DefaultIgnoreCondition=WhenWritingNull`（会波及其它 DTO 的 null 字段）——**建议以"无有效数值 + 页面无控件"为验收口径**（§8 清单 #7），除非用户坚持字段名级删除。
2. **客户列表"设备上限"列（原 R10）**：已按裁决 A 合并进 R07，仅在超管客户列表显示。若用户希望普通管理员也看到"用数/上限"做进度提示，需另开需求（R12 用量可视化，本轮不做）。

> 除上述两点（均为口径确认，非阻塞）外，无其它需用户拍板的问题。7 项用户决策 + 3 条主理人裁决均已落地。

---

## 附：与既有权威文档的一致性声明

- 本文所有行号/文件名均来自对 `/h/IoTPlatform` 实际源码的只读核对（未修改任何业务代码、未执行 git 写操作、未 `dotnet run`、未启停服务）。
- 本文遵循 PRD 全部 7 章事实与两张流程图；用户 7 项决策与主理人 3 条裁决覆盖 PRD 第七章待确认项。
- 设计阶段**不落任何实现代码**；待用户评估确认后，由工程师按 §6 任务列表施工、QA 按 §8 验证。
