namespace IoTPlatform.DTOs.Responses;

/// <summary>
/// ETL任务响应DTO
/// </summary>
public class ETLTaskDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? TaskType { get; set; }
    public string? SourceConfig { get; set; }
    public string? TargetConfig { get; set; }
    public string? TransformRule { get; set; }
    public string? Schedule { get; set; }
    public string? Status { get; set; }
    public DateTime? LastRunTime { get; set; }
    public DateTime? NextRunTime { get; set; }
    public string? AppCode { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
