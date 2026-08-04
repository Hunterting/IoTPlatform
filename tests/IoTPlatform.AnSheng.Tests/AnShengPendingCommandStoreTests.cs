using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IoTPlatform.Infrastructure.Protocol.AnSheng;
using IoTPlatform.Services;
using IoTPlatform.Services.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IoTPlatform.AnSheng.Tests;

/// <summary>
/// T7-2 在途命令表增强 —— 单元测试。
///
/// 【覆盖的验收项】
///   · 验收 #4：两台设备使用<b>相同 frameId</b> 各自在途，互不串扰（登记/摘除/等待者三条路径都不串）；
///   · 验收 #5 前半：过期条目被 <c>SweepExpiredDetailedAsync</c> 清出、带回 <c>RecordId</c>、
///     且其等待者被<b>取消</b>而不是永久挂起；内存不增长（1000 次循环后 <c>Count==0</c>）。
///
/// 【为什么这些用例必须存在】
///   在途表是「这条上行是不是我那条命令的应答」的唯一判据。它错一次的后果不是抛异常，
///   而是命令记录悄悄停在 Pending 直到超时、或者 A 设备的应答把 B 设备的命令标记成功。
///   这类缺陷在生产上只表现为「偶发的命令超时」，靠人工排查几乎不可能定位，只能靠断言守住。
///
/// 【时间纪律】全部用 <c>Task.Delay</c> / 事件量 / <c>Stopwatch</c>，<b>不使用</b> <c>Thread.Sleep</c>；
///   过期场景一律用「负 TTL / 极短 TTL + 一次让步」构造，不靠睡眠碰运气。
/// </summary>
public sealed class AnShengPendingCommandStoreTests : IDisposable
{
    private const string Imei = "864536072949900";
    private const string OtherImei = "864536072949901";
    private const string SharedFrameId = "a1b2c3d4e5f60718";

    private readonly AnShengPendingCommandStore _store =
        new(NullLogger<AnShengPendingCommandStore>.Instance);

    /// <summary>用例间必须清空，且清空要取消等待者，否则会污染下一个用例。</summary>
    public void Dispose() => _store.ClearAll();

    // ─────────────────────────────────────────────────────────────
    // 验收 #4：跨设备同 frameId 不串扰
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 两台设备用<b>同一个</b> frameId 各登记一条 ⇒ 两条并存；
    /// 完成其中一条后，另一条仍在途、其等待者仍未完成。
    /// </summary>
    [Fact]
    public async Task Register_SameFrameIdOnTwoDevices_DoesNotCrossTalk()
    {
        var first = await _store.RegisterAsync(Imei, SharedFrameId,
            PendingCommand.Create(1, Imei, SharedFrameId, "getDevStatus", TimeSpan.FromSeconds(30), recordId: 11));
        var second = await _store.RegisterAsync(OtherImei, SharedFrameId,
            PendingCommand.Create(2, OtherImei, SharedFrameId, "getDevStatus", TimeSpan.FromSeconds(30), recordId: 22));

        Assert.True(first.Registered);
        Assert.True(second.Registered);
        Assert.Equal(2, _store.Count);

        var completed = await _store.CompleteAsync(Imei, SharedFrameId, OkResponse(Imei, SharedFrameId));

        // 被摘的是第一台设备的条目
        Assert.NotNull(completed);
        Assert.Equal(Imei, completed!.Imei);
        Assert.Equal(11, completed.RecordId);

        // 第二台设备完全不受影响
        Assert.Equal(1, _store.Count);
        Assert.True(_store.IsInFlight(OtherImei, SharedFrameId));
        Assert.False(_store.IsInFlight(Imei, SharedFrameId));

        // 等待者也不能串：第一条已兑现，第二条仍在等
        Assert.True(first.Completion.IsCompletedSuccessfully);
        Assert.False(second.Completion.IsCompleted);
    }

    /// <summary>摘除只认 (imei, frameId) 组合，用别的 IMEI 摘不动本设备的条目。</summary>
    [Fact]
    public async Task Complete_WithWrongImei_DoesNotRemoveOtherDeviceEntry()
    {
        _store.TryRegister(Imei, SharedFrameId,
            PendingCommand.Create(1, Imei, SharedFrameId, "action", TimeSpan.FromSeconds(30)));

        var completed = await _store.CompleteAsync(OtherImei, SharedFrameId, null);

        Assert.Null(completed);
        Assert.Equal(1, _store.Count);
        Assert.True(_store.IsInFlight(Imei, SharedFrameId));
    }

