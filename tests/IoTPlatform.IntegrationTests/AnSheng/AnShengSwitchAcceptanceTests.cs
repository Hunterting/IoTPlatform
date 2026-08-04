using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
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
using IoTPlatform.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IoTPlatform.IntegrationTests.AnSheng;

/// <summary>
/// T8「安圣开关动作与延时任务（后端）」逐条验收（设计文档 §0.2 的 5 条验收标准）。
///
/// 本文件只做<b>端到端</b>行为验证，复用既有集成脚手架（TestServer + Testcontainers MySQL +
/// RecordingAnShengAdapter + AnShengUplinkPipeline 静态总线），不新增任何基础设施。
///
/// ═════════════════════════════════════════════════════════════════════════
/// 【5 条验收标准的落点】
/// ═════════════════════════════════════════════════════════════════════════
///   ① action 端点：出网报文字节级一致（对照 AnShengCommandBuilder）+ 设备 reply 触发 SlotsSnapshot 写回；
///   ② actions 端点：slotNums 必须是 JSON 数组 [1,3] 而非逗号串；
///   ③ startDelayTask：先落乐观镜像，≥120ms 后自动触发 getDelayTasks 回读并 bump SyncedAt；
///   ④ delayEvent 上行：对应插槽镜像 Enable=false 且该帧 slots[] 刷新 Profile.SlotsSnapshot；
///   ⑤ 喇叭类（SpeakerWiFi）下发 action：HTTP 200 + code=400 + rejectReason=RejectedByKind + 零出网 + 落库 Rejected。
///
/// ═════════════════════════════════════════════════════════════════════════
/// 【顺带验证的任务①：全局 JsonStringEnumConverter】
/// ═════════════════════════════════════════════════════════════════════════
/// 验收 ⑤ 的 rejectReason 走 JSON 出网。任务①在 Program.cs 全局注册了 JsonStringEnumConverter，
/// 因此本端点的枚举必须以<b>原名字符串</b>（如 "RejectedByKind"）出网，而不是 int。
/// 为兼容两种形态、把断言钉在<b>语义</b>上，<see cref="ReadRejectReason"/> 同时接受字符串与整数，
/// 归一成枚举后再比——这样就算有人把转换器撤掉，本用例也不会假红。
///
/// ═════════════════════════════════════════════════════════════════════════
/// 【上行注入方式（与 AnShengEventTests 同源的接缝）】
/// ═════════════════════════════════════════════════════════════════════════
/// 设备应答/事件一律用 <c>Adapter.RaiseAnShengUplink(imei, method, payload)</c> 直投静态总线，
/// 再 <c>await Pipeline.DrainAsync(timeout)</c> 自旋等管道排空——这是精确的异步完成信号，
/// 比 Thread.Sleep 既稳又快（详见 AnShengUplinkPipeline 类注释）。
/// 要让 action 的 reply 被路由成 Response（从而触发 SlotsSnapshot 写回），reply 必须带上
/// 与下发给设备的<b>同一 frameId</b>（frameId 在途 ⇒ Response 分支 ⇒ ApplyResponseMirrorAsync）。
/// </summary>
[Collection(SharedTestConstants.CollectionName)]
public sealed class AnShengSwitchAcceptanceTests : IntegrationTestBase
{
    /// <summary>管道排空等待上限。真实 MySQL 首次建连 + EF 首次编译查询可能慢，给足余量。</summary>
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(15);

    /// <summary>轮询等待「自动回读 getDelayTasks 出网」的上限（写后回读延迟默认 120ms）。</summary>
    private static readonly TimeSpan ReadbackPollTimeout = TimeSpan.FromSeconds(8);

    /// <summary>设备权威 + 异步刷新的副作用最终一致窗口上限（用于 fire-and-forget 的数据桥）。</summary>
    private static readonly TimeSpan SideEffectTimeout = TimeSpan.FromSeconds(15);

    /// <summary>安圣协议方法名：单插槽开关动作。</summary>
    private const string MethodAction = "action";

    /// <summary>安圣协议方法名：多插槽开关动作。</summary>
    private const string MethodActions = "actions";

    /// <summary>安圣协议方法名：查询延时任务列表（写后回读用）。</summary>
    private const string MethodGetDelayTasks = "getDelayTasks";

