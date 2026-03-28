using IoTPlatform.Models;

namespace IoTPlatform.Data.Repositories.Interfaces;

/// <summary>
/// 日志仓储接口
/// </summary>
public interface ILogRepository : IRepository<LoginLog>
{
    /// <summary>
    /// 获取登录日志
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="startTime">开始时间</param>
    /// <param name="endTime">结束时间</param>
    /// <param name="status">登录状态</param>
    /// <param name="limit">限制数量</param>
    /// <returns>登录日志列表</returns>
    Task<IEnumerable<LoginLog>> GetLoginLogsAsync(long? userId = null, DateTime? startTime = null, DateTime? endTime = null, 
        string? status = null, int? limit = 100);
    
    /// <summary>
    /// 根据邮箱获取登录日志
    /// </summary>
    /// <param name="email">邮箱地址</param>
    /// <param name="startTime">开始时间</param>
    /// <param name="endTime">结束时间</param>
    /// <param name="limit">限制数量</param>
    /// <returns>登录日志列表</returns>
    Task<IEnumerable<LoginLog>> GetLoginLogsByEmailAsync(string email, DateTime? startTime = null, DateTime? endTime = null, int? limit = 100);
    
    /// <summary>
    /// 获取操作日志
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="module">模块</param>
    /// <param name="action">操作</param>
    /// <param name="startTime">开始时间</param>
    /// <param name="endTime">结束时间</param>
    /// <param name="status">操作状态</param>
    /// <param name="limit">限制数量</param>
    /// <returns>操作日志列表</returns>
    Task<IEnumerable<OperationLog>> GetOperationLogsAsync(long? userId = null, string? module = null, string? action = null,
        DateTime? startTime = null, DateTime? endTime = null, string? status = null, int? limit = 100);
    
    /// <summary>
    /// 记录登录日志
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="email">邮箱</param>
    /// <param name="userName">用户名</param>
    /// <param name="ipAddress">IP地址</param>
    /// <param name="userAgent">用户代理</param>
    /// <param name="status">登录状态</param>
    /// <param name="failureReason">失败原因</param>
    /// <param name="location">位置</param>
    Task LogLoginAsync(long? userId, string? email, string? userName, string? ipAddress, string? userAgent, 
        string status, string? failureReason = null, string? location = null);
    
    /// <summary>
    /// 记录操作日志
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="userName">用户名</param>
    /// <param name="role">角色</param>
    /// <param name="module">模块</param>
    /// <param name="action">操作</param>
    /// <param name="target">目标</param>
    /// <param name="detail">详情</param>
    /// <param name="ip">IP地址</param>
    /// <param name="status">操作状态</param>
    /// <param name="duration">操作时长（毫秒）</param>
    /// <param name="appCode">应用代码</param>
    Task LogOperationAsync(long userId, string? userName, string? role, string? module, string action, string? target = null,
        string? detail = null, string? ip = null, string status = "success", int? duration = null, string? appCode = null);
    
    /// <summary>
    /// 获取日志统计信息
    /// </summary>
    /// <param name="startTime">开始时间</param>
    /// <param name="endTime">结束时间</param>
    /// <param name="appCode">应用代码</param>
    /// <returns>日志统计</returns>
    Task<LogStats> GetLogStatsAsync(DateTime? startTime = null, DateTime? endTime = null, string? appCode = null);
    
    /// <summary>
    /// 清理旧日志
    /// </summary>
    /// <param name="daysToKeep">保留天数</param>
    /// <returns>清理的记录数</returns>
    Task<int> CleanupOldLogsAsync(int daysToKeep = 90);
    
    /// <summary>
    /// 获取用户最后登录时间
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>最后登录时间</returns>
    Task<DateTime?> GetLastLoginTimeAsync(long userId);
    
    /// <summary>
    /// 获取用户登录次数统计
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="days">天数</param>
    /// <returns>登录次数</returns>
    Task<int> GetUserLoginCountAsync(long userId, int days = 30);
    
    /// <summary>
    /// 获取操作频率统计
    /// </summary>
    /// <param name="startTime">开始时间</param>
    /// <param name="endTime">结束时间</param>
    /// <param name="appCode">应用代码</param>
    /// <returns>操作频率统计</returns>
    Task<IEnumerable<OperationFrequency>> GetOperationFrequencyAsync(DateTime? startTime = null, DateTime? endTime = null, string? appCode = null);
}

/// <summary>
/// 日志统计信息
/// </summary>
public class LogStats
{
    public int TotalLogins { get; set; }
    public int SuccessfulLogins { get; set; }
    public int FailedLogins { get; set; }
    public int TotalOperations { get; set; }
    public int SuccessfulOperations { get; set; }
    public int FailedOperations { get; set; }
    public int UniqueUsers { get; set; }
    public Dictionary<string, int> OperationsByModule { get; set; } = new();
    public Dictionary<string, int> LoginsByHour { get; set; } = new();
    public DateTime LastUpdate { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 操作频率统计
/// </summary>
public class OperationFrequency
{
    public string Module { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public int Count { get; set; }
    public int AvgDuration { get; set; } // 平均时长（毫秒）
    public int SuccessCount { get; set; }
    public int FailCount { get; set; }
}
