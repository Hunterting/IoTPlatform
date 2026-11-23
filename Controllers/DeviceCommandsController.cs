using System.Security.Claims;
using IoTPlatform.Configuration;
using IoTPlatform.Data;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Filters;
using IoTPlatform.Helpers;
using IoTPlatform.Services;
using IoTPlatform.Services.Guards;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IoTPlatform.Controllers;

/// <summary>
/// 设备指令控制器（B1/M4：写端点归属收口）。
///
/// <para>原则 G：deviceId/commandId 归属判定在指令下发、取消、重试之前完成；
/// 越权抛 <see cref="ForbiddenAccessException"/> 并映射业务码 403，物理设备与 DB 零副作用。</para>
/// </summary>
[ApiController]
[Route("api/v1/device-commands")]
[PermissionAuthorize(Permissions.VIEW_DEVICE_COMMANDS)]
public class DeviceCommandsController : ControllerBase
{
    private readonly IDeviceCommandService _commandService;
    private readonly IDeviceAccessGuard _deviceGuard;
    private readonly ICustomerScopeGuard _scopeGuard;
    private readonly ICustomerScopeService _scope;
    private readonly AppDbContext _db;

    public DeviceCommandsController(
        IDeviceCommandService commandService,
        IDeviceAccessGuard deviceGuard,
        ICustomerScopeGuard scopeGuard,
        ICustomerScopeService scope,
        AppDbContext db)
    {
        _commandService = commandService;
        _deviceGuard = deviceGuard;
        _scopeGuard = scopeGuard;
        _scope = scope;
        _db = db;
    }

    private CancellationToken RequestAborted =>
        ControllerContext?.HttpContext?.RequestAborted ?? CancellationToken.None;

    // ─────────────────────────────────────────────
    // 发送指令
    // ─────────────────────────────────────────────

