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
    /// 租户过滤码：普通租户取自身 AppCode；超管（无 AppCode / system）返回 null 表示看全部。
    /// SwitchCustomer 后超管 token 会带目标租户 AppCode，此时按该租户过滤（符合切换语义）。
    /// </summary>
    private string? TenantAppCode
    {
        get
        {
            var c = User.FindFirst("AppCode")?.Value;
            return string.IsNullOrEmpty(c) || c == "system" ? null : c;
        }
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
            var appCode = TenantAppCode;
            // 【租户隔离】告警是设备级数据，租户范围已由 AppCode 精确限定。
            // 不再叠加 AllowedAreaIds 区域过滤：通用 ApplyFilters 的语义是
            // 「AreaId.HasValue && allowedAreaIds.Contains(AreaId.Value)」，
            // 会把 AreaId=null 的真实告警（如设备级阈值告警）全部排除，导致租户看不到自己的告警。
            List<long>? allowedAreaIds = null;

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
            var appCode = TenantAppCode;
            // 同列表：告警按 AppCode 隔离，不叠加区域过滤（避免漏掉 AreaId=null 的设备级告警）。
            List<long>? allowedAreaIds = null;

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
            // 租户传的 appCode 会被 JWT 的 TenantAppCode 覆盖（防越权）：
            // 普通租户 TenantAppCode 非空 → 用 JWT 值；超管 TenantAppCode=null → 允许 request.AppCode（代租户创建）。
            var effectiveAppCode = TenantAppCode ?? request.AppCode;
            var result = await _alertService.CreateAlertAsync(request, effectiveAppCode);
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
            var appCode = TenantAppCode;

            var result = await _alertService.GetAlertSummaryAsync(startTime, endTime, appCode);
            return ApiResponse<AlertSummaryDto>.Success(result);
        }
        catch (Exception ex)
        {
            var response = ApiResponse<AlertSummaryDto>.Error(ex.Message);
            return Ok(response);
        }
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
