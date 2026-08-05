using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Models;

namespace IoTPlatform.Services.Interfaces;

/// <summary>
/// 安圣延时任务调度服务（T8 业务编排）。
///
/// 【职责边界】
///   本服务<b>不直接碰 MQTT</b>——所有下行都经 <see cref="IAnShengCommandService.SendCommandAsync"/>
///   （T7 单点校验 + 先登记后下发 + 记录落库），保证喇叭类拒绝、零发布等语义与 T7 完全一致，
///   且不自造下发通道（铁律③）。
///   本服务只负责：① 编排「下发 + 写后回读」；② 维护平台镜像（<see cref="AnShengDelayTask"/> +
///   <see cref="AnShengDeviceProfile.SlotsSnapshot"/>）。
///
/// 【设备权威镜像（D6 Option A）】平台只存快照；start/stop 成功后立即返回「乐观镜像」，
///   随后在新作用域触发 <c>getDelayTasks</c>，其应答经 <see cref="AnShengMessageRouter"/> 钩子覆盖镜像。
///
/// 【后台作用域铁律（§7.1）】
///   <see cref="ApplyDelayTasksReadbackAsync"/> / <see cref="ApplyDelayEventAsync"/> /
///   <see cref="UpdateSlotsSnapshotAsync"/> 都可能在<b>后台作用域</b>被 Router / Handler 调用，
///   此时 <c>ITenantContextAccessor.Current</c> 为 null、全局过滤器不生效。
///   所有写都 <c>IgnoreQueryFilters</c> + 显式按 <see cref="AnShengDelayTask.DeviceId"/>（全局唯一）定位，
///   新建行时 AppCode 取自 Device 表（同设备必同租户）。
///
/// 【定时任务 T10】本任务仅实现延时部分；<c>StartTimeTaskAsync</c> / <c>SetSlotTimeTasksAsync</c> 等
///   定时方法仅留签名桩（见实现类），避免后续破坏接口稳定。
/// </summary>
public interface IAnShengScheduleService
{
    /// <summary>
    /// 开始/配置某插槽的延时任务。经 <c>startDelayTask</c> 下发；成功后写「乐观镜像」并
    /// 在<b>新作用域</b>写后回读一次 <c>getDelayTasks</c>。
    /// </summary>
    Task<AnShengDelayTaskResultDto> StartDelayTaskAsync(
        long deviceId, int slotNum, bool enable,
        string sAction, string eAction, int secs, CancellationToken ct = default);

    /// <summary>
    /// 停止某插槽的延时任务。经 <c>stopDelayTask</c> 下发 + 同样写后回读。
    /// </summary>
    Task<AnShengDelayTaskResultDto> StopDelayTaskAsync(
        long deviceId, int slotNum, CancellationToken ct = default);

    /// <summary>读取平台延时任务镜像（按插槽升序）。</summary>
    Task<List<AnShengDelayTaskDto>> GetDelayTasksAsync(
        long deviceId, CancellationToken ct = default);

    /// <summary>
    /// getDelayTasks 应答镜像回写（由 Router 钩子调用）。按下标 +1 推导 SlotNum，覆盖写全部插槽并 bump SyncedAt。
    /// </summary>
    Task ApplyDelayTasksReadbackAsync(
        long deviceId, IReadOnlyList<AnShengDelayTaskItem> tasks, CancellationToken ct = default);

    /// <summary>
    /// delayEvent 镜像更新（由 <c>DelayEventHandler</c> 调用）。该 <paramref name="slotNum"/> 行 <c>Enable=false</c>，
    /// 并视需要刷新 <see cref="AnShengDeviceProfile.SlotsSnapshot"/>。
    /// </summary>
    Task ApplyDelayEventAsync(
        long deviceId, int slotNum, IReadOnlyList<int>? slots, CancellationToken ct = default);

    /// <summary>把设备应答的 <c>slots[]</c> 写回 <see cref="AnShengDeviceProfile.SlotsSnapshot"/>。</summary>
    Task UpdateSlotsSnapshotAsync(
        long deviceId, IReadOnlyList<int> slots, CancellationToken ct = default);

    // ─────────────────────────────────────────────────────────────
    // T10 定时任务（设备权威镜像 + 写后回读 + 乐观并发）
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 读取平台定时任务镜像（按插槽升序，每插槽分普通 / 循环两组），每条带 <see cref="AnShengTimeTaskDto.IsStale"/>。
    /// 仅服务 HTTP 作用域，刻意不加 <c>IgnoreQueryFilters</c>（跨租户读的最后防线）。
    /// </summary>
    Task<List<AnShengSlotTimeTaskSetDto>> GetTimeTasksAsync(long deviceId, CancellationToken ct = default);

    /// <summary>
    /// 整表覆盖定时任务（<c>setTimeTasks</c>）。命令出网后即落「乐观镜像」并触发写后回读。
    /// <paramref name="confirm"/> 为 false 时直接业务拒绝、不下发（验收 #2）；
    /// <paramref name="rowVersion"/> 与服务器不一致时返回并发冲突（验收 #5，HTTP 409）。
    /// </summary>
    Task<AnShengTimeTaskResultDto> SetTimeTasksAsync(
        long deviceId, IReadOnlyList<AnShengSlotTimeTaskSet> slots, bool confirm,
        long? rowVersion = null, CancellationToken ct = default);

    /// <summary>读取单个插槽的定时任务镜像（普通 / 循环两组）。</summary>
    Task<AnShengSlotTimeTaskSetDto?> GetSlotTimeTasksAsync(
        long deviceId, int slotNum, CancellationToken ct = default);

    /// <summary>
    /// 单插槽定时任务设置（<c>setSlotTimeTasks</c>）。约束同 <see cref="SetTimeTasksAsync"/>；
    /// <paramref name="slotNum"/> 由调用方保证合法（控制器已拦截 &lt; 1，Guard 拦截越界）。
    /// </summary>
    Task<AnShengTimeTaskResultDto> SetSlotTimeTasksAsync(
        long deviceId, int slotNum,
        IReadOnlyList<AnShengTimeTaskItem> timeTasks,
        IReadOnlyList<AnShengLoopTimeTaskItem> loopTimeTasks,
        bool confirm, long? rowVersion = null, CancellationToken ct = default);

    /// <summary>
    /// getTimeTasks / getSlotTimeTasks 应答镜像回写（由 Router 钩子调用）。
    /// 用设备真值<b>覆盖</b>对应插槽的镜像并 bump <see cref="AnShengTimeTask.SyncedAt"/>（验收 #3）。
    /// 后台作用域调用，内部 <c>IgnoreQueryFilters</c> + 显式 AppCode。
    /// </summary>
    Task ApplyTimeTasksReadbackAsync(
        long deviceId, IReadOnlyList<AnShengSlotTimeTaskSet> slots, CancellationToken ct = default);

    /// <summary>
    /// timeEvent 镜像就地更新（由 <c>TimeEventHandler</c> 调用，验收 #4）。
    /// 按 <c>(SlotNum, Kind, TaskIndex)</c> 定位行并覆盖字段，<b>不</b>额外发命令；
    /// 行不存在时按设备权威新建。后台作用域调用。
    /// </summary>
    Task ApplyTimeEventAsync(
        long deviceId, int slotNum, int taskIndex, AnShengTimeEventTask task,
        IReadOnlyList<int>? slots, CancellationToken ct = default);
}
