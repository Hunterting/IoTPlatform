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
}
