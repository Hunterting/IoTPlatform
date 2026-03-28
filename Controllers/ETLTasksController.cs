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
/// ETL任务管理控制器
/// </summary>
[ApiController]
[Route("api/v1/etl-tasks")]
[PermissionAuthorize(Permissions.MANAGE_DATA_CENTER)]
public class ETLTasksController : ControllerBase
{
    private readonly IETLTaskService _etlTaskService;

    public ETLTasksController(IETLTaskService etlTaskService)
    {
        _etlTaskService = etlTaskService;
    }

    /// <summary>
    /// 获取ETL任务列表
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResponse<ETLTaskDto>>>> GetETLTasks(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? keyword = null,
        [FromQuery] string? taskType = null)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var result = await _etlTaskService.GetETLTasksAsync(page, pageSize, keyword, taskType, appCode);
            return ApiResponse<PagedResponse<ETLTaskDto>>.Success(result);
        }
        catch (Exception ex)
        {
            return ApiResponse<PagedResponse<ETLTaskDto>>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 获取ETL任务详情
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<ETLTaskDto>>> GetETLTask(long id)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var result = await _etlTaskService.GetETLTaskAsync(id, appCode);
            if (result == null)
            {
                return Ok(ApiResponse<ETLTaskDto>.NotFound("ETL任务不存在"));
            }

            return ApiResponse<ETLTaskDto>.Success(result);
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<ETLTaskDto>.Error(ex.Message));
        }
    }

    /// <summary>
    /// 创建ETL任务
    /// </summary>
    [HttpPost]
    [PermissionAuthorize(Permissions.MANAGE_DATA_CENTER)]
    public async Task<ActionResult<ApiResponse<ETLTaskDto>>> CreateETLTask([FromBody] CreateETLTaskRequest request)
    {
        try
        {
            var result = await _etlTaskService.CreateETLTaskAsync(request);
            return ApiResponse<ETLTaskDto>.Success(result, "ETL任务创建成功");
        }
        catch (InvalidOperationException ex)
        {
            return Ok(ApiResponse<ETLTaskDto>.BadRequest(ex.Message));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<ETLTaskDto>.Error(ex.Message));
        }
    }

    /// <summary>
    /// 更新ETL任务
    /// </summary>
    [HttpPut("{id}")]
    [PermissionAuthorize(Permissions.MANAGE_DATA_CENTER)]
    public async Task<ActionResult<ApiResponse<ETLTaskDto>>> UpdateETLTask(long id, [FromBody] UpdateETLTaskRequest request)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var result = await _etlTaskService.UpdateETLTaskAsync(id, request, appCode);
            return ApiResponse<ETLTaskDto>.Success(result, "ETL任务更新成功");
        }
        catch (InvalidOperationException ex)
        {
            return Ok(ApiResponse<ETLTaskDto>.BadRequest(ex.Message));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Ok(ApiResponse<ETLTaskDto>.Forbidden(ex.Message));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<ETLTaskDto>.Error(ex.Message));
        }
    }

    /// <summary>
    /// 删除ETL任务
    /// </summary>
    [HttpDelete("{id}")]
    [PermissionAuthorize(Permissions.MANAGE_DATA_CENTER)]
    public async Task<ActionResult<ApiResponse>> DeleteETLTask(long id)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            await _etlTaskService.DeleteETLTaskAsync(id, appCode);
            return ApiResponse.Success("ETL任务删除成功");
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
    /// 启动ETL任务
    /// </summary>
    [HttpPost("{id}/start")]
    [PermissionAuthorize(Permissions.MANAGE_DATA_CENTER)]
    public async Task<ActionResult<ApiResponse>> StartETLTask(long id)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            await _etlTaskService.StartETLTaskAsync(id, appCode);
            return ApiResponse.Success("ETL任务启动成功");
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
    /// 停止ETL任务
    /// </summary>
    [HttpPost("{id}/stop")]
    [PermissionAuthorize(Permissions.MANAGE_DATA_CENTER)]
    public async Task<ActionResult<ApiResponse>> StopETLTask(long id)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            await _etlTaskService.StopETLTaskAsync(id, appCode);
            return ApiResponse.Success("ETL任务停止成功");
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
