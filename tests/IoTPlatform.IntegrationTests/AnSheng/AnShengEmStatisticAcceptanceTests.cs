using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Helpers;
using IoTPlatform.Infrastructure.Protocol.AnSheng;
using IoTPlatform.IntegrationTests.Infrastructure;
using IoTPlatform.IntegrationTests.Infrastructure.Auth;
using IoTPlatform.IntegrationTests.Infrastructure.Mqtt;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IoTPlatform.IntegrationTests.AnSheng;

/// <summary>
/// T11「安圣电量计实时、统计与校准（仅后端）」逐条验收（设计文档 Phase 4 + 决策 D5 的 6 条验收标准）。
///
/// 复用 T8/T10 的验收范式（<c>AnShengSwitchAcceptanceTests</c> / <c>AnShengTimeTaskAcceptanceTests</c>）：
/// TestServer + 真实 MySQL 一次性 schema + RecordingAnShengAdapter + AnShengUplinkPipeline 静态总线，
/// 不新增任何基础设施。
///
/// ═════════════════════════════════════════════════════════════════════════
/// 【验收落点】
/// ═════════════════════════════════════════════════════════════════════════
///   ① 唯一键幂等：重复 <c>getEMStatistics</c> 应答按 (DeviceId, SlotNum, Granularity, PeriodKey)
///      UPSERT，行数不随重复上报增长，Kwh 被后到的真值覆盖；
///   ② hourSumData 48 项：仅 <c>hourSumLength==48</c> 才产出 HourSum 点，
///      PeriodKey 为 <c>00:00</c>~<c>23:30</c> 连续 48 个；长度不符则一个点都不产出；
///   ③ 无空洞行：48 个半小时槽位无缺漏无越界；而稀疏的 dayData <b>不得</b>被补齐成假 0 行；
///   ④ 清零后数据保留 + EmCleared：<c>clearEMStatistics</c> 后聚合行一行不删，
///      追加 <see cref="AnShengEventKind.EmCleared"/>(=8) 标记事件，后续刷新仍可写回；
///   ⑤ 实时 → DeviceDataRecord：<c>getEMRealtime</c> 应答经 <c>ProcessDeviceDataAsync</c>
///      真正落 <c>DeviceDataRecord</c>（断言落库，不是内存镜像）；
///   ⑥ 校准仅开关类：4 个校准命令对喇叭类结构性拒绝零出网，对开关类放行且报文与 Builder 一致。
///
/// ═════════════════════════════════════════════════════════════════════════
/// 【铁律复核点（顺带钉死）】
/// ═════════════════════════════════════════════════════════════════════════
///   铁律①：统计 UPSERT / 清零标记 / 实时落库跑在后台作用域，行的 AppCode 必须显式落对；
///   铁律②：业务拒绝一律 HTTP 200 + ApiResponse.Code=400，零裸 400；
///   铁律③：出网报文与 <see cref="AnShengCommandBuilder"/> 的 T11 七方法字节级一致；
///   铁律④：<c>granularity</c> / <c>rejectReason</c> 以枚举<b>字符串</b>出网（全局 JsonStringEnumConverter）。
/// </summary>
[Collection(SharedTestConstants.CollectionName)]
public sealed class AnShengEmStatisticAcceptanceTests : IntegrationTestBase
{
    /// <summary>管道排空等待上限。真实 MySQL 首次建连 + EF 首次编译查询可能慢，给足余量。</summary>
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(15);

    /// <summary>安圣协议方法名：获取电量计实时信息。</summary>
    private const string MethodGetEMRealtime = "getEMRealtime";

    /// <summary>安圣协议方法名：获取电量计统计信息。</summary>
    private const string MethodGetEMStatistics = "getEMStatistics";

    /// <summary>安圣协议方法名：清空电量计统计信息。</summary>
    private const string MethodClearEMStatistics = "clearEMStatistics";

    /// <summary>安圣协议方法名：获取校准参数。</summary>
    private const string MethodGetCalParams = "getCalParams";

    /// <summary>安圣协议方法名：设置校准参数。</summary>
    private const string MethodSetCalParams = "setCalParams";

    /// <summary>安圣协议方法名：重置校准参数。</summary>
    private const string MethodResetCalParams = "resetCalParams";

    /// <summary>安圣协议方法名：自动校准。</summary>
    private const string MethodAutoCal = "autoCal";

    /// <summary>拒绝态业务码（设计 §8.3：HTTP 恒 200，业务码 400）。</summary>
    private const int RejectedCode = 400;

    /// <summary>
    /// 测试设备插槽数。<b>必须与应答 data[] 长度一致</b>——
    /// 服务层按 §7-R8 校验长度，不符会整帧拒收（插槽号按下标推导，长度不符必然错位）。
    /// </summary>
    private const int SlotAmount = 2;

    public AnShengEmStatisticAcceptanceTests(DatabaseFixture fixture) : base(fixture)
    {
    }

    /// <summary>
    /// 用例结束后再清一次进程级静态状态：本文件的用例会往在途表登记 frameId
    /// （getEMStatistics / getEMRealtime 应答关联），残留会污染后续用例的路由判定。
    /// </summary>
    public override Task DisposeAsync()
    {
        StaticStateResetter.ResetAll(Fixture.Factory.Services);
        return base.DisposeAsync();
    }

