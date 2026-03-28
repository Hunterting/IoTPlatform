using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IoTPlatform.Models;

[Table("etl_tasks")]
public class ETLTask
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Required][MaxLength(100)] public string Name { get; set; } = string.Empty;
    [MaxLength(50)] public string? TaskType { get; set; } // transform, aggregation, export
    [MaxLength(1000)] public string? SourceConfig { get; set; } // JSON
    [MaxLength(1000)] public string? TargetConfig { get; set; } // JSON
    [MaxLength(1000)] public string? TransformRule { get; set; } // JSON
    [MaxLength(20)] public string? Schedule { get; set; } // cron expression
    [MaxLength(20)] public string? Status { get; set; } // active, paused, completed, failed
    public DateTime? LastRunTime { get; set; }
    public DateTime? NextRunTime { get; set; }
    public bool IsActive { get; set; } = true;
    [MaxLength(20)] public string? LastRunAt { get; set; }
    [MaxLength(20)] public string? NextRunAt { get; set; }
    public string? AppCode { get; set; }
    [MaxLength(1000)] public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual DataRule? DataRule { get; set; }
}
