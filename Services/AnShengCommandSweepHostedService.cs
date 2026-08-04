// T7-4：超时旁路清扫宿主（决策 D8 采纳方案 D）。
//
// 【为什么必须存在这个组件】
//   应答路径（AnShengMessageRouter）只能处理「设备回话了」的情况。设备掉电、固件卡死、
//   MQTT 丢包时，命令记录会永远停在 Sent —— 而 T7 §7 的硬约定是「不存在永远 Pending」：
//   任何一条命令都必须在有限时间内落到四种终态之一。本宿主就是那条兜底路径。
//
// 【为什么不并入既有后台组件】（D8 否决记录）
//   · 并入 AnShengOfflineDebouncer：离线防抖是分钟级周期，命令超时是秒级，合并必然牺牲一方；
//   · 搭车 AnShengUplinkPipeline：上行管道是数据面，无上行消息时不转动 —— 设备全离线场景下
//     超时永远不触发，恰好是最需要兜底的场景失效；
//   · 写进 AnShengPendingCommandStore：Store 是 Singleton 且绝不应依赖 AppDbContext（D6 职责边界）。

using System;
using System.Threading;
using System.Threading.Tasks;
using IoTPlatform.Configuration;
using IoTPlatform.Data;
using IoTPlatform.Models;
using IoTPlatform.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IoTPlatform.Services;

/// <summary>
/// 安圣命令超时旁路清扫宿主（T7 决策 D8）。
///
/// 【一句话职责】周期性把在途表里已过 TTL 的条目摘掉，并把对应的
/// <see cref="AnShengCommandRecord"/> 置为 <see cref="AnShengCommandStatus.Timeout"/>。
///
/// 【生命周期】Singleton（<c>AddHostedService</c>）。
///   因此<b>绝不能</b>在构造函数里注入 <see cref="AppDbContext"/> 之类的 Scoped 服务——
///   那会把一个请求级 DbContext 的寿命拉长到整个进程，制造连接泄漏与跨请求脏跟踪。
///   正确姿势是每轮清扫用 <see cref="IServiceScopeFactory.CreateScope"/> 现开现关。
///
/// 【终态互斥 —— 本组件正确性的基石】
///   谁写终态由在途表的 <c>ConcurrentDictionary.TryRemove</c>（CAS）裁定：
///   本宿主只处理 <c>SweepExpiredDetailedAsync</c> <b>实际摘到手</b>的条目。
///   若同一瞬间设备应答到达且 Router 先摘走了条目，本宿主根本拿不到它，
///   自然不会把一条已经 Succeeded 的记录改写成 Timeout。
///   落库前额外再加一道 <c>Status IN (Pending, Sent)</c> 的幂等判定作为纵深防御。
///
/// 【AppCode 陷阱（T6 §7.2 已踩过一次）】
///   后台线程没有 HTTP 上下文 ⇒ <c>ITenantContextAccessor.Current</c> 为 null ⇒
///   <c>AppDbContext</c> 的全局租户过滤器会把<b>所有行都过滤掉</b>，查询恒为空。
///   故本宿主的查询一律 <c>IgnoreQueryFilters()</c> 且<b>按主键定位</b>——
///   主键定位天然跨租户安全，不需要也不应该依赖过滤器。
///
/// 【异常策略】单轮失败绝不允许冒泡。
///   <see cref="BackgroundService.ExecuteAsync"/> 里抛出的异常会让整个宿主<b>静默停机</b>
///   （.NET 6 起默认 <c>BackgroundServiceExceptionBehavior.StopHost</c> 才会拉停进程，
///   而托管方若配成 Ignore 就只是这个宿主悄悄死掉）——两种结局都意味着从此再无超时兜底，
///   且没有任何显式报错。所以整轮 try/catch + Error 日志 + 继续下一轮。
/// </summary>
public sealed class AnShengCommandSweepHostedService : BackgroundService
{
    /// <summary>超时记录统一写入的机器可读错误码（集成测试按它断言）。</summary>
    public const string TimeoutErrorCode = "TIMEOUT";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAnShengPendingCommandStore _store;
    private readonly AnShengCommandOptions _options;
    private readonly ILogger<AnShengCommandSweepHostedService> _logger;