    // ─────────────────────────────────────────────────────────────
    // 验收 ①：唯一键幂等（重复上报 UPSERT，不插重复行）
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 验收 ①：连续三次 <c>getEMStatistics</c> 应答携带<b>相同周期键</b>时，
    /// 聚合表必须按 (DeviceId, SlotNum, Granularity, PeriodKey) UPSERT ——
    /// 行数恒定，Kwh 被最后一次真值覆盖。
    ///
    /// 【为什么这条最要命】getEMStatistics 是<b>全量快照式</b>返回（dayData 保留最近 30 条、
    /// monthData 最近 12 条），每拉一次就重复一次。没有唯一键去重，刷 10 次表就胀 10 倍，
    /// 而且每一行看上去都「合法」，直到有人拿它去算电费。
    ///
    /// 顺带复核铁律①（后台作用域写回的 AppCode）与铁律③（出网报文对齐 Builder）。
    /// </summary>
    [Fact(DisplayName = "验收① 重复 getEMStatistics 应答 → 按唯一键 UPSERT，行数不增长且 Kwh 被覆盖")]
    public async Task RepeatedStatistics_UpsertsByUniqueKey_RowCountStable()
    {
        await SeedProfileAsync(AnShengDeviceKind.Switch4G, SlotAmount);

        // ── 第 1 轮：建立基线（slot1 = 1 total + 3 day + 1 month，slot2 = 1 total，共 6 行）──
        var (firstSent, firstResult) = await RefreshStatisticsAsync();

        // 铁律③：拉取统计的出网报文必须与 Builder.BuildGetEMStatistics 一致
        AssertFramesStructurallyEqual(
            firstResult.Payload!,
            new AnShengCommandBuilder()
                .BuildGetEMStatistics(Seed.Imei, null, AnShengDeviceKind.Switch4G).Payload);

        await ReplyStatisticsAsync(firstSent.FrameId, BuildSlotsJson(
            slot1: """
            {"total":10.5,"dayData":[{"date":"20260801","kwh":1.1},{"date":"20260802","kwh":2.2},{"date":"20260803","kwh":3.3}],"monthData":[{"date":"202608","kwh":6.6}]}
            """,
            slot2: """{"total":2.25}"""));

        var baseline = await LoadStatisticsAsync();
        baseline.Should().HaveCount(6,
            "slot1 应产出 1 个 total + 3 个 day + 1 个 month，slot2 应产出 1 个 total");

        // 铁律①：后台作用域（TenantContext 为 null）写回，AppCode 必须显式落对
        baseline.Should().OnlyContain(r => r.AppCode == SharedTestConstants.AppCode,
            "统计 UPSERT 跑在后台作用域，AppCode 落错等于数据对任何租户视图都不可见");

        // ── 第 2、3 轮：同样的周期键、不同的电量值 ──
        for (var round = 2; round <= 3; round++)
        {
            var (sent, _) = await RefreshStatisticsAsync();
            await ReplyStatisticsAsync(sent.FrameId, BuildSlotsJson(
                slot1: $$"""
                {"total":{{10.5 + round}},"dayData":[{"date":"20260801","kwh":1.1},{"date":"20260802","kwh":2.2},{"date":"20260803","kwh":{{3.3 + round}}}],"monthData":[{"date":"202608","kwh":6.6}]}
                """,
                slot2: """{"total":2.25}"""));

            var rows = await LoadStatisticsAsync();
            rows.Should().HaveCount(6,
                $"第 {round} 次上报的周期键与首次完全相同，必须 UPSERT 而非插入重复行（唯一键失效的典型症状就是行数翻倍）");
        }

        // 最后一次真值必须覆盖旧值（UPSERT 的 U）
        var final = await LoadStatisticsAsync();
        final.Single(r => r.SlotNum == 1 && r.Granularity == AnShengEmGranularity.Total)
            .Kwh.Should().BeApproximately(13.5, 1e-6, "设备是权威，重复上报应覆盖为最后一次的值");
        final.Single(r => r.SlotNum == 1 && r.Granularity == AnShengEmGranularity.Day
                && r.PeriodKey == "20260803")
            .Kwh.Should().BeApproximately(6.3, 1e-6, "同一周期键的电量必须被后到的真值覆盖");

        // 唯一键的四元组在库内必须真正唯一
        final.Select(r => (r.DeviceId, r.SlotNum, r.Granularity, r.PeriodKey))
            .Should().OnlyHaveUniqueItems("(DeviceId, SlotNum, Granularity, PeriodKey) 是本表的生命线");

        // 插槽号必须按 data[] 下标 +1 推导（§7-R8，应答不含 slotNum）
        final.Select(r => r.SlotNum).Distinct().Should().BeEquivalentTo(new[] { 1, 2 },
            "data[] 下标 i ⇒ 插槽 i+1；出现 0 或 3 说明推导错位");
    }

    // ─────────────────────────────────────────────────────────────
    // 验收 ②③：hourSumData 48 项，PeriodKey 00:00~23:30 连续无空洞
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 验收 ②+③：<c>hourSumLength==48</c> 时必须产出<b>恰好 48</b> 个 HourSum 点，
    /// PeriodKey 为 <c>00:00</c>~<c>23:30</c> 严格连续（无缺漏、无越界、无重复），
    /// 且第 i 个槽位的电量与 <c>hourSumData[i]</c> 一一对应（下标 ⇒ 槽位不得错位）。
    /// </summary>
    [Fact(DisplayName = "验收②③ hourSumLength==48 → 48 个 HourSum 点，00:00~23:30 连续无空洞无越界")]
    public async Task HourSumData48_ProducesExactly48ContinuousSlots()
    {
        await SeedProfileAsync(AnShengDeviceKind.Switch4G, SlotAmount);

        // hourSumData[i] = i * 0.5，便于反查「第 i 个槽位有没有被写到正确的 PeriodKey 上」
        var hourSum = string.Join(",",
            Enumerable.Range(0, 48).Select(i => (i * 0.5).ToString("0.0#", CultureInfo.InvariantCulture)));

        var (sent, _) = await RefreshStatisticsAsync(q: "hourSum,total");
        await ReplyStatisticsAsync(sent.FrameId, BuildSlotsJson(
            slot1: $$"""{"total":100.0,"hourSumData":[{{hourSum}}]}""",
            slot2: """{"total":0}"""));

        var hourSumRows = (await LoadStatisticsAsync())
            .Where(r => r.SlotNum == 1 && r.Granularity == AnShengEmGranularity.HourSum)
            .ToList();

        // ② 数量：不多不少 48
        hourSumRows.Should().HaveCount(48,
            "协议规定 hourSumData 定长 48（asopen.md L4327），少一个就是日内画像有缺口");

        // ③ 连续性：PeriodKey 必须严格等于 00:00~23:30 这 48 个槽位，一个不多一个不少
        var expectedKeys = Enumerable.Range(0, 48)
            .Select(AnShengEmStatistic.HourSumPeriodKey).ToList();

        expectedKeys.First().Should().Be("00:00");
        expectedKeys.Last().Should().Be("23:30");

        hourSumRows.Select(r => r.PeriodKey).OrderBy(k => k, StringComparer.Ordinal)
            .Should().Equal(expectedKeys,
                "PeriodKey 必须是 00:00~23:30 的连续 48 槽；缺项=空洞，多项=越界（如 24:00）");

        hourSumRows.Select(r => r.PeriodKey).Should().OnlyHaveUniqueItems(
            "同一插槽同一槽位只能有一行，重复即唯一键失效");

        // 下标 ⇒ 槽位的对应关系不得错位（错位的画像看上去完全正常，却把用电高峰挪了时区）
        for (var i = 0; i < 48; i++)
        {
            var key = AnShengEmStatistic.HourSumPeriodKey(i);
            hourSumRows.Single(r => r.PeriodKey == key)
                .Kwh.Should().BeApproximately(i * 0.5, 1e-6,
                    $"hourSumData[{i}] 必须落到槽位 {key}，下标换算错位会让整条日内曲线整体平移");
        }

        // 同一帧里的 total 点不受影响
        (await LoadStatisticsAsync())
            .Single(r => r.SlotNum == 1 && r.Granularity == AnShengEmGranularity.Total)
            .Kwh.Should().BeApproximately(100.0, 1e-6);
    }

    /// <summary>
    /// 验收 ② 的反面：<c>hourSumLength != 48</c>（这里给 47）时，
    /// 该插槽<b>一个 HourSum 点都不得产出</b>——宁可少写，也不能把错位的半小时画像写进库。
    /// 同帧的 total 仍应正常入库，证明「丢弃」的粒度是 HourSum 而不是整帧。
    /// </summary>
    [Fact(DisplayName = "验收② hourSumLength!=48 → 零 HourSum 点（同帧 total 仍入库）")]
    public async Task HourSumDataWrongLength_ProducesNoHourSumPoints()
    {
        await SeedProfileAsync(AnShengDeviceKind.Switch4G, SlotAmount);

        var hourSum47 = string.Join(",", Enumerable.Range(0, 47).Select(i => i.ToString(CultureInfo.InvariantCulture)));

        var (sent, _) = await RefreshStatisticsAsync();
        await ReplyStatisticsAsync(sent.FrameId, BuildSlotsJson(
            slot1: $$"""{"total":7.5,"hourSumData":[{{hourSum47}}]}""",
            slot2: """{"total":1}"""));

        var rows = await LoadStatisticsAsync();

        rows.Where(r => r.Granularity == AnShengEmGranularity.HourSum).Should().BeEmpty(
            "长度不符说明固件违反协议，此时下标→槽位的换算必然错位，一个点都不能写");

        rows.Single(r => r.SlotNum == 1 && r.Granularity == AnShengEmGranularity.Total)
            .Kwh.Should().BeApproximately(7.5, 1e-6, "只丢弃 hourSum 画像，不应牵连同帧的其它粒度");
    }

