using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using IoTPlatform.Configuration;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.Infrastructure.Protocol.AnSheng;
using IoTPlatform.IntegrationTests.Infrastructure;
using IoTPlatform.IntegrationTests.Infrastructure.Auth;
using IoTPlatform.Models;
using IoTPlatform.Services;
using IoTPlatform.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace IoTPlatform.IntegrationTests.AnSheng;

/// <summary>
/// T7「命令服务重构」验收 <b>#5（超时闭环 + 在途表无增长）</b> 与
/// <b>#6（<c>GET /catalog</c> 返回 36 条）</b> 的集成验收。
///
/// ═══════════════════════════════════════════════════════════════════════
/// 【为什么这两条必须是集成测试】
/// ═══════════════════════════════════════════════════════════════════════
/// 单测层已经把「决策是否正确」锁死了（<c>AnShengPendingCommandStoreTests</c> 覆盖
/// 同 frameId 隔离、1000 条注册-清扫无残留、TCS 取消语义；<c>AnShengCatalogCountTests</c>
/// 覆盖 36 条规格）。但单测跑的是 <c>new</c> 出来的裸对象，覆盖不了 T7 真正的高危面：
///   · <c>AnShengCommandSweepHostedService</c> 是 <b>HostedService</b>——
///     只要 <c>Program.cs</c> 少一行 <c>AddHostedService</c>，超时兜底就<b>整条静默失效</b>：
///     命令永远停在 <c>Sent</c>，不报错、不抛异常，单测永远发现不了；
///   · 清扫跑在后台作用域里，<c>ITenantContextAccessor.Current</c> 为 null，
///     回填 <c>AnShengCommandRecord</c> 时要么被全局租户过滤器筛成空集（改不到行）、
///     要么写出空 <c>AppCode</c>——两种都只有连着真实 MySQL 才暴露；
///   · <c>GET /catalog</c> 的「36 条」是<b>端点契约</b>，不是数据结构。
///     Catalog 里有 36 条 ≠ 端点能吐出 36 条（序列化、租户过滤、权限特性都可能截断）。
///
/// ═══════════════════════════════════════════════════════════════════════
/// 【等待策略：不用 Thread.Sleep，也不用固定时长 Task.Delay 断言】
/// ═══════════════════════════════════════════════════════════════════════
/// 超时是「时间到了才发生」的行为，等待本身是被测语义的一部分，无法用完成信号替代。
/// 但我们不睡一个拍脑袋的固定时长，而是<b>轮询驱动</b>：
/// 每 100ms 主动触发一轮 <c>SweepOnceAsync()</c>，直到它真的扫出条目或超上限。
/// 这样 TTL 稍有抖动也不会偶发红，机器快时又能立刻返回。
/// TTL 已由 <c>appsettings.Testing.json</c> 的 <c>AnSheng:Command:DefaultTimeoutSeconds=1</c>
/// 压到 1 秒（生产 30 秒会让本用例白等半分钟），<c>SweepEnabled=false</c> 关掉后台线程
/// 以杜绝「后台清扫与断言抢同一行」的竞态。
///
/// ═══════════════════════════════════════════════════════════════════════
/// 【为什么用 JsonElement 而不是强类型 DTO 解 catalog】
/// ═══════════════════════════════════════════════════════════════════════
/// 验收 #6 的契约是「字段名 + 条数」，不是「某个 C# 类型」。用 <c>JsonElement</c> 断言
/// 实际序列化出来的 JSON，才能真正守住前端看到的形状；换成强类型 DTO 会把
/// 「字段拼错 / 被 JsonIgnore 掉」这类最容易犯的错整个吞掉。
/// </summary>
[Collection(SharedTestConstants.CollectionName)]
public sealed class AnShengCommandAcceptanceTests : IntegrationTestBase
{
    /// <summary>Catalog 端点路径（设计 §7 T7-5）。</summary>
    private const string CatalogUrl = "/api/v1/ansheng/catalog";

    /// <summary>轮询触发清扫的单次间隔。</summary>
    private static readonly TimeSpan SweepPollInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>轮询等待超时被扫出的上限。TTL=1s，给到 15s 余量足以吸收真实 MySQL 的首次建连抖动。</summary>
    private static readonly TimeSpan SweepPollTimeout = TimeSpan.FromSeconds(15);

