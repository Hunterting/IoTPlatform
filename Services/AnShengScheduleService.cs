// T8-3：安圣延时任务调度服务实现。
//
// 【这个类只做两件事】
//   ① 编排「下发 + 写后回读」—— 下发一律走 T7 的 AnShengCommandService.SendCommandAsync，
//      本类<b>不碰 MQTT、不碰 Guard、不碰在途表</b>（铁律③：不自造下发通道）；
//   ② 维护平台镜像 —— AnShengDelayTask（每设备每插槽一行）+ AnShengDeviceProfile.SlotsSnapshot。
//
// 【设备权威（D6 Option A）】平台镜像是快照不是账本：
//   start/stop 成功后先写一份「乐观镜像」让前端立刻有东西看，随后在新作用域触发 getDelayTasks，
//   其应答经 AnShengMessageRouter 钩子调 ApplyDelayTasksReadbackAsync 覆盖镜像并 bump SyncedAt。
//   也就是说——**平台写的值随时可能被设备的回读推翻，这是设计意图而非缺陷**。
//
// 【租户过滤器陷阱（设计 §7.1，本文件最易踩）】
//   ApplyDelayTasksReadbackAsync / ApplyDelayEventAsync / UpdateSlotsSnapshotAsync 三个方法
//   会被 Router / Handler 在**后台作用域**调用，此时 ITenantContextAccessor.Current 为 null，
//   AppDbContext 的全局查询过滤器会把所有行滤成空集 —— 表现为「事件收到了、镜像却纹丝不动」。
//   因此这三条路径上的每一次查询都必须 IgnoreQueryFilters() + 按 DeviceId（全局唯一）显式定位，
//   新建行的 AppCode 显式取自 Devices 表（同设备必同租户）。
//   反之 GetDelayTasksAsync 只在 HTTP 作用域被调用，**刻意不加** IgnoreQueryFilters ——
//   那是跨租户读的唯一防线，去掉就等于把别家租户的延时任务暴露出去。

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IoTPlatform.Configuration;
using IoTPlatform.Data;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Models;
using IoTPlatform.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IoTPlatform.Services;

/// <summary>
/// 安圣延时任务调度服务实现（T8，仅延时部分；定时部分见文末 T10 预留说明）。
///
/// 【生命周期】Scoped —— 持有 <see cref="AppDbContext"/>。
///   写后回读要脱离当前请求作用域，故额外注入 <see cref="IServiceScopeFactory"/>（Singleton，可被 Scoped 依赖）。
/// </summary>
public class AnShengScheduleService : IAnShengScheduleService
{
    /// <summary>安圣协议方法名：开始/配置延时任务。</summary>
    private const string MethodStartDelayTask = "startDelayTask";

    /// <summary>安圣协议方法名：停止延时任务。</summary>
    private const string MethodStopDelayTask = "stopDelayTask";

    /// <summary>安圣协议方法名：查询延时任务列表（写后回读用）。</summary>
    private const string MethodGetDelayTasks = "getDelayTasks";

    /// <summary><see cref="AnShengDelayTask.SAction"/> / <see cref="AnShengDelayTask.EAction"/> 列宽（varchar(16)）。</summary>
    private const int ActionColumnLength = 16;

    /// <summary>开始动作缺省值，与 <see cref="AnShengDelayTask.SAction"/> 的模型默认一致。</summary>
    private const string DefaultSAction = "none";

    /// <summary>结束动作缺省值，与 <see cref="AnShengDelayTask.EAction"/> 的模型默认一致。</summary>
    private const string DefaultEAction = "off";

    private readonly AppDbContext _db;
    private readonly IAnShengCommandService _cmd;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AnShengCommandOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<AnShengScheduleService> _logger;

    /// <summary>
    /// 构造调度服务。
    /// </summary>
    /// <param name="db">数据库上下文（Scoped）。</param>
    /// <param name="cmd">命令下发服务（T7 单点入口，本服务唯一的出网通道）。</param>
    /// <param name="scopeFactory">作用域工厂，供写后回读脱离当前作用域使用。</param>
    /// <param name="options">命令服务参数（回读延迟 / 陈旧阈值）。</param>
    /// <param name="logger">日志。</param>
    /// <param name="timeProvider">
    /// 时间源，可选。DI 容器未注册 <see cref="TimeProvider"/> 时回落到
    /// <see cref="TimeProvider.System"/>；单元测试可注册假时钟以断言 <c>IsStale</c> 边界，
    /// 无需真的等 24 小时。
    /// </param>
    public AnShengScheduleService(
        AppDbContext db,
        IAnShengCommandService cmd,
        IServiceScopeFactory scopeFactory,
        IOptions<AnShengCommandOptions> options,
        ILogger<AnShengScheduleService> logger,
        TimeProvider? timeProvider = null)
    {
        _db = db;
        _cmd = cmd;
        _scopeFactory = scopeFactory;
        _options = options?.Value ?? new AnShengCommandOptions();
        _logger = logger;
        _time = timeProvider ?? TimeProvider.System;
    }

