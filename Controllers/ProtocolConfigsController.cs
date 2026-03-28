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
/// 协议配置管理控制器
/// </summary>
[ApiController]
[Route("api/v1/protocol-configs")]
[PermissionAuthorize(Permissions.MANAGE_PROTOCOLS)]
public class ProtocolConfigsController : ControllerBase
{
    private readonly IProtocolConfigService _protocolConfigService;

    public ProtocolConfigsController(IProtocolConfigService protocolConfigService)
    {
        _protocolConfigService = protocolConfigService;
    }

    /// <summary>
    /// 获取协议配置列表
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResponse<ProtocolConfigDto>>>> GetProtocolConfigs(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? keyword = null,
        [FromQuery] string? type = null)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var result = await _protocolConfigService.GetProtocolConfigsAsync(page, pageSize, keyword, type, appCode);
            return ApiResponse<PagedResponse<ProtocolConfigDto>>.Success(result);
        }
        catch (Exception ex)
        {
            return ApiResponse<PagedResponse<ProtocolConfigDto>>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 获取协议配置详情
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<ProtocolConfigDto>>> GetProtocolConfig(long id)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var result = await _protocolConfigService.GetProtocolConfigAsync(id, appCode);
            if (result == null)
            {
                return Ok(ApiResponse<ProtocolConfigDto>.NotFound("协议配置不存在"));
            }

            return ApiResponse<ProtocolConfigDto>.Success(result);
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<ProtocolConfigDto>.Error(ex.Message));
        }
    }

    /// <summary>
    /// 创建协议配置
    /// </summary>
    [HttpPost]
    [PermissionAuthorize(Permissions.MANAGE_PROTOCOL_CONFIG)]
    public async Task<ActionResult<ApiResponse<ProtocolConfigDto>>> CreateProtocolConfig([FromBody] CreateProtocolConfigRequest request)
    {
        try
        {
            var result = await _protocolConfigService.CreateProtocolConfigAsync(request);
            return ApiResponse<ProtocolConfigDto>.Success(result, "协议配置创建成功");
        }
        catch (InvalidOperationException ex)
        {
            return Ok(ApiResponse<ProtocolConfigDto>.BadRequest(ex.Message));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<ProtocolConfigDto>.Error(ex.Message));
        }
    }

    /// <summary>
    /// 更新协议配置
    /// </summary>
    [HttpPut("{id}")]
    [PermissionAuthorize(Permissions.MANAGE_PROTOCOL_CONFIG)]
    public async Task<ActionResult<ApiResponse<ProtocolConfigDto>>> UpdateProtocolConfig(long id, [FromBody] UpdateProtocolConfigRequest request)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var result = await _protocolConfigService.UpdateProtocolConfigAsync(id, request, appCode);
            return ApiResponse<ProtocolConfigDto>.Success(result, "协议配置更新成功");
        }
        catch (InvalidOperationException ex)
        {
            return Ok(ApiResponse<ProtocolConfigDto>.BadRequest(ex.Message));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Ok(ApiResponse<ProtocolConfigDto>.Forbidden(ex.Message));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<ProtocolConfigDto>.Error(ex.Message));
        }
    }

    /// <summary>
    /// 删除协议配置
    /// </summary>
    [HttpDelete("{id}")]
    [PermissionAuthorize(Permissions.MANAGE_PROTOCOL_CONFIG)]
    public async Task<ActionResult<ApiResponse>> DeleteProtocolConfig(long id)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            await _protocolConfigService.DeleteProtocolConfigAsync(id, appCode);
            return ApiResponse.Success("协议配置删除成功");
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
    /// 启动协议
    /// </summary>
    [HttpPost("{id}/start")]
    [PermissionAuthorize(Permissions.MANAGE_PROTOCOL_GATEWAY)]
    public async Task<ActionResult<ApiResponse>> StartProtocol(long id)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            await _protocolConfigService.StartProtocolAsync(id, appCode);
            return ApiResponse.Success("协议启动成功");
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
    /// 停止协议
    /// </summary>
    [HttpPost("{id}/stop")]
    [PermissionAuthorize(Permissions.MANAGE_PROTOCOL_GATEWAY)]
    public async Task<ActionResult<ApiResponse>> StopProtocol(long id)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            await _protocolConfigService.StopProtocolAsync(id, appCode);
            return ApiResponse.Success("协议停止成功");
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
