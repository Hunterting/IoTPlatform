using IoTPlatform.Configuration;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Filters;
using IoTPlatform.Helpers;
using IoTPlatform.Services;
using Microsoft.AspNetCore.Mvc;

namespace IoTPlatform.Controllers;

/// <summary>
/// 设备授权控制器 —— 系统设置 → 设备授权 真实数据源。
///  - 列表/统计：租户可查看自己名下设备（VIEW_SETTINGS）；超管看全部。
///  - 授权/续期/撤销：仅超级管理员（服务端强校验，与 DevicesController T2 同款）。
/// </summary>
[ApiController]
[Route("api/v1/device-authorizations")]
[PermissionAuthorize(Permissions.VIEW_SETTINGS)]
public class DeviceAuthorizationsController : ControllerBase
{
    private readonly IDeviceAuthorizationService _service;
    private readonly ILogger<DeviceAuthorizationsController> _logger;

    public DeviceAuthorizationsController(
        IDeviceAuthorizationService service,
        ILogger<DeviceAuthorizationsController> logger)
    {
        _service = service;
        _logger = logger;
    }

    private string? AppCode => User.FindFirst("AppCode")?.Value;
    private bool IsSuperAdmin =>
        User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value == Roles.SUPER_ADMIN;
    private long? OperatorId => long.TryParse(User.FindFirst("UserId")?.Value, out var id) ? id : null;
    private string? OperatorName => User.FindFirst("UserName")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;

    /// <summary>授权列表（每台设备一行，含派生状态、剩余天数、即将过期标记）</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<DeviceAuthorizationDto>>>> GetList(CancellationToken ct)
    {
        try
        {
            var result = await _service.GetListAsync(IsSuperAdmin ? null : AppCode, ct);
            return ApiResponse<List<DeviceAuthorizationDto>>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "设备授权列表查询失败");
            return ApiResponse<List<DeviceAuthorizationDto>>.Error(ex.Message);
        }
    }

    /// <summary>统计（含即将过期阈值）</summary>
    [HttpGet("summary")]
    public async Task<ActionResult<ApiResponse<DeviceAuthorizationSummaryDto>>> GetSummary(CancellationToken ct)
    {
        try
        {
            var result = await _service.GetSummaryAsync(IsSuperAdmin ? null : AppCode, ct);
            return ApiResponse<DeviceAuthorizationSummaryDto>.Success(result);
        }
        catch (Exception ex)
        {
            return ApiResponse<DeviceAuthorizationSummaryDto>.Error(ex.Message);
        }
    }

    /// <summary>授权 / 续期（仅超管）</summary>
    [HttpPost("grant")]
    public async Task<ActionResult<ApiResponse<DeviceAuthorizationDto>>> Grant(
        [FromBody] GrantDeviceAuthorizationRequest request, CancellationToken ct)
    {
        if (!IsSuperAdmin)
            return ApiResponse<DeviceAuthorizationDto>.Fail(403, "仅超级管理员可为设备授权或续期。");
        try
        {
            var result = await _service.GrantAsync(request, OperatorId, OperatorName, AppCode, ct);
            return ApiResponse<DeviceAuthorizationDto>.Success(result, "授权成功");
        }
        catch (InvalidOperationException ex)
        {
            return ApiResponse<DeviceAuthorizationDto>.BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "设备授权失败");
            return ApiResponse<DeviceAuthorizationDto>.Error(ex.Message);
        }
    }

    /// <summary>手动触发到期扫描（仅超管；便于运维即时刷新与联调验证）</summary>
    [HttpPost("scan")]
    public async Task<ActionResult<ApiResponse<object>>> Scan(CancellationToken ct)
    {
        if (!IsSuperAdmin)
            return ApiResponse<object>.Fail(403, "仅超级管理员可手动触发到期扫描。");
        try
        {
            var count = await _service.ScanAndNotifyExpiringAsync(ct);
            return ApiResponse<object>.Success(new { created = count }, $"扫描完成，新增 {count} 条提醒");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "手动触发设备授权到期扫描失败");
            return ApiResponse<object>.Error(ex.Message);
        }
    }

    /// <summary>撤销授权（仅超管）</summary>
    [HttpPost("revoke")]
    public async Task<ActionResult<ApiResponse<DeviceAuthorizationDto>>> Revoke(
        [FromBody] RevokeDeviceAuthorizationRequest request, CancellationToken ct)
    {
        if (!IsSuperAdmin)
            return ApiResponse<DeviceAuthorizationDto>.Fail(403, "仅超级管理员可撤销设备授权。");
        try
        {
            var result = await _service.RevokeAsync(request, AppCode, ct);
            return ApiResponse<DeviceAuthorizationDto>.Success(result, "已撤销授权");
        }
        catch (InvalidOperationException ex)
        {
            return ApiResponse<DeviceAuthorizationDto>.BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "撤销设备授权失败");
            return ApiResponse<DeviceAuthorizationDto>.Error(ex.Message);
        }
    }
}
