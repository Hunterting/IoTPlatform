using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;

namespace IoTPlatform.Services;

/// <summary>
/// 告警服务接口
/// </summary>
public interface IAlertService
{
    Task<PagedResponse<AlertDto>> GetAlertsAsync(int page, int pageSize, string? status, string? level, string? alertType, string? appCode, List<long>? allowedAreaIds);
    Task<AlertDto?> GetAlertAsync(long id, string? appCode, List<long>? allowedAreaIds);
    Task<AlertDto> CreateAlertAsync(CreateAlertRequest request);
    Task<AlertDto> ProcessAlertAsync(long id, ProcessAlertRequest request);
    Task<AlertDto> AssignAlertAsync(long id, string assignee);
    Task<AlertDto> ResolveAlertAsync(long id, string? remark);
    Task<AlertDto> IgnoreAlertAsync(long id, string? remark);
    Task<List<AlertProcessLogDto>> GetAlertLogsAsync(long alertId);
    Task<AlertSummaryDto> GetAlertSummaryAsync(DateTime? startTime, DateTime? endTime, string? appCode);
    Task CheckAlertRulesAsync(DeviceDataRecord data);
}