    /// <summary>安圣协议方法名：延时到期事件上报。</summary>
    private const string MethodDelayEvent = "delayEvent";

    /// <summary>验收 #5 中设备权威 + 拒绝态业务码（设计 §8.3：HTTP 恒 200，业务码 400）。</summary>
    private const int RejectedCode = 400;

    public AnShengSwitchAcceptanceTests(DatabaseFixture fixture) : base(fixture)
    {
    }

    /// <summary>
    /// 用例结束后再清一次进程级静态状态（与 AnShengCommandRejectionEnvelopeTests 同款护栏）。
    /// 基类已在 InitializeAsync 清过「进门前」，这里清「出门后」——
    /// 本文件的用例会往在途表登记 frameId（action/startDelayTask/getDelayTasks 下发），
    /// 若哪天写后回读把条目残留下来，会污染后续用例的路由判定，排查成本极高。
    /// </summary>
    public override Task DisposeAsync()
    {
        StaticStateResetter.ResetAll(Fixture.Factory.Services);
        return base.DisposeAsync();
    }

    // ─────────────────────────────────────────────────────────────
    // 验收 ①：action 字节级报文 + SlotsSnapshot 写回
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 验收 ①：给 4G 开关下发 <c>action</c>，断言两件事：
    ///   (a) 出网报文与 <see cref="AnShengCommandBuilder.BuildAction"/> 字节级一致
    ///       （字段集合相同、业务字段值相同、frameId 为 16 位十六进制、4G 款必带 timestamp）；
    ///   (b) 设备随后回一条带 <c>slots[]</c> 的 action 应答（同源 frameId ⇒ Response 分支），
    ///       触发 <see cref="AnShengScheduleService.UpdateSlotsSnapshotAsync"/> 把状态写回档案。
    /// </summary>
    [Fact(DisplayName = "验收① action 出网报文字节级一致 + 设备 reply 触发 SlotsSnapshot 写回")]
    public async Task Action_PayloadIsByteLevel_And_SlotsSnapshotWrittenBack()
    {
        // Arrange —— 4G 开关（slotAmount≥1，版本给足排除固件门槛）
        await SeedProfileAsync(
            Seed.DeviceId, Seed.Imei, AnShengDeviceKind.Switch4G, slotAmount: 4, version: "V4.0.20");

        var client = Client.AsAdmin();
        const int slotNum = 1;
        const string action = "on";

        // Act ① —— 下发单插槽通断
        var response = await client.PostAsJsonAsync(
            $"/api/v1/ansheng/{Seed.DeviceId}/action",
            new AnShengActionRequest { SlotNum = slotNum, Action = action });

        response.StatusCode.Should().Be(
            HttpStatusCode.OK, "action 对合法 4G 开关必须受理并出网（HTTP 层不应失败）");

        var result = await ReadAsync<ApiResponse<AnShengSwitchResultDto>>(response);
        result.Should().NotBeNull();
        result!.Code.Should().Be(200, "命令被受理，业务码应为 200");
        result.Data.Should().NotBeNull();
        result.Data!.Accepted.Should().BeTrue("4G 开关支持 action，必须受理");
        result.Data.Payload.Should().NotBeNull("出网报文回显不得为 null，否则字节级断言无从谈起");

        // Assert (a) —— 字节级对照 AnShengCommandBuilder（4G 款）
        var expectedPayload = BuildExpectedActionPayload(Seed.Imei, slotNum, action, null);
        AssertFramesStructurallyEqual(result.Data.Payload!, expectedPayload);

        // Act ② —— 模拟设备回一条带 slots[] 的 action 应答，frameId 复用下发给设备的那个
        var frameId = result.Data.FrameId.Should().NotBeNull("受理的命令必有 frameId").And.Subject!;
        var replySlots = new[] { 1, 0, 1, 0 };
        var replyPayload = $$"""
        {"method":"{{MethodAction}}","imei":"{{Seed.Imei}}","slotNum":{{slotNum}},"action":"{{action}}","frameId":"{{frameId}}","slots":[{{string.Join(",", replySlots)}}]}
        """;

        Adapter.RaiseAnShengUplink(Seed.Imei, MethodAction, replyPayload);

        (await Pipeline.DrainAsync(DrainTimeout))
            .Should().BeTrue("上行管道应在 {0}s 内处理完这条 action 应答", DrainTimeout.TotalSeconds);

        // Assert (b) —— 档案 SlotsSnapshot 已写回，且租户码正确（后台作用域陷阱 §7.1）
        var profile = await QueryDbAsync(db =>
            db.AnShengDeviceProfiles.FirstOrDefaultAsync(p => p.DeviceId == Seed.DeviceId));
        profile.Should().NotBeNull("档案在 Arrange 阶段已播种");
        profile!.SlotsSnapshotAt.Should().NotBeNull(
            "设备回包带 slots[] 必须刷新 SlotsSnapshotAt，否则前端永远看不到最新状态");
        AnShengScheduleService.ParseSlotsSnapshot(profile.SlotsSnapshot)
            .Should().Equal(replySlots, "SlotsSnapshot 必须等于设备回包的插槽状态");
        profile.AppCode.Should().Be(
            SharedTestConstants.AppCode,
            "后台作用域写回必须显式带 AppCode，否则租户过滤器会让这条记录在租户视图里消失");
    }

