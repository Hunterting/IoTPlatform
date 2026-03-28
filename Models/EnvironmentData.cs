using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IoTPlatform.Models;

[Table("environment_data")]
public class EnvironmentData
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    public long DeviceId { get; set; }
    [MaxLength(200)] public string? DeviceName { get; set; }
    public long? AreaId { get; set; }
    [MaxLength(200)] public string? AreaName { get; set; }
    public double? PM25 { get; set; }
    public double? PM10 { get; set; }
    public double? CO2 { get; set; }
    public double? CO { get; set; }
    public double? Temperature { get; set; }
    public double? Humidity { get; set; }
    public double? Noise { get; set; }
    public double? CombustibleGas { get; set; }
    public double? Formaldehyde { get; set; }
    public double? Smoke { get; set; }
    public double? TVOC { get; set; }
    public DateTime RecordTime { get; set; } = DateTime.UtcNow;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? AppCode { get; set; }

    [ForeignKey("DeviceId")] public virtual Device? Device { get; set; }
    [ForeignKey("AreaId")] public virtual Area? Area { get; set; }
}
