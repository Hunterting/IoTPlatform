using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Helpers;
using IoTPlatform.Models;

namespace IoTPlatform.Services;

/// <summary>
/// ETL任务服务接口
/// </summary>
public interface IETLTaskService
{
    /// <summary>
    /// 获取ETL任务列表
    /// </summary>
    /// <summary>旧签名保留：内部/下级路径零回归。</summary>
    Task<PagedResponse<ETLTaskDto>> GetETLTasksAsync(int page, int pageSize, string? keyword, string? taskType, string? appCode);

    /// <summary>B4/M5：Multi 授权客户集合范围下推。</summary>
    Task<PagedResponse<ETLTaskDto>> GetETLTasksAsync(int page, int pageSize, string? keyword, string? taskType, string? appCode, IReadOnlyCollection<long>? customerIds, IReadOnlyCollection<string>? appCodes = null) => GetETLTasksAsync(page, pageSize, keyword, taskType, appCode);

    /// <summary>
    /// 获取ETL任务详情
    /// </summary>
    Task<ETLTaskDto?> GetETLTaskAsync(long id, string? appCode);

    /// <summary>
    /// 创建ETL任务
    /// </summary>
    Task<ETLTaskDto> CreateETLTaskAsync(CreateETLTaskRequest request, CustomerScope? scope = null);

    /// <summary>
    /// 更新ETL任务
    /// </summary>
    Task<ETLTaskDto> UpdateETLTaskAsync(long id, UpdateETLTaskRequest request, string? appCode, CustomerScope? scope = null);

    /// <summary>
    /// 删除ETL任务
    /// </summary>
    Task DeleteETLTaskAsync(long id, string? appCode, CustomerScope? scope = null);

    /// <summary>
    /// 启动ETL任务
    /// </summary>
    Task StartETLTaskAsync(long id, string? appCode, CustomerScope? scope = null);

    /// <summary>
    /// 停止ETL任务
    /// </summary>
    Task StopETLTaskAsync(long id, string? appCode, CustomerScope? scope = null);

    /// <summary>
    /// 执行ETL任务
    /// </summary>
    Task ExecuteETLTaskAsync(ETLTask task);
}
