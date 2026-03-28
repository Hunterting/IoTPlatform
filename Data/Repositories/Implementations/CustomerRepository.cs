using IoTPlatform.Data.Repositories.Interfaces;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;

namespace IoTPlatform.Data.Repositories.Implementations;

/// <summary>
/// 客户仓储实现类
/// </summary>
public class CustomerRepository : Repository<Customer>, ICustomerRepository
{
    public CustomerRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<Customer?> GetByCodeAsync(string code)
    {
        return await _context.Customers
            .FirstOrDefaultAsync(c => c.Code == code);
    }

    public async Task<Customer?> GetByAppCodeAsync(string appCode)
    {
        return await _context.Customers
            .FirstOrDefaultAsync(c => c.AppCode == appCode);
    }

    public async Task<bool> CodeExistsAsync(string code, long? excludeCustomerId = null)
    {
        var query = _context.Customers.AsQueryable();
        
        if (excludeCustomerId.HasValue)
        {
            query = query.Where(c => c.Id != excludeCustomerId.Value);
        }
        
        return await query.AnyAsync(c => c.Code == code);
    }

    public async Task<bool> AppCodeExistsAsync(string appCode, long? excludeCustomerId = null)
    {
        var query = _context.Customers.AsQueryable();
        
        if (excludeCustomerId.HasValue)
        {
            query = query.Where(c => c.Id != excludeCustomerId.Value);
        }
        
        return await query.AnyAsync(c => c.AppCode == appCode);
    }

    public async Task<(int DeviceCount, int ProjectCount, int UserCount)> GetCustomerStatsAsync(long customerId)
    {
        var deviceCount = await _context.Devices.CountAsync(d => d.Project != null && d.Project.CustomerId == customerId);
        var projectCount = await _context.Projects.CountAsync(p => p.CustomerId == customerId);
        var userCount = await _context.Users.CountAsync(u => u.CustomerId == customerId);
        
        return (deviceCount, projectCount, userCount);
    }

    public async Task UpdateDeviceCountAsync(long customerId, int increment = 1)
    {
        var customer = await _context.Customers.FindAsync(customerId);
        if (customer != null)
        {
            // 这里只是示例，实际上应该通过触发器或更复杂的逻辑来维护设备数量
            await Task.CompletedTask;
        }
    }
}
