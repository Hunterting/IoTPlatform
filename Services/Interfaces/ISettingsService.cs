using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Helpers;

namespace IoTPlatform.Services;

/// <summary>
/// 系统设置服务接口
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// 获取系统设置列表
    /// </summary>
    /// <summary>旧签名保留：内部/下级路径零回归。</summary>
    Task<PagedResponse<SettingDto>> GetSettingsAsync(int page, int pageSize, string? category, string? keyword, string? appCode);

    /// <summary>B4/M5：Multi 授权客户集合范围下推。</summary>
    Task<PagedResponse<SettingDto>> GetSettingsAsync(int page, int pageSize, string? category, string? keyword, string? appCode, IReadOnlyCollection<long>? customerIds, IReadOnlyCollection<string>? appCodes = null) => GetSettingsAsync(page, pageSize, category, keyword, appCode);

    /// <summary>
    /// 根据键获取设置
    /// </summary>
    Task<SettingDto?> GetSettingAsync(string key, string? appCode);

    /// <summary>
    /// 根据分类获取设置
    /// </summary>
    /// <summary>旧签名保留。</summary>
    Task<Dictionary<string, string?>> GetSettingsByCategoryAsync(string category, string? appCode);

    /// <summary>B4/M5：Multi 授权客户集合下推（共享系统设置仍可读）。</summary>
    Task<Dictionary<string, string?>> GetSettingsByCategoryAsync(
        string category, string? appCode,
        IReadOnlyCollection<long>? customerIds, IReadOnlyCollection<string>? appCodes = null)
        => GetSettingsByCategoryAsync(category, appCode);

    /// <summary>
    /// 创建系统设置
    /// </summary>
    Task<SettingDto> CreateSettingAsync(CreateSettingRequest request, CustomerScope? scope = null);

    /// <summary>
    /// 更新系统设置
    /// </summary>
    Task<SettingDto> UpdateSettingAsync(string key, UpdateSettingRequest request, string? appCode, CustomerScope? scope = null);

    /// <summary>
    /// 删除系统设置
    /// </summary>
    Task DeleteSettingAsync(string key, string? appCode, CustomerScope? scope = null);
}
