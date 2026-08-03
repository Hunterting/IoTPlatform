using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IoTPlatform.IntegrationTests.Infrastructure.Auth;

/// <summary>
/// 测试专用认证处理器（架构方案 §3.4，用户决策 3）。
///
/// 【为什么不签真 JWT】
///   真 JWT 需要 Issuer/Audience/SigningKey 与主工程配置严格对齐，任何配置漂移都会
///   让 401 变成难以定位的噪声；而集成测试关心的是「授权分支」而不是「令牌解析」。
///   因此这里把身份直接从请求头注入，令牌层留给单元测试/契约测试覆盖。
///
/// 【怎么触发匿名，以及匿名请求究竟返回什么】
///   请求不带任何 X-Test-* 头 ⇒ 返回 <see cref="AuthenticateResult.NoResult"/>。
///
///   此后 <c>AuthorizationMiddleware</c> 会在进入 MVC 过滤器<b>之前</b>就发起 challenge，
///   最终响应是<b>裸 HTTP 401 且包体为空</b>——不是 ApiResponse 包体。
///   也就是说 <c>PermissionAuthorizeAttribute</c> 里那段
///   <c>return new JsonResult(ApiResponse.Unauthorized(...))</c> 对匿名请求是<b>死代码</b>，
///   永远走不到。
///
///   三种场景的实测口径（勿再按「统一返回 200」的旧约定写断言）：
///     · 匿名（无任何 X-Test-* 头）  ⇒ 裸 HTTP 401 + 空包体 ⇒ 断 StatusCode，勿反序列化包体
///     · 已认证但权限不足            ⇒ HTTP 200 + Code 403 ⇒ 断 Code
///     · 正常                        ⇒ HTTP 200 + Code 200 ⇒ 断 Code
///
///   参见 示例-01 / 示例-08 与 README §5。
///
/// 【Claim 映射】必须与生产代码消费端逐一对齐：
///   · <see cref="ClaimTypes.Role"/>           ← X-Test-Role       （PermissionFilter / TenantContextAccessor）
///   · <see cref="ClaimTypes.NameIdentifier"/> ← X-Test-UserId     （TenantContextAccessor）
///   · "AppCode"                                ← X-Test-AppCode    （AnShengController / TenantContextAccessor）
///   · "CustomerId"                             ← X-Test-CustomerId （TenantContextAccessor）
///   · <see cref="ClaimTypes.Name"/>            ← X-Test-UserName
/// </summary>
public sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>测试认证方案名。<c>TestWebAppFactory</c> 会把它设为全部默认方案。</summary>
    public const string SchemeName = "Test";

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // 一个测试头都没有 ⇒ 匿名请求
        var hasAnyTestHeader = SharedTestConstants.Headers.All
            .Any(h => Request.Headers.ContainsKey(h));

        if (!hasAnyTestHeader)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var userId = ReadHeader(SharedTestConstants.Headers.UserId) ?? SharedTestConstants.DefaultUserId;
        var role = ReadHeader(SharedTestConstants.Headers.Role) ?? SharedTestConstants.RoleAdmin;
        var appCode = ReadHeader(SharedTestConstants.Headers.AppCode);
        var customerId = ReadHeader(SharedTestConstants.Headers.CustomerId);
        var userName = ReadHeader(SharedTestConstants.Headers.UserName) ?? $"test-user-{userId}";

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Name, userName),
            new(ClaimTypes.Role, role)
        };

        // AppCode / CustomerId 允许缺省：用于验证「少带租户声明」的负向分支。
        if (!string.IsNullOrWhiteSpace(appCode))
        {
            claims.Add(new Claim("AppCode", appCode));
        }

        if (!string.IsNullOrWhiteSpace(customerId))
        {
            claims.Add(new Claim("CustomerId", customerId));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    /// <summary>
    /// 覆盖挑战响应。
    ///
    /// 【这里就是匿名 401 的最终产出点】
    ///   匿名请求由 <c>AuthorizationMiddleware</c> 在 MVC 过滤器之前 challenge 到本方法，
    ///   响应到此为止——<b>不会</b>再有任何过滤器补上 <c>ApiResponse</c> 包体。
    ///   因此调用方应断言 <c>StatusCode == 401</c>，而不是去读包体里的 <c>Code</c>；
    ///   对空包体调用 <c>ReadAsync&lt;T&gt;</c> 会反序列化失败。
    ///
    /// 【不写包体是刻意为之】
    ///   本重写与基类 <see cref="AuthenticationHandler{TOptions}"/> 的默认行为一致
    ///   （都只置 401、不写包体）；显式写出来是为了把「匿名 = 裸 401 空包体」这一口径
    ///   钉在代码里，避免日后有人以为缺了 ApiResponse 包装而去"补"一个。
    ///   注：写 <c>WWW-Authenticate</c> 头的是 <c>JwtBearerHandler</c>，不是基类；
    ///   测试方案不需要该头，故不添加。
    /// </summary>
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }

    private string? ReadHeader(string name)
    {
        if (!Request.Headers.TryGetValue(name, out var values))
        {
            return null;
        }

        var value = values.ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
