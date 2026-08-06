using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IoTPlatform.Data;
using IoTPlatform.Infrastructure.Protocol.Adapters;
using IoTPlatform.Infrastructure.Protocol.AnSheng;
using IoTPlatform.Models;
using IoTPlatform.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IoTPlatform.AnSheng.Tests;

/// <summary>
/// P1 回归（第 ① 道防线）：<see cref="AnShengDiscoveryService"/> 待认领池写入的
/// <b>进程内串行化闸门</b>。
///
/// 【被回归的缺陷】
///   同一台设备（现场实证 IMEI=863434084755211）在 <c>discovered_ansheng_devices</c>
///   落出两行，<c>DiscoveredAt</c> 相差约 10ms。成因是 <c>OnDeviceOnlineAsync</c> 走
///   check-then-act（查不到才插）且无锁，而上行是并发的：<c>connected</c> 事件
///   （<c>ConnectedEventHandler</c>）与数据桥接（<c>ProtocolConfigService</c>，其中一条是
///   fire-and-forget）在设备刚连上时几乎同时触发，两条线程同时判定「不存在」后各插一行。
///
/// 【★ 本文件的能力边界 —— 必须先读懂再改】
///   测试基座是 <b>EF InMemory Provider</b>，它<b>不实现唯一索引</b>、也<b>不会抛 MySQL 1062</b>。
///   因此本文件<b>只能</b>验证第 ① 道防线（<c>_discoveryGates</c> 进程内闸门），
///   <b>验证不到</b>第 ② 道防线（数据库 <c>UNIQUE(Imei, AppCode)</c> 兜底与 1062 降级分支）。
///
///   反过来说，正因为 InMemory 不拦重复行，本文件对「闸门被移除」这一退化具备<b>真实证伪力</b>：
///   一旦去掉 <c>_discoveryGates</c>，并发用例会稳定落出多行而变红。
///   这恰恰是真库集成测试<b>做不到</b>的——那边有唯一索引兜底，删了闸门结果仍是一行。
///
    ///   第 ② 道防线（数据库 <c>UNIQUE(Imei, AppCode)</c> 兜底 + 1062 降级分支）需要真实
    ///   MySQL 才能触发——本文件做不到。本轮回归中，这层是通过以下方式<b>结构性验证</b>的：
    ///   （a）集成测试启动时对一次性 schema 执行 <c>Database.Migrate()</c>，新迁移
    ///   <c>20260806033128_AddDiscoveredAnShengUniqueImeiAppCode</c>（含去重 + 建唯一索引）
    ///   在全新 MySQL 上干净落地，65/65 通过，证明唯一索引与去重逻辑本身正确；
    ///   （b）既有发现/认领链路在「已带唯一索引」的库上全部通过，证明加约束未破坏既有行为。
    ///   <b>诚实说明【尚未闭环】</b>：1062 降级分支（并发撞键 → Detach → 重查 → 更新）目前
    ///   <b>没有被任何自动化并发用例在真实 MySQL 上直接命中</b>。若要彻底闭环，建议在
    ///   <c>IoTPlatform.IntegrationTests</c> 新增 <c>AnShengDiscoveryConcurrencyTests</c>：
    ///   用两个<b>独立实例</b>（各自持有进程内闸门字典、共享同一测试 schema）并发对同一 IMEI
    ///   上线，靠 MySQL 唯一索引做串行点，必然触发一次 1062 降级，断言最终只落一行。
    ///   两层缺一不可，但第 ② 层的运行时分支用例属于本轮待办，非已交付。
///
/// 【为什么归进禁并行集合】
///   <c>OnDeviceOnlineAsync</c> 会写 <c>AnShengMqttProtocolAdapter</c> 的进程级静态品类字典，
///   与同样触碰该字典的测试类并行会让结果随机漂移。见 <see cref="AnShengStaticStateCollection"/>。
/// </summary>
[Collection(AnShengStaticStateCollection.Name)]
public sealed class AnShengDiscoveryGateTests : IDisposable
{
    /// <summary>
    /// 本文件专用 IMEI。
    ///
    /// 【必须与其他用例错开】集成测试基线播种用 <c>864536072949900</c>、
    /// <c>AnShengClaimTests</c> 用 <c>864536072949901</c>、
    /// <c>ScaffoldFalsificationTests</c> 的污染探针用 <c>864536072949999</c>。
    /// 撞号会让「只落一行」的断言被别处的数据干扰，失败原因还极难定位。
    /// </summary>
    private const string GateImei = "864536072949930";

