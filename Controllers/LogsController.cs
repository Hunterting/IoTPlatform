using IoTPlatform.Configuration;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Filters;
using IoTPlatform.Helpers;
using IoTPlatform.Services;
using IoTPlatform.Services.Guards;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;

namespace IoTPlatform.Controllers;

/// <summary>
/// 日志管理控制器
/// </summary>
[ApiController]
[Route("api/v1/logs")]
[PermissionAuthorize(Permissions.VIEW_LOGS)]
public class LogsController : ControllerBase
{
    private readonly ILogService _logService;
    private readonly ICustomerScopeService? _customerScope;
    private readonly IDetailScopeGuard? _detailGuard;

    /// <summary>兼容构造：存量单测零改动。</summary>
    public LogsController(ILogService logService)
        : this(logService, null, null)
    {
    }

    /// <summary>生产构造：显式注入范围服务与详情归属守卫。</summary>
    [ActivatorUtilitiesConstructor]
    public LogsController(
        ILogService logService,
        ICustomerScopeService? customerScope,
        IDetailScopeGuard? detailGuard)
    {
        _logService = logService;
        _customerScope = customerScope;
        _detailGuard = detailGuard;
    }

    /// <summary>
    /// 获取操作日志列表
    /// </summary>
    [HttpGet("operation")]
    public async Task<ActionResult<ApiResponse<PagedResponse<OperationLogDto>>>> GetOperationLogs(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? module = null,
        [FromQuery] string? action = null,
        [FromQuery] long? userId = null,
        [FromQuery] DateTime? startTime = null,
        [FromQuery] DateTime? endTime = null)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var role = User.FindFirst(ClaimTypes.Role)?.Value;
            var listScope = await this.ResolveListScopeAsync(_customerScope);

            var result = await _logService.GetOperationLogsAsync(
                page, pageSize, module, action, userId, startTime, endTime, appCode, role,
                listScope.CustomerIds, listScope.AppCodes);
            return ApiResponse<PagedResponse<OperationLogDto>>.Success(result);
        }
        catch (Exception ex)
        {
            return ApiResponse<PagedResponse<OperationLogDto>>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 获取操作日志详情
    /// </summary>
    [HttpGet("operation/{id}")]
    public async Task<ActionResult<ApiResponse<OperationLogDto>>> GetOperationLog(long id)
    {
        try
        {
            if (_detailGuard != null && !await _detailGuard.CanReadAsync<IoTPlatform.Models.OperationLog>(User, id))
            {
                return Ok(ApiResponse<OperationLogDto>.Fail(403, "无权访问该操作日志"));
            }

            var appCode = User.FindFirst("AppCode")?.Value;
            var role = User.FindFirst(ClaimTypes.Role)?.Value;

            var result = await _logService.GetOperationLogAsync(id, appCode, role);
            if (result == null)
            {
                var response = ApiResponse.NotFound("操作日志不存在");
            return Ok(response);
            }

            return ApiResponse<OperationLogDto>.Success(result);
        }
        catch (Exception ex)
        {
            var response = ApiResponse.Error(ex.Message);
            return Ok(response);
        }
    }

    /// <summary>
    /// 获取登录日志列表
    /// </summary>
    [HttpGet("login")]
    public async Task<ActionResult<ApiResponse<PagedResponse<LoginLogDto>>>> GetLoginLogs(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] long? userId = null,
        [FromQuery] string? status = null,
        [FromQuery] DateTime? startTime = null,
        [FromQuery] DateTime? endTime = null)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var role = User.FindFirst(ClaimTypes.Role)?.Value;
            var listScope = await this.ResolveListScopeAsync(_customerScope);

            var result = await _logService.GetLoginLogsAsync(
                page, pageSize, userId, status, startTime, endTime, appCode, role,
                listScope.CustomerIds, listScope.AppCodes);
            return ApiResponse<PagedResponse<LoginLogDto>>.Success(result);
        }
        catch (Exception ex)
        {
            return ApiResponse<PagedResponse<LoginLogDto>>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 获取登录日志详情
    /// </summary>
    [HttpGet("login/{id}")]
    public async Task<ActionResult<ApiResponse<LoginLogDto>>> GetLoginLog(long id)
    {
        try
        {
            if (_detailGuard != null && !await _detailGuard.CanReadAsync<IoTPlatform.Models.LoginLog>(User, id))
            {
                return Ok(ApiResponse<LoginLogDto>.Fail(403, "无权访问该登录日志"));
            }

            var appCode = User.FindFirst("AppCode")?.Value;
            var role = User.FindFirst(ClaimTypes.Role)?.Value;

            var result = await _logService.GetLoginLogAsync(id, appCode, role);
            if (result == null)
            {
                var response = ApiResponse.NotFound("登录日志不存在");
            return Ok(response);
            }

            return ApiResponse<LoginLogDto>.Success(result);
        }
        catch (Exception ex)
        {
            var response = ApiResponse.Error(ex.Message);
            return Ok(response);
        }
    }
}
