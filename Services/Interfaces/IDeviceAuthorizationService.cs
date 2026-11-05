using IoTPlatform.DTOs.Responses;

namespace IoTPlatform.Services;

/// <summary>
/// 设备授权服务（按设备逐台授权 + 到期提醒）。
/// </summary>
public interface IDeviceAuthorizationService
{
    /// <summary>提前提醒天数阈值</summary>
    int ExpiringSoonDays { get; }

    /// <summary>授权状态列表（联表真实设备；未授权设备也列出为 pending）。appCode=null 表示超管看全部。</summary>
    Task<List<DeviceAuthorizationDto>> GetListAsync(string? appCode, CancellationToken ct = default);

    /// <summary>统计</summary>
    Task<DeviceAuthorizationSummaryDto> GetSummaryAsync(string? appCode, CancellationToken ct = default);

    /// <summary>授权/续期（仅超管可调用，由 Controller 强校验）</summary>
    Task<DeviceAuthorizationDto> GrantAsync(GrantDeviceAuthorizationRequest request, long? operatorId, string? operatorName, string? callerAppCode, CancellationToken ct = default);

    /// <summary>撤销授权（仅超管）</summary>
    Task<DeviceAuthorizationDto> RevokeAsync(RevokeDeviceAuthorizationRequest request, string? callerAppCode, CancellationToken ct = default);

    /// <summary>
    /// 设备是否因「已授权且已过期」被禁用。
    /// 重要边界：无授权记录（pending）返回 false —— 保护存量设备与真机联调。
    /// </summary>
    Task<bool> IsDeviceDisabledAsync(long deviceId, CancellationToken ct = default);

    /// <summary>扫描即将过期/已过期设备，生成去重告警（供 HostedService 调用）</summary>
    Task<int> ScanAndNotifyExpiringAsync(CancellationToken ct = default);
}
