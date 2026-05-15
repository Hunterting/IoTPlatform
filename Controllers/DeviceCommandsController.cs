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
/// 设备指令控制器
/// </summary>
[ApiController]
[Route("api/v1/device-commands")]
[PermissionAuthorize(Permissions.VIEW_DEVICE_COMMANDS)]
public class DeviceCommandsController : ControllerBase
{
    private readonly IDeviceCommandService _commandService;

    public DeviceCommandsController(IDeviceCommandService commandService)
    {
        _commandService = commandService;
    }

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
            var appCode = User.FindFirst("AppCode")?.Value;
            var userId = GetUserId();
            var userName = User.FindFirst(ClaimTypes.Name)?.Value
                          ?? User.FindFirst("username")?.Value;

            var result = await _commandService.SendCommandAsync(request, appCode, userId, userName);

            if (!result.Success)
                return Ok(ApiResponse<DeviceCommandResponse>.Error(result.ErrorMessage ?? "指令发送失败"));

            return Ok(ApiResponse<DeviceCommandResponse>.Success(result, "指令已成功下发"));
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

            var results = await _commandService.SendBatchCommandsAsync(requests, appCode, userId, userName);
            return Ok(ApiResponse<List<DeviceCommandResponse>>.Success(results,
                $"批量下发完成，共 {results.Count} 条，成功 {results.Count(r => r.Success)} 条"));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<List<DeviceCommandResponse>>.Error(ex.Message));
        }
    }

    // ─────────────────────────────────────────────
    // 查询指令状态
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
            var appCode = User.FindFirst("AppCode")?.Value;
            var result = await _commandService.GetCommandAsync(commandId, appCode);

            if (result == null)
                return Ok(ApiResponse.NotFound("指令不存在"));

            return Ok(ApiResponse<DeviceCommandDto>.Success(result));
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
            var result = await _commandService.GetCommandsAsync(deviceId, appCode, page, pageSize);
            return Ok(ApiResponse<PagedResponse<DeviceCommandDto>>.Success(result));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<PagedResponse<DeviceCommandDto>>.Error(ex.Message));
        }
    }

    // ─────────────────────────────────────────────
    // 指令历史
    // ─────────────────────────────────────────────

    /// <summary>
    /// 获取指令历史记录
    /// GET /api/v1/device-commands/{commandId}/history
    /// </summary>
    [HttpGet("{commandId}/history")]
    public async Task<ActionResult<ApiResponse<List<CommandHistoryDto>>>> GetCommandHistory(string commandId)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var result = await _commandService.GetCommandHistoryAsync(commandId, appCode);
            return Ok(ApiResponse<List<CommandHistoryDto>>.Success(result));
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
            var appCode = User.FindFirst("AppCode")?.Value;
            var success = await _commandService.CancelCommandAsync(commandId, appCode);

            if (!success)
                return Ok(ApiResponse<bool>.Error("取消失败：指令不存在或已处于终止状态"));

            return Ok(ApiResponse<bool>.Success(true, "指令已取消"));
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
            var success = await _commandService.RetryCommandAsync(commandId);

            if (!success)
                return Ok(ApiResponse<bool>.Error("重试失败：指令不存在、未达失败状态或已超过最大重试次数"));

            return Ok(ApiResponse<bool>.Success(true, "重试指令已下发"));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<bool>.Error(ex.Message));
        }
    }

    // ─────────────────────────────────────────────
    // 私有辅助
    // ─────────────────────────────────────────────

    private long? GetUserId()
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                       ?? User.FindFirst("sub")?.Value
                       ?? User.FindFirst("userId")?.Value;

        return long.TryParse(userIdStr, out var id) ? id : null;
    }
}
