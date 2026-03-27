using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Helpers;

namespace IoTPlatform.Services;

/// <summary>
/// 字典服务接口
/// </summary>
public interface IDictionaryService
{
    /// <summary>
    /// 获取字典类型列表
    /// </summary>
    Task<PagedResponse<DictionaryTypeDto>> GetDictionaryTypesAsync(int page, int pageSize, string? keyword, string? appCode);

    /// <summary>
    /// 获取字典类型详情
    /// </summary>
    Task<DictionaryTypeDto?> GetDictionaryTypeAsync(long id, string? appCode);

    /// <summary>
    /// 创建字典类型
    /// </summary>
    Task<DictionaryTypeDto> CreateDictionaryTypeAsync(CreateDictionaryTypeRequest request);

    /// <summary>
    /// 更新字典类型
    /// </summary>
    Task<DictionaryTypeDto> UpdateDictionaryTypeAsync(long id, UpdateDictionaryTypeRequest request, string? appCode);

    /// <summary>
    /// 删除字典类型
    /// </summary>
    Task DeleteDictionaryTypeAsync(long id, string? appCode);

    /// <summary>
    /// 根据类型获取字典项列表
    /// </summary>
    Task<List<DictionaryItemDto>> GetDictionaryItemsAsync(string type, string? appCode);

    /// <summary>
    /// 获取字典项详情
    /// </summary>
    Task<DictionaryItemDto?> GetDictionaryItemAsync(long id, string? appCode);

    /// <summary>
    /// 创建字典项
    /// </summary>
    Task<DictionaryItemDto> CreateDictionaryItemAsync(CreateDictionaryItemRequest request);

    /// <summary>
    /// 更新字典项
    /// </summary>
    Task<DictionaryItemDto> UpdateDictionaryItemAsync(long id, UpdateDictionaryItemRequest request, string? appCode);

    /// <summary>
    /// 删除字典项
    /// </summary>
    Task DeleteDictionaryItemAsync(long id, string? appCode);
}
