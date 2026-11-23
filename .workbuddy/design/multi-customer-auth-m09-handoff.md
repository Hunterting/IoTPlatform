# 多租户授权模型 → M09 工作台 UI 衔接说明

> 产出：T05.3（模型层最后一个任务）　|　上游：`multi-customer-auth-design.md`（Bob）
> 状态：**仅契约与指引，本轮不改任何 UI 逻辑代码**。`DashboardPage.tsx` / `UserManagementPage.tsx` / `AuthContext.tsx` 的 `deriveTenantContext` 零改动。

---

## 1. 本轮已经落地的契约（M09 可直接消费）

| 层 | 字段/接口 | 说明 |
|---|---|---|
| 后端 | `UserDto.ManagedCustomerIds`（`DTOs/Responses/UserResponse.cs`） | `List<long>?`；role=admin 时为授权客户 ID 集合（来自 `user_customers`，DB 为准），下级与超管为 `null` |
| 后端 | `LoginResponse.User` / `/auth/me` 的 `User` | 同上；admin 的 `CustomerId` / `AppCode` / `CustomerName` 全为 `null`，`CurrentCustomer = null` |
| 后端 | `GET /api/v1/customers/accessible` | admin 返回**授权客户集合**（不是"全部可切换客户"）；超管全量；下级按单 AppCode |
| 后端 | `PUT/GET /api/v1/users/{userId}/managed-customers` | 仅超管可读写 admin 的授权集合；变更即时生效（旧 token 下一次请求即反映） |
| 前端 | `UserDto.managedCustomerIds?: number[] \| null` | `Web/src/app/services/api/types/auth.types.ts`，`LoginResponse.User` 的新字段 |
| 前端 | `User.managedCustomerIds?: number[]` | `Web/src/app/contexts/AuthContext.tsx` 的 `User` 接口，类型预留 |

**关键不变量**：admin 不再签发 `CustomerId` / `AppCode` 单值 claim，只签 `ManagedCustomerIds` 快照；下级与超管逐字段不变。

---

## 2. M09 必须改的四处（本轮一律未改）

### 2.1 把集合从登录响应带进前端 `User`

`Web/src/app/services/adapters/authAdapter.ts` 的 `adaptLoginResponseToUser`（约 `:259-272`）目前返回 `any` 且**未拷贝** `managedCustomerIds`。M09 需补一行：

```ts
managedCustomerIds: loginResponse.user.managedCustomerIds ?? [],
```

否则 `AuthContext.user` 拿不到集合，后续三处逻辑都无从消费。同时确认 `localStorage.user` 的存量会话兼容（老会话无该字段 ⇒ 当作空集合处理）。

> 本轮刻意不改 adapter：它属于"数据消费逻辑"而非"类型声明"，M09 与 UI 一起改更内聚。

### 2.2 `DashboardPage.tsx:613-616` — `visibleCustomers` 三角色分支

现状：

```ts
const visibleCustomers = isSuperAdmin
  ? customers
  : customers.filter(c => String(c.id) === String(user?.customerId ?? currentCustomer?.id));
```

admin 的 `customerId` / `currentCustomer` 现在都是 `null`，上式会把 `visibleCustomers` 过滤成空 ⇒ 工作台首块空白（设计 §9 R5）。M09 改为：

```ts
const isSuperAdmin = user?.role === 'super_admin';
const isTenantAdmin = user?.role === 'admin';

const visibleCustomers = isSuperAdmin
  ? customers                                    // 超管：全部
  : isTenantAdmin
    ? customers.filter(c => (user?.managedCustomerIds ?? []).map(String).includes(String(c.id)))
    : customers.filter(c => String(c.id) === String(user?.customerId ?? currentCustomer?.id)); // 下级：原单值逻辑逐字保留
```

推荐做法：admin 的客户清单优先用 `GET /customers/accessible`（后端已按集合过滤，是权威数据源），前端集合仅作二次防御。

### 2.3 `AuthContext.tsx:110-113` — `deriveTenantContext` 对 admin 不再返回 null 兜底

现状函数在 `u.role === 'super_admin' || list.length === 0` 时返回 `null`；admin 无 `appCode` / `customerId`，在多条客户时也会走到 `return ... : null`。

- 该 `null` 在模型层是**预期行为**（设计 §9 R5 已登记），不是 bug，本轮不改。
- M09 需在 admin 分支改为"集合型上下文"：不再强行收敛到单个 `Customer`，而是把 `managedCustomerIds` 对应的客户集合作为工作上下文（或显式选中一个客户后用 URL 参数 + 集合内校验，不改 token、不调用 `SwitchCustomerAsync`——后者仍是超管独占）。
- 下级分支（AppCode → customerId → 单条收敛）必须逐字保留。

### 2.4 `UserManagementPage.tsx:465-479` — 客户下拉对 admin 显示授权集合

现状下拉只在 `user?.role === 'super_admin'` 时渲染。M09 改为：

- 超管：全部 `activeCustomers`（原样）。
- admin：仅 `activeCustomers` 中 Id ∈ `user.managedCustomerIds` 的子集；**仍是单选**，因为下级用户仍绑单客户（D4），不是把集合整包下发给新用户。
- 下级：沿用"不指定（归属当前登录租户）"，不显示下拉。
- 服务端已有强校验：admin 建户时 `UsersController.CreateUser` 会经 `ICustomerScopeService.CanAccessCustomerAsync` 校验 `request.CustomerId`，越权返回 HTTP 200 + `Code=403`。前端下拉只是体验优化，**不是安全边界**。

---

## 3. 验收要点（M09 自测）

1. admin 登录后工作台显示且仅显示授权客户；无授权时显示空态而非崩溃。
2. 超管给某 admin 追加客户后，admin **不重新登录**刷新页面即可看到新客户（后端 DB 为准）。
3. admin 在客户下拉里只能选授权客户；绕过前端直调 API 指定集合外客户仍得 `Code=403`。
4. 下级（operator/user）三处逻辑与改造前逐字段一致：单客户、单 AppCode、无 `ManagedCustomerIds`。
5. 超管仍可看全部、切换客户、建客户，行为零变化。

## 4. 边界与禁忌

- **不要**让 admin 参与 `SwitchCustomerAsync`（仅超管）；如需聚焦某客户，用 URL 参数 + 集合内校验。
- **不要**用 JWT 的 `ManagedCustomerIds` claim 做前端越权判定的唯一依据；claim 是快照，后端 `GetAccessibleCustomers` 才是权威。
- **不要**扩展 `AllowedAreaIds`：分配新客户不自动扩区域白名单（设计 §9 R6），由超管单独维护。
- **不要**把 `customer_admin` 纳入多客户分支（设计 §9 R9），它本轮仍走单客户。
