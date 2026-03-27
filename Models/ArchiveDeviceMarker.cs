using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IoTPlatform.Models;

[Table("archive_device_markers")]
public class ArchiveDeviceMarker
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    public long ArchiveId { get; set; }
    public long? DeviceId { get; set; }
    [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;
    [MaxLength(100)] public string? DeviceType { get; set; }
    [MaxLength(100)] public string? Model { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public string? Sensors { get; set; } // JSON array of sensor info

    [ForeignKey("ArchiveId")] public virtual Archive? Archive { get; set; }
    [ForeignKey("DeviceId")] public virtual Device? Device { get; set; }
}
