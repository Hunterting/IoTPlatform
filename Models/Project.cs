using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IoTPlatform.Models;

[Table("projects")]
public class Project
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    public long CustomerId { get; set; }
    [Required][MaxLength(200)] public string Name { get; set; } = string.Empty;
    [MaxLength(500)] public string? Address { get; set; }
    public int DeviceCount { get; set; }
    public DateTime? OnlineDate { get; set; }
    [Required][MaxLength(20)] public string Status { get; set; } = "planning";
    public string? AppCode { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey("CustomerId")] public virtual Customer? Customer { get; set; }
    public virtual ICollection<Contract>? Contracts { get; set; }
    public virtual ICollection<WorkSummary>? WorkSummaries { get; set; }
    public virtual ICollection<Device>? Devices { get; set; }
}
