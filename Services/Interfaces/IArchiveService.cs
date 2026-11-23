using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Helpers;

namespace IoTPlatform.Services;

/// <summary>
/// 档案服务接口
/// </summary>
public interface IArchiveService
{
    /// <summary>
    /// 获取档案列表
    /// </summary>
    /// <summary>旧签名保留：内部/下级路径零回归。</summary>
    Task<PagedResponse<ArchiveDto>> GetArchivesAsync(int page, int pageSize, string? keyword, string? type, long? areaId, string? appCode);

    /// <summary>B4/M5：Multi 授权客户集合范围下推。</summary>
    Task<PagedResponse<ArchiveDto>> GetArchivesAsync(int page, int pageSize, string? keyword, string? type, long? areaId, string? appCode, IReadOnlyCollection<long>? customerIds, IReadOnlyCollection<string>? appCodes = null) => GetArchivesAsync(page, pageSize, keyword, type, areaId, appCode);

    /// <summary>
    /// 获取档案详情
    /// </summary>
    Task<ArchiveDto?> GetArchiveAsync(long id, string? appCode);

    /// <summary>
    /// 创建档案
    /// </summary>
    Task<ArchiveDto> CreateArchiveAsync(CreateArchiveRequest request, CustomerScope? scope = null);

    /// <summary>
    /// 更新档案
    /// </summary>
    Task<ArchiveDto> UpdateArchiveAsync(long id, UpdateArchiveRequest request, string? appCode, CustomerScope? scope = null);

    /// <summary>
    /// 删除档案
    /// </summary>
    Task DeleteArchiveAsync(long id, string? appCode, CustomerScope? scope = null);

    /// <summary>
    /// 获取档案设备标记
    /// </summary>
    Task<List<ArchiveDeviceMarkerDto>> GetArchiveMarkersAsync(long archiveId, string? appCode, CustomerScope? scope = null);
}
