using System;
using System.Collections.Generic;
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
/// T10「安圣定时任务（仅后端）」逐条验收（设计文档 Phase 4 的 6 条验收标准 + slotNum 越界）。
///
/// 复用 T8 的验收范式（<c>AnShengSwitchAcceptanceTests</c>）：TestServer + 真实 MySQL 一次性 schema +
/// RecordingAnShengAdapter + AnShengUplinkPipeline 静态总线，不新增任何基础设施。
///
/// ═════════════════════════════════════════════════════════════════════════
/// 【验收落点】
/// ═════════════════════════════════════════════════════════════════════════
///   ① 仅 Switch4G 放行：SwitchWiFi / SpeakerWiFi 写端点 → 200 + code=400 + RejectedByKind + 零出网；
///   ② confirm=true 二次确认：不带 confirm → RejectedByConfirm + 零出网；
///   ③ 保存后自动回读：set 成功 ≥120ms 后自动追加 get，设备真值覆盖镜像并 bump SyncedAt；
///   ④ timeEvent 就地更新：注入上行 → 对应 (slotNum, kind, taskIndex) 镜像更新且<b>不额外发命令</b>；
///   ⑤ 409 并发冲突：RowVersion 过期 → HTTP 409 + 信封 code=409；
///   ⑥ 24h 过期提示：SyncedAt 超阈值 → IsStale=true；
///   ⑦ slotNum 越界：&lt;1 由控制器拦、&gt;SlotAmount 由 Guard 拦，均零出网。
///
/// ═════════════════════════════════════════════════════════════════════════
/// 【铁律复核点（顺带钉死）】
/// ═════════════════════════════════════════════════════════════════════════
///   铁律①：写后回读与 timeEvent 跑在后台作用域，镜像行 AppCode 必须显式落对（断言 AppCode）；
///   铁律②：业务拒绝一律 HTTP 200 + ApiResponse.Code=400，只有并发冲突走 409；
///   铁律③：出网报文与 <see cref="AnShengCommandBuilder"/> 的 T10 四方法字节级一致；
///   契约：<c>TaskKind</c> / <c>rejectReason</c> 以枚举<b>字符串</b>出网（全局 JsonStringEnumConverter）。
/// </summary>
[Collection(SharedTestConstants.CollectionName)]
public sealed class AnShengTimeTaskAcceptanceTests : IntegrationTestBase
{
    /// <summary>管道排空等待上限。真实 MySQL 首次建连 + EF 首次编译查询可能慢，给足余量。</summary>
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(15);

    /// <summary>轮询等待「自动回读出网」的上限（写后回读延迟默认 120ms）。</summary>
    private static readonly TimeSpan ReadbackPollTimeout = TimeSpan.FromSeconds(8);

    /// <summary>安圣协议方法名：整表读定时任务（写后回读用）。</summary>
    private const string MethodGetTimeTasks = "getTimeTasks";

    /// <summary>安圣协议方法名：整表覆盖定时任务。</summary>
    private const string MethodSetTimeTasks = "setTimeTasks";

    /// <summary>安圣协议方法名：单插槽读定时任务（写后回读用）。</summary>
    private const string MethodGetSlotTimeTasks = "getSlotTimeTasks";

    /// <summary>安圣协议方法名：单插槽设置定时任务。</summary>
    private const string MethodSetSlotTimeTasks = "setSlotTimeTasks";

    /// <summary>安圣协议方法名：定时任务触发事件上报。</summary>
    private const string MethodTimeEvent = "timeEvent";

    /// <summary>拒绝态业务码（设计 §8.3：HTTP 恒 200，业务码 400）。</summary>
    private const int RejectedCode = 400;

    /// <summary>测试设备插槽数。</summary>
    private const int SlotAmount = 4;

    public AnShengTimeTaskAcceptanceTests(DatabaseFixture fixture) : base(fixture)
    {
    }

    /// <summary>
    /// 用例结束后再清一次进程级静态状态：本文件的用例会往在途表登记 frameId
    /// （setTimeTasks / getTimeTasks 回读），残留会污染后续用例的路由判定。
    /// </summary>
    public override Task DisposeAsync()
    {
        StaticStateResetter.ResetAll(Fixture.Factory.Services);
        return base.DisposeAsync();
    }

    // ─────────────────────────────────────────────────────────────
    // 验收 ①：仅 Switch4G 放行
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 验收 ①-a：<c>SwitchWiFi</c>（WiFi 开关，G4 表明确不支持定时任务）调整表写端点，
    /// 断言拒绝信封 + <c>RejectedByKind</c> + 零出网。
    /// </summary>
    [Fact(DisplayName = "验收① SwitchWiFi 调 setTimeTasks → 200 + code=400 + RejectedByKind + 零出网")]
    public async Task SwitchWiFi_SetTimeTasks_RejectedByKind_ZeroPublish()
    {
        await SeedProfileAsync(AnShengDeviceKind.SwitchWiFi, SlotAmount);
        var client = Client.AsAdmin();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/ansheng/{Seed.DeviceId}/time-tasks", BuildSetTimeTasksRequest(confirm: true));

        var (data, raw) = await AssertRejectionEnvelopeAsync(response);
        ReadRejectReason(data, raw).Should().Be(
            AnShengCommandRejectReason.RejectedByKind,
            "G4 定时任务组仅 Switch4G 支持；WiFi 开关必须以品类维度拒绝。实际响应：" + Truncate(raw));

        AssertRejectReasonIsEnumString(data, raw, nameof(AnShengCommandRejectReason.RejectedByKind));

        Adapter.Sent.Should().BeEmpty("被 Guard 拦下的命令必须零 MQTT 发布");
    }

