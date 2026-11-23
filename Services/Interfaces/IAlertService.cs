using System.Security.Claims;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Helpers;
using IoTPlatform.Models;

namespace IoTPlatform.Services;

/// <summary>
/// 告警服务接口
/// </summary>
public interface IAlertService
{
    /// <summary>旧签名保留。</summary>
    Task<PagedResponse<AlertDto>> GetAlertsAsync(int page, int pageSize, string? status, string? level, string? alertType, string? appCode, List<long>? allowedAreaIds);

    /// <summary>B4/M5：Multi 授权客户集合下推。</summary>
    Task<PagedResponse<AlertDto>> GetAlertsAsync(int page, int pageSize, string? status, string? level, string? alertType, string? appCode, List<long>? allowedAreaIds, IReadOnlyCollection<long>? customerIds, IReadOnlyCollection<string>? appCodes = null) => GetAlertsAsync(page, pageSize, status, level, alertType, appCode, allowedAreaIds);
    /// <summary>旧签名保留。</summary>
    Task<AlertDto?> GetAlertAsync(long id, string? appCode, List<long>? allowedAreaIds);

    /// <summary>B4/M5：Multi 授权客户集合下推。</summary>
    Task<AlertDto?> GetAlertAsync(long id, string? appCode, List<long>? allowedAreaIds, IReadOnlyCollection<long>? customerIds, IReadOnlyCollection<string>? appCodes = null) => GetAlertAsync(id, appCode, allowedAreaIds);
    /// <param name="appCode">租户 AppCode（由服务端从 JWT 解析，不由前端传入，避免越权伪造）</param>
    Task<AlertDto> CreateAlertAsync(CreateAlertRequest request, string? appCode = null);

    /// <summary>M1 二次防御重载：目标客户已由 Controller 通过范围服务校验。</summary>
    Task<AlertDto> CreateAlertAsync(CreateAlertRequest request, string? appCode, bool customerScopeVerified);

    Task<AlertDto> ProcessAlertAsync(long id, ProcessAlertRequest request);
    Task<AlertDto> AssignAlertAsync(long id, string assignee);
    Task<AlertDto> ResolveAlertAsync(long id, string? remark);
    Task<AlertDto> IgnoreAlertAsync(long id, string? remark);

    // ── M3 显式 principal 重载：guard 前置，旧签名保留供内部通道零回归 ──────────
    Task<AlertDto> ProcessAlertAsync(long id, ProcessAlertRequest request, ClaimsPrincipal principal);
    Task<AlertDto> AssignAlertAsync(long id, string assignee, ClaimsPrincipal principal);
    Task<AlertDto> ResolveAlertAsync(long id, string? remark, ClaimsPrincipal principal);
    Task<AlertDto> IgnoreAlertAsync(long id, string? remark, ClaimsPrincipal principal);
    Task<List<AlertProcessLogDto>> GetAlertLogsAsync(long alertId);
    /// <summary>旧签名保留。</summary>
    Task<AlertSummaryDto> GetAlertSummaryAsync(DateTime? startTime, DateTime? endTime, string? appCode);

    /// <summary>B4/M5：Multi 授权客户集合下推。</summary>
    Task<AlertSummaryDto> GetAlertSummaryAsync(DateTime? startTime, DateTime? endTime, string? appCode, IReadOnlyCollection<long>? customerIds, IReadOnlyCollection<string>? appCodes = null) => GetAlertSummaryAsync(startTime, endTime, appCode);
    Task CheckAlertRulesAsync(DeviceDataRecord data);
}
