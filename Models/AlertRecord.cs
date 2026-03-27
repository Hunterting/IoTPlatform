using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IoTPlatform.Models;

[Table("alert_records")]
public class AlertRecord
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Required, MaxLength(50)] public string AlertNo { get; set; } = string.Empty;
    [Required, MaxLength(200)] public string DeviceName { get; set; } = string.Empty;
    [MaxLength(50)] public string? DeviceCode { get; set; }
    [MaxLength(200)] public string? Area { get; set; }
    [Required, MaxLength(50)] public string AlertType { get; set; } = string.Empty; // temperature, humidity, pm25, co2, smoke, gas, water_leak, device_offline
    [Required, MaxLength(20)] public string Level { get; set; } = "info"; // info, warning, critical
    public double? Value { get; set; }
    public double? Threshold { get; set; }
    public DateTime AlertTime { get; set; } = DateTime.UtcNow;
    [Required, MaxLength(20)] public string Status { get; set; } = "pending"; // pending, processing, resolved, ignored
    [MaxLength(100)] public string? Assignee { get; set; }
    public DateTime? ResolveTime { get; set; }
    [MaxLength(1000)] public string? Remark { get; set; }
    public long? DeviceId { get; set; }
    public long? AreaId { get; set; }
    public string? AppCode { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey("DeviceId")] public virtual Device? Device { get; set; }
    [ForeignKey("AreaId")] public virtual Area? Area { get; set; }
    public virtual ICollection<AlertProcessLog>? ProcessLogs { get; set; }
}
