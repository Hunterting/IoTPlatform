using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IoTPlatform.Models;

[Table("devices")]
public class Device
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Required][MaxLength(50)] public string AppCode { get; set; } = string.Empty;
    [Required][MaxLength(200)] public string Name { get; set; } = string.Empty;
    [MaxLength(100)] public string? Model { get; set; }
    [MaxLength(100)] public string? SerialNumber { get; set; }
    [MaxLength(100)] public string? Category { get; set; }
    [MaxLength(200)] public string? Location { get; set; }
    public long? AreaId { get; set; }
    public long? ProjectId { get; set; }
    [MaxLength(200)] public string? ProjectName { get; set; }
    public string? EnergyTypes { get; set; }
    [Required][MaxLength(20)] public string Status { get; set; } = "offline";
    public DateTime? InstallDate { get; set; }
    public DateTime? LastMaintenance { get; set; }
    [MaxLength(200)] public string? Supplier { get; set; }
    public DateTime? WarrantyDate { get; set; }
    public double? Power { get; set; }
    [MaxLength(20)] public string? Voltage { get; set; }
    public bool MeterInstalled { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey("AreaId")] public virtual Area? Area { get; set; }
    [ForeignKey("ProjectId")] public virtual Project? Project { get; set; }
    public virtual ICollection<DeviceSensor>? Sensors { get; set; }
    public virtual ICollection<DeviceDataRecord>? DataRecords { get; set; }
    public virtual ICollection<AreaDevice>? AreaDevices { get; set; }
}
