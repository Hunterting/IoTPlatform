using IoTPlatform.Models;

namespace IoTPlatform.Data.Repositories.Interfaces;

/// <summary>
/// 数据规则仓储接口
/// </summary>
public interface IDataRuleRepository : IRepository<DataRule>
{
    /// <summary>
    /// 根据规则类型获取规则列表
    /// </summary>
    /// <param name="ruleType">规则类型</param>
    /// <param name="appCode">应用代码</param>
    /// <returns>规则列表</returns>
    Task<IEnumerable<DataRule>> GetByRuleTypeAsync(string ruleType, string? appCode = null);
    
    /// <summary>
    /// 根据数据类型获取规则列表
    /// </summary>
    /// <param name="dataType">数据类型</param>
    /// <param name="appCode">应用代码</param>
    /// <returns>规则列表</returns>
    Task<IEnumerable<DataRule>> GetByDataTypeAsync(string dataType, string? appCode = null);
    
    /// <summary>
    /// 根据规则级别获取规则列表
    /// </summary>
    /// <param name="level">规则级别</param>
    /// <param name="appCode">应用代码</param>
    /// <returns>规则列表</returns>
    Task<IEnumerable<DataRule>> GetByLevelAsync(string level, string? appCode = null);
    
    /// <summary>
    /// 根据设备ID获取规则列表
    /// </summary>
    /// <param name="deviceId">设备ID</param>
    /// <param name="appCode">应用代码</param>
    /// <returns>规则列表</returns>
    Task<IEnumerable<DataRule>> GetByDeviceIdAsync(long deviceId, string? appCode = null);
    
    /// <summary>
    /// 根据区域ID获取规则列表
    /// </summary>
    /// <param name="areaId">区域ID</param>
    /// <param name="appCode">应用代码</param>
    /// <returns>规则列表</returns>
    Task<IEnumerable<DataRule>> GetByAreaIdAsync(long areaId, string? appCode = null);
    
    /// <summary>
    /// 获取活动规则列表
    /// </summary>
    /// <param name="appCode">应用代码</param>
    /// <returns>活动规则列表</returns>
    Task<IEnumerable<DataRule>> GetActiveRulesAsync(string? appCode = null);
    
    /// <summary>
    /// 更新规则状态
    /// </summary>
    /// <param name="ruleId">规则ID</param>
    /// <param name="isActive">是否激活</param>
    Task UpdateStatusAsync(long ruleId, bool isActive);
    
    /// <summary>
    /// 检查数据规则名称是否已存在
    /// </summary>
    /// <param name="name">规则名称</param>
    /// <param name="appCode">应用代码</param>
    /// <param name="excludeRuleId">排除的规则ID（用于更新操作）</param>
    /// <returns>是否已存在</returns>
    Task<bool> NameExistsAsync(string name, string? appCode = null, long? excludeRuleId = null);
    
    /// <summary>
    /// 获取数据规则统计信息
    /// </summary>
    /// <param name="appCode">应用代码</param>
    /// <returns>规则统计</returns>
    Task<DataRuleStats> GetDataRuleStatsAsync(string? appCode = null);
    
    /// <summary>
    /// 获取适用于指定设备的规则列表
    /// </summary>
    /// <param name="deviceId">设备ID</param>
    /// <param name="dataType">数据类型</param>
    /// <param name="appCode">应用代码</param>
    /// <returns>适用规则列表</returns>
    Task<IEnumerable<DataRule>> GetApplicableRulesAsync(long deviceId, string? dataType = null, string? appCode = null);
}

/// <summary>
/// 数据规则统计信息
/// </summary>
public class DataRuleStats
{
    public int TotalRules { get; set; }
    public int ActiveRules { get; set; }
    public int InactiveRules { get; set; }
    public int AlertRules { get; set; }
    public int TransformRules { get; set; }
    public int ValidationRules { get; set; }
    public int CriticalRules { get; set; }
    public int WarningRules { get; set; }
    public int InfoRules { get; set; }
    public DateTime LastUpdate { get; set; } = DateTime.UtcNow;
}
