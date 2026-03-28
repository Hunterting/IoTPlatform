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
/// 设备管理控制器
/// </summary>
[ApiController]
[Route("api/v1/devices")]
[PermissionAuthorize(Permissions.VIEW_DEVICES)]
public class DevicesController : ControllerBase
{
    private readonly IDeviceService _deviceService;

    public DevicesController(IDeviceService deviceService)
    {
        _deviceService = deviceService;
    }

    /// <summary>
    /// 获取设备列表
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResponse<DeviceDto>>>> GetDevices(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? keyword = null,
        [FromQuery] string? status = null)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var allowedAreaIds = GetAllowedAreaIds();

            var result = await _deviceService.GetDevicesAsync(page, pageSize, keyword, status, appCode, allowedAreaIds);
            return ApiResponse<PagedResponse<DeviceDto>>.Success(result);
        }
        catch (Exception ex)
        {
            return ApiResponse<PagedResponse<DeviceDto>>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 获取设备详情
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<DeviceDto>>> GetDevice(long id)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var allowedAreaIds = GetAllowedAreaIds();

            var result = await _deviceService.GetDeviceAsync(id, appCode, allowedAreaIds);
            if (result == null)
            {
                var response = ApiResponse.NotFound("设备不存在");
            return Ok(response);
            }

            return ApiResponse<DeviceDto>.Success(result);
        }
        catch (Exception ex)
        {
            var response = ApiResponse.Error(ex.Message);
            return Ok(response);
        }
    }

    /// <summary>
    /// 获取设备详情（包含传感器）
    /// </summary>
    [HttpGet("{id}/detail")]
    public async Task<ActionResult<ApiResponse<DeviceDetailDto>>> GetDeviceDetail(long id)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var allowedAreaIds = GetAllowedAreaIds();

            var result = await _deviceService.GetDeviceDetailAsync(id, appCode, allowedAreaIds);
            if (result == null)
            {
                var response = ApiResponse.NotFound("设备不存在");
            return Ok(response);
            }

            return ApiResponse<DeviceDetailDto>.Success(result);
        }
        catch (Exception ex)
        {
            var response = ApiResponse.Error(ex.Message);
            return Ok(response);
        }
    }

    /// <summary>
    /// 根据区域获取设备列表
    /// </summary>
    [HttpGet("area/{areaId}")]
    public async Task<ActionResult<ApiResponse<List<DeviceDto>>>> GetDevicesByArea(long areaId)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var allowedAreaIds = GetAllowedAreaIds();

            var result = await _deviceService.GetDevicesByAreaAsync(areaId, appCode, allowedAreaIds);
            return ApiResponse<List<DeviceDto>>.Success(result);
        }
        catch (Exception ex)
        {
            return ApiResponse<List<DeviceDto>>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 创建设备
    /// </summary>
    [HttpPost]
    [PermissionAuthorize(Permissions.CREATE_DEVICES)]
    public async Task<ActionResult<ApiResponse<DeviceDto>>> CreateDevice([FromBody] CreateDeviceRequest request)
    {
        try
        {
            var result = await _deviceService.CreateDeviceAsync(request);
            return ApiResponse<DeviceDto>.Success(result, "设备创建成功");
        }
        catch (InvalidOperationException ex)
        {
            var response = ApiResponse.BadRequest(ex.Message);
            return Ok(response);
        }
        catch (Exception ex)
        {
            var response = ApiResponse.Error(ex.Message);
            return Ok(response);
        }
    }

    /// <summary>
    /// 更新设备
    /// </summary>
    [HttpPut("{id}")]
    [PermissionAuthorize(Permissions.UPDATE_DEVICES)]
    public async Task<ActionResult<ApiResponse<DeviceDto>>> UpdateDevice(long id, [FromBody] UpdateDeviceRequest request)
    {
        try
        {
            var result = await _deviceService.UpdateDeviceAsync(id, request);
            return ApiResponse<DeviceDto>.Success(result, "设备更新成功");
        }
        catch (InvalidOperationException ex)
        {
            var response = ApiResponse.BadRequest(ex.Message);
            return Ok(response);
        }
        catch (Exception ex)
        {
            var response = ApiResponse.Error(ex.Message);
            return Ok(response);
        }
    }

    /// <summary>
    /// 删除设备
    /// </summary>
    [HttpDelete("{id}")]
    [PermissionAuthorize(Permissions.DELETE_DEVICES)]
    public async Task<ActionResult<ApiResponse>> DeleteDevice(long id)
    {
        try
        {
            await _deviceService.DeleteDeviceAsync(id);
            return ApiResponse.Success("设备删除成功");
        }
        catch (InvalidOperationException ex)
        {
            var response = ApiResponse.BadRequest(ex.Message);
            return Ok(response);
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
