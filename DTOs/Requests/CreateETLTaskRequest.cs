using System.ComponentModel.DataAnnotations;

namespace IoTPlatform.DTOs.Requests;

/// <summary>
/// 创建ETL任务请求
/// </summary>
public class CreateETLTaskRequest
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? TaskType { get; set; } // transform, aggregation, export

    [MaxLength(1000)]
    public string? SourceConfig { get; set; } // JSON

    [MaxLength(1000)]
    public string? TargetConfig { get; set; } // JSON

    [MaxLength(1000)]
    public string? TransformRule { get; set; } // JSON

    [MaxLength(20)]
    public string? Schedule { get; set; } // cron expression

    [MaxLength(50)]
    public string? AppCode { get; set; }

    [MaxLength(1000)]
    public string? Description { get; set; }
}
