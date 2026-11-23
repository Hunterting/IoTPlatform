using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Filters;
using IoTPlatform.Helpers;
using IoTPlatform.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using IoTPlatform.Configuration;
using IoTPlatform.Services.Guards;

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
    private readonly ICustomerScopeService? _customerScope;

    public SettingsController(ISettingsService settingsService, ICustomerScopeService? customerScope = null)
    {
        _settingsService = settingsService;
        _customerScope = customerScope;
    }

    /// <summary>下推给 Service 双保险的范围；缺少范围服务时返回 null，由守卫 fail-closed。</summary>
    private async Task<CustomerScope?> ResolveScopeAsync() =>
        _customerScope == null ? null : await _customerScope.ResolveAsync(User);

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
            var listScope = await this.ResolveListScopeAsync(_customerScope);
            var result = await _settingsService.GetSettingsAsync(page, pageSize, category, keyword, appCode, listScope.CustomerIds, listScope.AppCodes);
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
            // B4/M6 + PII：设置值可能含连接串/密钥，先按 Key 点查归属再返回。
            if (!await this.CanReadDetailByKeyAsync<IoTPlatform.Models.SystemSetting>(key, allowShared: true))
            {
                return Ok(ApiResponse<SettingDto>.Fail(403, "无权访问该设置"));
            }

            var appCode = User.FindFirst("AppCode")?.Value;
            var result = await _settingsService.GetSettingAsync(key, appCode);
            if (result == null)
            {
                var response = ApiResponse.NotFound("设置不存在");
            return Ok(response);
            }

            return ApiResponse<SettingDto>.Success(result);
        }
        catch (Exception ex)
        {
            var response = ApiResponse.Error(ex.Message);
            return Ok(response);
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
            var listScope = await this.ResolveListScopeAsync(_customerScope);
            var result = await _settingsService.GetSettingsByCategoryAsync(
                category, appCode, listScope.CustomerIds, listScope.AppCodes);
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
            var target = await WriteTargetResolver.ResolveAsync(_customerScope, User, request.AppCode);
            if (!target.Allowed)
            {
                return Ok(target.ErrorCode == 400
                    ? ApiResponse.BadRequest(target.ErrorMessage)
                    : ApiResponse.Forbidden(target.ErrorMessage));
            }

            var result = await _settingsService.CreateSettingAsync(request, await ResolveScopeAsync());
            return ApiResponse<SettingDto>.Success(result, "设置创建成功");
        }
        catch (ForbiddenAccessException ex)
        {
            return Ok(ApiResponse.Forbidden(ex.Message));
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
    /// 更新系统设置
    /// </summary>
    [HttpPut("key/{key}")]
    [PermissionAuthorize(Permissions.UPDATE_SETTINGS)]
    public async Task<ActionResult<ApiResponse<SettingDto>>> UpdateSetting(string key, [FromBody] UpdateSettingRequest request)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var result = await _settingsService.UpdateSettingAsync(key, request, appCode, await ResolveScopeAsync());
            return ApiResponse<SettingDto>.Success(result, "设置更新成功");
        }
        catch (ForbiddenAccessException ex)
        {
            return Ok(ApiResponse.Forbidden(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            var response = ApiResponse.BadRequest(ex.Message);
            return Ok(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            var response = ApiResponse.Forbidden(ex.Message);
            return Ok(response);
        }
        catch (Exception ex)
        {
            var response = ApiResponse.Error(ex.Message);
            return Ok(response);
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
            await _settingsService.DeleteSettingAsync(key, appCode, await ResolveScopeAsync());
            return ApiResponse.Success("设置删除成功");
        }
        catch (ForbiddenAccessException ex)
        {
            return Ok(ApiResponse.Forbidden(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            var response = ApiResponse.BadRequest(ex.Message);
            return Ok(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            var response = ApiResponse.Forbidden(ex.Message);
            return Ok(response);
        }
        catch (Exception ex)
        {
            var response = ApiResponse.Error(ex.Message);
            return Ok(response);
        }
    }
}
