using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Helpers;

namespace IoTPlatform.Services;

/// <summary>
/// 监控服务接口
/// </summary>
public interface IMonitoringService
{
    /// <summary>旧签名保留：内部/下级路径零回归。</summary>
    Task<PagedResponse<MonitoringDataDto>> GetMonitoringDataAsync(int page, int pageSize, string? appCode, List<long>? allowedAreaIds);

    /// <summary>B4/M5：Multi 授权客户集合下推。</summary>
    Task<PagedResponse<MonitoringDataDto>> GetMonitoringDataAsync(int page, int pageSize, string? appCode, List<long>? allowedAreaIds, IReadOnlyCollection<long>? customerIds, IReadOnlyCollection<string>? appCodes = null) => GetMonitoringDataAsync(page, pageSize, appCode, allowedAreaIds);

    /// <summary>旧签名保留：内部/下级路径零回归。</summary>
    Task<List<AirQualityDataDto>> GetAirQualityDataAsync(long areaId, DateTime? startTime, DateTime? endTime, string? appCode);

    /// <summary>B4/M5：Multi 授权客户集合下推。</summary>
    Task<List<AirQualityDataDto>> GetAirQualityDataAsync(long areaId, DateTime? startTime, DateTime? endTime, string? appCode, IReadOnlyCollection<long>? customerIds, IReadOnlyCollection<string>? appCodes = null) => GetAirQualityDataAsync(areaId, startTime, endTime, appCode);

    /// <summary>旧签名保留：内部/下级路径零回归。</summary>
    Task<List<EnvironmentDataDto>> GetEnvironmentDataAsync(long deviceId, DateTime? startTime, DateTime? endTime, string? appCode);

    /// <summary>B4/M5：Multi 授权客户集合下推。</summary>
    Task<List<EnvironmentDataDto>> GetEnvironmentDataAsync(long deviceId, DateTime? startTime, DateTime? endTime, string? appCode, IReadOnlyCollection<long>? customerIds, IReadOnlyCollection<string>? appCodes = null) => GetEnvironmentDataAsync(deviceId, startTime, endTime, appCode);

    /// <summary>旧签名保留：内部/下级路径零回归。</summary>
    Task<MonitoringSummaryDto> GetMonitoringSummaryAsync(string? appCode);

    /// <summary>B4/M5：Multi 授权客户集合下推。</summary>
    Task<MonitoringSummaryDto> GetMonitoringSummaryAsync(string? appCode, IReadOnlyCollection<long>? customerIds, IReadOnlyCollection<string>? appCodes = null) => GetMonitoringSummaryAsync(appCode);
}
