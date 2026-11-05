using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using IoTPlatform.Data;

namespace IoTPlatform.Models;

/// <summary>
/// 设备授权（按设备逐台授权，含有效期）。
///
/// 业务规则：
///  - 每台设备每个租户最多一条授权记录（唯一索引 DeviceId + AppCode）。
///  - 无记录 = 待授权（pending），不阻断采集；只有显式「已授权且已过期」才禁用设备
///    —— 这条边界至关重要：否则新表上线瞬间会把所有存量设备判为过期，打断真机联调。
///  - Status: active(生效) / expired(已过期) / revoked(已撤销)；pending 为「无记录」的派生状态，不落库。
/// </summary>
[Table("device_authorizations")]
public class DeviceAuthorization : IHasAppCode
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    public long DeviceId { get; set; }

    [MaxLength(50)] public string? AppCode { get; set; }

    /// <summary>active / expired / revoked</summary>
    [Required, MaxLength(20)] public string Status { get; set; } = "active";

    /// <summary>授权时间（UTC）</summary>
    public DateTime AuthorizedAt { get; set; } = DateTime.UtcNow;

    /// <summary>到期时间（UTC）</summary>
    public DateTime ExpireAt { get; set; }

    /// <summary>授权人 ID（超管）</summary>
    public long? AuthorizedBy { get; set; }

    [MaxLength(100)] public string? AuthorizedByName { get; set; }

    [MaxLength(500)] public string? Remark { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey("DeviceId")] public virtual Device? Device { get; set; }
}
