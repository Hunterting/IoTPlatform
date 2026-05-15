using IoTPlatform.Configuration;
using IoTPlatform.Helpers;
using IoTPlatform.Infrastructure.Tenant;
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
        if (roleClaim?.Value == IoTPlatform.Configuration.Roles.SUPER_ADMIN)
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
    /// 根据用户角色获取权限列表
    /// </summary>
    private List<string> GetUserPermissions(ClaimsPrincipal user)
    {
        var roleClaim = user.FindFirst(ClaimTypes.Role);
        if (roleClaim == null || string.IsNullOrEmpty(roleClaim.Value))
        {
            return new List<string>();
        }

        // 使用 RoleConfig 中定义的权限映射
        return IoTPlatform.Configuration.Roles.GetRolePermissions(roleClaim.Value);
    }
}

/// <summary>
/// 租户过滤器 - 自动从HTTP上下文初始化租户上下文
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class TenantFilterAttribute : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var httpContext = context.HttpContext;

        // 获取租户上下文访问器（需要通过 DI 注入）
        var tenantAccessor = httpContext.RequestServices.GetService<ITenantContextAccessor>();

        if (tenantAccessor == null)
        {
            context.Result = new JsonResult(ApiResponse.InternalError("租户上下文服务未注册"));
            return;
        }

        // 从 HTTP 上下文初始化租户信息
        tenantAccessor.InitializeFromHttpContext(httpContext);

        var user = httpContext.User;
        var roleClaim = user.FindFirst(ClaimTypes.Role);

        // 超级管理员不需要租户过滤
        if (roleClaim?.Value == IoTPlatform.Configuration.Roles.SUPER_ADMIN)
        {
            base.OnActionExecuting(context);
            return;
        }

        // 从 Claims 中获取 AppCode
        var appCode = user.FindFirst("AppCode")?.Value;
        if (string.IsNullOrEmpty(appCode))
        {
            context.Result = new JsonResult(ApiResponse.Forbidden("无法确定租户"));
            return;
        }

        // 验证租户上下文已正确设置
        if (string.IsNullOrEmpty(tenantAccessor.Current.AppCode))
        {
            context.Result = new JsonResult(ApiResponse.Forbidden("租户上下文未初始化"));
            return;
        }

        base.OnActionExecuting(context);
    }
}
