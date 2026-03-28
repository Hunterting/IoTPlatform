using IoTPlatform.Data.Repositories.Interfaces;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace IoTPlatform.Data.Repositories.Implementations;

/// <summary>
/// 角色仓储实现类
/// </summary>
public class RoleRepository : Repository<Role>, IRoleRepository
{
    public RoleRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<Role?> GetByCodeAsync(string code, string? appCode = null)
    {
        var query = ApplyFilters(_context.Roles.AsQueryable(), appCode, null);
        return await query.FirstOrDefaultAsync(r => r.Code == code);
    }

    public async Task<IEnumerable<Role>> GetSystemRolesAsync()
    {
        return await _context.Roles
            .Where(r => r.IsSystem)
            .ToListAsync();
    }

    public async Task<bool> CodeExistsAsync(string code, string? appCode = null, long? excludeRoleId = null)
    {
        var query = ApplyFilters(_context.Roles.AsQueryable(), appCode, null);
        
        if (excludeRoleId.HasValue)
        {
            query = query.Where(r => r.Id != excludeRoleId.Value);
        }
        
        return await query.AnyAsync(r => r.Code == code);
    }

    public async Task<IEnumerable<Role>> GetByPermissionAsync(string permission, string? appCode = null)
    {
        var query = ApplyFilters(_context.Roles.AsQueryable(), appCode, null);
        var roles = await query.ToListAsync();
        
        // 在内存中过滤，因为Permissions是JSON字符串
        return roles.Where(r => 
            !string.IsNullOrEmpty(r.Permissions) && 
            r.Permissions.Contains(permission));
    }

    public async Task UpdatePermissionsAsync(long roleId, List<string> permissions)
    {
        var role = await _context.Roles.FindAsync(roleId);
        if (role != null)
        {
            role.Permissions = JsonSerializer.Serialize(permissions);
            role.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }
}
