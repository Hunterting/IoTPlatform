using System.Security.Claims;
using IoTPlatform.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace IoTPlatform.AnSheng.Tests;

/// <summary>
/// 授权缺口模式测试的统一 principal 工厂（设计 §5.2 测试基建）。
///
/// <para>三角色 + 匿名：All(超管) / Multi(admin，DB 授权集合) / Single(下级 claim 单值) / Anonymous。
/// <c>ForgedHeader</c> 用于构造 <c>X-App-Code: customer_999</c> 的恶意请求上下文，
/// 验证该头<b>不影响任何归属判定与落库</b>（M9-P0-0）。</para>
/// </summary>
public static class ScopePrincipals
{
    /// <summary>恶意伪造的 AppCode（前端完全可控，测试断言落库绝不包含它）。</summary>
    public const string ForgedAppCode = "customer_999";

    /// <summary>伪造请求头名称。</summary>
    public const string ForgedHeaderName = "X-App-Code";

    /// <summary>超管 principal：All 范围（空集合=不过滤）。</summary>
    public static ClaimsPrincipal All(long userId = 1) =>
        Principal(userId, Roles.SUPER_ADMIN);

    /// <summary>
    /// admin principal：Multi 范围。<paramref name="managedCustomerIds"/> 仅为 JWT claim 快照，
    /// 实际授权集合由 UserCustomerRepository（DB）决定。
    /// </summary>
    public static ClaimsPrincipal Multi(long userId = 10, params long[] managedCustomerIds) =>
        Principal(userId, Roles.ADMIN, managedClaim: string.Join(",", managedCustomerIds));

    /// <summary>下级 principal：Single 范围，只读 CustomerId/AppCode claim，零授权表访问。</summary>
    public static ClaimsPrincipal Single(long customerId, string appCode, long userId = 20) =>
        Principal(userId, Roles.OPERATOR, customerId: customerId, appCode: appCode);

    /// <summary>匿名 principal：无角色、无 AppCode claim（fail-closed 场景）。</summary>
    public static ClaimsPrincipal Anonymous() => new(new ClaimsIdentity());

    /// <summary>
    /// 构造带伪造 <c>X-App-Code: customer_999</c> 头的 ControllerContext。
    /// 任何被测端点若读取该头即会在断言中暴露（落库 AppCode 变成 customer_999 或越权成功）。
    /// </summary>
    public static ControllerContext ForgedHeaderContext(
        ClaimsPrincipal principal, string? forgedAppCode = ForgedAppCode)
    {
        var httpContext = new DefaultHttpContext { User = principal };
        httpContext.Request.Headers[ForgedHeaderName] = forgedAppCode ?? string.Empty;
        return new ControllerContext { HttpContext = httpContext };
    }

    /// <summary>构造公共 claims 的 principal。</summary>
    private static ClaimsPrincipal Principal(
        long userId,
        string role,
        long? customerId = null,
        string? appCode = null,
        string? managedClaim = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Role, role)
        };
        if (customerId.HasValue)
        {
            claims.Add(new Claim("CustomerId", customerId.Value.ToString()));
        }
        if (!string.IsNullOrEmpty(appCode))
        {
            claims.Add(new Claim("AppCode", appCode));
        }
        if (!string.IsNullOrEmpty(managedClaim))
        {
            claims.Add(new Claim(CustomerScopeConstants.ClaimManagedCustomerIds, managedClaim));
        }
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }
}
