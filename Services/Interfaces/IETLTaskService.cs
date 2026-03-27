using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Helpers;

namespace IoTPlatform.Services;

/// <summary>
/// ETL任务服务接口
/// </summary>
public interface IETLTaskService
{
    /// <summary>
    /// 获取ETL任务列表
    /// </summary>
    Task<PagedResponse<ETLTaskDto>> GetETLTasksAsync(int page, int pageSize, string? keyword, string? taskType, string? appCode);

    /// <summary>
    /// 获取ETL任务详情
    /// </summary>
    Task<ETLTaskDto?> GetETLTaskAsync(long id, string? appCode);

    /// <summary>
    /// 创建ETL任务
    /// </summary>
    Task<ETLTaskDto> CreateETLTaskAsync(CreateETLTaskRequest request);

    /// <summary>
    /// 更新ETL任务
    /// </summary>
    Task<ETLTaskDto> UpdateETLTaskAsync(long id, UpdateETLTaskRequest request, string? appCode);

    /// <summary>
    /// 删除ETL任务
    /// </summary>
    Task DeleteETLTaskAsync(long id, string? appCode);

    /// <summary>
    /// 启动ETL任务
    /// </summary>
    Task StartETLTaskAsync(long id, string? appCode);

    /// <summary>
    /// 停止ETL任务
    /// </summary>
    Task StopETLTaskAsync(long id, string? appCode);

    /// <summary>
    /// 执行ETL任务
    /// </summary>
    Task ExecuteETLTaskAsync(ETLTask task);
}
