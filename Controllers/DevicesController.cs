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
        // ── T2 权限收敛：仅超级管理员可走「直接新增」兜底通道 ──
        // PermissionFilter 已放行所有持 CREATE_DEVICES 的角色（含租户 admin/operator），
        // 此处再做服务端强校验：非超级管理员一律 403，杜绝租户越权建设备（风险 R3）。
        // 超级管理员判据取自 ClaimTypes.Role（与 PermissionFilter L37-39 同源）。
        var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        if (role != IoTPlatform.Configuration.Roles.SUPER_ADMIN)
        {
            // 返回泛型 ApiResponse<DeviceDto> 以保持与动作返回类型一致（HTTP 仍为 200，业务码 403）。
            var forbidden = ApiResponse<DeviceDto>.Fail(
                403, "仅超级管理员可直接新增设备，请走认领流程或由超级管理员操作。");
            return Ok(forbidden);
        }

        try
        {
            var result = await _deviceService.CreateDeviceAsync(request);
            return ApiResponse<DeviceDto>.Success(result, "设备创建成功");
        }
        catch (DeviceRegistrationException ex)
        {
            // 去重 / 存在性 / 越权类拒绝：按异常携带的语义状态码映射（403 / 409）。
            // 返回泛型 ApiResponse<DeviceDto> 以保持与动作返回类型一致。
            var response = ApiResponse<DeviceDto>.Fail(ex.StatusCode, ex.Message);
            return Ok(response);
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
