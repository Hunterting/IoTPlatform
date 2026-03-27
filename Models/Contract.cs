using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IoTPlatform.Models;

[Table("contracts")]
public class Contract
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    public long ProjectId { get; set; }
    [Required][MaxLength(200)] public string Name { get; set; } = string.Empty;
    [Required][MaxLength(20)] public string Type { get; set; } = "service";
    [MaxLength(500)] public string? FileUrl { get; set; }
    [MaxLength(20)] public string? FileSize { get; set; }
    public DateTime UploadDate { get; set; } = DateTime.UtcNow;
    public string? AppCode { get; set; }

    [ForeignKey("ProjectId")] public virtual Project? Project { get; set; }
}
