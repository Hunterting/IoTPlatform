using IoTPlatform.Data.Repositories.Interfaces;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;

namespace IoTPlatform.Data.Repositories.Implementations;

/// <summary>
/// 项目仓储实现类
/// </summary>
public class ProjectRepository : Repository<Project>, IProjectRepository
{
    public ProjectRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<Project>> GetByCustomerIdAsync(long customerId, string? appCode = null)
    {
        var query = ApplyFilters(_context.Projects.AsQueryable(), appCode, null);
        return await query
            .Where(p => p.CustomerId == customerId)
            .Include(p => p.Customer)
            .Include(p => p.Contracts)
            .ToListAsync();
    }

    public async Task<Project?> GetProjectDetailsAsync(long id, string? appCode = null)
    {
        var query = ApplyFilters(_context.Projects.AsQueryable(), appCode, null);
        return await query
            .Where(p => p.Id == id)
            .Include(p => p.Customer)
            .Include(p => p.Contracts)
            .Include(p => p.WorkSummaries)
            .Include(p => p.Devices)
            .FirstOrDefaultAsync();
    }

    public async Task<(int DeviceCount, int ContractCount, int WorkSummaryCount)> GetProjectStatsAsync(long projectId)
    {
        var query = ApplyFilters(_context.Projects.AsQueryable(), null, null);
        
        var project = await query
            .Where(p => p.Id == projectId)
            .Select(p => new
            {
                DeviceCount = p.Devices.Count(),
                ContractCount = p.Contracts.Count(),
                WorkSummaryCount = p.WorkSummaries.Count()
            })
            .FirstOrDefaultAsync();

        return project != null 
            ? (project.DeviceCount, project.ContractCount, project.WorkSummaryCount)
            : (0, 0, 0);
    }

    public async Task UpdateDeviceCountAsync(long projectId, int increment = 1)
    {
        var project = await _context.Projects.FindAsync(projectId);
        if (project != null)
        {
            // 如果项目有设备数量字段，可以在这里更新
            // project.DeviceCount += increment;
            project.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    public async Task<IEnumerable<Contract>> GetContractsAsync(long projectId)
    {
        var query = ApplyFilters(_context.Projects.AsQueryable(), null, null);
        var project = await query
            .Where(p => p.Id == projectId)
            .Include(p => p.Contracts)
            .FirstOrDefaultAsync();

        return project?.Contracts ?? Enumerable.Empty<Contract>();
    }

    public async Task<IEnumerable<WorkSummary>> GetWorkSummariesAsync(long projectId)
    {
        var query = ApplyFilters(_context.Projects.AsQueryable(), null, null);
        var project = await query
            .Where(p => p.Id == projectId)
            .Include(p => p.WorkSummaries)
            .FirstOrDefaultAsync();

        return project?.WorkSummaries ?? Enumerable.Empty<WorkSummary>();
    }
}