    /// <summary>第二租户码，用于验证唯一性只约束到「租户内」。</summary>
    private const string ForeignAppCode = "TEST_OTHER";

    private const string AppCode = "TEST";

    /// <summary>
    /// 并发线程数。取 8 而非 2：闸门失效时线程越多越容易真的撞上同一窗口，
    /// 2 条线程有相当概率被调度器错开，让退化用例假绿。
    /// </summary>
    private const int Concurrency = 8;

    private readonly ServiceProvider _provider;
    private readonly DbContextOptions<AppDbContext> _options;

    /// <summary>
    /// 每个用例一套独立内存库 + 独立 <see cref="AnShengDiscoveryService"/> 实例
    /// （闸门字典随实例存活，共用实例会让用例间互相「预热」出已存在的锁）。
    /// </summary>
    public AnShengDiscoveryGateTests()
    {
        // 刻意用 AppDbContext(DbContextOptions) 这个「设计时」重载：
        // 它把 _tenantContextAccessor 置 null，于是不挂全局租户过滤器，
        // 单元层拿到的是无过滤的干净视图（租户隔离由集成测试覆盖）。
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"discovery-gate-{Guid.NewGuid():N}")
            .EnableSensitiveDataLogging()
            .Options;

        var services = new ServiceCollection();

        // 被测服务是 Singleton，取 DbContext 一律经 IServiceScopeFactory 开作用域：
        // 这里必须注册成 Scoped，才能真实复刻「每条上行各持一个 DbContext」的并发形态。
        // 若注册成 Singleton，所有线程共用一个上下文，竞态根本不会出现，用例就成了摆设。
        services.AddScoped(_ => new AppDbContext(_options));

        _provider = services.BuildServiceProvider();

