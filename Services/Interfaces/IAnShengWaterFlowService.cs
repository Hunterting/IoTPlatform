using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Models;

namespace IoTPlatform.Services.Interfaces;

/// <summary>
/// 安圣水流量服务（T12 业务编排：查询 / 配置 / 实时回读）。
///
/// 【职责边界】
///   本服务<b>不直接碰 MQTT</b>——所有下行都经 <see cref="IAnShengCommandService.SendCommandAsync"/>
///   （T7 单点校验 + 先登记后下发 + 记录落库），因此「喇叭类拒绝校准命令」这类验收
///   由 <c>AnShengCommandCatalog</c>（GroupSwitchAction）+ <c>AnShengCommandGuard</c>
///   <b>结构性</b>保证，本服务不写第二份品类判断（铁律③）。
///
/// 【与 T11 电量计一致：没有乐观镜像】
///   水流量是「设备测、平台读」——平台没有资格替设备猜一个流量值。
///   因此数据只在<b>设备应答真的回来</b>时才写（Router 钩子 → <see cref="ApplyFlowInfoReadbackAsync"/>）。
///
/// 【分层存储】
///   · 实时 <c>getFlowInfo</c>（iFlow / tFlow / tPulses）⇒ 归一化成 <c>waterflow</c> / <c>watertotal</c> /
///     <c>wpulses</c> 走既有 <see cref="IDataCollectionService"/> 落 <c>DeviceDataRecord</c>（零改动复用告警引擎）；
///   · 配置 <c>getFlowConfig</c>（k / offset / minFlow / maxFlow）⇒ UPSERT 进 <see cref="AnShengWaterFlowConfig"/>
///     镜像表，唯一键 <c>(DeviceId, AppCode)</c> 保证幂等。
///
/// 【后台作用域铁律（设计 §7.1）】
///   <see cref="ApplyFlowInfoReadbackAsync"/> / <see cref="ApplyFlowConfigReadbackAsync"/> 由 Router
///   在<b>后台作用域</b>调用，<c>ITenantContextAccessor.Current</c> 为 null、全局过滤器不生效。
///   两者内部一律 <c>IgnoreQueryFilters</c> + 按 <c>DeviceId</c>（全局唯一）显式定位，
///   新建行的 AppCode 显式取自 Devices 表。反之 <see cref="GetLatestFlowInfoAsync"/> /
///   <see cref="GetFlowConfigMirrorAsync"/> 只服务 HTTP 作用域，<b>刻意不加</b> <c>IgnoreQueryFilters</c> ——
///   那是跨租户读的最后防线。
/// </summary>
public interface IAnShengWaterFlowService
{
    // ─────────────────────────────────────────────────────────────
    // 下发编排（HTTP 作用域）
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 下发 <c>getFlowInfo</c>，拉取水流量实时读数。
    /// 应答经 Router 钩子 → <see cref="ApplyFlowInfoReadbackAsync"/> 归一化入 <c>DeviceDataRecord</c>。
    /// </summary>
    /// <param name="deviceId">设备主键。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>下发受理结果。</returns>
    Task<AnShengEnergyResultDto> RequestFlowInfoAsync(long deviceId, CancellationToken ct = default);

    /// <summary>【B1】带 principal 的实时读数请求；内部走命令服务单点归属校验。</summary>
    Task<AnShengEnergyResultDto> RequestFlowInfoAsync(
        long deviceId, ClaimsPrincipal? principal, CancellationToken ct = default);

    /// <summary>
    /// 下发 <c>resetTotalFlow</c>，重置设备累计流量。
    /// </summary>
    /// <param name="deviceId">设备主键。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>下发受理结果。</returns>
    Task<AnShengEnergyResultDto> ResetTotalFlowAsync(long deviceId, CancellationToken ct = default);

    /// <summary>【B1】带 principal 的累计流量重置。</summary>
    Task<AnShengEnergyResultDto> ResetTotalFlowAsync(
        long deviceId, ClaimsPrincipal? principal, CancellationToken ct = default);

    /// <summary>
    /// 下发 <c>getFlowConfig</c>，拉取水流量配置。
    /// 应答经 Router 钩子 → <see cref="ApplyFlowConfigReadbackAsync"/> UPSERT 进镜像表。
    /// </summary>
    /// <param name="deviceId">设备主键。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>下发受理结果。</returns>
    Task<AnShengEnergyResultDto> GetFlowConfigAsync(long deviceId, CancellationToken ct = default);

