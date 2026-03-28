using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IoTPlatform.Models;

[Table("areas")]
public class Area
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Required][MaxLength(200)] public string Name { get; set; } = string.Empty;
    [Required][MaxLength(10)] public string Type { get; set; } = "level1";
    [MaxLength(500)] public string? Image { get; set; }
    public long? ParentId { get; set; }
    public long? CustomerId { get; set; }
    public string? AppCode { get; set; }
    [MaxLength(500)] public string? Description { get; set; }
    public int DeviceCount { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey("ParentId")] public virtual Area? Parent { get; set; }
    [ForeignKey("CustomerId")] public virtual Customer? Customer { get; set; }
    public virtual ICollection<Area>? Children { get; set; }
    public virtual ICollection<AreaDevice>? Devices { get; set; }
    public virtual ICollection<Archive>? Archives { get; set; }
}
