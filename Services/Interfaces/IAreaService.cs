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

    /// <summary>M1 二次防御重载：目标客户已由 Controller 通过 ICustomerScopeService 校验。</summary>
    Task<AreaDto> CreateAreaAsync(CreateAreaRequest request, string? appCode, bool customerScopeVerified);
    Task<AreaDto> UpdateAreaAsync(long id, UpdateAreaRequest request, string? appCode, string? currentUserRole);

    /// <summary>B5/M8：Controller 显式解析范围后下推的三分支改删重载；未校验通过 fail-closed。</summary>
    Task<AreaDto> UpdateAreaAsync(long id, UpdateAreaRequest request, string? appCode, CustomerScope scope, bool customerScopeVerified);
    Task DeleteAreaAsync(long id, string? appCode, string? currentUserRole);

    /// <summary>B5/M8：Controller 显式解析范围后下推的三分支删除重载；未校验通过 fail-closed。</summary>
    Task DeleteAreaAsync(long id, string? appCode, CustomerScope scope, bool customerScopeVerified);
    Task<List<AreaTreeNodeDto>> GetAreaTreeAsync(string? appCode, string? currentUserRole, List<long>? allowedAreaIds);

    /// <summary>【多客户授权 T04】多客户区域树：一次 SQL Contains 取树，避免 N+1。</summary>
    Task<List<AreaTreeNodeDto>> GetAreaTreeAsync(string? appCode, string? currentUserRole, List<long>? allowedAreaIds, IReadOnlyCollection<long>? customerIds);

    Task<List<AreaDto>> GetChildAreasAsync(long parentId, string? appCode, List<long>? allowedAreaIds);

    /// <summary>【多客户授权 T04】按授权客户集合过滤子区域；原签名重载保持下级零回归。</summary>
    Task<List<AreaDto>> GetChildAreasAsync(long parentId, string? appCode, List<long>? allowedAreaIds, IReadOnlyCollection<long>? customerIds);
}
