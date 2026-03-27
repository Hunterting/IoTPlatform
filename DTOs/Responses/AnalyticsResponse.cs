namespace IoTPlatform.DTOs.Responses;

/// <summary>
/// 汇总统计DTO
/// </summary>
public class AnalyticsSummaryDto
{
    public int TotalDevices { get; set; }
    public int OnlineDevices { get; set; }
    public int TotalAlerts { get; set; }
    public int PendingAlerts { get; set; }
    public int TotalWorkOrders { get; set; }
    public int PendingWorkOrders { get; set; }
}

/// <summary>
/// 设备状态统计DTO
/// </summary>
public class DeviceStatusStatisticsDto
{
    public string Status { get; set; } = string.Empty;
    public int Count { get; set; }
}

/// <summary>
/// 告警趋势DTO
/// </summary>
public class AlertTrendDto
{
    public DateTime Date { get; set; }
    public string Level { get; set; } = string.Empty;
    public int Count { get; set; }
}

/// <summary>
/// 能耗统计DTO
/// </summary>
public class EnergyConsumptionDto
{
    public double TotalConsumption { get; set; }
    public double TotalCost { get; set; }
    public Dictionary<string, double> Breakdown { get; set; } = new();
}