    // ─────────────────────────────────────────────────────────────
    // 验收 ②：actions 数组形态 [1,3]
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 验收 ②：给 4G 开关下发 <c>actions</c>（多插槽批量），断言：
    ///   (a) 出网报文的 <c>slotNums</c> 是 JSON 数组 <c>[1,3]</c>，而非 "1,3" 逗号串；
    ///   (b) 整帧与 <see cref="AnShengCommandBuilder.BuildActions"/> 字节级一致。
    /// </summary>
    [Fact(DisplayName = "验收② actions 出网报文中 slotNums 为 JSON 数组 [1,3]（字节级一致）")]
    public async Task Actions_SlotNumsIsJsonArray_ByteLevelConsistent()
    {
        // Arrange —— 4G 开关（slotAmount≥3 才能合法下发 [1,3]）
        await SeedProfileAsync(
            Seed.DeviceId, Seed.Imei, AnShengDeviceKind.Switch4G, slotAmount: 4, version: "V4.0.20");

        var client = Client.AsAdmin();
        var slotNums = new[] { 1, 3 };
        const string action = "off";

        // Act
        var response = await client.PostAsJsonAsync(
            $"/api/v1/ansheng/{Seed.DeviceId}/actions",
            new AnShengActionsRequest { SlotNums = slotNums, Action = action });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await ReadAsync<ApiResponse<AnShengSwitchResultDto>>(response);
        result.Should().NotBeNull();
        result!.Code.Should().Be(200);
        result.Data.Should().NotBeNull();
        result.Data!.Accepted.Should().BeTrue("4G 开关支持 actions，必须受理");
        result.Data.Payload.Should().NotBeNull();

        // Assert (a) —— slotNums 必须是 JSON 数组
        using var doc = JsonDocument.Parse(result.Data.Payload!);
        var root = doc.RootElement;
        root.TryGetProperty("slotNums", out var slotNumsElement)
            .Should().BeTrue("出网报文必须含 slotNums 字段");
        slotNumsElement.ValueKind.Should().Be(
            JsonValueKind.Array, "slotNums 必须是 JSON 数组 [1,3]，绝不能是逗号串（验收②核心判据）");
        slotNumsElement.GetArrayLength().Should().Be(2, "数组长度必须恰好为 2");
        slotNumsElement.EnumerateArray().Select(e => e.GetInt32()).Should()
            .Equal(slotNums, "数组元素必须按请求顺序为 [1,3]");

        // Assert (b) —— 整帧字节级对照 AnShengCommandBuilder（4G 款）
        var expectedPayload = BuildExpectedActionsPayload(Seed.Imei, slotNums, action, null);
        AssertFramesStructurallyEqual(result.Data.Payload!, expectedPayload);

        // 兜底：下发通道确实走了 actions 方法（不是被退化成单条 action）
        Adapter.Sent.Should().ContainSingle(s =>
                string.Equals(s.CommandType, MethodActions, StringComparison.Ordinal),
            "下发记录必须至少有一条 actions 命令");
    }

