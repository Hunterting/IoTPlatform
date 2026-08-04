using IoTPlatform.Data;
using IoTPlatform.Infrastructure.Protocol.Adapters;
using IoTPlatform.Infrastructure.Protocol.AnSheng;
using IoTPlatform.Models;
using IoTPlatform.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IoTPlatform.AnSheng.Tests;

/// <summary>
/// <see cref="AnShengDeviceProfileService"/> 验收测试。
///
/// 【为什么用 InMemory 而不是真库】
///   本服务的全部业务分支（品类判定、快照合并、来源降级保护）都不依赖任何
///   MySQL 特有语义，只需要一个能跑 <c>DbSet</c> 增删查的上下文。
///   真库放到集成测试里跑一遍端到端即可，单元层没必要为此背上一个 MySQL 依赖。
///
/// 【DbContext 的构造方式】
///   刻意使用 <c>AppDbContext(DbContextOptions)</c> 这个"设计时"重载：
///   它把 <c>_tenantContextAccessor</c> 置 null，而 <c>ConfigureGlobalQueryFilters</c>
///   全程用 <c>?.</c> 取值，因此不会挂租户过滤器 —— 单元测试拿到的是无过滤的干净视图，
///   正是我们想要的（租户隔离本身由集成测试覆盖）。
///
/// 【为什么归进禁并行集合】
///   本类会 <c>ClearDeviceKinds()</c> / <c>RegisterDeviceKind()</c> 这个进程级静态字典。
///   xUnit 默认让不同测试类并行跑，若与同样触碰静态状态的类撞车，结果会随机漂移。
///   见 <see cref="AnShengStaticStateCollection"/>。
/// </summary>
[Collection(AnShengStaticStateCollection.Name)]
public class AnShengDeviceProfileServiceTests : IDisposable
{
    private const string Imei = "864536072949900";
    private const string AppCode = "TEST";

    private readonly AppDbContext _db;
    private readonly AnShengDeviceProfileService _service;