    public AnShengCommandAcceptanceTests(DatabaseFixture fixture) : base(fixture)
    {
    }

    // ══════════════════════════════════════════════════════════════════
    // 验收 #6：GET /catalog 返回 36 条
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 验收 #6：<c>GET /api/v1/ansheng/catalog</c>（<b>不传 deviceId</b>）返回全部 36 条规格。
    ///
    /// 「不传 deviceId 才是 36」是设计 §8-B 的硬判据：Catalog 必须是<b>纯静态数据</b>，
    /// 一旦 spec 依赖 <c>Profile</c>，无设备查询就无法序列化，本条验收直接破功。
    /// </summary>
    [Fact(DisplayName = "验收#6 GET /catalog 不传 deviceId → 36 条，且字段齐全")]
    public async Task GetCatalog_Returns36Items()
    {
        // Arrange
        var client = Client.AsAdmin();

        // Act
        var response = await client.GetAsync(CatalogUrl);

        // Assert —— HTTP 层
        response.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "catalog 是只读静态目录，任何已认证用户都应能取到；" +
            "500 通常意味着 DI 未接线，404 意味着 T7-5 的端点还没暴露");

        var raw = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;

        root.TryGetProperty("code", out var code).Should().BeTrue("全站 ApiResponse 约定必须有 code");
        code.GetInt32().Should().Be(200, $"catalog 查询不应失败，实际响应：{Truncate(raw)}");

        root.TryGetProperty("data", out var data).Should().BeTrue("ApiResponse.data 必须存在");
        data.ValueKind.Should().Be(JsonValueKind.Array, "catalog 是数组");

        var items = data.EnumerateArray().ToArray();
        items.Should().HaveCount(
            AnShengCommandCatalog.Count,
            "端点必须原样吐出 Catalog 的全部规格；" +
            $"Catalog 侧已由单测 Catalog_Has36Specs 锁死为 {AnShengCommandCatalog.Count} 条");
        items.Should().HaveCount(36, "验收 #6 的字面判据：36 条");

        // Assert —— 字段齐全。逐条全查而不是抽样：抽样只能证明「大部分对」，
        //           而字段缺失往往只发生在某一两条特殊规格上（无参命令、纯事件方法），
        //           恰恰是抽样最容易漏掉的那几条。
        foreach (var item in items)
        {
            HasPropertyIgnoreCase(item, "method").Should().BeTrue($"每项必须有 method，实际：{item}");
            HasPropertyIgnoreCase(item, "supportedKinds").Should().BeTrue($"每项必须有 supportedKinds，实际：{item}");
            HasPropertyIgnoreCase(item, "params").Should().BeTrue($"每项必须有 params，实际：{item}");
            HasPropertyIgnoreCase(item, "isEvent").Should().BeTrue($"每项必须有 isEvent 标志位，实际：{item}");

            TryGetPropertyIgnoreCase(item, "method", out var m);
            m.GetString().Should().NotBeNullOrWhiteSpace("method 不得为空串");
        }

        // Assert —— 事件方法的 isEvent 必须<b>一条不漏</b>地为 true。
        //           只断言「>0 条被标注」会让「36 条里只标对了 1 条」也通过，
        //           而漏标的那几条前端会当成可下发命令渲染出按钮，点下去必然被 Guard 拒绝。
        var expectedEventCount = AnShengCommandCatalog.ListAll().Count(s => s.IsEvent);
        expectedEventCount.Should().BeGreaterThan(0, "目录里本就应当存在事件方法，否则本断言失去意义");

