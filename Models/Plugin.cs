using IoTPlatform.Data;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IoTPlatform.Models;

/// <summary>
/// 插件配置
/// </summary>
[Table("plugins")]
public class Plugin : IHasAppCode
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string Version { get; set; } = "1.0.0";

    [MaxLength(50)]
    public string Status { get; set; } = "stopped"; // running, stopped, error

    public bool IsActive { get; set; } = false;

    /// <summary>
    /// 插件类型
    /// </summary>
    [MaxLength(50)]
    public string? PluginType { get; set; } // protocol, parser, transform

    /// <summary>
    /// 插件描述
    /// </summary>
    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// 作者
    /// </summary>
    [MaxLength(100)]
    public string? Author { get; set; }

    /// <summary>
    /// 插件文件路径
    /// </summary>
    [MaxLength(500)]
    public string? FilePath { get; set; }

    /// <summary>
    /// 插件配置JSON
    /// </summary>
    public string? Config { get; set; }

    /// <summary>
    /// 依赖项JSON
    /// </summary>
    public string? Dependencies { get; set; }

    /// <summary>
    /// 安装日期
    /// </summary>
    public DateTime? InstalledAt { get; set; }

    /// <summary>
    /// 租户代码
    /// </summary>
    [MaxLength(50)]
    public string? AppCode { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
