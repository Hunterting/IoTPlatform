using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using IoTPlatform.Data.Repositories.Interfaces;

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
            // 将操作日志保存到数据库（通过作用域解析仓储）
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var logRepository = scope.ServiceProvider.GetService<ILogRepository>();
                if (logRepository != null)
                {
                    long parsedUserId = 0;
                    if (!string.IsNullOrEmpty(userId) && long.TryParse(userId, out var uid))
                    {
                        parsedUserId = uid;
                    }

                    var status = statusCode >= 400 ? "failed" : "success";

                    // derive appCode and allowed friendly action/module names
                    var appCode = context.User.FindFirst("AppCode")?.Value ?? "system";

                    // Prefer controller/action from route values when available
                    string? controller = null;
                    string? action = null;
                    try
                    {
                        controller = context.Request.RouteValues?[
                            "controller"]?.ToString();
                        action = context.Request.RouteValues?["action"]?.ToString();
                    }
                    catch
                    {
                        // ignore
                    }

                    var moduleName = !string.IsNullOrEmpty(controller) ? controller : path;
                    var actionName = !string.IsNullOrEmpty(action) ? action : method.ToUpperInvariant();
                    var targetName = !string.IsNullOrEmpty(controller) && !string.IsNullOrEmpty(action) ? $"{controller}/{action}" : path;

                    await logRepository.LogOperationAsync(
                        userId: parsedUserId,
                        userName: userEmail ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value,
                        role: userRole,
                        module: moduleName,
                        action: actionName,
                        target: targetName,
                        detail: responseBody,
                        ip: clientIp,
                        status: status,
                        duration: (int)duration,
                        appCode: appCode
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "将操作日志保存到数据库时发生错误");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "记录操作日志失败");
        }
    }
}