    /// <summary>
    /// 验收 ①-b：<c>SpeakerWiFi</c>（WiFi 喇叭）调单插槽写端点，断言同样被品类拒绝且零出网。
    /// </summary>
    [Fact(DisplayName = "验收① SpeakerWiFi 调 setSlotTimeTasks → 200 + code=400 + RejectedByKind + 零出网")]
    public async Task SpeakerWiFi_SetSlotTimeTasks_RejectedByKind_ZeroPublish()
    {
        await SeedProfileAsync(AnShengDeviceKind.SpeakerWiFi, slotAmount: 0);
        var client = Client.AsAdmin();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/ansheng/{Seed.DeviceId}/time-tasks/1", BuildSetSlotTimeTasksRequest(confirm: true));

        var (data, raw) = await AssertRejectionEnvelopeAsync(response);
        ReadRejectReason(data, raw).Should().Be(
            AnShengCommandRejectReason.RejectedByKind,
            "喇叭类不具备定时任务能力。实际响应：" + Truncate(raw));

        Adapter.Sent.Should().BeEmpty("被 Guard 拦下的命令必须零 MQTT 发布");
    }

    /// <summary>
    /// 验收 ①-c：<c>Switch4G</c> 正常放行；同时钉死铁律③——出网报文与
    /// <see cref="AnShengCommandBuilder.BuildSetSlotTimeTasks"/> 字节级一致。
    /// 顺带覆盖「读端点对空镜像返回空集合而非 500」。
    /// </summary>
    [Fact(DisplayName = "验收① Switch4G 放行 + 出网报文对齐 Builder；读端点空镜像返回空集合")]
    public async Task Switch4G_Accepted_And_PayloadMatchesBuilder()
    {
        await SeedProfileAsync(AnShengDeviceKind.Switch4G, SlotAmount);
        var client = Client.AsAdmin();

        // 读端点：镜像为空时必须 200 + 空集合（不是 500、不是 null）
        var getAll = await client.GetAsync($"/api/v1/ansheng/{Seed.DeviceId}/time-tasks");
        getAll.StatusCode.Should().Be(HttpStatusCode.OK);
        var getAllResult = await ReadAsync<ApiResponse<List<AnShengSlotTimeTaskSetDto>>>(getAll);
        getAllResult!.Code.Should().Be(200);
        getAllResult.Data.Should().NotBeNull().And.BeEmpty("无镜像时应返回空集合");

        // 写端点：Switch4G 必须受理
        const int slotNum = 2;
        var response = await client.PostAsJsonAsync(
            $"/api/v1/ansheng/{Seed.DeviceId}/time-tasks/{slotNum}",
            BuildSetSlotTimeTasksRequest(confirm: true));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await ReadAsync<ApiResponse<AnShengTimeTaskResultDto>>(response);
        result!.Code.Should().Be(200, "4G 开关支持 setSlotTimeTasks，必须受理");
        result.Data!.Accepted.Should().BeTrue();

        // 铁律③：整帧与 Builder.BuildSetSlotTimeTasks 一致（字段集合 + 业务字段值）
        var expected = new AnShengCommandBuilder()
            .BuildSetSlotTimeTasks(
                Seed.Imei, slotNum,
                new object?[] { BuildWireTimeTask() },
                new object?[] { BuildWireLoopTimeTask() },
                AnShengDeviceKind.Switch4G)
            .Payload;

        AssertFramesStructurallyEqual(result.Data.Payload!, expected);
    }

    // ─────────────────────────────────────────────────────────────
    // 验收 ②：confirm=true 二次确认
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 验收 ②：两个写端点在<b>不带</b> <c>confirm=true</c> 时都必须以
    /// <see cref="AnShengCommandRejectReason.RejectedByConfirm"/> 拒绝，且命令零出网。
    /// </summary>
    [Theory(DisplayName = "验收② 无 confirm=true → RejectedByConfirm + 零出网")]
    [InlineData("time-tasks")]
    [InlineData("time-tasks/1")]
    public async Task MissingConfirm_RejectedByConfirm_ZeroPublish(string path)
    {
        await SeedProfileAsync(AnShengDeviceKind.Switch4G, SlotAmount);
        var client = Client.AsAdmin();

        object body = path == "time-tasks"
            ? BuildSetTimeTasksRequest(confirm: false)
            : BuildSetSlotTimeTasksRequest(confirm: false);

        var response = await client.PostAsJsonAsync($"/api/v1/ansheng/{Seed.DeviceId}/{path}", body);

        var (data, raw) = await AssertRejectionEnvelopeAsync(response);
        ReadRejectReason(data, raw).Should().Be(
            AnShengCommandRejectReason.RejectedByConfirm,
            "整表覆盖是高危操作，未二次确认必须拒绝。实际响应：" + Truncate(raw));

        Adapter.Sent.Should().BeEmpty("未确认的高危命令绝不允许出网");

        // 拒绝态不得留下任何镜像行（乐观镜像只在命令出网后才写）
        var rows = await QueryDbAsync(db => db.Set<AnShengTimeTask>()
            .IgnoreQueryFilters().CountAsync(t => t.DeviceId == Seed.DeviceId));
        rows.Should().Be(0, "未出网就不该有乐观镜像");
    }

