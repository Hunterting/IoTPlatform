namespace IoTPlatform.DTOs.Responses;

/// <summary>
/// 系统设置响应DTO
/// </summary>
public class SettingDto
{
    public long Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string? Value { get; set; }
    public string? Description { get; set; }
    public string? Category { get; set; }
    public string ValueType { get; set; } = string.Empty; // string, number, boolean, json
    public string? AppCode { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
