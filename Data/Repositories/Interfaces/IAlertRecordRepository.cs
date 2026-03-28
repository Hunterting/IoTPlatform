using IoTPlatform.Models;
using IoTPlatform.DTOs.Responses;

namespace IoTPlatform.Data.Repositories.Interfaces;

/// <summary>
/// 告警仓储接口
/// </summary>
public interface IAlertRecordRepository : IRepository<AlertRecord>
{
    /// <summary>
    /// 根据告警编号获取告警
    /// </summary>
    /// <param name="alertNo">告警编号</param>
    /// <param name="appCode">应用代码</param>
    /// <returns>告警对象或null</returns>
    Task<AlertRecord?> GetByAlertNoAsync(string alertNo, string? appCode = null);
    
    /// <summary>
    /// 根据设备ID获取告警列表
    /// </summary>
    /// <param name="deviceId">设备ID</param>
    /// <param name="appCode">应用代码</param>
    /// <param name="status">告警状态</param>
    /// <returns>告警列表</returns>
    Task<IEnumerable<AlertRecord>> GetByDeviceIdAsync(long deviceId, string? appCode = null, string? status = null);
    
    /// <summary>
    /// 根据区域ID获取告警列表
    /// </summary>
    /// <param name="areaId">区域ID</param>
    /// <param name="appCode">应用代码</param>
    /// <param name="status">告警状态</param>
    /// <returns>告警列表</returns>
    Task<IEnumerable<AlertRecord>> GetByAreaIdAsync(long areaId, string? appCode = null, string? status = null);
    
    /// <summary>
    /// 根据告警类型获取告警列表
    /// </summary>
    /// <param name="alertType">告警类型</param>
    /// <param name="appCode">应用代码</param>
    /// <param name="status">告警状态</param>
    /// <returns>告警列表</returns>
    Task<IEnumerable<AlertRecord>> GetByAlertTypeAsync(string alertType, string? appCode = null, string? status = null);
    
    /// <summary>
    /// 根据告警级别获取告警列表
    /// </summary>
    /// <param name="level">告警级别</param>
    /// <param name="appCode">应用代码</param>
    /// <param name="status">告警状态</param>
    /// <returns>告警列表</returns>
    Task<IEnumerable<AlertRecord>> GetByLevelAsync(string level, string? appCode = null, string? status = null);
    
    /// <summary>
    /// 根据告警状态获取告警列表
    /// </summary>
    /// <param name="status">告警状态</param>
    /// <param name="appCode">应用代码</param>
    /// <returns>告警列表</returns>
    Task<IEnumerable<AlertRecord>> GetByStatusAsync(string status, string? appCode = null);
    
    /// <summary>
    /// 获取指派给指定处理人的告警列表
    /// </summary>
    /// <param name="assignee">处理人</param>
    /// <param name="appCode">应用代码</param>
    /// <returns>告警列表</returns>
    Task<IEnumerable<AlertRecord>> GetByAssigneeAsync(string assignee, string? appCode = null);
    
    /// <summary>
    /// 更新告警状态
    /// </summary>
    /// <param name="alertId">告警ID</param>
    /// <param name="status">新状态</param>
    /// <param name="resolveTime">解决时间（如果状态为resolved）</param>
    Task UpdateStatusAsync(long alertId, string status, DateTime? resolveTime = null);
    
    /// <summary>
    /// 指派告警
    /// </summary>
    /// <param name="alertId">告警ID</param>
    /// <param name="assignee">指派给</param>
    Task AssignAlertAsync(long alertId, string assignee);
    
    /// <summary>
    /// 获取告警处理日志
    /// </summary>
    /// <param name="alertId">告警ID</param>
    /// <returns>处理日志列表</returns>
    Task<IEnumerable<AlertProcessLog>> GetProcessLogsAsync(long alertId);
    
    /// <summary>
    /// 添加告警处理日志
    /// </summary>
    /// <param name="alertId">告警ID</param>
    /// <param name="operatorName">操作人</param>
    /// <param name="action">操作</param>
    /// <param name="comment">备注</param>
    Task AddProcessLogAsync(long alertId, string operatorName, string action, string? comment = null);
    
    /// <summary>
    /// 获取告警统计信息
    /// </summary>
    /// <param name="startTime">开始时间</param>
    /// <param name="endTime">结束时间</param>
    /// <param name="appCode">应用代码</param>
    /// <returns>告警统计</returns>
    Task<AlertStats> GetAlertStatsAsync(DateTime? startTime = null, DateTime? endTime = null, string? appCode = null);
    
    /// <summary>
    /// 获取未处理的告警数量
    /// </summary>
    /// <param name="appCode">应用代码</param>
    /// <returns>未处理告警数量</returns>
    Task<int> GetPendingAlertCountAsync(string? appCode = null);
    
    /// <summary>
    /// 获取紧急告警数量
    /// </summary>
    /// <param name="appCode">应用代码</param>
    /// <returns>紧急告警数量</returns>
    Task<int> GetCriticalAlertCountAsync(string? appCode = null);

    /// <summary>
    /// 获取告警列表（带详情和分页）
    /// </summary>
    /// <param name="status">状态</param>
    /// <param name="level">级别</param>
    /// <param name="alertType">告警类型</param>
    /// <param name="appCode">应用代码</param>
    /// <param name="allowedAreaIds">允许的区域ID列表</param>
    /// <param name="page">页码</param>
    /// <param name="pageSize">页大小</param>
    /// <returns>分页结果</returns>
    Task<PagedResult<AlertRecord>> GetAlertsWithDetailsAsync(string? status = null, string? level = null, string? alertType = null, string? appCode = null, List<long>? allowedAreaIds = null, int page = 1, int pageSize = 20);

    /// <summary>
    /// 获取告警详情（带权限检查）
    /// </summary>
    /// <param name="id">告警ID</param>
    /// <param name="appCode">应用代码</param>
    /// <param name="allowedAreaIds">允许的区域ID列表</param>
    /// <returns>告警对象或null</returns>
    Task<AlertRecord?> GetAlertWithDetailsAsync(long id, string? appCode = null, List<long>? allowedAreaIds = null);

    /// <summary>
    /// 获取告警日志
    /// </summary>
    /// <param name="alertId">告警ID</param>
    /// <returns>告警日志列表</returns>
    Task<IEnumerable<AlertProcessLog>> GetAlertLogsAsync(long alertId);

    /// <summary>
    /// 获取告警摘要
    /// </summary>
    /// <param name="startTime">开始时间</param>
    /// <param name="endTime">结束时间</param>
    /// <param name="appCode">应用代码</param>
    /// <returns>告警摘要</returns>
    Task<AlertSummaryDto> GetAlertSummaryAsync(DateTime? startTime = null, DateTime? endTime = null, string? appCode = null);
}

/// <summary>
/// 分页结果
/// </summary>
public class PagedResult<T>
{
    public List<T> Data { get; set; } = new();
    public int TotalCount { get; set; }
}

/// <summary>
/// 告警统计信息
/// </summary>
public class AlertStats
{
    public int TotalAlerts { get; set; }
    public int PendingAlerts { get; set; }
    public int ProcessingAlerts { get; set; }
    public int ResolvedAlerts { get; set; }
    public int IgnoredAlerts { get; set; }
    public int CriticalAlerts { get; set; }
    public int WarningAlerts { get; set; }
    public int InfoAlerts { get; set; }
    public Dictionary<string, int> AlertsByType { get; set; } = new();
    public DateTime LastUpdate { get; set; } = DateTime.UtcNow;
}
