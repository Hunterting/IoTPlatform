namespace IoTPlatform.DTOs.Responses;

/// <summary>
/// 告警DTO
/// </summary>
public class AlertDto
{
    public long Id { get; set; }
    public string AlertNo { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string? DeviceCode { get; set; }
    public string? Area { get; set; }
    public string AlertType { get; set; } = string.Empty;
    public string Level { get; set; } = string.Empty;
    public double? Value { get; set; }
    public double? Threshold { get; set; }
    public DateTime AlertTime { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Assignee { get; set; }
    public DateTime? ResolveTime { get; set; }
    public string? Remark { get; set; }
    public long? DeviceId { get; set; }
    public long? AreaId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// 告警处理日志DTO
/// </summary>
public class AlertProcessLogDto
{
    public long Id { get; set; }
    public long AlertId { get; set; }
    public string? Operator { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// 告警汇总DTO
/// </summary>
public class AlertSummaryDto
{
    public int TotalAlerts { get; set; }
    public int PendingAlerts { get; set; }
    public int ProcessingAlerts { get; set; }
    public int ResolvedAlerts { get; set; }
    public int IgnoredAlerts { get; set; }
    public int CriticalAlerts { get; set; }
    public int WarningAlerts { get; set; }
    public int InfoAlerts { get; set; }
    public Dictionary<string, int> AlertsByType { get; set; } = new();
    public DateTime LastUpdate { get; set; } = DateTime.UtcNow;
}