    // ─────────────────────────────────────────────────────────────
    // 内存不增长
    // ─────────────────────────────────────────────────────────────

    /// <summary>1000 次「登记 → 完成」后条目数必须归零，否则在途表就是内存泄漏点。</summary>
    [Fact]
    public async Task RegisterThenComplete_ThousandTimes_LeavesNoResidue()
    {
        for (var i = 0; i < 1000; i++)
        {
            var frameId = $"frame-{i:D6}";
            Assert.True(_store.TryRegister(Imei, frameId,
                PendingCommand.Create(i, Imei, frameId, "getDevStatus", TimeSpan.FromSeconds(30))));

            var completed = await _store.CompleteAsync(Imei, frameId, null);
            Assert.NotNull(completed);
        }

        Assert.Equal(0, _store.Count);
    }

    /// <summary>1000 次「登记 → 过期 → 清扫」后条目数必须归零。</summary>
    [Fact]
    public async Task RegisterThenSweep_ThousandTimes_LeavesNoResidue()
    {
        for (var i = 0; i < 1000; i++)
        {
            var frameId = $"stale-{i:D6}";

            // 负 TTL：条目一诞生就是过期的，无需任何等待，判定完全确定
            Assert.True(_store.TryRegister(Imei, frameId,
                PendingCommand.Create(i, Imei, frameId, "getDevStatus", TimeSpan.FromMilliseconds(-1))));
        }

        Assert.Equal(1000, _store.Count);

        var swept = await _store.SweepExpiredDetailedAsync();

        Assert.Equal(1000, swept.Count);
        Assert.Equal(0, _store.Count);
    }

    // ─────────────────────────────────────────────────────────────
    // 验收 #5 前半：清扫返回条目 + RecordId + 等待者被取消
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 清扫必须把过期条目<b>带回来</b>（含 RecordId，供宿主回填 Status=Timeout），
    /// 并取消其等待者 —— 不取消的话，等待方会一直挂到进程结束。
    /// </summary>
    [Fact]
    public async Task SweepExpired_RemovesAndReturnsItemsWithRecordId()
    {
        var registration = await _store.RegisterAsync(Imei, SharedFrameId,
            PendingCommand.Create(7, Imei, SharedFrameId, "getLogs", TimeSpan.FromMilliseconds(1), recordId: 4242));

        // 让出一次调度即可越过 1ms TTL，比固定睡眠更稳也更快
        await Task.Delay(20);

        var swept = await _store.SweepExpiredDetailedAsync();

        Assert.Single(swept);
        Assert.Equal(4242, swept[0].RecordId);
        Assert.Equal("getLogs", swept[0].Method);
        Assert.Equal(TimeSpan.FromMilliseconds(1), swept[0].Ttl);
        Assert.Equal(0, _store.Count);
        Assert.True(registration.Completion.IsCanceled);
    }

    /// <summary>指定基准时刻的清扫重载：同一轮内所有条目共用一个 now，判定可复现。</summary>
    [Fact]
    public async Task SweepExpired_WithExplicitNow_OnlyRemovesEntriesExpiredBeforeIt()
    {
        _store.TryRegister(Imei, "soon",
            PendingCommand.Create(1, Imei, "soon", "action", TimeSpan.FromSeconds(10)));
        _store.TryRegister(Imei, "later",
            PendingCommand.Create(2, Imei, "later", "action", TimeSpan.FromSeconds(600)));

        // 基准时刻取「现在 + 1 分钟」：10s 的那条过期，600s 的那条不过期
        var swept = await _store.SweepExpiredAsync(DateTime.UtcNow.AddMinutes(1));

        Assert.Single(swept);
        Assert.Equal("soon", swept[0].FrameId);
        Assert.Equal(1, _store.Count);
    }

    /// <summary>T6 遗留的 int 版清扫必须与新重载行为一致（内部委托，不得出现第二份逻辑）。</summary>
    [Fact]
    public async Task SweepExpiredAsync_LegacyOverload_ReturnsSameCount()
    {
        _store.TryRegister(Imei, "expired-1",
            PendingCommand.Create(1, Imei, "expired-1", "action", TimeSpan.FromMilliseconds(-1)));
        _store.TryRegister(Imei, "expired-2",
            PendingCommand.Create(2, Imei, "expired-2", "action", TimeSpan.FromMilliseconds(-1)));
        _store.TryRegister(Imei, "alive",
            PendingCommand.Create(3, Imei, "alive", "action", TimeSpan.FromSeconds(30)));

        var removed = await _store.SweepExpiredAsync();

        Assert.Equal(2, removed);
        Assert.Equal(1, _store.Count);
    }

