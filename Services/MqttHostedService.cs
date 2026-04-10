using IoTPlatform.Services;

namespace IoTPlatform.Services;

/// <summary>
/// MQTT托管服务（BackgroundService）
/// </summary>
public class MqttHostedService : BackgroundService
{
    private readonly IMqttClientService _mqttClientService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MqttHostedService> _logger;

    public MqttHostedService(
        IMqttClientService mqttClientService,
        IServiceScopeFactory scopeFactory,
        ILogger<MqttHostedService> logger)
    {
        _mqttClientService = mqttClientService;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// 后台服务执行方法
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MQTT Hosted Service starting...");

        // 订阅数据接收事件
        _mqttClientService.OnDataReceived += OnDeviceDataReceived;

        try
        {
            // 启动MQTT客户端
            await _mqttClientService.StartAsync(stoppingToken);

            _logger.LogInformation("MQTT Hosted Service is running.");

            // 等待取消信号
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("MQTT Hosted Service is stopping...");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MQTT Hosted Service encountered an error");
        }
        finally
        {
            // 停止MQTT客户端
            await _mqttClientService.StopAsync();
            _logger.LogInformation("MQTT Hosted Service stopped.");
        }
    }

    /// <summary>
    /// 设备数据接收处理
    /// </summary>
    private void OnDeviceDataReceived(object? sender, DeviceDataEventArgs e)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                _logger.LogDebug("Device data received: DeviceId={DeviceId}, AppCode={AppCode}", e.DeviceId, e.AppCode);

                // 通过ScopeFactory创建Scope来获取Scoped服务
                using var scope = _scopeFactory.CreateScope();
                var dataCollectionService = scope.ServiceProvider.GetRequiredService<IDataCollectionService>();

                // 调用数据采集服务处理数据
                await dataCollectionService.ProcessDeviceDataAsync(
                    e.DeviceId,
                    e.AppCode,
                    e.SensorData,
                    e.Timestamp);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling device data: DeviceId={DeviceId}", e.DeviceId);
            }
        });
    }
}
