using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;

namespace IoTPlatform.Services;

/// <summary>
/// 监控服务接口
/// </summary>
public interface IMonitoringService
{
    Task<PagedResponse<MonitoringDataDto>> GetMonitoringDataAsync(int page, int pageSize, string? appCode, List<long>? allowedAreaIds);
    Task<List<AirQualityDataDto>> GetAirQualityDataAsync(long areaId, DateTime? startTime, DateTime? endTime, string? appCode);
    Task<List<EnvironmentDataDto>> GetEnvironmentDataAsync(long deviceId, DateTime? startTime, DateTime? endTime, string? appCode);
    Task<MonitoringSummaryDto> GetMonitoringSummaryAsync(string? appCode);
}
