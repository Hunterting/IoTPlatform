using System.ComponentModel.DataAnnotations;

namespace IoTPlatform.DTOs.Requests;

/// <summary>
/// 创建系统设置请求
/// </summary>
public class CreateSettingRequest
{
    [Required]
    [MaxLength(100)]
    public string Key { get; set; } = string.Empty;

    [Required]
    public string? Value { get; set; }

    [MaxLength(200)]
    public string? Description { get; set; }

    [MaxLength(50)]
    public string? Category { get; set; }

    [MaxLength(20)]
    public string ValueType { get; set; } = "string"; // string, number, boolean, json

    [MaxLength(50)]
    public string? AppCode { get; set; }
}
