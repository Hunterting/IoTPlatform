using IoTPlatform.Data;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IoTPlatform.Models;

/// <summary>
/// 协议网关配置
/// </summary>
[Table("gateways")]
public class Gateway : IHasAppCode
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? GatewayType { get; set; } // protocol_conversion, data_forwarding

    [Required]
    [MaxLength(50)]
    public string SourceProtocol { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string TargetProtocol { get; set; } = string.Empty;

    [MaxLength(20)]
    public string Status { get; set; } = "offline"; // online, offline, error

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// 吞吐量 (msg/s)
    /// </summary>
    public int Throughput { get; set; }

    /// <summary>
    /// 网关配置JSON
    /// </summary>
    public string? Config { get; set; }

    /// <summary>
    /// 描述
    /// </summary>
    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// 租户代码
    /// </summary>
    [MaxLength(50)]
    public string? AppCode { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