    /// <summary>
    /// 验收 ③ 的另一面：<c>dayData</c> 是<b>可能不连续</b>的稀疏序列
    /// （协议明说「没记录到的日期表示无累计电量或超出保留期」）。
    /// 平台<b>只为真实存在的元素产出行</b>，绝不按日期区间补齐 ——
    /// 补出来的 0 行会被前端当成「那天真的用了 0 度电」，与「那天没数据」是两回事。
    /// </summary>
    [Fact(DisplayName = "验收③ 稀疏 dayData 不得被补齐成假 0 行（缺失日期不落库）")]
    public async Task SparseDayData_IsNotBackFilledWithFakeZeroRows()
    {
        await SeedProfileAsync(AnShengDeviceKind.Switch4G, SlotAmount);

        // 故意跳过 20260802 与 20260804
        var (sent, _) = await RefreshStatisticsAsync(q: "day");
        await ReplyStatisticsAsync(sent.FrameId, BuildSlotsJson(
            slot1: """
            {"dayData":[{"date":"20260801","kwh":1.5},{"date":"20260803","kwh":2.5},{"date":"20260805","kwh":3.5}]}
            """,
            slot2: """{"dayData":[]}"""));

        var dayRows = (await LoadStatisticsAsync())
            .Where(r => r.Granularity == AnShengEmGranularity.Day).ToList();

        dayRows.Select(r => r.PeriodKey).Should().BeEquivalentTo(
            new[] { "20260801", "20260803", "20260805" },
            "稀疏序列必须原样保留：补 20260802/20260804 等于凭空造出「那天用了 0 度电」的假数据");

        dayRows.Should().NotContain(r => r.PeriodKey == "20260802" || r.PeriodKey == "20260804");
    }

    // ─────────────────────────────────────────────────────────────
    // 验收 ④：清零后平台数据保留 + EmCleared 标记事件 + 可再次写回
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 验收 ④：<c>clearEMStatistics</c> 只清<b>设备侧</b>统计，平台聚合表一行不删（设计 D5），
    /// 并追加一条 <see cref="AnShengEventKind.EmCleared"/>(=8) 标记事件用于对账；
    /// 清零之后设备再上报统计，平台仍能正常 UPSERT 写回。
    ///
    /// 【为什么必须留痕】清零后「平台累计值 ≫ 设备读数」会成为常态。
    /// 没有这条标记，日后对账只会得出「平台数据错了」的错误结论。
    ///
    /// 顺带复核铁律③（clearEMStatistics 报文对齐 Builder）与铁律①（事件行 AppCode）。
    /// </summary>
    [Fact(DisplayName = "验收④ clearEMStatistics → 聚合行保留 + EmCleared(8) 事件 + 后续刷新可再写回")]
    public async Task ClearStatistics_RetainsPlatformRows_AndWritesEmClearedEvent()
    {
        await SeedProfileAsync(AnShengDeviceKind.Switch4G, SlotAmount);

        // Arrange —— 先攒一份历史统计（6 行）
        var (seedSent, _) = await RefreshStatisticsAsync();
        await ReplyStatisticsAsync(seedSent.FrameId, BuildSlotsJson(
            slot1: """
            {"total":88.8,"dayData":[{"date":"20260801","kwh":1.1},{"date":"20260802","kwh":2.2},{"date":"20260803","kwh":3.3}],"monthData":[{"date":"202608","kwh":6.6}]}
            """,
            slot2: """{"total":4.4}"""));

        var before = await LoadStatisticsAsync();
        before.Should().HaveCount(6, "清零前的基线");

        // Act —— 清零（confirm=true，全插槽）
        var client = Client.AsAdmin();
        var response = await client.PostAsJsonAsync(
            $"/api/v1/ansheng/{Seed.DeviceId}/energy/statistics/clear",
            new AnShengClearEMStatisticsRequest { Confirm = true });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await ReadAsync<ApiResponse<AnShengEnergyResultDto>>(response);
        result!.Code.Should().Be(200, "Switch4G 支持 clearEMStatistics（G4），必须受理");
        result.Data!.Accepted.Should().BeTrue();

        // 铁律③：清零报文必须与 Builder.BuildClearEMStatistics 一致（不带 slotNum ⇒ 全清）
        AssertFramesStructurallyEqual(
            result.Data.Payload!,
            new AnShengCommandBuilder()
                .BuildClearEMStatistics(Seed.Imei, null, AnShengDeviceKind.Switch4G).Payload);

        Adapter.Sent.Should().ContainSingle(s => s.CommandType == MethodClearEMStatistics,
            "清零命令必须恰好出网一次");

        // Assert ①（验收 ④ 核心）—— 平台历史统计一行不删
        var after = await LoadStatisticsAsync();
        after.Should().HaveCount(6,
            "设计 D5：平台聚合表只累积保留，clearEMStatistics 只清设备侧；删平台行等于把历史电费凭据毁了");
        after.Single(r => r.SlotNum == 1 && r.Granularity == AnShengEmGranularity.Total)
            .Kwh.Should().BeApproximately(88.8, 1e-6, "清零不得篡改平台已有读数");

        // Assert ② —— EmCleared 标记事件
        var events = await QueryDbAsync(db => db.Set<AnShengDeviceEvent>()
            .IgnoreQueryFilters()
            .Where(e => e.DeviceId == Seed.DeviceId && e.Kind == AnShengEventKind.EmCleared)
            .ToListAsync());

        events.Should().ContainSingle(
            "清零必须留下且只留下一条对账标记事件（AnShengEventKind.EmCleared）");

        var marker = events.Single();
        ((int)marker.Kind).Should().Be(8, "EmCleared 的枚举底值一经发布不得重排（底值即库里的存量数据）");
        marker.Method.Should().Be(MethodClearEMStatistics);
        marker.SlotNum.Should().BeNull("未指定插槽 ⇒ 全清，SlotNum 应归一为 null");
        marker.AppCode.Should().Be(SharedTestConstants.AppCode, "铁律①：显式落租户码");
        marker.PayloadJson.Should().Contain("platformDataRetained",
            "标记事件必须写明「平台数据已保留」，否则对账时无从判断口径");

        // Assert ③ —— 清零后仍可再次写回（聚合链路没有被清零操作弄坏）
        var (againSent, _) = await RefreshStatisticsAsync();
        await ReplyStatisticsAsync(againSent.FrameId, BuildSlotsJson(
            slot1: """
            {"total":0.5,"dayData":[{"date":"20260801","kwh":1.1},{"date":"20260802","kwh":2.2},{"date":"20260803","kwh":3.3}],"monthData":[{"date":"202608","kwh":6.6}]}
            """,
            slot2: """{"total":0}"""));

        var reWritten = await LoadStatisticsAsync();
        reWritten.Should().HaveCount(6, "清零后再刷新仍按唯一键 UPSERT，不应产生重复行");
        reWritten.Single(r => r.SlotNum == 1 && r.Granularity == AnShengEmGranularity.Total)
            .Kwh.Should().BeApproximately(0.5, 1e-6, "设备清零后重新累计的新值必须能写回平台");
    }

