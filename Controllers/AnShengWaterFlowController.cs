// T12：安圣水流量 API（仅后端，不含前端面板）。
//
// 【路由不冲突】与 AnShengEnergyController 等同处 `api/v1/ansheng` 模板层级，本控制器的字面量段以
//   `water/flow/` 打头（water/flow/info、water/flow/reset、water/flow/config、water/flow/config/reset），
//   与既有 `energy/*` / `action` / `delay-tasks` / `time-tasks` 等互不重叠，不会触发 AmbiguousMatchException。
//
// 【拒绝信封（铁律②）】业务失败一律 ApiResponse<T>.BadRequest(message) / .Error(message)，HTTP 恒 200。
//   <b>禁止</b>裸 BadRequest() / StatusCode(400)。命令端点成功/失败均走 AnShengEnergyResultDto。
//
// 【下发唯一入口（铁律③）】所有写端点调 IAnShengWaterFlowService，其内部只走
//   IAnShengCommandService.SendCommandAsync；本控制器不碰 MQTT / Builder / Guard。
//
// 【设备权威、平台只读】实时 / 配置数据只在设备应答真回来后才落库
//   （Router 钩子 → Apply*ReadbackAsync），本控制器 GET 端点只做查询转发。

using System;
using System.Threading;
using System.Threading.Tasks;
using IoTPlatform.Configuration;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Filters;
using IoTPlatform.Helpers;
using IoTPlatform.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace IoTPlatform.Controllers;

/// <summary>
/// 安圣二开设备「水流量」控制器（T12）。
///
/// 端点一览（均挂载于 <c>api/v1/ansheng/{deviceId}</c> 之下）：
/// <list type="bullet">
///   <item><c>POST {deviceId}/water/flow/info</c> —— 拉取水流量实时读数（应答归一化入 DeviceDataRecord）；</item>
///   <item><c>GET  {deviceId}/water/flow/info</c> —— 读平台水流量最新读数（WaterFlow / WaterTotal / WaterPulses）；</item>
///   <item><c>POST {deviceId}/water/flow/reset</c> —— 重置设备累计流量；</item>
///   <item><c>GET  {deviceId}/water/flow/config</c> —— 读平台水流量配置镜像（ansheng_wf_config）；</item>
///   <item><c>POST {deviceId}/water/flow/config</c> —— 设置水流量配置（k/offset/minFlow/maxFlow）；</item>
///   <item><c>POST {deviceId}/water/flow/config/reset</c> —— 重置水流量配置。</item>
/// </list>
/// </summary>
[ApiController]
[Route("api/v1/ansheng")]
[PermissionAuthorize(Permissions.VIEW_DEVICES)]
public class AnShengWaterFlowController : ControllerBase
{
    private readonly IAnShengWaterFlowService _waterFlowService;
    private readonly ILogger<AnShengWaterFlowController> _logger;

    /// <summary>构造控制器。</summary>
    public AnShengWaterFlowController(
        IAnShengWaterFlowService waterFlowService,
        ILogger<AnShengWaterFlowController> logger)
    {
        _waterFlowService = waterFlowService;
        _logger = logger;
    }

    // ─────────────────────────────────────────────
    // 实时流量
    // ─────────────────────────────────────────────

