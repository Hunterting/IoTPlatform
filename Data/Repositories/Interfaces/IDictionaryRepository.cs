using IoTPlatform.Models;

namespace IoTPlatform.Data.Repositories.Interfaces;

/// <summary>
/// 字典仓储接口
/// </summary>
public interface IDictionaryRepository : IRepository<DictionaryItem>
{
    /// <summary>
    /// 根据字典类型代码获取字典项列表
    /// </summary>
    /// <param name="typeCode">字典类型代码</param>
    /// <param name="appCode">应用代码</param>
    /// <returns>字典项列表</returns>
    Task<IEnumerable<DictionaryItem>> GetByTypeCodeAsync(string typeCode, string? appCode = null);
    
    /// <summary>
    /// 根据字典项代码获取字典项
    /// </summary>
    /// <param name="typeCode">字典类型代码</param>
    /// <param name="itemCode">字典项代码</param>
    /// <param name="appCode">应用代码</param>
    /// <returns>字典项或null</returns>
    Task<DictionaryItem?> GetByTypeAndCodeAsync(string typeCode, string itemCode, string? appCode = null);
    
    /// <summary>
    /// 根据字典项状态获取字典项列表
    /// </summary>
    /// <param name="status">字典项状态</param>
    /// <param name="appCode">应用代码</param>
    /// <returns>字典项列表</returns>
    Task<IEnumerable<DictionaryItem>> GetByStatusAsync(string status, string? appCode = null);
    
    /// <summary>
    /// 获取所有字典类型
    /// </summary>
    /// <param name="appCode">应用代码</param>
    /// <returns>字典类型列表</returns>
    Task<IEnumerable<DictionaryTypeConfig>> GetDictionaryTypesAsync(string? appCode = null);
    
    /// <summary>
    /// 根据字典类型代码获取字典类型
    /// </summary>
    /// <param name="code">字典类型代码</param>
    /// <param name="appCode">应用代码</param>
    /// <returns>字典类型或null</returns>
    Task<DictionaryTypeConfig?> GetDictionaryTypeByCodeAsync(string code, string? appCode = null);
    
    /// <summary>
    /// 检查字典类型代码是否已存在
    /// </summary>
    /// <param name="code">字典类型代码</param>
    /// <param name="appCode">应用代码</param>
    /// <param name="excludeTypeId">排除的类型ID（用于更新操作）</param>
    /// <returns>是否已存在</returns>
    Task<bool> TypeCodeExistsAsync(string code, string? appCode = null, long? excludeTypeId = null);
    
    /// <summary>
    /// 检查字典项代码是否已存在（在同一类型下）
    /// </summary>
    /// <param name="typeCode">字典类型代码</param>
    /// <param name="itemCode">字典项代码</param>
    /// <param name="appCode">应用代码</param>
    /// <param name="excludeItemId">排除的项ID（用于更新操作）</param>
    /// <returns>是否已存在</returns>
    Task<bool> ItemCodeExistsAsync(string typeCode, string itemCode, string? appCode = null, long? excludeItemId = null);
    
    /// <summary>
    /// 获取活动的字典项列表
    /// </summary>
    /// <param name="typeCode">字典类型代码</param>
    /// <param name="appCode">应用代码</param>
    /// <returns>活动字典项列表</returns>
    Task<IEnumerable<DictionaryItem>> GetActiveItemsByTypeAsync(string typeCode, string? appCode = null);
    
    /// <summary>
    /// 更新字典项状态
    /// </summary>
    /// <param name="itemId">字典项ID</param>
    /// <param name="status">新状态</param>
    Task UpdateItemStatusAsync(long itemId, string status);
    
    /// <summary>
    /// 更新字典类型状态
    /// </summary>
    /// <param name="typeId">字典类型ID</param>
    /// <param name="isActive">是否激活</param>
    Task UpdateTypeStatusAsync(long typeId, bool isActive);
    
    /// <summary>
    /// 获取字典统计信息
    /// </summary>
    /// <param name="appCode">应用代码</param>
    /// <returns>字典统计</returns>
    Task<DictionaryStats> GetDictionaryStatsAsync(string? appCode = null);
}

/// <summary>
/// 字典统计信息
/// </summary>
public class DictionaryStats
{
    public int TotalTypes { get; set; }
    public int ActiveTypes { get; set; }
    public int TotalItems { get; set; }
    public int ActiveItems { get; set; }
    public int InactiveItems { get; set; }
    public DateTime LastUpdate { get; set; } = DateTime.UtcNow;
}
