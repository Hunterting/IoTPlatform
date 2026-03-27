namespace IoTPlatform.DTOs.Responses;

/// <summary>
/// 设备响应DTO
/// </summary>
public class DeviceDto
{
    public long Id { get; set; }
    public string AppCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Model { get; set; }
    public string? SerialNumber { get; set; }
    public string? Category { get; set; }
    public string? Location { get; set; }
    public long? AreaId { get; set; }
    public string? AreaName { get; set; }
    public long? ProjectId { get; set; }
    public string? ProjectName { get; set; }
    public string? EnergyTypes { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? InstallDate { get; set; }
    public DateTime? LastMaintenance { get; set; }
    public string? Supplier { get; set; }
    public DateTime? WarrantyDate { get; set; }
    public double? Power { get; set; }
    public string? Voltage { get; set; }
    public bool MeterInstalled { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// 设备详情响应DTO
/// </summary>
public class DeviceDetailDto : DeviceDto
{
    public List<DeviceSensorDto>? Sensors { get; set; }
}

/// <summary>
/// 设备传感器DTO
/// </summary>
public class DeviceSensorDto
{
    public long Id { get; set; }
    public long DeviceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? SensorType { get; set; }
    public string? LastValue { get; set; }
    public string? Unit { get; set; }
}
