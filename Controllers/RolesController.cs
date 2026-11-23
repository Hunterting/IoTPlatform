using IoTPlatform.Configuration;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Filters;
using IoTPlatform.Helpers;
using IoTPlatform.Services;
using IoTPlatform.Services.Guards;
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
    private readonly ICustomerScopeService? _customerScope;
    private readonly IPermissionService? _permissionService;

    public RolesController(
        IRoleService roleService,
        ICustomerScopeService? customerScope = null,
        IPermissionService? permissionService = null)
    {
        _roleService = roleService;
        _customerScope = customerScope;
        _permissionService = permissionService;
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

            var listScope = await this.ResolveListScopeAsync(_customerScope);
            var result = await _roleService.GetRolesAsync(page, pageSize, keyword, appCode, role, listScope.CustomerIds, listScope.AppCodes);
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
            // B4/M6：先点查权威归属并按 scope 三分支判定；越权 HTTP 200 + Code=403。
            if (!await this.CanReadDetailAsync<IoTPlatform.Models.Role>(id, true))
            {
                return Ok(ApiResponse<RoleDto>.Fail(403, "无权访问该记录"));
            }

        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var role = User.FindFirst(ClaimTypes.Role)?.Value;

            var result = await _roleService.GetRoleAsync(id, appCode, role);
            if (result == null)
            {
                var response = ApiResponse.NotFound("角色不存在");
            return Ok(response);
            }

            return ApiResponse<RoleDto>.Success(result);
        }
        catch (Exception ex)
        {
            var response = ApiResponse.Error(ex.Message);
            return Ok(response);
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
            var appCode = User.FindFirst("AppCode")?.Value;
            var role = User.FindFirst(ClaimTypes.Role)?.Value;

            if (role == Roles.ADMIN)
            {
                // M10 ① 目标客户必须存在且在 DB 授权集合内（claim 快照不作准）。
                if (!request.CustomerId.HasValue || _customerScope == null)
                {
                    return Ok(ApiResponse<RoleDto>.BadRequest("租户管理员必须在授权客户下创建角色"));
                }

                if (!await _customerScope.CanAccessCustomerAsync(User, request.CustomerId.Value))
                {
                    return Ok(ApiResponse<RoleDto>.Forbidden("无权在该客户下创建角色"));
                }

                // resolvedAppCode 强制取授权客户 AppCode，严禁为 null（不许建平台级角色）。
                var resolvedAppCode = await _customerScope.ResolveAppCodeByCustomerIdAsync(request.CustomerId.Value);
                if (string.IsNullOrEmpty(resolvedAppCode))
                {
                    return Ok(ApiResponse<RoleDto>.BadRequest("目标客户缺少 AppCode"));
                }

                // M10 ② 权限白名单降级：不得授予超出调用方自身权限的权限。
                var callerPermissions = await _permissionService.GetEffectivePermissionsAsync(User);
                var illegal = (request.Permissions ?? new List<string>())
                    .Except(callerPermissions)
                    .ToList();
                if (illegal.Count > 0)
                {
                    return Ok(ApiResponse<RoleDto>.Forbidden(
                        "不能授予超出自身权限的权限：" + string.Join(",", illegal)));
                }

                var scopedResult = await _roleService.CreateRoleAsync(
                    request, resolvedAppCode, customerScopeVerified: true);
                return ApiResponse<RoleDto>.Success(scopedResult, "角色创建成功");
            }

            // 超管/下级：原路径逐字不改。
            var result = await _roleService.CreateRoleAsync(request, appCode, role);
            return ApiResponse<RoleDto>.Success(result, "角色创建成功");
        }
        catch (ForbiddenAccessException ex)
        {
            return Ok(ApiResponse<RoleDto>.Forbidden(ex.Message));
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

            // B5/M8：显式解析范围；Admin 仅能改授权客户下的角色，集合外仍 403。
            var scope = await _customerScope!.ResolveAsync(User);

            // M10 提权面：放宽改权限的同时保持与 CreateRole 一致的白名单，禁止授予超出自身的权限。
            if (role == Roles.ADMIN && scope.Mode == CustomerScopeMode.Multi && request.Permissions != null)
            {
                var callerPermissions = await _permissionService!.GetEffectivePermissionsAsync(User);
                var illegal = request.Permissions.Except(callerPermissions).ToList();
                if (illegal.Count > 0)
                {
                    return Ok(ApiResponse<RoleDto>.Forbidden(
                        "不能授予超出自身权限的权限：" + string.Join(",", illegal)));
                }
            }

            var result = await _roleService.UpdateRoleAsync(id, request, appCode, scope, customerScopeVerified: true);
            return ApiResponse<RoleDto>.Success(result, "角色更新成功");
        }
        catch (ForbiddenAccessException ex)
        {
            return Ok(ApiResponse<RoleDto>.Forbidden(ex.Message));
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
    /// 删除角色
    /// </summary>
    [HttpDelete("{id}")]
    [PermissionAuthorize(Permissions.DELETE_ROLES)]
    public async Task<ActionResult<ApiResponse>> DeleteRole(long id)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;

            // B5/M8：删除前按权威范围判定，集合外客户的角色仍拒绝删除。
            var scope = await _customerScope!.ResolveAsync(User);
            await _roleService.DeleteRoleAsync(id, appCode, scope, customerScopeVerified: true);
            return ApiResponse.Success("角色删除成功");
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
