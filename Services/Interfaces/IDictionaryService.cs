using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Helpers;

// 授权缺口修复 B3：Service 接收 Controller 下推的范围，自身不解析 principal。

namespace IoTPlatform.Services;

/// <summary>
/// 字典服务接口
/// </summary>
public interface IDictionaryService
{
    /// <summary>
    /// 获取字典类型列表
    /// </summary>
    /// <summary>旧签名保留：内部/下级路径零回归。</summary>
    Task<PagedResponse<DictionaryTypeDto>> GetDictionaryTypesAsync(int page, int pageSize, string? keyword, string? appCode);

    /// <summary>B4/M5：Multi 授权客户集合范围下推。</summary>
    Task<PagedResponse<DictionaryTypeDto>> GetDictionaryTypesAsync(int page, int pageSize, string? keyword, string? appCode, IReadOnlyCollection<long>? customerIds, IReadOnlyCollection<string>? appCodes = null) => GetDictionaryTypesAsync(page, pageSize, keyword, appCode);

    /// <summary>
    /// 获取字典类型详情
    /// </summary>
    Task<DictionaryTypeDto?> GetDictionaryTypeAsync(long id, string? appCode);

    /// <summary>
    /// 创建字典类型
    /// </summary>
    /// <param name="request">字典类型创建请求</param>
    /// <param name="appCode">服务端从 JWT 解析的租户码，避免前端越权伪造归属</param>
    Task<DictionaryTypeDto> CreateDictionaryTypeAsync(CreateDictionaryTypeRequest request, string? appCode = null, CustomerScope? scope = null);

    /// <summary>
    /// 更新字典类型
    /// </summary>
    Task<DictionaryTypeDto> UpdateDictionaryTypeAsync(long id, UpdateDictionaryTypeRequest request, string? appCode, CustomerScope? scope = null);

    /// <summary>
    /// 删除字典类型
    /// </summary>
    Task DeleteDictionaryTypeAsync(long id, string? appCode, CustomerScope? scope = null);

    /// <summary>
    /// 根据类型获取字典项列表
    /// </summary>
    /// <summary>旧签名保留：内部/下级路径零回归。</summary>
    Task<List<DictionaryItemDto>> GetDictionaryItemsAsync(string type, string? appCode);

    /// <summary>B4/M5：Multi 授权客户集合范围下推。</summary>
    Task<List<DictionaryItemDto>> GetDictionaryItemsAsync(string type, string? appCode, IReadOnlyCollection<long>? customerIds, IReadOnlyCollection<string>? appCodes = null) => GetDictionaryItemsAsync(type, appCode);

    /// <summary>
    /// 获取字典项详情
    /// </summary>
    Task<DictionaryItemDto?> GetDictionaryItemAsync(long id, string? appCode);

    /// <summary>
    /// 创建字典项
    /// </summary>
    /// <param name="request">字典项创建请求</param>
    /// <param name="appCode">服务端从 JWT 解析的租户码，避免前端越权伪造归属</param>
    Task<DictionaryItemDto> CreateDictionaryItemAsync(CreateDictionaryItemRequest request, string? appCode = null, CustomerScope? scope = null);

    /// <summary>
    /// 更新字典项
    /// </summary>
    Task<DictionaryItemDto> UpdateDictionaryItemAsync(long id, UpdateDictionaryItemRequest request, string? appCode, CustomerScope? scope = null);

    /// <summary>
    /// 删除字典项
    /// </summary>
    Task DeleteDictionaryItemAsync(long id, string? appCode, CustomerScope? scope = null);
}
