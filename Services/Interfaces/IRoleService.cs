using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Helpers;

namespace IoTPlatform.Services;

/// <summary>
/// 角色服务接口
/// </summary>
public interface IRoleService
{
    Task<PagedResponse<RoleDto>> GetRolesAsync(int page, int pageSize, string? keyword, string? appCode, string? currentUserRole);
    Task<RoleDto?> GetRoleAsync(long id, string? appCode, string? currentUserRole);
    Task<RoleDto> CreateRoleAsync(CreateRoleRequest request);
    Task<RoleDto> UpdateRoleAsync(long id, UpdateRoleRequest request, string? appCode, string? currentUserRole);
    Task DeleteRoleAsync(long id, string? appCode, string? currentUserRole);
    Task<List<string>> GetRolePermissionsAsync(long roleId);
}
