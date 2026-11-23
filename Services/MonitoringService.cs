using IoTPlatform.Data.Repositories.Interfaces;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Helpers;
using IoTPlatform.Models;
using IoTPlatform.Services.Guards;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace IoTPlatform.Services;

/// <summary>
/// 监控服务实现（使用仓储模式）
/// </summary>
public class MonitoringService : IMonitoringService
{
    private readonly IMonitoringRepository _monitoringRepository;
    private readonly IUnitOfWork _unitOfWork;

    public MonitoringService(
        IMonitoringRepository monitoringRepository,
        IUnitOfWork unitOfWork)
    {
        _monitoringRepository = monitoringRepository;
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// 获取监控数据列表
    /// </summary>
    public Task<PagedResponse<MonitoringDataDto>> GetMonitoringDataAsync(int page, int pageSize, string? appCode, List<long>? allowedAreaIds)
        => GetMonitoringDataAsync(page, pageSize, appCode, allowedAreaIds, null, null);

    /// <summary>B4/M5：admin 授权客户集合下推。</summary>
    public async Task<PagedResponse<MonitoringDataDto>> GetMonitoringDataAsync(
        int page, int pageSize, string? appCode, List<long>? allowedAreaIds,
        IReadOnlyCollection<long>? customerIds, IReadOnlyCollection<string>? appCodes = null)
    {
        var query = _monitoringRepository.GetQueryable();

        // 租户过滤
        if (!string.IsNullOrEmpty(appCode))
        {
            query = query.Where(d => d.AppCode == appCode);
        }

        query = query.ApplyQueryScope(appCode, customerIds, appCodes);

        // 区域权限过滤
        if (allowedAreaIds != null && allowedAreaIds.Count > 0)
        {
            query = query.Where(d => d.Device.AreaId == null || allowedAreaIds.Contains(d.Device.AreaId.Value));
        }

        var totalCount = await query.CountAsync();

        // 第一步：仅做可被 EF 翻译成 SQL 的原始字段投影（含 Include 关联），
        // 避免在表达式树 Lambda 中使用 ?. 或无法翻译的自定义方法（CS8072 / 运行时无法翻译）。
        var rawRecords = await query
            .Include(d => d.Device)
            .Include(d => d.Device.Area)
            .OrderByDescending(d => d.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(d => new
            {
                d.Id,
                d.DeviceId,
                DeviceName = d.Device != null ? d.Device.Name : null,
                AreaId = d.Device != null ? d.Device.AreaId : null,
                AreaName = d.Device != null && d.Device.Area != null ? d.Device.Area.Name : null,
                EnergyTypes = d.Device != null ? d.Device.EnergyTypes : null,
                d.SensorData,
                d.Level,
                d.Timestamp
            })
            .ToListAsync();

        // 第二步：在内存中做 JSON 解析与质量映射（LINQ to Objects，可自由调用自定义方法）。
        var dataRecords = rawRecords
            .Select(d => new MonitoringDataDto
            {
                Id = d.Id,
                DeviceId = d.DeviceId,
                DeviceName = d.DeviceName,
                AreaId = d.AreaId,
                AreaName = d.AreaName,
                EnergyType = ParseFirstEnergyType(d.EnergyTypes),
                SensorValues = ParseSensorData(d.SensorData),
                Quality = MapQuality(d.Level),
                Timestamp = d.Timestamp
            })
            .ToList();

        return PagedResponse<MonitoringDataDto>.Create(dataRecords, totalCount, page, pageSize);
    }

    /// <summary>
    /// 从 DeviceDataRecord.SensorData（原始上报 JSON）解析出全部数据点。
    /// 安圣等设备的上报值只落进 SensorData 原始 JSON，类型化列多为 null；
    /// 直接读 SensorData 才能保证「设备上报了什么，界面就显示什么」。
    /// </summary>
    private static Dictionary<string, object?>? ParseSensorData(string? sensorData)
    {
        if (string.IsNullOrWhiteSpace(sensorData)) return null;
        try
        {
            using var doc = JsonDocument.Parse(sensorData);
            var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                dict[prop.Name] = prop.Value.ValueKind switch
                {
                    JsonValueKind.Number => prop.Value.GetDouble(),
                    JsonValueKind.String => prop.Value.GetString(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => prop.Value.ToString()
                };
            }
            return dict;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>解析设备能源类型（JSON 数组或逗号分隔），取首个。</summary>
    private static string? ParseFirstEnergyType(string? energyTypesJson)
    {
        if (string.IsNullOrWhiteSpace(energyTypesJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(energyTypesJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in doc.RootElement.EnumerateArray())
                {
                    var v = e.GetString();
                    if (!string.IsNullOrWhiteSpace(v)) return v.Trim().ToLowerInvariant();
                }
            }
        }
        catch (JsonException) { }
        return energyTypesJson.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim().ToLowerInvariant())
            .FirstOrDefault(t => t.Length > 0);
    }

    /// <summary>将 DeviceDataRecord.Level 映射为可读质量标签。</summary>
    private static string? MapQuality(string? level) => level switch
    {
        "warning" => "警告",
        "error" => "异常",
        "info" => "良好",
        _ => "正常"
    };

    /// <summary>
    /// 获取空气质量数据
    /// </summary>
    public Task<List<AirQualityDataDto>> GetAirQualityDataAsync(long areaId, DateTime? startTime, DateTime? endTime, string? appCode)
        => GetAirQualityDataAsync(areaId, startTime, endTime, appCode, null, null);

    /// <summary>B4/M5：admin 授权客户集合下推。</summary>
    public async Task<List<AirQualityDataDto>> GetAirQualityDataAsync(
        long areaId, DateTime? startTime, DateTime? endTime, string? appCode,
        IReadOnlyCollection<long>? customerIds, IReadOnlyCollection<string>? appCodes = null)
    {
        var query = _monitoringRepository.GetQueryable();

        // 区域过滤
        query = query.Where(a => a.AreaId == areaId);

        // 租户过滤
        if (!string.IsNullOrEmpty(appCode))
        {
            query = query.Where(a => a.AppCode == appCode);
        }

        query = query.ApplyQueryScope(appCode, customerIds, appCodes);

        // 时间范围过滤
        if (startTime.HasValue)
        {
            query = query.Where(a => a.Timestamp >= startTime.Value);
        }
        if (endTime.HasValue)
        {
            query = query.Where(a => a.Timestamp <= endTime.Value);
        }

        return await query
            .OrderByDescending(a => a.Timestamp)
            .Select(a => new AirQualityDataDto
            {
                Id = a.Id,
                AreaId = a.AreaId,
                AreaName = a.AreaName,
                FreshAirVolume = a.FreshAirVolume,
                ExhaustVolume = a.ExhaustVolume,
                SmokeConcentration = a.SmokeConcentration,
                PM25 = a.PM25,
                Temperature = a.Temperature,
                Humidity = a.Humidity,
                CO2 = a.CO2,
                OilFume = a.OilFume,
                Timestamp = a.Timestamp
            })
            .ToListAsync();
    }

    /// <summary>
    /// 获取环境监测数据
    /// </summary>
    public Task<List<EnvironmentDataDto>> GetEnvironmentDataAsync(long deviceId, DateTime? startTime, DateTime? endTime, string? appCode)
        => GetEnvironmentDataAsync(deviceId, startTime, endTime, appCode, null, null);

    /// <summary>B4/M5：admin 授权客户集合下推。</summary>
    public async Task<List<EnvironmentDataDto>> GetEnvironmentDataAsync(
        long deviceId, DateTime? startTime, DateTime? endTime, string? appCode,
        IReadOnlyCollection<long>? customerIds, IReadOnlyCollection<string>? appCodes = null)
    {
        var query = _monitoringRepository.GetQueryable();

        // 设备过滤
        query = query.Where(e => e.DeviceId == deviceId);

        // 租户过滤
        if (!string.IsNullOrEmpty(appCode))
        {
            query = query.Where(e => e.AppCode == appCode);
        }

        query = query.ApplyQueryScope(appCode, customerIds, appCodes);

        // 时间范围过滤
        if (startTime.HasValue)
        {
            query = query.Where(e => e.Timestamp >= startTime.Value);
        }
        if (endTime.HasValue)
        {
            query = query.Where(e => e.Timestamp <= endTime.Value);
        }

        return await query
            .OrderByDescending(e => e.Timestamp)
            .Select(e => new EnvironmentDataDto
            {
                Id = e.Id,
                DeviceId = e.DeviceId,
                DeviceName = e.DeviceName,
                AreaId = e.AreaId,
                AreaName = e.AreaName,
                PM25 = e.PM25,
                PM10 = e.PM10,
                CO2 = e.CO2,
                CO = e.CO,
                Temperature = e.Temperature,
                Humidity = e.Humidity,
                Noise = e.Noise,
                CombustibleGas = e.CombustibleGas,
                Formaldehyde = e.Formaldehyde,
                Smoke = e.Smoke,
                TVOC = e.TVOC,
                Timestamp = e.Timestamp
            })
            .ToListAsync();
    }

    /// <summary>
    /// 获取监控汇总
    /// </summary>
    public Task<MonitoringSummaryDto> GetMonitoringSummaryAsync(string? appCode)
        => GetMonitoringSummaryAsync(appCode, null, null);

    /// <summary>B4/M5：admin 授权客户集合下推到设备/告警汇总。</summary>
    public async Task<MonitoringSummaryDto> GetMonitoringSummaryAsync(
        string? appCode, IReadOnlyCollection<long>? customerIds, IReadOnlyCollection<string>? appCodes = null)
    {
        var devicesQuery = _monitoringRepository.GetQueryable();
        if (!string.IsNullOrEmpty(appCode))
        {
            devicesQuery = devicesQuery.Where(d => d.AppCode == appCode);
        }

        var totalDevices = await devicesQuery.CountAsync();
        var onlineDevices = await devicesQuery.CountAsync(d => d.Status == "online");
        var offlineDevices = totalDevices - onlineDevices;

        devicesQuery = devicesQuery.ApplyQueryScope(appCode, customerIds, appCodes);

        var alertsQuery = _monitoringRepository.GetQueryable();
        if (!string.IsNullOrEmpty(appCode))
        {
            alertsQuery = alertsQuery.Where(a => a.AppCode == appCode);
        }
        alertsQuery = alertsQuery.ApplyQueryScope(appCode, customerIds, appCodes);

        var totalAlerts = await alertsQuery.CountAsync();
        var pendingAlerts = await alertsQuery.CountAsync(a => a.Status == "pending");
        var criticalAlerts = await alertsQuery.CountAsync(a => a.Level == "critical" && a.Status != "resolved");

        return new MonitoringSummaryDto
        {
            TotalDevices = totalDevices,
            OnlineDevices = onlineDevices,
            OfflineDevices = offlineDevices,
            TotalAlerts = totalAlerts,
            PendingAlerts = pendingAlerts,
            CriticalAlerts = criticalAlerts
        };
    }
}
