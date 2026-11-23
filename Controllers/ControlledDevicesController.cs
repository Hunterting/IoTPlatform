using IoTPlatform.Configuration;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Filters;
using IoTPlatform.Helpers;
using IoTPlatform.Services;
using IoTPlatform.Services.Guards;
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
    private readonly ICustomerScopeService _customerScope;

    public ControlledDevicesController(IControlledDeviceService service, ICustomerScopeService customerScope)
    {
        _service = service;
        _customerScope = customerScope ?? throw new ArgumentNullException(nameof(customerScope));
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

            // 【注册归属】admin 自身无 AppCode：先按设备反查真实归属，DB 授权集合校验后再注册；
            // 落库 AppCode 由 Service 取设备真实值（非 null），不接受客户端/调用方传入的 null。
            if (Roles.ADMIN == User.FindFirst(ClaimTypes.Role)?.Value)
            {
                var resolved = await ResolveDeviceAppCodeForAdminAsync(request.DeviceId);
                if (!resolved.Approved)
                {
                    return Ok(ApiResponse<ControlledDeviceDto>.Forbidden("无权注册该设备到控制系统"));
                }
                if (string.IsNullOrWhiteSpace(resolved.AppCode))
                {
                    return Ok(ApiResponse<ControlledDeviceDto>.BadRequest("设备缺少 AppCode，拒绝注册"));
                }

                var verifiedResult = await _service.RegisterDeviceAsync(
                    request.DeviceId, resolved.AppCode, userId, userName);
                return Ok(ApiResponse<ControlledDeviceDto>.Success(verifiedResult, "设备已添加到控制系统"));
            }

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

            // 批量注册：先整批鉴权（零写入），再交给 Service；避免部分设备越权成功。
            if (Roles.ADMIN == User.FindFirst(ClaimTypes.Role)?.Value)
            {
                foreach (var deviceId in request.DeviceIds)
                {
                    var resolved = await ResolveDeviceAppCodeForAdminAsync(deviceId);
                    if (!resolved.Approved)
                    {
                        return Ok(ApiResponse<List<ControlledDeviceDto>>.Forbidden("无权注册该设备到控制系统"));
                    }
                    if (string.IsNullOrWhiteSpace(resolved.AppCode))
                    {
                        return Ok(ApiResponse<List<ControlledDeviceDto>>.BadRequest("设备缺少 AppCode，拒绝注册"));
                    }
                }
            }

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

            var success = await _service.UnregisterDeviceAsync(id, appCode, User);

            if (!success)
                return Ok(ApiResponse<bool>.Error("设备未找到或无权限"));

            return Ok(ApiResponse<bool>.Success(true, "设备已从控制系统移除"));
        }
        catch (ForbiddenAccessException ex)
        {
            return Ok(ApiResponse<bool>.Forbidden(ex.Message));
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

            var listScope = await this.ResolveListScopeAsync(_customerScope);
            var result = await _service.GetDevicesAsync(appCode, page, pageSize, isEnabled, isFavorite, listScope.CustomerIds, listScope.AppCodes);

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
            // B4/M6：先点查权威归属并按 scope 三分支判定；越权 HTTP 200 + Code=403。
            if (!await this.CanReadDetailAsync<IoTPlatform.Models.ControlledDevice>(id, false))
            {
                return Ok(ApiResponse<ControlledDeviceDto>.Fail(403, "无权访问该记录"));
            }

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

            var result = await _service.UpdateDeviceAsync(id, request, appCode, User);

            if (result == null)
                return Ok(ApiResponse<ControlledDeviceDto>.NotFound("设备未找到"));

            return Ok(ApiResponse<ControlledDeviceDto>.Success(result, "设备信息已更新"));
        }
        catch (ForbiddenAccessException ex)
        {
            return Ok(ApiResponse<ControlledDeviceDto>.Forbidden(ex.Message));
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

            var success = await _service.ToggleFavoriteAsync(id, appCode, User);

            if (!success)
                return Ok(ApiResponse<bool>.Error("设备未找到"));

            return Ok(ApiResponse<bool>.Success(true, "收藏状态已切换"));
        }
        catch (ForbiddenAccessException ex)
        {
            return Ok(ApiResponse<bool>.Forbidden(ex.Message));
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

    /// <summary>
    /// admin 注册前按设备反查归属（DB 权威）：设备不存在/归属缺失 → 拒绝；
    /// 不在授权客户集合 → 拒绝；通过则返回服务端派生的真实 AppCode。
    /// 该方法只读，不产生任何写库或缓存副作用。
    /// </summary>
    private async Task<(bool Approved, string? AppCode)> ResolveDeviceAppCodeForAdminAsync(long deviceId)
    {
        var customerId = await _customerScope.ResolveCustomerIdByDeviceIdAsync(deviceId);
        if (!customerId.HasValue || !await _customerScope.CanAccessCustomerAsync(User, customerId.Value))
        {
            return (false, null);
        }

        var appCode = await _customerScope.ResolveAppCodeByCustomerIdAsync(customerId.Value);
        return (true, appCode);
    }

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
