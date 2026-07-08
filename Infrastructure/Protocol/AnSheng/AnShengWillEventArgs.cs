namespace IoTPlatform.Infrastructure.Protocol.AnSheng;

/// <summary>
/// 安圣设备 Will（离线通知）事件参数
/// 由 AnShengMqttProtocolAdapter 在收到 will topic 消息时触发
/// </summary>
public class AnShengWillEventArgs : EventArgs
{
    /// <summary>设备 IMEI</summary>
    public string Imei { get; init; } = string.Empty;

    /// <summary>Will 消息的原始 JSON payload</summary>
    public string? Payload { get; init; }

    /// <summary>收到时间</summary>
    public DateTime ReceivedAt { get; init; } = DateTime.UtcNow;

    /// <summary>租户 AppCode</summary>
    public string? AppCode { get; init; }
}
