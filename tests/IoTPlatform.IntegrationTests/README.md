# IoTPlatform 集成测试脚手架

安圣二开协议重构线「第 2 步」产物。本工程**只提供脚手架和示例**，不含 T5–T14 的业务用例。

---

## 1. 三十秒跑起来

```bash
# 1) 配置测试库连接串（不含 Database；库名由脚手架自动生成）
export IOT_TEST_MYSQL="Server=192.168.3.7;Port=3306;User Id=<账号>;Password=<密码>;"

# Windows PowerShell:
#   $env:IOT_TEST_MYSQL = "Server=192.168.3.7;Port=3306;User Id=<账号>;Password=<密码>;"

# 2) 跑
dotnet test tests/IoTPlatform.IntegrationTests/
```

没配 `IOT_TEST_MYSQL` 时，脚手架会回落到 `appsettings.Testing.json` 的 `ConnectionStrings:TestMySql`，
该项在仓库里**留空**——所以未配置就会直接报错退出，而不会静默连到某个生产库。这是刻意设计。

> **绝不要**把账号密码写进仓库任何文件。CI 请用 secret 注入同名环境变量。

---

## 2. 环境变量

| 变量 | 必填 | 默认 | 说明 |
|---|---|---|---|
| `IOT_TEST_MYSQL` | 是 | — | 测试 MySQL 服务器连接串，**不要带 `Database=`**（带了也会被剥掉） |
| `IOT_TEST_DB_PROVIDER` | 否 | `mysql` | `mysql` 用现成实例；`testcontainers` 用容器（见 §6） |
| `IOT_TEST_KEEP_SCHEMA` | 否 | — | 置 `1` 时跑完不删 schema，用于排障 |
| `IOT_TEST_SWEEP_HOURS` | 否 | `2` | 回收早于该小时数的残留测试库；置 `0` 关闭 |

---

## 3. 一次运行的生命周期

```
[集合级 DatabaseFixture]  ← 整个测试进程只跑一次
  ① MySqlDbProvisioner 建库 iot_platform_test_{时间戳}_{随机}
  ② new TestWebAppFactory → 探针请求拉起管道 → Program.cs 的 Database.Migrate() 在真实库上建表
  ③ 建 Respawner（忽略 __EFMigrationsHistory）
  ④ 基线置空

      [用例级 IntegrationTestBase]  ← 每个 [Fact] 各一次
        ① Respawn 清数据（保表结构）
        ② StaticStateResetter 清进程级静态字典
        ③ FakeProtocolAdapterFactory.Reset() 清录制（含 GetOrCreateFor 登记的分身）
        ④ SeedData 播种 6 条基线记录
        ⑤ CreateClient()
        —— 测试方法体 ——
        ⑥ 释放 HttpClient

  ⑤ 释放 TestServer
  ⑥ DROP DATABASE（除非 IOT_TEST_KEEP_SCHEMA=1）
```

建库时还会顺带**回收早于 2 小时的残留测试库**（`IOT_TEST_SWEEP_HOURS`），
兜住「进程被强杀 ⇒ ⑥ 没跑到 ⇒ 测试库堆积」这条必然会发生的路径。
回收有三重护栏：只认 `iot_platform_test_` 前缀、必须能解析出库名里的时间戳、且早于阈值——
解析不出就跳过，宁可漏删不可错删。

**为什么用真实 MySQL 而不是 InMemory/SQLite**：本平台重度依赖 MySQL 方言（JSON 列、
`ExecuteSqlRawAsync`、Pomelo 的类型映射）与真实迁移脚本。InMemory provider 连
`Database.Migrate()` 都不支持，测出来的绿是假绿。顺带地，跑通迁移本身就是 T5 的验收项之一。

---

## 4. 为什么必须禁并行

三处共享状态决定了并行必然串扰：

1. **同一个 schema** —— Respawn 清库是全库级操作，A 用例清库会把 B 用例播种的数据一起端走；
2. **同一个 TestServer** —— 共享 DI 容器、共享 `RecordingAnShengAdapter` 的录制队列；
3. **进程级静态字典** —— `AnShengMqttProtocolAdapter.DeviceKinds`、
   `AnShengCommandService.FrameIdCommandIdMap` 不随作用域回收。

禁并行落在两处，**改一处不生效，必须同时保留**：

