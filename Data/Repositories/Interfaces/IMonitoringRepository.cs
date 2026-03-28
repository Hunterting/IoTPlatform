using IoTPlatform.Models;

namespace IoTPlatform.Data.Repositories.Interfaces;

/// <summary>
/// 监控数据仓储接口
/// </summary>
public interface IMonitoringRepository : IRepository<DeviceDataRecord>
{
    /// <summary>
    /// 获取设备数据记录
    /// </summary>
    /// <param name="deviceId">设备ID</param>
    /// <param name="startTime">开始时间</param>
    /// <param name="endTime">结束时间</param>
    /// <param name="limit">限制数量</param>
    /// <returns>数据记录列表</returns>
    Task<IEnumerable<DeviceDataRecord>> GetDeviceDataAsync(long deviceId, DateTime? startTime = null, DateTime? endTime = null, int? limit = 100);
    
    /// <summary>
    /// 获取设备最新数据
    /// </summary>
    /// <param name="deviceId">设备ID</param>
    /// <returns>最新数据记录</returns>
    Task<DeviceDataRecord?> GetLatestDeviceDataAsync(long deviceId);
    
    /// <summary>
    /// 获取区域设备数据
    /// </summary>
    /// <param name="areaId">区域ID</param>
    /// <param name="startTime">开始时间</param>
    /// <param name="endTime">结束时间</param>
    /// <param name="limit">限制数量</param>
    /// <returns>数据记录列表</returns>
    Task<IEnumerable<DeviceDataRecord>> GetAreaDeviceDataAsync(long areaId, DateTime? startTime = null, DateTime? endTime = null, int? limit = 100);
    
    /// <summary>
    /// 获取设备数据统计
    /// </summary>
    /// <param name="deviceId">设备ID</param>
    /// <param name="startTime">开始时间</param>
    /// <param name="endTime">结束时间</param>
    /// <returns>数据统计</returns>
    Task<DeviceDataStats> GetDeviceDataStatsAsync(long deviceId, DateTime? startTime = null, DateTime? endTime = null);
    
    /// <summary>
    /// 获取空气质量数据
    /// </summary>
    /// <param name="areaId">区域ID</param>
    /// <param name="startTime">开始时间</param>
    /// <param name="endTime">结束时间</param>
    /// <param name="limit">限制数量</param>
    /// <returns>空气质量数据列表</returns>
    Task<IEnumerable<AirQualityData>> GetAirQualityDataAsync(long? areaId = null, DateTime? startTime = null, DateTime? endTime = null, int? limit = 100);
    
    /// <summary>
    /// 获取最新空气质量数据
    /// </summary>
    /// <param name="areaId">区域ID</param>
    /// <returns>最新空气质量数据</returns>
    Task<AirQualityData?> GetLatestAirQualityDataAsync(long? areaId = null);
    
    /// <summary>
    /// 获取环境监测数据
    /// </summary>
    /// <param name="deviceId">设备ID</param>
    /// <param name="areaId">区域ID</param>
    /// <param name="startTime">开始时间</param>
    /// <param name="endTime">结束时间</param>
    /// <param name="limit">限制数量</param>
    /// <returns>环境监测数据列表</returns>
    Task<IEnumerable<EnvironmentData>> GetEnvironmentDataAsync(long? deviceId = null, long? areaId = null,
        DateTime? startTime = null, DateTime? endTime = null, int? limit = 100);
    
    /// <summary>
    /// 获取最新环境监测数据
    /// </summary>
    /// <param name="deviceId">设备ID</param>
    /// <param name="areaId">区域ID</param>
    /// <returns>最新环境监测数据</returns>
    Task<EnvironmentData?> GetLatestEnvironmentDataAsync(long? deviceId = null, long? areaId = null);
    
    /// <summary>
    /// 添加设备数据记录
    /// </summary>
    /// <param name="deviceId">设备ID</param>
    /// <param name="sensorData">传感器数据（JSON）</param>
    /// <param name="appCode">应用代码</param>
    Task AddDeviceDataRecordAsync(long deviceId, string sensorData, string? appCode = null);
    
