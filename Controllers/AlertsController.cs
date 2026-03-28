using IoTPlatform.Configuration;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Filters;
using IoTPlatform.Helpers;
using IoTPlatform.Services;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace IoTPlatform.Controllers;

/// <summary>
/// 告警管理控制器
/// </summary>
[ApiController]
[Route("api/v1/alerts")]
[PermissionAuthorize(Permissions.VIEW_ALERT_CENTER)]
public class AlertsController : ControllerBase
{
    private readonly IAlertService _alertService;

    public AlertsController(IAlertService alertService)
    {
        _alertService = alertService;
    }

    /// <summary>
    /// 获取告警列表
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResponse<AlertDto>>>> GetAlerts(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        [FromQuery] string? level = null,
        [FromQuery] string? alertType = null)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var allowedAreaIds = GetAllowedAreaIds();

            var result = await _alertService.GetAlertsAsync(page, pageSize, status, level, alertType, appCode, allowedAreaIds);
            var response = ApiResponse<PagedResponse<AlertDto>>.Success(result);
            return Ok(response);
        }
        catch (Exception ex)
        {
            var response = ApiResponse<PagedResponse<AlertDto>>.Error(ex.Message);
            return Ok(response);
        }
    }

    /// <summary>
    /// 获取告警详情
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<AlertDto>>> GetAlert(long id)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var allowedAreaIds = GetAllowedAreaIds();

            var result = await _alertService.GetAlertAsync(id, appCode, allowedAreaIds);
            if (result == null)
            {
                var response = ApiResponse<AlertDto>.NotFound("告警不存在");
                return Ok(response);
            }

            var response2 = ApiResponse<AlertDto>.Success(result);
            return Ok(response2);
        }
        catch (Exception ex)
        {
            var response = ApiResponse<AlertDto>.Error(ex.Message);
            return Ok(response);
        }
    }

    /// <summary>
    /// 创建告警
    /// </summary>
    [HttpPost]
    [PermissionAuthorize(Permissions.CREATE_ALERTS)]
    public async Task<ActionResult<ApiResponse<AlertDto>>> CreateAlert([FromBody] CreateAlertRequest request)
    {
        try
        {
            var result = await _alertService.CreateAlertAsync(request);
            var response = ApiResponse<AlertDto>.Success(result, "告警创建成功");
            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            var response = ApiResponse<AlertDto>.BadRequest(ex.Message);
            return Ok(response);
        }
        catch (Exception ex)
        {
            var response = ApiResponse<AlertDto>.Error(ex.Message);
            return Ok(response);
        }
    }

    /// <summary>
    /// 处理告警
    /// </summary>
    [HttpPost("{id}/process")]
    [PermissionAuthorize(Permissions.UPDATE_ALERTS)]
    public async Task<ActionResult<ApiResponse<AlertDto>>> ProcessAlert(long id, [FromBody] ProcessAlertRequest request)
    {
        try
        {
            var result = await _alertService.ProcessAlertAsync(id, request);
            var response = ApiResponse<AlertDto>.Success(result, "告警处理成功");
            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            var response = ApiResponse<AlertDto>.BadRequest(ex.Message);
            return Ok(response);
        }
        catch (Exception ex)
        {
            var response = ApiResponse<AlertDto>.Error(ex.Message);
            return Ok(response);
        }
    }

    /// <summary>
    /// 分配告警
    /// </summary>
    [HttpPost("{id}/assign")]
    [PermissionAuthorize(Permissions.UPDATE_ALERTS)]
    public async Task<ActionResult<ApiResponse<AlertDto>>> AssignAlert(long id, [FromBody] AssignAlertRequest request)
    {
        try
        {
            var result = await _alertService.AssignAlertAsync(id, request.Assignee);
            var response = ApiResponse<AlertDto>.Success(result, "告警分配成功");
            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            var response = ApiResponse<AlertDto>.BadRequest(ex.Message);
            return Ok(response);
        }
        catch (Exception ex)
        {
            var response = ApiResponse<AlertDto>.Error(ex.Message);
            return Ok(response);
        }
    }

    /// <summary>
    /// 解决告警
    /// </summary>
    [HttpPost("{id}/resolve")]
    [PermissionAuthorize(Permissions.UPDATE_ALERTS)]
    public async Task<ActionResult<ApiResponse<AlertDto>>> ResolveAlert(long id, [FromBody] ResolveAlertRequest request)
    {
        try
        {
            var result = await _alertService.ResolveAlertAsync(id, request.Remark);
            var response = ApiResponse<AlertDto>.Success(result, "告警已解决");
            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            var response = ApiResponse<AlertDto>.BadRequest(ex.Message);
            return Ok(response);
        }
        catch (Exception ex)
        {
            var response = ApiResponse<AlertDto>.Error(ex.Message);
            return Ok(response);
        }
    }

    /// <summary>
    /// 忽略告警
    /// </summary>
    [HttpPost("{id}/ignore")]
    [PermissionAuthorize(Permissions.UPDATE_ALERTS)]
    public async Task<ActionResult<ApiResponse<AlertDto>>> IgnoreAlert(long id, [FromBody] IgnoreAlertRequest request)
    {
        try
        {
            var result = await _alertService.IgnoreAlertAsync(id, request.Remark);
            var response = ApiResponse<AlertDto>.Success(result, "告警已忽略");
            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            var response = ApiResponse<AlertDto>.BadRequest(ex.Message);
            return Ok(response);
        }
        catch (Exception ex)
        {
            var response = ApiResponse<AlertDto>.Error(ex.Message);
            return Ok(response);
        }
    }

    /// <summary>
    /// 获取告警处理日志
    /// </summary>
    [HttpGet("{id}/logs")]
    public async Task<ActionResult<ApiResponse<List<AlertProcessLogDto>>>> GetAlertLogs(long id)
    {
        try
        {
            var result = await _alertService.GetAlertLogsAsync(id);
            var response = ApiResponse<List<AlertProcessLogDto>>.Success(result);
            return Ok(response);
        }
        catch (Exception ex)
        {
            var response = ApiResponse<List<AlertProcessLogDto>>.Error(ex.Message);
            return Ok(response);
        }
    }

    /// <summary>
    /// 获取告警汇总
    /// </summary>
    [HttpGet("summary")]
    public async Task<ActionResult<ApiResponse<AlertSummaryDto>>> GetAlertSummary(
        [FromQuery] DateTime? startTime = null,
        [FromQuery] DateTime? endTime = null)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;

            var result = await _alertService.GetAlertSummaryAsync(startTime, endTime, appCode);
            return ApiResponse<AlertSummaryDto>.Success(result);
        }
        catch (Exception ex)
        {
            var response = ApiResponse<AlertSummaryDto>.Error(ex.Message);
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

/// <summary>
/// 分配告警请求
/// </summary>
public class AssignAlertRequest
{
    public string Assignee { get; set; } = string.Empty;
}

/// <summary>
/// 解决告警请求
/// </summary>
public class ResolveAlertRequest
{
    public string? Remark { get; set; }
}

/// <summary>
/// 忽略告警请求
/// </summary>
public class IgnoreAlertRequest
{
    public string? Remark { get; set; }
}
