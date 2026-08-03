using IoTPlatform.Data;
using IoTPlatform.Infrastructure.Protocol.AnSheng;
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

    // ─────────────────────────────────────────────────────────────
    // T5 新增：待认领阶段的能力快照
    //
    // 【为什么这些字段要在 discovered 表再存一份，而不是只放 Profile】
    //   待认领池是给运维「挑设备」看的列表页：他需要在点认领<b>之前</b>就知道
    //   这台是 4G 开关还是 WiFi 喇叭、几路插槽、上次探测为什么失败。
    //   若只存 Profile，列表页要么 JOIN 一张认领后才有数据的表（永远是空），
    //   要么每行发一次探测（列表页打爆设备）。故在此冗余一份只读快照。
    //
    // 【追加而非重排】以下字段一律追加在末尾。EF 迁移按属性顺序生成 ADD COLUMN，
    //   插到中间会让 Designer 快照与已上线库产生无谓 diff。
    // ─────────────────────────────────────────────────────────────

    /// <summary>探测/自学习推断出的设备品类；未判定为 <see cref="AnShengDeviceKind.Unknown"/>。</summary>
    public AnShengDeviceKind Kind { get; set; } = AnShengDeviceKind.Unknown;

    /// <summary>插槽数量（开关款权威判据，<c>&gt; 0</c> 即开关）。未知为 <c>null</c>。</summary>
    public int? SlotAmount { get; set; }

    /// <summary>固件版本号，如 <c>SWITCH-EC618X-R24-O-V4.0.8</c>。</summary>
    [MaxLength(100)]
    public string? Version { get; set; }

    /// <summary>物联卡 ICCID，仅 4G 款有值。</summary>
    [MaxLength(50)]
    public string? Iccid { get; set; }

    /// <summary>最近一次探测状态。默认 <see cref="AnShengProbeStatus.NotProbed"/>。</summary>
    public AnShengProbeStatus ProbeStatus { get; set; } = AnShengProbeStatus.NotProbed;

    /// <summary>最近一次探测失败原因摘要；成功时清空为 <c>null</c>。</summary>
    [MaxLength(500)]
    public string? ProbeError { get; set; }

    /// <summary>最近一次探测时间（UTC）。从未探测过为 <c>null</c>。</summary>
    public DateTime? LastProbedAt { get; set; }
}
