namespace IoTPlatform.DTOs.Responses;

/// <summary>
/// 档案响应DTO
/// </summary>
public class ArchiveDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? AppCode { get; set; }
    public string? Type { get; set; }
    public string? Size { get; set; }
    public DateTime? Date { get; set; }
    public string? Category { get; set; }
    public bool Is3DModel { get; set; }
    public long? AreaId { get; set; }
    public string? AreaName { get; set; }
    public string? ImageUrl { get; set; }
    public string? FilePath { get; set; }
    public string? SceneConfig { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// 档案设备标记响应DTO
/// </summary>
public class ArchiveDeviceMarkerDto
{
    public long Id { get; set; }
    public long ArchiveId { get; set; }
    public long? DeviceId { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? DeviceType { get; set; }
    public string? Model { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public string? Sensors { get; set; }
    public DateTime CreatedAt { get; set; }
}
