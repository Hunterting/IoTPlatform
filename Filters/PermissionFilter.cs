using IoTPlatform.Configuration;
using IoTPlatform.Helpers;
using IoTPlatform.Infrastructure.Tenant;
using IoTPlatform.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Security.Claims;

namespace IoTPlatform.Filters;

/// <summary>
/// 权限验证过滤器（异步实现，避免同步阻塞 DB 调用）
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class PermissionAuthorizeAttribute : AuthorizeAttribute, IAsyncAuthorizationFilter
{
    private readonly string[] _requiredPermissions;

    public PermissionAuthorizeAttribute(params string[] permissions)
    {
        _requiredPermissions = permissions;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;

        // 检查用户是否已认证
        if (user == null || user.Identity == null || !user.Identity.IsAuthenticated)
        {
            context.Result = new JsonResult(ApiResponse.Unauthorized("未授权访问"));
            return;
        }

        // 超级管理员拥有所有权限（短路放行，不查 DB）
        var roleClaim = user.FindFirst(ClaimTypes.Role);
        if (IoTPlatform.Configuration.Roles.IsSuperAdmin(roleClaim?.Value))
        {
            return;
        }

        // 如果没有指定权限，只检查认证状态
        if (_requiredPermissions.Length == 0)
        {
            return;
        }

        // 获取用户权限（异步解析，单一事实来源 = DB Role.Permissions）
        var userPermissions = await GetUserPermissionsAsync(context.HttpContext);

        // 检查用户是否拥有所需权限
        var hasPermission = _requiredPermissions.Any(perm => userPermissions.Contains(perm));

        if (!hasPermission)
        {
            context.Result = new JsonResult(ApiResponse.Forbidden("权限不足"));
        }
    }

    /// <summary>
    /// 通过 RequestServices 获取 IPermissionService，异步解析角色的有效权限（跨租户 + 静态回退）。
    /// 服务为 null（未注册）时兜底空列表，避免过滤器自身崩溃。
    /// </summary>
    private async Task<List<string>> GetUserPermissionsAsync(HttpContext httpContext)
    {
        var roleClaim = httpContext.User.FindFirst(ClaimTypes.Role);
        if (roleClaim == null || string.IsNullOrEmpty(roleClaim.Value))
        {
            return new List<string>();
        }

        var permissionService = httpContext.RequestServices.GetService<IPermissionService>();
        if (permissionService == null)
        {
            return new List<string>();
        }

        return await permissionService.GetEffectivePermissionsAsync(roleClaim.Value);
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

        // 【多客户授权 T03】admin 的 CustomerId/AppCode 按设计留空，跳过 AppCode 校验。
        // 这里只用 claim 快照做「快速拒绝」：集合为空直接 403，真正的授权判定在 ICustomerScopeService（DB 权威，T04 收口）。
        if (roleClaim?.Value == Roles.ADMIN)
        {
            var managedCustomerIdsClaim = user.FindFirst(CustomerScopeConstants.ClaimManagedCustomerIds)?.Value;
            var hasAnyManagedCustomer = !string.IsNullOrEmpty(managedCustomerIdsClaim)
                && managedCustomerIdsClaim!
                    .Split(CustomerScopeConstants.ManagedCustomerIdsSeparator, StringSplitOptions.RemoveEmptyEntries)
                    .Any(id => long.TryParse(id.Trim(), out _));

            if (!hasAnyManagedCustomer)
            {
                context.Result = new JsonResult(ApiResponse.Forbidden("该管理员未获授权任何客户"));
                return;
            }

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
