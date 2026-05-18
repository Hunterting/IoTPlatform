using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Filters;
using IoTPlatform.Helpers;
using IoTPlatform.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using IoTPlatform.Configuration;

namespace IoTPlatform.Controllers;

/// <summary>
/// 网关管理控制器
/// </summary>
[ApiController]
[Route("api/v1/gateways")]
[PermissionAuthorize(Permissions.MANAGE_PROTOCOL_GATEWAY)]
public class GatewaysController : ControllerBase
{
    private readonly IGatewayService _gatewayService;

    public GatewaysController(IGatewayService gatewayService)
    {
        _gatewayService = gatewayService;
    }

    /// <summary>
    /// 获取网关列表
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResponse<GatewayDto>>>> GetGateways(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? keyword = null)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var result = await _gatewayService.GetGatewaysAsync(page, pageSize, keyword, appCode);
            return ApiResponse<PagedResponse<GatewayDto>>.Success(result);
        }
        catch (Exception ex)
        {
            return ApiResponse<PagedResponse<GatewayDto>>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 获取网关详情
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<GatewayDto>>> GetGateway(long id)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var result = await _gatewayService.GetGatewayAsync(id, appCode);
            if (result == null)
            {
                return Ok(ApiResponse.NotFound("网关不存在"));
            }

            return ApiResponse<GatewayDto>.Success(result);
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse.Error(ex.Message));
        }
    }

    /// <summary>
    /// 创建网关
    /// </summary>
    [HttpPost]
    [PermissionAuthorize(Permissions.MANAGE_GATEWAY_CONFIG)]
    public async Task<ActionResult<ApiResponse<GatewayDto>>> CreateGateway([FromBody] CreateGatewayRequest request)
    {
        try
        {
            var result = await _gatewayService.CreateGatewayAsync(request);
            return ApiResponse<GatewayDto>.Success(result, "网关创建成功");
        }
        catch (InvalidOperationException ex)
        {
            return Ok(ApiResponse.BadRequest(ex.Message));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse.Error(ex.Message));
        }
    }

    /// <summary>
    /// 更新网关
    /// </summary>
    [HttpPut("{id}")]
    [PermissionAuthorize(Permissions.MANAGE_GATEWAY_CONFIG)]
    public async Task<ActionResult<ApiResponse<GatewayDto>>> UpdateGateway(long id, [FromBody] UpdateGatewayRequest request)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var result = await _gatewayService.UpdateGatewayAsync(id, request, appCode);
            return ApiResponse<GatewayDto>.Success(result, "网关更新成功");
        }
        catch (InvalidOperationException ex)
        {
            return Ok(ApiResponse.BadRequest(ex.Message));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Ok(ApiResponse.Forbidden(ex.Message));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse.Error(ex.Message));
        }
    }

    /// <summary>
    /// 删除网关
    /// </summary>
    [HttpDelete("{id}")]
    [PermissionAuthorize(Permissions.MANAGE_GATEWAY_CONFIG)]
    public async Task<ActionResult<ApiResponse>> DeleteGateway(long id)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            await _gatewayService.DeleteGatewayAsync(id, appCode);
            return ApiResponse.Success("网关删除成功");
        }
        catch (InvalidOperationException ex)
        {
            return Ok(ApiResponse.BadRequest(ex.Message));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Ok(ApiResponse.Forbidden(ex.Message));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse.Error(ex.Message));
        }
    }

    /// <summary>
    /// 启动网关
    /// </summary>
    [HttpPost("{id}/start")]
    [PermissionAuthorize(Permissions.MANAGE_GATEWAY_CONFIG)]
    public async Task<ActionResult<ApiResponse>> StartGateway(long id)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            await _gatewayService.StartGatewayAsync(id, appCode);
            return ApiResponse.Success("网关启动成功");
        }
        catch (InvalidOperationException ex)
        {
            return Ok(ApiResponse.BadRequest(ex.Message));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Ok(ApiResponse.Forbidden(ex.Message));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse.Error(ex.Message));
        }
    }

    /// <summary>
    /// 停止网关
    /// </summary>
    [HttpPost("{id}/stop")]
    [PermissionAuthorize(Permissions.MANAGE_GATEWAY_CONFIG)]
    public async Task<ActionResult<ApiResponse>> StopGateway(long id)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            await _gatewayService.StopGatewayAsync(id, appCode);
            return ApiResponse.Success("网关停止成功");
        }
        catch (InvalidOperationException ex)
        {
            return Ok(ApiResponse.BadRequest(ex.Message));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Ok(ApiResponse.Forbidden(ex.Message));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse.Error(ex.Message));
        }
    }
}
