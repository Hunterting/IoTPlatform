namespace IoTPlatform.DTOs.Responses;

public class ProjectResponse
{
    public long Id { get; set; }
    public long CustomerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public int DeviceCount { get; set; }
    public DateTime? OnlineDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? AppCode { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<WorkSummaryResponse> WorkSummaries { get; set; } = new();
}

public class WorkSummaryResponse
{
    public long Id { get; set; }
    public long ProjectId { get; set; }
    public string? FeedbackPerson { get; set; }
    public string? Assignee { get; set; }
    public string? Assistant { get; set; }
    public string? WorkContent { get; set; }
    public DateTime Date { get; set; }
    public string? AppCode { get; set; }
}
