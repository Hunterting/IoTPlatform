using IoTPlatform.Configuration;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Filters;
using IoTPlatform.Helpers;
using IoTPlatform.Services;
using IoTPlatform.Services.Guards;
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
    private readonly ICustomerScopeService _customerScope;

    public AlertsController(IAlertService alertService, ICustomerScopeService customerScope)
    {
        _alertService = alertService;
        _customerScope = customerScope ?? throw new ArgumentNullException(nameof(customerScope));
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
            var listScope = await this.ResolveListScopeAsync(_customerScope);
            // 【租户隔离】告警是设备级数据，租户范围已由 AppCode 精确限定。
            // 不再叠加 AllowedAreaIds 区域过滤：通用 ApplyFilters 的语义是
            // 「AreaId.HasValue && allowedAreaIds.Contains(AreaId.Value)」，
            // 会把 AreaId=null 的真实告警（如设备级阈值告警）全部排除，导致租户看不到自己的告警。
            List<long>? allowedAreaIds = null;

            var result = await _alertService.GetAlertsAsync(page, pageSize, status, level, alertType, appCode, allowedAreaIds, listScope.CustomerIds, listScope.AppCodes);
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
            // B4/M6：先点查权威归属并按 scope 三分支判定；越权 HTTP 200 + Code=403。
            if (!await this.CanReadDetailAsync<IoTPlatform.Models.AlertRecord>(id, false))
            {
                return Ok(ApiResponse<AlertDto>.Fail(403, "无权访问该记录"));
            }

        try
        {
            var appCode = TenantAppCode;
            var listScope = await this.ResolveListScopeAsync(_customerScope);
            // 同列表：告警按 AppCode 隔离，不叠加区域过滤（避免漏掉 AreaId=null 的设备级告警）。
            List<long>? allowedAreaIds = null;

            var result = await _alertService.GetAlertAsync(id, appCode, allowedAreaIds, listScope.CustomerIds, listScope.AppCodes);
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
            // 【M1】admin 必须指定授权集合内的目标客户；校验在任何写库之前完成。
            if (Roles.ADMIN == User.FindFirst(ClaimTypes.Role)?.Value)
            {
                if (!request.CustomerId.HasValue)
                {
                    return Ok(ApiResponse<AlertDto>.BadRequest("租户管理员必须在授权客户下创建告警。"));
                }
                if (!await _customerScope.CanAccessCustomerAsync(User, request.CustomerId.Value))
                {
                    return Ok(ApiResponse<AlertDto>.Forbidden("无权在该客户下创建告警"));
                }

                var resolvedAppCode = await _customerScope.ResolveAppCodeByCustomerIdAsync(request.CustomerId.Value);
                if (string.IsNullOrWhiteSpace(resolvedAppCode))
                {
                    return Ok(ApiResponse<AlertDto>.BadRequest("目标客户缺少 AppCode"));
                }

                var verifiedResult = await _alertService.CreateAlertAsync(
                    request, resolvedAppCode, customerScopeVerified: true);
                return Ok(ApiResponse<AlertDto>.Success(verifiedResult, "告警创建成功"));
            }

            // 超管 / 下级：原路径逐字不改。
            // 租户传的 appCode 会被 JWT 的 TenantAppCode 覆盖（防越权）：
            // 普通租户 TenantAppCode 非空 → 用 JWT 值；超管 TenantAppCode=null → 允许 request.AppCode（代租户创建）。
            var effectiveAppCode = TenantAppCode ?? request.AppCode;
            var result = await _alertService.CreateAlertAsync(request, effectiveAppCode);
            var response = ApiResponse<AlertDto>.Success(result, "告警创建成功");
            return Ok(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Ok(ApiResponse<AlertDto>.Forbidden(ex.Message));
        }
        catch (ForbiddenAccessException ex)
        {
            return Ok(ApiResponse<AlertDto>.Forbidden(ex.Message));
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
            var result = await _alertService.ProcessAlertAsync(id, request, User);
            var response = ApiResponse<AlertDto>.Success(result, "告警处理成功");
            return Ok(response);
        }
        catch (ForbiddenAccessException ex)
        {
            return Ok(ApiResponse<AlertDto>.Forbidden(ex.Message));
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
            var result = await _alertService.AssignAlertAsync(id, request.Assignee, User);
            var response = ApiResponse<AlertDto>.Success(result, "告警分配成功");
            return Ok(response);
        }
        catch (ForbiddenAccessException ex)
        {
            return Ok(ApiResponse<AlertDto>.Forbidden(ex.Message));
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
            var result = await _alertService.ResolveAlertAsync(id, request.Remark, User);
            var response = ApiResponse<AlertDto>.Success(result, "告警已解决");
            return Ok(response);
        }
        catch (ForbiddenAccessException ex)
        {
            return Ok(ApiResponse<AlertDto>.Forbidden(ex.Message));
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
            var result = await _alertService.IgnoreAlertAsync(id, request.Remark, User);
            var response = ApiResponse<AlertDto>.Success(result, "告警已忽略");
            return Ok(response);
        }
        catch (ForbiddenAccessException ex)
        {
            return Ok(ApiResponse<AlertDto>.Forbidden(ex.Message));
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
            // B4/M6：先点查权威归属并按 scope 三分支判定；越权 HTTP 200 + Code=403。
            if (!await this.CanReadDetailAsync<IoTPlatform.Models.AlertRecord>(id, false))
            {
                return Ok(ApiResponse<List<AlertProcessLogDto>>.Fail(403, "无权访问该记录"));
            }

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
            var listScope = await this.ResolveListScopeAsync(_customerScope);

            var result = await _alertService.GetAlertSummaryAsync(startTime, endTime, appCode, listScope.CustomerIds, listScope.AppCodes);
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
