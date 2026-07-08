using IoTPlatform.Data;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IoTPlatform.Models;

/// <summary>
/// 安圣设备自动上报配置（每设备持久化）
/// </summary>
[Table("ansheng_device_configs")]
public class AnShengDeviceConfig : IHasAppCode
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Required]
    public long DeviceId { get; set; }

    [MaxLength(50)]
    public string? AppCode { get; set; }

    /// <summary>
    /// 设备 IMEI（从 Device.SerialNumber 读取，此处冗余存储便于查询）
    /// </summary>
    [Required, MaxLength(50)]
    public string Imei { get; set; } = string.Empty;

    /// <summary>状态上报间隔（秒），默认 60</summary>
    public int? GetDevStatusSec { get; set; }

    /// <summary>状态上报查询参数</summary>
    [MaxLength(255)]
    public string? GetDevStatusQ { get; set; }

    /// <summary>订单进度上报间隔（秒），默认 300</summary>
    public int? OrderUpSec { get; set; }

    /// <summary>RS485 轮询间隔（秒），0 表示关闭</summary>
    public int? Rs485Sec { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey("DeviceId")]
    public virtual Device? Device { get; set; }
}
