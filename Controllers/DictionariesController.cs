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
/// 字典管理控制器
/// </summary>
[ApiController]
[Route("api/v1/dictionaries")]
[PermissionAuthorize(Permissions.VIEW_DICTIONARY)]
public class DictionariesController : ControllerBase
{
    private readonly IDictionaryService _dictionaryService;

    public DictionariesController(IDictionaryService dictionaryService)
    {
        _dictionaryService = dictionaryService;
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
            var appCode = User.FindFirst("AppCode")?.Value;
            var result = await _dictionaryService.GetDictionaryTypesAsync(page, pageSize, keyword, appCode);
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
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var result = await _dictionaryService.GetDictionaryTypeAsync(id, appCode);
            if (result == null)
            {
                return Ok(ApiResponse<DictionaryTypeDto>.NotFound("字典类型不存在"));
            }

            return ApiResponse<DictionaryTypeDto>.Success(result);
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<DictionaryTypeDto>.Error(ex.Message));
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
            var result = await _dictionaryService.CreateDictionaryTypeAsync(request);
            return ApiResponse<DictionaryTypeDto>.Success(result, "字典类型创建成功");
        }
        catch (InvalidOperationException ex)
        {
            return Ok(ApiResponse<DictionaryTypeDto>.BadRequest(ex.Message));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<DictionaryTypeDto>.Error(ex.Message));
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
            var appCode = User.FindFirst("AppCode")?.Value;
            var result = await _dictionaryService.UpdateDictionaryTypeAsync(id, request, appCode);
            return ApiResponse<DictionaryTypeDto>.Success(result, "字典类型更新成功");
        }
        catch (InvalidOperationException ex)
        {
            return Ok(ApiResponse<DictionaryTypeDto>.BadRequest(ex.Message));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Ok(ApiResponse<DictionaryTypeDto>.Forbidden(ex.Message));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<DictionaryTypeDto>.Error(ex.Message));
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
            var appCode = User.FindFirst("AppCode")?.Value;
            await _dictionaryService.DeleteDictionaryTypeAsync(id, appCode);
            return ApiResponse.Success("字典类型删除成功");
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
            var appCode = User.FindFirst("AppCode")?.Value;
            var result = await _dictionaryService.GetDictionaryItemsAsync(type, appCode);
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
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var result = await _dictionaryService.GetDictionaryItemAsync(id, appCode);
            if (result == null)
            {
                return Ok(ApiResponse<DictionaryItemDto>.NotFound("字典项不存在"));
            }

            return ApiResponse<DictionaryItemDto>.Success(result);
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<DictionaryItemDto>.Error(ex.Message));
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
            var result = await _dictionaryService.CreateDictionaryItemAsync(request);
            return ApiResponse<DictionaryItemDto>.Success(result, "字典项创建成功");
        }
        catch (InvalidOperationException ex)
        {
            return Ok(ApiResponse<DictionaryItemDto>.BadRequest(ex.Message));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<DictionaryItemDto>.Error(ex.Message));
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
            var appCode = User.FindFirst("AppCode")?.Value;
            var result = await _dictionaryService.UpdateDictionaryItemAsync(id, request, appCode);
            return ApiResponse<DictionaryItemDto>.Success(result, "字典项更新成功");
        }
        catch (InvalidOperationException ex)
        {
            return Ok(ApiResponse<DictionaryItemDto>.BadRequest(ex.Message));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Ok(ApiResponse<DictionaryItemDto>.Forbidden(ex.Message));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<DictionaryItemDto>.Error(ex.Message));
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
            var appCode = User.FindFirst("AppCode")?.Value;
            await _dictionaryService.DeleteDictionaryItemAsync(id, appCode);
            return ApiResponse.Success("字典项删除成功");
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

    #endregion
}
