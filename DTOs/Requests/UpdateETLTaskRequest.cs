using System.ComponentModel.DataAnnotations;

namespace IoTPlatform.DTOs.Requests;

/// <summary>
/// 更新ETL任务请求
/// </summary>
public class UpdateETLTaskRequest
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? SourceConfig { get; set; }

    [MaxLength(1000)]
    public string? TargetConfig { get; set; }

    [MaxLength(1000)]
    public string? TransformRule { get; set; }

    [MaxLength(20)]
    public string? Schedule { get; set; }

    [MaxLength(20)]
    public string? Status { get; set; } // active, paused, completed, failed

    [MaxLength(1000)]
    public string? Description { get; set; }
}
