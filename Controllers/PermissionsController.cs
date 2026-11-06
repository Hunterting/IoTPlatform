using IoTPlatform.Configuration;
using IoTPlatform.Helpers;
using IoTPlatform.Services;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace IoTPlatform.Controllers;

/// <summary>
/// 权限控制器
/// </summary>
[ApiController]
[Route("api/v1/permissions")]
public class PermissionsController : ControllerBase
{
    private readonly IPermissionService _permissionService;

    public PermissionsController(IPermissionService permissionService)
    {
        _permissionService = permissionService;
    }

    /// <summary>
    /// 获取当前用户的权限列表
    /// </summary>
    [HttpGet("current")]
    public async Task<ActionResult<ApiResponse<object>>> GetCurrentUserPermissions()
    {
        try
        {
            var role = User.FindFirst(ClaimTypes.Role)?.Value;

            if (string.IsNullOrEmpty(role))
            {
                return ApiResponse<object>.Unauthorized("未获取到用户角色");
            }

            // 统一解析有效权限（单一事实来源 = DB Role.Permissions）：
            // 超管→全量；自定义/内置角色→跨租户解析 DB 权限 JSON 或回退静态表。
            var permissions = await _permissionService.GetEffectivePermissionsAsync(role);

            return ApiResponse<object>.Success(new
            {
                Role = role,
                Permissions = permissions,
                IsSuperAdmin = Roles.IsSuperAdmin(role)
            });
        }
        catch (Exception ex)
        {
            return ApiResponse<object>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 获取所有角色及其权限
    /// </summary>
    [HttpGet("roles")]
    public ActionResult<ApiResponse<object>> GetAllRolePermissions()
    {
        try
        {
            var result = _permissionService.GetAllRolePermissions()
                .ToDictionary(
                    kvp => kvp.Key,
                    kvp => (object)new
                    {
                        Role = kvp.Key,
                        PermissionCount = kvp.Value.Count,
                        Permissions = kvp.Value
                    });

            return ApiResponse<object>.Success(result);
        }
        catch (Exception ex)
        {
            return ApiResponse<object>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 获取所有权限定义
    /// </summary>
    [HttpGet("definitions")]
    public ActionResult<ApiResponse<object>> GetPermissionDefinitions()
    {
        try
        {
            var result = new
            {
                Permissions = _permissionService.GetPermissionDescriptions(),
                TotalCount = _permissionService.GetAllPermissionCodes().Count
            };

            return ApiResponse<object>.Success(result);
        }
        catch (Exception ex)
        {
            return ApiResponse<object>.Error(ex.Message);
        }
    }
}
