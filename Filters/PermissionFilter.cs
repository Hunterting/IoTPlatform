using IoTPlatform.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Security.Claims;

namespace IoTPlatform.Filters;

/// <summary>
/// 权限验证过滤器
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class PermissionAuthorizeAttribute : AuthorizeAttribute, IAuthorizationFilter
{
    private readonly string[] _requiredPermissions;

    public PermissionAuthorizeAttribute(params string[] permissions)
    {
        _requiredPermissions = permissions;
    }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;

        // 检查用户是否已认证
        if (user == null || user.Identity == null || !user.Identity.IsAuthenticated)
        {
            context.Result = new JsonResult(ApiResponse.Unauthorized("未授权访问"));
            return;
        }

        // 超级管理员拥有所有权限
        var roleClaim = user.FindFirst(ClaimTypes.Role);
        if (roleClaim?.Value == "super_admin")
        {
            return;
        }

        // 如果没有指定权限，只检查认证状态
        if (_requiredPermissions.Length == 0)
        {
            return;
        }

        // 获取用户权限
        var userPermissions = GetUserPermissions(user);

        // 检查用户是否拥有所需权限
        var hasPermission = _requiredPermissions.Any(perm => userPermissions.Contains(perm));

        if (!hasPermission)
        {
            context.Result = new JsonResult(ApiResponse.Forbidden("权限不足"));
        }
    }

    /// <summary>
    /// 从用户Claims中获取权限列表
    /// </summary>
    private List<string> GetUserPermissions(ClaimsPrincipal user)
    {
        var roleClaim = user.FindFirst(ClaimTypes.Role);
        if (roleClaim == null) return new List<string>();

        // 根据角色获取权限 - 简化版本,暂时返回空列表
        // TODO: 后续可以集成权限系统
        return new List<string>();
    }
}

/// <summary>
/// 租户过滤器 - 自动过滤数据范围
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class TenantFilterAttribute : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var user = context.HttpContext.User;
        var roleClaim = user.FindFirst(ClaimTypes.Role);

        // 超级管理员不需要租户过滤
        if (roleClaim?.Value == "super_admin")
        {
            return;
        }

        // 从Claims中获取AppCode
        var appCode = user.FindFirst("AppCode")?.Value;
        if (string.IsNullOrEmpty(appCode))
        {
            context.Result = new JsonResult(ApiResponse.Forbidden("无法确定租户"));
            return;
        }

        // 将AppCode添加到Action参数中（如果需要）
        if (context.ActionArguments.ContainsKey("appCode"))
        {
            context.ActionArguments["appCode"] = appCode;
        }

        base.OnActionExecuting(context);
    }
}
