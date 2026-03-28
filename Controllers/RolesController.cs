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
/// 角色管理控制器
/// </summary>
[ApiController]
[Route("api/v1/roles")]
[PermissionAuthorize(Permissions.VIEW_ROLES)]
public class RolesController : ControllerBase
{
    private readonly IRoleService _roleService;

    public RolesController(IRoleService roleService)
    {
        _roleService = roleService;
    }

    /// <summary>
    /// 获取角色列表
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResponse<RoleDto>>>> GetRoles(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? keyword = null)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var role = User.FindFirst(ClaimTypes.Role)?.Value;

            var result = await _roleService.GetRolesAsync(page, pageSize, keyword, appCode, role);
            return ApiResponse<PagedResponse<RoleDto>>.Success(result);
        }
        catch (Exception ex)
        {
            return ApiResponse<PagedResponse<RoleDto>>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 获取角色详情
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<RoleDto>>> GetRole(long id)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var role = User.FindFirst(ClaimTypes.Role)?.Value;

            var result = await _roleService.GetRoleAsync(id, appCode, role);
            if (result == null)
            {
                return Ok(ApiResponse<RoleDto>.NotFound("角色不存在");
            }

            return ApiResponse<RoleDto>.Success(result);
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<RoleDto>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 创建角色
    /// </summary>
    [HttpPost]
    [PermissionAuthorize(Permissions.CREATE_ROLES)]
    public async Task<ActionResult<ApiResponse<RoleDto>>> CreateRole([FromBody] CreateRoleRequest request)
    {
        try
        {
            var result = await _roleService.CreateRoleAsync(request);
            return ApiResponse<RoleDto>.Success(result, "角色创建成功");
        }
        catch (InvalidOperationException ex)
        {
            return Ok(ApiResponse<RoleDto>.BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<RoleDto>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 更新角色
    /// </summary>
    [HttpPut("{id}")]
    [PermissionAuthorize(Permissions.UPDATE_ROLES)]
    public async Task<ActionResult<ApiResponse<RoleDto>>> UpdateRole(long id, [FromBody] UpdateRoleRequest request)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var role = User.FindFirst(ClaimTypes.Role)?.Value;

            var result = await _roleService.UpdateRoleAsync(id, request, appCode, role);
            return ApiResponse<RoleDto>.Success(result, "角色更新成功");
        }
        catch (InvalidOperationException ex)
        {
            return Ok(ApiResponse<RoleDto>.BadRequest(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Ok(ApiResponse<RoleDto>.Forbidden(ex.Message);
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<RoleDto>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 删除角色
    /// </summary>
    [HttpDelete("{id}")]
    [PermissionAuthorize(Permissions.DELETE_ROLES)]
    public async Task<ActionResult<ApiResponse>> DeleteRole(long id)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var role = User.FindFirst(ClaimTypes.Role)?.Value;

            await _roleService.DeleteRoleAsync(id, appCode, role);
            return ApiResponse.Success("角色删除成功");
        }
        catch (InvalidOperationException ex)
        {
            return Ok(ApiResponse.BadRequest(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Ok(ApiResponse.Forbidden(ex.Message);
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse.Error(ex.Message);
        }
    }

    /// <summary>
    /// 获取角色权限列表
    /// </summary>
    [HttpGet("{id}/permissions")]
    public async Task<ActionResult<ApiResponse<List<string>>>> GetRolePermissions(long id)
    {
        try
        {
            var result = await _roleService.GetRolePermissionsAsync(id);
            return ApiResponse<List<string>>.Success(result);
        }
        catch (InvalidOperationException ex)
        {
            return ApiResponse<List<string>>.BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            return ApiResponse<List<string>>.Error(ex.Message);
        }
    }
}