    /// <summary>
    /// 构造清扫宿主。
    /// </summary>
    /// <param name="scopeFactory">作用域工厂；每轮清扫用它取 Scoped 的 <see cref="AppDbContext"/>。</param>
    /// <param name="store">在途命令表（Singleton，同为单例，可直接注入）。</param>
    /// <param name="options">命令服务配置（清扫周期、开关）。</param>
    /// <param name="logger">日志器。</param>
    public AnShengCommandSweepHostedService(
        IServiceScopeFactory scopeFactory,
        IAnShengPendingCommandStore store,
        IOptions<AnShengCommandOptions> options,
        ILogger<AnShengCommandSweepHostedService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 后台循环：每 <c>SweepIntervalSeconds</c> 秒清扫一轮。
    ///
    /// <c>SweepEnabled=false</c> 时直接返回（集成测试的默认姿势：关掉后台线程，
    /// 由用例调 <see cref="SweepOnceAsync"/> 手工触发一轮，杜绝后台线程与断言竞态）。
    /// </summary>
    /// <param name="stoppingToken">宿主停止令牌。</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.SweepEnabled)
        {
            _logger.LogInformation(
                "[AnShengSweep] 后台清扫已禁用（AnSheng:Command:SweepEnabled=false），" +
                "命令超时将不会被自动置终态，需由调用方手工触发 SweepOnceAsync。");
            return;
        }

        var interval = TimeSpan.FromSeconds(_options.EffectiveSweepIntervalSeconds);
        _logger.LogInformation("[AnShengSweep] 后台清扫已启动，周期 {Interval}。", interval);

        // PeriodicTimer 而非 Task.Delay 循环：它不会因单轮耗时而累积漂移，
        // 且 WaitForNextTickAsync 支持取消令牌，停机时能立刻退出而不用等满一个周期。
        using var timer = new PeriodicTimer(interval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    await SweepOnceAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // 停机途中被打断属正常收尾，不记 Error。
                    break;
                }
                catch (Exception ex)
                {
                    // ★ 这里的 catch 是本组件的生命线：一旦让异常冒出 ExecuteAsync，
                    //   宿主就此停摆，此后所有命令都永远停在 Sent 且无任何报错。
                    _logger.LogError(ex, "[AnShengSweep] 单轮清扫失败，已跳过本轮，下一轮继续。");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 宿主正常停止。
        }

        _logger.LogInformation("[AnShengSweep] 后台清扫已停止。");
    }

