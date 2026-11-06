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
    public bool IsActive { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    public List<long>? DeviceIds { get; set; }

    public Dictionary<string, object>? Config { get; set; }

    /// <summary>目标租户 AppCode（仅超级管理员编辑时生效，用于指派归属；普通租户传值被忽略）</summary>
    [MaxLength(50)]
    public string? AppCode { get; set; }
}