* `xunit.runner.json` → `"parallelizeTestCollections": false`
* `Collections/IntegrationTestCollection.cs` → `[CollectionDefinition(..., DisableParallelization = true)]`

---

## 5. 写新用例的正确姿势

```csharp
[Collection(SharedTestConstants.CollectionName)]   // ← 必须显式标注，不要依赖基类继承
public sealed class MyFeatureTests : IntegrationTestBase
{
    public MyFeatureTests(DatabaseFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Something()
    {
        var res = await Client.AsAdmin().GetAsync("/api/v1/...");

        // 断言业务码，不是 HTTP 状态码
        var body = await ReadAsync<ApiResponse<XxxDto>>(res);
        body!.Code.Should().Be(200);

        // 断言下发副作用
        Adapter.Sent.Should().ContainSingle();

        // 断言落库副作用
        var n = await QueryDbAsync(db => db.Devices.CountAsync());
    }
}
```

### 五条硬约定

1. **业务分支断言 `ApiResponse.Code`，认证分支断言 HTTP 状态码。**
   本平台绝大多数业务失败仍返回 `HTTP 200 + Code != 200`（如越权是 `200 + Code 403`），
   只看 HTTP 状态码会漏掉全部业务回归。

   **但「完全匿名」是唯一例外，已实测确认：**

   | 场景 | 实际响应 | 断言方式 |
   |---|---|---|
   | 未带任何 `X-Test-*` 头（匿名） | **裸 HTTP 401，包体为空** | 断 `StatusCode`，**不要**调 `ReadAsync`（空包体会反序列化失败） |
   | 已认证但权限不足 | `HTTP 200 + Code 403` | 断 `Code` |
   | 正常 | `HTTP 200 + Code 200` | 断 `Code` |

   原因：`AuthorizationMiddleware` 在进入 MVC 过滤器**之前**就对匿名请求发起 challenge，
   所以 `PermissionAuthorizeAttribute` 里那段 `return new JsonResult(ApiResponse.Unauthorized(...))`
   对匿名请求而言是**死代码**，永远走不到。参见 `示例-01` / `示例-08`。

2. **主键一律用 `Seed.XxxId`，不要写字面量。**
   `SharedTestConstants.ProtocolConfigId` 只是个名义值；真实 Id 由 MySQL 自增生成。

3. **常量统一放 `SharedTestConstants`**，不要各写各的 IMEI / AppCode。

4. **一个 Factory 只对应一个 AppCode。**
   EF 的模型缓存键不含 AppCode，`AppDbContext.ConfigureGlobalQueryFilters` 会在首次建模时冻结。
   本脚手架里首次建模发生在启动迁移阶段（此时 `TenantContext.AppCode` 为空）→
   **全局租户过滤器实际不会被装上**，租户隔离由控制器内的显式 `Where` 承担。
   要验证「全局过滤器」本身，必须另起一个 `TestWebAppFactory`（新进程更稳）。

5. **需要新的静态状态清理时，改 `StaticStateResetter` 并让 `Verify()` 覆盖它。**
   示例 `示例-04` 就是这个自检的哨兵：生产代码一旦改字段名，它会红。

---

## 6. 切到 Testcontainers（Docker 就位后）

当前开发机无 Docker，故默认走 `MySqlDbProvisioner`。切换只需三步，**其余文件零改动**：

1. `IoTPlatform.IntegrationTests.csproj`：解开 `Testcontainers.MySql` 的 `PackageReference` 注释，
   并在 `PropertyGroup` 加 `<DefineConstants>$(DefineConstants);TESTCONTAINERS</DefineConstants>`；
2. 补齐 `Infrastructure/TestcontainersDbProvisioner.cs` 里被 `#if TESTCONTAINERS` 包住的实现；
3. 设 `IOT_TEST_DB_PROVIDER=testcontainers`。

这个可切换性正是 `IDbProvisioner` 抽象存在的唯一理由——
`DatabaseFixture` 只认接口，不认具体实现。

---

## 7. 关键替换点一览（改动生产代码前先看这里）

| 生产依赖 | 测试替换为 | 位置 |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | 一次性测试 schema | `TestWebAppFactory.ConfigureAppConfiguration` |
| `DbContextOptions<AppDbContext>` | 显式 `ServerVersion` + 测试库 | `TestWebAppFactory.ReplaceDbContext` |
| 3 个 `IHostedService` + `IMqttClientService` + `IAnShengDiscoveryService` | 全部移除 | `TestWebAppFactory.RemoveBackgroundServices` |
| `IProtocolAdapterFactory` | `FakeProtocolAdapterFactory` | `TestWebAppFactory.ReplaceAdapterFactory` |
| 默认认证方案（JwtBearer） | `TestAuthHandler`（`"Test"`） | `TestWebAppFactory.ReplaceAuthentication` |

