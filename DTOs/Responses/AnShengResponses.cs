namespace IoTPlatform.DTOs.Responses;

/// <summary>
/// 安圣命令下发响应
/// </summary>
public class AnShengCommandResponse
{
    /// <summary>是否成功</summary>
    public bool Success { get; set; }

    /// <summary>安圣 FrameId（请求-响应关联）</summary>
    public string? FrameId { get; set; }

    /// <summary>下发的命令 JSON（用于调试）</summary>
    public string? Payload { get; set; }

    /// <summary>错误信息</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>下发时间</summary>
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 安圣设备发现项
/// </summary>
public class DiscoveredAnShengDeviceDto
{
    /// <summary>ID</summary>
    public long Id { get; set; }

    /// <summary>IMEI</summary>
    public string Imei { get; set; } = string.Empty;

    /// <summary>设备型号</summary>
    public string? Model { get; set; }

    /// <summary>网络类型</summary>
    public string? NetType { get; set; }

    /// <summary>首次发现时间</summary>
    public DateTime DiscoveredAt { get; set; }

    /// <summary>最后在线时间</summary>
    public DateTime? LastSeenAt { get; set; }

    /// <summary>是否已认领</summary>
    public bool IsClaimed { get; set; }

    /// <summary>认领后设备 ID</summary>
    public long? ClaimedDeviceId { get; set; }
}

/// <summary>
/// 安圣设备认领响应
/// </summary>
public class ClaimAnShengDeviceResponse
{
    /// <summary>认领是否成功</summary>
    public bool Success { get; set; }

    /// <summary>创建后的设备 ID</summary>
    public long? DeviceId { get; set; }

    /// <summary>设备名称</summary>
    public string? DeviceName { get; set; }

    /// <summary>错误信息</summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// 安圣设备发现列表分页响应
/// </summary>
public class DiscoveredDeviceListResponse
{
    /// <summary>设备列表</summary>
    public List<DiscoveredAnShengDeviceDto> Items { get; set; } = new();

    /// <summary>总记录数</summary>
    public int Total { get; set; }

    /// <summary>当前页码</summary>
    public int Page { get; set; }

    /// <summary>每页条数</summary>
    public int PageSize { get; set; }
}
