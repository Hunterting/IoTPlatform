// T11：安圣电量计 API（仅后端，不含前端面板）。
//
// 【路由不冲突】与 AnShengScheduleController / AnShengSwitchController 同处
//   `api/v1/ansheng` 模板层级，本控制器的字面量段以 `energy/` 打头
//   （energy/realtime、energy/statistics、energy/statistics/refresh、energy/statistics/clear、
//    energy/cal-params、energy/cal-params/reset、energy/cal-params/auto），
//   与既有 `action` / `delay-tasks` / `time-tasks` 等互不重叠，不会触发 AmbiguousMatchException。
//
// 【拒绝信封（铁律②）】业务失败一律 ApiResponse<T>.BadRequest(message) / .Error(message)，HTTP 恒 200。
//   <b>禁止</b>裸 BadRequest() / StatusCode(400)。验收引用的「400」指 ApiResponse.Code=400，
//   不是 HTTP 400。电量计<b>无乐观并发令牌</b>（平台只累积保留、不写乐观镜像），故本控制器
//   不返回 HTTP 409 —— 全部结果都走 200（成功）或 200 + Code=400（被拒）。
//
// 【下发唯一入口（铁律③）】所有写端点调 IAnShengEnergyService，其内部只走
//   IAnShengCommandService.SendCommandAsync；本控制器不碰 MQTT / Builder / Guard。
//   仅开关类放行由 Catalog(GroupSwitchAction / GroupTimeTask) + Guard 结构性保证（验收 #6）。
//
// 【设备权威、平台只读（验收 #4/#5）】统计 / 实时数据只在设备应答真回来后才落库
//   （Router 钩子 → Apply*ReadbackAsync），本控制器 GET 端点只做查询转发。

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Configuration;
using IoTPlatform.Filters;
using IoTPlatform.Helpers;
using IoTPlatform.Models;
using IoTPlatform.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace IoTPlatform.Controllers;

/// <summary>
/// 安圣二开设备「电量计」控制器（T11）。
///
/// 端点一览（均挂载于 <c>api/v1/ansheng/{deviceId}</c> 之下）：
/// <list type="bullet">
///   <item><c>POST {deviceId}/energy/realtime</c> —— 拉取电量计实时读数（应答归一化入 DeviceDataRecord，验收 #5）；</item>
///   <item><c>POST {deviceId}/energy/statistics/refresh</c> —— 拉取电量计统计（应答 UPSERT 进聚合表，验收 #1/#2/#3）；</item>
///   <item><c>POST {deviceId}/energy/statistics/clear</c> —— 清空设备侧统计（平台只记标记事件、聚合表保留，验收 #4）；</item>
///   <item><c>GET  {deviceId}/energy/statistics</c> —— 读平台电量计统计聚合表（按插槽 / 粒度过滤）；</item>
///   <item><c>GET  {deviceId}/energy/cal-params</c> —— 读取校准参数（仅开关类放行，验收 #6）；</item>
///   <item><c>POST {deviceId}/energy/cal-params</c> —— 设置校准参数（仅开关类放行，验收 #6）；</item>
///   <item><c>POST {deviceId}/energy/cal-params/reset</c> —— 重置校准参数（仅开关类放行，验收 #6）；</item>
///   <item><c>POST {deviceId}/energy/cal-params/auto</c> —— 按已知负载功率自动校准（仅开关类放行，验收 #6）。</item>
/// </list>
/// </summary>
[ApiController]
[Route("api/v1/ansheng")]
[PermissionAuthorize(Permissions.VIEW_DEVICES)]
public class AnShengEnergyController : ControllerBase
{
    private readonly IAnShengEnergyService _energyService;
    private readonly ILogger<AnShengEnergyController> _logger;

    /// <summary>构造控制器。</summary>
    public AnShengEnergyController(
        IAnShengEnergyService energyService,
        ILogger<AnShengEnergyController> logger)
    {
        _energyService = energyService;
        _logger = logger;
    }

    // ─────────────────────────────────────────────
    // 实时电量计
    // ─────────────────────────────────────────────

