using IoTPlatform.Configuration;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Filters;
using IoTPlatform.Helpers;
using IoTPlatform.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace IoTPlatform.Controllers;

/// <summary>
/// 受控设备控制器
/// 管理已添加到指令控制系统的设备
/// </summary>
[ApiController]
[Route("api/v1/controlled-devices")]
[PermissionAuthorize(Permissions.VIEW_DEVICE_COMMANDS)]
public class ControlledDevicesController : ControllerBase
{
    private readonly IControlledDeviceService _service;

    public ControlledDevicesController(IControlledDeviceService service)
    {
        _service = service;
    }

    /// <summary>
    /// 注册设备到控制系统
    /// POST /api/v1/controlled-devices/register
    /// </summary>
    [HttpPost("register")]
    [PermissionAuthorize(Permissions.SEND_DEVICE_COMMANDS)]
    public async Task<ActionResult<ApiResponse<ControlledDeviceDto>>> RegisterDevice(
        [FromBody] RegisterControlledDeviceRequest request)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var userId = GetUserId();
            var userName = GetUserName();

            var result = await _service.RegisterDeviceAsync(request.DeviceId, appCode, userId, userName);

            return Ok(ApiResponse<ControlledDeviceDto>.Success(result, "设备已添加到控制系统"));
        }
        catch (InvalidOperationException ex)
        {
            return Ok(ApiResponse<ControlledDeviceDto>.Error(ex.Message));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<ControlledDeviceDto>.Error(ex.Message));
        }
    }

    /// <summary>
    /// 批量注册设备
    /// POST /api/v1/controlled-devices/register/batch
    /// </summary>
    [HttpPost("register/batch")]
    [PermissionAuthorize(Permissions.SEND_DEVICE_COMMANDS)]
    public async Task<ActionResult<ApiResponse<List<ControlledDeviceDto>>>> RegisterDevices(
        [FromBody] BatchRegisterControlledDeviceRequest request)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var userId = GetUserId();
            var userName = GetUserName();

            var results = await _service.RegisterDevicesAsync(request.DeviceIds, appCode, userId, userName);

            return Ok(ApiResponse<List<ControlledDeviceDto>>.Success(results,
                $"成功注册 {results.Count} 个设备到控制系统"));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<List<ControlledDeviceDto>>.Error(ex.Message));
        }
    }

    /// <summary>
    /// 取消注册设备
    /// DELETE /api/v1/controlled-devices/{id}
    /// </summary>
    [HttpDelete("{id}")]
    [PermissionAuthorize(Permissions.SEND_DEVICE_COMMANDS)]
    public async Task<ActionResult<ApiResponse<bool>>> UnregisterDevice(long id)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;

            var success = await _service.UnregisterDeviceAsync(id, appCode);

            if (!success)
                return Ok(ApiResponse<bool>.Error("设备未找到或无权限"));

            return Ok(ApiResponse<bool>.Success(true, "设备已从控制系统移除"));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<bool>.Error(ex.Message));
        }
    }

    /// <summary>
    /// 获取受控设备列表
    /// GET /api/v1/controlled-devices?page=1&pageSize=20&isEnabled=true&isFavorite=true
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResponse<ControlledDeviceDto>>>> GetDevices(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] bool? isEnabled = null,
        [FromQuery] bool? isFavorite = null)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;

            var result = await _service.GetDevicesAsync(appCode, page, pageSize, isEnabled, isFavorite);

            return Ok(ApiResponse<PagedResponse<ControlledDeviceDto>>.Success(result));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<PagedResponse<ControlledDeviceDto>>.Error(ex.Message));
        }
    }

    /// <summary>
    /// 获取单个受控设备
    /// GET /api/v1/controlled-devices/{id}
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<ControlledDeviceDto>>> GetDevice(long id)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;

            var result = await _service.GetDeviceAsync(id, appCode);

            if (result == null)
                return Ok(ApiResponse<ControlledDeviceDto>.NotFound("设备未找到"));

            return Ok(ApiResponse<ControlledDeviceDto>.Success(result));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<ControlledDeviceDto>.Error(ex.Message));
        }
    }

    /// <summary>
    /// 更新受控设备
    /// PUT /api/v1/controlled-devices/{id}
    /// </summary>
    [HttpPut("{id}")]
    [PermissionAuthorize(Permissions.SEND_DEVICE_COMMANDS)]
    public async Task<ActionResult<ApiResponse<ControlledDeviceDto>>> UpdateDevice(
        long id,
        [FromBody] UpdateControlledDeviceRequest request)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;

            var result = await _service.UpdateDeviceAsync(id, request, appCode);

            if (result == null)
                return Ok(ApiResponse<ControlledDeviceDto>.NotFound("设备未找到"));

            return Ok(ApiResponse<ControlledDeviceDto>.Success(result, "设备信息已更新"));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<ControlledDeviceDto>.Error(ex.Message));
        }
    }

    /// <summary>
    /// 切换收藏状态
    /// POST /api/v1/controlled-devices/{id}/toggle-favorite
    /// </summary>
    [HttpPost("{id}/toggle-favorite")]
    public async Task<ActionResult<ApiResponse<bool>>> ToggleFavorite(long id)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;

            var success = await _service.ToggleFavoriteAsync(id, appCode);

            if (!success)
                return Ok(ApiResponse<bool>.Error("设备未找到"));

            return Ok(ApiResponse<bool>.Success(true, "收藏状态已切换"));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<bool>.Error(ex.Message));
        }
    }

    /// <summary>
    /// 检查设备是否已注册
    /// GET /api/v1/controlled-devices/check/{deviceId}
    /// </summary>
    [HttpGet("check/{deviceId}")]
    public async Task<ActionResult<ApiResponse<bool>>> CheckDeviceRegistered(long deviceId)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;

            var isRegistered = await _service.IsDeviceRegisteredAsync(deviceId, appCode);

            return Ok(ApiResponse<bool>.Success(isRegistered));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<bool>.Error(ex.Message));
        }
    }

    // ── 私有方法 ───────────────────────────────────────────────────────────

    private long? GetUserId()
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                       ?? User.FindFirst("sub")?.Value
                       ?? User.FindFirst("userId")?.Value;

        return long.TryParse(userIdStr, out var id) ? id : null;
    }

    private string? GetUserName()
    {
        return User.FindFirst(ClaimTypes.Name)?.Value
              ?? User.FindFirst("username")?.Value;
    }
}
