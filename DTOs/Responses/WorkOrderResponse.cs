namespace IoTPlatform.DTOs.Responses;

/// <summary>
/// 工单DTO
/// </summary>
public class WorkOrderDto
{
    public long Id { get; set; }
    public string OrderNo { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public long? CustomerId { get; set; }
    public long? DeviceId { get; set; }
    public long? AreaId { get; set; }
    public string? DeviceName { get; set; }
    public string? DeviceCode { get; set; }
    public string? AreaName { get; set; }
    public string? Description { get; set; }
    public string? Reporter { get; set; }
    public DateTime ReportTime { get; set; }
    public string? Assignee { get; set; }
    public DateTime? AssignTime { get; set; }
    public DateTime? EstimatedTime { get; set; }
    public DateTime? ResolvedTime { get; set; }
    public DateTime? ClosedTime { get; set; }
    public string? ResolveDescription { get; set; }
    public string? ProjectName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// 工单日志DTO
/// </summary>
public class WorkOrderLogDto
{
    public long Id { get; set; }
    public long WorkOrderId { get; set; }
    public string? Operator { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// 工单附件DTO
/// </summary>
public class WorkOrderAttachmentDto
{
    public long Id { get; set; }
    public long WorkOrderId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string? FileUrl { get; set; }
    public string? FileSize { get; set; }
    public string? FileType { get; set; }
    public DateTime CreatedAt { get; set; }
}
