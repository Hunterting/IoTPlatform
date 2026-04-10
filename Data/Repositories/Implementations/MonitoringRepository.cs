using IoTPlatform.Data.Repositories.Interfaces;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;

namespace IoTPlatform.Data.Repositories.Implementations;

/// <summary>
/// 监控数据仓储实现类
/// </summary>
public class MonitoringRepository : Repository<DeviceDataRecord>, IMonitoringRepository
{
    public MonitoringRepository(AppDbContext context) : base(context)
    {
    }

    /// <summary>
    /// 获取设备数据记录
    /// </summary>
    public async Task<IEnumerable<DeviceDataRecord>> GetDeviceDataAsync(
        long deviceId, DateTime? startTime = null, DateTime? endTime = null, int? limit = 100)
    {
        var query = _context.DeviceDataRecords
            .Where(r => r.DeviceId == deviceId);

        if (startTime.HasValue)
        {
            query = query.Where(r => r.RecordTime >= startTime.Value);
        }

        if (endTime.HasValue)
        {
            query = query.Where(r => r.RecordTime <= endTime.Value);
        }

        query = query.OrderByDescending(r => r.RecordTime);

        if (limit.HasValue && limit.Value > 0)
        {
            query = query.Take(limit.Value);
        }

        return await query.ToListAsync();
    }

