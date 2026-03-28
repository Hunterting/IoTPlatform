using IoTPlatform.Configuration;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Filters;
using IoTPlatform.Helpers;
using IoTPlatform.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

    public LogsController(ILogService logService)
    {
        _logService = logService;
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

            var result = await _logService.GetOperationLogsAsync(page, pageSize, module, action, userId, startTime, endTime, appCode, role);
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
            var appCode = User.FindFirst("AppCode")?.Value;
            var role = User.FindFirst(ClaimTypes.Role)?.Value;

            var result = await _logService.GetOperationLogAsync(id, appCode, role);
            if (result == null)
            {
                return Ok(ApiResponse<OperationLogDto>.NotFound("操作日志不存在");
            }

            return ApiResponse<OperationLogDto>.Success(result);
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<OperationLogDto>.Error(ex.Message);
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

            var result = await _logService.GetLoginLogsAsync(page, pageSize, userId, status, startTime, endTime, appCode, role);
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
            var appCode = User.FindFirst("AppCode")?.Value;
            var role = User.FindFirst(ClaimTypes.Role)?.Value;

            var result = await _logService.GetLoginLogAsync(id, appCode, role);
            if (result == null)
            {
                return Ok(ApiResponse<LoginLogDto>.NotFound("登录日志不存在");
            }

            return ApiResponse<LoginLogDto>.Success(result);
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<LoginLogDto>.Error(ex.Message);
        }
    }
}
