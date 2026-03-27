using System.ComponentModel.DataAnnotations;

namespace IoTPlatform.DTOs.Requests;

/// <summary>
/// 创建协议配置请求
/// </summary>
public class CreateProtocolConfigRequest
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string Type { get; set; } = string.Empty; // modbus, mqtt, opcua, http, tcp, bacnet

    [MaxLength(500)]
    public string? Description { get; set; }

    [MaxLength(50)]
    public string? AppCode { get; set; }

    public List<long>? DeviceIds { get; set; } // JSON数组转换

    public Dictionary<string, object>? Config { get; set; } // 配置对象转换
}
