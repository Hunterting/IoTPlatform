namespace IoTPlatform.Services;

/// <summary>
/// MQTT客户端服务接口
/// </summary>
public interface IMqttClientService
{
    /// <summary>
    /// 启动MQTT客户端
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 停止MQTT客户端
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// 订阅主题
    /// </summary>
    Task SubscribeToTopicAsync(string topic);

    /// <summary>
    /// 发布消息
    /// </summary>
    Task PublishMessageAsync(string topic, string payload);

    /// <summary>
    /// 数据接收事件
    /// </summary>
    event EventHandler<DeviceDataEventArgs> OnDataReceived;
}

/// <summary>
/// 设备数据事件参数
/// </summary>
public class DeviceDataEventArgs : EventArgs
{
    public long DeviceId { get; set; }
    public string? AppCode { get; set; }
    public string? SensorData { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
