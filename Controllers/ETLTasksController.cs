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
/// ETL任务管理控制器
/// </summary>
[ApiController]
[Route("api/v1/etl-tasks")]
[PermissionAuthorize(Permissions.MANAGE_DATA_CENTER)]
public class ETLTasksController : ControllerBase
{
    private readonly IETLTaskService _etlTaskService;
    private readonly ICustomerScopeService? _customerScope;

    public ETLTasksController(IETLTaskService etlTaskService, ICustomerScopeService? customerScope = null)
    {
        _etlTaskService = etlTaskService;
        _customerScope = customerScope;
    }

    /// <summary>下推给 Service 双保险的范围；缺少范围服务时返回 null，由守卫 fail-closed。</summary>
    private async Task<CustomerScope?> ResolveScopeAsync() =>
        _customerScope == null ? null : await _customerScope.ResolveAsync(User);

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
            var listScope = await this.ResolveListScopeAsync(_customerScope);
            var result = await _etlTaskService.GetETLTasksAsync(page, pageSize, keyword, taskType, appCode, listScope.CustomerIds, listScope.AppCodes);
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
            // B4/M6：先点查权威归属并按 scope 三分支判定；越权 HTTP 200 + Code=403。
            if (!await this.CanReadDetailAsync<IoTPlatform.Models.ETLTask>(id, false))
            {
                return Ok(ApiResponse<ETLTaskDto>.Fail(403, "无权访问该记录"));
            }

        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var result = await _etlTaskService.GetETLTaskAsync(id, appCode);
            if (result == null)
            {
                var response = ApiResponse.NotFound("ETL任务不存在");
            return Ok(response);
            }

            return ApiResponse<ETLTaskDto>.Success(result);
        }
        catch (Exception ex)
        {
            var response = ApiResponse.Error(ex.Message);
            return Ok(response);
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
            var target = await WriteTargetResolver.ResolveAsync(_customerScope, User, request.AppCode);
            if (!target.Allowed)
            {
                return Ok(target.ErrorCode == 400
                    ? ApiResponse.BadRequest(target.ErrorMessage)
                    : ApiResponse.Forbidden(target.ErrorMessage));
            }

            var result = await _etlTaskService.CreateETLTaskAsync(request, await ResolveScopeAsync());
            return ApiResponse<ETLTaskDto>.Success(result, "ETL任务创建成功");
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
    /// 更新ETL任务
    /// </summary>
    [HttpPut("{id}")]
    [PermissionAuthorize(Permissions.MANAGE_DATA_CENTER)]
    public async Task<ActionResult<ApiResponse<ETLTaskDto>>> UpdateETLTask(long id, [FromBody] UpdateETLTaskRequest request)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var result = await _etlTaskService.UpdateETLTaskAsync(id, request, appCode, await ResolveScopeAsync());
            return ApiResponse<ETLTaskDto>.Success(result, "ETL任务更新成功");
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
    /// 删除ETL任务
    /// </summary>
    [HttpDelete("{id}")]
    [PermissionAuthorize(Permissions.MANAGE_DATA_CENTER)]
    public async Task<ActionResult<ApiResponse>> DeleteETLTask(long id)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            await _etlTaskService.DeleteETLTaskAsync(id, appCode, await ResolveScopeAsync());
            return ApiResponse.Success("ETL任务删除成功");
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
    /// 启动ETL任务
    /// </summary>
    [HttpPost("{id}/start")]
    [PermissionAuthorize(Permissions.MANAGE_DATA_CENTER)]
    public async Task<ActionResult<ApiResponse>> StartETLTask(long id)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            await _etlTaskService.StartETLTaskAsync(id, appCode, await ResolveScopeAsync());
            return ApiResponse.Success("ETL任务启动成功");
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
    /// 停止ETL任务
    /// </summary>
    [HttpPost("{id}/stop")]
    [PermissionAuthorize(Permissions.MANAGE_DATA_CENTER)]
    public async Task<ActionResult<ApiResponse>> StopETLTask(long id)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            await _etlTaskService.StopETLTaskAsync(id, appCode, await ResolveScopeAsync());
            return ApiResponse.Success("ETL任务停止成功");
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
