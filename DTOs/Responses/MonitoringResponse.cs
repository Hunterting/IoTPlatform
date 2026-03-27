namespace IoTPlatform.DTOs.Responses;

/// <summary>
/// 监控数据DTO
/// </summary>
public class MonitoringDataDto
{
    public long Id { get; set; }
    public long? DeviceId { get; set; }
    public string? DeviceName { get; set; }
    public long? AreaId { get; set; }
    public string? AreaName { get; set; }
    public Dictionary<string, double?>? SensorValues { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// 空气质量数据DTO
/// </summary>
public class AirQualityDataDto
{
    public long Id { get; set; }
    public long? AreaId { get; set; }
    public string? AreaName { get; set; }
    public double? FreshAirVolume { get; set; }
    public double? ExhaustVolume { get; set; }
    public double? SmokeConcentration { get; set; }
    public double? PM25 { get; set; }
    public double? Temperature { get; set; }
    public double? Humidity { get; set; }
    public double? CO2 { get; set; }
    public double? OilFume { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// 环境监测数据DTO
/// </summary>
public class EnvironmentDataDto
{
    public long Id { get; set; }
    public long DeviceId { get; set; }
    public string? DeviceName { get; set; }
    public long? AreaId { get; set; }
    public string? AreaName { get; set; }
    public double? PM25 { get; set; }
    public double? PM10 { get; set; }
    public double? CO2 { get; set; }
    public double? CO { get; set; }
    public double? Temperature { get; set; }
    public double? Humidity { get; set; }
    public double? Noise { get; set; }
    public double? CombustibleGas { get; set; }
    public double? Formaldehyde { get; set; }
    public double? Smoke { get; set; }
    public double? TVOC { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// 监控汇总DTO
/// </summary>
public class MonitoringSummaryDto
{
    public int TotalDevices { get; set; }
    public int OnlineDevices { get; set; }
    public int OfflineDevices { get; set; }
    public int TotalAlerts { get; set; }
    public int PendingAlerts { get; set; }
    public int CriticalAlerts { get; set; }
    public DateTime LastUpdate { get; set; } = DateTime.UtcNow;
}
