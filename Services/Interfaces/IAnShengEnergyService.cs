using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Models;

namespace IoTPlatform.Services.Interfaces;

/// <summary>
/// 安圣电量计服务（T11 业务编排：实时 / 统计 / 校准）。
///
/// 【职责边界】
///   本服务<b>不直接碰 MQTT</b>——所有下行都经 <see cref="IAnShengCommandService.SendCommandAsync"/>
///   （T7 单点校验 + 先登记后下发 + 记录落库），因此「喇叭类拒绝校准命令」这条验收
///   由 <c>AnShengCommandCatalog</c>（GroupSwitchAction / GroupTimeTask）+ <c>AnShengCommandGuard</c>
///   <b>结构性</b>保证，本服务不写第二份品类判断（铁律③）。
///
/// 【与 T8/T10 的关键差异：没有乐观镜像】
///   延时 / 定时任务是「平台写、设备执行」，可以先落乐观值；
///   电量计是「设备测、平台读」——平台没有资格替设备猜一个电量值。
///   因此聚合表只在<b>设备应答真的回来</b>时才写（Router 钩子 → <see cref="ApplyStatisticsReadbackAsync"/>）。
///
/// 【分层存储（设计 D5）】
///   · 实时 <c>getEMRealtime.data[]</c>（每插槽 v/c/p/e）⇒ 归一化成 <c>slot{n}_*</c> 走既有
///     <see cref="IDataCollectionService"/> 落 <c>DeviceDataRecord</c>（验收 #5），零改动复用告警引擎；
///   · 统计 <c>getEMStatistics.data[]</c>（多粒度序列）⇒ UPSERT 进 <see cref="AnShengEmStatistic"/>
///     聚合表，唯一键 <c>(DeviceId, SlotNum, Granularity, PeriodKey)</c> 保证幂等（验收 #1）。
///
/// 【后台作用域铁律（设计 §7.1）】
///   <see cref="ApplyStatisticsReadbackAsync"/> / <see cref="ApplyRealtimeReadbackAsync"/> 由 Router
///   在<b>后台作用域</b>调用，<c>ITenantContextAccessor.Current</c> 为 null、全局过滤器不生效。
///   两者内部一律 <c>IgnoreQueryFilters</c> + 按 <c>DeviceId</c>（全局唯一）显式定位，
///   新建行的 AppCode 显式取自 Devices 表。反之 <see cref="QueryStatisticsAsync"/> 只服务 HTTP 作用域，
///   <b>刻意不加</b> <c>IgnoreQueryFilters</c> —— 那是跨租户读的最后防线。
/// </summary>
public interface IAnShengEnergyService
{
    // ─────────────────────────────────────────────────────────────
    // 下发编排（HTTP 作用域）
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 下发 <c>getEMRealtime</c>，拉取电量计实时读数。
    /// 应答经 Router 钩子 → <see cref="ApplyRealtimeReadbackAsync"/> 归一化入 <c>DeviceDataRecord</c>（验收 #5）。
    /// </summary>
    /// <param name="deviceId">设备主键。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>下发受理结果。</returns>
    Task<AnShengEnergyResultDto> RequestRealtimeAsync(long deviceId, CancellationToken ct = default);

    /// <summary>
    /// 下发 <c>getEMStatistics</c>，拉取电量计统计。
    /// 应答经 Router 钩子 → <see cref="ApplyStatisticsReadbackAsync"/> UPSERT 进聚合表（验收 #1/#2/#3）。
    /// </summary>
    /// <param name="deviceId">设备主键。</param>
    /// <param name="q">查询串（all/month/day/hour/hourSum/total，可逗号组合）；null 表示不带该参数。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>下发受理结果。</returns>
    Task<AnShengEnergyResultDto> RequestStatisticsAsync(
        long deviceId, string? q = null, CancellationToken ct = default);

    /// <summary>
    /// 下发 <c>clearEMStatistics</c>，清空<b>设备侧</b>统计。
    ///
    /// 平台聚合表数据一行不删（设计 D5），命令成功出网后追加一条
    /// <see cref="AnShengEventKind.EmCleared"/> 标记事件用于对账（验收 #4）。
    /// <paramref name="confirm"/> 为 false 时直接业务拒绝、<b>不下发</b>。
    /// </summary>
    /// <param name="deviceId">设备主键。</param>
    /// <param name="slotNum">插槽编号，从 1 开始；null 或 0 表示所有插槽。</param>
    /// <param name="confirm">二次确认。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>下发受理结果。</returns>
    Task<AnShengEnergyResultDto> ClearStatisticsAsync(
        long deviceId, int? slotNum, bool confirm, CancellationToken ct = default);

