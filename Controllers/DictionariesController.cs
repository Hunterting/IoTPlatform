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
/// 字典管理控制器
/// </summary>
[ApiController]
[Route("api/v1/dictionaries")]
[PermissionAuthorize(Permissions.VIEW_DICTIONARY)]
public class DictionariesController : ControllerBase
{
    private readonly IDictionaryService _dictionaryService;
    private readonly ICustomerScopeService? _customerScope;

    public DictionariesController(
        IDictionaryService dictionaryService,
        ICustomerScopeService? customerScope = null)
    {
        _dictionaryService = dictionaryService;
        _customerScope = customerScope;
    }

    /// <summary>下推给 Service 双保险的范围；缺少范围服务时返回 null，由守卫 fail-closed。</summary>
    private async Task<CustomerScope?> ResolveScopeAsync() =>
        _customerScope == null ? null : await _customerScope.ResolveAsync(User);

    /// <summary>
    /// 租户过滤码：普通租户取自身 AppCode；超管（无 AppCode / system）返回 null 表示看全部。
    /// </summary>
    private string? TenantAppCode
    {
        get
        {
            var c = User.FindFirst("AppCode")?.Value;
            return string.IsNullOrEmpty(c) || c == "system" ? null : c;
        }
    }

    #region 字典类型管理