    /// <summary>
    /// 添加空气质量数据
    /// </summary>
    /// <param name="areaId">区域ID</param>
    /// <param name="areaName">区域名称</param>
    /// <param name="pm25">PM2.5</param>
    /// <param name="temperature">温度</param>
    /// <param name="humidity">湿度</param>
    /// <param name="co2">二氧化碳</param>
    /// <param name="freshAirVolume">新风量</param>
    /// <param name="exhaustVolume">排风量</param>
    /// <param name="smokeConcentration">烟感浓度</param>
    /// <param name="oilFume">油烟</param>
    /// <param name="appCode">应用代码</param>
    Task AddAirQualityDataAsync(long? areaId, string? areaName, double? pm25, double? temperature, double? humidity, double? co2,
        double? freshAirVolume = null, double? exhaustVolume = null, double? smokeConcentration = null, double? oilFume = null,
        string? appCode = null);
    
    /// <summary>
    /// 添加环境监测数据
    /// </summary>
    /// <param name="deviceId">设备ID</param>
    /// <param name="deviceName">设备名称</param>
    /// <param name="areaId">区域ID</param>
    /// <param name="areaName">区域名称</param>
    /// <param name="pm25">PM2.5</param>
    /// <param name="pm10">PM10</param>
    /// <param name="co2">二氧化碳</param>
    /// <param name="temperature">温度</param>
    /// <param name="humidity">湿度</param>
    /// <param name="co">一氧化碳</param>
    /// <param name="noise">噪声</param>
    /// <param name="combustibleGas">可燃气体</param>
    /// <param name="formaldehyde">甲醛</param>
    /// <param name="smoke">烟雾</param>
    /// <param name="tvoc">总挥发性有机物</param>
    /// <param name="appCode">应用代码</param>
    Task AddEnvironmentDataAsync(long deviceId, string? deviceName, long? areaId, string? areaName,
        double? pm25, double? pm10, double? co2, double? temperature, double? humidity,
        double? co = null, double? noise = null, double? combustibleGas = null, double? formaldehyde = null,
        double? smoke = null, double? tvoc = null, string? appCode = null);
    
    /// <summary>
    /// 获取监控数据汇总
    /// </summary>
    /// <param name="appCode">应用代码</param>
    /// <returns>监控汇总信息</returns>
    Task<MonitoringSummary> GetMonitoringSummaryAsync(string? appCode = null);
    
    /// <summary>
    /// 清理旧监控数据
    /// </summary>
    /// <param name="daysToKeep">保留天数</param>
    /// <returns>清理的记录数</returns>
    Task<int> CleanupOldMonitoringDataAsync(int daysToKeep = 30);
    
    /// <summary>
    /// 获取设备数据趋势
    /// </summary>
    /// <param name="deviceId">设备ID</param>
    /// <param name="sensorType">传感器类型</param>
    /// <param name="startTime">开始时间</param>
    /// <param name="endTime">结束时间</param>
    /// <param name="interval">时间间隔（小时）</param>
    /// <returns>数据趋势</returns>
    Task<IEnumerable<DataTrend>> GetDeviceDataTrendAsync(long deviceId, string sensorType, DateTime startTime, DateTime endTime, int interval = 1);
}

/// <summary>
/// 设备数据统计
/// </summary>
public class DeviceDataStats
{
    public long DeviceId { get; set; }
    public int TotalRecords { get; set; }
    public DateTime? FirstRecordTime { get; set; }
    public DateTime? LastRecordTime { get; set; }
    public Dictionary<string, double> AverageValues { get; set; } = new();
    public Dictionary<string, double> MaxValues { get; set; } = new();
    public Dictionary<string, double> MinValues { get; set; } = new();
    public DateTime LastUpdate { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 监控汇总信息
/// </summary>
public class MonitoringSummary
{
    public int TotalDevices { get; set; }
    public int OnlineDevices { get; set; }
    public int OfflineDevices { get; set; }
    public int TotalAlerts { get; set; }
    public int PendingAlerts { get; set; }
    public int CriticalAlerts { get; set; }
    public int TotalDataRecords { get; set; }
    public int AirQualityRecords { get; set; }
    public int EnvironmentRecords { get; set; }
    public double AvgPM25 { get; set; }
    public double AvgTemperature { get; set; }
    public double AvgHumidity { get; set; }
    public double AvgCO2 { get; set; }
    public DateTime LastUpdate { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 数据趋势
/// </summary>
public class DataTrend
{
    public DateTime Time { get; set; }
    public double Value { get; set; }
    public string SensorType { get; set; } = string.Empty;
}
