using System.Net.Http;

namespace IoTPlatform.IntegrationTests.Infrastructure.Auth;

/// <summary>
/// 身份切换扩展方法（架构方案 §3.4）。
///
/// 用法（链式，返回同一个 <see cref="HttpClient"/>）：
/// <code>
///   var res = await Client.AsAdmin().GetAsync("/api/v1/ansheng/discovered");
///   var res = await Client.AsAnonymous().GetAsync("/api/v1/ansheng/discovered");
/// </code>
///
/// 【为什么改的是 DefaultRequestHeaders 而不是每次 new HttpClient】
///   同一用例内往往要连续换身份做对照断言；复用 Client 可避免反复走
///   <c>WebApplicationFactory.CreateClient()</c> 的开销，也保证 CookieContainer 等状态一致。
///   代价是必须「先清后设」，否则上一次的头会残留 —— 所有方法都以 <see cref="AsAnonymous"/> 打底。
/// </summary>
public static class AuthTestHelper
{
    /// <summary>清除全部 X-Test-* 头 ⇒ 后续请求为匿名（用于 401 用例）。</summary>
    public static HttpClient AsAnonymous(this HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        foreach (var header in SharedTestConstants.Headers.All)
        {
            client.DefaultRequestHeaders.Remove(header);
        }

        return client;
    }

    /// <summary>普通管理员（role=admin），带默认租户声明。</summary>
    public static HttpClient AsAdmin(this HttpClient client) =>
        client.AsRole(SharedTestConstants.RoleAdmin, SharedTestConstants.AppCode);

    /// <summary>
    /// 超级管理员（role=super_admin）。
    ///
    /// 注意：<c>PermissionAuthorizeAttribute</c> 对 super_admin 直接放行，但
    /// <c>AnShengController</c> 仍会读 AppCode claim 做数据过滤，
    /// 因此这里依然下发 AppCode（用户决策 3 明确要求）。
    /// </summary>
    public static HttpClient AsSuperAdmin(this HttpClient client) =>
        client.AsRole(SharedTestConstants.RoleSuperAdmin, SharedTestConstants.AppCode);

    /// <summary>
    /// 任意角色 + 任意租户。
    /// </summary>
    /// <param name="client">测试客户端。</param>
    /// <param name="role">角色码，须与 <c>IoTPlatform.Configuration.Roles</c> 中的常量一致。</param>
    /// <param name="appCode">租户码；传 null 表示「不下发 AppCode claim」，用于负向分支。</param>
    /// <param name="userId">用户主键，默认 <see cref="SharedTestConstants.DefaultUserId"/>。</param>
    /// <param name="customerId">租户主键，默认 <see cref="SharedTestConstants.DefaultCustomerId"/>。</param>
    public static HttpClient AsRole(
        this HttpClient client,
        string role,
        string? appCode = SharedTestConstants.AppCode,
        string? userId = null,
        string? customerId = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);

        client.AsAnonymous();

        client.DefaultRequestHeaders.Add(SharedTestConstants.Headers.Role, role);
        client.DefaultRequestHeaders.Add(
            SharedTestConstants.Headers.UserId, userId ?? SharedTestConstants.DefaultUserId);
        client.DefaultRequestHeaders.Add(
            SharedTestConstants.Headers.CustomerId, customerId ?? SharedTestConstants.DefaultCustomerId);

        if (!string.IsNullOrWhiteSpace(appCode))
        {
            client.DefaultRequestHeaders.Add(SharedTestConstants.Headers.AppCode, appCode);
        }

        return client;
    }

    /// <summary>
    /// 【预留】走真实登录接口换取 JWT。
    ///
    /// 当前脚手架一律用 <see cref="TestAuthHandler"/> 注入身份，不依赖真令牌。
    /// 若后续出现「必须验证 JWT 解析/过期/刷新」的用例（不属于 T5–T14 范围），
    /// 在此实现：POST /api/v1/auth/login → 取 token → 塞 Authorization: Bearer，
    /// 同时需要在 <c>TestWebAppFactory</c> 保留 JwtBearer 方案而非整体覆盖默认方案。
    /// </summary>
    public static Task<string> CreateRealJwtAsync(HttpClient client, string username, string password)
    {
        ArgumentNullException.ThrowIfNull(client);

        throw new NotSupportedException(
            "当前集成测试脚手架使用 TestAuthHandler 注入身份，不签发真实 JWT。" +
            "若确需验证令牌链路，请先在 TestWebAppFactory.ReplaceAuthentication 中保留 JwtBearer 方案。");
    }
}
