using IoTPlatform.Data;
using IoTPlatform.DTOs.Responses;
using Microsoft.EntityFrameworkCore;

namespace IoTPlatform.Services;

/// <summary>
/// 统计分析服务实现
/// </summary>
public class AnalyticsService : IAnalyticsService
{
    private readonly AppDbContext _dbContext;

    public AnalyticsService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// 获取汇总统计
    /// </summary>
    public async Task<AnalyticsSummaryDto> GetSummaryAsync(string? appCode)
    {
        var deviceQuery = _dbContext.Devices.AsQueryable();
        var alertQuery = _dbContext.AlertRecords.AsQueryable();
        var workOrderQuery = _dbContext.WorkOrders.AsQueryable();

        if (!string.IsNullOrEmpty(appCode))
        {
            deviceQuery = deviceQuery.Where(d => d.AppCode == appCode);
            alertQuery = alertQuery.Where(a => a.AppCode == appCode);
            workOrderQuery = workOrderQuery.Where(w => w.AppCode == appCode);
        }

        var totalDevices = await deviceQuery.CountAsync();
        var onlineDevices = await deviceQuery.CountAsync(d => d.Status == "online");
        var totalAlerts = await alertQuery.CountAsync();
        var pendingAlerts = await alertQuery.CountAsync(a => a.Status == "pending");
        var totalWorkOrders = await workOrderQuery.CountAsync();
        var pendingWorkOrders = await workOrderQuery.CountAsync(w => w.Status == "pending");

        return new AnalyticsSummaryDto
        {
            TotalDevices = totalDevices,
            OnlineDevices = onlineDevices,
            TotalAlerts = totalAlerts,
            PendingAlerts = pendingAlerts,
            TotalWorkOrders = totalWorkOrders,
            PendingWorkOrders = pendingWorkOrders
        };
    }

    /// <summary>
    /// 获取设备状态统计
    /// </summary>
    public async Task<List<DeviceStatusStatisticsDto>> GetDeviceStatusStatisticsAsync(string? appCode)
    {
        var query = _dbContext.Devices.AsQueryable();

        if (!string.IsNullOrEmpty(appCode))
        {
            query = query.Where(d => d.AppCode == appCode);
        }

        var statistics = await query
            .GroupBy(d => d.Status)
            .Select(g => new DeviceStatusStatisticsDto
            {
                Status = g.Key,
                Count = g.Count()
            })
            .ToListAsync();

        return statistics;
    }

    /// <summary>
    /// 获取告警趋势
    /// </summary>
    public async Task<List<AlertTrendDto>> GetAlertTrendAsync(DateTime startTime, DateTime endTime, string? appCode)
    {
        var query = _dbContext.AlertRecords
            .Where(a => a.AlertTime >= startTime && a.AlertTime <= endTime);

        if (!string.IsNullOrEmpty(appCode))
        {
            query = query.Where(a => a.AppCode == appCode);
        }

        var trend = await query
            .GroupBy(a => new { a.AlertTime.Date, a.Level })
            .Select(g => new AlertTrendDto
            {
                Date = g.Key.Date,
                Level = g.Key.Level,
                Count = g.Count()
            })
            .OrderBy(t => t.Date)
            .ToListAsync();

        return trend;
    }

    /// <summary>
    /// 获取能耗统计
    /// </summary>
    public async Task<EnergyConsumptionDto> GetEnergyConsumptionAsync(DateTime startTime, DateTime endTime, string? appCode)
    {
        // TODO: 实现能耗统计逻辑
        return new EnergyConsumptionDto
        {
            TotalConsumption = 0,
            TotalCost = 0,
            Breakdown = new Dictionary<string, double>()
        };
    }
}
