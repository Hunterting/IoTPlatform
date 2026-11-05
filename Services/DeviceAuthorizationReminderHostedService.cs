using IoTPlatform.Data;

namespace IoTPlatform.Services;

/// <summary>
/// 设备授权到期提醒宿主服务：每天扫描一次「即将过期(≤30天)/已过期」的授权，
/// 按租户生成去重告警（AlertRecord 显式带 AppCode，租户在告警中心可见）。
///
/// 设计要点：
///  - 启动后立即跑一次（延迟几秒等 DB 迁移完成），之后每 24h 一次。
///  - ID 服务是 Scoped，必须在 HostedService(Singleton) 内手动开 Scope 解析。
///  - 单轮异常只记 WRN，不阻断宿主启动/其它后台服务（与 ProtocolConfigAutoStart 同款）。
///  - 去重由 Service 层按「设备+告警类型+当天」保证，重复重启不会产生告警风暴。
/// </summary>
public class DeviceAuthorizationReminderHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DeviceAuthorizationReminderHostedService> _logger;
    // 每 6 小时扫描一次（比 24h 更及时）；Service 层按「设备+告警类型+当天」去重，
    // 因此更频繁的扫描不会产生告警风暴，重启也不会重复提醒。
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(10);

    public DeviceAuthorizationReminderHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<DeviceAuthorizationReminderHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IDeviceAuthorizationService>();
                var count = await service.ScanAndNotifyExpiringAsync(stoppingToken);
                _logger.LogInformation("设备授权到期扫描完成：新增 {Count} 条提醒", count);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                // 单轮失败不阻断宿主（例如 DB 暂不可用），下一轮继续
                _logger.LogWarning(ex, "设备授权到期扫描失败，将在下一轮重试");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
        }
    }
}
