using IoTPlatform.Data.Repositories.Interfaces;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;

namespace IoTPlatform.Data.Repositories.Implementations;

/// <summary>
/// 区域仓储实现类
/// </summary>
public class AreaRepository : Repository<Area>, IAreaRepository
{
    public AreaRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<Area>> GetTreeAsync(long? customerId = null, string? appCode = null)
    {
        var query = ApplyFilters(_context.Areas.AsQueryable(), appCode, null);
        
        if (customerId.HasValue)
        {
            query = query.Where(a => a.CustomerId == customerId.Value);
        }
        
        var areas = await query
            .Include(a => a.Devices)
            .Include(a => a.Archives)
            .ToListAsync();
        
        // 构建树形结构（这里简化处理，实际可能需要递归构建）
        return areas.Where(a => a.ParentId == null);
    }

    public async Task<IEnumerable<Area>> GetChildrenAsync(long parentId, string? appCode = null)
    {
        var query = ApplyFilters(_context.Areas.AsQueryable(), appCode, null);
        return await query
            .Where(a => a.ParentId == parentId)
            .Include(a => a.Devices)
            .Include(a => a.Archives)
            .ToListAsync();
    }

    public async Task<IEnumerable<Area>> GetPathAsync(long areaId, string? appCode = null)
    {
        var path = new List<Area>();
        long? currentId = areaId;

        while (currentId != null)
        {
            var query = ApplyFilters(_context.Areas.AsQueryable(), appCode, null);
            var area = await query.FirstOrDefaultAsync(a => a.Id == currentId);
            if (area == null) break;

            path.Insert(0, area);
            currentId = area.ParentId;
        }

        return path;
    }

    public async Task<IEnumerable<Area>> GetByTypeAsync(string type, string? appCode = null)
    {
        var query = ApplyFilters(_context.Areas.AsQueryable(), appCode, null);
        return await query
            .Where(a => a.Type == type)
            .Include(a => a.Devices)
            .Include(a => a.Archives)
            .ToListAsync();
    }

    public async Task UpdateDeviceCountAsync(long areaId, int increment = 1)
    {
        var area = await _context.Areas.FindAsync(areaId);
        if (area != null)
        {
            area.UpdatedAt = DateTime.UtcNow;
            // 如果有设备数量字段可以更新
            // area.DeviceCount += increment;
            await _context.SaveChangesAsync();
        }
    }

    public async Task<bool> NameExistsAsync(string name, long? parentId, long? customerId, string? appCode = null, long? excludeAreaId = null)
    {
        var query = ApplyFilters(_context.Areas.AsQueryable(), appCode, null);
        
        query = query.Where(a => a.Name == name);
        
        if (parentId.HasValue)
        {
            query = query.Where(a => a.ParentId == parentId.Value);
        }
        else
        {
            query = query.Where(a => a.ParentId == null);
        }
        
        if (customerId.HasValue)
        {
            query = query.Where(a => a.CustomerId == customerId.Value);
        }
        
        if (excludeAreaId.HasValue)
        {
            query = query.Where(a => a.Id != excludeAreaId.Value);
        }
        
        return await query.AnyAsync();
    }

    public async Task<IEnumerable<Device>> GetDevicesAsync(long areaId)
    {
        var area = await _context.Areas
            .Where(a => a.Id == areaId)
            .Include(a => a.Devices)
            .ThenInclude(ad => ad.Device)
            .FirstOrDefaultAsync();

        return area?.Devices?.Select(ad => ad.Device).Where(d => d != null).Cast<Device>() ?? Enumerable.Empty<Device>();
    }

    public async Task<IEnumerable<Archive>> GetArchivesAsync(long areaId)
    {
        var area = await _context.Areas
            .Where(a => a.Id == areaId)
            .Include(a => a.Archives)
            .FirstOrDefaultAsync();
        
        return area?.Archives ?? Enumerable.Empty<Archive>();
    }
}