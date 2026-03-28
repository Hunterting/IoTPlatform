using IoTPlatform.Data.Repositories.Interfaces;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;

namespace IoTPlatform.Data.Repositories.Implementations;

/// <summary>
/// 用户仓储实现类
/// </summary>
public class UserRepository : Repository<User>, IUserRepository
{
    public UserRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<User?> GetByEmailAsync(string email)
    {
        return await _context.Users
            .Include(u => u.Customer)
            .FirstOrDefaultAsync(u => u.Email == email);
    }

    public async Task<bool> ValidatePasswordAsync(string email, string password)
    {
        var user = await GetByEmailAsync(email);
        return user != null && user.Password == password;
    }

    public async Task<IEnumerable<User>> GetByRoleAsync(string role, string? appCode = null)
    {
        var query = ApplyFilters(_context.Users.Include(u => u.Customer).AsQueryable(), appCode, null);
        return await query.Where(u => u.Role == role).ToListAsync();
    }

    public async Task<IEnumerable<User>> GetByCustomerIdAsync(long customerId, string? appCode = null)
    {
        var query = ApplyFilters(_context.Users.Include(u => u.Customer).AsQueryable(), appCode, null);
        return await query.Where(u => u.CustomerId == customerId).ToListAsync();
    }

    public async Task<bool> EmailExistsAsync(string email, long? excludeUserId = null)
    {
        var query = _context.Users.AsQueryable();
        
        if (excludeUserId.HasValue)
        {
            query = query.Where(u => u.Id != excludeUserId.Value);
        }
        
        return await query.AnyAsync(u => u.Email == email);
    }

    public async Task UpdatePasswordAsync(long userId, string password)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user != null)
        {
            user.Password = password;
            user.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    public async Task UpdateLastLoginAsync(long userId)
    {
        // 这个方法应该在LoginLog中记录，这里作为示例
        await Task.CompletedTask;
    }

    public async Task<User?> GetUserDetailsAsync(long id, string? appCode = null)
    {
        var query = ApplyFilters(_context.Users.AsQueryable(), appCode, null);
        return await query
            .Include(u => u.Customer)
            .FirstOrDefaultAsync(u => u.Id == id);
    }

    protected override IQueryable<User> ApplyFilters(IQueryable<User> query, string? appCode, List<long>? allowedAreaIds)
    {
        query = base.ApplyFilters(query, appCode, allowedAreaIds);
        
        // 用户特有的过滤逻辑
        if (allowedAreaIds != null && allowedAreaIds.Any())
        {
            // 如果用户有AllowedAreaIds字段，也需要过滤
            query = query.Where(u => string.IsNullOrEmpty(u.AllowedAreaIds) || 
                u.AllowedAreaIds.Split(',').Select(long.Parse).Any(id => allowedAreaIds.Contains(id)));
        }
        
        return query;
    }
}