    // ─────────────────────────────────────────────────────────────
    // 验收 ③：保存后自动回读，设备真值覆盖镜像
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 验收 ③-a（整表）：<c>setTimeTasks</c> 成功后平台必须自动追加 <c>getTimeTasks</c>；
    /// 设备应答里的真值要<b>覆盖</b>掉之前写入的乐观镜像，并 bump <c>SyncedAt</c>。
    /// 同时钉死 §7-R9：<c>tasks[]</c> 下标 i ⇒ 插槽 i+1。
    /// 并顺带复核铁律①：后台作用域写回的行必须带正确 AppCode。
    /// </summary>
    [Fact(DisplayName = "验收③ setTimeTasks 后自动回读 getTimeTasks，设备真值覆盖镜像 + bump SyncedAt")]
    public async Task SetTimeTasks_AutoReadback_OverwritesMirrorWithDeviceTruth()
    {
        await SeedProfileAsync(AnShengDeviceKind.Switch4G, SlotAmount);
        var client = Client.AsAdmin();

        // Act ① —— 整表覆盖：插槽 1 放一条普通定时（hour=8），插槽 2 放一条循环定时
        var response = await client.PostAsJsonAsync(
            $"/api/v1/ansheng/{Seed.DeviceId}/time-tasks", BuildSetTimeTasksRequest(confirm: true));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await ReadAsync<ApiResponse<AnShengTimeTaskResultDto>>(response);
        result!.Code.Should().Be(200);
        result.Data!.Accepted.Should().BeTrue();

        // 乐观镜像先落「请求意图」：插槽 1 普通定时 hour 应为请求里的 8
        var optimistic = await QueryDbAsync(db => db.Set<AnShengTimeTask>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.DeviceId == Seed.DeviceId
                && t.SlotNum == 1 && t.TaskKind == AnShengTimeTaskKind.Normal && t.TaskIndex == 1));
        optimistic.Should().NotBeNull("命令已出网，必须先落一份乐观镜像（验收 ③ 的前置）");
        optimistic!.Hour.Should().Be(8, "乐观镜像取自请求意图");
        var optimisticSyncedAt = optimistic.SyncedAt;

        // Act ② —— 等平台自动追加的回读命令
        var readback = await WaitForReadbackAsync(MethodGetTimeTasks, ReadbackPollTimeout);
        readback.FrameId.Should().NotBeNullOrWhiteSpace("回读命令必须带 frameId 才能被路由成 Response");

        // Act ③ —— 设备回真值：插槽 1 的普通定时实际是 21:30（与乐观值 8:00 不同）
        var replyPayload = $$"""
        {"method":"{{MethodGetTimeTasks}}","result":"ok","imei":"{{Seed.Imei}}","frameId":"{{readback.FrameId}}","tasks":[{"timeTasks":[{"id":"dev-1","enable":true,"weekDays":[1,2,3],"hour":21,"minute":30,"action":"off","uploadEnable":true}],"loopTimeTasks":[]},{"timeTasks":[],"loopTimeTasks":[{"id":"dev-2","enable":true,"weekDays":[6,7],"sHour":7,"sMinute":0,"eHour":19,"eMinute":0,"onMins":15,"offMins":45}]}]}
        """;
        Adapter.RaiseAnShengUplink(Seed.Imei, MethodGetTimeTasks, replyPayload);

        (await Pipeline.DrainAsync(DrainTimeout))
            .Should().BeTrue("上行管道应在 {0}s 内处理完 getTimeTasks 应答", DrainTimeout.TotalSeconds);

        // Assert —— 设备真值已覆盖乐观镜像
        var mirror = await QueryDbAsync(db => db.Set<AnShengTimeTask>()
            .IgnoreQueryFilters()
            .Where(t => t.DeviceId == Seed.DeviceId)
            .OrderBy(t => t.SlotNum).ThenBy(t => t.TaskKind).ThenBy(t => t.TaskIndex)
            .ToListAsync());

        var slot1Normal = mirror.SingleOrDefault(t =>
            t.SlotNum == 1 && t.TaskKind == AnShengTimeTaskKind.Normal && t.TaskIndex == 1);
        slot1Normal.Should().NotBeNull("tasks[0] ⇒ 插槽 1（§7-R9）");
        slot1Normal!.Hour.Should().Be(21, "设备是权威：回读真值必须覆盖乐观镜像的 8 点");
        slot1Normal.Minute.Should().Be(30);
        slot1Normal.Action.Should().Be("off");
        slot1Normal.TaskId.Should().Be("dev-1", "设备分配的任务 id 必须落库");

        var slot2Loop = mirror.SingleOrDefault(t =>
            t.SlotNum == 2 && t.TaskKind == AnShengTimeTaskKind.Loop && t.TaskIndex == 1);
        slot2Loop.Should().NotBeNull("tasks[1] ⇒ 插槽 2（§7-R9）");
        slot2Loop!.OnMins.Should().Be(15);
        slot2Loop.OffMins.Should().Be(45);

