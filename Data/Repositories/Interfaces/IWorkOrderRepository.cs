using IoTPlatform.Models;

namespace IoTPlatform.Data.Repositories.Interfaces;

/// <summary>
/// 工单仓储接口
/// </summary>
public interface IWorkOrderRepository : IRepository<WorkOrder>
{
    /// <summary>
    /// 根据工单编号获取工单
    /// </summary>
    /// <param name="orderNo">工单编号</param>
    /// <param name="appCode">应用代码</param>
    /// <returns>工单对象或null</returns>
    Task<WorkOrder?> GetByOrderNoAsync(string orderNo, string? appCode = null);
    
    /// <summary>
    /// 根据设备ID获取工单列表
    /// </summary>
    /// <param name="deviceId">设备ID</param>
    /// <param name="appCode">应用代码</param>
    /// <param name="status">工单状态</param>
    /// <returns>工单列表</returns>
    Task<IEnumerable<WorkOrder>> GetByDeviceIdAsync(long deviceId, string? appCode = null, string? status = null);
    
    /// <summary>
    /// 根据区域ID获取工单列表
    /// </summary>
    /// <param name="areaId">区域ID</param>
    /// <param name="appCode">应用代码</param>
    /// <param name="status">工单状态</param>
    /// <returns>工单列表</returns>
    Task<IEnumerable<WorkOrder>> GetByAreaIdAsync(long areaId, string? appCode = null, string? status = null);
    
    /// <summary>
    /// 根据客户ID获取工单列表
    /// </summary>
    /// <param name="customerId">客户ID</param>
    /// <param name="appCode">应用代码</param>
    /// <param name="status">工单状态</param>
    /// <returns>工单列表</returns>
    Task<IEnumerable<WorkOrder>> GetByCustomerIdAsync(long customerId, string? appCode = null, string? status = null);
    
    /// <summary>
    /// 根据工单类型获取工单列表
    /// </summary>
    /// <param name="type">工单类型</param>
    /// <param name="appCode">应用代码</param>
    /// <param name="status">工单状态</param>
    /// <returns>工单列表</returns>
    Task<IEnumerable<WorkOrder>> GetByTypeAsync(string type, string? appCode = null, string? status = null);
    
    /// <summary>
    /// 根据工单优先级获取工单列表
    /// </summary>
    /// <param name="priority">工单优先级</param>
    /// <param name="appCode">应用代码</param>
    /// <param name="status">工单状态</param>
    /// <returns>工单列表</returns>
    Task<IEnumerable<WorkOrder>> GetByPriorityAsync(string priority, string? appCode = null, string? status = null);
    
    /// <summary>
    /// 根据工单状态获取工单列表
    /// </summary>
    /// <param name="status">工单状态</param>
    /// <param name="appCode">应用代码</param>
    /// <returns>工单列表</returns>
    Task<IEnumerable<WorkOrder>> GetByStatusAsync(string status, string? appCode = null);
    
    /// <summary>
    /// 获取指派给指定处理人的工单列表
    /// </summary>
    /// <param name="assignee">处理人</param>
    /// <param name="appCode">应用代码</param>
    /// <returns>工单列表</returns>
    Task<IEnumerable<WorkOrder>> GetByAssigneeAsync(string assignee, string? appCode = null);
    
    /// <summary>
    /// 获取指定上报人的工单列表
    /// </summary>
    /// <param name="reporter">上报人</param>
    /// <param name="appCode">应用代码</param>
    /// <returns>工单列表</returns>
    Task<IEnumerable<WorkOrder>> GetByReporterAsync(string reporter, string? appCode = null);
    
    /// <summary>
    /// 更新工单状态
    /// </summary>
    /// <param name="workOrderId">工单ID</param>
    /// <param name="status">新状态</param>
    /// <param name="resolvedTime">解决时间（如果状态为resolved）</param>
    /// <param name="closedTime">关闭时间（如果状态为closed）</param>
    Task UpdateStatusAsync(long workOrderId, string status, DateTime? resolvedTime = null, DateTime? closedTime = null);
    
    /// <summary>
    /// 指派工单
    /// </summary>
    /// <param name="workOrderId">工单ID</param>
    /// <param name="assignee">指派给</param>
    /// <param name="estimatedTime">预计完成时间</param>
    Task AssignWorkOrderAsync(long workOrderId, string assignee, DateTime? estimatedTime = null);
    
    /// <summary>
    /// 获取工单日志
    /// </summary>
    /// <param name="workOrderId">工单ID</param>
    /// <returns>日志列表</returns>
    Task<IEnumerable<WorkOrderLog>> GetLogsAsync(long workOrderId);
    
    /// <summary>
    /// 添加工单日志
    /// </summary>
    /// <param name="workOrderId">工单ID</param>
    /// <param name="operatorName">操作人</param>
    /// <param name="action">操作</param>
    /// <param name="comment">备注</param>
    Task AddLogAsync(long workOrderId, string operatorName, string action, string? comment = null);
    
    /// <summary>
    /// 获取工单附件
    /// </summary>
    /// <param name="workOrderId">工单ID</param>
    /// <returns>附件列表</returns>
    Task<IEnumerable<WorkOrderAttachment>> GetAttachmentsAsync(long workOrderId);
    
    /// <summary>
    /// 添加工单附件
    /// </summary>
    /// <param name="workOrderId">工单ID</param>
    /// <param name="fileName">文件名</param>
    /// <param name="fileUrl">文件URL</param>
    /// <param name="fileSize">文件大小</param>
    /// <param name="fileType">文件类型</param>
    Task AddAttachmentAsync(long workOrderId, string fileName, string fileUrl, string? fileSize = null, string? fileType = null);
    
    /// <summary>
    /// 获取工单统计信息
    /// </summary>
    /// <param name="startTime">开始时间</param>
    /// <param name="endTime">结束时间</param>
    /// <param name="appCode">应用代码</param>
    /// <returns>工单统计</returns>
    Task<WorkOrderStats> GetWorkOrderStatsAsync(DateTime? startTime = null, DateTime? endTime = null, string? appCode = null);
    
    /// <summary>
    /// 获取未处理的工单数量
    /// </summary>
    /// <param name="appCode">应用代码</param>
    /// <returns>未处理工单数量</returns>
    Task<int> GetPendingWorkOrderCountAsync(string? appCode = null);
    
    /// <summary>
    /// 获取紧急工单数量
    /// </summary>
    /// <param name="appCode">应用代码</param>
    /// <returns>紧急工单数量</returns>
    Task<int> GetUrgentWorkOrderCountAsync(string? appCode = null);
}

/// <summary>
/// 工单统计信息
/// </summary>
public class WorkOrderStats
{
    public int TotalWorkOrders { get; set; }
    public int PendingWorkOrders { get; set; }
    public int AssignedWorkOrders { get; set; }
    public int InProgressWorkOrders { get; set; }
    public int ResolvedWorkOrders { get; set; }
    public int ClosedWorkOrders { get; set; }
    public int UrgentWorkOrders { get; set; }
    public int HighPriorityWorkOrders { get; set; }
    public int MediumPriorityWorkOrders { get; set; }
    public int LowPriorityWorkOrders { get; set; }
    public Dictionary<string, int> WorkOrdersByType { get; set; } = new();
    public DateTime LastUpdate { get; set; } = DateTime.UtcNow;
}
