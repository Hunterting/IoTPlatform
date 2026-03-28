using IoTPlatform.Models;

namespace IoTPlatform.Data.Repositories.Interfaces;

/// <summary>
/// 系统设置仓储接口
/// </summary>
public interface ISystemSettingRepository : IRepository<SystemSetting>
{
    /// <summary>
    /// 根据设置键获取设置
    /// </summary>
    /// <param name="key">设置键</param>
    /// <param name="appCode">应用代码</param>
    /// <returns>设置对象或null</returns>
    Task<SystemSetting?> GetByKeyAsync(string key, string? appCode = null);
    
    /// <summary>
    /// 根据设置分类获取设置列表
    /// </summary>
    /// <param name="category">设置分类</param>
    /// <param name="appCode">应用代码</param>
    /// <returns>设置列表</returns>
    Task<IEnumerable<SystemSetting>> GetByCategoryAsync(string category, string? appCode = null);
    
    /// <summary>
    /// 检查设置键是否已存在
    /// </summary>
    /// <param name="key">设置键</param>
    /// <param name="appCode">应用代码</param>
    /// <param name="excludeSettingId">排除的设置ID（用于更新操作）</param>
    /// <returns>是否已存在</returns>
    Task<bool> KeyExistsAsync(string key, string? appCode = null, long? excludeSettingId = null);
    
    /// <summary>
    /// 获取或创建设置（如果不存在）
    /// </summary>
    /// <param name="key">设置键</param>
    /// <param name="defaultValue">默认值</param>
    /// <param name="description">描述</param>
    /// <param name="category">分类</param>
    /// <param name="valueType">值类型</param>
    /// <param name="appCode">应用代码</param>
    /// <returns>设置对象</returns>
    Task<SystemSetting> GetOrCreateAsync(string key, string? defaultValue = null, string? description = null, 
        string? category = null, string? valueType = "string", string? appCode = null);
    
    /// <summary>
    /// 获取设置值（强类型）
    /// </summary>
    /// <typeparam name="T">值类型</typeparam>
    /// <param name="key">设置键</param>
    /// <param name="defaultValue">默认值</param>
    /// <param name="appCode">应用代码</param>
    /// <returns>设置值</returns>
    Task<T?> GetValueAsync<T>(string key, T? defaultValue = default, string? appCode = null);
    
    /// <summary>
    /// 设置值
    /// </summary>
    /// <typeparam name="T">值类型</typeparam>
    /// <param name="key">设置键</param>
    /// <param name="value">值</param>
    /// <param name="description">描述</param>
    /// <param name="category">分类</param>
    /// <param name="appCode">应用代码</param>
    Task SetValueAsync<T>(string key, T value, string? description = null, string? category = null, string? appCode = null);
    
    /// <summary>
    /// 批量获取设置值
    /// </summary>
    /// <param name="keys">设置键列表</param>
    /// <param name="appCode">应用代码</param>
    /// <returns>设置字典</returns>
    Task<Dictionary<string, string?>> GetValuesAsync(IEnumerable<string> keys, string? appCode = null);
    
    /// <summary>
    /// 批量设置值
    /// </summary>
    /// <param name="values">键值对字典</param>
    /// <param name="appCode">应用代码</param>
    Task SetValuesAsync(Dictionary<string, string> values, string? appCode = null);
    
    /// <summary>
    /// 获取所有设置分类
    /// </summary>
    /// <param name="appCode">应用代码</param>
    /// <returns>分类列表</returns>
    Task<IEnumerable<string>> GetCategoriesAsync(string? appCode = null);
}
