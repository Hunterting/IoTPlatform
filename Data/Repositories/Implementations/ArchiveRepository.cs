using IoTPlatform.Data.Repositories.Interfaces;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;

namespace IoTPlatform.Data.Repositories.Implementations;

/// <summary>
/// 档案仓储实现类
/// </summary>
public class ArchiveRepository : Repository<Archive>, IArchiveRepository
{
    public ArchiveRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<Archive>> GetByTypeAsync(string type, string? appCode = null)
    {
        var query = ApplyFilters(_context.Archives.AsQueryable(), appCode, null);
        return await query
            .Where(a => a.Type == type)
            .Include(a => a.Area)
            .Include(a => a.DeviceMarkers)
            .ToListAsync();
    }

    public async Task<IEnumerable<Archive>> GetByCategoryAsync(string category, string? appCode = null)
    {
        var query = ApplyFilters(_context.Archives.AsQueryable(), appCode, null);
        return await query
            .Where(a => a.Category == category)
            .Include(a => a.Area)
            .Include(a => a.DeviceMarkers)
            .ToListAsync();
    }

    public async Task<IEnumerable<Archive>> GetByAreaIdAsync(long areaId, string? appCode = null)
    {
        var query = ApplyFilters(_context.Archives.AsQueryable(), appCode, null);
        return await query
            .Where(a => a.AreaId == areaId)
            .Include(a => a.Area)
            .Include(a => a.DeviceMarkers)
            .ToListAsync();
    }

    public async Task<IEnumerable<Archive>> Get3DModelsAsync(string? appCode = null)
    {
        var query = ApplyFilters(_context.Archives.AsQueryable(), appCode, null);
        return await query
            .Where(a => a.Is3DModel)
            .Include(a => a.Area)
            .Include(a => a.DeviceMarkers)
            .ToListAsync();
    }

    public async Task<Archive?> GetArchiveDetailsAsync(long id, string? appCode = null)
    {
        var query = ApplyFilters(_context.Archives.AsQueryable(), appCode, null);
        return await query
            .Where(a => a.Id == id)
            .Include(a => a.Area)
            .Include(a => a.DeviceMarkers)
            .FirstOrDefaultAsync();
    }

    public async Task<IEnumerable<ArchiveDeviceMarker>> GetDeviceMarkersAsync(long archiveId)
    {
        return await _context.ArchiveDeviceMarkers
            .Where(m => m.ArchiveId == archiveId)
            .Include(m => m.Device)
            .OrderBy(m => m.Id)
            .ToListAsync();
    }

    public async Task AddDeviceMarkerAsync(long archiveId, long? deviceId, string name, string? deviceType = null, string? model = null, 
        double x = 0, double y = 0, double z = 0, string? sensors = null)
    {
        var marker = new ArchiveDeviceMarker
        {
            ArchiveId = archiveId,
            DeviceId = deviceId,
            Name = name,
            DeviceType = deviceType,
            Model = model,
            X = x,
            Y = y,
            Z = z,
            Sensors = sensors,
            CreatedAt = DateTime.UtcNow
        };
        
        await _context.ArchiveDeviceMarkers.AddAsync(marker);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateDeviceMarkerAsync(long markerId, string? name = null, string? deviceType = null, string? model = null,
        double? x = null, double? y = null, double? z = null, string? sensors = null)
    {
        var marker = await _context.ArchiveDeviceMarkers.FindAsync(markerId);
        if (marker != null)
        {
            if (name != null) marker.Name = name;
            if (deviceType != null) marker.DeviceType = deviceType;
            if (model != null) marker.Model = model;
            if (x.HasValue) marker.X = x.Value;
            if (y.HasValue) marker.Y = y.Value;
            if (z.HasValue) marker.Z = z.Value;
            if (sensors != null) marker.Sensors = sensors;
            marker.UpdatedAt = DateTime.UtcNow;
            
            await _context.SaveChangesAsync();
        }
    }

    public async Task DeleteDeviceMarkerAsync(long markerId)
    {
        var marker = await _context.ArchiveDeviceMarkers.FindAsync(markerId);
        if (marker != null)
        {
            _context.ArchiveDeviceMarkers.Remove(marker);
            await _context.SaveChangesAsync();
        }
    }

    public async Task<bool> NameExistsAsync(string name, string? appCode = null, long? excludeArchiveId = null)
    {
        var query = ApplyFilters(_context.Archives.AsQueryable(), appCode, null);
        
        query = query.Where(a => a.Name == name);
        
        if (excludeArchiveId.HasValue)
        {
            query = query.Where(a => a.Id != excludeArchiveId.Value);
        }
        
        return await query.AnyAsync();
    }

    public async Task<ArchiveStats> GetArchiveStatsAsync(string? appCode = null)
    {
        var query = ApplyFilters(_context.Archives.AsQueryable(), appCode, null);
        var archives = await query.ToListAsync();
        
        var stats = new ArchiveStats
        {
            TotalArchives = archives.Count,
            FloorPlanCount = archives.Count(a => a.Type == "floor_plan"),
            Model3DCount = archives.Count(a => a.Type == "3d_model"),
            PhotoCount = archives.Count(a => a.Type == "photo"),
            DocumentCount = archives.Count(a => a.Type == "document"),
            TotalDeviceMarkers = 0,
            TotalFileSize = 0
        };
        
        // 获取设备标记总数和文件大小总和
        foreach (var archive in archives)
        {
            var markers = await _context.ArchiveDeviceMarkers.CountAsync(m => m.ArchiveId == archive.Id);
            stats.TotalDeviceMarkers += markers;
            
            if (!string.IsNullOrEmpty(archive.Size))
            {
                if (long.TryParse(archive.Size, out var size))
                {
                    stats.TotalFileSize += size;
                }
            }
        }
        
        return stats;
    }
}