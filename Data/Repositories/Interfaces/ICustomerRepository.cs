using IoTPlatform.Models;

namespace IoTPlatform.Data.Repositories.Interfaces;

/// <summary>
/// 客户仓储接口
/// </summary>
public interface ICustomerRepository : IRepository<Customer>
{
    /// <summary>
    /// 根据客户代码获取客户
    /// </summary>
    /// <param name="code">客户代码</param>
    /// <returns>客户对象或null</returns>
    Task<Customer?> GetByCodeAsync(string code);
    
    /// <summary>
    /// 根据应用代码获取客户
    /// </summary>
    /// <param name="appCode">应用代码</param>
    /// <returns>客户对象或null</returns>
    Task<Customer?> GetByAppCodeAsync(string appCode);
    
    /// <summary>
    /// 检查客户代码是否已存在
    /// </summary>
    /// <param name="code">客户代码</param>
    /// <param name="excludeCustomerId">排除的客户ID（用于更新操作）</param>
    /// <returns>是否已存在</returns>
    Task<bool> CodeExistsAsync(string code, long? excludeCustomerId = null);
    
    /// <summary>
    /// 检查应用代码是否已存在
    /// </summary>
    /// <param name="appCode">应用代码</param>
    /// <param name="excludeCustomerId">排除的客户ID（用于更新操作）</param>
    /// <returns>是否已存在</returns>
    Task<bool> AppCodeExistsAsync(string appCode, long? excludeCustomerId = null);
    
    /// <summary>
    /// 获取客户统计信息
    /// </summary>
    /// <param name="customerId">客户ID</param>
    /// <returns>统计信息</returns>
    Task<(int DeviceCount, int ProjectCount, int UserCount)> GetCustomerStatsAsync(long customerId);
    
    /// <summary>
    /// 更新客户设备数量
    /// </summary>
    /// <param name="customerId">客户ID</param>
    /// <param name="increment">增量（正数增加，负数减少）</param>
    Task UpdateDeviceCountAsync(long customerId, int increment = 1);
}