    /// <summary>
    /// 执行<b>一轮</b>清扫：摘除全部过期在途条目，并把对应记录置为 <c>Timeout</c>。
    ///
    /// 【为什么是 public】它不是「为测试开的后门」，而是本组件的语义单元 ——
    /// <see cref="ExecuteAsync"/> 只是「按周期反复调用它」这一层壳。
    /// 集成测试在 <c>SweepEnabled=false</c> 下手工调它，验证的正是生产同一段代码路径
    /// （验收 #5 要求断言 <c>Record.Status==Timeout</c>，只调 Store 的清扫拿不到落库效果）。
    ///
    /// 【摘除与落库分离】条目一旦被 Store 摘除，内存就已释放，本方法即便落库失败也<b>不回滚</b>摘除。
    /// 内存正确性优先于记录完整性 —— 否则验收 #5 的「1000 条后 Count==0、内存无增长」不成立。
    /// 落库失败只记 Error 日志（风险 R6：极端情况下会留下「僵尸 Sent 记录」，已登记运维巡检）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>本轮被摘除的在途条目数（不等于成功回填的记录数）。</returns>
    public async Task<int> SweepOnceAsync(CancellationToken cancellationToken = default)
    {
        var expired = await _store.SweepExpiredDetailedAsync(cancellationToken).ConfigureAwait(false);
        if (expired == null || expired.Count == 0)
        {
            // 空转零开销、零日志 —— 5 秒一轮的组件不能在无事发生时刷屏。
            return 0;
        }

        _logger.LogWarning(
            "[AnShengSweep] 本轮清出 {Count} 条超时命令，开始回填记录。", expired.Count);

        // 一轮共用一个 scope + 一次 SaveChanges：N 条过期通常同时发生（设备批量掉线），
        // 逐条开 scope 会把连接池打满。
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTime.UtcNow;
        var affected = 0;

        try
        {
            foreach (var item in expired)
            {
                if (item.RecordId <= 0)
                {
                    // T6 遗留调用或纯内存单测登记的条目没有库记录，跳过是正常路径而非异常。
                    _logger.LogDebug(
                        "[AnShengSweep] 条目 {Imei}:{FrameId} 无 RecordId，跳过回填（未落库的在途登记）。",
                        item.Imei, item.FrameId);
                    continue;
                }

                // ★ IgnoreQueryFilters + 按主键：后台线程无 AppCode 上下文，
                //   走全局租户过滤器会一行都查不到（T6 §7.2 踩过的坑）。
                var record = await db.AnShengCommandRecords
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(r => r.Id == item.RecordId, cancellationToken)
                    .ConfigureAwait(false);

                if (record == null)
                {
                    _logger.LogWarning(
                        "[AnShengSweep] 找不到命令记录 Id={RecordId}（{Imei}:{FrameId}），跳过回填。",
                        item.RecordId, item.Imei, item.FrameId);
                    continue;
                }

                // 纵深防御：正常情况下能被本宿主摘到的条目一定还没有终态，
                // 但记录可能被别的路径（人工干预 / 未来的重试逻辑）提前置终态过。
                // 幂等判定保证「重复清扫不会把 Succeeded 改回 Timeout」。
                if (record.Status != AnShengCommandStatus.Pending &&
                    record.Status != AnShengCommandStatus.Sent)
                {
                    _logger.LogDebug(
                        "[AnShengSweep] 记录 Id={RecordId} 已是终态 {Status}，跳过（终态互斥）。",
                        record.Id, record.Status);
                    continue;
                }

                record.Status = AnShengCommandStatus.Timeout;
                record.CompletedAt = now;
                record.ErrorCode = TimeoutErrorCode;
                record.ErrorMessage = AnShengCommandRecord.TruncateErrorMessage(
                    $"设备在 {item.Ttl.TotalSeconds:0.###} 秒内未应答（frameId={item.FrameId}）");
                record.DurationMs = ComputeDurationMs(record.SentAt, now);

                affected++;
            }

            if (affected > 0)
            {
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            _logger.LogInformation(
                "[AnShengSweep] 回填完成：清出 {Expired} 条，置 Timeout {Affected} 条。",
                expired.Count, affected);
        }
        catch (Exception ex)
        {
            // 落库失败不重新入队 —— 条目已从内存摘除，重试也找不回来。
            // 记录会停在 Sent（风险 R6），由运维巡检「Status=Sent 且 TimeoutAt < now」兜底。
            _logger.LogError(ex,
                "[AnShengSweep] 回填超时记录失败（已清出 {Count} 条，内存已释放，记录可能停留在 Sent）。",
                expired.Count);
        }

        return expired.Count;
    }

    /// <summary>
    /// 计算往返耗时（毫秒）。
    ///
    /// 未发布（<paramref name="sentAt"/> 为 null）时返回 null —— 没发出去谈不上往返；
    /// 时钟回拨导致的负值统一钳到 0，避免出现「耗时 -3 毫秒」这种让人怀疑数据可信度的值。
    /// </summary>
    /// <param name="sentAt">发布时刻（UTC），可为 null。</param>
    /// <param name="completedAt">终态时刻（UTC）。</param>
    /// <returns>毫秒数；无法计算时为 null。</returns>
    private static int? ComputeDurationMs(DateTime? sentAt, DateTime completedAt)
    {
        if (sentAt == null)
        {
            return null;
        }

        var ms = (completedAt - sentAt.Value).TotalMilliseconds;
        if (ms < 0)
        {
            return 0;
        }

        return ms > int.MaxValue ? int.MaxValue : (int)ms;
    }
}
