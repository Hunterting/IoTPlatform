using IoTPlatform.Data;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IoTPlatform.Models;

/// <summary>
/// 数据库配置
/// </summary>
[Table("database_configs")]
public class DatabaseConfig : IHasAppCode
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string DatabaseType { get; set; } = "MySQL"; // MySQL, TDengine, InfluxDB, PostgreSQL, MongoDB

    [MaxLength(20)]
    public string Status { get; set; } = "disconnected"; // connected, disconnected, error

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// 主机地址
    /// </summary>
    [Required]
    [MaxLength(255)]
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// 端口
    /// </summary>
    public int Port { get; set; } = 3306;

    /// <summary>
    /// 数据库名称
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string DatabaseName { get; set; } = string.Empty;

    /// <summary>
    /// 用户名
    /// </summary>
    [MaxLength(100)]
    public string? Username { get; set; }

    /// <summary>
    /// 密码（加密存储）
    /// </summary>
    public string? EncryptedPassword { get; set; }

    /// <summary>
    /// 连接字符串
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// 其他配置JSON
    /// </summary>
    public string? Config { get; set; }

    /// <summary>
    /// 描述
    /// </summary>
    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// 最后测试时间
    /// </summary>
    public DateTime? LastTestAt { get; set; }

    /// <summary>
    /// 租户代码
    /// </summary>
    [MaxLength(50)]
    public string? AppCode { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
