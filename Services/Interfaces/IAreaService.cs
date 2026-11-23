using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Helpers;

namespace IoTPlatform.Services;

/// <summary>
/// 区域服务接口
/// </summary>
public interface IAreaService
{
    Task<PagedResponse<AreaDto>> GetAreasAsync(int page, int pageSize, string? keyword, string? appCode, string? currentUserRole, List<long>? allowedAreaIds);

    /// <summary>【多客户授权 T04】按授权客户集合取区域列表；原签名重载保持下级零回归。</summary>
    Task<PagedResponse<AreaDto>> GetAreasAsync(int page, int pageSize, string? keyword, string? appCode, string? currentUserRole, List<long>? allowedAreaIds, IReadOnlyCollection<long>? customerIds);

    Task<AreaDto?> GetAreaAsync(long id, string? appCode, string? currentUserRole, List<long>? allowedAreaIds);

    /// <summary>【多客户授权 T04】按授权客户集合校验区域详情；原签名重载保持下级零回归。</summary>
    Task<AreaDto?> GetAreaAsync(long id, string? appCode, string? currentUserRole, List<long>? allowedAreaIds, IReadOnlyCollection<long>? customerIds);
    Task<AreaDto> CreateAreaAsync(CreateAreaRequest request);
    Task<AreaDto> UpdateAreaAsync(long id, UpdateAreaRequest request, string? appCode, string? currentUserRole);
    Task DeleteAreaAsync(long id, string? appCode, string? currentUserRole);
    Task<List<AreaTreeNodeDto>> GetAreaTreeAsync(string? appCode, string? currentUserRole, List<long>? allowedAreaIds);

    /// <summary>【多客户授权 T04】多客户区域树：一次 SQL Contains 取树，避免 N+1。</summary>
    Task<List<AreaTreeNodeDto>> GetAreaTreeAsync(string? appCode, string? currentUserRole, List<long>? allowedAreaIds, IReadOnlyCollection<long>? customerIds);

    Task<List<AreaDto>> GetChildAreasAsync(long parentId, string? appCode, List<long>? allowedAreaIds);

    /// <summary>【多客户授权 T04】按授权客户集合过滤子区域；原签名重载保持下级零回归。</summary>
    Task<List<AreaDto>> GetChildAreasAsync(long parentId, string? appCode, List<long>? allowedAreaIds, IReadOnlyCollection<long>? customerIds);
}
