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
    public ActionResult<ApiResponse<object>> GetCurrentUserPermissions()
    {
        try
        {
            var role = User.FindFirst(ClaimTypes.Role)?.Value;

            if (string.IsNullOrEmpty(role))
            {
                return ApiResponse<object>.Unauthorized("未获取到用户角色");
            }

            // 超级管理员拥有所有权限
            if (role == Roles.SUPER_ADMIN)
            {
                return ApiResponse<object>.Success(new
                {
                    Role = role,
                    Permissions = _permissionService.GetAllPermissionCodes(),
                    IsSuperAdmin = true
                });
            }

            var permissions = _permissionService.GetRolePermissions(role);

            return ApiResponse<object>.Success(new
            {
                Role = role,
                Permissions = permissions,
                IsSuperAdmin = false
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
