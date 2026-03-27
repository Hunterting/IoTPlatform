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
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? AppCode { get; set; }

    [ForeignKey("DeviceId")] public virtual Device? Device { get; set; }
}
