using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IoTPlatform.Models;

[Table("operation_logs")]
public class OperationLog
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    public long UserId { get; set; }
    [MaxLength(100)] public string? UserName { get; set; }
    [MaxLength(50)] public string? Role { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime OperationTime { get; set; } = DateTime.UtcNow;
    public DateTime Time { get; set; } = DateTime.UtcNow;
    [MaxLength(50)] public string? Module { get; set; }
    [Required, MaxLength(50)] public string Operation { get; set; } = string.Empty; // create, update, delete, query, export, import
    [MaxLength(50)] public string Action { get; set; } = string.Empty; // create, update, delete, query, export, import
    [MaxLength(200)] public string? Target { get; set; }
    [MaxLength(2000)] public string? Detail { get; set; }
    [MaxLength(50)] public string? IP { get; set; }
    [Required, MaxLength(20)] public string Status { get; set; } = "success"; // success, failed
    public int? Duration { get; set; } // ms
    public string? AppCode { get; set; }
}
