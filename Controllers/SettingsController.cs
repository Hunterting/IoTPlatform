using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Filters;
using IoTPlatform.Helpers;
using IoTPlatform.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace IoTPlatform.Controllers;

/// <summary>
/// 系统设置控制器
/// </summary>
[ApiController]
[Route("api/v1/settings")]
[PermissionAuthorize(Permissions.VIEW_SETTINGS)]
public class SettingsController : ControllerBase
{
    private readonly ISettingsService _settingsService;

    public SettingsController(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    /// <summary>
    /// 获取系统设置列表
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResponse<SettingDto>>>> GetSettings(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? category = null,
        [FromQuery] string? keyword = null)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var result = await _settingsService.GetSettingsAsync(page, pageSize, category, keyword, appCode);
            return ApiResponse<PagedResponse<SettingDto>>.Success(result);
        }
        catch (Exception ex)
        {
            return ApiResponse<PagedResponse<SettingDto>>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 根据键获取设置
    /// </summary>
    [HttpGet("key/{key}")]
    public async Task<ActionResult<ApiResponse<SettingDto>>> GetSetting(string key)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var result = await _settingsService.GetSettingAsync(key, appCode);
            if (result == null)
            {
                return ApiResponse<SettingDto>.NotFound("设置不存在");
            }

            return ApiResponse<SettingDto>.Success(result);
        }
        catch (Exception ex)
        {
            return ApiResponse<SettingDto>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 根据分类获取设置
    /// </summary>
    [HttpGet("category/{category}")]
    public async Task<ActionResult<ApiResponse<Dictionary<string, string?>>>> GetSettingsByCategory(string category)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var result = await _settingsService.GetSettingsByCategoryAsync(category, appCode);
            return ApiResponse<Dictionary<string, string?>>.Success(result);
        }
        catch (Exception ex)
        {
            return ApiResponse<Dictionary<string, string?>>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 创建系统设置
    /// </summary>
    [HttpPost]
    [PermissionAuthorize(Permissions.UPDATE_SETTINGS)]
    public async Task<ActionResult<ApiResponse<SettingDto>>> CreateSetting([FromBody] CreateSettingRequest request)
    {
        try
        {
            var result = await _settingsService.CreateSettingAsync(request);
            return ApiResponse<SettingDto>.Success(result, "设置创建成功");
        }
        catch (InvalidOperationException ex)
        {
            return ApiResponse<SettingDto>.BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            return ApiResponse<SettingDto>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 更新系统设置
    /// </summary>
    [HttpPut("key/{key}")]
    [PermissionAuthorize(Permissions.UPDATE_SETTINGS)]
    public async Task<ActionResult<ApiResponse<SettingDto>>> UpdateSetting(string key, [FromBody] UpdateSettingRequest request)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var result = await _settingsService.UpdateSettingAsync(key, request, appCode);
            return ApiResponse<SettingDto>.Success(result, "设置更新成功");
        }
        catch (InvalidOperationException ex)
        {
            return ApiResponse<SettingDto>.BadRequest(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return ApiResponse<SettingDto>.Forbidden(ex.Message);
        }
        catch (Exception ex)
        {
            return ApiResponse<SettingDto>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 删除系统设置
    /// </summary>
    [HttpDelete("key/{key}")]
    [PermissionAuthorize(Permissions.UPDATE_SETTINGS)]
    public async Task<ActionResult<ApiResponse>> DeleteSetting(string key)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            await _settingsService.DeleteSettingAsync(key, appCode);
            return ApiResponse.Success("设置删除成功");
        }
        catch (InvalidOperationException ex)
        {
            return ApiResponse.BadRequest(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return ApiResponse.Forbidden(ex.Message);
        }
        catch (Exception ex)
        {
            return ApiResponse.Error(ex.Message);
        }
    }
}