**MQTT 的接缝是 `IProtocolAdapterFactory`，不是 `IMqttClient`。**
MQTTnet 的客户端从未进入 DI 容器，无从替换；而 `AnShengCommandService` 正是通过
`IProtocolAdapterFactory.GetAdapter(configId)` 取适配器——这是唯一稳定的注入点。
另注意：`FakeProtocolAdapterFactory.GetAdapter` **对任意 configId 都返回非 null**，
与生产实现（未命中返回 `null`）不同，这是为了让用例不必预知自增 Id。

### 7.1 替身必须复刻生产的「默认拒绝」护栏

第 1 步给 `AnShengMqttProtocolAdapter.SendCommandAsync` 加了默认拒绝：
方法既不在 `AnShengCommandCatalog`、也不在 `LegacyMethodWhitelist`（`orderStart` /
`orderEnd` / `orderUp`）里，就抛 `NotSupportedException`。

**这条护栏必须由 `RecordingAnShengAdapter` 一起复刻**，否则「协议外命令被放行」这类
严重缺陷会被测成绿色——因为服务层 `AnShengCommandService` 的目录校验包在

```csharp
if (AnShengCommandCatalog.TryGet(method, out var spec) && spec != null) { ... }
```

里，方法不在目录中时整个校验块被**跳过**，服务层并不拦截，真正把关的是适配器层。

为避免「双份真相」漂移，替身的白名单是**反射读取生产字段**而非手抄，
并由 `RecordingAnShengAdapter.WhitelistSource` 暴露实际来源
（`reflection` = 已同步，`fallback` = 反射失效、可能漂移）。
`示例-10` 就是盯这个的哨兵：生产一旦重命名 `LegacyMethodWhitelist`，它立刻变红。

需要绕过护栏观察下游行为时，用例可显式设 `Adapter.EnforceProtocolWhitelist = false`
（`Reset()` 会自动复位为 `true`）。

---

## 8. 对生产代码的改动

**仅 1 行**，在 `Program.cs` 末尾：

```csharp
public partial class Program { }
```

`WebApplicationFactory<TEntryPoint>` 需要一个可见的入口类型，而顶级语句生成的 `Program`
是 `internal` 的。这行不改变任何运行时行为。除此之外，本次未修改任何业务 `.cs`。

> 若 `git diff` 里看到 `Infrastructure/Protocol/Adapters/AnShengMqttProtocolAdapter.cs`
> 也有改动（`LegacyMethodWhitelist` + 默认拒绝），那是**第 1 步**的产物，不属于本步骤。
>
> 另外 `IoTPlatform.sln` 被改过：按方案 §2 文件清单第 21 项，
> 把 `IoTPlatform.IntegrationTests` 与既有的 `IoTPlatform.AnSheng.Tests` 都注册进解决方案
> （此前 sln 只含主工程，两个测试工程都是游离的）。

---

## 9. 排障

| 现象 | 原因 / 处置 |
|---|---|
| 启动即报「未配置测试数据库连接串」 | 没设 `IOT_TEST_MYSQL`，见 §1 |
| `Unknown database 'iot_platform_test_...'` | 账号缺 `CREATE`/`DROP` 权限 |
| 单跑绿、连跑红 | 静态状态泄漏。检查是否有新的 `static` 字典未纳入 `StaticStateResetter` |
| 想看跑完后的库 | `IOT_TEST_KEEP_SCHEMA=1`，跑完手动 `DROP DATABASE` |
| 残留了一堆 `iot_platform_test_*` 库 | 进程被强杀（CI 超时 / 停止调试 / Ctrl+C）导致 `DisposeAsync` 未执行。**下次跑测试会自动回收**早于 `IOT_TEST_SWEEP_HOURS`（默认 2h）的残留库，通常无需人工干预；想立刻清可临时设 `IOT_TEST_SWEEP_HOURS=0.001` 跑一次 |
| FluentAssertions 报许可证 | 被误升到 7.x（改商业许可）。锁回 `6.12.2` |
