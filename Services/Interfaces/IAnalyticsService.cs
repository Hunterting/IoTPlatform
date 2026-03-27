using IoTPlatform.DTOs.Responses;

namespace IoTPlatform.Services;

/// <summary>
/// 统计分析服务接口
/// </summary>
public interface IAnalyticsService
{
    Task<AnalyticsSummaryDto> GetSummaryAsync(string? appCode);
    Task<List<DeviceStatusStatisticsDto>> GetDeviceStatusStatisticsAsync(string? appCode);
    Task<List<AlertTrendDto>> GetAlertTrendAsync(DateTime startTime, DateTime endTime, string? appCode);
    Task<EnergyConsumptionDto> GetEnergyConsumptionAsync(DateTime startTime, DateTime endTime, string? appCode);
}
