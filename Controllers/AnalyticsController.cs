using IoTPlatform.Configuration;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Filters;
using IoTPlatform.Helpers;
using IoTPlatform.Services;
using Microsoft.AspNetCore.Mvc;

namespace IoTPlatform.Controllers;

/// <summary>
/// 统计分析控制器 —— 智能分析页（水 / 电 / 气 用能分析）真实数据接口。
/// 数据来自 DeviceDataRecord 设备真实上报；口径为采样平均值，不伪造累计电量。
/// </summary>
[ApiController]
[Route("api/v1/analytics")]
[PermissionAuthorize(Permissions.VIEW_ANALYTICS)]
public class AnalyticsController : ControllerBase
{
    private readonly IAnalyticsService _analyticsService;

    public AnalyticsController(IAnalyticsService analyticsService)
    {
        _analyticsService = analyticsService;
    }

    /// <summary>
    /// 用能分析：一次返回卡片汇总 + 日历(日)/月柱(月)/年线(年)三组聚合。
    /// </summary>
    /// <param name="energyType">electric / water / gas（默认 electric）</param>
    /// <param name="year">选中年份（默认当前 UTC 年）</param>
    /// <param name="month">选中月份 1-12（默认当前 UTC 月）</param>
    /// <param name="areaId">可选区域过滤</param>
    [HttpGet("energy")]
    public async Task<ActionResult<ApiResponse<EnergyAnalysisDto>>> GetEnergyAnalysis(
        [FromQuery] string energyType = "electric",
        [FromQuery] int? year = null,
        [FromQuery] int? month = null,
        [FromQuery] long? areaId = null)
    {
        try
        {
            var now = DateTime.UtcNow;
            var y = year ?? now.Year;
            var m = month ?? now.Month;
            if (m < 1 || m > 12) m = now.Month;
            if (y < 2000 || y > 2100) y = now.Year;

            var appCode = User.FindFirst("AppCode")?.Value;
            var result = await _analyticsService.GetEnergyAnalysisAsync(energyType, y, m, areaId, appCode);
            return ApiResponse<EnergyAnalysisDto>.Success(result);
        }
        catch (Exception ex)
        {
            return ApiResponse<EnergyAnalysisDto>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 汇总统计（设备/告警/工单）
    /// </summary>
    [HttpGet("summary")]
    public async Task<ActionResult<ApiResponse<AnalyticsSummaryDto>>> GetSummary()
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var result = await _analyticsService.GetSummaryAsync(appCode);
            return ApiResponse<AnalyticsSummaryDto>.Success(result);
        }
        catch (Exception ex)
        {
            return ApiResponse<AnalyticsSummaryDto>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 能耗统计（指定时间区间）
    /// </summary>
    [HttpGet("consumption")]
    public async Task<ActionResult<ApiResponse<EnergyConsumptionDto>>> GetConsumption(
        [FromQuery] DateTime? startTime = null,
        [FromQuery] DateTime? endTime = null)
    {
        try
        {
            var now = DateTime.UtcNow;
            var start = startTime ?? now.AddDays(-30);
            var end = endTime ?? now;
            var appCode = User.FindFirst("AppCode")?.Value;
            var result = await _analyticsService.GetEnergyConsumptionAsync(start, end, appCode);
            return ApiResponse<EnergyConsumptionDto>.Success(result);
        }
        catch (Exception ex)
        {
            return ApiResponse<EnergyConsumptionDto>.Error(ex.Message);
        }
    }
}
