using IoTPlatform.DTOs.Responses;
using IoTPlatform.Filters;
using IoTPlatform.Helpers;
using IoTPlatform.Services;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace IoTPlatform.Controllers;

/// <summary>
/// 监控数据控制器
/// </summary>
[ApiController]
[Route("api/v1/monitoring")]
[PermissionAuthorize(Permissions.VIEW_MONITORING)]
public class MonitoringController : ControllerBase
{
    private readonly IMonitoringService _monitoringService;

    public MonitoringController(IMonitoringService monitoringService)
    {
        _monitoringService = monitoringService;
    }

    /// <summary>
    /// 获取监控数据列表
    /// </summary>
    [HttpGet("data")]
    public async Task<ActionResult<ApiResponse<PagedResponse<MonitoringDataDto>>>> GetMonitoringData(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var allowedAreaIds = GetAllowedAreaIds();

            var result = await _monitoringService.GetMonitoringDataAsync(page, pageSize, appCode, allowedAreaIds);
            return ApiResponse<PagedResponse<MonitoringDataDto>>.Success(result);
        }
        catch (Exception ex)
        {
            return ApiResponse<PagedResponse<MonitoringDataDto>>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 获取空气质量数据
    /// </summary>
    [HttpGet("air-quality/{areaId}")]
    public async Task<ActionResult<ApiResponse<List<AirQualityDataDto>>>> GetAirQualityData(
        long areaId,
        [FromQuery] DateTime? startTime = null,
        [FromQuery] DateTime? endTime = null)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;

            var result = await _monitoringService.GetAirQualityDataAsync(areaId, startTime, endTime, appCode);
            return ApiResponse<List<AirQualityDataDto>>.Success(result);
        }
        catch (Exception ex)
        {
            return ApiResponse<List<AirQualityDataDto>>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 获取环境监测数据
    /// </summary>
    [HttpGet("environment/{deviceId}")]
    public async Task<ActionResult<ApiResponse<List<EnvironmentDataDto>>>> GetEnvironmentData(
        long deviceId,
        [FromQuery] DateTime? startTime = null,
        [FromQuery] DateTime? endTime = null)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;

            var result = await _monitoringService.GetEnvironmentDataAsync(deviceId, startTime, endTime, appCode);
            return ApiResponse<List<EnvironmentDataDto>>.Success(result);
        }
        catch (Exception ex)
        {
            return ApiResponse<List<EnvironmentDataDto>>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 获取监控汇总
    /// </summary>
    [HttpGet("summary")]
    public async Task<ActionResult<ApiResponse<MonitoringSummaryDto>>> GetMonitoringSummary()
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;

            var result = await _monitoringService.GetMonitoringSummaryAsync(appCode);
            return ApiResponse<MonitoringSummaryDto>.Success(result);
        }
        catch (Exception ex)
        {
            return ApiResponse<MonitoringSummaryDto>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 获取用户允许的区域ID列表
    /// </summary>
    private List<long>? GetAllowedAreaIds()
    {
        var allowedAreaIdsClaim = User.FindFirst("AllowedAreaIds")?.Value;
        if (string.IsNullOrEmpty(allowedAreaIdsClaim))
            return null;

        return allowedAreaIdsClaim.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(id => long.Parse(id.Trim()))
            .ToList();
    }
}