    /// <summary>【B1】带 principal 的流量配置拉取（名义读、实际下发命令）。</summary>
    Task<AnShengEnergyResultDto> GetFlowConfigAsync(
        long deviceId, ClaimsPrincipal? principal, CancellationToken ct = default);

    /// <summary>
    /// 下发 <c>setFlowConfig</c>，设置水流量配置（k / offset / minFlow / maxFlow，仅非 null 的才下发）。
    /// </summary>
    /// <param name="deviceId">设备主键。</param>
    /// <param name="request">配置请求（四个参数均可选）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>下发受理结果。</returns>
    Task<AnShengEnergyResultDto> SetFlowConfigAsync(
        long deviceId, AnShengWaterFlowConfigRequest? request, CancellationToken ct = default);

    /// <summary>【B1】带 principal 的流量配置下发。</summary>
    Task<AnShengEnergyResultDto> SetFlowConfigAsync(
        long deviceId, AnShengWaterFlowConfigRequest? request,
        ClaimsPrincipal? principal, CancellationToken ct = default);

    /// <summary>
    /// 下发 <c>resetFlowConfig</c>，重置水流量配置。
    /// </summary>
    /// <param name="deviceId">设备主键。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>下发受理结果。</returns>
    Task<AnShengEnergyResultDto> ResetFlowConfigAsync(long deviceId, CancellationToken ct = default);

    /// <summary>【B1】带 principal 的流量配置重置。</summary>
    Task<AnShengEnergyResultDto> ResetFlowConfigAsync(
        long deviceId, ClaimsPrincipal? principal, CancellationToken ct = default);

    // ─────────────────────────────────────────────────────────────
    // 只读查询（HTTP 作用域）
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 读取平台水流量最新读数（该设备最新的 <c>DeviceDataRecord</c> 的 WaterFlow / WaterTotal / WaterPulses）。
    /// 仅服务 HTTP 作用域，<b>刻意不加</b> <c>IgnoreQueryFilters</c>（跨租户读的最后防线）。
    /// </summary>
    /// <param name="deviceId">设备主键。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>流量信息 DTO；该设备尚无数据记录时为 <c>null</c>。</returns>
    Task<AnShengWaterFlowInfoDto?> GetLatestFlowInfoAsync(long deviceId, CancellationToken ct = default);

    /// <summary>
    /// 读取平台水流量配置镜像（<c>ansheng_wf_config</c>）。
    /// 仅服务 HTTP 作用域，<b>刻意不加</b> <c>IgnoreQueryFilters</c>（跨租户读的最后防线）。
    /// </summary>
    /// <param name="deviceId">设备主键。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>配置 DTO；该设备尚无镜像时为 <c>null</c>。</returns>
    Task<AnShengWaterFlowConfigDto?> GetFlowConfigMirrorAsync(long deviceId, CancellationToken ct = default);

    // ─────────────────────────────────────────────────────────────
    // 应答写回（后台作用域，由 Router 钩子调用）
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>getFlowInfo</c> 应答写回：把 iFlow→WaterFlow、tFlow→WaterTotal、tPulses→WaterPulses
    /// 归一化后经 <see cref="IDataCollectionService"/> 落 <c>DeviceDataRecord</c>（零改动复用告警引擎）。
    /// </summary>
    /// <param name="deviceId">设备主键。</param>
    /// <param name="snapshot">解析后的流量信息快照。</param>
    /// <param name="ct">取消令牌。</param>
    Task ApplyFlowInfoReadbackAsync(
        long deviceId, AnShengWaterFlowInfoSnapshot snapshot, CancellationToken ct = default);

    /// <summary>
    /// <c>getFlowConfig</c> 应答写回：按唯一键 <c>(DeviceId, AppCode)</c> 幂等 UPSERT 进
    /// <see cref="AnShengWaterFlowConfig"/> 镜像表（设备权威、平台只读镜像）。
    /// </summary>
    /// <param name="deviceId">设备主键。</param>
    /// <param name="snapshot">解析后的流量配置快照。</param>
    /// <param name="ct">取消令牌。</param>
    Task ApplyFlowConfigReadbackAsync(
        long deviceId, AnShengWaterFlowConfigSnapshot snapshot, CancellationToken ct = default);
}