    // ─────────────────────────────────────────────────────────────
    // 验收 ③：startDelayTask 自动回读 getDelayTasks + SyncedAt bump
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 验收 ③：给 4G 开关下发 <c>startDelayTask</c>，断言：
    ///   (a) 立即返回乐观镜像（slot 1：Enable=true / SAction=on / EAction=off / Secs=30）；
    ///   (b) ≥120ms 后平台<b>自动</b>下发一条 <c>getDelayTasks</c> 回读（Adapter.Sent 可见）；
    ///   (c) 设备回 getDelayTasks 应答后，镜像被设备真值覆盖且 <c>SyncedAt</c> 被 bump，租户码正确。
    /// </summary>
    [Fact(DisplayName = "验收③ startDelayTask 先落乐观镜像，≥120ms 自动回读 getDelayTasks 并 bump SyncedAt")]
    public async Task StartDelayTask_OptimisticMirror_ThenAutoReadbackBumpsSyncedAt()
    {
        // Arrange —— 4G 开关
        await SeedProfileAsync(
            Seed.DeviceId, Seed.Imei, AnShengDeviceKind.Switch4G, slotAmount: 4, version: "V4.0.20");

        var client = Client.AsAdmin();
        const int slotNum = 1;
        const bool enable = true;
        const string sAction = "on";
        const string eAction = "off";
        const int secs = 30;

        // Act ① —— 开始延时任务
        var response = await client.PostAsJsonAsync(
            $"/api/v1/ansheng/{Seed.DeviceId}/delay-tasks/start",
            new AnShengStartDelayTaskRequest
            {
                SlotNum = slotNum,
                Enable = enable,
                SAction = sAction,
                EAction = eAction,
                Secs = secs
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await ReadAsync<ApiResponse<AnShengDelayTaskResultDto>>(response);
        result.Should().NotBeNull();
        result!.Code.Should().Be(200);
        result.Data.Should().NotBeNull();
        result.Data!.Accepted.Should().BeTrue("4G 开关支持 startDelayTask，必须受理");

        // Assert (a) —— 乐观镜像立即返回且内容正确
        var optimistic = result.Data.Tasks.Should().NotBeNull("乐观镜像不得为 null").And.Subject;
        var slotTask = optimistic!.FirstOrDefault(t => t.SlotNum == slotNum);
        slotTask.Should().NotBeNull($"镜像必须含插槽 {slotNum} 一行");
        slotTask!.Enable.Should().Be(enable, "乐观镜像按请求意图落 Enable");
        slotTask.SAction.Should().Be(sAction);
        slotTask.EAction.Should().Be(eAction);
        slotTask.Secs.Should().Be(secs);
        var optimisticSyncedAt = slotTask.SyncedAt;

        // Assert (b) —— 平台自动回读 getDelayTasks（写后回读机制）
        var readback = await WaitForReadbackAsync(MethodGetDelayTasks, ReadbackPollTimeout);
        readback.CommandType.Should().Be(MethodGetDelayTasks, "startDelayTask 必须触发自动 getDelayTasks 回读");

        // Act ② —— 模拟设备回 getDelayTasks 应答（带 tasks[]，下标 0 ⇒ 插槽 1）
        var deviceTask = new { enable = true, sAction = "on", eAction = "off", secs = 30, cnt = 7 };
        var replyPayload = $$"""
        {"method":"{{MethodGetDelayTasks}}","imei":"{{Seed.Imei}}","frameId":"{{readback.FrameId}}","tasks":[{{JsonSerializer.Serialize(deviceTask)}}]}
        """;
        Adapter.RaiseAnShengUplink(Seed.Imei, MethodGetDelayTasks, replyPayload);

        (await Pipeline.DrainAsync(DrainTimeout))
            .Should().BeTrue("上行管道应在 {0}s 内处理完 getDelayTasks 应答", DrainTimeout.TotalSeconds);

        // Assert (c) —— 镜像被设备真值覆盖且 SyncedAt bump
        var mirror = await QueryDbAsync(db =>
            db.Set<AnShengDelayTask>().AsNoTracking()
                .FirstOrDefaultAsync(t => t.DeviceId == Seed.DeviceId && t.SlotNum == slotNum));
        mirror.Should().NotBeNull($"插槽 {slotNum} 的延时任务镜像应在回读后存在");
        mirror!.Enable.Should().Be(deviceTask.enable, "回读后镜像必须以设备真值为准");
        mirror.SAction.Should().Be(deviceTask.sAction);
        mirror.EAction.Should().Be(deviceTask.eAction);
        mirror.Secs.Should().Be(deviceTask.secs);
        mirror.Cnt.Should().Be(deviceTask.cnt, "回读应答带的 cnt 必须写回镜像");
        mirror.SyncedAt.Should().BeAfter(
            optimisticSyncedAt, "回读必须把 SyncedAt 往前 bump（设备权威 + 写后回读）");
        mirror.AppCode.Should().Be(
            SharedTestConstants.AppCode, "后台作用域写回必须显式带 AppCode");
    }

    // ─────────────────────────────────────────────────────────────
    // 验收 ④：delayEvent → Enable=false + SlotsSnapshot 更新
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 验收 ④：预置插槽 1 的延时任务镜像 <c>Enable=true</c>，注入一条 <c>delayEvent</c> 上行
    /// （<c>slotNum=1</c> + <c>slots=[0,1,0,1]</c>），断言：
    ///   (a) 该插槽镜像被置 <c>Enable=false</c>（延时到期已自行结束）；
    ///   (b) 同帧 slots[] 刷新 <c>Profile.SlotsSnapshot</c>，且租户码正确。
    /// </summary>
    [Fact(DisplayName = "验收④ delayEvent 上行 → 插槽镜像 Enable=false 且 SlotsSnapshot 刷新")]
    public async Task DelayEvent_SetsEnableFalse_And_UpdatesSlotsSnapshot()
    {
        // Arrange —— 4G 开关 + 预置一条 Enable=true 的延时任务镜像
        await SeedProfileAsync(
            Seed.DeviceId, Seed.Imei, AnShengDeviceKind.Switch4G, slotAmount: 4, version: "V4.0.20");
        await SeedDelayTaskMirrorAsync(Seed.DeviceId, slotNum: 1, enable: true);

        var client = Client.AsAdmin();
        _ = client; // 本用例不走 HTTP，纯上行注入；保留引用以明示同设备上下文

        // 预置镜像此刻应 Enable=true
        var before = await QueryDbAsync(db =>
            db.Set<AnShengDelayTask>().AsNoTracking()
                .FirstOrDefaultAsync(t => t.DeviceId == Seed.DeviceId && t.SlotNum == 1));
        before.Should().NotBeNull();
        before!.Enable.Should().BeTrue("预置镜像初始为 Enable=true，作为对照基线");

        // Act —— 注入 delayEvent 上行（注意位路号字段名是 slotNum，与归一化器一致）
        var eventSlots = new[] { 0, 1, 0, 1 };
        var payload = $$"""
        {"method":"{{MethodDelayEvent}}","imei":"{{Seed.Imei}}","slotNum":1,"slots":[{{string.Join(",", eventSlots)}}]}
        """;
        Adapter.RaiseAnShengUplink(Seed.Imei, MethodDelayEvent, payload);

        (await Pipeline.DrainAsync(DrainTimeout))
            .Should().BeTrue("事件管道应在 {0}s 内处理完 delayEvent", DrainTimeout.TotalSeconds);

        // Assert (a) —— 插槽 1 镜像被置 Enable=false
        var after = await QueryDbAsync(db =>
            db.Set<AnShengDelayTask>().AsNoTracking()
                .FirstOrDefaultAsync(t => t.DeviceId == Seed.DeviceId && t.SlotNum == 1));
        after.Should().NotBeNull();
        after!.Enable.Should().BeFalse(
            "delayEvent 表示延时任务已执行完毕并自行结束，对应插槽镜像必须落到 Enable=false");

        // Assert (b) —— 同帧 slots[] 刷新 Profile.SlotsSnapshot
        var profile = await QueryDbAsync(db =>
            db.AnShengDeviceProfiles.FirstOrDefaultAsync(p => p.DeviceId == Seed.DeviceId));
        profile.Should().NotBeNull();
        profile!.SlotsSnapshotAt.Should().NotBeNull("delayEvent 同帧 slots[] 必须刷新 SlotsSnapshotAt");
        AnShengScheduleService.ParseSlotsSnapshot(profile.SlotsSnapshot)
            .Should().Equal(eventSlots, "SlotsSnapshot 必须等于 delayEvent 同帧的插槽状态");
        profile.AppCode.Should().Be(SharedTestConstants.AppCode);
    }

    // ─────────────────────────────────────────────────────────────
    // 验收 ⑤：喇叭类设备 → 拒绝信封（RejectedByKind）+ 零出网
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 验收 ⑤：给 WiFi 喇叭（<c>SpeakerWiFi</c>）下发 <c>action</c>，断言全站拒绝信封：
    ///   ① <c>StatusCode == 200</c>（业务失败也走 HTTP 200，靠 code 表达）；
    ///   ② <c>code == 400</c>；
    ///   ③ <c>data.rejectReason</c> 语义等于 <c>RejectedByKind</c>（任务①要求以字符串 "RejectedByKind" 出网）；
    ///   ④ <c>Adapter.Sent</c> 为空（零出网）；
    ///   ⑤ 落库 <c>AnShengCommandRecords</c> 有 <c>Rejected</c> 终态（FrameId/SentAt 恒为 null）。
    /// </summary>
    [Fact(DisplayName = "验收⑤ 喇叭类设备 action → 200 + code=400 + RejectedByKind + 零出网 + 落库 Rejected")]
    public async Task Speaker_RejectedByKind_ReturnsEnvelope_ZeroPublish()
    {
        // Arrange —— SpeakerWiFi 落档。版本给足（V4.0.20），红的原因只可能是品类。
        await SeedProfileAsync(
            Seed.DeviceId, Seed.Imei, AnShengDeviceKind.SpeakerWiFi, slotAmount: 0, version: "V4.0.20");

        var client = Client.AsAdmin();
        Adapter.Sent.Should().BeEmpty("基类已在 InitializeAsync 重置录制适配器");

        // Act
        var response = await client.PostAsJsonAsync(
            $"/api/v1/ansheng/{Seed.DeviceId}/action",
            new AnShengActionRequest { SlotNum = 1, Action = "on" });

        // Assert —— 信封三件套
        var (data, raw) = await AssertRejectionEnvelopeAsync(response);

        ReadRejectReason(data, raw).Should().Be(
            AnShengCommandRejectReason.RejectedByKind,
            "WiFi 喇叭不具备开关能力，必须以品类维度拒绝；" +
            $"若得到 RejectedByValidation 说明 Guard 的环节顺序被改动。实际响应：{Truncate(raw)}");

        // 任务①的专门断言：枚举必须以<b>字符串原名</b>出网（不是 int）。
        // 这里直接断言原始 JSON 里 rejectReason 是字符串 "RejectedByKind"，
        // 以此钉死「全局 JsonStringEnumConverter 已生效、且没破坏本端点的枚举出网形态」。
        TryGetPropertyIgnoreCase(data, "rejectReason", out var rejectEl).Should().BeTrue();
        rejectEl.ValueKind.Should().Be(
            JsonValueKind.String,
            "任务①全局注册 JsonStringEnumConverter 后，rejectReason 必须以字符串（如 \"RejectedByKind\"）" +
            "出网，而不应是整数；若此断言失败，说明枚举出网形态被破坏。实际响应：" + Truncate(raw));
        rejectEl.GetString().Should().Be(
            nameof(AnShengCommandRejectReason.RejectedByKind),
            "字符串内容必须等于枚举名 RejectedByKind");

        // Assert —— 零出网（与「返回了拒绝信封」是两件独立的事）
        Adapter.Sent.Should().BeEmpty(
            "被 Guard 拦下的命令必须零 MQTT 发布；一旦出网，用户会看到「平台说不支持、设备却动了」");

        // Assert —— 落库留痕
        await AssertRejectedRecordAsync(data, raw, MethodAction);
    }

    // ─────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────

    /// <summary>事件管道单例。断言前必须用它 <c>DrainAsync</c>，否则读到的是半成品状态。</summary>
    private AnShengUplinkPipeline Pipeline =>
        Fixture.Factory.Services.GetRequiredService<AnShengUplinkPipeline>();

    /// <summary>
    /// 轮询等待平台自动下发的回读命令（如 getDelayTasks）出现在 <see cref="Adapter"/> 录制里。
    /// 用轮询而非固定 <c>Task.Delay</c>：写后回读走 fire-and-forget 后台 Task.Run，
    /// 抖动不会偶发红，机器快时立刻返回。
    /// </summary>
    /// <param name="method">期望出现的方法名。</param>
    /// <param name="timeout">最长等待。</param>
    /// <returns>命中的下发记录。</returns>
    private async Task<SentCommand> WaitForReadbackAsync(string method, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var hit = Adapter.Sent
                .FirstOrDefault(s => string.Equals(s.CommandType, method, StringComparison.Ordinal));
            if (hit != null)
            {
                return hit;
            }

            await Task.Delay(50);
        }

        throw new Xunit.Sdk.XunitException(
            $"在 {timeout} 内未观测到平台自动下发的回读命令 {method}（写后回读机制可能未生效）");
    }

    /// <summary>
    /// 断言「拒绝态 HTTP 信封」的公共骨架（设计 §8.3），返回 <c>data</c> 节点与响应原文。
    /// 复刻自 AnShengCommandRejectionEnvelopeTests，因原 helper 为 private static 不可跨文件复用。
    /// </summary>
    private static async Task<(JsonElement Data, string Raw)> AssertRejectionEnvelopeAsync(
        HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "全站约定 HTTP 200 + 业务 Code≠200；返回裸 400/500 说明控制器改用了 MVC 的 BadRequest()。" +
            $"实际响应：{Truncate(raw)}");

        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement.Clone();

        TryGetPropertyIgnoreCase(root, "code", out var code).Should().BeTrue(
            $"全站 ApiResponse 约定必须有 code，实际响应：{Truncate(raw)}");
        code.GetInt32().Should().Be(
            RejectedCode, $"命令被 Guard 拒绝时业务码必须是 400，实际响应：{Truncate(raw)}");

        TryGetPropertyIgnoreCase(root, "message", out var message).Should().BeTrue("拒绝必须给出说明");
        message.GetString().Should().NotBeNullOrWhiteSpace("message 为空等于让用户面对没有解释的失败");

        TryGetPropertyIgnoreCase(root, "data", out var data).Should().BeTrue(
            $"拒绝态信封必须有 data 节点，实际响应：{Truncate(raw)}");
        data.ValueKind.Should().Be(
            JsonValueKind.Object,
            "data 为 null 意味着控制器丢掉了服务层已填好的 result。" +
            $"实际响应：{Truncate(raw)}");

        return (data, raw);
    }

