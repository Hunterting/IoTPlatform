using System.Net;
using System.Text.Json;

namespace IoTPlatform.Infrastructure.Middleware;

/// <summary>
/// 操作日志记录中间件
/// </summary>
public class OperationLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<OperationLoggingMiddleware> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    // 不需要记录日志的路径
    private static readonly string[] ExcludePaths = new[]
    {
        "/api/v1/auth/login",
        "/api/v1/auth/refresh",
        "/health",
        "/swagger",
        "/favicon.ico"
    };

    public OperationLoggingMiddleware(
        RequestDelegate next,
        ILogger<OperationLoggingMiddleware> logger,
        IServiceScopeFactory scopeFactory)
    {
        _next = next;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // 排除不需要记录的路径
        if (ExcludePaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        var originalBodyStream = context.Response.Body;

        using var memoryStream = new MemoryStream();
        context.Response.Body = memoryStream;

        var startTime = DateTime.UtcNow;

        try
        {
            await _next(context);
        }
        finally
        {
            var duration = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
            var statusCode = context.Response.StatusCode;

            // 读取响应体（用于记录错误信息）
            memoryStream.Seek(0, SeekOrigin.Begin);
            var responseBody = await new StreamReader(memoryStream).ReadToEndAsync();

            // 记录日志
            await LogOperationAsync(context, path, statusCode, duration, responseBody);

            // 恢复响应体
            memoryStream.Seek(0, SeekOrigin.Begin);
            await memoryStream.CopyToAsync(originalBodyStream);
            context.Response.Body = originalBodyStream;
        }
    }

    private async Task LogOperationAsync(HttpContext context, string path, int statusCode, long duration, string responseBody)
    {
        try
        {
            var userId = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var userEmail = context.User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
            var userRole = context.User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
            var method = context.Request.Method;
            var clientIp = context.Connection.RemoteIpAddress?.ToString();

            var logLevel = statusCode >= 500 ? LogLevel.Error :
                          statusCode >= 400 ? LogLevel.Warning :
                          LogLevel.Information;

            var logMessage = $"[{method}] {path} - {statusCode} - {duration}ms - User: {userEmail ?? "Anonymous"} ({userRole})";

            _logger.Log(logLevel, logMessage);

            // TODO: 在后续实现中，将操作日志保存到数据库
            // await SaveOperationLogAsync(userId, userEmail, userRole, method, path, clientIp, statusCode, duration, responseBody);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "记录操作日志失败");
        }
    }
}
