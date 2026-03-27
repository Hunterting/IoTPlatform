using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IoTPlatform.Models;

[Table("archives")]
public class Archive
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;
    [MaxLength(50)] public string? AppCode { get; set; }
    [MaxLength(50)] public string? Type { get; set; } // floor_plan, 3d_model, photo, document
    [MaxLength(20)] public string? Size { get; set; }
    public DateTime? Date { get; set; }
    [MaxLength(50)] public string? Category { get; set; }
    public bool Is3DModel { get; set; }
    public long? AreaId { get; set; }
    [MaxLength(500)] public string? ImageUrl { get; set; }
    [MaxLength(500)] public string? FilePath { get; set; }
    public string? SceneConfig { get; set; } // JSON: SceneConfig with DeviceMarker3D[]
    public string? AppCodeTenant { get; set; } // Tenant isolation field
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey("AreaId")] public virtual Area? Area { get; set; }
    public virtual ICollection<ArchiveDeviceMarker>? DeviceMarkers { get; set; }
}
