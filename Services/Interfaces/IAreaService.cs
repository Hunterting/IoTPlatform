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
    Task<AreaDto?> GetAreaAsync(long id, string? appCode, string? currentUserRole, List<long>? allowedAreaIds);
    Task<AreaDto> CreateAreaAsync(CreateAreaRequest request);
    Task<AreaDto> UpdateAreaAsync(long id, UpdateAreaRequest request, string? appCode, string? currentUserRole);
    Task DeleteAreaAsync(long id, string? appCode, string? currentUserRole);
    Task<List<AreaTreeNodeDto>> GetAreaTreeAsync(string? appCode, string? currentUserRole, List<long>? allowedAreaIds);
    Task<List<AreaDto>> GetChildAreasAsync(long parentId, string? appCode, List<long>? allowedAreaIds);
}