    /// <summary>
    /// 从 <c>data</c> 读出拒绝原因，同时兼容枚举的两种上线形态（字符串名 / 整数底值），
    /// 归一成 <see cref="AnShengCommandRejectReason"/> 后再断言语义。
    /// </summary>
    private static AnShengCommandRejectReason ReadRejectReason(JsonElement data, string raw)
    {
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

    /// <summary>
    /// 断言这条命令在 <c>AnShengCommandRecords</c> 里留下了 <c>Rejected</c> 终态记录，
    /// 且 FrameId/SentAt 恒为 null（零发布的持久化证据）。
    /// </summary>
    private async Task AssertRejectedRecordAsync(JsonElement data, string raw, string expectedMethod)
    {
        TryGetPropertyIgnoreCase(data, "commandId", out var commandIdElement).Should().BeTrue(
            "CommandId 从命令被受理那一刻就存在，缺失说明控制器没把 result 带回来。" +
            $"实际响应：{Truncate(raw)}");

        var commandId = commandIdElement.GetString();
        commandId.Should().NotBeNullOrWhiteSpace($"CommandId 不得为空串。实际响应：{Truncate(raw)}");

        var record = await QueryDbAsync(db => db.AnShengCommandRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.CommandId == commandId));

        record.Should().NotBeNull("拒绝也必须留痕，否则「这条命令为什么没发出去」无从追溯");
        record!.Status.Should().Be(
            AnShengCommandStatus.Rejected, "被 Guard 拦下的终态是 Rejected，不是 Failed");
        record.Method.Should().Be(expectedMethod, "留痕的 method 必须与请求一致");
        record.RejectReason.Should().NotBeNull("Rejected 记录必须落拒绝原因列");
        record.FrameId.Should().BeNull("未出网就没有帧号——这是「零发布」的持久化证据");
        record.SentAt.Should().BeNull("未出网就没有发送时刻");
        record.CompletedAt.Should().NotBeNull("Rejected 是终态，必须带完成时刻");
        record.AppCode.Should().Be(
            SharedTestConstants.AppCode, "租户码缺失会让这条记录在任何租户视图里都查不到");
    }

