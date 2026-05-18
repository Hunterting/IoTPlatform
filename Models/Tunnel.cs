using IoTPlatform.Data;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IoTPlatform.Models;

/// <summary>
/// 网络隧道配置
/// </summary>
[Table("tunnels")]
public class Tunnel : IHasAppCode
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(20)]
    public string TunnelType { get; set; } = "P2P"; // P2P, Proxy, VPN

    [MaxLength(20)]
    public string Status { get; set; } = "disconnected"; // connected, disconnected, error

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// 本地端口
    /// </summary>
    public int LocalPort { get; set; }

    /// <summary>
    /// 远程端口
    /// </summary>
    public int RemotePort { get; set; }

    /// <summary>
    /// 远程主机
    /// </summary>
    [MaxLength(255)]
    public string? RemoteHost { get; set; }

    /// <summary>
    /// 是否启用加密
    /// </summary>
    public bool Encryption { get; set; } = true;

    /// <summary>
    /// 带宽
    /// </summary>
    [MaxLength(50)]
    public string? Bandwidth { get; set; }

    /// <summary>
    /// 隧道配置JSON
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
