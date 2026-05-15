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
    /// 下发设备指令（发布到 {appCode}/{deviceId}/command 主题）
    /// </summary>
    /// <param name="appCode">租户代码</param>
    /// <param name="deviceId">设备ID</param>
    /// <param name="commandId">指令唯一ID</param>
    /// <param name="commandType">指令类型</param>
    /// <param name="parameters">指令参数（JSON）</param>
    Task SendDeviceCommandAsync(string appCode, long deviceId, string commandId, string commandType, string? parameters);

    /// <summary>
    /// 数据接收事件
    /// </summary>
    event EventHandler<DeviceDataEventArgs> OnDataReceived;

    /// <summary>
    /// 指令响应事件（设备回复 {appCode}/{deviceId}/command/response 时触发）
    /// </summary>
    event EventHandler<CommandResponseEventArgs> OnCommandResponse;
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

/// <summary>
/// 指令响应事件参数
/// </summary>
public class CommandResponseEventArgs : EventArgs
{
    /// <summary>
    /// 设备ID
    /// </summary>
    public long DeviceId { get; set; }

    /// <summary>
    /// 租户代码
    /// </summary>
    public string? AppCode { get; set; }

    /// <summary>
    /// 指令ID（与下发时一致）
    /// </summary>
    public string CommandId { get; set; } = string.Empty;

    /// <summary>
    /// 执行状态：success / failed / timeout
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// 结果数据（JSON）
    /// </summary>
    public string? ResultData { get; set; }

    /// <summary>
    /// 错误信息
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 响应时间
    /// </summary>
    public DateTime RespondedAt { get; set; } = DateTime.UtcNow;
}
