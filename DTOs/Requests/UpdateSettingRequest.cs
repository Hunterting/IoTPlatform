using System.ComponentModel.DataAnnotations;

namespace IoTPlatform.DTOs.Requests;

/// <summary>
/// 更新系统设置请求
/// </summary>
public class UpdateSettingRequest
{
    [Required]
    public string? Value { get; set; }

    [MaxLength(200)]
    public string? Description { get; set; }

    [MaxLength(20)]
    public string ValueType { get; set; } = "string"; // string, number, boolean, json
}
