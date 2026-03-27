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
    Task<PagedResponse<SettingDto>> GetSettingsAsync(int page, int pageSize, string? category, string? keyword, string? appCode);

    /// <summary>
    /// 根据键获取设置
    /// </summary>
    Task<SettingDto?> GetSettingAsync(string key, string? appCode);

    /// <summary>
    /// 根据分类获取设置
    /// </summary>
    Task<Dictionary<string, string?>> GetSettingsByCategoryAsync(string category, string? appCode);

    /// <summary>
    /// 创建系统设置
    /// </summary>
    Task<SettingDto> CreateSettingAsync(CreateSettingRequest request);

    /// <summary>
    /// 更新系统设置
    /// </summary>
    Task<SettingDto> UpdateSettingAsync(string key, UpdateSettingRequest request, string? appCode);

    /// <summary>
    /// 删除系统设置
    /// </summary>
    Task DeleteSettingAsync(string key, string? appCode);
}
