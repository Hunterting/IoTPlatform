using IoTPlatform.Data;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IoTPlatform.Models;

/// <summary>
/// 受控设备
/// 用于记录已添加到指令控制系统的设备，方便快速发送指令
/// </summary>
[Table("controlled_devices")]
public class ControlledDevice : IHasAppCode
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    /// <summary>
    /// 应用代码（租户标识）
    /// </summary>
    [MaxLength(50)]
    public string? AppCode { get; set; }

    /// <summary>
    /// 关联的设备ID
    /// </summary>
    public long DeviceId { get; set; }

    /// <summary>
    /// 设备名称（冗余存储，便于显示）
    /// </summary>
    [MaxLength(100)]
    public string DeviceName { get; set; } = string.Empty;

    /// <summary>
    /// 设备序列号（冗余存储）
    /// </summary>
    [MaxLength(100)]
    public string? SerialNumber { get; set; }

    /// <summary>
    /// 设备型号（冗余存储）
    /// </summary>
    [MaxLength(100)]
    public string? Model { get; set; }

    /// <summary>
    /// 设备分类/协议类型（冗余存储）
    /// </summary>
    [MaxLength(50)]
    public string? Category { get; set; }

    /// <summary>
    /// 安装位置（冗余存储）
    /// </summary>
    [MaxLength(200)]
    public string? Location { get; set; }

    /// <summary>
    /// 备注信息
    /// </summary>
    [MaxLength(500)]
    public string? Remark { get; set; }

    /// <summary>
    /// 优先级（1-10，数字越大优先级越高）
    /// </summary>
    public int Priority { get; set; } = 5;

    /// <summary>
    /// 是否启用控制
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 是否收藏（便于快速访问）
    /// </summary>
    public bool IsFavorite { get; set; } = false;

    /// <summary>
    /// 注册时间
    /// </summary>
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 最后一次发送指令时间
    /// </summary>
    public DateTime? LastCommandAt { get; set; }

    /// <summary>
    /// 累计发送指令次数
    /// </summary>
    public int CommandCount { get; set; } = 0;

    /// <summary>
    /// 创建人ID
    /// </summary>
    public long? CreatedBy { get; set; }

    /// <summary>
    /// 创建人名称
    /// </summary>
    [MaxLength(100)]
    public string? CreatedByName { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 更新时间
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 关联的设备
    /// </summary>
    [ForeignKey("DeviceId")]
    public virtual Device? Device { get; set; }
}