        // 铁律①：后台作用域写回，AppCode 必须显式落对，否则任何租户视图都查不到
        mirror.Should().OnlyContain(t => t.AppCode == SharedTestConstants.AppCode,
            "写后回读跑在后台作用域（TenantContext 为 null），必须显式赋 AppCode");

        // SyncedAt 必须被 bump（设备真值同步时刻）
        slot1Normal.SyncedAt.Should().BeOnOrAfter(optimisticSyncedAt,
            "回读覆盖必须刷新 SyncedAt，否则 24h 过期提示会误报");
    }

    /// <summary>
    /// 验收 ③-b（单插槽）：<c>setSlotTimeTasks</c> 成功后自动回读 <c>getSlotTimeTasks</c>，
    /// 设备应答必须落回<b>被写的那个插槽</b>。
    ///
    /// 【关键】协议文档 asopen.md L3627-3643 的 <c>getSlotTimeTasks</c> <b>应答参数表没有 slotNum</b>
    /// （只有 method/result/loopTimeTasks/timeTasks/imei/frameId/timestamp）。因此平台必须凭
    /// 「自己发出的那条回读命令的 slotNum」来定位插槽，绝不能指望应答里带。
    /// 回读结果一旦落到插槽 0，等于真值永远进不了被查询的插槽——镜像形同虚设。
    /// </summary>
    [Fact(DisplayName = "验收③ setSlotTimeTasks 后自动回读，真值必须落回被写插槽（不得落到幽灵插槽 0）")]
    public async Task SetSlotTimeTasks_AutoReadback_WritesBackToRequestedSlot()
    {
        await SeedProfileAsync(AnShengDeviceKind.Switch4G, SlotAmount);
        var client = Client.AsAdmin();
        const int slotNum = 3;

        var response = await client.PostAsJsonAsync(
            $"/api/v1/ansheng/{Seed.DeviceId}/time-tasks/{slotNum}",
            BuildSetSlotTimeTasksRequest(confirm: true));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await ReadAsync<ApiResponse<AnShengTimeTaskResultDto>>(response);
        result!.Data!.Accepted.Should().BeTrue();

        // 平台自动追加的回读命令必须带上 slotNum —— 这是它唯一能"记住查的是哪个插槽"的地方
        var readback = await WaitForReadbackAsync(MethodGetSlotTimeTasks, ReadbackPollTimeout);
        ReadIntParam(readback.Parameters, "slotNum").Should().Be(
            slotNum, "回读命令必须指明查哪个插槽，否则设备不知道回哪一路");

        // 设备应答：按协议原样——**不带 slotNum**
        var replyPayload = $$"""
        {"method":"{{MethodGetSlotTimeTasks}}","result":"ok","imei":"{{Seed.Imei}}","frameId":"{{readback.FrameId}}","timeTasks":[{"id":"dev-s3","enable":true,"weekDays":[1],"hour":6,"minute":15,"action":"on","uploadEnable":true}],"loopTimeTasks":[]}
        """;
        Adapter.RaiseAnShengUplink(Seed.Imei, MethodGetSlotTimeTasks, replyPayload);

        (await Pipeline.DrainAsync(DrainTimeout))
            .Should().BeTrue("上行管道应在 {0}s 内处理完 getSlotTimeTasks 应答", DrainTimeout.TotalSeconds);

        var mirror = await QueryDbAsync(db => db.Set<AnShengTimeTask>()
            .IgnoreQueryFilters()
            .Where(t => t.DeviceId == Seed.DeviceId)
            .ToListAsync());

        mirror.Should().NotContain(t => t.SlotNum <= 0,
            "getSlotTimeTasks 应答不含 slotNum（协议 asopen.md L3627-3643）；" +
            "平台若从应答里取 slotNum 就会恒为 0，把真值写进不存在的幽灵插槽");

        var written = mirror.SingleOrDefault(t =>
            t.SlotNum == slotNum && t.TaskKind == AnShengTimeTaskKind.Normal && t.TaskIndex == 1);
        written.Should().NotBeNull($"回读真值必须落回被写的插槽 {slotNum}");
        written!.TaskId.Should().Be("dev-s3", "设备真值应覆盖乐观镜像");
        written.Hour.Should().Be(6);
        written.Minute.Should().Be(15);
        written.AppCode.Should().Be(SharedTestConstants.AppCode, "铁律①：后台作用域必须显式落租户码");
    }

    // ─────────────────────────────────────────────────────────────
    // 验收 ④：timeEvent 上行就地更新镜像，且不额外发命令
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 验收 ④：注入 <c>timeEvent</c> 上行事件，按 <c>(slotNum, taskKind, taskIndex)</c> 就地更新
    /// 对应镜像行；<b>不得</b>因此触发任何新的下行命令（事件是设备主动告知，不是查询触发器）。
    /// 顺带复核铁律①（后台作用域 AppCode）。
    /// </summary>
    [Fact(DisplayName = "验收④ timeEvent 上行就地更新镜像 + 零额外下行命令")]
    public async Task TimeEvent_UpdatesMirrorInPlace_WithoutSendingCommand()
    {
        await SeedProfileAsync(AnShengDeviceKind.Switch4G, SlotAmount);

        // Arrange —— 预置一条旧镜像：插槽 2 / 普通 / 序号 1，8:00 且未启用
        await SeedTimeTaskMirrorAsync(slotNum: 2, kind: AnShengTimeTaskKind.Normal, taskIndex: 1,
            configure: t =>
            {
                t.Hour = 8;
                t.Minute = 0;
                t.Enable = false;
                t.Action = "on";
                t.TaskId = "old-id";
                t.SyncedAt = DateTime.UtcNow.AddHours(-3);
            });

        var sentBefore = Adapter.Sent.Count;

        // Act —— 设备上报：该定时任务实际是 22:45 且已启用（无 frameId ⇒ 走 Event 分支）
        // 用序列化构造而非原始字符串：报文尾部连续的 }} 会与 $$""" 的插值定界符冲突。
        var payload = JsonSerializer.Serialize(new
        {
            method = MethodTimeEvent,
            imei = Seed.Imei,
            slotNum = 2,
            taskIndex = 1,
            slots = new[] { 1, 0, 0, 0 },
            task = new
            {
                id = "dev-evt",
                enable = true,
                weekDays = new[] { 1, 2, 3, 4, 5 },
                hour = 22,
                minute = 45,
                action = "off",
                uploadEnable = true
            }
        });
        Adapter.RaiseAnShengUplink(Seed.Imei, MethodTimeEvent, payload);

        (await Pipeline.DrainAsync(DrainTimeout))
            .Should().BeTrue("上行管道应在 {0}s 内处理完 timeEvent", DrainTimeout.TotalSeconds);

        // Assert (a) —— 就地更新，不新增行
        var rows = await QueryDbAsync(db => db.Set<AnShengTimeTask>()
            .IgnoreQueryFilters()
            .Where(t => t.DeviceId == Seed.DeviceId)
            .ToListAsync());

        rows.Should().HaveCount(1, "timeEvent 是就地更新，不该在同一 (插槽,类型,序号) 上再造一行");

        var row = rows[0];
        row.SlotNum.Should().Be(2);
        row.TaskKind.Should().Be(AnShengTimeTaskKind.Normal, "无 onMins/sHour 等字段 ⇒ 判定为普通定时");
        row.TaskIndex.Should().Be(1);
        row.Hour.Should().Be(22, "设备权威：事件真值必须覆盖镜像旧值 8");
        row.Minute.Should().Be(45);
        row.Enable.Should().BeTrue();
        row.Action.Should().Be("off");
        row.TaskId.Should().Be("dev-evt");
        row.AppCode.Should().Be(SharedTestConstants.AppCode, "铁律①：后台作用域必须显式落租户码");

        // Assert (b) —— 零额外下行：事件处理链路不得反向触发命令
        Adapter.Sent.Count.Should().Be(sentBefore,
            "timeEvent 只更新镜像，绝不能因此再发任何命令（否则设备一上报就被平台反打一轮）");
    }

    // ─────────────────────────────────────────────────────────────
    // 验收 ⑤：乐观并发冲突 → HTTP 409
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 验收 ⑤-a（单插槽）：携带过期 <c>rowVersion</c> 下发，必须返回<b>真正的 HTTP 409</b>
    /// （而非业务拒绝的 200），信封 <c>code=409</c>。这是铁律②唯一允许的非 200 出口。
    /// </summary>
    [Fact(DisplayName = "验收⑤ 单插槽携带过期 rowVersion → HTTP 409 + 信封 code=409")]
    public async Task SetSlotTimeTasks_StaleRowVersion_Returns409()
    {
        await SeedProfileAsync(AnShengDeviceKind.Switch4G, SlotAmount);

        // Arrange —— 插槽 2 已有镜像，当前令牌 7
        await SeedTimeTaskMirrorAsync(slotNum: 2, kind: AnShengTimeTaskKind.Normal, taskIndex: 1,
            configure: t => t.RowVersion = 7);

        var client = Client.AsAdmin();

        // Act —— 客户端拿着过期令牌 999 来写
        var response = await client.PostAsJsonAsync(
            $"/api/v1/ansheng/{Seed.DeviceId}/time-tasks/2",
            BuildSetSlotTimeTasksRequest(confirm: true, rowVersion: 999));

        var raw = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(
            HttpStatusCode.Conflict,
            "乐观并发冲突是全站唯一允许返回非 200 的业务出口（设计 §8.3）。实际响应：" + Truncate(raw));

        using var doc = JsonDocument.Parse(raw);
        TryGetPropertyIgnoreCase(doc.RootElement, "code", out var code).Should().BeTrue();
        code.GetInt32().Should().Be(409, "信封业务码需与 HTTP 状态一致，实际响应：" + Truncate(raw));

        TryGetPropertyIgnoreCase(doc.RootElement, "data", out var data).Should().BeTrue();
        TryGetPropertyIgnoreCase(data, "concurrencyConflict", out var flag).Should().BeTrue(
            "调用方需据此决定是否刷新后重试，实际响应：" + Truncate(raw));
        flag.GetBoolean().Should().BeTrue();
    }

    /// <summary>
    /// 验收 ⑤-b（整表）：整表覆盖端点同样声明支持 <c>rowVersion</c>（见
    /// <see cref="AnShengSetTimeTasksRequest.RowVersion"/> 的 XML 注释「返回 409（验收 #5）」），
    /// 因此过期令牌也必须走 409，而不能降级成业务拒绝的 200。
    /// </summary>
    [Fact(DisplayName = "验收⑤ 整表携带过期 rowVersion → HTTP 409 + 信封 code=409")]
    public async Task SetTimeTasks_StaleRowVersion_Returns409()
    {
        await SeedProfileAsync(AnShengDeviceKind.Switch4G, SlotAmount);

        // 整表请求会覆盖插槽 1、2；在插槽 1 预置一条镜像，令牌 7
        await SeedTimeTaskMirrorAsync(slotNum: 1, kind: AnShengTimeTaskKind.Normal, taskIndex: 1,
            configure: t => t.RowVersion = 7);

        var client = Client.AsAdmin();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/ansheng/{Seed.DeviceId}/time-tasks",
            BuildSetTimeTasksRequest(confirm: true, rowVersion: 999));

        var raw = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(
            HttpStatusCode.Conflict,
            "服务层 SetTimeTasksAsync 已置 ConcurrencyConflict=true，控制器必须把它映射成 409；" +
            "若返回 200 说明整表端点漏了 ConcurrencyConflict 分支。实际响应：" + Truncate(raw));

        using var doc = JsonDocument.Parse(raw);
        TryGetPropertyIgnoreCase(doc.RootElement, "code", out var code).Should().BeTrue();
        code.GetInt32().Should().Be(409, "实际响应：" + Truncate(raw));
    }

    // ─────────────────────────────────────────────────────────────
    // 验收 ⑥：>24h 未同步 → IsStale=true
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 验收 ⑥：镜像 <c>SyncedAt</c> 超过 24h 阈值的行必须以 <c>IsStale=true</c> 出网，
    /// 阈值内的行为 <c>false</c>。顺带钉死出网契约：<c>taskKind</c> 必须是枚举<b>字符串</b>
    /// （"Normal"/"Loop"），不是魔数 0/1。
    /// </summary>
    [Fact(DisplayName = "验收⑥ SyncedAt>24h → IsStale=true；taskKind 以枚举字符串出网")]
    public async Task GetTimeTasks_MarksStaleMirror_AndSerializesEnumAsString()
    {
        await SeedProfileAsync(AnShengDeviceKind.Switch4G, SlotAmount);

        // 插槽 1：25 小时前同步 ⇒ 陈旧；插槽 2：刚同步 ⇒ 新鲜（且为循环类型，验证枚举字符串）
        await SeedTimeTaskMirrorAsync(slotNum: 1, kind: AnShengTimeTaskKind.Normal, taskIndex: 1,
            configure: t => t.SyncedAt = DateTime.UtcNow.AddHours(-25));
        await SeedTimeTaskMirrorAsync(slotNum: 2, kind: AnShengTimeTaskKind.Loop, taskIndex: 1,
            configure: t => t.SyncedAt = DateTime.UtcNow.AddMinutes(-5));

        var client = Client.AsAdmin();
        var response = await client.GetAsync($"/api/v1/ansheng/{Seed.DeviceId}/time-tasks");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var raw = await response.Content.ReadAsStringAsync();
        var result = await ReadAsync<ApiResponse<List<AnShengSlotTimeTaskSetDto>>>(response);
        result!.Code.Should().Be(200);
        result.Data.Should().HaveCount(2, "两个插槽各有一条镜像");

        var slot1 = result.Data!.Single(s => s.SlotNum == 1);
        slot1.TimeTasks.Single().IsStale.Should().BeTrue(
            "25h 前同步的镜像已超 24h 阈值，必须标记为陈旧，否则用户会把过期数据当真值");

        var slot2 = result.Data!.Single(s => s.SlotNum == 2);
        slot2.LoopTimeTasks.Single().IsStale.Should().BeFalse("5 分钟前同步的镜像是新鲜的");

        // 出网契约：taskKind 必须是字符串枚举名
        using var doc = JsonDocument.Parse(raw);
        var kindTokens = CollectPropertyValues(doc.RootElement, "taskKind").ToList();
        kindTokens.Should().NotBeEmpty("响应里必须能找到 taskKind 字段");
        kindTokens.Should().OnlyContain(
            e => e.ValueKind == JsonValueKind.String,
            "全局 JsonStringEnumConverter 要求枚举以字符串出网；出现数字说明转换器被撤或被局部覆盖。响应：" + Truncate(raw));
        kindTokens.Select(e => e.GetString()).Should().BeSubsetOf(
            new[] { nameof(AnShengTimeTaskKind.Normal), nameof(AnShengTimeTaskKind.Loop) });
    }

    // ─────────────────────────────────────────────────────────────
    // 验收 ⑦：slotNum 越界（下界由控制器拦、上界由 Guard 拦）
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 验收 ⑦-a：<c>slotNum &lt; 1</c> 由控制器在<b>下发前</b>拦下，
    /// 返回 HTTP 200 + 业务码 400（铁律②），且命令零出网。读写端点都要覆盖。
    /// </summary>
    [Theory(DisplayName = "验收⑦ slotNum<1 被控制器前置拦截 → 200 + code=400 + 零出网")]
    [InlineData("POST")]
    [InlineData("GET")]
    public async Task SlotNumBelowOne_RejectedBeforeDispatch_ZeroPublish(string verb)
    {
        await SeedProfileAsync(AnShengDeviceKind.Switch4G, SlotAmount);
        var client = Client.AsAdmin();
        var url = $"/api/v1/ansheng/{Seed.DeviceId}/time-tasks/0";

        var response = verb == "POST"
            ? await client.PostAsJsonAsync(url, BuildSetSlotTimeTasksRequest(confirm: true))
            : await client.GetAsync(url);

        var raw = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "铁律②：业务校验失败一律 HTTP 200 + 业务码 400；返回裸 400 说明用了 MVC 的 BadRequest()。响应：" + Truncate(raw));

        using var doc = JsonDocument.Parse(raw);
        TryGetPropertyIgnoreCase(doc.RootElement, "code", out var code).Should().BeTrue();
        code.GetInt32().Should().Be(RejectedCode, "非法 slotNum 必须以业务码 400 拒绝。响应：" + Truncate(raw));

        Adapter.Sent.Should().BeEmpty("非法插槽号必须在下发前拦住，一条命令都不能出网");
    }

    /// <summary>
    /// 验收 ⑦-b：<c>slotNum &gt; SlotAmount</c> 由 Guard 的 <c>CheckSlotRange</c> 依据设备档案拦下，
    /// 以 <see cref="AnShengCommandRejectReason.RejectedByValidation"/> 拒绝且零出网。
    /// </summary>
    [Fact(DisplayName = "验收⑦ slotNum>SlotAmount 被 Guard 拦截 → RejectedByValidation + 零出网")]
    public async Task SlotNumAboveSlotAmount_RejectedByGuard_ZeroPublish()
    {
        await SeedProfileAsync(AnShengDeviceKind.Switch4G, SlotAmount);
        var client = Client.AsAdmin();

        // 设备只有 4 路，写第 5 路
        var response = await client.PostAsJsonAsync(
            $"/api/v1/ansheng/{Seed.DeviceId}/time-tasks/{SlotAmount + 1}",
            BuildSetSlotTimeTasksRequest(confirm: true));

        var (data, raw) = await AssertRejectionEnvelopeAsync(response);
        ReadRejectReason(data, raw).Should().Be(
            AnShengCommandRejectReason.RejectedByValidation,
            $"设备档案 SlotAmount={SlotAmount}，第 {SlotAmount + 1} 路不存在，必须按参数校验拒绝。响应：" + Truncate(raw));

        Adapter.Sent.Should().BeEmpty("越界插槽号必须在下发前拦住");

        var rows = await QueryDbAsync(db => db.Set<AnShengTimeTask>()
            .IgnoreQueryFilters().CountAsync(t => t.DeviceId == Seed.DeviceId));
        rows.Should().Be(0, "被拒的命令不得留下乐观镜像");
    }

    // ═════════════════════════════════════════════════════════════
    // 测试辅助（T8 同名 helper 均为 private，无法跨文件复用，故按需复刻）
    // ═════════════════════════════════════════════════════════════

    /// <summary>事件管道单例。断言前必须 <c>DrainAsync</c>，否则读到的是半成品状态。</summary>
    private AnShengUplinkPipeline Pipeline =>
        Fixture.Factory.Services.GetRequiredService<AnShengUplinkPipeline>();

    /// <summary>
    /// 自旋等待平台自动追加的回读命令出现在录制适配器里。
    /// 用轮询而非固定 Sleep：慢机器上不会偶发红，快机器上立刻返回。
    /// </summary>
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
    /// 插入设备能力档案（决策 D7：品类必须显式落档，否则 Guard 走「未知即放行」降级分支）。
    /// AppCode 必须显式赋值：播种走 DI 作用域直连 AppDbContext，此时 TenantContext 为空。
    /// </summary>
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

    /// <summary>预置一条定时任务镜像行，供「覆盖 / 并发 / 过期」类用例做对照基线。</summary>
    private Task SeedTimeTaskMirrorAsync(
        int slotNum, AnShengTimeTaskKind kind, int taskIndex, Action<AnShengTimeTask>? configure = null)
        => ExecuteDbAsync(async db =>
        {
            var row = new AnShengTimeTask
            {
                AppCode = SharedTestConstants.AppCode,
                DeviceId = Seed.DeviceId,
                SlotNum = slotNum,
                TaskKind = kind,
                TaskIndex = taskIndex,
                TaskId = string.Empty,
                Enable = true,
                WeekDays = "[]",
                Action = "on",
                SyncedAt = DateTime.UtcNow,
                RowVersion = 1
            };
            configure?.Invoke(row);
            db.Set<AnShengTimeTask>().Add(row);
            await db.SaveChangesAsync();
        });

    /// <summary>构造整表覆盖请求：插槽 1 一条普通定时（8:00），插槽 2 一条循环定时。</summary>
    private static AnShengSetTimeTasksRequest BuildSetTimeTasksRequest(bool confirm, long? rowVersion = null)
        => new()
        {
            Confirm = confirm,
            RowVersion = rowVersion,
            Slots = new List<AnShengSlotTimeTaskSetRequest>
            {
                new()
                {
                    SlotNum = 1,
                    TimeTasks = new List<AnShengTimeTaskItemRequest> { BuildTimeTaskItemRequest() },
                    LoopTimeTasks = new List<AnShengLoopTimeTaskItemRequest>()
                },
                new()
                {
                    SlotNum = 2,
                    TimeTasks = new List<AnShengTimeTaskItemRequest>(),
                    LoopTimeTasks = new List<AnShengLoopTimeTaskItemRequest> { BuildLoopTimeTaskItemRequest() }
                }
            }
        };

    /// <summary>构造单插槽请求：一条普通定时 + 一条循环定时。</summary>
    private static AnShengSetSlotTimeTasksRequest BuildSetSlotTimeTasksRequest(bool confirm, long? rowVersion = null)
        => new()
        {
            Confirm = confirm,
            RowVersion = rowVersion,
            TimeTasks = new List<AnShengTimeTaskItemRequest> { BuildTimeTaskItemRequest() },
            LoopTimeTasks = new List<AnShengLoopTimeTaskItemRequest> { BuildLoopTimeTaskItemRequest() }
        };

    /// <summary>统一的普通定时任务请求项（与 <see cref="BuildWireTimeTask"/> 一一对应）。</summary>
    private static AnShengTimeTaskItemRequest BuildTimeTaskItemRequest()
        => new()
        {
            Id = null,
            Enable = true,
            WeekDays = new List<int> { 1, 3, 5 },
            Hour = 8,
            Minute = 0,
            Action = "on",
            UploadEnable = true
        };

    /// <summary>统一的循环定时任务请求项（与 <see cref="BuildWireLoopTimeTask"/> 一一对应）。</summary>
    private static AnShengLoopTimeTaskItemRequest BuildLoopTimeTaskItemRequest()
        => new()
        {
            Id = null,
            Enable = true,
            WeekDays = new List<int> { 2, 4 },
            SHour = 9,
            SMinute = 30,
            EHour = 18,
            EMinute = 0,
            OnMins = 10,
            OffMins = 20
        };

    /// <summary>
    /// 普通定时任务的<b>出网线格式</b>（供铁律③ 字节级对照）。
    /// 键名与顺序需与服务层 <c>ToTimeTaskWire</c> 一致；用 Dictionary 而非匿名对象，
    /// 避免序列化命名策略把 <c>sHour</c> 之类的驼峰改写掉。
    /// </summary>
    private static Dictionary<string, object?> BuildWireTimeTask()
        => new(StringComparer.Ordinal)
        {
            ["id"] = null,
            ["enable"] = true,
            ["weekDays"] = new[] { 1, 3, 5 },
            ["hour"] = 8,
            ["minute"] = 0,
            ["action"] = "on",
            ["uploadEnable"] = true
        };

    /// <summary>循环定时任务的出网线格式（供铁律③ 字节级对照）。</summary>
    private static Dictionary<string, object?> BuildWireLoopTimeTask()
        => new(StringComparer.Ordinal)
        {
            ["id"] = null,
            ["enable"] = true,
            ["weekDays"] = new[] { 2, 4 },
            ["sHour"] = 9,
            ["sMinute"] = 30,
            ["eHour"] = 18,
            ["eMinute"] = 0,
            ["onMins"] = 10,
            ["offMins"] = 20
        };

    /// <summary>从出网报文 JSON 里读取一个整数参数（回读命令带没带 slotNum 就靠它验）。</summary>
    private static int? ReadIntParam(string payloadJson, string name)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return null;
        }

        using var doc = JsonDocument.Parse(payloadJson);
        return TryGetPropertyIgnoreCase(doc.RootElement, name, out var el)
            && el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var v)
            ? v
            : null;
    }

    /// <summary>递归收集响应里所有同名属性的值（用于枚举字符串契约的全量体检）。</summary>
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

    /// <summary>断言「拒绝态 HTTP 信封」的公共骨架（设计 §8.3），返回 data 节点与响应原文。</summary>
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
        data.ValueKind.Should().Be(
            JsonValueKind.Object,
            $"data 为 null 意味着控制器丢掉了服务层已填好的 result。实际响应：{Truncate(raw)}");

        return (data, raw);
    }

    /// <summary>
    /// 从 data 读出拒绝原因，兼容枚举的两种上线形态（字符串名 / 整数底值），
    /// 归一成枚举后再断言语义——这样即便转换器被撤，用例也不会假红。
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

    /// <summary>断言 rejectReason 确实以<b>字符串</b>形态出网（全局枚举字符串契约）。</summary>
    private static void AssertRejectReasonIsEnumString(JsonElement data, string raw, string expectedName)
    {
        TryGetPropertyIgnoreCase(data, "rejectReason", out var element).Should().BeTrue();
        element.ValueKind.Should().Be(
            JsonValueKind.String,
            "全局 JsonStringEnumConverter 要求枚举以字符串出网；出现数字说明转换器被撤或被局部覆盖。" +
            $"实际响应：{Truncate(raw)}");
        element.GetString().Should().Be(expectedName);
    }

    /// <summary>
    /// 字节级对照两条出网报文（忽略 frameId 的随机性但校验其形态；校验 timestamp 为合理秒级整数）。
    /// 用于钉死铁律③——出网报文与协议构建器产出一致。
    /// </summary>
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
    /// 递归结构相等比较（值语义，忽略成员书写顺序）。
    /// 本环境的 <see cref="JsonElement"/> 未暴露 <c>DeepEquals</c>（CS1061），故自实现一份。
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
                return string.Equals(a.GetString(), b.GetString(), StringComparison.Ordinal);

            case JsonValueKind.Number:
                return a.GetRawText() == b.GetRawText();

            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return true;

            default:
                return false;
        }
    }
}