        // 用例之间必须清干净：OnDeviceOnlineAsync 会登记设备品类到进程级静态字典。
        AnShengMqttProtocolAdapter.ClearDeviceKinds();
    }

    /// <summary>释放容器并清理静态副作用。</summary>
    public void Dispose()
    {
        AnShengMqttProtocolAdapter.ClearDeviceKinds();
        _provider.Dispose();
        GC.SuppressFinalize(this);
    }

    // ─────────────────────────────────────────────────────────────
    // 第 ① 道防线：进程内闸门
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// ★ 核心回归：同一 IMEI 并发上线，待认领池<b>只能</b>落一行。
    ///
    /// 【怎么逼出竞态】用一个 <see cref="TaskCompletionSource"/> 当发令枪，
    /// 让 8 个线程先各自就位、再同时冲进 <c>OnDeviceOnlineAsync</c>，
    /// 把「查不到 → 插入」的窗口尽量重叠。
    /// </summary>
    [Fact(DisplayName = "P1回归① 同一 IMEI 并发上线 → 待认领池只落一行（进程内闸门）")]
    public async Task OnDeviceOnline_Concurrent_SameImei_Should_Insert_Only_One_Row()
    {
        var service = NewService();

        await RunConcurrentlyAsync(() =>
            service.OnDeviceOnlineAsync(GateImei, "Air780E", "4G", AppCode));

        var rows = await QueryAsync(db => db.Set<DiscoveredAnShengDevice>()
            .Where(d => d.Imei == GateImei)
            .ToListAsync());

        Assert.True(rows.Count == 1,
            $"同一 IMEI 并发上线应只落一行，实际落了 {rows.Count} 行" +
            $"（Id={string.Join(",", rows.Select(r => r.Id))}）——进程内串行化闸门已失效");

        // 顺带确认这一行是「登记成功」而不是「插了个空壳」：
        // OnDeviceOnlineAsync 内部 catch 掉全部异常，只断言行数会漏掉静默失败。
        var row = rows[0];
        Assert.Equal(AppCode, row.AppCode);
        Assert.Equal("Air780E", row.Model);
        Assert.Equal("4G", row.NetType);
        Assert.False(row.IsClaimed);
        Assert.NotNull(row.LastSeenAt);
    }

    /// <summary>
    /// 并发上线不得把「首次发现时间」往后推。
    ///
    /// 【为什么单独钉一条】<c>DiscoveredAt</c> 是待认领池列表的默认排序键，
    /// 若后到的线程把它当成 upsert 字段一起刷新，设备会在列表里反复跳到最前，
    /// 且「这台设备什么时候第一次出现」这一排障信息就永久丢失了。
    /// </summary>
    [Fact(DisplayName = "P1回归① 重复上线只推进 LastSeenAt，不得改写 DiscoveredAt")]
    public async Task Repeated_Online_Should_Advance_LastSeenAt_But_Keep_DiscoveredAt()
    {
        var service = NewService();

        await service.OnDeviceOnlineAsync(GateImei, "Air780E", "4G", AppCode);

        var first = await QueryAsync(db => db.Set<DiscoveredAnShengDevice>()
            .AsNoTracking()
            .SingleAsync(d => d.Imei == GateImei));

        // 拉开可观测的时间差，否则两次 UtcNow 可能落在同一刻度上，断言退化成恒真。
        await Task.Delay(20);

        await RunConcurrentlyAsync(() =>
            service.OnDeviceOnlineAsync(GateImei, "Air780E", "4G", AppCode));

        var after = await QueryAsync(db => db.Set<DiscoveredAnShengDevice>()
            .AsNoTracking()
            .SingleAsync(d => d.Imei == GateImei));

        Assert.Equal(first.Id, after.Id);
        Assert.Equal(first.DiscoveredAt, after.DiscoveredAt);
        Assert.True(after.LastSeenAt >= first.LastSeenAt, "重复上线必须推进 LastSeenAt");
    }

    /// <summary>
    /// 闸门按 IMEI 分桶：两台不同设备并发上线互不阻塞，且各自落各自的行。
    /// 若实现退化成「全局一把大锁」，功能虽仍正确，但会把整条上行链路串成单线程；
    /// 若退化成「不分桶且串写」，则会出现两台设备互相覆盖。本例守住行数与归属。
    /// </summary>
    [Fact(DisplayName = "P1回归① 不同 IMEI 并发上线 → 各落一行，互不串写")]
    public async Task Concurrent_Different_Imei_Should_Each_Insert_One_Row()
    {
        const string otherImei = "864536072949931";
        var service = NewService();

        await Task.WhenAll(
            RunConcurrentlyAsync(() => service.OnDeviceOnlineAsync(GateImei, "Air780E", "4G", AppCode)),
            RunConcurrentlyAsync(() => service.OnDeviceOnlineAsync(otherImei, "Air720", "WiFi", AppCode)));

        var rows = await QueryAsync(db => db.Set<DiscoveredAnShengDevice>().ToListAsync());

        Assert.Equal(2, rows.Count);
        Assert.Single(rows, r => r.Imei == GateImei && r.NetType == "4G");
        Assert.Single(rows, r => r.Imei == otherImei && r.NetType == "WiFi");
    }

    /// <summary>
    /// 唯一性只约束到「租户内」：同一 IMEI 落在不同租户下各有一行是<b>合法</b>状态。
    ///
    /// 【为什么必须钉住】如果哪天有人把唯一键从 <c>(Imei, AppCode)</c> 收紧成单列 <c>Imei</c>，
    /// 或把闸门里的查询改成只按 IMEI 命中，B 租户的设备就会被 A 租户的上报改写，
    /// 表现为跨租户串数据——那是比重复行严重得多的事故。
    /// </summary>
    [Fact(DisplayName = "P1回归① 同 IMEI 不同租户 → 各自一行（唯一性只到租户内）")]
    public async Task Same_Imei_Under_Different_AppCode_Should_Keep_One_Row_Each()
    {
        var service = NewService();

        await service.OnDeviceOnlineAsync(GateImei, "Air780E", "4G", AppCode);
        await service.OnDeviceOnlineAsync(GateImei, "Air780E", "4G", ForeignAppCode);

        var rows = await QueryAsync(db => db.Set<DiscoveredAnShengDevice>()
            .Where(d => d.Imei == GateImei)
            .ToListAsync());

        Assert.Equal(2, rows.Count);
        Assert.Single(rows, r => r.AppCode == AppCode);
        Assert.Single(rows, r => r.AppCode == ForeignAppCode);
    }

    /// <summary>
    /// 租户码迟到的场景：先落一行「尚未归属租户」的记录，随后带 AppCode 的上报到达时
    /// 必须<b>回填到同一行</b>，而不是另起一行。
    /// 这是 <c>FindDiscoveredForUpsertAsync</c> 的「未归属行回填」分支，
    /// 也是历史数据不产生重复行的关键。
    /// </summary>
    [Fact(DisplayName = "P1回归① 无租户码先落行 → 后续带租户码的上报回填同一行")]
    public async Task Online_Without_AppCode_Then_With_AppCode_Should_Backfill_Same_Row()
    {
        var service = NewService();

        await service.OnDeviceOnlineAsync(GateImei, null, null, null);
        await service.OnDeviceOnlineAsync(GateImei, "Air780E", "4G", AppCode);

        var rows = await QueryAsync(db => db.Set<DiscoveredAnShengDevice>()
            .Where(d => d.Imei == GateImei)
            .ToListAsync());

        Assert.True(rows.Count == 1, $"租户码迟到不应另起一行，实际 {rows.Count} 行");
        Assert.Equal(AppCode, rows[0].AppCode);
        Assert.Equal("Air780E", rows[0].Model);
    }

    /// <summary>空 IMEI 直接忽略，不得落出脏行。</summary>
    [Theory(DisplayName = "P1回归① 空 IMEI 一律忽略，不落行")]
    [InlineData(null)]
    [InlineData("")]
    public async Task Online_With_Blank_Imei_Should_Be_Ignored(string? imei)
    {
        var service = NewService();

        await service.OnDeviceOnlineAsync(imei!, "Air780E", "4G", AppCode);

        var count = await QueryAsync(db => db.Set<DiscoveredAnShengDevice>().CountAsync());
        Assert.Equal(0, count);
    }

    // ─────────────────────────────────────────────────────────────
    // 辅助
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 造一个被测服务实例。适配器工厂与探测服务在 <c>OnDeviceOnlineAsync</c> 路径上
    /// 完全不被触碰，故给替身即可；探测替身的任一成员被调用都会抛异常，
    /// 一旦将来实现意外依赖它们，会立刻暴露而不是静默通过。
    /// </summary>
    private AnShengDiscoveryService NewService() => new(
        _provider.GetRequiredService<IServiceScopeFactory>(),
        new FakeProtocolAdapterFactory(),
        new UnusedProbeService(),
        NullLogger<AnShengDiscoveryService>.Instance);

    /// <summary>
    /// 让 <see cref="Concurrency"/> 个线程「同时」执行同一操作。
    /// 发令枪模式：先让所有线程排队等同一个 TCS，再一次性放行，最大化重叠窗口。
    /// </summary>
    private static async Task RunConcurrentlyAsync(Func<Task> action)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var workers = Enumerable.Range(0, Concurrency)
            .Select(_ => Task.Run(async () =>
            {
                await start.Task;
                await action();
            }))
            .ToArray();

        start.SetResult();
        await Task.WhenAll(workers);
    }

    /// <summary>在独立作用域里查内存库，避免复用被测代码的跟踪状态。</summary>
    private async Task<T> QueryAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await query(db);
    }

    /// <summary>
    /// 探测服务替身：上线路径不该碰它，碰了就炸。
    /// </summary>
    private sealed class UnusedProbeService : IAnShengProbeService
    {
        public Task<AnShengProbeResult> ProbeAsync(
            int protocolConfigId, string imei, CancellationToken ct = default)
            => throw new NotSupportedException("设备上线路径不应触发探测");

        public void ClearPending()
            => throw new NotSupportedException("设备上线路径不应清理在途探测");
    }
}