    /// <summary>
    /// 获取字典类型列表
    /// </summary>
    [HttpGet("types")]
    public async Task<ActionResult<ApiResponse<PagedResponse<DictionaryTypeDto>>>> GetDictionaryTypes(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? keyword = null)
    {
        try
        {
            var appCode = TenantAppCode;
            var listScope = await this.ResolveListScopeAsync(_customerScope);
            var result = await _dictionaryService.GetDictionaryTypesAsync(page, pageSize, keyword, appCode, listScope.CustomerIds, listScope.AppCodes);
            return ApiResponse<PagedResponse<DictionaryTypeDto>>.Success(result);
        }
        catch (Exception ex)
        {
            return ApiResponse<PagedResponse<DictionaryTypeDto>>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 获取字典类型详情
    /// </summary>
    [HttpGet("types/{id}")]
    public async Task<ActionResult<ApiResponse<DictionaryTypeDto>>> GetDictionaryType(long id)
    {
            // B4/M6：先点查权威归属并按 scope 三分支判定；越权 HTTP 200 + Code=403。
            if (!await this.CanReadDetailAsync<IoTPlatform.Models.DictionaryTypeConfig>(id, true))
            {
                return Ok(ApiResponse<DictionaryTypeDto>.Fail(403, "无权访问该记录"));
            }

        try
        {
            var appCode = TenantAppCode;
            var result = await _dictionaryService.GetDictionaryTypeAsync(id, appCode);
            if (result == null)
            {
                var response = ApiResponse.NotFound("字典类型不存在");
            return Ok(response);
            }

            return ApiResponse<DictionaryTypeDto>.Success(result);
        }
        catch (Exception ex)
        {
            var response = ApiResponse.Error(ex.Message);
            return Ok(response);
        }
    }

    /// <summary>
    /// 创建字典类型
    /// </summary>
    [HttpPost("types")]
    [PermissionAuthorize(Permissions.CREATE_DICTIONARY)]
    public async Task<ActionResult<ApiResponse<DictionaryTypeDto>>> CreateDictionaryType([FromBody] CreateDictionaryTypeRequest request)
    {
        try
        {
            // M2：请求体 AppCode 仅为待校验目标；admin 走 DB 授权集合，归属由服务端派生。
            var target = await WriteTargetResolver.ResolveAsync(_customerScope, User, request.AppCode);
            if (!target.Allowed)
            {
                return Ok(target.ErrorCode == 400
                    ? ApiResponse.BadRequest(target.ErrorMessage)
                    : ApiResponse.Forbidden(target.ErrorMessage));
            }

            var result = await _dictionaryService.CreateDictionaryTypeAsync(request, target.AppCode, await ResolveScopeAsync());
            return ApiResponse<DictionaryTypeDto>.Success(result, "字典类型创建成功");
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
    /// 更新字典类型
    /// </summary>
    [HttpPut("types/{id}")]
    [PermissionAuthorize(Permissions.UPDATE_DICTIONARY)]
    public async Task<ActionResult<ApiResponse<DictionaryTypeDto>>> UpdateDictionaryType(long id, [FromBody] UpdateDictionaryTypeRequest request)
    {
        try
        {
            var appCode = TenantAppCode;
            var result = await _dictionaryService.UpdateDictionaryTypeAsync(id, request, appCode, await ResolveScopeAsync());
            return ApiResponse<DictionaryTypeDto>.Success(result, "字典类型更新成功");
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
    /// 删除字典类型
    /// </summary>
    [HttpDelete("types/{id}")]
    [PermissionAuthorize(Permissions.DELETE_DICTIONARY)]
    public async Task<ActionResult<ApiResponse>> DeleteDictionaryType(long id)
    {
        try
        {
            var appCode = TenantAppCode;
            await _dictionaryService.DeleteDictionaryTypeAsync(id, appCode, await ResolveScopeAsync());
            return ApiResponse.Success("字典类型删除成功");
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

    #endregion

    #region 字典项管理

    /// <summary>
    /// 根据类型获取字典项列表
    /// </summary>
    [HttpGet("items")]
    public async Task<ActionResult<ApiResponse<List<DictionaryItemDto>>>> GetDictionaryItems([FromQuery] string type)
    {
        try
        {
            var appCode = TenantAppCode;
            var listScope = await this.ResolveListScopeAsync(_customerScope);
            var result = await _dictionaryService.GetDictionaryItemsAsync(type, appCode, listScope.CustomerIds, listScope.AppCodes);
            return ApiResponse<List<DictionaryItemDto>>.Success(result);
        }
        catch (Exception ex)
        {
            return ApiResponse<List<DictionaryItemDto>>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 获取字典项详情
    /// </summary>
    [HttpGet("items/{id}")]
    public async Task<ActionResult<ApiResponse<DictionaryItemDto>>> GetDictionaryItem(long id)
    {
            // B4/M6：先点查权威归属并按 scope 三分支判定；越权 HTTP 200 + Code=403。
            if (!await this.CanReadDetailAsync<IoTPlatform.Models.DictionaryItem>(id, true))
            {
                return Ok(ApiResponse<DictionaryItemDto>.Fail(403, "无权访问该记录"));
            }

        try
        {
            var appCode = TenantAppCode;
            var result = await _dictionaryService.GetDictionaryItemAsync(id, appCode);
            if (result == null)
            {
                var response = ApiResponse.NotFound("字典项不存在");
            return Ok(response);
            }

            return ApiResponse<DictionaryItemDto>.Success(result);
        }
        catch (Exception ex)
        {
            var response = ApiResponse.Error(ex.Message);
            return Ok(response);
        }
    }

    /// <summary>
    /// 创建字典项
    /// </summary>
    [HttpPost("items")]
    [PermissionAuthorize(Permissions.CREATE_DICTIONARY)]
    public async Task<ActionResult<ApiResponse<DictionaryItemDto>>> CreateDictionaryItem([FromBody] CreateDictionaryItemRequest request)
    {
        try
        {
            // M2：请求体 AppCode 仅为待校验目标；admin 走 DB 授权集合，归属由服务端派生。
            var target = await WriteTargetResolver.ResolveAsync(_customerScope, User, request.AppCode);
            if (!target.Allowed)
            {
                return Ok(target.ErrorCode == 400
                    ? ApiResponse.BadRequest(target.ErrorMessage)
                    : ApiResponse.Forbidden(target.ErrorMessage));
            }

            var result = await _dictionaryService.CreateDictionaryItemAsync(request, target.AppCode, await ResolveScopeAsync());
            return ApiResponse<DictionaryItemDto>.Success(result, "字典项创建成功");
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
    /// 更新字典项
    /// </summary>
    [HttpPut("items/{id}")]
    [PermissionAuthorize(Permissions.UPDATE_DICTIONARY)]
    public async Task<ActionResult<ApiResponse<DictionaryItemDto>>> UpdateDictionaryItem(long id, [FromBody] UpdateDictionaryItemRequest request)
    {
        try
        {
            var appCode = TenantAppCode;
            var result = await _dictionaryService.UpdateDictionaryItemAsync(id, request, appCode, await ResolveScopeAsync());
            return ApiResponse<DictionaryItemDto>.Success(result, "字典项更新成功");
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
    /// 删除字典项
    /// </summary>
    [HttpDelete("items/{id}")]
    [PermissionAuthorize(Permissions.DELETE_DICTIONARY)]
    public async Task<ActionResult<ApiResponse>> DeleteDictionaryItem(long id)
    {
        try
        {
            var appCode = TenantAppCode;
            await _dictionaryService.DeleteDictionaryItemAsync(id, appCode, await ResolveScopeAsync());
            return ApiResponse.Success("字典项删除成功");
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

    #endregion
}
