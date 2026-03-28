using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IoTPlatform.Models;

[Table("login_logs")]
public class LoginLog
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    public long? UserId { get; set; }
    [MaxLength(200)] public string? Email { get; set; }
    [MaxLength(100)] public string? UserName { get; set; }
    [MaxLength(100)] public string? Role { get; set; }
    [MaxLength(50)] public string? IpAddress { get; set; }
    [MaxLength(50)] public string? IP { get; set; } // 别名
    [MaxLength(500)] public string? UserAgent { get; set; }
    [MaxLength(20)] public string? Status { get; set; } // success, failed
    [MaxLength(500)] public string? FailureReason { get; set; }
    public DateTime LoginTime { get; set; } = DateTime.UtcNow;
    [MaxLength(50)] public string? Location { get; set; }
    public string? AppCode { get; set; }
}