    /// <summary>
    /// 下发 <c>getFlowInfo</c>，拉取水流量实时读数。
    /// 应答经 Router 钩子 → <see cref="IAnShengWaterFlowService.ApplyFlowInfoReadbackAsync"/> 归一化入
    /// <c>DeviceDataRecord</c>。本端点只回下发受理情况，真值需轮询数据曲线。
    /// </summary>
    /// <param name="deviceId">设备主键。</param>
    /// <returns>下发受理结果。</returns>
    [HttpPost("{deviceId:long}/water/flow/info")]
    [PermissionAuthorize(Permissions.SEND_DEVICE_COMMANDS)]
    public async Task<ActionResult<ApiResponse<AnShengEnergyResultDto>>> RequestFlowInfo(long deviceId)
    {
        var ct = HttpContext.RequestAborted;
        try
        {
            var result = await _waterFlowService.RequestFlowInfoAsync(deviceId, ct);
            return result.Accepted
                ? ApiResponse<AnShengEnergyResultDto>.Success(result, "水流量实时读数请求已下发")
                : ApiResponse<AnShengEnergyResultDto>.BadRequest(
                    result.ErrorMessage ?? "实时读数请求下发失败", result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "下发水流量实时读数请求失败: DeviceId={DeviceId}", deviceId);
            return ApiResponse<AnShengEnergyResultDto>.Error($"实时读数请求下发失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 读取平台水流量最新读数（该设备最新 <c>DeviceDataRecord</c> 的 WaterFlow / WaterTotal / WaterPulses）。
    /// 设备权威镜像，平台只累积保留。
    /// </summary>
    /// <param name="deviceId">设备主键。</param>
    /// <returns>流量信息 DTO（无水流量记录时为 null）。</returns>
    [HttpGet("{deviceId:long}/water/flow/info")]
    public async Task<ActionResult<ApiResponse<AnShengWaterFlowInfoDto?>>> GetLatestFlowInfo(long deviceId)
    {
        var ct = HttpContext.RequestAborted;
        try
        {
            var dto = await _waterFlowService.GetLatestFlowInfoAsync(deviceId, ct);
            return ApiResponse<AnShengWaterFlowInfoDto?>.Success(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询水流量实时读数失败: DeviceId={DeviceId}", deviceId);
            return ApiResponse<AnShengWaterFlowInfoDto?>.Error($"查询水流量实时读数失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 下发 <c>resetTotalFlow</c>，重置设备累计流量。平台只镜像设备读数，不影响设备侧归零的语义。
    /// </summary>
    /// <param name="deviceId">设备主键。</param>
    /// <returns>下发受理结果。</returns>
    [HttpPost("{deviceId:long}/water/flow/reset")]
    [PermissionAuthorize(Permissions.SEND_DEVICE_COMMANDS)]
    public async Task<ActionResult<ApiResponse<AnShengEnergyResultDto>>> ResetTotalFlow(long deviceId)
    {
        var ct = HttpContext.RequestAborted;
        try
        {
            var result = await _waterFlowService.ResetTotalFlowAsync(deviceId, ct);
            return result.Accepted
                ? ApiResponse<AnShengEnergyResultDto>.Success(result, "重置累计流量请求已下发")
                : ApiResponse<AnShengEnergyResultDto>.BadRequest(
                    result.ErrorMessage ?? "重置累计流量请求下发失败", result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "下发重置累计流量请求失败: DeviceId={DeviceId}", deviceId);
            return ApiResponse<AnShengEnergyResultDto>.Error($"重置累计流量请求下发失败：{ex.Message}");
        }
    }

    // ─────────────────────────────────────────────
    // 流量配置
    // ─────────────────────────────────────────────

    /// <summary>
    /// 读取平台水流量配置镜像（<c>ansheng_wf_config</c>，设备权威镜像，平台只累积保留）。
    /// </summary>
    /// <param name="deviceId">设备主键。</param>
    /// <returns>配置 DTO（无镜像时为 null）。</returns>
    [HttpGet("{deviceId:long}/water/flow/config")]
    public async Task<ActionResult<ApiResponse<AnShengWaterFlowConfigDto?>>> GetFlowConfigMirror(long deviceId)
    {
        var ct = HttpContext.RequestAborted;
        try
        {
            var dto = await _waterFlowService.GetFlowConfigMirrorAsync(deviceId, ct);
            return ApiResponse<AnShengWaterFlowConfigDto?>.Success(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询水流量配置失败: DeviceId={DeviceId}", deviceId);
            return ApiResponse<AnShengWaterFlowConfigDto?>.Error($"查询水流量配置失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 下发 <c>setFlowConfig</c>，设置水流量配置（k / offset / minFlow / maxFlow，仅非 null 的才下发）。
    /// 应答经 Router 钩子 → <see cref="IAnShengWaterFlowService.ApplyFlowConfigReadbackAsync"/> UPSERT 进镜像表。
    /// </summary>
    /// <param name="deviceId">设备主键。</param>
    /// <param name="request">配置请求（k / offset / minFlow / maxFlow 均可选）。</param>
    /// <returns>下发受理结果。</returns>
    [HttpPost("{deviceId:long}/water/flow/config")]
    [PermissionAuthorize(Permissions.SEND_DEVICE_COMMANDS)]
    public async Task<ActionResult<ApiResponse<AnShengEnergyResultDto>>> SetFlowConfig(
        long deviceId, [FromBody] AnShengWaterFlowConfigRequest? request)
    {
        var ct = HttpContext.RequestAborted;
        try
        {
            if (request == null)
            {
                return ApiResponse<AnShengEnergyResultDto>.BadRequest("请求体不能为空");
            }

            var result = await _waterFlowService.SetFlowConfigAsync(deviceId, request, ct);
            return result.Accepted
                ? ApiResponse<AnShengEnergyResultDto>.Success(result, "设置流量配置请求已下发")
                : ApiResponse<AnShengEnergyResultDto>.BadRequest(
                    result.ErrorMessage ?? "设置流量配置请求下发失败", result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "下发设置流量配置请求失败: DeviceId={DeviceId}", deviceId);
            return ApiResponse<AnShengEnergyResultDto>.Error($"设置流量配置请求下发失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 下发 <c>resetFlowConfig</c>，重置水流量配置。
    /// </summary>
    /// <param name="deviceId">设备主键。</param>
    /// <returns>下发受理结果。</returns>
    [HttpPost("{deviceId:long}/water/flow/config/reset")]
    [PermissionAuthorize(Permissions.SEND_DEVICE_COMMANDS)]
    public async Task<ActionResult<ApiResponse<AnShengEnergyResultDto>>> ResetFlowConfig(long deviceId)
    {
        var ct = HttpContext.RequestAborted;
        try
        {
            var result = await _waterFlowService.ResetFlowConfigAsync(deviceId, ct);
            return result.Accepted
                ? ApiResponse<AnShengEnergyResultDto>.Success(result, "重置流量配置请求已下发")
                : ApiResponse<AnShengEnergyResultDto>.BadRequest(
                    result.ErrorMessage ?? "重置流量配置请求下发失败", result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "下发重置流量配置请求失败: DeviceId={DeviceId}", deviceId);
            return ApiResponse<AnShengEnergyResultDto>.Error($"重置流量配置请求下发失败：{ex.Message}");
        }
    }
}