    /// <summary>
    /// 字节级对照两条出网报文（忽略 frameId 的随机性，但校验其形态；校验 timestamp 为合理秒级整数）。
    /// 用于验收 ①② 钉死「报文与协议文档一致」——这是 T8 设计里明确声明的断言点。
    /// </summary>
    /// <param name="actualJson">实际出网报文（来自响应 data.Payload）。</param>
    /// <param name="expectedJson">期望报文（来自 AnShengCommandBuilder 同参数构建）。</param>
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
                // frameId 每次随机生成，只断言形态（16 位小写十六进制）。
                actualValue.ValueKind.Should().Be(JsonValueKind.String);
                actualValue.GetString().Should().MatchRegex(
                    "^[0-9a-f]{16}$", "frameId 必须是 16 位小写十六进制串（协议强制）");
            }
            else if (string.Equals(name, "timestamp", StringComparison.Ordinal))
            {
                // 4G 款必须注入秒级 timestamp；WiFi 款整段省略（字段集合等式已隐含此约束）。
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

    /// <summary>以 4G 款为口径，构建 action 的期望出网报文（供字节级对照）。</summary>
    private static string BuildExpectedActionPayload(
        string imei, int slotNum, string action, bool? hasStopDelayTask)
        => new AnShengCommandBuilder()
            .BuildAction(imei, slotNum, action, hasStopDelayTask, AnShengDeviceKind.Switch4G)
            .Payload;

    /// <summary>以 4G 款为口径，构建 actions 的期望出网报文（供字节级对照）。</summary>
    private static string BuildExpectedActionsPayload(
        string imei, IReadOnlyList<int> slotNums, string action, bool? hasStopDelayTask)
        => new AnShengCommandBuilder()
            .BuildActions(imei, slotNums, action, hasStopDelayTask, AnShengDeviceKind.Switch4G)
            .Payload;

    /// <summary>
    /// 插入一条设备能力档案（决策 D7：品类必须显式落档，否则 Guard 走「未知即放行」的降级分支）。
    /// AppCode 必须显式赋值：播种走 DI 作用域直连 AppDbContext，此时 TenantContext 为空，
    /// 全局过滤器不会代填；漏了它，服务层按租户查档案时会查不到。
    /// </summary>
    private Task SeedProfileAsync(
        long deviceId, string imei, AnShengDeviceKind kind, int slotAmount, string version)
        => ExecuteDbAsync(async db =>
        {
            db.AnShengDeviceProfiles.Add(new AnShengDeviceProfile
            {
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

    /// <summary>预置一条延时任务镜像行（验收 ④ 的对照基线）。AppCode 同设备必同租户。</summary>
    private Task SeedDelayTaskMirrorAsync(long deviceId, int slotNum, bool enable)
        => ExecuteDbAsync(async db =>
        {
            db.Set<AnShengDelayTask>().Add(new AnShengDelayTask
            {
                AppCode = SharedTestConstants.AppCode,
                DeviceId = deviceId,
                SlotNum = slotNum,
                Enable = enable
            });
            await db.SaveChangesAsync();
        });

    /// <summary>大小写不敏感地取 JSON 属性（兼容 camelCase / PascalCase 命名）。</summary>
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
    private static string Truncate(string s) => s.Length <= 800 ? s : s[..800] + "…";

    /// <summary>
    /// 递归结构相等比较。
    ///
    /// 本环境引用的 <see cref="JsonElement"/> 未暴露 <c>DeepEquals</c>（CS1061），
    /// 故自实现一份纯结构比较（值语义，忽略成员书写顺序），供字节级报文断言复用。
    /// 仅覆盖本协议报文可能用到的 ValueKind：Object / Array / String / Number / Boolean / Null。
    /// </summary>
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
                return a.GetString() == b.GetString();

            case JsonValueKind.Number:
                // 比较原始文本，规避浮点/整数格式化差异（本场景均为整数秒级时间戳与插槽号）。
                return a.GetRawText() == b.GetRawText();

            case JsonValueKind.True:
            case JsonValueKind.False:
                return a.GetBoolean() == b.GetBoolean();

            case JsonValueKind.Null:
                return true;

            default:
                return a.GetRawText() == b.GetRawText();
        }
    }
}
