namespace IoTPlatform.DTOs.Responses;

/// <summary>
/// 协议配置响应DTO
/// </summary>
public class ProtocolConfigDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public List<long>? DeviceIds { get; set; }
    public string? DeviceIdsJson { get; set; } // 内部使用，用于表达式树查询
    public Dictionary<string, object>? Config { get; set; }
    public string? ConfigJson { get; set; } // 内部使用，用于表达式树查询
    public string? Description { get; set; }
    public string? AppCode { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