    /// <summary>
    /// 获取设备最新数据
    /// </summary>
    public async Task<DeviceDataRecord?> GetLatestDeviceDataAsync(long deviceId)
    {
        return await _context.DeviceDataRecords
            .Where(r => r.DeviceId == deviceId)
            .OrderByDescending(r => r.RecordTime)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// 获取区域设备数据
    /// </summary>
    public async Task<IEnumerable<DeviceDataRecord>> GetAreaDeviceDataAsync(
        long areaId, DateTime? startTime = null, DateTime? endTime = null, int? limit = 100)
    {
        var query = _context.DeviceDataRecords
            .Include(r => r.Device)
            .Where(r => r.Device != null && r.Device.AreaId == areaId);

        if (startTime.HasValue)
        {
            query = query.Where(r => r.RecordTime >= startTime.Value);
        }

        if (endTime.HasValue)
        {
            query = query.Where(r => r.RecordTime <= endTime.Value);
        }

        query = query.OrderByDescending(r => r.RecordTime);

        if (limit.HasValue && limit.Value > 0)
        {
            query = query.Take(limit.Value);
        }

        return await query.ToListAsync();
    }

    /// <summary>
    /// 获取设备数据统计
    /// </summary>
    public async Task<DeviceDataStats> GetDeviceDataStatsAsync(
        long deviceId, DateTime? startTime = null, DateTime? endTime = null)
    {
        var query = _context.DeviceDataRecords
            .Where(r => r.DeviceId == deviceId);

        if (startTime.HasValue)
        {
            query = query.Where(r => r.RecordTime >= startTime.Value);
        }

        if (endTime.HasValue)
        {
            query = query.Where(r => r.RecordTime <= endTime.Value);
        }

        var records = await query.ToListAsync();
        
        var stats = new DeviceDataStats
        {
            DeviceId = deviceId,
            TotalRecords = records.Count,
            FirstRecordTime = records.Min(r => r.RecordTime),
            LastRecordTime = records.Max(r => r.RecordTime),
            LastUpdate = DateTime.UtcNow
        };

        // 计算平均值、最大值、最小值
        if (records.Any())
        {
            stats.AverageValues["temperature"] = records.Where(r => r.Temperature.HasValue).Average(r => r.Temperature!.Value);
            stats.AverageValues["humidity"] = records.Where(r => r.Humidity.HasValue).Average(r => r.Humidity!.Value);
            stats.AverageValues["pm25"] = records.Where(r => r.PM25.HasValue).Average(r => r.PM25!.Value);
            stats.AverageValues["co2"] = records.Where(r => r.CO2.HasValue).Average(r => r.CO2!.Value);
            stats.AverageValues["co"] = records.Where(r => r.CO.HasValue).Average(r => r.CO!.Value);

            stats.MaxValues["temperature"] = records.Where(r => r.Temperature.HasValue).Max(r => r.Temperature!.Value);
            stats.MaxValues["humidity"] = records.Where(r => r.Humidity.HasValue).Max(r => r.Humidity!.Value);
            stats.MaxValues["pm25"] = records.Where(r => r.PM25.HasValue).Max(r => r.PM25!.Value);
            stats.MaxValues["co2"] = records.Where(r => r.CO2.HasValue).Max(r => r.CO2!.Value);
            stats.MaxValues["co"] = records.Where(r => r.CO.HasValue).Max(r => r.CO!.Value);

            stats.MinValues["temperature"] = records.Where(r => r.Temperature.HasValue).Min(r => r.Temperature!.Value);
            stats.MinValues["humidity"] = records.Where(r => r.Humidity.HasValue).Min(r => r.Humidity!.Value);
            stats.MinValues["pm25"] = records.Where(r => r.PM25.HasValue).Min(r => r.PM25!.Value);
            stats.MinValues["co2"] = records.Where(r => r.CO2.HasValue).Min(r => r.CO2!.Value);
            stats.MinValues["co"] = records.Where(r => r.CO.HasValue).Min(r => r.CO!.Value);
        }

        return stats;
    }

    /// <summary>
    /// 获取空气质量数据
    /// </summary>
    public async Task<IEnumerable<AirQualityData>> GetAirQualityDataAsync(
        long? areaId = null, DateTime? startTime = null, DateTime? endTime = null, int? limit = 100)
    {
        var query = _context.AirQualityData.AsQueryable();

        if (areaId.HasValue)
        {
            query = query.Where(a => a.AreaId == areaId.Value);
        }

        if (startTime.HasValue)
        {
            query = query.Where(a => a.Timestamp >= startTime.Value);
        }

        if (endTime.HasValue)
        {
            query = query.Where(a => a.Timestamp <= endTime.Value);
        }

        query = query.OrderByDescending(a => a.Timestamp);

        if (limit.HasValue && limit.Value > 0)
        {
            query = query.Take(limit.Value);
        }

        return await query.ToListAsync();
    }

    /// <summary>
    /// 获取最新空气质量数据
    /// </summary>
    public async Task<AirQualityData?> GetLatestAirQualityDataAsync(long? areaId = null)
    {
        var query = _context.AirQualityData.AsQueryable();

        if (areaId.HasValue)
        {
            query = query.Where(a => a.AreaId == areaId.Value);
        }

        return await query.OrderByDescending(a => a.Timestamp).FirstOrDefaultAsync();
    }

    /// <summary>
    /// 获取环境监测数据
    /// </summary>
    public async Task<IEnumerable<EnvironmentData>> GetEnvironmentDataAsync(
        long? deviceId = null, long? areaId = null,
        DateTime? startTime = null, DateTime? endTime = null, int? limit = 100)
    {
        var query = _context.EnvironmentData.AsQueryable();

        if (deviceId.HasValue)
        {
            query = query.Where(e => e.DeviceId == deviceId.Value);
        }

        if (areaId.HasValue)
        {
            query = query.Where(e => e.AreaId == areaId.Value);
        }

        if (startTime.HasValue)
        {
            query = query.Where(e => e.Timestamp >= startTime.Value);
        }

        if (endTime.HasValue)
        {
            query = query.Where(e => e.Timestamp <= endTime.Value);
        }

        query = query.OrderByDescending(e => e.Timestamp);

        if (limit.HasValue && limit.Value > 0)
        {
            query = query.Take(limit.Value);
        }

        return await query.ToListAsync();
    }

    /// <summary>
    /// 获取最新环境监测数据
    /// </summary>
    public async Task<EnvironmentData?> GetLatestEnvironmentDataAsync(long? deviceId = null, long? areaId = null)
    {
        var query = _context.EnvironmentData.AsQueryable();

        if (deviceId.HasValue)
        {
            query = query.Where(e => e.DeviceId == deviceId.Value);
        }

        if (areaId.HasValue)
        {
            query = query.Where(e => e.AreaId == areaId.Value);
        }

        return await query.OrderByDescending(e => e.Timestamp).FirstOrDefaultAsync();
    }

    /// <summary>
    /// 添加设备数据记录
    /// </summary>
    public async Task AddDeviceDataRecordAsync(long deviceId, string sensorData, string? appCode = null)
    {
        var record = new DeviceDataRecord
        {
            DeviceId = deviceId,
            SensorData = sensorData,
            AppCode = appCode,
            RecordTime = DateTime.UtcNow
        };

        _context.DeviceDataRecords.Add(record);
        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// 添加空气质量数据
    /// </summary>
    public async Task AddAirQualityDataAsync(
        long? areaId, string? areaName, double? pm25, double? temperature, double? humidity, double? co2,
        double? freshAirVolume = null, double? exhaustVolume = null, double? smokeConcentration = null, double? oilFume = null,
        string? appCode = null)
    {
        var record = new AirQualityData
        {
            AreaId = areaId,
            AreaName = areaName,
            PM25 = pm25,
            Temperature = temperature,
            Humidity = humidity,
            CO2 = co2,
            FreshAirVolume = freshAirVolume,
            ExhaustVolume = exhaustVolume,
            SmokeConcentration = smokeConcentration,
            OilFume = oilFume,
            AppCode = appCode,
            Timestamp = DateTime.UtcNow
        };

        _context.AirQualityData.Add(record);
        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// 添加环境监测数据
    /// </summary>
    public async Task AddEnvironmentDataAsync(
        long deviceId, string? deviceName, long? areaId, string? areaName,
        double? pm25, double? pm10, double? co2, double? temperature, double? humidity,
        double? co = null, double? noise = null, double? combustibleGas = null, double? formaldehyde = null,
        double? smoke = null, double? tvoc = null, string? appCode = null)
    {
        var record = new EnvironmentData
        {
            DeviceId = deviceId,
            DeviceName = deviceName,
            AreaId = areaId,
            AreaName = areaName,
            PM25 = pm25,
            PM10 = pm10,
            CO2 = co2,
            Temperature = temperature,
            Humidity = humidity,
            CO = co,
            Noise = noise,
            CombustibleGas = combustibleGas,
            Formaldehyde = formaldehyde,
            Smoke = smoke,
            TVOC = tvoc,
            AppCode = appCode,
            Timestamp = DateTime.UtcNow
        };

        _context.EnvironmentData.Add(record);
        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// 获取监控数据汇总
    /// </summary>
    public async Task<MonitoringSummary> GetMonitoringSummaryAsync(string? appCode = null)
    {
        var deviceQuery = _context.Devices.AsQueryable();
        var alertQuery = _context.AlertRecords.AsQueryable();
        var dataRecordQuery = _context.DeviceDataRecords.AsQueryable();
        var airQualityQuery = _context.AirQualityData.AsQueryable();
        var envDataQuery = _context.EnvironmentData.AsQueryable();

        if (!string.IsNullOrEmpty(appCode))
        {
            deviceQuery = deviceQuery.Where(d => d.AppCode == appCode);
            alertQuery = alertQuery.Where(a => a.AppCode == appCode);
            dataRecordQuery = dataRecordQuery.Where(r => r.AppCode == appCode);
            airQualityQuery = airQualityQuery.Where(a => a.AppCode == appCode);
            envDataQuery = envDataQuery.Where(e => e.AppCode == appCode);
        }

        var totalDevices = await deviceQuery.CountAsync();
        var onlineDevices = await deviceQuery.CountAsync(d => d.Status == "online");
        var totalAlerts = await alertQuery.CountAsync();
        var pendingAlerts = await alertQuery.CountAsync(a => a.Status == "pending");
        var criticalAlerts = await alertQuery.CountAsync(a => a.Level == "critical" && a.Status != "resolved");
        var totalDataRecords = await dataRecordQuery.CountAsync();
        var airQualityRecords = await airQualityQuery.CountAsync();
        var envRecords = await envDataQuery.CountAsync();

        var latestAirQuality = await airQualityQuery.OrderByDescending(a => a.Timestamp).FirstOrDefaultAsync();

        return new MonitoringSummary
        {
            TotalDevices = totalDevices,
            OnlineDevices = onlineDevices,
            OfflineDevices = totalDevices - onlineDevices,
            TotalAlerts = totalAlerts,
            PendingAlerts = pendingAlerts,
            CriticalAlerts = criticalAlerts,
            TotalDataRecords = totalDataRecords,
            AirQualityRecords = airQualityRecords,
            EnvironmentRecords = envRecords,
            AvgPM25 = latestAirQuality?.PM25 ?? 0,
            AvgTemperature = latestAirQuality?.Temperature ?? 0,
            AvgHumidity = latestAirQuality?.Humidity ?? 0,
            AvgCO2 = latestAirQuality?.CO2 ?? 0,
            LastUpdate = DateTime.UtcNow
        };
    }

    /// <summary>
    /// 清理旧监控数据
    /// </summary>
    public async Task<int> CleanupOldMonitoringDataAsync(int daysToKeep = 30)
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-daysToKeep);
        var deletedCount = 0;

        // 清理旧的设备数据记录
        var oldDataRecords = await _context.DeviceDataRecords
            .Where(r => r.RecordTime < cutoffDate)
            .ToListAsync();
        _context.DeviceDataRecords.RemoveRange(oldDataRecords);
        deletedCount += oldDataRecords.Count;

        // 清理旧的空气质量数据
        var oldAirQuality = await _context.AirQualityData
            .Where(a => a.Timestamp < cutoffDate)
            .ToListAsync();
        _context.AirQualityData.RemoveRange(oldAirQuality);
        deletedCount += oldAirQuality.Count;

        // 清理旧的环境监测数据
        var oldEnvData = await _context.EnvironmentData
            .Where(e => e.Timestamp < cutoffDate)
            .ToListAsync();
        _context.EnvironmentData.RemoveRange(oldEnvData);
        deletedCount += oldEnvData.Count;

        await _context.SaveChangesAsync();
        return deletedCount;
    }

    /// <summary>
    /// 获取设备数据趋势
    /// </summary>
    public async Task<IEnumerable<DataTrend>> GetDeviceDataTrendAsync(
        long deviceId, string sensorType, DateTime startTime, DateTime endTime, int interval = 1)
    {
        var records = await _context.DeviceDataRecords
            .Where(r => r.DeviceId == deviceId && r.RecordTime >= startTime && r.RecordTime <= endTime)
            .OrderBy(r => r.RecordTime)
            .ToListAsync();

        var intervalHours = TimeSpan.FromHours(interval);
        var trends = new List<DataTrend>();

        var groupedRecords = records
            .GroupBy(r => new DateTime(
                r.RecordTime.Year,
                r.RecordTime.Month,
                r.RecordTime.Day,
                r.RecordTime.Hour,
                (r.RecordTime.Minute / interval) * interval,
                0))
            .OrderBy(g => g.Key);

        foreach (var group in groupedRecords)
        {
            var value = sensorType.ToLower() switch
            {
                "temperature" => group.Average(r => r.Temperature ?? 0),
                "humidity" => group.Average(r => r.Humidity ?? 0),
                "pm25" => group.Average(r => r.PM25 ?? 0),
                "co2" => group.Average(r => r.CO2 ?? 0),
                "co" => group.Average(r => r.CO ?? 0),
                _ => group.Average(r => 0)
            };

            trends.Add(new DataTrend
            {
                Time = group.Key,
                Value = value,
                SensorType = sensorType
            });
        }

        return trends;
    }
}