    /// <summary>
    /// 下发 <c>getEMRealtime</c>，拉取电量计实时读数。
    /// 应答经 Router 钩子 → <see cref="IAnShengEnergyService.ApplyRealtimeReadbackAsync"/> 归一化入
    /// <c>DeviceDataRecord</c>（验收 #5）。本端点只回下发受理情况，真值需轮询数据曲线。
    /// </summary>
    /// <param name="deviceId">设备主键。</param>
    /// <returns>下发受理结果。</returns>
    [HttpPost("{deviceId:long}/energy/realtime")]
    [PermissionAuthorize(Permissions.SEND_DEVICE_COMMANDS)]
    public async Task<ActionResult<ApiResponse<AnShengEnergyResultDto>>> RequestRealtime(long deviceId)
    {
        var ct = HttpContext.RequestAborted;
        try
        {
            var result = await _energyService.RequestRealtimeAsync(deviceId, ct);
            return result.Accepted
                ? ApiResponse<AnShengEnergyResultDto>.Success(result, "电量计实时读数请求已下发")
                : ApiResponse<AnShengEnergyResultDto>.BadRequest(
                    result.ErrorMessage ?? "实时读数请求下发失败", result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "下发电量计实时读数请求失败: DeviceId={DeviceId}", deviceId);
            return ApiResponse<AnShengEnergyResultDto>.Error($"实时读数请求下发失败：{ex.Message}");
        }
    }

    // ─────────────────────────────────────────────
    // 统计电量计
    // ─────────────────────────────────────────────

