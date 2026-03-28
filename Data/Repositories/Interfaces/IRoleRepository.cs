using IoTPlatform.Models;

namespace IoTPlatform.Data.Repositories.Interfaces;

/// <summary>
/// 角色仓储接口
/// </summary>
public interface IRoleRepository : IRepository<Role>
{
    /// <summary>
    /// 根据角色代码获取角色
    /// </summary>
    /// <param name="code">角色代码</param>
    /// <param name="appCode">应用代码</param>
    /// <returns>角色对象或null</returns>
    Task<Role?> GetByCodeAsync(string code, string? appCode = null);
    
    /// <summary>
    /// 获取系统默认角色
    /// </summary>
    /// <returns>系统角色列表</returns>
    Task<IEnumerable<Role>> GetSystemRolesAsync();
    
    /// <summary>
    /// 检查角色代码是否已存在
    /// </summary>
    /// <param name="code">角色代码</param>
    /// <param name="appCode">应用代码</param>
    /// <param name="excludeRoleId">排除的角色ID（用于更新操作）</param>
    /// <returns>是否已存在</returns>
    Task<bool> CodeExistsAsync(string code, string? appCode = null, long? excludeRoleId = null);
    
    /// <summary>
    /// 根据权限获取角色列表
    /// </summary>
    /// <param name="permission">权限字符串</param>
    /// <param name="appCode">应用代码</param>
    /// <returns>角色列表</returns>
    Task<IEnumerable<Role>> GetByPermissionAsync(string permission, string? appCode = null);
    
    /// <summary>
    /// 更新角色权限
    /// </summary>
    /// <param name="roleId">角色ID</param>
    /// <param name="permissions">权限列表</param>
    Task UpdatePermissionsAsync(long roleId, List<string> permissions);
}
