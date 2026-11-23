using IoTPlatform.DTOs.Responses;
using IoTPlatform.Helpers;

namespace IoTPlatform.Services;

/// <summary>
/// 日志服务接口
/// </summary>
public interface ILogService
{
    /// <summary>
    /// 获取操作日志列表
    /// </summary>
    Task<PagedResponse<OperationLogDto>> GetOperationLogsAsync(int page, int pageSize, string? module, string? action, long? userId, DateTime? startTime, DateTime? endTime, string? appCode, string? currentUserRole);

    /// <summary>B4/M5：admin 授权客户集合下推，旧签名保持不变。</summary>
    Task<PagedResponse<OperationLogDto>> GetOperationLogsAsync(int page, int pageSize, string? module, string? action, long? userId, DateTime? startTime, DateTime? endTime, string? appCode, string? currentUserRole, IReadOnlyCollection<long>? customerIds, IReadOnlyCollection<string>? appCodes = null) => GetOperationLogsAsync(page, pageSize, module, action, userId, startTime, endTime, appCode, currentUserRole);

    /// <summary>
    /// 获取登录日志列表
    /// </summary>
    Task<PagedResponse<LoginLogDto>> GetLoginLogsAsync(int page, int pageSize, long? userId, string? status, DateTime? startTime, DateTime? endTime, string? appCode, string? currentUserRole);

    /// <summary>B4/M5：admin 授权客户集合下推，旧签名保持不变。</summary>
    Task<PagedResponse<LoginLogDto>> GetLoginLogsAsync(int page, int pageSize, long? userId, string? status, DateTime? startTime, DateTime? endTime, string? appCode, string? currentUserRole, IReadOnlyCollection<long>? customerIds, IReadOnlyCollection<string>? appCodes = null) => GetLoginLogsAsync(page, pageSize, userId, status, startTime, endTime, appCode, currentUserRole);

    /// <summary>
    /// 获取操作日志详情
    /// </summary>
    Task<OperationLogDto?> GetOperationLogAsync(long id, string? appCode, string? currentUserRole);

    /// <summary>
    /// 获取登录日志详情
    /// </summary>
    Task<LoginLogDto?> GetLoginLogAsync(long id, string? appCode, string? currentUserRole);
}
