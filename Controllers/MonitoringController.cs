using IoTPlatform.Configuration;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Filters;
using IoTPlatform.Helpers;
using IoTPlatform.Services;
using IoTPlatform.Services.Guards;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
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
    private readonly ICustomerScopeService? _customerScope;

    /// <summary>兼容构造：存量单测零改动。</summary>
    public MonitoringController(IMonitoringService monitoringService)
        : this(monitoringService, null)
    {
    }

    /// <summary>生产构造：显式注入范围服务。</summary>
    [ActivatorUtilitiesConstructor]
    public MonitoringController(IMonitoringService monitoringService, ICustomerScopeService? customerScope)
    {
        _monitoringService = monitoringService;
        _customerScope = customerScope;
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
            var listScope = await this.ResolveListScopeAsync(_customerScope);

            var result = await _monitoringService.GetMonitoringDataAsync(
                page, pageSize, appCode, allowedAreaIds, listScope.CustomerIds, listScope.AppCodes);
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
            var listScope = await this.ResolveListScopeAsync(_customerScope);

            var result = await _monitoringService.GetAirQualityDataAsync(
                areaId, startTime, endTime, appCode, listScope.CustomerIds, listScope.AppCodes);
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
            var listScope = await this.ResolveListScopeAsync(_customerScope);

            var result = await _monitoringService.GetEnvironmentDataAsync(
                deviceId, startTime, endTime, appCode, listScope.CustomerIds, listScope.AppCodes);
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
            var listScope = await this.ResolveListScopeAsync(_customerScope);

            var result = await _monitoringService.GetMonitoringSummaryAsync(
                appCode, listScope.CustomerIds, listScope.AppCodes);
            return ApiResponse<MonitoringSummaryDto>.Success(result);
        }
        catch (Exception ex)
        {
            var response = ApiResponse.Error(ex.Message);
            return Ok(response);
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
