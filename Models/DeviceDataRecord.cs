using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IoTPlatform.Models;

[Table("device_data_records")]
public class DeviceDataRecord
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    public long DeviceId { get; set; }
    public string? SensorData { get; set; }
    public double? Temperature { get; set; }
    public double? Humidity { get; set; }
    public double? PM25 { get; set; }
    public double? PM10 { get; set; }
    public double? CO2 { get; set; }
    public double? CO { get; set; }
    public double? FreshAirVolume { get; set; }
    public double? CombustibleGas { get; set; }
    public double? Formaldehyde { get; set; }
    public double? Smoke { get; set; }
    public double? TVOC { get; set; }
    public double? ExhaustVolume { get; set; }
    public double? SmokeConcentration { get; set; }
    public double? OilFume { get; set; }
    public double? Noise { get; set; }
    public long? AreaId { get; set; }
    public string? AreaName { get; set; }
    public string? DeviceName { get; set; }
    public string? Status { get; set; }
    public string? Level { get; set; }
    public DateTime RecordTime { get; set; } = DateTime.UtcNow;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? AppCode { get; set; }

    [ForeignKey("DeviceId")] public virtual Device? Device { get; set; }
    [ForeignKey("AreaId")] public virtual Area? Area { get; set; }
}