    /// <summary>下发 <c>getCalParams</c>（仅开关类放行，验收 #6）。</summary>
    /// <param name="deviceId">设备主键。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>下发受理结果。</returns>
    Task<AnShengEnergyResultDto> GetCalParamsAsync(long deviceId, CancellationToken ct = default);

    /// <summary>下发 <c>setCalParams</c>（仅开关类放行，验收 #6）。</summary>
    /// <param name="deviceId">设备主键。</param>
    /// <param name="calParams">校准参数字典（含 <c>RL</c>）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>下发受理结果。</returns>
    Task<AnShengEnergyResultDto> SetCalParamsAsync(
        long deviceId, IReadOnlyDictionary<string, double> calParams, CancellationToken ct = default);

    /// <summary>下发 <c>resetCalParams</c>（仅开关类放行，验收 #6）。</summary>
    /// <param name="deviceId">设备主键。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>下发受理结果。</returns>
    Task<AnShengEnergyResultDto> ResetCalParamsAsync(long deviceId, CancellationToken ct = default);

    /// <summary>下发 <c>autoCal</c>（仅开关类放行，验收 #6）。</summary>
    /// <param name="deviceId">设备主键。</param>
    /// <param name="power">已知负载功率（W）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>下发受理结果。</returns>
    Task<AnShengEnergyResultDto> AutoCalAsync(
        long deviceId, double power, CancellationToken ct = default);

    // ─────────────────────────────────────────────────────────────
    // 只读查询（HTTP 作用域）
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 读取平台电量计统计聚合表（按插槽 → 粒度 → 周期键升序）。
    /// 仅服务 HTTP 作用域，<b>刻意不加</b> <c>IgnoreQueryFilters</c>（跨租户读的最后防线）。
    /// </summary>
    /// <param name="deviceId">设备主键。</param>
    /// <param name="slotNum">按插槽过滤；null 表示全部插槽。</param>
    /// <param name="granularity">按粒度过滤；null 表示全部粒度。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>统计行列表。</returns>
    Task<List<AnShengEmStatisticDto>> QueryStatisticsAsync(
        long deviceId, int? slotNum = null, AnShengEmGranularity? granularity = null,
        CancellationToken ct = default);

    // ─────────────────────────────────────────────────────────────
    // 应答写回（后台作用域，由 Router 钩子调用）
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>getEMStatistics</c> 应答写回：按唯一键
    /// <c>(DeviceId, SlotNum, Granularity, PeriodKey)</c> 幂等 UPSERT（验收 #1/#2/#3）。
    ///
    /// <paramref name="snapshot"/> 的 <c>DataLength</c> 与 <c>AnShengDeviceProfile.SlotAmount</c>
    /// 不一致时<b>整帧拒绝入库并告警</b>（§7-R8）：插槽号是按下标推导的，
    /// 长度对不上意味着推导必然错位，写进去的每一行都会挂到错误的插槽上。
    /// </summary>
    /// <param name="deviceId">设备主键。</param>
    /// <param name="snapshot">解析后的统计快照。</param>
    /// <param name="ct">取消令牌。</param>
    Task ApplyStatisticsReadbackAsync(
        long deviceId, AnShengEmStatisticsSnapshot snapshot, CancellationToken ct = default);

    /// <summary>
    /// <c>getEMRealtime</c> 应答写回：归一化成 <c>slot{n}_voltage</c> / <c>slot{n}_current</c> /
    /// <c>slot{n}_power</c> / <c>slot{n}_energy</c>，经 <see cref="IDataCollectionService"/>
    /// 落 <c>DeviceDataRecord</c>（验收 #5）。
    /// </summary>
    /// <param name="deviceId">设备主键。</param>
    /// <param name="slots">逐插槽实时读数。</param>
    /// <param name="deviceTimestampUtc">设备上报时间（UTC）；不可信或缺失时由实现回退到平台时间。</param>
    /// <param name="ct">取消令牌。</param>
    Task ApplyRealtimeReadbackAsync(
        long deviceId, IReadOnlyList<AnShengEmRealtimeSlot> slots,
        DateTime? deviceTimestampUtc = null, CancellationToken ct = default);
}
