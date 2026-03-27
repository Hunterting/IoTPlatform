using System.ComponentModel.DataAnnotations;

namespace IoTPlatform.DTOs.Requests;

/// <summary>
/// 更新协议配置请求
/// </summary>
public class UpdateProtocolConfigRequest
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(20)]
    public string Status { get; set; } = string.Empty; // active, inactive

    [MaxLength(500)]
    public string? Description { get; set; }

    public List<long>? DeviceIds { get; set; }

    public Dictionary<string, object>? Config { get; set; }
}