    /// <summary>
    /// 发送设备控制指令
    /// POST /api/v1/device-commands/send
    /// </summary>
    [HttpPost("send")]
    [PermissionAuthorize(Permissions.SEND_DEVICE_COMMANDS)]
    public async Task<ActionResult<ApiResponse<DeviceCommandResponse>>> SendCommand(
        [FromBody] SendDeviceCommandRequest request)
    {
        try
        {
            // 【M4】设备在 Controller 前置校验；Service/AnSheng 单点再做一次双保险。
            await _deviceGuard.EnsureCanOperateAsync(User, request.DeviceId, RequestAborted);

            var appCode = User.FindFirst("AppCode")?.Value;
            var userId = GetUserId();
            var userName = User.FindFirst(ClaimTypes.Name)?.Value
                          ?? User.FindFirst("username")?.Value;

            var result = await _commandService.SendCommandAsync(
                request, appCode, userId, userName, User, RequestAborted);

            if (!result.Success)
                return Ok(ApiResponse<DeviceCommandResponse>.Error(result.ErrorMessage ?? "指令发送失败"));

            return Ok(ApiResponse<DeviceCommandResponse>.Success(result, "指令已成功下发"));
        }
        catch (ForbiddenAccessException ex)
        {
            return Ok(ApiResponse<DeviceCommandResponse>.Forbidden(ex.Message));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Ok(ApiResponse<DeviceCommandResponse>.Unauthorized(ex.Message));
        }
        catch (NotFoundException ex)
        {
            return Ok(ApiResponse<DeviceCommandResponse>.NotFound(ex.Message));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<DeviceCommandResponse>.Error(ex.Message));
        }
    }

    /// <summary>
    /// 批量发送设备指令
    /// POST /api/v1/device-commands/send/batch
    /// </summary>
    [HttpPost("send/batch")]
    [PermissionAuthorize(Permissions.SEND_DEVICE_COMMANDS)]
    public async Task<ActionResult<ApiResponse<List<DeviceCommandResponse>>>> SendBatchCommands(
        [FromBody] SendBatchDeviceCommandRequest request)
    {
        try
        {
            // 【M4 批量】先一次性校验全部 deviceId，任一越权即整体拒绝，绝无部分下发。
            var deviceIds = request.DeviceIds.Distinct().ToList();
            await _deviceGuard.EnsureCanOperateAsync(User, deviceIds, RequestAborted);

            var appCode = User.FindFirst("AppCode")?.Value;
            var userId = GetUserId();
            var userName = User.FindFirst(ClaimTypes.Name)?.Value
                          ?? User.FindFirst("username")?.Value;

            // 将批量请求转换为单个请求列表
            var requests = request.DeviceIds.Select(deviceId => new SendDeviceCommandRequest
            {
                DeviceId = deviceId,
                CommandType = request.CommandType,
                Parameters = request.Parameters,
                TimeoutSeconds = request.TimeoutSeconds,
                MaxRetries = request.MaxRetries
            }).ToList();

            var results = await _commandService.SendBatchCommandsAsync(
                requests, appCode, userId, userName, User, RequestAborted);
            return Ok(ApiResponse<List<DeviceCommandResponse>>.Success(results,
                $"批量下发完成，共 {results.Count} 条，成功 {results.Count(r => r.Success)} 条"));
        }
        catch (ForbiddenAccessException ex)
        {
            return Ok(ApiResponse<List<DeviceCommandResponse>>.Forbidden(ex.Message));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Ok(ApiResponse<List<DeviceCommandResponse>>.Unauthorized(ex.Message));
        }
        catch (NotFoundException ex)
        {
            return Ok(ApiResponse<List<DeviceCommandResponse>>.NotFound(ex.Message));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<List<DeviceCommandResponse>>.Error(ex.Message));
        }
    }

    // ─────────────────────────────────────────────
    // 查询指令状态（B4 查询侧不属本批，保持原路径）
    // ─────────────────────────────────────────────

    /// <summary>
    /// 获取指令状态
    /// GET /api/v1/device-commands/{commandId}
    /// </summary>
    [HttpGet("{commandId}")]
    public async Task<ActionResult<ApiResponse<DeviceCommandDto>>> GetCommand(string commandId)
    {
        try
        {
            await EnsureCommandOwnerAsync(commandId);
            var appCode = User.FindFirst("AppCode")?.Value;
            var result = await _commandService.GetCommandAsync(commandId, appCode);

            if (result == null)
                return Ok(ApiResponse.NotFound("指令不存在"));

            return Ok(ApiResponse<DeviceCommandDto>.Success(result));
        }
        catch (IoTPlatform.Services.Guards.ForbiddenAccessException ex)
        {
            return Ok(ApiResponse<DeviceCommandDto>.Forbidden(ex.Message));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<DeviceCommandDto>.Error(ex.Message));
        }
    }

    /// <summary>
    /// 获取设备指令列表
    /// GET /api/v1/device-commands?deviceId=xxx&page=1&pageSize=20
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResponse<DeviceCommandDto>>>> GetCommands(
        [FromQuery] long deviceId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var listScope = await this.ResolveListScopeAsync(_scope);
            var result = await _commandService.GetCommandsAsync(deviceId, appCode, page, pageSize, listScope.CustomerIds, listScope.AppCodes);
            return Ok(ApiResponse<PagedResponse<DeviceCommandDto>>.Success(result));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<PagedResponse<DeviceCommandDto>>.Error(ex.Message));
        }
    }

    /// <summary>
    /// 获取指令历史记录
    /// GET /api/v1/device-commands/{commandId}/history
    /// </summary>
    [HttpGet("{commandId}/history")]
    public async Task<ActionResult<ApiResponse<List<CommandHistoryDto>>>> GetCommandHistory(string commandId)
    {
        try
        {
            await EnsureCommandOwnerAsync(commandId);
            var appCode = User.FindFirst("AppCode")?.Value;
            var result = await _commandService.GetCommandHistoryAsync(commandId, appCode);
            return Ok(ApiResponse<List<CommandHistoryDto>>.Success(result));
        }
        catch (IoTPlatform.Services.Guards.ForbiddenAccessException ex)
        {
            return Ok(ApiResponse<List<CommandHistoryDto>>.Forbidden(ex.Message));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<List<CommandHistoryDto>>.Error(ex.Message));
        }
    }

    // ─────────────────────────────────────────────
    // 取消 & 重试
    // ─────────────────────────────────────────────

    /// <summary>
    /// 取消指令
    /// POST /api/v1/device-commands/{commandId}/cancel
    /// </summary>
    [HttpPost("{commandId}/cancel")]
    [PermissionAuthorize(Permissions.CANCEL_DEVICE_COMMANDS)]
    public async Task<ActionResult<ApiResponse<bool>>> CancelCommand(string commandId)
    {
        try
        {
            // 【M4】commandId → AppCode → CustomerId → 统一 Guard；不通过则零状态变更。
            await EnsureCommandOwnerAsync(commandId);

            var appCode = User.FindFirst("AppCode")?.Value;
            var success = await _commandService.CancelCommandAsync(
                commandId, appCode, User, RequestAborted);

            if (!success)
                return Ok(ApiResponse<bool>.Error("取消失败：指令不存在或已处于终止状态"));

            return Ok(ApiResponse<bool>.Success(true, "指令已取消"));
        }
        catch (ForbiddenAccessException ex)
        {
            return Ok(ApiResponse<bool>.Forbidden(ex.Message));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Ok(ApiResponse<bool>.Unauthorized(ex.Message));
        }
        catch (NotFoundException ex)
        {
            return Ok(ApiResponse<bool>.NotFound(ex.Message));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<bool>.Error(ex.Message));
        }
    }

    /// <summary>
    /// 重试失败指令
    /// POST /api/v1/device-commands/{commandId}/retry
    /// </summary>
    [HttpPost("{commandId}/retry")]
    [PermissionAuthorize(Permissions.SEND_DEVICE_COMMANDS)]
    public async Task<ActionResult<ApiResponse<bool>>> RetryCommand(string commandId)
    {
        try
        {
            // 【M4】重试会重新下发到物理设备，归属校验必须先于任何副作用。
            await EnsureCommandOwnerAsync(commandId);

            var success = await _commandService.RetryCommandAsync(
                commandId, User, RequestAborted);

            if (!success)
                return Ok(ApiResponse<bool>.Error("重试失败：指令不存在、未达失败状态或已超过最大重试次数"));

            return Ok(ApiResponse<bool>.Success(true, "重试指令已下发"));
        }
        catch (ForbiddenAccessException ex)
        {
            return Ok(ApiResponse<bool>.Forbidden(ex.Message));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Ok(ApiResponse<bool>.Unauthorized(ex.Message));
        }
        catch (NotFoundException ex)
        {
            return Ok(ApiResponse<bool>.NotFound(ex.Message));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<bool>.Error(ex.Message));
        }
    }

    // ─────────────────────────────────────────────
    // 私有辅助
    // ─────────────────────────────────────────────

    /// <summary>
    /// 指令归属点查（IgnoreQueryFilters）+ 统一范围守卫判定。
    /// 所有归属值来自服务端，绝不使用请求头或请求体中的 AppCode。
    /// </summary>
    private async Task EnsureCommandOwnerAsync(string commandId)
    {
        var appCode = await _db.DeviceCommands
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(c => c.CommandId == commandId)
            .Select(c => c.AppCode)
            .FirstOrDefaultAsync(RequestAborted);

        if (appCode == null)
        {
            throw new NotFoundException("指令不存在");
        }

        var customerId = await _scope.ResolveCustomerIdByAppCodeAsync(appCode, RequestAborted);
        var scope = await _scope.ResolveAsync(User, RequestAborted);
        _scopeGuard.AssertScope(scope, customerId, appCode);
    }

    private long? GetUserId()
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                       ?? User.FindFirst("sub")?.Value
                       ?? User.FindFirst("userId")?.Value;

        return long.TryParse(userIdStr, out var id) ? id : null;
    }
}
