using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using IoTPlatform.Services;

namespace IoTPlatform.Services;

/// <summary>
/// 后端 cold start 后自动拉起所有已激活的协议配置，免去运维每次重启手动 POST /api/v1/protocol-configs/{id}/start。
/// 在 <see cref="IHostApplicationLifetime.ApplicationStarted"/> 钩子里执行；
/// 单个协议拉起失败只记 WRN，不阻断宿主启动，也不影响其它协议的拉起。
/// </summary>
public sealed class ProtocolConfigAutoStartHostedService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<ProtocolConfigAutoStartHostedService> _logger;

    public ProtocolConfigAutoStartHostedService(
        IServiceProvider serviceProvider,
        IHostApplicationLifetime lifetime,
        ILogger<ProtocolConfigAutoStartHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _lifetime = lifetime;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // 等宿主真正开始接收请求后再拉起，避免与启动期其它初始化争抢资源。
        _lifetime.ApplicationStarted.Register(() => _ = PullUpAsync());
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task PullUpAsync()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<IProtocolConfigService>();
            await svc.AutoStartActiveProtocolsAsync();
            _logger?.LogInformation("协议配置自动拉起完成");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "协议配置自动拉起过程中发生异常（不影响宿主启动）");
        }
    }
}