    /// <summary>
    /// 每个用例一套独立的内存库与静态品类字典，避免用例间互相污染。
    /// </summary>
    public AnShengDeviceProfileServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"profile-{Guid.NewGuid():N}")
            .EnableSensitiveDataLogging()
            .Options;

        _db = new AppDbContext(options);

        // ApplyProbeAsync 会写适配器的进程级静态字典，用例之间必须清干净。
        AnShengMqttProtocolAdapter.ClearDeviceKinds();

        _service = new AnShengDeviceProfileService(_db);
    }

    /// <summary>释放内存库并清理静态副作用。</summary>
    public void Dispose()
    {
        AnShengMqttProtocolAdapter.ClearDeviceKinds();
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    // ─────────────────────────────────────────────────────────────
    // 一、null 容忍（产品决策 Q5：存量不回填）
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 存量设备没有档案行，查询必须<b>返回 null 而不是抛异常</b>。
    /// 这是 Q5「不回填」决策能够成立的前提：调用方走降级分支即可，无需 try/catch。
    /// </summary>
    [Fact]
    public async Task GetByImeiAsync_Should_Return_Null_For_Legacy_Device()
    {
        Assert.Null(await _service.GetByImeiAsync(Imei));
        Assert.Null(await _service.GetByImeiAsync("不存在的IMEI"));
    }

    /// <summary>空白 IMEI / 非法 DeviceId 一律返回 null，不抛参数异常。</summary>
    [Fact]
    public async Task Getters_Should_Tolerate_Invalid_Input()
    {
        Assert.Null(await _service.GetByImeiAsync(""));
        Assert.Null(await _service.GetByImeiAsync("   "));
        Assert.Null(await _service.GetByDeviceIdAsync(0));
        Assert.Null(await _service.GetByDeviceIdAsync(-1));
    }

    /// <summary>档案不存在时 <c>GetByDeviceIdAsync</c> 同样返回 null。</summary>
    [Fact]
    public async Task GetByDeviceIdAsync_Should_Return_Null_When_Not_Attached()
    {
        await _service.GetOrCreateAsync(Imei, AppCode);
        await _db.SaveChangesAsync();

        Assert.Null(await _service.GetByDeviceIdAsync(12345));
    }

    // ─────────────────────────────────────────────────────────────
    // 二、GetOrCreate
    // ─────────────────────────────────────────────────────────────

    /// <summary>首次调用建一份空档案，且<b>不落库</b>（事务边界归调用方）。</summary>
    [Fact]
    public async Task GetOrCreateAsync_Should_Add_Without_Saving()
    {
        var profile = await _service.GetOrCreateAsync(Imei, AppCode);

        Assert.Equal(Imei, profile.Imei);
        Assert.Equal(AppCode, profile.AppCode);
        Assert.Equal(AnShengDeviceKind.Unknown, profile.Kind);
        Assert.Equal(AnShengKindSource.Unknown, profile.KindSource);
        Assert.Equal(AnShengProbeStatus.NotProbed, profile.ProbeStatus);

        // 已进变更追踪，但尚未提交。
        Assert.Equal(EntityState.Added, _db.Entry(profile).State);
        Assert.Equal(0, await _db.AnShengDeviceProfiles.CountAsync());

        await _db.SaveChangesAsync();
        Assert.Equal(1, await _db.AnShengDeviceProfiles.CountAsync());
    }

    /// <summary>同一 IMEI 重复调用必须复用同一实例，不得建出第二份档案。</summary>
    [Fact]
    public async Task GetOrCreateAsync_Should_Be_Idempotent()
    {
        var first = await _service.GetOrCreateAsync(Imei, AppCode);
        await _db.SaveChangesAsync();

        var second = await _service.GetOrCreateAsync(Imei, AppCode);

        Assert.Same(first, second);
        Assert.Equal(1, await _db.AnShengDeviceProfiles.CountAsync());
    }

    /// <summary>IMEI 是档案的身份，空值直接拒绝 —— 建一份没有身份的档案毫无意义。</summary>
    [Fact]
    public async Task GetOrCreateAsync_Should_Reject_Empty_Imei()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.GetOrCreateAsync("", AppCode));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.GetOrCreateAsync("  ", AppCode));
    }

    // ─────────────────────────────────────────────────────────────
    // 三、ResolveKind：三级判据 + Manual 权威
    // ─────────────────────────────────────────────────────────────

    /// <summary>一级：本次显式带了人工品类，直接采信并标 Manual。</summary>
    [Fact]
    public void ResolveKind_Manual_Argument_Should_Win()
    {
        // 快照明明指向 4G 开关，人工却说是 4G 喇叭 —— 以人工为准。
        var snapshot = new AnShengCapabilitySnapshot(NetType: "4G", SlotAmount: 4);

        var (kind, source) = _service.ResolveKind(null, snapshot, AnShengDeviceKind.Speaker4G);

        Assert.Equal(AnShengDeviceKind.Speaker4G, kind);
        Assert.Equal(AnShengKindSource.Manual, source);
    }

    /// <summary>
    /// 一级：档案里已是人工指定值时，任何自动推断都不得改写。
    /// 这条守的是"运维手工纠正后又被自学习覆盖回去"这类事故。
    /// </summary>
    [Fact]
    public void ResolveKind_Existing_Manual_Should_Not_Be_Overwritten()
    {
        var profile = new AnShengDeviceProfile
        {
            Imei = Imei,
            Kind = AnShengDeviceKind.Speaker4G,
            KindSource = AnShengKindSource.Manual
        };

        var snapshot = new AnShengCapabilitySnapshot(NetType: "4G", SlotAmount: 8, Version: "SWITCH-X");

        var (kind, source) = _service.ResolveKind(profile, snapshot);

        Assert.Equal(AnShengDeviceKind.Speaker4G, kind);
        Assert.Equal(AnShengKindSource.Manual, source);
    }

    /// <summary>
    /// Manual 权威只对<b>非 Unknown</b> 生效。
    /// 档案来源标了 Manual 但值是 Unknown（历史脏数据），仍应让推断接管。
    /// </summary>
    [Fact]
    public void ResolveKind_Manual_Unknown_Should_Not_Block_Inference()
    {
        var profile = new AnShengDeviceProfile
        {
            Imei = Imei,
            Kind = AnShengDeviceKind.Unknown,
            KindSource = AnShengKindSource.Manual
        };

        var snapshot = new AnShengCapabilitySnapshot(NetType: "4G", SlotAmount: 4);

        var (kind, source) = _service.ResolveKind(profile, snapshot);

        Assert.Equal(AnShengDeviceKind.Switch4G, kind);
        Assert.Equal(AnShengKindSource.Probe, source);
    }

    /// <summary>二级：slotAmount 推断，来源标 Probe。</summary>
    [Theory]
    [InlineData("4G", 4, AnShengDeviceKind.Switch4G)]
    [InlineData("4G", 0, AnShengDeviceKind.Speaker4G)]
    [InlineData("WiFi", 2, AnShengDeviceKind.SwitchWiFi)]
    [InlineData("WiFi", 0, AnShengDeviceKind.SpeakerWiFi)]
    public void ResolveKind_Should_Infer_From_SlotAmount(
        string netType, int slotAmount, AnShengDeviceKind expected)
    {
        var snapshot = new AnShengCapabilitySnapshot(NetType: netType, SlotAmount: slotAmount);

        var (kind, source) = _service.ResolveKind(null, snapshot);

        Assert.Equal(expected, kind);
        Assert.Equal(AnShengKindSource.Probe, source);
    }

    /// <summary>
    /// 【验收 #3 · 服务层】slotAmount <b>未上报（null）</b>、且无版本线索时，仍须判出确定品类。
    ///
    /// 注意与上一条用例的区别：上面传的是 <c>slotAmount = 0</c>（设备<b>显式声明</b>没有插槽），
    /// 这里传的是 <c>null</c>（设备<b>压根没报</b>这个字段）。验收 #3 钉的是后者，
    /// 二者走 <c>InferKind</c> 里完全不同的分支（二级 vs 三级），此前只覆盖了 0，
    /// null 这条路径实际处于无人看守状态。
    /// </summary>
    [Theory]
    [InlineData("WiFi", AnShengDeviceKind.SpeakerWiFi)]
    [InlineData("4G", AnShengDeviceKind.Speaker4G)]
    public void ResolveKind_Should_Infer_Speaker_When_SlotAmount_Missing(
        string netType, AnShengDeviceKind expected)
    {
        var snapshot = new AnShengCapabilitySnapshot(NetType: netType, SlotAmount: null);

        var (kind, source) = _service.ResolveKind(null, snapshot);

        Assert.Equal(expected, kind);
        Assert.Equal(AnShengKindSource.Probe, source);
    }

    /// <summary>
    /// 快照字段缺失时回落到档案里的历史值 —— 上次探到的仍是有效事实。
    /// </summary>
    [Fact]
    public void ResolveKind_Should_Fall_Back_To_Profile_Fields()
    {
        var profile = new AnShengDeviceProfile
        {
            Imei = Imei,
            NetType = "4G",
            SlotAmount = 4
        };

        // 本次快照什么都没带。
        var (kind, source) = _service.ResolveKind(profile, AnShengCapabilitySnapshot.Empty);

        Assert.Equal(AnShengDeviceKind.Switch4G, kind);
        Assert.Equal(AnShengKindSource.Probe, source);
    }

    /// <summary>
    /// 推不出来 ≠ 要清空。已有判定结果与来源必须原样保留。
    /// </summary>
    [Fact]
    public void ResolveKind_Unknown_Inference_Should_Preserve_Existing()
    {
        var profile = new AnShengDeviceProfile
        {
            Imei = Imei,
            Kind = AnShengDeviceKind.SwitchWiFi,
            KindSource = AnShengKindSource.Uplink
        };

        var (kind, source) = _service.ResolveKind(profile, AnShengCapabilitySnapshot.Empty);

        Assert.Equal(AnShengDeviceKind.SwitchWiFi, kind);
        Assert.Equal(AnShengKindSource.Uplink, source);
    }

    /// <summary>无档案 + 无快照 ⇒ Unknown/Unknown，且不抛异常。</summary>
    [Fact]
    public void ResolveKind_Should_Tolerate_Null_Profile_And_Empty_Snapshot()
    {
        var (kind, source) = _service.ResolveKind(null, AnShengCapabilitySnapshot.Empty);

        Assert.Equal(AnShengDeviceKind.Unknown, kind);
        Assert.Equal(AnShengKindSource.Unknown, source);
    }

    // ─────────────────────────────────────────────────────────────
    // 四、ApplyProbeAsync：快照合并 + 静态字典同步
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 一次成功探测应写全能力字段、置 Probed、清空错因，并同步静态品类字典。
    /// 对应验收 #2。
    /// </summary>
    [Fact]
    public async Task ApplyProbeAsync_Should_Persist_Full_Snapshot()
    {
        var snapshot = new AnShengCapabilitySnapshot(
            NetType: "4G",
            SlotAmount: 4,
            PhaseAmount: 1,
            Version: "SWITCH-EC618X-R24-O-V4.0.8",
            Model: "Air780E",
            Iccid: "89860445102180123456",
            Signal: 24);

        var profile = await _service.ApplyProbeAsync(Imei, AppCode, snapshot, AnShengDeviceKind.Switch4G);
        await _db.SaveChangesAsync();

        Assert.Equal("4G", profile.NetType);
        Assert.Equal(4, profile.SlotAmount);
        Assert.Equal(1, profile.PhaseAmount);
        Assert.Equal("SWITCH-EC618X-R24-O-V4.0.8", profile.Version);
        Assert.Equal("Air780E", profile.Model);
        Assert.Equal("89860445102180123456", profile.Iccid);
        Assert.Equal(24, profile.Signal);

        Assert.Equal(AnShengDeviceKind.Switch4G, profile.Kind);
        Assert.Equal(AnShengKindSource.Manual, profile.KindSource);
        Assert.Equal(AnShengProbeStatus.Probed, profile.ProbeStatus);
        Assert.Null(profile.ProbeError);
        Assert.NotNull(profile.LastProbedAt);

        // 静态品类字典必须同步，否则下发侧仍按 Unknown 走保守策略。
        Assert.Equal(AnShengDeviceKind.Switch4G, AnShengMqttProtocolAdapter.GetDeviceKind(Imei));
    }

    /// <summary>
    /// 合并规则：<b>新的非空值才覆盖</b>。
    /// 第二段探测（getDevStatus）不带 version，绝不能把第一段写进去的 version 抹掉。
    /// </summary>
    [Fact]
    public async Task ApplyProbeAsync_Should_Not_Erase_With_Nulls()
    {
        await _service.ApplyProbeAsync(
            Imei, AppCode,
            new AnShengCapabilitySnapshot(
                NetType: "4G", SlotAmount: 4, Version: "SWITCH-V1", Iccid: "ICCID-1"),
            AnShengDeviceKind.Switch4G);
        await _db.SaveChangesAsync();

        // 第二次只带信号强度，其余字段为 null。
        var profile = await _service.ApplyProbeAsync(
            Imei, AppCode,
            new AnShengCapabilitySnapshot(Signal: 18),
            AnShengDeviceKind.Switch4G);
        await _db.SaveChangesAsync();

        Assert.Equal("4G", profile.NetType);
        Assert.Equal(4, profile.SlotAmount);
        Assert.Equal("SWITCH-V1", profile.Version);
        Assert.Equal("ICCID-1", profile.Iccid);
        Assert.Equal(18, profile.Signal);
    }

    /// <summary>
    /// 空白串同样视为"没探到"。固件对"没有"的表达并不统一：有的不带键，有的带 <c>""</c>。
    /// </summary>
    [Fact]
    public async Task ApplyProbeAsync_Should_Treat_Blank_String_As_Missing()
    {
        await _service.ApplyProbeAsync(
            Imei, AppCode,
            new AnShengCapabilitySnapshot(NetType: "4G", SlotAmount: 4, Version: "SWITCH-V1"),
            AnShengDeviceKind.Switch4G);
        await _db.SaveChangesAsync();

        var profile = await _service.ApplyProbeAsync(
            Imei, AppCode,
            new AnShengCapabilitySnapshot(Version: "   ", Iccid: ""),
            AnShengDeviceKind.Switch4G);

        Assert.Equal("SWITCH-V1", profile.Version);
        Assert.Null(profile.Iccid);
    }

    /// <summary>未指定人工品类时，探测结论来源标 Probe。</summary>
    [Fact]
    public async Task ApplyProbeAsync_Without_ManualKind_Should_Mark_Probe_Source()
    {
        var profile = await _service.ApplyProbeAsync(
            Imei, AppCode,
            new AnShengCapabilitySnapshot(NetType: "WiFi", SlotAmount: 2));

        Assert.Equal(AnShengDeviceKind.SwitchWiFi, profile.Kind);
        Assert.Equal(AnShengKindSource.Probe, profile.KindSource);
    }

    // ─────────────────────────────────────────────────────────────
    // 五、ApplyProbeFailureAsync：只记状态，不毁数据
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 探测失败只更新状态/错因/时间三个字段。
    /// 这次没探到不等于上次探到的是假的，清空只会让能力校验从"降级"退化成"全错"。
    /// </summary>
    [Fact]
    public async Task ApplyProbeFailureAsync_Should_Preserve_Known_Capabilities()
    {
        await _service.ApplyProbeAsync(
            Imei, AppCode,
            new AnShengCapabilitySnapshot(NetType: "4G", SlotAmount: 4, Version: "SWITCH-V1"),
            AnShengDeviceKind.Switch4G);
        await _db.SaveChangesAsync();

        var profile = await _service.ApplyProbeFailureAsync(Imei, AppCode, "设备 5000ms 内未应答 getDevInfo");
        await _db.SaveChangesAsync();

        Assert.Equal(AnShengProbeStatus.ProbeFailed, profile.ProbeStatus);
        Assert.Equal("设备 5000ms 内未应答 getDevInfo", profile.ProbeError);
        Assert.NotNull(profile.LastProbedAt);

        // 能力字段一个都不许动。
        Assert.Equal(AnShengDeviceKind.Switch4G, profile.Kind);
        Assert.Equal("4G", profile.NetType);
        Assert.Equal(4, profile.SlotAmount);
        Assert.Equal("SWITCH-V1", profile.Version);
    }

    /// <summary>从未探测过的设备探测失败，应新建一份只带失败状态的档案。</summary>
    [Fact]
    public async Task ApplyProbeFailureAsync_Should_Create_Profile_When_Absent()
    {
        var profile = await _service.ApplyProbeFailureAsync(Imei, AppCode, "适配器未连接");
        await _db.SaveChangesAsync();

        Assert.Equal(1, await _db.AnShengDeviceProfiles.CountAsync());
        Assert.Equal(AnShengProbeStatus.ProbeFailed, profile.ProbeStatus);
        Assert.Equal(AnShengDeviceKind.Unknown, profile.Kind);
    }

    /// <summary>超长错因必须截断到列宽 500，否则整条 SQL 会失败。</summary>
    [Fact]
    public async Task ApplyProbeFailureAsync_Should_Truncate_Long_Error()
    {
        var profile = await _service.ApplyProbeFailureAsync(Imei, AppCode, new string('x', 900));

        Assert.NotNull(profile.ProbeError);
        Assert.Equal(500, profile.ProbeError!.Length);
    }

    /// <summary>空错因归一成 null，避免库里出现无意义的空白串。</summary>
    [Fact]
    public async Task ApplyProbeFailureAsync_Should_Normalize_Blank_Error()
    {
        var profile = await _service.ApplyProbeFailureAsync(Imei, AppCode, "   ");
        Assert.Null(profile.ProbeError);
    }

    // ─────────────────────────────────────────────────────────────
    // 六、RefreshAsync：上行自学习不得越权
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 已认领设备的首次上行刷新，来源标 Uplink。
    ///
    /// 【T6 决策 A 后的前置条件变化】
    ///   <c>RefreshAsync</c> 不再隐式建档，因此本用例必须先用 <c>GetOrCreateAsync</c>
    ///   模拟"认领已完成、档案已存在"的状态，否则拿到的是 null 而不是 Uplink 档案。
    ///   这不是测试将就实现 —— 未认领设备本来就不该有档案，见
    ///   <see cref="RefreshAsync_Should_Return_Null_When_Profile_Missing"/>。
    /// </summary>
    [Fact]
    public async Task RefreshAsync_Should_Mark_Uplink_Source()
    {
        // 前置：认领流程已建档（此时 Kind=Unknown、KindSource=Unknown）。
        await _service.GetOrCreateAsync(Imei, AppCode);
        await _db.SaveChangesAsync();

        var profile = await _service.RefreshAsync(
            Imei, AppCode, new AnShengCapabilitySnapshot(NetType: "4G", SlotAmount: 4));

        Assert.NotNull(profile);
        Assert.Equal(AnShengDeviceKind.Switch4G, profile!.Kind);
        Assert.Equal(AnShengKindSource.Uplink, profile.KindSource);
    }

    /// <summary>
    /// 【T6 决策 A】档案不存在时，<c>RefreshAsync</c> 必须返回 <c>null</c> 且<b>绝不建档</b>。
    ///
    /// 【为什么这条是红线】
    ///   上行报文只带 <c>netType</c>/<c>slotAmount</c>，档案 Kind 只能靠三级前缀猜测。
    ///   一旦猜错并落库，<c>SyncDeviceKindCache</c> 会把错误品类推进适配器静态字典，
    ///   <c>AnShengCommandCatalog.GroupSwitchAction</c> 会**确定地**拦掉 10 条下发命令 ——
    ///   比 <c>Unknown</c>（fail-open 放行一切）严格更糟。
    ///   因此档案的唯一创建入口收敛为认领流程（强制 getDevInfo + getDevStatus 硬事实）。
    ///   详见 t5-profile-system-design.md §605-630 与 t6-event-pipeline-design.md §8.1。
    /// </summary>
    [Fact]
    public async Task RefreshAsync_Should_Return_Null_When_Profile_Missing()
    {
        var profile = await _service.RefreshAsync(
            Imei, AppCode, new AnShengCapabilitySnapshot(NetType: "4G", SlotAmount: 4));

        Assert.Null(profile);

        // 关键断言：不仅返回 null，连一行"孤儿档案"都不许留下。
        Assert.Equal(0, await _db.AnShengDeviceProfiles.CountAsync());

        // 连带断言：也不许把猜测出来的品类推进适配器静态字典。
        Assert.Equal(AnShengDeviceKind.Unknown, AnShengMqttProtocolAdapter.GetDeviceKind(Imei));
    }

    /// <summary>
    /// 上行的可信度低于探测：已是 Probe 来源的档案，刷新后来源<b>不得降级</b>为 Uplink。
    /// </summary>
    [Fact]
    public async Task RefreshAsync_Should_Not_Downgrade_Probe_Source()
    {
        await _service.ApplyProbeAsync(
            Imei, AppCode, new AnShengCapabilitySnapshot(NetType: "4G", SlotAmount: 4));
        await _db.SaveChangesAsync();

        var profile = await _service.RefreshAsync(
            Imei, AppCode, new AnShengCapabilitySnapshot(NetType: "4G", SlotAmount: 4));

        Assert.NotNull(profile);
        Assert.Equal(AnShengKindSource.Probe, profile!.KindSource);
    }

    /// <summary>Manual 是最高权威，上行自学习永远改不动它。</summary>
    [Fact]
    public async Task RefreshAsync_Should_Never_Override_Manual()
    {
        await _service.ApplyProbeAsync(
            Imei, AppCode,
            new AnShengCapabilitySnapshot(NetType: "4G", SlotAmount: 0),
            AnShengDeviceKind.Speaker4G);
        await _db.SaveChangesAsync();

        // 上行报了 4 个插槽，指向开关款 —— 但人工已判定为喇叭款。
        var profile = await _service.RefreshAsync(
            Imei, AppCode, new AnShengCapabilitySnapshot(NetType: "4G", SlotAmount: 4));

        Assert.NotNull(profile);
        Assert.Equal(AnShengDeviceKind.Speaker4G, profile!.Kind);
        Assert.Equal(AnShengKindSource.Manual, profile.KindSource);

        // 能力字段仍如实记录设备自报值 —— 品类是结论，slotAmount 是事实，两者互不干扰。
        Assert.Equal(4, profile.SlotAmount);
    }

    // ─────────────────────────────────────────────────────────────
    // 七、AttachDevice
    // ─────────────────────────────────────────────────────────────

    /// <summary>挂设备后应能按 DeviceId 反查到档案。</summary>
    [Fact]
    public async Task AttachDevice_Should_Make_Profile_Queryable_By_DeviceId()
    {
        var profile = await _service.GetOrCreateAsync(Imei, AppCode);
        _service.AttachDevice(profile, 42);
        await _db.SaveChangesAsync();

        var found = await _service.GetByDeviceIdAsync(42);

        Assert.NotNull(found);
        Assert.Equal(Imei, found!.Imei);
    }

    /// <summary>非法入参必须当场拒绝，避免把脏关联写进库里。</summary>
    [Fact]
    public async Task AttachDevice_Should_Reject_Invalid_Input()
    {
        var profile = await _service.GetOrCreateAsync(Imei, AppCode);

        Assert.Throws<ArgumentNullException>(() => _service.AttachDevice(null!, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => _service.AttachDevice(profile, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => _service.AttachDevice(profile, -5));
    }

    // ─────────────────────────────────────────────────────────────
    // 八、AnShengCapabilitySnapshot 的双源归一
    // ─────────────────────────────────────────────────────────────

    /// <summary><c>getDevInfo</c> 的字段应完整落进快照，含 T5 新增的 <c>iccid</c>。</summary>
    [Fact]
    public void Snapshot_FromDevInfo_Should_Map_All_Fields()
    {
        var snapshot = AnShengCapabilitySnapshot.FromDevInfo(new AnShengDevInfo
        {
            NetType = "4G",
            SlotAmount = 4,
            PhaseAmount = 1,
            Version = "SWITCH-V1",
            Model = "Air780E",
            Iccid = "ICCID-1"
        });

        Assert.Equal("4G", snapshot.NetType);
        Assert.Equal(4, snapshot.SlotAmount);
        Assert.Equal(1, snapshot.PhaseAmount);
        Assert.Equal("SWITCH-V1", snapshot.Version);
        Assert.Equal("Air780E", snapshot.Model);
        Assert.Equal("ICCID-1", snapshot.Iccid);
    }

    /// <summary><c>getDevStatus</c> 的字段应完整落进快照，含 T5 新增的 <c>slotAmount</c>。</summary>
    [Fact]
    public void Snapshot_FromDevStatus_Should_Prefer_Explicit_SlotAmount()
    {
        var snapshot = AnShengCapabilitySnapshot.FromDevStatus(new AnShengDevStatus
        {
            NetType = "4G",
            SlotAmount = 4,
            Version = "SWITCH-V1",
            Iccid = "ICCID-1",
            Signal = 24
        });

        Assert.Equal(4, snapshot.SlotAmount);
        Assert.Equal("ICCID-1", snapshot.Iccid);
        Assert.Equal(24, snapshot.Signal);
    }

    /// <summary>
    /// 显式 <c>slotAmount</c> 缺失时退而用数组长度；但长度为 0 视为"没探到"而非"确认 0 路"——
    /// 设备可能只是这一帧没带数组，报 0 会把开关款误判成喇叭款。
    /// </summary>
    [Fact]
    public void Snapshot_FromDevStatus_Should_Not_Treat_Empty_Array_As_Zero()
    {
        var snapshot = AnShengCapabilitySnapshot.FromDevStatus(new AnShengDevStatus { NetType = "4G" });

        Assert.Null(snapshot.SlotAmount);
    }

    /// <summary>入参为 null 时返回空快照，不抛异常。</summary>
    [Fact]
    public void Snapshot_Should_Tolerate_Null_Input()
    {
        Assert.Same(AnShengCapabilitySnapshot.Empty, AnShengCapabilitySnapshot.FromDevInfo(null));
        Assert.Same(AnShengCapabilitySnapshot.Empty, AnShengCapabilitySnapshot.FromDevStatus(null));
    }
}
