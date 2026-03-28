using IoTPlatform.Models;

namespace IoTPlatform.Data.Repositories.Interfaces;

/// <summary>
/// 协议配置仓储接口
/// </summary>
public interface IProtocolConfigRepository : IRepository<ProtocolConfig>
{
    /// <summary>
    /// 根据协议类型获取配置列表
    /// </summary>
    /// <param name="type">协议类型</param>
    /// <param name="appCode">应用代码</param>
    /// <returns>配置列表</returns>
    Task<IEnumerable<ProtocolConfig>> GetByTypeAsync(string type, string? appCode = null);
    
    /// <summary>
    /// 根据协议状态获取配置列表
    /// </summary>
    /// <param name="status">协议状态</param>
    /// <param name="appCode">应用代码</param>
    /// <returns>配置列表</returns>
    Task<IEnumerable<ProtocolConfig>> GetByStatusAsync(string status, string? appCode = null);
    
    /// <summary>
    /// 根据设备ID获取相关协议配置
    /// </summary>
    /// <param name="deviceId">设备ID</param>
    /// <param name="appCode">应用代码</param>
    /// <returns>协议配置列表</returns>
    Task<IEnumerable<ProtocolConfig>> GetByDeviceIdAsync(long deviceId, string? appCode = null);
    
    /// <summary>
    /// 获取活动的协议配置
    /// </summary>
    /// <param name="appCode">应用代码</param>
    /// <returns>活动配置列表</returns>
    Task<IEnumerable<ProtocolConfig>> GetActiveConfigsAsync(string? appCode = null);
    
    /// <summary>
    /// 更新协议配置状态
    /// </summary>
    /// <param name="configId">配置ID</param>
    /// <param name="status">新状态</param>
    Task UpdateStatusAsync(long configId, string status);
    
    /// <summary>
    /// 检查协议配置名称是否已存在
    /// </summary>
    /// <param name="name">配置名称</param>
    /// <param name="appCode">应用代码</param>
    /// <param name="excludeConfigId">排除的配置ID（用于更新操作）</param>
    /// <returns>是否已存在</returns>
    Task<bool> NameExistsAsync(string name, string? appCode = null, long? excludeConfigId = null);
    
    /// <summary>
    /// 获取协议统计信息
    /// </summary>
    /// <param name="appCode">应用代码</param>
    /// <returns>协议统计</returns>
    Task<ProtocolStats> GetProtocolStatsAsync(string? appCode = null);
}

/// <summary>
/// 协议统计信息
/// </summary>
public class ProtocolStats
{
    public int TotalConfigs { get; set; }
    public int ActiveConfigs { get; set; }
    public int InactiveConfigs { get; set; }
    public int ModbusConfigs { get; set; }
    public int MqttConfigs { get; set; }
    public int OpcUaConfigs { get; set; }
    public int HttpConfigs { get; set; }
    public int TcpConfigs { get; set; }
    public int BacnetConfigs { get; set; }
    public DateTime LastUpdate { get; set; } = DateTime.UtcNow;
}