    // ─────────────────────────────────────────────────────────────
    // TaskCompletionSource 语义
    // ─────────────────────────────────────────────────────────────

    /// <summary>收到应答时，等待者拿到的必须正是那条应答报文。</summary>
    [Fact]
    public async Task Complete_FulfillsWaiterWithResponse()
    {
        var registration = await _store.RegisterAsync(Imei, SharedFrameId,
            PendingCommand.Create(1, Imei, SharedFrameId, "action", TimeSpan.FromSeconds(30)));

        var response = OkResponse(Imei, SharedFrameId);
        await _store.CompleteAsync(Imei, SharedFrameId, response);

        var awaited = await registration.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(awaited);
        Assert.Same(response, awaited);
    }

    /// <summary>
    /// TCS <b>必须</b>建在 <c>RunContinuationsAsynchronously</c> 之上。
    ///
    /// 验证方式：挂一个 <c>ExecuteSynchronously</c> 且会阻塞 5 秒的续体。
    ///   · 若 TCS 允许同步续体 ⇒ <c>CompleteAsync</c> 会被这 5 秒<b>卡住</b>
    ///     （生产上这条线程就是 MQTT 上行接收线程，整条上行管道随之停摆）；
    ///   · 若正确使用了 RunContinuationsAsynchronously ⇒ 续体被强制排到线程池，
    ///     <c>CompleteAsync</c> 立即返回。
    /// </summary>
    [Fact]
    public async Task Store_TcsUsesRunContinuationsAsynchronously()
    {
        var registration = await _store.RegisterAsync(Imei, SharedFrameId,
            PendingCommand.Create(1, Imei, SharedFrameId, "action", TimeSpan.FromSeconds(30)));

        using var gate = new ManualResetEventSlim(false);
        _ = registration.Completion.ContinueWith(
            _ => gate.Wait(TimeSpan.FromSeconds(5)),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        var stopwatch = Stopwatch.StartNew();
        var completed = await _store.CompleteAsync(Imei, SharedFrameId, null);
        stopwatch.Stop();

        gate.Set(); // 无论断言结果如何都要放行，避免拖住线程池

        Assert.NotNull(completed);
        Assert.True(stopwatch.ElapsedMilliseconds < 1000,
            $"CompleteAsync 被等待者的续体同步阻塞了 {stopwatch.ElapsedMilliseconds}ms —— "
            + "说明 TaskCompletionSource 没有使用 TaskCreationOptions.RunContinuationsAsynchronously。");
    }

    /// <summary>等待超时不抛异常，按「没等到」返回 null，终态交由清扫宿主写 Timeout。</summary>
    [Fact]
    public async Task WaitAsync_WhenNoResponse_ReturnsNullInsteadOfThrowing()
    {
        var registration = await _store.RegisterAsync(Imei, SharedFrameId,
            PendingCommand.Create(1, Imei, SharedFrameId, "action", TimeSpan.FromSeconds(30)));

        var awaited = await registration.WaitAsync(TimeSpan.FromMilliseconds(30));

        Assert.Null(awaited);
        Assert.True(_store.IsInFlight(Imei, SharedFrameId)); // 等待超时不等于摘条目
    }

    /// <summary>登记失败时返回的凭据必须是「已取消」，绝不能让调用方 await 到天荒地老。</summary>
    [Fact]
    public async Task RegisterAsync_OnConflict_ReturnsRejectedRegistration()
    {
        var first = await _store.RegisterAsync(Imei, SharedFrameId,
            PendingCommand.Create(1, Imei, SharedFrameId, "action", TimeSpan.FromSeconds(30)));
        var second = await _store.RegisterAsync(Imei, SharedFrameId,
            PendingCommand.Create(2, Imei, SharedFrameId, "action", TimeSpan.FromSeconds(30)));

        Assert.True(first.Registered);
        Assert.False(second.Registered);
        Assert.True(second.Completion.IsCanceled);
        Assert.Equal(1, _store.Count);

        // 已取消的凭据 WaitAsync 也必须立刻返回 null，而不是抛 TaskCanceledException
        Assert.Null(await second.WaitAsync(TimeSpan.FromSeconds(1)));
    }

    /// <summary>覆盖已过期条目时，被顶掉的旧条目的等待者必须被取消。</summary>
    [Fact]
    public async Task RegisterAsync_OverExpiredEntry_CancelsPreviousWaiter()
    {
        var stale = await _store.RegisterAsync(Imei, SharedFrameId,
            PendingCommand.Create(1, Imei, SharedFrameId, "action", TimeSpan.FromMilliseconds(-1)));

        var fresh = await _store.RegisterAsync(Imei, SharedFrameId,
            PendingCommand.Create(2, Imei, SharedFrameId, "action", TimeSpan.FromSeconds(30)));

        Assert.True(fresh.Registered);
        Assert.True(stale.Completion.IsCanceled);
        Assert.False(fresh.Completion.IsCompleted);
        Assert.Equal(1, _store.Count);
    }

    /// <summary>惰性过期摘除时同样要取消等待者。</summary>
    [Fact]
    public async Task IsInFlight_OnExpiredEntry_CancelsWaiterAndRemoves()
    {
        var registration = await _store.RegisterAsync(Imei, SharedFrameId,
            PendingCommand.Create(1, Imei, SharedFrameId, "action", TimeSpan.FromMilliseconds(-1)));

        Assert.False(_store.IsInFlight(Imei, SharedFrameId));
        Assert.Equal(0, _store.Count);
        Assert.True(registration.Completion.IsCanceled);
    }

    /// <summary><c>ClearAll</c> 必须取消全部等待者，否则用例间会留下永不完成的 await。</summary>
    [Fact]
    public async Task ClearAll_CancelsAllWaiters()
    {
        var a = await _store.RegisterAsync(Imei, "frame-a",
            PendingCommand.Create(1, Imei, "frame-a", "action", TimeSpan.FromSeconds(30)));
        var b = await _store.RegisterAsync(OtherImei, "frame-b",
            PendingCommand.Create(2, OtherImei, "frame-b", "action", TimeSpan.FromSeconds(30)));

        _store.ClearAll();

        Assert.Equal(0, _store.Count);
        Assert.True(a.Completion.IsCanceled);
        Assert.True(b.Completion.IsCanceled);
    }

    // ─────────────────────────────────────────────────────────────
    // PendingCommand 契约
    // ─────────────────────────────────────────────────────────────

    /// <summary><c>Create</c> 必须同时填好 Ttl 与 RecordId —— 清扫宿主靠它们回填记录。</summary>
    [Fact]
    public void Create_PopulatesTtlAndRecordId()
    {
        var ttl = TimeSpan.FromSeconds(60);
        var cmd = PendingCommand.Create(9, Imei, SharedFrameId, "getLogs", ttl, recordId: 100);

        Assert.Equal(ttl, cmd.Ttl);
        Assert.Equal(100, cmd.RecordId);
        Assert.Equal(9, cmd.CommandId);
        Assert.Equal(ttl, cmd.ExpiresAt - cmd.SentAt);
        Assert.False(cmd.IsExpired);
    }

    /// <summary>T6 遗留调用不传 recordId 时应安全退化为 0，清扫宿主据此跳过回填。</summary>
    [Fact]
    public void Create_WithoutRecordId_DefaultsToZero()
    {
        var cmd = PendingCommand.Create(1, Imei, SharedFrameId, "action", TimeSpan.FromSeconds(30));

        Assert.Equal(0, cmd.RecordId);
    }

    /// <summary>过期判定必须以调用方给的基准时刻为准，而不是每次都读一遍时钟。</summary>
    [Fact]
    public void IsExpiredAt_UsesSuppliedInstant()
    {
        var cmd = PendingCommand.Create(1, Imei, SharedFrameId, "action", TimeSpan.FromSeconds(30));

        Assert.False(cmd.IsExpiredAt(cmd.SentAt.AddSeconds(29)));
        Assert.True(cmd.IsExpiredAt(cmd.SentAt.AddSeconds(31)));
    }

    /// <summary>构造一条 result=ok 的应答报文。</summary>
    /// <param name="imei">设备 IMEI。</param>
    /// <param name="frameId">帧 ID。</param>
    /// <returns>应答报文。</returns>
    private static AnShengMessage OkResponse(string imei, string frameId) => new()
    {
        Method = "action",
        Imei = imei,
        FrameId = frameId,
        Result = "ok",
        RawJson = $"{{\"method\":\"action\",\"imei\":\"{imei}\",\"frameId\":\"{frameId}\",\"result\":\"ok\"}}"
    };
}