    /// <summary>
    /// 验收 ④ 的守门条件：清零不带 <c>confirm=true</c> 时必须业务拒绝，
    /// 命令<b>零出网</b>、不产生 <c>EmCleared</c> 事件（铁律②：HTTP 200 + 业务码 400）。
    /// </summary>
    [Fact(DisplayName = "验收④ clearEMStatistics 无 confirm → 200 + code=400 + 零出网 + 零事件")]
    public async Task ClearStatistics_WithoutConfirm_Rejected_ZeroPublish()
    {
        await SeedProfileAsync(AnShengDeviceKind.Switch4G, SlotAmount);
        var client = Client.AsAdmin();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/ansheng/{Seed.DeviceId}/energy/statistics/clear",
            new AnShengClearEMStatisticsRequest { Confirm = false });

        var (_, raw) = await AssertRejectionEnvelopeAsync(response);
        raw.Should().Contain("confirm", "拒绝文案要说清楚缺的是二次确认");

        Adapter.Sent.Should().BeEmpty("清零是不可逆的设备侧操作，未确认绝不允许出网");

        var events = await QueryDbAsync(db => db.Set<AnShengDeviceEvent>()
            .IgnoreQueryFilters()
            .CountAsync(e => e.DeviceId == Seed.DeviceId && e.Kind == AnShengEventKind.EmCleared));
        events.Should().Be(0, "命令没出网，设备没被清，绝不能留下「已清零」的标记（否则对账指鹿为马）");
    }

    // ─────────────────────────────────────────────────────────────
    // 验收 ⑤：实时读数经 ProcessDeviceDataAsync 落 DeviceDataRecord
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 验收 ⑤：<c>getEMRealtime</c> 应答必须经 <c>IDataCollectionService.ProcessDeviceDataAsync</c>
    /// <b>真正落库</b>到 <c>DeviceDataRecord</c>（断言数据库行，不是内存镜像）。
    ///
    /// 【为什么走既有采集通路而不是新表】设计 D5：复用现成的 DataRule 告警引擎与图表，零改动。
    /// 落不进 DeviceDataRecord ⇒ T11 的实时数据对既有功能等于不存在。
    ///
    /// 【字段命名硬约束】必须与 <c>DataCollectionService.SlotFieldRegex</c>
    /// （<c>^slot(\d+)_(state|voltage|current|power|energy|pf)$</c>）对齐，
    /// 名字对不上就不会被识别成传感器数据点。
    ///
    /// 顺带复核铁律③（getEMRealtime 报文对齐 Builder）与铁律①（记录行 AppCode）。
    /// </summary>
    [Fact(DisplayName = "验收⑤ getEMRealtime 应答 → 经 ProcessDeviceDataAsync 落 DeviceDataRecord")]
    public async Task RealtimeReadback_PersistsIntoDeviceDataRecord()
    {
        await SeedProfileAsync(AnShengDeviceKind.Switch4G, SlotAmount);
        var client = Client.AsAdmin();

        // 前置：确认落库前确实没有任何数据记录，避免「本来就有」造成假绿
        (await QueryDbAsync(db => db.Set<DeviceDataRecord>()
            .IgnoreQueryFilters().CountAsync(r => r.DeviceId == Seed.DeviceId)))
            .Should().Be(0, "基线播种不含 DeviceDataRecord，落库断言才有意义");

        var response = await client.PostAsync(
            $"/api/v1/ansheng/{Seed.DeviceId}/energy/realtime", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await ReadAsync<ApiResponse<AnShengEnergyResultDto>>(response);
        result!.Code.Should().Be(200, "Switch4G 属于开关类（G3），getEMRealtime 必须放行");
        result.Data!.Accepted.Should().BeTrue();

        // 铁律③：实时读数报文必须与 Builder.BuildGetEMRealtime 一致
        AssertFramesStructurallyEqual(
            result.Data.Payload!,
            new AnShengCommandBuilder()
                .BuildGetEMRealtime(Seed.Imei, AnShengDeviceKind.Switch4G).Payload);

        var sent = Adapter.Sent.Single(s => s.CommandType == MethodGetEMRealtime);

        // 设备应答：两个插槽各带 v/c/p/e
        var replyPayload = $$"""
        {"method":"{{MethodGetEMRealtime}}","result":"ok","imei":"{{Seed.Imei}}","frameId":"{{sent.FrameId}}","data":[{"v":220.5,"c":1.5,"p":330.0,"e":12.5},{"v":219.5,"c":0.5,"p":110.0,"e":3.5}]}
        """;
        Adapter.RaiseAnShengUplink(Seed.Imei, MethodGetEMRealtime, replyPayload);

        (await Pipeline.DrainAsync(DrainTimeout))
            .Should().BeTrue("上行管道应在 {0}s 内处理完 getEMRealtime 应答", DrainTimeout.TotalSeconds);

        // Assert —— 真的落库了
        var records = await QueryDbAsync(db => db.Set<DeviceDataRecord>()
            .IgnoreQueryFilters()
            .Where(r => r.DeviceId == Seed.DeviceId)
            .ToListAsync());

        records.Should().ContainSingle(
            "一次 getEMRealtime 应答应产出恰好一条 DeviceDataRecord；" +
            "零条说明实时链路没接上 ProcessDeviceDataAsync（验收 ⑤ 失败）");

        var record = records.Single();
        record.AppCode.Should().Be(SharedTestConstants.AppCode,
            "铁律①：实时落库跑在后台作用域，AppCode 必须显式落对，否则曲线在租户视图里查不到");

        record.SensorData.Should().NotBeNullOrWhiteSpace();
        using var sensor = JsonDocument.Parse(record.SensorData!);
        var root = sensor.RootElement;

        // 逐插槽字段命名必须与 SlotFieldRegex 对齐
        root.GetProperty("slot1_voltage").GetDouble().Should().BeApproximately(220.5, 1e-6);
        root.GetProperty("slot1_current").GetDouble().Should().BeApproximately(1.5, 1e-6);
        root.GetProperty("slot1_power").GetDouble().Should().BeApproximately(330.0, 1e-6);
        root.GetProperty("slot1_energy").GetDouble().Should().BeApproximately(12.5, 1e-6);
        root.GetProperty("slot2_voltage").GetDouble().Should().BeApproximately(219.5, 1e-6);
        root.GetProperty("slot2_energy").GetDouble().Should().BeApproximately(3.5, 1e-6);

        // 整机聚合：电压取平均，电流/功率/电量求和
        root.GetProperty("avg_voltage").GetDouble().Should().BeApproximately(220.0, 1e-6);
        root.GetProperty("total_current").GetDouble().Should().BeApproximately(2.0, 1e-6);
        root.GetProperty("total_power").GetDouble().Should().BeApproximately(440.0, 1e-6);
        root.GetProperty("total_energy").GetDouble().Should().BeApproximately(16.0, 1e-6);

        // 既有告警/报表口径的两列必须被填上，否则实时数据对存量功能等于不存在
        record.ElectricPower.Should().BeApproximately(440.0, 1e-6,
            "total_power 必须映射到 ElectricPower（DataCollectionService 的安圣标准化字段表）");
        record.ElectricKWh.Should().BeApproximately(16.0, 1e-6,
            "total_energy 必须映射到 ElectricKWh");

        // 实时链路不得污染统计聚合表（两条链路必须彼此独立）
        (await LoadStatisticsAsync()).Should().BeEmpty(
            "getEMRealtime 只落时序表；写进统计聚合表会造出与设备口径冲突的假统计");
    }

    // ─────────────────────────────────────────────────────────────
    // 验收 ⑥：4 个校准命令仅开关类放行
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 验收 ⑥-a：喇叭类（<c>SpeakerWiFi</c>，无电量计硬件）调用<b>全部 4 个</b>校准端点，
    /// 必须以 <see cref="AnShengCommandRejectReason.RejectedByKind"/> 结构性拒绝且零出网。
    ///
    /// 校准报文打到没有电量计的设备上，轻则被忽略，重则把设备的出厂参数写坏。
    /// 这条防线由 Catalog(GroupSwitchAction) + Guard 保证，不允许控制器再写一份品类 if。
    /// </summary>
    [Theory(DisplayName = "验收⑥ SpeakerWiFi 调 4 个校准端点 → 200 + code=400 + RejectedByKind + 零出网")]
    [InlineData("GET", "energy/cal-params")]
    [InlineData("POST", "energy/cal-params")]
    [InlineData("POST", "energy/cal-params/reset")]
    [InlineData("POST", "energy/cal-params/auto")]
    public async Task SpeakerWiFi_CalibrationEndpoints_RejectedByKind_ZeroPublish(string verb, string path)
    {
        await SeedProfileAsync(AnShengDeviceKind.SpeakerWiFi, slotAmount: 0);
        var client = Client.AsAdmin();
        var url = $"/api/v1/ansheng/{Seed.DeviceId}/{path}";

        var response = verb == "GET"
            ? await client.GetAsync(url)
            : await client.PostAsJsonAsync(url, BuildCalBody(path));

        var (data, raw) = await AssertRejectionEnvelopeAsync(response);
        ReadRejectReason(data, raw).Should().Be(
            AnShengCommandRejectReason.RejectedByKind,
            "校准命令属 G3 开关动作组，喇叭类不具备电量计硬件，必须以品类维度拒绝。实际响应：" + Truncate(raw));

        // 铁律④：拒绝原因以枚举字符串出网
        AssertRejectReasonIsEnumString(data, raw, nameof(AnShengCommandRejectReason.RejectedByKind));

        Adapter.Sent.Should().BeEmpty("被 Guard 拦下的校准命令必须零 MQTT 发布");
    }

    /// <summary>
    /// 验收 ⑥-b：开关类（<c>Switch4G</c>）调用 4 个校准端点全部放行，
    /// 且出网报文与 <see cref="AnShengCommandBuilder"/> 的对应专用方法<b>字节级一致</b>（铁律③）。
    ///
    /// 这条把「命令构造」与「下发路径」一起钉死：4 个命令各出网恰好一次，
    /// 报文由 Builder 专用方法产出，不存在手搓 JSON 的旁路。
    /// </summary>
    [Fact(DisplayName = "验收⑥ Switch4G 4 个校准命令放行 + 报文与 Builder 四方法字节级一致")]
    public async Task Switch4G_CalibrationCommands_Accepted_AndPayloadsMatchBuilder()
    {
        await SeedProfileAsync(AnShengDeviceKind.Switch4G, SlotAmount);
        var client = Client.AsAdmin();
        var builder = new AnShengCommandBuilder();
        const double rl = 1.85;
        const double power = 1500;

        // ① getCalParams
        var getResp = await client.GetAsync($"/api/v1/ansheng/{Seed.DeviceId}/energy/cal-params");
        var getResult = await AssertAcceptedAsync(getResp, "读取校准参数");
        AssertFramesStructurallyEqual(
            getResult.Payload!,
            builder.BuildGetCalParams(Seed.Imei, AnShengDeviceKind.Switch4G).Payload);

        // ② setCalParams —— 校准参数以 JSON 对象出网（Guard 只认 JsonElement 的 Object 形态）
        var setResp = await client.PostAsJsonAsync(
            $"/api/v1/ansheng/{Seed.DeviceId}/energy/cal-params",
            new AnShengSetCalParamsRequest { RL = rl });
        var setResult = await AssertAcceptedAsync(setResp, "设置校准参数");
        AssertFramesStructurallyEqual(
            setResult.Payload!,
            builder.BuildSetCalParams(
                Seed.Imei,
                JsonSerializer.SerializeToElement(
                    new Dictionary<string, double>(StringComparer.Ordinal) { ["RL"] = rl }),
                AnShengDeviceKind.Switch4G).Payload);

        // ③ resetCalParams
        var resetResp = await client.PostAsync(
            $"/api/v1/ansheng/{Seed.DeviceId}/energy/cal-params/reset", content: null);
        var resetResult = await AssertAcceptedAsync(resetResp, "重置校准参数");
        AssertFramesStructurallyEqual(
            resetResult.Payload!,
            builder.BuildResetCalParams(Seed.Imei, AnShengDeviceKind.Switch4G).Payload);

        // ④ autoCal
        var autoResp = await client.PostAsJsonAsync(
            $"/api/v1/ansheng/{Seed.DeviceId}/energy/cal-params/auto",
            new AnShengAutoCalRequest { Power = power });
        var autoResult = await AssertAcceptedAsync(autoResp, "自动校准");
        AssertFramesStructurallyEqual(
            autoResult.Payload!,
            builder.BuildAutoCal(Seed.Imei, power, AnShengDeviceKind.Switch4G).Payload);

        // 下发路径：4 个命令各出网恰好一次，且方法名不串台
        Adapter.Sent.Select(s => s.CommandType).Should().BeEquivalentTo(
            new[] { MethodGetCalParams, MethodSetCalParams, MethodResetCalParams, MethodAutoCal },
            "4 个校准命令必须各出网恰好一次，多发/少发/串台都是缺陷");
    }

    /// <summary>
    /// 验收 ⑥-c：<c>setCalParams</c> 的 <c>calParams</c> 为空时在<b>下发前</b>拒绝
    /// （铁律②：HTTP 200 + 业务码 400），零出网。空参数下发等于让设备用一组空校准值覆盖出厂值。
    /// </summary>
    [Fact(DisplayName = "验收⑥ setCalParams 空参数 → 200 + code=400 + 零出网")]
    public async Task SetCalParams_EmptyParams_Rejected_ZeroPublish()
    {
        await SeedProfileAsync(AnShengDeviceKind.Switch4G, SlotAmount);
        var client = Client.AsAdmin();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/ansheng/{Seed.DeviceId}/energy/cal-params",
            new AnShengSetCalParamsRequest());

        var raw = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "铁律②：业务校验失败一律 HTTP 200 + 业务码 400。实际响应：" + Truncate(raw));

        using var doc = JsonDocument.Parse(raw);
        TryGetPropertyIgnoreCase(doc.RootElement, "code", out var code).Should().BeTrue();
        code.GetInt32().Should().Be(RejectedCode, "空校准参数必须以业务码 400 拒绝。响应：" + Truncate(raw));

        Adapter.Sent.Should().BeEmpty("空校准参数必须在下发前拦住");
    }

    /// <summary>
    /// 验收 ⑥ 的另一半（统计组品类边界）：<c>getEMStatistics</c> / <c>clearEMStatistics</c>
    /// 属 G4 组（<c>GroupTimeTask = Switch4G</c>），WiFi 开关必须被结构性拒绝且零出网。
    /// </summary>
    [Theory(DisplayName = "验收⑥ SwitchWiFi 调统计刷新/清零 → RejectedByKind + 零出网（G4 仅 4G 开关）")]
    [InlineData("energy/statistics/refresh")]
    [InlineData("energy/statistics/clear")]
    public async Task SwitchWiFi_StatisticsEndpoints_RejectedByKind_ZeroPublish(string path)
    {
        await SeedProfileAsync(AnShengDeviceKind.SwitchWiFi, SlotAmount);
        var client = Client.AsAdmin();

        object body = path.EndsWith("clear", StringComparison.Ordinal)
            ? new AnShengClearEMStatisticsRequest { Confirm = true }
            : new AnShengGetEMStatisticsRequest();

        var response = await client.PostAsJsonAsync($"/api/v1/ansheng/{Seed.DeviceId}/{path}", body);

        var (data, raw) = await AssertRejectionEnvelopeAsync(response);
        ReadRejectReason(data, raw).Should().Be(
            AnShengCommandRejectReason.RejectedByKind,
            "电量计统计组仅 Switch4G 支持（G4）。实际响应：" + Truncate(raw));

        Adapter.Sent.Should().BeEmpty("被 Guard 拦下的命令必须零 MQTT 发布");
        (await LoadStatisticsAsync()).Should().BeEmpty("被拒的命令不得留下任何聚合行");
    }

    // ─────────────────────────────────────────────────────────────
    // 铁律④：枚举以字符串原名（PascalCase）出网
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 铁律④：<c>GET energy/statistics</c> 返回的 <c>granularity</c> 必须是枚举<b>字符串原名</b>
    /// （"Total"/"HourSum"/"Day"/"Month"/"Hour"），不是魔数 0/1/3/4。
    /// 顺带覆盖读端点的过滤参数与 <c>IsStale</c> 语义。
    ///
    /// 【说明】<see cref="AnShengEventKind.EmCleared"/> 目前没有任何 HTTP 端点暴露
    /// （<c>AnShengDeviceEvent</c> 未挂控制器），故其出网形态无法在本层断言；
    /// 本用例以同一全局转换器下的 <c>granularity</c> 作为契约代表，
    /// EmCleared 的落库形态已在验收 ④ 中按底值 8 钉死。
    /// </summary>
    [Fact(DisplayName = "铁律④ granularity 以枚举字符串出网；读端点支持插槽/粒度过滤")]
    public async Task GetStatistics_SerializesGranularityAsEnumString_AndSupportsFilters()
    {
        await SeedProfileAsync(AnShengDeviceKind.Switch4G, SlotAmount);

        var (sent, _) = await RefreshStatisticsAsync();
        await ReplyStatisticsAsync(sent.FrameId, BuildSlotsJson(
            slot1: """
            {"total":5.5,"dayData":[{"date":"20260801","kwh":1.1}],"monthData":[{"date":"202608","kwh":6.6}]}
            """,
            slot2: """{"total":2.2}"""));

        var client = Client.AsAdmin();
        var response = await client.GetAsync($"/api/v1/ansheng/{Seed.DeviceId}/energy/statistics");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var raw = await response.Content.ReadAsStringAsync();
        var result = await ReadAsync<ApiResponse<List<AnShengEmStatisticDto>>>(response);
        result!.Code.Should().Be(200);
        result.Data.Should().HaveCount(4, "slot1 的 total/day/month 共 3 行 + slot2 的 total 1 行");

        // 铁律④：枚举字符串
        using var doc = JsonDocument.Parse(raw);
        var granularityTokens = CollectPropertyValues(doc.RootElement, "granularity").ToList();
        granularityTokens.Should().NotBeEmpty("响应里必须能找到 granularity 字段");
        granularityTokens.Should().OnlyContain(
            e => e.ValueKind == JsonValueKind.String,
            "全局 JsonStringEnumConverter 要求枚举以字符串出网；出现数字说明转换器被撤或被局部覆盖。响应：" + Truncate(raw));
        granularityTokens.Select(e => e.GetString()).Should().BeSubsetOf(new[]
        {
            nameof(AnShengEmGranularity.Total), nameof(AnShengEmGranularity.HourSum),
            nameof(AnShengEmGranularity.Hour), nameof(AnShengEmGranularity.Day),
            nameof(AnShengEmGranularity.Month)
        }, "枚举必须以 PascalCase 原名出网");

        // 刚同步的行不该被标记为陈旧
        result.Data!.Should().OnlyContain(d => !d.IsStale, "刚写回的统计行不应被判为超 24h 陈旧");

        // 过滤：按插槽
        var slot2Resp = await client.GetAsync(
            $"/api/v1/ansheng/{Seed.DeviceId}/energy/statistics?slotNum=2");
        var slot2 = await ReadAsync<ApiResponse<List<AnShengEmStatisticDto>>>(slot2Resp);
        slot2!.Data.Should().ContainSingle().Which.SlotNum.Should().Be(2);

        // 过滤：按粒度（枚举字符串入参）
        var dayResp = await client.GetAsync(
            $"/api/v1/ansheng/{Seed.DeviceId}/energy/statistics?granularity=Day");
        var day = await ReadAsync<ApiResponse<List<AnShengEmStatisticDto>>>(dayResp);
        day!.Data.Should().ContainSingle()
            .Which.Granularity.Should().Be(AnShengEmGranularity.Day);

        // 非法 slotNum 走信封拒绝（铁律②）
        var badResp = await client.GetAsync(
            $"/api/v1/ansheng/{Seed.DeviceId}/energy/statistics?slotNum=0");
        badResp.StatusCode.Should().Be(HttpStatusCode.OK, "铁律②：不得返回裸 HTTP 400");
        (await ReadAsync<ApiResponse<List<AnShengEmStatisticDto>>>(badResp))!
            .Code.Should().Be(RejectedCode);
    }

    // ═════════════════════════════════════════════════════════════
    // 测试辅助（T8/T10 同名 helper 均为 private，无法跨文件复用，故按需复刻）
    // ═════════════════════════════════════════════════════════════

    /// <summary>事件管道单例。断言前必须 <c>DrainAsync</c>，否则读到的是半成品状态。</summary>
    private AnShengUplinkPipeline Pipeline =>
        Fixture.Factory.Services.GetRequiredService<AnShengUplinkPipeline>();

    /// <summary>
    /// 下发一次 <c>getEMStatistics</c> 并断言受理，返回「录制到的下行命令 + 接口结果」。
    /// 命令帧 id 用于后续构造设备应答，接口结果里的 Payload 用于铁律③ 报文对照。
    /// </summary>
    /// <param name="q">可选查询串（all / month / day / hour / hourSum / total）。</param>
    /// <returns>录制到的下行命令与接口返回的结果 DTO。</returns>
    private async Task<(SentCommand Sent, AnShengEnergyResultDto Result)> RefreshStatisticsAsync(
        string? q = null)
    {
        var client = Client.AsAdmin();
        var response = await client.PostAsJsonAsync(
            $"/api/v1/ansheng/{Seed.DeviceId}/energy/statistics/refresh",
            new AnShengGetEMStatisticsRequest { Q = q });

        var result = await AssertAcceptedAsync(response, "拉取电量计统计");

        var sent = Adapter.Sent.LastOrDefault(
            s => string.Equals(s.CommandType, MethodGetEMStatistics, StringComparison.Ordinal));
        sent.Should().NotBeNull("受理后必须有一条 getEMStatistics 出网记录");
        sent!.FrameId.Should().NotBeNullOrWhiteSpace("应答要靠 frameId 关联回本次请求");

        return (sent, result);
    }

    /// <summary>
    /// 注入一帧 <c>getEMStatistics</c> 设备应答并等待上行管道排空。
    /// </summary>
    /// <param name="frameId">与下行命令关联的帧 id。</param>
    /// <param name="dataArrayJson">应答的 <c>data</c> 数组 JSON 原文。</param>
    private async Task ReplyStatisticsAsync(string frameId, string dataArrayJson)
    {
        var payload = $$"""
        {"method":"{{MethodGetEMStatistics}}","result":"ok","imei":"{{Seed.Imei}}","frameId":"{{frameId}}","data":{{dataArrayJson}}}
        """;

        Adapter.RaiseAnShengUplink(Seed.Imei, MethodGetEMStatistics, payload);

        (await Pipeline.DrainAsync(DrainTimeout))
            .Should().BeTrue("上行管道应在 {0}s 内处理完 getEMStatistics 应答", DrainTimeout.TotalSeconds);
    }

    /// <summary>
    /// 拼出 <c>data[]</c>：<b>长度必须等于 <see cref="SlotAmount"/></b>，
    /// 否则服务层按 §7-R8 整帧拒收（插槽号按下标推导，长度不符必然错位）。
    /// </summary>
    /// <param name="slot1">插槽 1 的 JSON 对象原文。</param>
    /// <param name="slot2">插槽 2 的 JSON 对象原文。</param>
    /// <returns>data 数组 JSON。</returns>
    private static string BuildSlotsJson(string slot1, string slot2) => $"[{slot1},{slot2}]";

    /// <summary>读取本设备的全部聚合行（后台视角，绕过租户过滤器）。</summary>
    /// <returns>聚合行列表。</returns>
    private Task<List<AnShengEmStatistic>> LoadStatisticsAsync()
        => QueryDbAsync(db => db.Set<AnShengEmStatistic>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => s.DeviceId == Seed.DeviceId)
            .ToListAsync());

    /// <summary>
    /// 插入设备能力档案（决策 D7：品类必须显式落档，否则 Guard 走「未知即放行」降级分支）。
    /// AppCode 必须显式赋值：播种走 DI 作用域直连 AppDbContext，此时 TenantContext 为空。
    /// </summary>
    /// <param name="kind">设备品类。</param>
    /// <param name="slotAmount">插槽数。</param>
    private Task SeedProfileAsync(AnShengDeviceKind kind, int slotAmount)
        => ExecuteDbAsync(async db =>
        {
            db.AnShengDeviceProfiles.Add(new AnShengDeviceProfile
            {
                AppCode = SharedTestConstants.AppCode,
                Imei = Seed.Imei,
                DeviceId = Seed.DeviceId,
                Kind = kind,
                KindSource = AnShengKindSource.Manual,
                SlotAmount = slotAmount,
                Version = "5.1.0",
                ProbeStatus = AnShengProbeStatus.Probed
            });
            await db.SaveChangesAsync();
        });

    /// <summary>按端点路径构造校准请求体（GET 端点不会用到）。</summary>
    /// <param name="path">端点相对路径。</param>
    /// <returns>请求体对象。</returns>
    private static object BuildCalBody(string path) => path switch
    {
        "energy/cal-params" => new AnShengSetCalParamsRequest { RL = 1.85 },
        "energy/cal-params/auto" => new AnShengAutoCalRequest { Power = 1500 },
        _ => new { }
    };

    /// <summary>断言「受理态信封」（HTTP 200 + Code 200 + Accepted）并返回结果 DTO。</summary>
    /// <param name="response">HTTP 响应。</param>
    /// <param name="what">用于失败文案的操作名。</param>
    /// <returns>结果 DTO。</returns>
    private static async Task<AnShengEnergyResultDto> AssertAcceptedAsync(
        HttpResponseMessage response, string what)
    {
        var raw = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"{what} 应返回 HTTP 200。响应：{Truncate(raw)}");

        var result = await ReadAsync<ApiResponse<AnShengEnergyResultDto>>(response);
        result.Should().NotBeNull();
        result!.Code.Should().Be(200, $"{what} 应被受理。响应：{Truncate(raw)}");
        result.Data.Should().NotBeNull($"{what} 的 data 不得为空。响应：{Truncate(raw)}");
        result.Data!.Accepted.Should().BeTrue($"{what} 应被受理。响应：{Truncate(raw)}");
        result.Data.Payload.Should().NotBeNullOrWhiteSpace("受理态必须回显出网报文，供报文对照");

        return result.Data;
    }

    /// <summary>断言「拒绝态 HTTP 信封」的公共骨架（设计 §8.3），返回 data 节点与响应原文。</summary>
    /// <param name="response">HTTP 响应。</param>
    /// <returns>data 节点与响应原文。</returns>
    private static async Task<(JsonElement Data, string Raw)> AssertRejectionEnvelopeAsync(
        HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "铁律②：全站 HTTP 200 + 业务 Code≠200；返回裸 400/500 说明控制器改用了 MVC 的 BadRequest()。" +
            $"实际响应：{Truncate(raw)}");

        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement.Clone();

        TryGetPropertyIgnoreCase(root, "code", out var code).Should().BeTrue(
            $"全站 ApiResponse 约定必须有 code，实际响应：{Truncate(raw)}");
        code.GetInt32().Should().Be(
            RejectedCode, $"命令被拒绝时业务码必须是 400，实际响应：{Truncate(raw)}");

        TryGetPropertyIgnoreCase(root, "message", out var message).Should().BeTrue("拒绝必须给出说明");
        message.GetString().Should().NotBeNullOrWhiteSpace("message 为空等于让用户面对没有解释的失败");

        TryGetPropertyIgnoreCase(root, "data", out var data).Should().BeTrue(
            $"拒绝态信封必须有 data 节点，实际响应：{Truncate(raw)}");

        return (data, raw);
    }

    /// <summary>
    /// 从 data 读出拒绝原因，兼容枚举的两种上线形态（字符串名 / 整数底值），
    /// 归一成枚举后再断言语义——这样即便转换器被撤，用例也不会假红。
    /// </summary>
    /// <param name="data">信封 data 节点。</param>
    /// <param name="raw">响应原文（失败文案用）。</param>
    /// <returns>拒绝原因枚举。</returns>
    private static AnShengCommandRejectReason ReadRejectReason(JsonElement data, string raw)
    {
        data.ValueKind.Should().Be(
            JsonValueKind.Object,
            $"data 为 null 意味着控制器丢掉了服务层已填好的 result。实际响应：{Truncate(raw)}");

        TryGetPropertyIgnoreCase(data, "rejectReason", out var element).Should().BeTrue(
            "拒绝态信封必须带 rejectReason。实际响应：" + Truncate(raw));

        element.ValueKind.Should().NotBe(
            JsonValueKind.Null, $"rejectReason 为 null 意味着判定结论没被透传。实际响应：{Truncate(raw)}");

        switch (element.ValueKind)
        {
            case JsonValueKind.String:
            {
                var text = element.GetString();
                Enum.TryParse<AnShengCommandRejectReason>(text, ignoreCase: true, out var parsed)
                    .Should().BeTrue($"rejectReason=\"{text}\" 不是合法成员。实际响应：{Truncate(raw)}");
                return parsed;
            }

            case JsonValueKind.Number:
            {
                var value = element.GetInt32();
                Enum.IsDefined(typeof(AnShengCommandRejectReason), value).Should()
                    .BeTrue($"rejectReason={value} 不在枚举定义域内。实际响应：{Truncate(raw)}");
                return (AnShengCommandRejectReason)value;
            }

            default:
                throw new Xunit.Sdk.XunitException(
                    $"rejectReason 只应为枚举名字符串或整数，实际 ValueKind={element.ValueKind}。响应：{Truncate(raw)}");
        }
    }

    /// <summary>断言 rejectReason 确实以<b>字符串</b>形态出网（全局枚举字符串契约）。</summary>
    /// <param name="data">信封 data 节点。</param>
    /// <param name="raw">响应原文。</param>
    /// <param name="expectedName">期望的枚举名。</param>
    private static void AssertRejectReasonIsEnumString(JsonElement data, string raw, string expectedName)
    {
        TryGetPropertyIgnoreCase(data, "rejectReason", out var element).Should().BeTrue();
        element.ValueKind.Should().Be(
            JsonValueKind.String,
            "全局 JsonStringEnumConverter 要求枚举以字符串出网；出现数字说明转换器被撤或被局部覆盖。" +
            $"实际响应：{Truncate(raw)}");
        element.GetString().Should().Be(expectedName);
    }

    /// <summary>递归收集响应里所有同名属性的值（用于枚举字符串契约的全量体检）。</summary>
    /// <param name="element">JSON 节点。</param>
    /// <param name="name">属性名。</param>
    /// <returns>所有同名属性值。</returns>
    private static IEnumerable<JsonElement> CollectPropertyValues(JsonElement element, string name)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        yield return prop.Value;
                    }

                    foreach (var nested in CollectPropertyValues(prop.Value, name))
                    {
                        yield return nested;
                    }
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var nested in CollectPropertyValues(item, name))
                    {
                        yield return nested;
                    }
                }

                break;
        }
    }

    /// <summary>
    /// 字节级对照两条出网报文（忽略 frameId 的随机性但校验其形态；校验 timestamp 为合理秒级整数）。
    /// 用于钉死铁律③——出网报文与协议构建器产出一致。
    /// </summary>
    /// <param name="actualJson">实际出网报文。</param>
    /// <param name="expectedJson">Builder 产出的期望报文。</param>
    private static void AssertFramesStructurallyEqual(string actualJson, string expectedJson)
    {
        using var actualDoc = JsonDocument.Parse(actualJson);
        using var expectedDoc = JsonDocument.Parse(expectedJson);
        var actual = actualDoc.RootElement.Clone();
        var expected = expectedDoc.RootElement.Clone();

        var actualProps = actual.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var expectedProps = expected.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        actualProps.Should().BeEquivalentTo(
            expectedProps,
            "出网报文的字段集合必须与方法/品类的协议规定完全一致（多/少字段都是缺陷）");

        foreach (var prop in expected.EnumerateObject())
        {
            var name = prop.Name;
            var actualValue = actual.GetProperty(name);

            if (string.Equals(name, "frameId", StringComparison.Ordinal))
            {
                actualValue.ValueKind.Should().Be(JsonValueKind.String);
                actualValue.GetString().Should().MatchRegex(
                    "^[0-9a-f]{16}$", "frameId 必须是 16 位小写十六进制串（协议强制）");
            }
            else if (string.Equals(name, "timestamp", StringComparison.Ordinal))
            {
                actualValue.ValueKind.Should().Be(JsonValueKind.Number, "timestamp 必须是秒级整数");
                actualValue.GetInt64().Should().BeGreaterThan(
                    1_700_000_000, "timestamp 应是合理的 Unix 秒级时间戳");
            }
            else
            {
                JsonElementDeepEquals(actualValue, prop.Value).Should().BeTrue(
                    $"报文字段 {name} 的值必须与协议构建器产出一致，实际={actualValue}，期望={prop.Value}");
            }
        }
    }

    /// <summary>大小写不敏感地取 JSON 属性（兼容 camelCase / PascalCase）。</summary>
    /// <param name="element">JSON 节点。</param>
    /// <param name="name">属性名。</param>
    /// <param name="value">出参：属性值。</param>
    /// <returns>是否命中。</returns>
    private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = prop.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    /// <summary>失败文案里截断超长响应，避免测试输出被整包 JSON 淹没。</summary>
    /// <param name="s">原文。</param>
    /// <returns>截断后的文本。</returns>
    private static string Truncate(string s) => s.Length <= 800 ? s : s[..800] + "…";

    /// <summary>
    /// 递归结构相等比较（值语义，忽略成员书写顺序）。
    /// 本环境的 <see cref="JsonElement"/> 未暴露 <c>DeepEquals</c>（CS1061），故自实现一份。
    /// </summary>
    /// <param name="a">左值。</param>
    /// <param name="b">右值。</param>
    /// <returns>是否结构相等。</returns>
    private static bool JsonElementDeepEquals(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind)
        {
            return false;
        }

        switch (a.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var aProps = a.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
                var bCount = 0;
                foreach (var bProp in b.EnumerateObject())
                {
                    bCount++;
                    if (!aProps.TryGetValue(bProp.Name, out var av) ||
                        !JsonElementDeepEquals(av, bProp.Value))
                    {
                        return false;
                    }
                }

                return aProps.Count == bCount;
            }

            case JsonValueKind.Array:
            {
                var aArr = a.EnumerateArray().ToArray();
                var bArr = b.EnumerateArray().ToArray();
                if (aArr.Length != bArr.Length)
                {
                    return false;
                }

                for (var i = 0; i < aArr.Length; i++)
                {
                    if (!JsonElementDeepEquals(aArr[i], bArr[i]))
                    {
                        return false;
                    }
                }

                return true;
            }

            case JsonValueKind.String:
                return string.Equals(a.GetString(), b.GetString(), StringComparison.Ordinal);

            case JsonValueKind.Number:
                return a.GetDouble().Equals(b.GetDouble());

            default:
                return true;
        }
    }
}
