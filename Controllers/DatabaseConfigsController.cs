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
/// 数据库配置管理控制器
/// </summary>
[ApiController]
[Route("api/v1/database-configs")]
[PermissionAuthorize(Permissions.MANAGE_DATABASE_CONFIG)]
public class DatabaseConfigsController : ControllerBase
{
    private readonly IDatabaseConfigService _databaseConfigService;

    public DatabaseConfigsController(IDatabaseConfigService databaseConfigService)
    {
        _databaseConfigService = databaseConfigService;
    }

    /// <summary>
    /// 获取数据库配置列表
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResponse<DatabaseConfigDto>>>> GetDatabaseConfigs(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? keyword = null,
        [FromQuery] string? databaseType = null)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var result = await _databaseConfigService.GetDatabaseConfigsAsync(page, pageSize, keyword, databaseType, appCode);
            return ApiResponse<PagedResponse<DatabaseConfigDto>>.Success(result);
        }
        catch (Exception ex)
        {
            return ApiResponse<PagedResponse<DatabaseConfigDto>>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 获取数据库配置详情
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<DatabaseConfigDto>>> GetDatabaseConfig(long id)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var result = await _databaseConfigService.GetDatabaseConfigAsync(id, appCode);
            if (result == null)
            {
                return Ok(ApiResponse.NotFound("数据库配置不存在"));
            }

            return ApiResponse<DatabaseConfigDto>.Success(result);
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse.Error(ex.Message));
        }
    }

    /// <summary>
    /// 创建数据库配置
    /// </summary>
    [HttpPost]
    [PermissionAuthorize(Permissions.MANAGE_DATABASE_CONFIG)]
    public async Task<ActionResult<ApiResponse<DatabaseConfigDto>>> CreateDatabaseConfig([FromBody] CreateDatabaseConfigRequest request)
    {
        try
        {
            var result = await _databaseConfigService.CreateDatabaseConfigAsync(request);
            return ApiResponse<DatabaseConfigDto>.Success(result, "数据库配置创建成功");
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
    /// 更新数据库配置
    /// </summary>
    [HttpPut("{id}")]
    [PermissionAuthorize(Permissions.MANAGE_DATABASE_CONFIG)]
    public async Task<ActionResult<ApiResponse<DatabaseConfigDto>>> UpdateDatabaseConfig(long id, [FromBody] UpdateDatabaseConfigRequest request)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var result = await _databaseConfigService.UpdateDatabaseConfigAsync(id, request, appCode);
            return ApiResponse<DatabaseConfigDto>.Success(result, "数据库配置更新成功");
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
    /// 删除数据库配置
    /// </summary>
    [HttpDelete("{id}")]
    [PermissionAuthorize(Permissions.MANAGE_DATABASE_CONFIG)]
    public async Task<ActionResult<ApiResponse>> DeleteDatabaseConfig(long id)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            await _databaseConfigService.DeleteDatabaseConfigAsync(id, appCode);
            return ApiResponse.Success("数据库配置删除成功");
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
    /// 测试数据库连接
    /// </summary>
    [HttpPost("test-connection")]
    [PermissionAuthorize(Permissions.MANAGE_DATABASE_CONFIG)]
    public async Task<ActionResult<ApiResponse<bool>>> TestConnection([FromBody] TestDatabaseConnectionRequest request)
    {
        try
        {
            var result = await _databaseConfigService.TestConnectionAsync(request);
            if (result)
            {
                return ApiResponse<bool>.Success(result, "数据库连接成功");
            }
            else
            {
                return Ok(ApiResponse<bool>.Fail(400, "数据库连接失败"));
            }
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<bool>.Fail(400, $"数据库连接失败: {ex.Message}"));
        }
    }

    /// <summary>
    /// 测试已有配置的连接
    /// </summary>
    [HttpPost("{id}/test-connection")]
    [PermissionAuthorize(Permissions.MANAGE_DATABASE_CONFIG)]
    public async Task<ActionResult<ApiResponse<bool>>> TestConnectionById(long id)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var result = await _databaseConfigService.TestConnectionByIdAsync(id, appCode);
            if (result)
            {
                return ApiResponse<bool>.Success(result, "数据库连接成功");
            }
            else
            {
                return Ok(ApiResponse<bool>.Fail(400, "数据库连接失败"));
            }
        }
        catch (InvalidOperationException ex)
        {
            return Ok(ApiResponse<bool>.Fail(400, ex.Message));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<bool>.Fail(400, $"数据库连接失败: {ex.Message}"));
        }
    }
}
