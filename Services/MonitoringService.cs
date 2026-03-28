using IoTPlatform.Data.Repositories.Interfaces;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Helpers;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;

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
    public async Task<PagedResponse<MonitoringDataDto>> GetMonitoringDataAsync(int page, int pageSize, string? appCode, List<long>? allowedAreaIds)
    {
        var query = _monitoringRepository.GetQueryable()
            .Include(d => d.Device)
            .Include(d => d.Device.Area);

        // 租户过滤
        if (!string.IsNullOrEmpty(appCode))
        {
            query = query.Where(d => d.AppCode == appCode);
        }

        // 区域权限过滤
        if (allowedAreaIds != null && allowedAreaIds.Count > 0)
        {
            query = query.Where(d => d.Device.AreaId == null || allowedAreaIds.Contains(d.Device.AreaId.Value));
        }

        var totalCount = await query.CountAsync();
        var dataRecords = await query
            .OrderByDescending(d => d.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(d => new MonitoringDataDto
            {
                Id = d.Id,
                DeviceId = d.DeviceId,
                DeviceName = d.Device != null ? d.Device.Name : null,
                AreaId = d.Device != null ? d.Device.AreaId : null,
                AreaName = d.Device != null && d.Device.Area != null ? d.Device.Area.Name : null,
                SensorValues = new Dictionary<string, double?>
                {
                    { "temperature", d.Temperature },
                    { "humidity", d.Humidity },
                    { "pm25", d.PM25 },
                    { "co2", d.CO2 },
                    { "co", d.CO }
                },
                Timestamp = d.Timestamp
            })
            .ToListAsync();

        return PagedResponse<MonitoringDataDto>.Create(dataRecords, totalCount, page, pageSize);
    }

    /// <summary>
    /// 获取空气质量数据
    /// </summary>
    public async Task<List<AirQualityDataDto>> GetAirQualityDataAsync(long areaId, DateTime? startTime, DateTime? endTime, string? appCode)
    {
        var query = _monitoringRepository.GetQueryable();

        // 区域过滤
        query = query.Where(a => a.AreaId == areaId);

        // 租户过滤
        if (!string.IsNullOrEmpty(appCode))
        {
            query = query.Where(a => a.AppCode == appCode);
        }

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
    public async Task<List<EnvironmentDataDto>> GetEnvironmentDataAsync(long deviceId, DateTime? startTime, DateTime? endTime, string? appCode)
    {
        var query = _monitoringRepository.GetQueryable();

        // 设备过滤
        query = query.Where(e => e.DeviceId == deviceId);

        // 租户过滤
        if (!string.IsNullOrEmpty(appCode))
        {
            query = query.Where(e => e.AppCode == appCode);
        }

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
    public async Task<MonitoringSummaryDto> GetMonitoringSummaryAsync(string? appCode)
    {
        var devicesQuery = _monitoringRepository.GetQueryable();
        if (!string.IsNullOrEmpty(appCode))
        {
            devicesQuery = devicesQuery.Where(d => d.AppCode == appCode);
        }

        var totalDevices = await devicesQuery.CountAsync();
        var onlineDevices = await devicesQuery.CountAsync(d => d.Status == "online");
        var offlineDevices = totalDevices - onlineDevices;

        var alertsQuery = _monitoringRepository.GetQueryable();
        if (!string.IsNullOrEmpty(appCode))
        {
            alertsQuery = alertsQuery.Where(a => a.AppCode == appCode);
        }

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
