using IoTPlatform.Infrastructure.Tenant;

namespace IoTPlatform.Infrastructure.Middleware;

/// <summary>
/// 租户上下文初始化中间件
/// 在每个请求开始时，从HttpContext中提取租户信息并初始化租户上下文
/// </summary>
public class TenantInitializationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TenantInitializationMiddleware> _logger;

    public TenantInitializationMiddleware(RequestDelegate next, ILogger<TenantInitializationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ITenantContextAccessor tenantContextAccessor)
    {
        try
        {
            // 初始化租户上下文
            tenantContextAccessor.InitializeFromHttpContext(context);
            
            // 记录租户信息（仅调试模式）
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                var appCode = tenantContextAccessor.Current?.AppCode;
                _logger.LogDebug("请求租户上下文已初始化: AppCode={AppCode}", appCode ?? "未设置");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "初始化租户上下文时发生错误");
        }

        await _next(context);
    }
}

/// <summary>
/// 租户中间件扩展方法
/// </summary>
public static class TenantMiddlewareExtensions
{
    /// <summary>
    /// 使用租户上下文初始化中间件
    /// 应该在 UseAuthentication 之后、UseAuthorization 之前调用
    /// </summary>
    public static IApplicationBuilder UseTenantInitialization(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<TenantInitializationMiddleware>();
    }
}
