using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Helpers;

namespace IoTPlatform.Services;

/// <summary>
/// 角色服务接口
/// </summary>
public interface IRoleService
{
    /// <summary>旧签名保留：内部/下级路径零回归。</summary>
    Task<PagedResponse<RoleDto>> GetRolesAsync(int page, int pageSize, string? keyword, string? appCode, string? currentUserRole);

    /// <summary>B4/M5：Multi 授权客户集合范围下推。</summary>
    Task<PagedResponse<RoleDto>> GetRolesAsync(int page, int pageSize, string? keyword, string? appCode, string? currentUserRole, IReadOnlyCollection<long>? customerIds, IReadOnlyCollection<string>? appCodes = null) => GetRolesAsync(page, pageSize, keyword, appCode, currentUserRole);
    Task<RoleDto?> GetRoleAsync(long id, string? appCode, string? currentUserRole);
    Task<RoleDto> CreateRoleAsync(CreateRoleRequest request, string? appCode = null, string? currentUserRole = null);

    /// <summary>
    /// M10 提权面重载：必须由 Controller 先完成归属/权限白名单校验，AppCode 严禁为 null。
    /// </summary>
    Task<RoleDto> CreateRoleAsync(CreateRoleRequest request, string? resolvedAppCode, bool customerScopeVerified);
    Task<RoleDto> UpdateRoleAsync(long id, UpdateRoleRequest request, string? appCode, string? currentUserRole);

    /// <summary>B5/M8：Controller 显式解析范围后下推的三分支改删重载；未校验通过 fail-closed。</summary>
    Task<RoleDto> UpdateRoleAsync(long id, UpdateRoleRequest request, string? appCode, CustomerScope scope, bool customerScopeVerified);
    Task DeleteRoleAsync(long id, string? appCode, string? currentUserRole);

    /// <summary>B5/M8：Controller 显式解析范围后下推的三分支删除重载；未校验通过 fail-closed。</summary>
    Task DeleteRoleAsync(long id, string? appCode, CustomerScope scope, bool customerScopeVerified);
    Task<List<string>> GetRolePermissionsAsync(long roleId);
}
