using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IoTPlatform.Models;

[Table("customers")]
public class Customer
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Required][MaxLength(200)] public string Name { get; set; } = string.Empty;
    [Required][MaxLength(50)] public string Code { get; set; } = string.Empty;
    [Required][MaxLength(50)] public string AppCode { get; set; } = string.Empty;
    [MaxLength(100)] public string? Contact { get; set; }
    [MaxLength(20)] public string? Phone { get; set; }
    [MaxLength(500)] public string? Address { get; set; }
    [Required][MaxLength(20)] public string Status { get; set; } = "active";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual ICollection<Project>? Projects { get; set; }
    public virtual ICollection<User>? Users { get; set; }
    public virtual ICollection<Area>? Areas { get; set; }
}
