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
/// 区域管理控制器
/// </summary>
[ApiController]
[Route("api/v1/areas")]
[PermissionAuthorize(Permissions.VIEW_AREAS)]
public class AreasController : ControllerBase
{
    private readonly IAreaService _areaService;

    public AreasController(IAreaService areaService)
    {
        _areaService = areaService;
    }

    /// <summary>
    /// 获取区域列表
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResponse<AreaDto>>>> GetAreas(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? keyword = null)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var role = User.FindFirst(ClaimTypes.Role)?.Value;
            var allowedAreaIds = GetAllowedAreaIds();

            var result = await _areaService.GetAreasAsync(page, pageSize, keyword, appCode, role, allowedAreaIds);
            return ApiResponse<PagedResponse<AreaDto>>.Success(result);
        }
        catch (Exception ex)
        {
            return ApiResponse<PagedResponse<AreaDto>>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 获取区域树
    /// </summary>
    [HttpGet("tree")]
    public async Task<ActionResult<ApiResponse<List<AreaTreeNodeDto>>>> GetAreaTree()
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var role = User.FindFirst(ClaimTypes.Role)?.Value;
            var allowedAreaIds = GetAllowedAreaIds();

            var result = await _areaService.GetAreaTreeAsync(appCode, role, allowedAreaIds);
            return ApiResponse<List<AreaTreeNodeDto>>.Success(result);
        }
        catch (Exception ex)
        {
            return ApiResponse<List<AreaTreeNodeDto>>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 获取子区域列表
    /// </summary>
    [HttpGet("{parentId}/children")]
    public async Task<ActionResult<ApiResponse<List<AreaDto>>>> GetChildAreas(long parentId)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var allowedAreaIds = GetAllowedAreaIds();

            var result = await _areaService.GetChildAreasAsync(parentId, appCode, allowedAreaIds);
            return ApiResponse<List<AreaDto>>.Success(result);
        }
        catch (Exception ex)
        {
            return ApiResponse<List<AreaDto>>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 获取区域详情
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<AreaDto>>> GetArea(long id)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var role = User.FindFirst(ClaimTypes.Role)?.Value;
            var allowedAreaIds = GetAllowedAreaIds();

            var result = await _areaService.GetAreaAsync(id, appCode, role, allowedAreaIds);
            if (result == null)
            {
                return Ok(ApiResponse<AreaDto>.NotFound("区域不存在");
            }

            return ApiResponse<AreaDto>.Success(result);
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<AreaDto>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 创建区域
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ApiResponse<AreaDto>>> CreateArea([FromBody] CreateAreaRequest request)
    {
        try
        {
            var result = await _areaService.CreateAreaAsync(request);
            return ApiResponse<AreaDto>.Success(result, "区域创建成功");
        }
        catch (InvalidOperationException ex)
        {
            return Ok(ApiResponse<AreaDto>.BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<AreaDto>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 更新区域
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResponse<AreaDto>>> UpdateArea(long id, [FromBody] UpdateAreaRequest request)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var role = User.FindFirst(ClaimTypes.Role)?.Value;

            var result = await _areaService.UpdateAreaAsync(id, request, appCode, role);
            return ApiResponse<AreaDto>.Success(result, "区域更新成功");
        }
        catch (InvalidOperationException ex)
        {
            return Ok(ApiResponse<AreaDto>.BadRequest(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Ok(ApiResponse<AreaDto>.Forbidden(ex.Message);
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<AreaDto>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 删除区域
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResponse>> DeleteArea(long id)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var role = User.FindFirst(ClaimTypes.Role)?.Value;

            await _areaService.DeleteAreaAsync(id, appCode, role);
            return ApiResponse.Success("区域删除成功");
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
