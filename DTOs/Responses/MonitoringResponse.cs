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
    /// <summary>设备能源类型（取 EnergyTypes 首个，如 electric/water/gas）</summary>
    public string? EnergyType { get; set; }
    /// <summary>
    /// 原始上报数据点（来自 DeviceDataRecord.SensorData JSON）。
    /// 之所以不再读类型化列（Temperature/Humidity…），是因为安圣等设备上报的值只落进
    /// SensorData 原始 JSON、类型化列多为 null；直接读 SensorData 才能保证「设备上报了什么，界面就显示什么」。
    /// </summary>
    public Dictionary<string, object?>? SensorValues { get; set; }
    /// <summary>数据质量（由 Level 映射：info→良好 / warning→警告 / error→异常 / 其它→正常）</summary>
    public string? Quality { get; set; }
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
