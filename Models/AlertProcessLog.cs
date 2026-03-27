using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IoTPlatform.Models;

[Table("alert_process_logs")]
public class AlertProcessLog
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    public long AlertId { get; set; }
    [MaxLength(100)] public string? Operator { get; set; }
    [Required, MaxLength(50)] public string Action { get; set; } = string.Empty; // assign, process, resolve, ignore, remark
    [MaxLength(2000)] public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey("AlertId")] public virtual AlertRecord? Alert { get; set; }
}
