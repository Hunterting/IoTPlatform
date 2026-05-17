namespace IoTPlatform.DTOs.Responses;

/// <summary>
/// 受控设备响应DTO
/// </summary>
public class ControlledDeviceDto
{
    public long Id { get; set; }
    public string? AppCode { get; set; }
    public long DeviceId { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public string? SerialNumber { get; set; }
    public string? Model { get; set; }
    public string? Category { get; set; }
    public string? Location { get; set; }
    public string? Remark { get; set; }
    public int Priority { get; set; }
    public bool IsEnabled { get; set; }
    public bool IsFavorite { get; set; }
    public DateTime RegisteredAt { get; set; }
    public DateTime? LastCommandAt { get; set; }
    public int CommandCount { get; set; }
    public long? CreatedBy { get; set; }
    public string? CreatedByName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// 关联设备的实时状态（从Devices表获取）
    /// </summary>
    public string? DeviceStatus { get; set; }
}

/// <summary>
/// 受控设备注册结果DTO
/// </summary>
public class ControlledDeviceRegisterResultDto
{
    public long Id { get; set; }
    public long DeviceId { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
}
