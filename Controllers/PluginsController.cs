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
/// 插件管理控制器
/// </summary>
[ApiController]
[Route("api/v1/plugins")]
[PermissionAuthorize(Permissions.MANAGE_PLUGIN_SYSTEM)]
public class PluginsController : ControllerBase
{
    private readonly IPluginService _pluginService;

    public PluginsController(IPluginService pluginService)
    {
        _pluginService = pluginService;
    }

    /// <summary>
    /// 获取插件列表
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResponse<PluginDto>>>> GetPlugins(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? keyword = null)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var result = await _pluginService.GetPluginsAsync(page, pageSize, keyword, appCode);
            return ApiResponse<PagedResponse<PluginDto>>.Success(result);
        }
        catch (Exception ex)
        {
            return ApiResponse<PagedResponse<PluginDto>>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 获取插件详情
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<PluginDto>>> GetPlugin(long id)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var result = await _pluginService.GetPluginAsync(id, appCode);
            if (result == null)
            {
                return Ok(ApiResponse.NotFound("插件不存在"));
            }

            return ApiResponse<PluginDto>.Success(result);
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse.Error(ex.Message));
        }
    }

    /// <summary>
    /// 创建插件
    /// </summary>
    [HttpPost]
    [PermissionAuthorize(Permissions.MANAGE_PLUGIN_SYSTEM)]
    public async Task<ActionResult<ApiResponse<PluginDto>>> CreatePlugin([FromBody] CreatePluginRequest request)
    {
        try
        {
            var result = await _pluginService.CreatePluginAsync(request);
            return ApiResponse<PluginDto>.Success(result, "插件创建成功");
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
    /// 更新插件
    /// </summary>
    [HttpPut("{id}")]
    [PermissionAuthorize(Permissions.MANAGE_PLUGIN_SYSTEM)]
    public async Task<ActionResult<ApiResponse<PluginDto>>> UpdatePlugin(long id, [FromBody] UpdatePluginRequest request)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var result = await _pluginService.UpdatePluginAsync(id, request, appCode);
            return ApiResponse<PluginDto>.Success(result, "插件更新成功");
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
    /// 删除插件
    /// </summary>
    [HttpDelete("{id}")]
    [PermissionAuthorize(Permissions.MANAGE_PLUGIN_SYSTEM)]
    public async Task<ActionResult<ApiResponse>> DeletePlugin(long id)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            await _pluginService.DeletePluginAsync(id, appCode);
            return ApiResponse.Success("插件删除成功");
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
    /// 启动插件
    /// </summary>
    [HttpPost("{id}/start")]
    [PermissionAuthorize(Permissions.MANAGE_PLUGIN_SYSTEM)]
    public async Task<ActionResult<ApiResponse>> StartPlugin(long id)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            await _pluginService.StartPluginAsync(id, appCode);
            return ApiResponse.Success("插件启动成功");
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
    /// 停止插件
    /// </summary>
    [HttpPost("{id}/stop")]
    [PermissionAuthorize(Permissions.MANAGE_PLUGIN_SYSTEM)]
    public async Task<ActionResult<ApiResponse>> StopPlugin(long id)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            await _pluginService.StopPluginAsync(id, appCode);
            return ApiResponse.Success("插件停止成功");
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