    /// <summary>
    /// 下发 <c>getEMStatistics</c>，拉取电量计统计。
    /// 应答经 Router 钩子 → <see cref="IAnShengEnergyService.ApplyStatisticsReadbackAsync"/> 按唯一键
    /// <c>(DeviceId, SlotNum, Granularity, PeriodKey)</c> 幂等 UPSERT 进聚合表（验收 #1/#2/#3）。
    /// </summary>
    /// <param name="deviceId">设备主键。</param>
    /// <param name="request">拉取请求（含可选查询串 q）。</param>
    /// <returns>下发受理结果。</returns>
    [HttpPost("{deviceId:long}/energy/statistics/refresh")]
    [PermissionAuthorize(Permissions.SEND_DEVICE_COMMANDS)]
    public async Task<ActionResult<ApiResponse<AnShengEnergyResultDto>>> RefreshStatistics(
        long deviceId, [FromBody] AnShengGetEMStatisticsRequest? request)
    {
        var ct = HttpContext.RequestAborted;
        try
        {
            var q = request?.Q;
            var result = await _energyService.RequestStatisticsAsync(deviceId, q, ct);
            return result.Accepted
                ? ApiResponse<AnShengEnergyResultDto>.Success(result, "电量计统计请求已下发")
                : ApiResponse<AnShengEnergyResultDto>.BadRequest(
                    result.ErrorMessage ?? "统计请求下发失败", result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "下发电量计统计请求失败: DeviceId={DeviceId}", deviceId);
            return ApiResponse<AnShengEnergyResultDto>.Error($"统计请求下发失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 下发 <c>clearEMStatistics</c>，清空<b>设备侧</b>统计。
    ///
    /// 平台聚合表数据一行不删（设计 D5），命令成功出网后平台追加一条
    /// <see cref="AnShengEventKind.EmCleared"/> 标记事件用于对账（验收 #4）。
    /// <c>confirm=false</c> 时直接业务拒绝、<b>不下发</b>。
    /// </summary>
    /// <param name="deviceId">设备主键。</param>
    /// <param name="request">清空请求（含 confirm / slotNum）。</param>
    /// <returns>下发受理结果。</returns>
    [HttpPost("{deviceId:long}/energy/statistics/clear")]
    [PermissionAuthorize(Permissions.SEND_DEVICE_COMMANDS)]
    public async Task<ActionResult<ApiResponse<AnShengEnergyResultDto>>> ClearStatistics(
        long deviceId, [FromBody] AnShengClearEMStatisticsRequest? request)
    {
        var ct = HttpContext.RequestAborted;
        try
        {
            if (request == null)
            {
                return ApiResponse<AnShengEnergyResultDto>.BadRequest("请求体不能为空");
            }

            var result = await _energyService.ClearStatisticsAsync(
                deviceId, request.SlotNum, request.Confirm, ct);

            return result.Accepted
                ? ApiResponse<AnShengEnergyResultDto>.Success(result, "电量计统计清零请求已下发（平台数据保留）")
                : ApiResponse<AnShengEnergyResultDto>.BadRequest(
                    result.ErrorMessage ?? "清零请求下发失败", result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "下发电量计清零请求失败: DeviceId={DeviceId}", deviceId);
            return ApiResponse<AnShengEnergyResultDto>.Error($"清零请求下发失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 读取平台电量计统计聚合表（设备权威镜像，平台只累积保留）。
    /// 按插槽号 / 粒度过滤；返回的 <see cref="AnShengEmStatisticDto.IsStale"/> 表示是否超过 24h 未同步。
    /// </summary>
    /// <param name="deviceId">设备主键。</param>
    /// <param name="slotNum">按插槽过滤；留空表示全部插槽。</param>
    /// <param name="granularity">按粒度过滤；留空表示全部粒度。</param>
    /// <returns>统计行列表（按插槽 → 粒度 → 周期键升序）。</returns>
    [HttpGet("{deviceId:long}/energy/statistics")]
    public async Task<ActionResult<ApiResponse<List<AnShengEmStatisticDto>>>> GetStatistics(
        long deviceId,
        [FromQuery] int? slotNum = null,
        [FromQuery] AnShengEmGranularity? granularity = null)
    {
        var ct = HttpContext.RequestAborted;
        try
        {
            if (slotNum.HasValue && slotNum.Value < 1)
            {
                return ApiResponse<List<AnShengEmStatisticDto>>.BadRequest("slotNum 必须 ≥ 1");
            }

            var rows = await _energyService.QueryStatisticsAsync(deviceId, slotNum, granularity, ct);
            return ApiResponse<List<AnShengEmStatisticDto>>.Success(rows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询电量计统计失败: DeviceId={DeviceId}", deviceId);
            return ApiResponse<List<AnShengEmStatisticDto>>.Error($"查询电量计统计失败：{ex.Message}");
        }
    }

    // ─────────────────────────────────────────────
    // 校准（仅开关类放行，验收 #6）
    // ─────────────────────────────────────────────

    /// <summary>
    /// 下发 <c>getCalParams</c>，读取校准参数。仅开关类设备由 Guard 放行（验收 #6）。
    /// </summary>
    /// <param name="deviceId">设备主键。</param>
    /// <returns>下发受理结果。</returns>
    [HttpGet("{deviceId:long}/energy/cal-params")]
    [PermissionAuthorize(Permissions.SEND_DEVICE_COMMANDS)]
    public async Task<ActionResult<ApiResponse<AnShengEnergyResultDto>>> GetCalParams(long deviceId)
    {
        var ct = HttpContext.RequestAborted;
        try
        {
            var result = await _energyService.GetCalParamsAsync(deviceId, ct);
            return result.Accepted
                ? ApiResponse<AnShengEnergyResultDto>.Success(result, "读取校准参数请求已下发")
                : ApiResponse<AnShengEnergyResultDto>.BadRequest(
                    result.ErrorMessage ?? "读取校准参数请求下发失败", result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "下发读取校准参数请求失败: DeviceId={DeviceId}", deviceId);
            return ApiResponse<AnShengEnergyResultDto>.Error($"读取校准参数请求下发失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 下发 <c>setCalParams</c>，设置校准参数（含 <c>RL</c> 校准电阻值）。仅开关类放行（验收 #6）。
    /// <c>RL</c> 与 <c>CalParams</c> 同时提供时以字典为准，但字典缺 <c>RL</c> 而 <c>RL</c> 有值时会自动补入。
    /// </summary>
    /// <param name="deviceId">设备主键。</param>
    /// <param name="request">校准参数请求（RL 与 CalParams 字典）。</param>
    /// <returns>下发受理结果。</returns>
    [HttpPost("{deviceId:long}/energy/cal-params")]
    [PermissionAuthorize(Permissions.SEND_DEVICE_COMMANDS)]
    public async Task<ActionResult<ApiResponse<AnShengEnergyResultDto>>> SetCalParams(
        long deviceId, [FromBody] AnShengSetCalParamsRequest? request)
    {
        var ct = HttpContext.RequestAborted;
        try
        {
            if (request == null)
            {
                return ApiResponse<AnShengEnergyResultDto>.BadRequest("请求体不能为空");
            }

            var calParams = MergeCalParams(request);
            if (calParams.Count == 0)
            {
                return ApiResponse<AnShengEnergyResultDto>.BadRequest(
                    "calParams 不能为空，至少需要提供 RL（校准电阻值）");
            }

            var result = await _energyService.SetCalParamsAsync(deviceId, calParams, ct);
            return result.Accepted
                ? ApiResponse<AnShengEnergyResultDto>.Success(result, "设置校准参数请求已下发")
                : ApiResponse<AnShengEnergyResultDto>.BadRequest(
                    result.ErrorMessage ?? "设置校准参数请求下发失败", result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "下发设置校准参数请求失败: DeviceId={DeviceId}", deviceId);
            return ApiResponse<AnShengEnergyResultDto>.Error($"设置校准参数请求下发失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 下发 <c>resetCalParams</c>，重置校准参数。仅开关类放行（验收 #6）。
    /// </summary>
    /// <param name="deviceId">设备主键。</param>
    /// <returns>下发受理结果。</returns>
    [HttpPost("{deviceId:long}/energy/cal-params/reset")]
    [PermissionAuthorize(Permissions.SEND_DEVICE_COMMANDS)]
    public async Task<ActionResult<ApiResponse<AnShengEnergyResultDto>>> ResetCalParams(long deviceId)
    {
        var ct = HttpContext.RequestAborted;
        try
        {
            var result = await _energyService.ResetCalParamsAsync(deviceId, ct);
            return result.Accepted
                ? ApiResponse<AnShengEnergyResultDto>.Success(result, "重置校准参数请求已下发")
                : ApiResponse<AnShengEnergyResultDto>.BadRequest(
                    result.ErrorMessage ?? "重置校准参数请求下发失败", result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "下发重置校准参数请求失败: DeviceId={DeviceId}", deviceId);
            return ApiResponse<AnShengEnergyResultDto>.Error($"重置校准参数请求下发失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 下发 <c>autoCal</c>，按已知负载功率自动校准。仅开关类放行（验收 #6）。
    /// </summary>
    /// <param name="deviceId">设备主键。</param>
    /// <param name="request">自动校准请求（已知负载功率 W）。</param>
    /// <returns>下发受理结果。</returns>
    [HttpPost("{deviceId:long}/energy/cal-params/auto")]
    [PermissionAuthorize(Permissions.SEND_DEVICE_COMMANDS)]
    public async Task<ActionResult<ApiResponse<AnShengEnergyResultDto>>> AutoCal(
        long deviceId, [FromBody] AnShengAutoCalRequest? request)
    {
        var ct = HttpContext.RequestAborted;
        try
        {
            if (request == null)
            {
                return ApiResponse<AnShengEnergyResultDto>.BadRequest("请求体不能为空");
            }

            var result = await _energyService.AutoCalAsync(deviceId, request.Power, ct);
            return result.Accepted
                ? ApiResponse<AnShengEnergyResultDto>.Success(result, "自动校准请求已下发")
                : ApiResponse<AnShengEnergyResultDto>.BadRequest(
                    result.ErrorMessage ?? "自动校准请求下发失败", result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "下发自动校准请求失败: DeviceId={DeviceId}", deviceId);
            return ApiResponse<AnShengEnergyResultDto>.Error($"自动校准请求下发失败：{ex.Message}");
        }
    }

    // ─────────────────────────────────────────────
    // 私有工具
    // ─────────────────────────────────────────────

    /// <summary>
    /// 把请求里的 <c>RL</c> 与 <c>CalParams</c> 字典合并成单一校准参数字典。
    /// 字典优先；字典缺失 <c>RL</c> 而 <c>RL</c> 有值时自动补入，避免「填了 RL 却没发出去」。
    /// </summary>
    /// <param name="request">设置校准参数请求。</param>
    /// <returns>合并后的校准参数字典（可能为空）。</returns>
    private static IReadOnlyDictionary<string, double> MergeCalParams(AnShengSetCalParamsRequest request)
    {
        // 用有序字典保证出网参数顺序稳定、便于排查。
        var merged = new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (var kv in request.CalParams)
        {
            merged[kv.Key] = kv.Value;
        }

        if (request.RL.HasValue && !merged.ContainsKey("RL"))
        {
            merged["RL"] = request.RL.Value;
        }

        return merged;
    }
}