        var eventFlagged = items.Count(i =>
            TryGetPropertyIgnoreCase(i, "isEvent", out var f) && f.ValueKind == JsonValueKind.True);
        eventFlagged.Should().Be(
            expectedEventCount,
            "事件方法必须逐条以 isEvent=true 标注，否则前端无法区分「能下发」与「只会上行」；" +
            $"目录侧共有 {expectedEventCount} 条事件规格，端点只吐出 {eventFlagged} 条标注");
    }

    /// <summary>
    /// 验收 #6 变体：<c>GET /catalog?deviceId={id}</c> 是<b>过滤视图</b>——
    /// 按该设备 Kind 计算，返回条数 ≤ 36，且每条的 <c>supportedKinds</c> 都得包含该设备品类。
    ///
    /// 与上一条分开断言的原因：两者语义不同，混在一起会让「过滤没生效」伪装成通过。
    /// </summary>
    [Fact(DisplayName = "验收#6变体 GET /catalog?deviceId → 按 Kind 过滤，条数 ≤36")]
    public async Task GetCatalog_WithDeviceId_FiltersByKind()
    {
        // Arrange —— 给基线设备补一份 Switch4G 档案（D7：品类必须显式落档，否则降级放行）
        await SeedProfileAsync(Seed.DeviceId, Seed.Imei, AnShengDeviceKind.Switch4G, slotAmount: 4, version: "V4.0.20");

        var client = Client.AsAdmin();

        // Act
        var response = await client.GetAsync($"{CatalogUrl}?deviceId={Seed.DeviceId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var raw = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;

        root.GetProperty("code").GetInt32().Should().Be(200, $"实际响应：{Truncate(raw)}");

        var items = root.GetProperty("data").EnumerateArray().ToArray();
        items.Should().NotBeEmpty("Switch4G 至少支持通用组与开关组的方法");
        items.Length.Should().BeLessThanOrEqualTo(
            36, "过滤视图只会变少不会变多；等于 36 说明 deviceId 参数被忽略了");

        foreach (var item in items)
        {
            TryGetPropertyIgnoreCase(item, "supportedKinds", out var kinds).Should().BeTrue();

            // supportedKinds 既可能序列化成字符串数组，也可能是 flags 数值，两种都接受，
            // 但必须能证明「包含 Switch4G」。
            var mentionsSwitch4G = kinds.ValueKind switch
            {
                JsonValueKind.Array => kinds.EnumerateArray().Any(k =>
                    k.ValueKind == JsonValueKind.String &&
                    k.GetString()!.Contains("Switch4G", StringComparison.OrdinalIgnoreCase)),
                JsonValueKind.String => kinds.GetString()!.Contains("Switch4G", StringComparison.OrdinalIgnoreCase),
                JsonValueKind.Number => (kinds.GetInt32() & (int)AnShengDeviceKind.Switch4G) != 0,
                _ => false
            };

            mentionsSwitch4G.Should().BeTrue(
                $"过滤视图里的每条规格都必须支持 Switch4G，越界项：{item}");
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // 验收 #5：超时置终态 + 在途表清空
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 验收 #5：命令下发后设备<b>不回应答</b>，超过 TTL 由清扫置 <c>Timeout</c> 终态，
    /// 且在途表被摘干净。
    ///
    /// 断言四件套（设计 §9.2）：
    /// <c>Status=Timeout</c> && <c>CompletedAt != null</c> && <c>ErrorCode=="TIMEOUT"</c> && <c>Store.Count==0</c>。
    /// 其中 <c>Store.Count==0</c> 是「内存无增长」的直接证据——在途表是本期唯一的进程内累积点，
    /// 它归零就意味着没有条目泄漏（1000 条量级的压力证据由单测
    /// <c>RegisterThenSweep_ThousandTimes_LeavesNoResidue</c> 承担，集成层不重复烧 CI 时间）。
    /// </summary>
    [Fact(DisplayName = "验收#5 无应答超时 → Record=Timeout/ErrorCode=TIMEOUT/在途表归零")]
    public async Task Timeout_MarksRecordTimeout_AndClearsStore()
    {
        // Arrange —— 刻意不挂 AutoReplyUplink：本用例的被测行为就是「设备没回话」
        var client = Client.AsAdmin();
        Adapter.Sent.Should().BeEmpty("每个用例开始前 IntegrationTestBase 都会 Reset 适配器");

        var store = Fixture.Factory.Services.GetRequiredService<IAnShengPendingCommandStore>();
        var options = Fixture.Factory.Services
            .GetRequiredService<IOptions<AnShengCommandOptions>>().Value;

        options.SweepEnabled.Should().BeFalse(
            "集成测试必须关掉后台清扫线程（appsettings.Testing.json: AnSheng:Command:SweepEnabled=false），" +
            "否则它会与本用例的手工清扫抢同一行，断言变成掷骰子");
        options.EffectiveDefaultTimeoutSeconds.Should().BeLessThanOrEqualTo(
            2, "TTL 必须压短（DefaultTimeoutSeconds=1），生产 30s 会让本用例白等半分钟");

        var sweeper = ResolveSweeper();

        // Act 1 —— 下发一条通用命令（getDevInfo 属 GroupCommon，任何品类都放行，
        //          让本用例专注超时语义，不与 Guard 的品类判定纠缠）
        var response = await client.PostAsJsonAsync(
            $"/api/v1/ansheng/{Seed.DeviceId}/command",
            new AnShengCommandRequest { Method = "getDevInfo" });

        response.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "下发本身应当成功（超时发生在之后）；500 通常意味着 AnShengCommandService 的依赖没在 Program.cs 注册");

        var sentBody = await response.Content.ReadAsStringAsync();
        using var sentDoc = JsonDocument.Parse(sentBody);
        var commandId = sentDoc.RootElement
            .GetProperty("data")
            .GetProperty("commandId").GetString();

        commandId.Should().NotBeNullOrWhiteSpace($"CommandId 必须在最早期就确定，实际响应：{Truncate(sentBody)}");
        Adapter.Sent.Should().HaveCount(1, "命令必须真的出网，才谈得上「等应答超时」");
        store.Count.Should().Be(1, "先登记后下发（硬约束 N1）：下发返回时在途条目必须已在表里");

        // Act 2 —— 轮询触发清扫，直到它真的扫出条目（不睡固定时长）
        var swept = await PollUntilSweptAsync(sweeper);

        // Assert
        swept.Should().BeGreaterThan(
            0,
            $"TTL={options.EffectiveDefaultTimeoutSeconds}s 早已过，清扫必须扫出条目；" +
            "扫不出说明在途条目的 ExpiresAt 没按 ResolveTtl 计算，或条目压根没登记");

        store.Count.Should().Be(
            0, "超时条目必须从在途表摘除——这是「内存无增长」的直接证据");

        var record = await QueryDbAsync(db => db.AnShengCommandRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.CommandId == commandId));

        record.Should().NotBeNull("命令记录必须落库，否则「命令去哪了」无从追溯");
        record!.Status.Should().Be(
            AnShengCommandStatus.Timeout, "无应答的终态是 Timeout，不是 Failed，也不能停在 Sent");
        record.CompletedAt.Should().NotBeNull("终态必须带完成时刻，否则无法算时延、无法做保留期清理");
        record.ErrorCode.Should().Be(
            AnShengCommandSweepHostedService.TimeoutErrorCode,
            "错误码必须是机器可读的 TIMEOUT，前端靠它区分「设备没回」与「业务失败」");
        record.AppCode.Should().Be(
            SharedTestConstants.AppCode,
            "清扫跑在后台作用域、TenantContext 为 null，AppCode 必须由写入方显式带上；" +
            "空租户码会让这条记录在任何租户视图里都查不到");
    }

    /// <summary>
    /// 验收 #5 的规模侧证据：连发多条命令且全部无应答，一轮清扫后在途表必须<b>整体归零</b>，
    /// 且每条记录都独立拿到 <c>Timeout</c> 终态（不能只回填第一条）。
    ///
    /// 这条守的是「批量回填」最容易犯的两个错：
    ///   ① 清扫里 <c>FirstOrDefault</c> 只改一行就 <c>SaveChanges</c>；
    ///   ② 用 <c>foreach</c> 改多行却在循环里逐条 <c>SaveChanges</c>，中途抛异常留下半截状态。
    /// </summary>
    [Fact(DisplayName = "验收#5 批量无应答 → 一轮清扫后在途表归零且逐条置 Timeout")]
    public async Task Timeout_ManyCommands_AllMarkedAndStoreDrainsToZero()
    {
        // Arrange
        const int batchSize = 12;
        var client = Client.AsAdmin();
        var store = Fixture.Factory.Services.GetRequiredService<IAnShengPendingCommandStore>();
        var sweeper = ResolveSweeper();

        var commandIds = new List<string>(batchSize);

        // Act 1 —— 连发 batchSize 条，全都不回应答
        for (var i = 0; i < batchSize; i++)
        {
            var response = await client.PostAsJsonAsync(
                $"/api/v1/ansheng/{Seed.DeviceId}/command",
                new AnShengCommandRequest { Method = "getDevInfo" });

            response.StatusCode.Should().Be(HttpStatusCode.OK, $"第 {i + 1} 条下发失败");

            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            commandIds.Add(doc.RootElement.GetProperty("data").GetProperty("commandId").GetString()!);
        }

        store.Count.Should().Be(
            batchSize, "frameId 含递增序号 + 随机数，同设备连发 12 条不应互相顶掉");

        // Act 2 —— 轮询清扫，直到在途表归零
        await PollUntilSweptAsync(sweeper, untilStoreEmpty: store);

        // Assert
        store.Count.Should().Be(0, "全部超时条目必须被摘除，一条不留");

        var records = await QueryDbAsync(db => db.AnShengCommandRecords
            .AsNoTracking()
            .Where(r => commandIds.Contains(r.CommandId))
            .ToListAsync());

        records.Should().HaveCount(batchSize, "每条命令都要有独立记录");
        records.Should().OnlyContain(
            r => r.Status == AnShengCommandStatus.Timeout,
            "逐条回填，不能只改第一行；停在 Sent 的行说明批量回填漏了");
        records.Should().OnlyContain(r => r.CompletedAt != null);
        records.Should().OnlyContain(
            r => r.ErrorCode == AnShengCommandSweepHostedService.TimeoutErrorCode);
    }

    // ══════════════════════════════════════════════════════════════════
    // Helpers
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 从 DI 取清扫宿主。
    ///
    /// 刻意走 <c>IHostedService</c> 集合而不是 <c>GetRequiredService</c>：
    /// 设计要求它以 <c>AddHostedService</c> 注册，只有从宿主集合里能捞到，
    /// 才证明「后台超时兜底真的会跑」。缺注册时给出的失败文案直接指向修复位置。
    /// </summary>
    private AnShengCommandSweepHostedService ResolveSweeper()
    {
        var sweeper = Fixture.Factory.Services
            .GetServices<IHostedService>()
            .OfType<AnShengCommandSweepHostedService>()
            .FirstOrDefault();

        sweeper.Should().NotBeNull(
            "AnShengCommandSweepHostedService 必须在 Program.cs 以 AddHostedService<>() 注册（设计 §7）；" +
            "缺了它超时兜底整条静默失效——命令会永远停在 Sent，不报错也不抛异常");

        return sweeper!;
    }

    /// <summary>
    /// 轮询触发清扫，直到扫出条目（或在途表归零），返回累计扫出条数。
    /// 用轮询而非固定 <c>Task.Delay</c>：TTL 抖动不会偶发红，机器快时立刻返回。
    /// </summary>
    /// <param name="sweeper">清扫宿主。</param>
    /// <param name="untilStoreEmpty">给定时，改为「等到在途表归零」才停；否则「扫出任意条目」即停。</param>
    private static async Task<int> PollUntilSweptAsync(
        AnShengCommandSweepHostedService sweeper,
        IAnShengPendingCommandStore? untilStoreEmpty = null)
    {
        var deadline = DateTime.UtcNow + SweepPollTimeout;
        var total = 0;

        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(SweepPollInterval);
            total += await sweeper.SweepOnceAsync();

            if (untilStoreEmpty != null)
            {
                if (untilStoreEmpty.Count == 0 && total > 0) return total;
            }
            else if (total > 0)
            {
                return total;
            }
        }

        return total;
    }

    /// <summary>插入一条设备能力档案（D7：品类必须显式落档，否则 Guard 走降级放行）。</summary>
    private Task SeedProfileAsync(
        long deviceId, string imei, AnShengDeviceKind kind, int slotAmount, string version)
        => ExecuteDbAsync(async db =>
        {
            db.AnShengDeviceProfiles.Add(new AnShengDeviceProfile
            {
                // AppCode 必须显式赋值：播种走 DI 作用域直连 DbContext，
                // 此时 TenantContext 为空，全局过滤器不会代填。
                AppCode = SharedTestConstants.AppCode,
                Imei = imei,
                DeviceId = deviceId,
                Kind = kind,
                KindSource = AnShengKindSource.Manual,
                SlotAmount = slotAmount,
                Version = version,
                ProbeStatus = AnShengProbeStatus.Probed
            });
            await db.SaveChangesAsync();
        });

    private static bool HasPropertyIgnoreCase(JsonElement element, string name)
        => TryGetPropertyIgnoreCase(element, name, out _);

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        foreach (var prop in element.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string Truncate(string s) => s.Length <= 800 ? s : s[..800] + "…";
}
