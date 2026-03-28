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
/// 档案管理控制器
/// </summary>
[ApiController]
[Route("api/v1/archives")]
[PermissionAuthorize(Permissions.VIEW_ARCHIVES)]
public class ArchivesController : ControllerBase
{
    private readonly IArchiveService _archiveService;

    public ArchivesController(IArchiveService archiveService)
    {
        _archiveService = archiveService;
    }

    /// <summary>
    /// 获取档案列表
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResponse<ArchiveDto>>>> GetArchives(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? keyword = null,
        [FromQuery] string? type = null,
        [FromQuery] long? areaId = null)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var result = await _archiveService.GetArchivesAsync(page, pageSize, keyword, type, areaId, appCode);
            return ApiResponse<PagedResponse<ArchiveDto>>.Success(result);
        }
        catch (Exception ex)
        {
            return ApiResponse<PagedResponse<ArchiveDto>>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 获取档案详情
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<ArchiveDto>>> GetArchive(long id)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var result = await _archiveService.GetArchiveAsync(id, appCode);
            if (result == null)
            {
                var response = ApiResponse.NotFound("档案不存在");
            return Ok(response);
            }

            return ApiResponse<ArchiveDto>.Success(result);
        }
        catch (Exception ex)
        {
            var response = ApiResponse.Error(ex.Message);
            return Ok(response);
        }
    }

    /// <summary>
    /// 创建档案
    /// </summary>
    [HttpPost]
    [PermissionAuthorize(Permissions.CREATE_ARCHIVES)]
    public async Task<ActionResult<ApiResponse<ArchiveDto>>> CreateArchive([FromBody] CreateArchiveRequest request)
    {
        try
        {
            var result = await _archiveService.CreateArchiveAsync(request);
            return ApiResponse<ArchiveDto>.Success(result, "档案创建成功");
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
    /// 更新档案
    /// </summary>
    [HttpPut("{id}")]
    [PermissionAuthorize(Permissions.UPDATE_ARCHIVES)]
    public async Task<ActionResult<ApiResponse<ArchiveDto>>> UpdateArchive(long id, [FromBody] UpdateArchiveRequest request)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var result = await _archiveService.UpdateArchiveAsync(id, request, appCode);
            return ApiResponse<ArchiveDto>.Success(result, "档案更新成功");
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
    /// 删除档案
    /// </summary>
    [HttpDelete("{id}")]
    [PermissionAuthorize(Permissions.DELETE_ARCHIVES)]
    public async Task<ActionResult<ApiResponse>> DeleteArchive(long id)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            await _archiveService.DeleteArchiveAsync(id, appCode);
            return ApiResponse.Success("档案删除成功");
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
    /// 获取档案设备标记
    /// </summary>
    [HttpGet("{id}/markers")]
    public async Task<ActionResult<ApiResponse<List<ArchiveDeviceMarkerDto>>>> GetArchiveMarkers(long id)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var result = await _archiveService.GetArchiveMarkersAsync(id, appCode);
            return ApiResponse<List<ArchiveDeviceMarkerDto>>.Success(result);
        }
        catch (InvalidOperationException ex)
        {
            return ApiResponse<List<ArchiveDeviceMarkerDto>>.BadRequest(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return ApiResponse<List<ArchiveDeviceMarkerDto>>.Forbidden(ex.Message);
        }
        catch (Exception ex)
        {
            return ApiResponse<List<ArchiveDeviceMarkerDto>>.Error(ex.Message);
        }
    }
}