    // ─────────────────────────────────────────────────────────────
    // 下发编排（HTTP 作用域）
    // ─────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<AnShengDelayTaskResultDto> StartDelayTaskAsync(
        long deviceId, int slotNum, bool enable,
        string sAction, string eAction, int secs, CancellationToken ct = default)
    {
        // 动作串只做「空 → 缺省」与大小写归一，**不做白名单纠正**：
        // 非法值必须原样送到 Guard 手里被拒，否则调用方永远不知道自己传错了。
        var normalizedSAction = NormalizeAction(sAction, DefaultSAction);
        var normalizedEAction = NormalizeAction(eAction, DefaultEAction);

        var parameters = new Dictionary<string, object?>
        {
            ["slotNum"] = slotNum,
            ["enable"] = enable,
            ["sAction"] = normalizedSAction,
            ["eAction"] = normalizedEAction,
            ["secs"] = secs
        };

        var response = await _cmd.SendCommandAsync(deviceId, MethodStartDelayTask, parameters, ct);

        if (!response.Success)
        {
            // 零发布 ⇒ 设备状态没变 ⇒ 镜像一个字都不能动（含喇叭类 RejectedByKind）。
            return BuildRejected(response);
        }

        // 乐观镜像：命令已出网，但设备应答还没回来。先按「请求意图」落一份，
        // 让前端立刻有东西渲染；真值由下面的写后回读覆盖。
        await UpsertMirrorAsync(
            deviceId, slotNum,
            row =>
            {
                row.Enable = enable;
                row.SAction = ClampAction(normalizedSAction, DefaultSAction);
                row.EAction = ClampAction(normalizedEAction, DefaultEAction);
                row.Secs = secs;
            },
            ct);

        var tasks = await GetDelayTasksAsync(deviceId, ct);

        ScheduleReadback(deviceId);

        return BuildAccepted(response, tasks);
    }

    /// <inheritdoc />
    public async Task<AnShengDelayTaskResultDto> StopDelayTaskAsync(
        long deviceId, int slotNum, CancellationToken ct = default)
    {
        var parameters = new Dictionary<string, object?>
        {
            ["slotNum"] = slotNum
        };

        var response = await _cmd.SendCommandAsync(deviceId, MethodStopDelayTask, parameters, ct);

        if (!response.Success)
        {
            return BuildRejected(response);
        }

        // stop 只改 Enable：sAction / eAction / secs 是设备侧保留的配置，
        // 平台无权臆断它们被清空——那要等回读说了算。
        await UpsertMirrorAsync(deviceId, slotNum, row => row.Enable = false, ct);

        var tasks = await GetDelayTasksAsync(deviceId, ct);

        ScheduleReadback(deviceId);

        return BuildAccepted(response, tasks);
    }

    /// <inheritdoc />
    public async Task<List<AnShengDelayTaskDto>> GetDelayTasksAsync(
        long deviceId, CancellationToken ct = default)
    {
        // 刻意<b>不</b> IgnoreQueryFilters：本方法只服务 HTTP 作用域，
        // 全局租户过滤器是这里唯一的跨租户读防线（设计 §7.1 末段）。
        var rows = await _db.Set<AnShengDelayTask>()
            .AsNoTracking()
            .Where(t => t.DeviceId == deviceId)
            .OrderBy(t => t.SlotNum)
            .ToListAsync(ct);

        var now = UtcNow();
        var threshold = _options.EffectiveMirrorStaleThreshold;

        return rows
            .Select(t => new AnShengDelayTaskDto
            {
                SlotNum = t.SlotNum,
                Enable = t.Enable,
                SAction = t.SAction,
                EAction = t.EAction,
                Secs = t.Secs,
                Cnt = t.Cnt,
                SyncedAt = t.SyncedAt,
                IsStale = (now - t.SyncedAt) > threshold
            })
            .ToList();
    }

    // ─────────────────────────────────────────────────────────────
    // 镜像写回（可能在后台作用域被调用 —— 一律 IgnoreQueryFilters + 显式 AppCode）
    // ─────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task ApplyDelayTasksReadbackAsync(
        long deviceId, IReadOnlyList<AnShengDelayTaskItem> tasks, CancellationToken ct = default)
    {
        if (tasks == null || tasks.Count == 0)
        {
            // 空 tasks[] 不等于「全部清空」：设备也可能只是这一帧没带数组。
            // 按「没探到」处理，保留既有镜像，避免把有效数据抹成 0 行。
            _logger.LogDebug(
                "getDelayTasks 应答不含 tasks[]，跳过镜像回写: DeviceId={DeviceId}", deviceId);
            return;
        }

        var appCode = await ResolveAppCodeAsync(deviceId, ct);
        if (appCode == null)
        {
            _logger.LogWarning(
                "延时任务回读失败：设备不存在或无租户码，已跳过。DeviceId={DeviceId}, TaskCount={Count}",
                deviceId, tasks.Count);
            return;
        }

        var existing = await _db.Set<AnShengDelayTask>()
            .IgnoreQueryFilters()
            .Where(t => t.DeviceId == deviceId)
            .ToListAsync(ct);

        // 不用 ToDictionary：UQ(DeviceId, SlotNum) 理论上保证唯一，
        // 但历史脏数据一旦重键就会让整条上行链路抛异常，代价远大于「后写覆盖先写」。
        var bySlot = new Dictionary<int, AnShengDelayTask>(existing.Count);
        foreach (var row in existing)
        {
            bySlot[row.SlotNum] = row;
        }

        var now = UtcNow();

        for (var i = 0; i < tasks.Count; i++)
        {
            var item = tasks[i];
            if (item == null)
            {
                continue;
            }

            // §7.7：tasks[] 不含 slotNum，下标 i 对应插槽 i+1。
            var slotNum = i + 1;

            if (!bySlot.TryGetValue(slotNum, out var row))
            {
                row = new AnShengDelayTask
                {
                    AppCode = appCode,
                    DeviceId = deviceId,
                    SlotNum = slotNum
                };
                _db.Set<AnShengDelayTask>().Add(row);
                bySlot[slotNum] = row;
            }

            // AppCode 每次都以 Devices 表为准重写：设备换租户（重认领）后镜像不会留在旧租户下。
            row.AppCode = appCode;
            row.Enable = item.Enable;
            row.SAction = ClampAction(item.SAction, DefaultSAction);
            row.EAction = ClampAction(item.EAction, DefaultEAction);
            row.Secs = item.Secs;
            row.Cnt = item.Cnt;
            row.SyncedAt = now;
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogDebug(
            "延时任务镜像已回读覆盖: DeviceId={DeviceId}, AppCode={AppCode}, Slots={Count}, SyncedAt={SyncedAt:O}",
            deviceId, appCode, tasks.Count, now);
    }

    /// <inheritdoc />
    public async Task ApplyDelayEventAsync(
        long deviceId, int slotNum, IReadOnlyList<int>? slots, CancellationToken ct = default)
    {
        // delayEvent 的语义是「这一路的延时任务刚刚执行完并自行结束了」，
        // 因此镜像上该插槽必须落到 Enable=false（验收 #4 的断言点）。
        if (slotNum > 0)
        {
            await UpsertMirrorAsync(deviceId, slotNum, row => row.Enable = false, ct);
        }
        else
        {
            _logger.LogWarning(
                "delayEvent 未携带有效 slot_num，跳过延时任务镜像更新: DeviceId={DeviceId}, SlotNum={SlotNum}",
                deviceId, slotNum);
        }

        // 事件同帧常带 slots[]（全部插槽的通断快照）——顺手把 Profile 快照刷新掉，
        // 省去一次 getDevStatus 往返（决策 D-H）。
        if (slots != null && slots.Count > 0)
        {
            await UpdateSlotsSnapshotAsync(deviceId, slots, ct);
        }
    }

    /// <inheritdoc />
    public async Task UpdateSlotsSnapshotAsync(
        long deviceId, IReadOnlyList<int> slots, CancellationToken ct = default)
    {
        if (slots == null || slots.Count == 0)
        {
            // 与 ApplyDelayTasksReadbackAsync 同理：空数组按「本帧没带」处理，不清空既有快照。
            return;
        }

        var profile = await _db.Set<AnShengDeviceProfile>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.DeviceId == deviceId, ct);

        if (profile == null)
        {
            // 存量设备（产品决策 Q5：不回填）没有档案。这是正常业务状态，不建档、不报错。
            // 建档的唯一入口是认领流程（T5），此处隐式建档会造出没有 IMEI 的残缺档案。
            _logger.LogDebug(
                "设备无安圣能力档案，跳过 slots 快照写回: DeviceId={DeviceId}", deviceId);
            return;
        }

        var now = UtcNow();
        profile.SlotsSnapshot = SerializeSlots(slots);
        profile.SlotsSnapshotAt = now;
        profile.UpdatedAt = now;

        await _db.SaveChangesAsync(ct);

        _logger.LogDebug(
            "slots 快照已写回档案: DeviceId={DeviceId}, AppCode={AppCode}, Slots={Slots}",
            deviceId, profile.AppCode, profile.SlotsSnapshot);
    }

    // ─────────────────────────────────────────────────────────────
    // 写后回读
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 在<b>新作用域</b>里延迟触发一次 <c>getDelayTasks</c>，把设备的真值拉回来覆盖乐观镜像。
    ///
    /// 【为什么必须新作用域】本方法返回后 HTTP 请求就结束了，原作用域的 <see cref="AppDbContext"/>
    ///   与 <see cref="IAnShengCommandService"/> 都会被 Dispose；继续用它们必然 <c>ObjectDisposedException</c>。
    ///
    /// 【为什么必须 fire-and-forget】回读是「最终一致」的补偿动作，不是本次请求的成败依据。
    ///   等它会把接口响应时间硬生生拉长 120ms+，而设备离线时更是白等一整个 TTL。
    ///
    /// 【为什么 CancellationToken.None】请求作用域的 ct 在响应写完那一刻就被取消，
    ///   传进去等于让回读必定夭折 —— 这正是「写后回读看似没生效」最常见的成因。
    /// </summary>
    /// <param name="deviceId">目标设备主键。</param>
    private void ScheduleReadback(long deviceId)
    {
        var delayMs = _options.EffectiveReadbackDelayMs;

        _ = Task.Run(async () =>
        {
            try
            {
                if (delayMs > 0)
                {
                    await Task.Delay(delayMs).ConfigureAwait(false);
                }

                using var scope = _scopeFactory.CreateScope();
                var cmd = scope.ServiceProvider.GetRequiredService<IAnShengCommandService>();

                var response = await cmd
                    .SendCommandAsync(deviceId, MethodGetDelayTasks, null, CancellationToken.None)
                    .ConfigureAwait(false);

                if (response.Success)
                {
                    _logger.LogDebug(
                        "写后回读已下发: DeviceId={DeviceId}, CommandId={CommandId}, FrameId={FrameId}",
                        deviceId, response.CommandId, response.FrameId);
                }
                else
                {
                    // 回读失败不影响已经成功的 start/stop —— 镜像会停留在乐观值并最终被标 IsStale。
                    _logger.LogDebug(
                        "写后回读未受理（不影响本次下发结果）: DeviceId={DeviceId}, Reason={Reason}, Error={Error}",
                        deviceId, response.RejectReason, response.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                // 后台任务里的异常若不接住，会变成 UnobservedTaskException，
                // 在某些宿主配置下足以拖垮进程。这里必须吞并记录。
                _logger.LogWarning(ex, "写后回读执行失败: DeviceId={DeviceId}", deviceId);
            }
        });
    }

    // ─────────────────────────────────────────────────────────────
    // 私有工具
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 按 <c>(DeviceId, SlotNum)</c> 定位镜像行并原地修改；不存在则新建。统一 bump <c>SyncedAt</c>。
    ///
    /// 全程 <c>IgnoreQueryFilters()</c>：本方法既被 HTTP 作用域（start/stop）调用，
    /// 也被后台作用域（<see cref="ApplyDelayEventAsync"/>）调用，后者没有租户上下文。
    /// 跨租户越权由上游把关 —— HTTP 路径上 <c>SendCommandAsync</c> 查 <c>Devices</c> 时
    /// 走的是带租户过滤器的查询，别家设备根本走不到这一步。
    /// </summary>
    /// <param name="deviceId">设备主键。</param>
    /// <param name="slotNum">插槽编号（从 1 起）。</param>
    /// <param name="mutate">对镜像行的修改动作。</param>
    /// <param name="ct">取消令牌。</param>
    private async Task UpsertMirrorAsync(
        long deviceId, int slotNum, Action<AnShengDelayTask> mutate, CancellationToken ct)
    {
        var row = await _db.Set<AnShengDelayTask>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.DeviceId == deviceId && t.SlotNum == slotNum, ct);

        if (row == null)
        {
            var appCode = await ResolveAppCodeAsync(deviceId, ct);
            if (appCode == null)
            {
                _logger.LogWarning(
                    "无法新建延时任务镜像：设备不存在或无租户码。DeviceId={DeviceId}, SlotNum={SlotNum}",
                    deviceId, slotNum);
                return;
            }

            row = new AnShengDelayTask
            {
                AppCode = appCode,
                DeviceId = deviceId,
                SlotNum = slotNum
            };
            _db.Set<AnShengDelayTask>().Add(row);
        }

        mutate(row);
        row.SyncedAt = UtcNow();

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// 取设备的租户码。<c>IgnoreQueryFilters()</c> 是刚需：后台作用域下带过滤器必然查不到设备，
    /// 表现为「事件到了却一行镜像都没写」。
    /// </summary>
    /// <param name="deviceId">设备主键。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>租户码；设备不存在或租户码为空时返回 <c>null</c>。</returns>
    private async Task<string?> ResolveAppCodeAsync(long deviceId, CancellationToken ct)
    {
        var appCode = await _db.Devices
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(d => d.Id == deviceId)
            .Select(d => d.AppCode)
            .FirstOrDefaultAsync(ct);

        return string.IsNullOrWhiteSpace(appCode) ? null : appCode;
    }

    /// <summary>当前 UTC 时刻（走 <see cref="TimeProvider"/> 以便测试注入假时钟）。</summary>
    /// <returns>UTC 时间。</returns>
    private DateTime UtcNow() => _time.GetUtcNow().UtcDateTime;

    /// <summary>
    /// 归一化动作串：空白回落缺省值，其余仅 <c>Trim</c> + 转小写。
    /// <b>不做白名单纠正</b> —— 非法值要原样交给 <c>AnShengCommandGuard</c> 拒掉，
    /// 悄悄改成合法值会让调用方永远发现不了自己传错。
    /// </summary>
    /// <param name="action">原始动作串。</param>
    /// <param name="fallback">空白时的缺省值。</param>
    /// <returns>归一化后的动作串。</returns>
    private static string NormalizeAction(string? action, string fallback)
        => string.IsNullOrWhiteSpace(action) ? fallback : action.Trim().ToLowerInvariant();

    /// <summary>
    /// 落库前的动作串收敛：在 <see cref="NormalizeAction"/> 基础上截断到列宽 16。
    /// 设备回读的值不受平台校验保护，超长会直接撞 <c>DbUpdateException</c> 把整条上行链路打断。
    /// </summary>
    /// <param name="action">原始动作串。</param>
    /// <param name="fallback">空白时的缺省值。</param>
    /// <returns>可安全落库的动作串。</returns>
    private static string ClampAction(string? action, string fallback)
    {
        var normalized = NormalizeAction(action, fallback);
        return normalized.Length <= ActionColumnLength
            ? normalized
            : normalized.Substring(0, ActionColumnLength);
    }

    /// <summary>
    /// 把 <c>slots[]</c> 序列化成落库用的 JSON 串（如 <c>[0,1,0,1]</c>）。
    /// </summary>
    /// <param name="slots">插槽通断数组（0=关 1=开）。</param>
    /// <returns>JSON 字符串。</returns>
    public static string SerializeSlots(IReadOnlyList<int> slots)
        => JsonSerializer.Serialize(slots ?? Array.Empty<int>());

    /// <summary>
    /// 反序列化 <see cref="AnShengDeviceProfile.SlotsSnapshot"/>。
    ///
    /// 【为什么吞异常】这份 JSON 是设备写进来的，不是平台生成的；一条脏数据不该让
    /// 「查设备详情」整个接口 500。解析不了就当成「还没有快照」。
    /// </summary>
    /// <param name="snapshot">档案里的 JSON 快照，可为 null。</param>
    /// <returns>插槽数组；无快照或格式非法时返回 <c>null</c>。</returns>
    public static int[]? ParseSlotsSnapshot(string? snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<int[]>(snapshot);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>构造「已受理」结果。</summary>
    /// <param name="response">命令下发响应。</param>
    /// <param name="tasks">当前镜像快照。</param>
    /// <returns>延时任务下发结果。</returns>
    private static AnShengDelayTaskResultDto BuildAccepted(
        AnShengCommandResponse response, List<AnShengDelayTaskDto> tasks)
        => new()
        {
            Accepted = true,
            CommandId = response.CommandId,
            FrameId = response.FrameId,
            RejectReason = null,
            ErrorMessage = null,
            Tasks = tasks
        };

    /// <summary>
    /// 构造「未受理」结果。
    ///
    /// <c>Tasks</c> 刻意为 <c>null</c> 而非空列表：命令没出网，镜像没有发生任何变化，
    /// 返回一份「快照」只会让前端误以为这是本次操作的结果。
    /// </summary>
    /// <param name="response">命令下发响应（含 <c>RejectReason</c>）。</param>
    /// <returns>延时任务下发结果。</returns>
    private static AnShengDelayTaskResultDto BuildRejected(AnShengCommandResponse response)
        => new()
        {
            Accepted = false,
            CommandId = response.CommandId,
            FrameId = response.FrameId,
            RejectReason = response.RejectReason,
            ErrorMessage = response.ErrorMessage,
            Tasks = null
        };

    // ─────────────────────────────────────────────────────────────
    // T10（定时任务）接口预留 —— 本任务范围外，刻意不实现
    //
    // 设计 §0.1 / §10.4 明确把 timeTask / timeEvent / setSlotTimeTasks 排除在 T8 之外。
    // 这里<b>不放 throw 桩方法</b>：它们不在 IAnShengScheduleService 上，谁也调不到，
    // 只会变成永远不被覆盖的死代码。真正需要守护的是「接口形状将来别打架」，
    // 因此把预留签名以契约注释的形式钉在这里：
    //
    //   Task<AnShengTimeTaskResultDto> StartTimeTaskAsync(
    //       long deviceId, int slotNum, string startTime, string endTime,
    //       IReadOnlyList<int> weekdays, CancellationToken ct = default);
    //
    //   Task<AnShengTimeTaskResultDto> SetSlotTimeTasksAsync(
    //       long deviceId, int slotNum, IReadOnlyList<AnShengTimeTaskItem> items,
    //       CancellationToken ct = default);
    //
    // T10 落地时的三条约束（与本文件已实现部分保持同构，别再发明第二套）：
    //   1. 下发仍走 IAnShengCommandService.SendCommandAsync，不得直连适配器；
    //   2. 镜像回写沿用「IgnoreQueryFilters + ResolveAppCodeAsync」这一对；
    //   3. 写后回读复用 ScheduleReadback 的模式（新作用域 + CancellationToken.None）。
    // ─────────────────────────────────────────────────────────────

    // ─────────────────────────────────────────────────────────────
    // T10 定时任务（设备权威镜像 + 写后回读 + 乐观并发）
    // ─────────────────────────────────────────────────────────────

    /// <summary>安圣协议方法名：查询所有插槽定时任务（写后回读用）。</summary>
    private const string MethodGetTimeTasks = "getTimeTasks";

    /// <summary>安圣协议方法名：整表覆盖定时任务。</summary>
    private const string MethodSetTimeTasks = "setTimeTasks";

    /// <summary>安圣协议方法名：查询单插槽定时任务（写后回读用）。</summary>
    private const string MethodGetSlotTimeTasks = "getSlotTimeTasks";

    /// <summary>安圣协议方法名：设置单插槽定时任务。</summary>
    private const string MethodSetSlotTimeTasks = "setSlotTimeTasks";

    /// <inheritdoc />
    public async Task<List<AnShengSlotTimeTaskSetDto>> GetTimeTasksAsync(
        long deviceId, CancellationToken ct = default)
    {
        // 刻意<b>不</b> IgnoreQueryFilters：本方法只服务 HTTP 作用域（同 GetDelayTasksAsync）。
        var rows = await _db.Set<AnShengTimeTask>()
            .AsNoTracking()
            .Where(t => t.DeviceId == deviceId)
            .OrderBy(t => t.SlotNum)
            .ThenBy(t => t.TaskKind)   // Normal(0) 在前，Loop(1) 在后
            .ThenBy(t => t.TaskIndex)
            .ToListAsync(ct);

        var now = UtcNow();
        var threshold = _options.EffectiveMirrorStaleThreshold;
        return ProjectSlots(rows, now, threshold);
    }

    /// <inheritdoc />
    public async Task<AnShengSlotTimeTaskSetDto?> GetSlotTimeTasksAsync(
        long deviceId, int slotNum, CancellationToken ct = default)
    {
        var rows = await _db.Set<AnShengTimeTask>()
            .AsNoTracking()
            .Where(t => t.DeviceId == deviceId && t.SlotNum == slotNum)
            .OrderBy(t => t.TaskKind)
            .ThenBy(t => t.TaskIndex)
            .ToListAsync(ct);

        if (rows.Count == 0)
        {
            return null;
        }

        var now = UtcNow();
        var threshold = _options.EffectiveMirrorStaleThreshold;
        var list = ProjectSlots(rows, now, threshold);
        return list.Count > 0 ? list[0] : null;
    }

    /// <inheritdoc />
    public async Task<AnShengTimeTaskResultDto> SetTimeTasksAsync(
        long deviceId, IReadOnlyList<AnShengSlotTimeTaskSet> slots, bool confirm,
        long? rowVersion = null, CancellationToken ct = default)
    {
        if (!confirm)
        {
            // 验收 #2：高危操作需二次确认，未确认直接业务拒绝、绝不下发。
            return BuildTimeRejected(
                AnShengCommandRejectReason.RejectedByConfirm,
                "定时任务为高危操作，需要二次确认（confirm=true）后才下发");
        }

        var parameters = BuildSetTimeTasksParameters(slots);
        var response = await _cmd.SendCommandAsync(deviceId, MethodSetTimeTasks, parameters, ct);
        if (!response.Success)
        {
            return BuildTimeRejected(response);
        }

        // 乐观镜像：命令已出网，先按「请求意图」整表覆盖一份（验收 #3）。
        try
        {
            await ReplaceTimeTasksAsync(deviceId, slots, rowVersion, ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "定时任务整表覆盖并发冲突: DeviceId={DeviceId}", deviceId);
            return new AnShengTimeTaskResultDto
            {
                Accepted = false,
                ConcurrencyConflict = true,
                ErrorMessage = "定时任务已被其他操作修改，请刷新后重试"
            };
        }

        var current = await ProjectSlotsFromDeviceAsync(deviceId, ct);
        ScheduleTimeTaskReadback(deviceId, MethodGetTimeTasks, null);
        return BuildTimeAccepted(response, current);
    }

    /// <inheritdoc />
    public async Task<AnShengTimeTaskResultDto> SetSlotTimeTasksAsync(
        long deviceId, int slotNum,
        IReadOnlyList<AnShengTimeTaskItem> timeTasks,
        IReadOnlyList<AnShengLoopTimeTaskItem> loopTimeTasks,
        bool confirm, long? rowVersion = null, CancellationToken ct = default)
    {
        if (!confirm)
        {
            // 验收 #2：同整表覆盖，需二次确认。
            return BuildTimeRejected(
                AnShengCommandRejectReason.RejectedByConfirm,
                "定时任务为高危操作，需要二次确认（confirm=true）后才下发");
        }

        var set = new AnShengSlotTimeTaskSet
        {
            SlotNum = slotNum,
            TimeTasks = timeTasks,
            LoopTimeTasks = loopTimeTasks
        };

        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["slotNum"] = slotNum,
            ["timeTasks"] = timeTasks.Select(ToTimeTaskWire).ToList(),
            ["loopTimeTasks"] = loopTimeTasks.Select(ToLoopTimeTaskWire).ToList()
        };

        var response = await _cmd.SendCommandAsync(deviceId, MethodSetSlotTimeTasks, parameters, ct);
        if (!response.Success)
        {
            return BuildTimeRejected(response);
        }

        try
        {
            await ReplaceTimeTasksAsync(deviceId, new[] { set }, rowVersion, ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "单插槽定时任务并发冲突: DeviceId={DeviceId}, SlotNum={SlotNum}", deviceId, slotNum);
            return new AnShengTimeTaskResultDto
            {
                Accepted = false,
                ConcurrencyConflict = true,
                ErrorMessage = "定时任务已被其他操作修改，请刷新后重试"
            };
        }

        var current = await ProjectSlotsFromDeviceAsync(deviceId, ct);
        ScheduleTimeTaskReadback(
            deviceId, MethodGetSlotTimeTasks,
            new Dictionary<string, object?> { ["slotNum"] = slotNum });
        return BuildTimeAccepted(response, current);
    }

    /// <inheritdoc />
    public async Task ApplyTimeTasksReadbackAsync(
        long deviceId, IReadOnlyList<AnShengSlotTimeTaskSet> slots, CancellationToken ct = default)
    {
        // 纵深防御：插槽号从 1 开始。任何 SlotNum ≤ 0 的集合都是「定位信息缺失」的产物
        // （典型如误从不含 slotNum 的 getSlotTimeTasks 应答里取值），写进去就是一行
        // 谁也查不到的幽灵数据，且会掩盖真正插槽的乐观值未被覆盖这一事实。宁可丢弃并告警。
        var valid = slots.Where(s => s.SlotNum > 0).ToList();
        if (valid.Count != slots.Count)
        {
            _logger.LogWarning(
                "定时任务回读写回丢弃了 {Dropped} 个非法插槽集合（SlotNum ≤ 0）: DeviceId={DeviceId}",
                slots.Count - valid.Count, deviceId);
        }

        if (valid.Count == 0)
        {
            return;
        }

        // 写后回读没有并发令牌：设备真值是权威，直接覆盖即可（验收 #3）。
        await ReplaceTimeTasksAsync(deviceId, valid, null, ct);
    }

    /// <inheritdoc />
    public async Task ApplyTimeEventAsync(
        long deviceId, int slotNum, int taskIndex, AnShengTimeEventTask task,
        IReadOnlyList<int>? slots, CancellationToken ct = default)
    {
        if (slotNum <= 0 || taskIndex <= 0)
        {
            // 验收 #4：非法定位信息直接跳过，不写镜像、不抛异常。
            _logger.LogWarning(
                "timeEvent 携带非法 slot_num/task_index，跳过镜像写回: DeviceId={DeviceId}, SlotNum={SlotNum}, TaskIndex={TaskIndex}",
                deviceId, slotNum, taskIndex);
            return;
        }

        var appCode = await ResolveAppCodeAsync(deviceId, ct);
        if (appCode == null)
        {
            _logger.LogWarning("timeEvent 写回失败：设备不存在或无租户码。DeviceId={DeviceId}", deviceId);
            return;
        }

        // 验收 #4：按 (SlotNum, Kind, TaskIndex) 就地更新，不额外发命令。
        var row = await _db.Set<AnShengTimeTask>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                t => t.DeviceId == deviceId && t.SlotNum == slotNum
                     && t.TaskKind == task.Kind && t.TaskIndex == taskIndex, ct);

        if (row == null)
        {
            // 设备权威：事件报了但我们镜像里没有这行 → 按事件真值新建（不丢数据）。
            row = new AnShengTimeTask
            {
                AppCode = appCode,
                DeviceId = deviceId,
                SlotNum = slotNum,
                TaskKind = task.Kind,
                TaskIndex = taskIndex
            };
            _db.Set<AnShengTimeTask>().Add(row);
        }

        row.TaskId = task.Id ?? string.Empty;
        row.Enable = task.Enable;
        row.WeekDays = AnShengTimeTask.SerializeWeekDays(task.WeekDays);
        row.Hour = task.Hour;
        row.Minute = task.Minute;
        row.Action = ClampAction(task.Action, string.Empty);
        row.UploadEnable = task.UploadEnable;
        row.SHour = task.SHour;
        row.SMinute = task.SMinute;
        row.EHour = task.EHour;
        row.EMinute = task.EMinute;
        row.OnMins = task.OnMins;
        row.OffMins = task.OffMins;
        row.SyncedAt = UtcNow();
        row.RowVersion = row.RowVersion + 1;

        await _db.SaveChangesAsync(ct);

        // 事件同帧可带 slots[] 快照，顺手刷新档案（与 DelayEventHandler 同策略）。
        if (slots != null && slots.Count > 0)
        {
            await UpdateSlotsSnapshotAsync(deviceId, slots, ct);
        }
    }

    /// <summary>
    /// 把读取到的镜像行投影成按插槽分组的只读 DTO（普通 / 循环两组）。
    /// </summary>
    private static List<AnShengSlotTimeTaskSetDto> ProjectSlots(
        IReadOnlyList<AnShengTimeTask> rows, DateTime now, TimeSpan threshold)
    {
        var bySlot = new SortedDictionary<int, AnShengSlotTimeTaskSetDto>();
        foreach (var t in rows)
        {
            if (!bySlot.TryGetValue(t.SlotNum, out var set))
            {
                set = new AnShengSlotTimeTaskSetDto { SlotNum = t.SlotNum };
                bySlot[t.SlotNum] = set;
            }

            var dto = new AnShengTimeTaskDto
            {
                SlotNum = t.SlotNum,
                TaskKind = t.TaskKind,
                TaskIndex = t.TaskIndex,
                TaskId = string.IsNullOrEmpty(t.TaskId) ? null : t.TaskId,
                Enable = t.Enable,
                WeekDays = AnShengTimeTask.ParseWeekDays(t.WeekDays),
                Hour = t.Hour,
                Minute = t.Minute,
                Action = t.Action,
                UploadEnable = t.UploadEnable,
                SHour = t.SHour,
                SMinute = t.SMinute,
                EHour = t.EHour,
                EMinute = t.EMinute,
                OnMins = t.OnMins,
                OffMins = t.OffMins,
                SyncedAt = t.SyncedAt,
                IsStale = (now - t.SyncedAt) > threshold,
                RowVersion = t.RowVersion
            };

            if (t.TaskKind == AnShengTimeTaskKind.Loop)
            {
                set.LoopTimeTasks.Add(dto);
            }
            else
            {
                set.TimeTasks.Add(dto);
            }
        }

        return bySlot.Values.ToList();
    }

    /// <summary>从数据库读取设备全部定时任务镜像并投影成 DTO（乐观快照用）。</summary>
    private async Task<List<AnShengSlotTimeTaskSetDto>> ProjectSlotsFromDeviceAsync(
        long deviceId, CancellationToken ct)
    {
        var rows = await _db.Set<AnShengTimeTask>()
            .AsNoTracking()
            .Where(t => t.DeviceId == deviceId)
            .OrderBy(t => t.SlotNum)
            .ThenBy(t => t.TaskKind)
            .ThenBy(t => t.TaskIndex)
            .ToListAsync(ct);

        return ProjectSlots(rows, UtcNow(), _options.EffectiveMirrorStaleThreshold);
    }

    /// <summary>
    /// 用设备真值<b>覆盖</b>指定插槽的定时任务镜像（整表 / 单插槽通用）。
    ///
    /// 后台作用域调用：全程 <c>IgnoreQueryFilters</c> + 显式 AppCode。删除不在期望集合内的既有行、
    /// 其余 upsert 并更新 <see cref="AnShengTimeTask.SyncedAt"/>。
    ///
    /// <paramref name="expectedRowVersion"/> 非空时，把<b>本次涉及插槽</b>的既有行 OriginalValue 设为该令牌，
    /// 使 UPDATE/DELETE 带 <c>AND RowVersion = @expected</c>；一旦被他人并发改动即 0 行 →
    /// <see cref="DbUpdateConcurrencyException"/>（验收 #5）。只对本插槽生效，避免误伤其它插槽的
    /// 不同 RowVersion 引发假冲突。
    /// </summary>
    private async Task ReplaceTimeTasksAsync(
        long deviceId, IReadOnlyList<AnShengSlotTimeTaskSet> slots,
        long? expectedRowVersion, CancellationToken ct)
    {
        var appCode = await ResolveAppCodeAsync(deviceId, ct);
        if (appCode == null)
        {
            _logger.LogWarning("定时任务镜像写回失败：设备不存在或无租户码。DeviceId={DeviceId}", deviceId);
            return;
        }

        var existing = await _db.Set<AnShengTimeTask>()
            .IgnoreQueryFilters()
            .Where(t => t.DeviceId == deviceId)
            .ToListAsync(ct);

        // 期望的 (SlotNum, Kind, TaskIndex) → 待写行
        var desired = new Dictionary<(int, AnShengTimeTaskKind, int), AnShengTimeTask>();
        foreach (var set in slots)
        {
            for (var i = 0; i < set.TimeTasks.Count; i++)
            {
                desired[(set.SlotNum, AnShengTimeTaskKind.Normal, i + 1)] =
                    ToRow(set.SlotNum, AnShengTimeTaskKind.Normal, i + 1, set.TimeTasks[i]);
            }

            for (var i = 0; i < set.LoopTimeTasks.Count; i++)
            {
                desired[(set.SlotNum, AnShengTimeTaskKind.Loop, i + 1)] =
                    ToRow(set.SlotNum, AnShengTimeTaskKind.Loop, i + 1, set.LoopTimeTasks[i]);
            }
        }

        // 并发令牌：仅施加于本次涉及的插槽，避免误伤其它插槽（验收 #5）。
        if (expectedRowVersion.HasValue)
        {
            var touchedSlots = new HashSet<int>(slots.Select(s => s.SlotNum));
            foreach (var row in existing)
            {
                if (touchedSlots.Contains(row.SlotNum))
                {
                    _db.Entry(row).Property(r => r.RowVersion).OriginalValue = expectedRowVersion.Value;
                }
            }
        }

        var byKey = new Dictionary<(int, AnShengTimeTaskKind, int), AnShengTimeTask>(existing.Count);
        foreach (var row in existing)
        {
            byKey[(row.SlotNum, row.TaskKind, row.TaskIndex)] = row; // 后写覆盖先写
        }

        var newVersion = existing.Count > 0 ? existing.Max(r => r.RowVersion) + 1 : 1;
        var now = UtcNow();

        foreach (var (key, newRow) in desired)
        {
            if (byKey.TryGetValue(key, out var old))
            {
                CopyRow(old, newRow);
                old.RowVersion = newVersion;
                old.SyncedAt = now;
            }
            else
            {
                newRow.AppCode = appCode;
                newRow.DeviceId = deviceId;
                newRow.RowVersion = newVersion;
                newRow.SyncedAt = now;
                _db.Set<AnShengTimeTask>().Add(newRow);
            }
        }

        // 删除「本次涉及插槽」内、不在期望集合里的既有行：单插槽覆盖只动本插槽
        // （其余插槽的任务不动），整表覆盖则客户端须下发全部插槽，未下发的插槽其任务
        // 自然不在 desired 中而被清——两种语义统一于此。
        var slotsToDelete = new HashSet<int>(slots.Select(s => s.SlotNum));
        foreach (var row in existing)
        {
            if (slotsToDelete.Contains(row.SlotNum)
                && !desired.ContainsKey((row.SlotNum, row.TaskKind, row.TaskIndex)))
            {
                _db.Set<AnShengTimeTask>().Remove(row);
            }
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogDebug(
            "定时任务镜像已覆盖写回: DeviceId={DeviceId}, AppCode={AppCode}, SlotSets={Count}, NewVersion={NewVersion}",
            deviceId, appCode, slots.Count, newVersion);
    }

    /// <summary>把整表覆盖请求映射为 setTimeTasks 的下发参数（<c>tasks[]</c> 数组）。</summary>
    private static Dictionary<string, object?> BuildSetTimeTasksParameters(
        IReadOnlyList<AnShengSlotTimeTaskSet> slots)
    {
        var tasks = new List<Dictionary<string, object?>>(slots.Count);
        foreach (var set in slots)
        {
            tasks.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["timeTasks"] = set.TimeTasks.Select(ToTimeTaskWire).ToList(),
                ["loopTimeTasks"] = set.LoopTimeTasks.Select(ToLoopTimeTaskWire).ToList()
            });
        }

        return new Dictionary<string, object?> { ["tasks"] = tasks };
    }

    /// <summary>把普通定时任务传输视图映射为下发报文项（平铺字段）。</summary>
    private static Dictionary<string, object?> ToTimeTaskWire(AnShengTimeTaskItem item)
        => new(StringComparer.Ordinal)
        {
            ["id"] = string.IsNullOrEmpty(item.Id) ? null : item.Id,
            ["enable"] = item.Enable,
            ["weekDays"] = item.WeekDays?.ToArray() ?? Array.Empty<int>(),
            ["hour"] = item.Hour,
            ["minute"] = item.Minute,
            ["action"] = item.Action,
            ["uploadEnable"] = item.UploadEnable
        };

    /// <summary>把循环定时任务传输视图映射为下发报文项（平铺字段）。</summary>
    private static Dictionary<string, object?> ToLoopTimeTaskWire(AnShengLoopTimeTaskItem item)
        => new(StringComparer.Ordinal)
        {
            ["id"] = string.IsNullOrEmpty(item.Id) ? null : item.Id,
            ["enable"] = item.Enable,
            ["weekDays"] = item.WeekDays?.ToArray() ?? Array.Empty<int>(),
            ["sHour"] = item.SHour,
            ["sMinute"] = item.SMinute,
            ["eHour"] = item.EHour,
            ["eMinute"] = item.EMinute,
            ["onMins"] = item.OnMins,
            ["offMins"] = item.OffMins
        };

    /// <summary>由传输视图构造一行镜像实体（普通定时）。</summary>
    private static AnShengTimeTask ToRow(int slotNum, AnShengTimeTaskKind kind, int index, AnShengTimeTaskItem item)
        => new()
        {
            SlotNum = slotNum,
            TaskKind = kind,
            TaskIndex = index,
            TaskId = item.Id ?? string.Empty,
            Enable = item.Enable,
            WeekDays = AnShengTimeTask.SerializeWeekDays(item.WeekDays),
            Hour = item.Hour,
            Minute = item.Minute,
            Action = ClampAction(item.Action, "on"),
            UploadEnable = item.UploadEnable
        };

    /// <summary>由传输视图构造一行镜像实体（循环定时）。</summary>
    private static AnShengTimeTask ToRow(int slotNum, AnShengTimeTaskKind kind, int index, AnShengLoopTimeTaskItem item)
        => new()
        {
            SlotNum = slotNum,
            TaskKind = kind,
            TaskIndex = index,
            TaskId = item.Id ?? string.Empty,
            Enable = item.Enable,
            WeekDays = AnShengTimeTask.SerializeWeekDays(item.WeekDays),
            SHour = item.SHour,
            SMinute = item.SMinute,
            EHour = item.EHour,
            EMinute = item.EMinute,
            OnMins = item.OnMins,
            OffMins = item.OffMins
        };

    /// <summary>把源行（已含最新字段）拷入被跟踪的旧行，供 upsert 就地更新。</summary>
    private static void CopyRow(AnShengTimeTask old, AnShengTimeTask src)
    {
        old.TaskId = src.TaskId;
        old.Enable = src.Enable;
        old.WeekDays = src.WeekDays;
        old.Hour = src.Hour;
        old.Minute = src.Minute;
        old.Action = src.Action;
        old.UploadEnable = src.UploadEnable;
        old.SHour = src.SHour;
        old.SMinute = src.SMinute;
        old.EHour = src.EHour;
        old.EMinute = src.EMinute;
        old.OnMins = src.OnMins;
        old.OffMins = src.OffMins;
    }

    /// <summary>
    /// 在<b>新作用域</b>里延迟触发一次定时任务写后回读（getTimeTasks / getSlotTimeTasks）。
    /// 复用 T8 <see cref="ScheduleReadback"/> 的模式：新作用域 + Task.Delay + CancellationToken.None。
    /// </summary>
    private void ScheduleTimeTaskReadback(
        long deviceId, string method, Dictionary<string, object?>? parameters)
    {
        var delayMs = _options.EffectiveReadbackDelayMs;

        _ = Task.Run(async () =>
        {
            try
            {
                if (delayMs > 0)
                {
                    await Task.Delay(delayMs).ConfigureAwait(false);
                }

                using var scope = _scopeFactory.CreateScope();
                var cmd = scope.ServiceProvider.GetRequiredService<IAnShengCommandService>();

                var response = await cmd
                    .SendCommandAsync(deviceId, method, parameters, CancellationToken.None)
                    .ConfigureAwait(false);

                if (response.Success)
                {
                    _logger.LogDebug(
                        "定时任务写后回读已下发: DeviceId={DeviceId}, Method={Method}, CommandId={CommandId}",
                        deviceId, method, response.CommandId);
                }
                else
                {
                    _logger.LogDebug(
                        "定时任务写后回读未受理（不影响本次下发结果）: DeviceId={DeviceId}, Method={Method}, Reason={Reason}",
                        deviceId, method, response.RejectReason);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "定时任务写后回读执行失败: DeviceId={DeviceId}, Method={Method}", deviceId, method);
            }
        });
    }

    /// <summary>构造「已受理」定时任务结果（乐观镜像快照）。</summary>
    private static AnShengTimeTaskResultDto BuildTimeAccepted(
        AnShengCommandResponse response, List<AnShengSlotTimeTaskSetDto> slots)
        => new()
        {
            Accepted = true,
            CommandId = response.CommandId,
            FrameId = response.FrameId,
            RejectReason = null,
            ErrorMessage = null,
            Payload = response.Payload,
            ConcurrencyConflict = false,
            Slots = slots
        };

    /// <summary>构造「未受理」定时任务结果（命令被 Guard 拒收，带 RejectReason）。</summary>
    private static AnShengTimeTaskResultDto BuildTimeRejected(AnShengCommandResponse response)
        => new()
        {
            Accepted = false,
            CommandId = response.CommandId,
            FrameId = response.FrameId,
            RejectReason = response.RejectReason,
            ErrorMessage = response.ErrorMessage,
            ConcurrencyConflict = false,
            Slots = null
        };

    /// <summary>构造「未受理」定时任务结果（业务规则拒绝，如缺少二次确认）。</summary>
    private static AnShengTimeTaskResultDto BuildTimeRejected(
        AnShengCommandRejectReason reason, string message)
        => new()
        {
            Accepted = false,
            CommandId = null,
            FrameId = null,
            RejectReason = reason,
            ErrorMessage = message,
            ConcurrencyConflict = false,
            Slots = null
        };
}
