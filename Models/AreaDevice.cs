using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IoTPlatform.Models;

[Table("area_devices")]
public class AreaDevice
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    public long AreaId { get; set; }
    public long DeviceId { get; set; }
    [Required][MaxLength(200)] public string Name { get; set; } = string.Empty;
    [MaxLength(100)] public string? DeviceType { get; set; }
    [Required][MaxLength(20)] public string Status { get; set; } = "offline";
    public double X { get; set; }
    public double Y { get; set; }
    public string? AppCode { get; set; }

    [ForeignKey("AreaId")] public virtual Area? Area { get; set; }
    [ForeignKey("DeviceId")] public virtual Device? Device { get; set; }
}
