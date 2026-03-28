using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IoTPlatform.Models;

[Table("roles")]
public class Role
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Required][MaxLength(50)] public string Code { get; set; } = string.Empty;
    [Required][MaxLength(100)] public string Name { get; set; } = string.Empty;
    [MaxLength(500)] public string? Description { get; set; }
    public string? Permissions { get; set; } // JSON array of permission strings
    [Required][MaxLength(10)] public string DataScope { get; set; } = "CUSTOM"; // ALL, CUSTOM
    public string? AppCode { get; set; }
    public bool IsSystem { get; set; } // true for default roles that cannot be deleted
    public bool IsDefault { get; set; } // true for default roles
    public string Status { get; set; } = "active"; // active, inactive
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
