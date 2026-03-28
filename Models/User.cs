using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IoTPlatform.Models;

[Table("users")]
public class User
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Required][MaxLength(100)] public string Username { get; set; } = string.Empty;
    [Required][MaxLength(500)] public string Password { get; set; } = string.Empty;
    [Required][MaxLength(100)] public string Name { get; set; } = string.Empty;
    [Required][MaxLength(100)] public string FullName { get; set; } = string.Empty;
    [Required][MaxLength(200)][EmailAddress] public string Email { get; set; } = string.Empty;
    [MaxLength(20)] public string? Phone { get; set; }
    [MaxLength(20)] public string Status { get; set; } = "active";
    public bool IsSuperAdmin { get; set; } = false;
    public long? RoleId { get; set; }
    [Required][MaxLength(50)] public string Role { get; set; } = "staff";
    public long? CustomerId { get; set; }
    public string? AppCode { get; set; }
    [MaxLength(200)] public string? Avatar { get; set; }
    public string? AllowedAreaIds { get; set; }
    public string? EnergyTypes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginTime { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey("CustomerId")] public virtual Customer? Customer { get; set; }
    [ForeignKey("RoleId")] public virtual Role? UserRole { get; set; }
}
