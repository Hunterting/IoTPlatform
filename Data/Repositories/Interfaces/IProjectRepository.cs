using IoTPlatform.Models;

namespace IoTPlatform.Data.Repositories.Interfaces;

/// <summary>
/// 项目仓储接口
/// </summary>
public interface IProjectRepository : IRepository<Project>
{
    /// <summary>
    /// 根据客户ID获取项目列表
    /// </summary>
    /// <param name="customerId">客户ID</param>
    /// <param name="appCode">应用代码</param>
    /// <returns>项目列表</returns>
    Task<IEnumerable<Project>> GetByCustomerIdAsync(long customerId, string? appCode = null);
    
    /// <summary>
    /// 获取项目详情（包含关联的客户、合同和设备信息）
    /// </summary>
    /// <param name="id">项目ID</param>
    /// <param name="appCode">应用代码</param>
    /// <returns>项目详情或null</returns>
    Task<Project?> GetProjectDetailsAsync(long id, string? appCode = null);
    
    /// <summary>
    /// 获取项目统计信息
    /// </summary>
    /// <param name="projectId">项目ID</param>
    /// <returns>统计信息</returns>
    Task<(int DeviceCount, int ContractCount, int WorkSummaryCount)> GetProjectStatsAsync(long projectId);
    
    /// <summary>
    /// 更新项目设备数量
    /// </summary>
    /// <param name="projectId">项目ID</param>
    /// <param name="increment">增量（正数增加，负数减少）</param>
    Task UpdateDeviceCountAsync(long projectId, int increment = 1);
    
    /// <summary>
    /// 获取项目合同列表
    /// </summary>
    /// <param name="projectId">项目ID</param>
    /// <returns>合同列表</returns>
    Task<IEnumerable<Contract>> GetContractsAsync(long projectId);
    
    /// <summary>
    /// 获取项目工作纪要列表
    /// </summary>
    /// <param name="projectId">项目ID</param>
    /// <returns>工作纪要列表</returns>
    Task<IEnumerable<WorkSummary>> GetWorkSummariesAsync(long projectId);
}
