using IoTPlatform.Models;

namespace IoTPlatform.Data.Repositories.Interfaces;

/// <summary>
/// ETL任务仓储接口
/// </summary>
public interface IETLTaskRepository : IRepository<ETLTask>
{
    /// <summary>
    /// 根据任务类型获取任务列表
    /// </summary>
    /// <param name="taskType">任务类型</param>
    /// <param name="appCode">应用代码</param>
    /// <returns>任务列表</returns>
    Task<IEnumerable<ETLTask>> GetByTaskTypeAsync(string taskType, string? appCode = null);
    
    /// <summary>
    /// 根据任务状态获取任务列表
    /// </summary>
    /// <param name="status">任务状态</param>
    /// <param name="appCode">应用代码</param>
    /// <returns>任务列表</returns>
    Task<IEnumerable<ETLTask>> GetByStatusAsync(string status, string? appCode = null);
    
    /// <summary>
    /// 获取需要运行的任务列表
    /// </summary>
    /// <param name="appCode">应用代码</param>
    /// <returns>需要运行的任务列表</returns>
    Task<IEnumerable<ETLTask>> GetTasksToRunAsync(string? appCode = null);
    
    /// <summary>
    /// 更新任务状态
    /// </summary>
    /// <param name="taskId">任务ID</param>
    /// <param name="status">新状态</param>
    /// <param name="lastRunTime">最后运行时间</param>
    /// <param name="nextRunTime">下次运行时间</param>
    Task UpdateTaskStatusAsync(long taskId, string status, DateTime? lastRunTime = null, DateTime? nextRunTime = null);
    
    /// <summary>
    /// 检查ETL任务名称是否已存在
    /// </summary>
    /// <param name="name">任务名称</param>
    /// <param name="appCode">应用代码</param>
    /// <param name="excludeTaskId">排除的任务ID（用于更新操作）</param>
    /// <returns>是否已存在</returns>
    Task<bool> NameExistsAsync(string name, string? appCode = null, long? excludeTaskId = null);
    
    /// <summary>
    /// 获取任务执行历史（最后N次）
    /// </summary>
    /// <param name="taskId">任务ID</param>
    /// <param name="limit">限制数量</param>
    /// <returns>任务执行历史</returns>
    Task<IEnumerable<TaskExecutionHistory>> GetExecutionHistoryAsync(long taskId, int limit = 10);
    
    /// <summary>
    /// 记录任务执行历史
    /// </summary>
    /// <param name="taskId">任务ID</param>
    /// <param name="status">执行状态</param>
    /// <param name="message">执行消息</param>
    /// <param name="duration">执行时长（毫秒）</param>
    Task RecordExecutionHistoryAsync(long taskId, string status, string? message = null, int? duration = null);
    
    /// <summary>
    /// 获取ETL任务统计信息
    /// </summary>
    /// <param name="appCode">应用代码</param>
    /// <returns>任务统计</returns>
    Task<ETLTaskStats> GetETLTaskStatsAsync(string? appCode = null);
}

/// <summary>
/// 任务执行历史
/// </summary>
public class TaskExecutionHistory
{
    public long Id { get; set; }
    public long TaskId { get; set; }
    public DateTime ExecutionTime { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Message { get; set; }
    public int? Duration { get; set; } // 毫秒
}

/// <summary>
/// ETL任务统计信息
/// </summary>
public class ETLTaskStats
{
    public int TotalTasks { get; set; }
    public int ActiveTasks { get; set; }
    public int PausedTasks { get; set; }
    public int FailedTasks { get; set; }
    public int CompletedTasks { get; set; }
    public int TransformTasks { get; set; }
    public int AggregationTasks { get; set; }
    public int ExportTasks { get; set; }
    public int ScheduledTasks { get; set; }
    public int ManualTasks { get; set; }
    public DateTime LastUpdate { get; set; } = DateTime.UtcNow;
}
