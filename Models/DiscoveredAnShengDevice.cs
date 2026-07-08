using IoTPlatform.Data;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IoTPlatform.Models;

/// <summary>
/// 安圣设备发现待认领池 — 未知 IMEI 设备暂存表
/// 管理员手动认领后转为正式 Device
/// </summary>
[Table("discovered_ansheng_devices")]
public class DiscoveredAnShengDevice : IHasAppCode
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [MaxLength(50)]
    public string? AppCode { get; set; }

    /// <summary>设备唯一标识 IMEI</summary>
    [Required, MaxLength(50)]
    public string Imei { get; set; } = string.Empty;

    /// <summary>设备型号（从 getDevInfo 获取）</summary>
    [MaxLength(100)]
    public string? Model { get; set; }

    /// <summary>网络类型（WiFi / 4G / NB-IoT 等）</summary>
    [MaxLength(50)]
    public string? NetType { get; set; }

    /// <summary>首次发现时间</summary>
    public DateTime DiscoveredAt { get; set; } = DateTime.UtcNow;

    /// <summary>最近一次收到数据的时间</summary>
    public DateTime? LastSeenAt { get; set; }

    /// <summary>是否已被认领转为正式 Device</summary>
    public bool IsClaimed { get; set; }

    /// <summary>认领后关联的正式 Device Id</summary>
    public long? ClaimedDeviceId { get; set; }
}
