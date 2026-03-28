using IoTPlatform.Data.Repositories.Interfaces;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;

namespace IoTPlatform.Data.Repositories.Implementations;

/// <summary>
/// 设备仓储实现类
/// </summary>
public class DeviceRepository : Repository<Device>, IDeviceRepository
{
    public DeviceRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<Device?> GetBySerialNumberAsync(string serialNumber, string? appCode = null)
    {
        var query = ApplyFilters(_context.Devices.AsQueryable(), appCode, null);
        return await query
            .Where(d => d.SerialNumber == serialNumber)
            .Include(d => d.Area)
            .Include(d => d.Project)
            .Include(d => d.Sensors)
            .FirstOrDefaultAsync();
    }

    public async Task<IEnumerable<Device>> GetByAreaIdAsync(long areaId, string? appCode = null)
    {
        var query = ApplyFilters(_context.Devices.AsQueryable(), appCode, null);
        return await query
            .Where(d => d.AreaId == areaId)
            .Include(d => d.Area)
            .Include(d => d.Project)
            .Include(d => d.Sensors)
            .ToListAsync();
    }

    public async Task<IEnumerable<Device>> GetByProjectIdAsync(long projectId, string? appCode = null)
    {
        var query = ApplyFilters(_context.Devices.AsQueryable(), appCode, null);
        return await query
            .Where(d => d.ProjectId == projectId)
            .Include(d => d.Area)
            .Include(d => d.Project)
            .Include(d => d.Sensors)
            .ToListAsync();
    }

    public async Task<IEnumerable<Device>> GetByStatusAsync(string status, string? appCode = null)
    {
        var query = ApplyFilters(_context.Devices.AsQueryable(), appCode, null);
        return await query
            .Where(d => d.Status == status)
            .Include(d => d.Area)
            .Include(d => d.Project)
            .Include(d => d.Sensors)
            .ToListAsync();
    }

    public async Task<bool> SerialNumberExistsAsync(string serialNumber, string? appCode = null, long? excludeDeviceId = null)
    {
        var query = ApplyFilters(_context.Devices.AsQueryable(), appCode, null);
        
        query = query.Where(d => d.SerialNumber == serialNumber);
        
        if (excludeDeviceId.HasValue)
        {
            query = query.Where(d => d.Id != excludeDeviceId.Value);
        }
        
        return await query.AnyAsync();
    }

    public async Task UpdateStatusAsync(long deviceId, string status)
    {
        var device = await _context.Devices.FindAsync(deviceId);
        if (device != null)
        {
            device.Status = status;
            device.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    public async Task UpdateLastMaintenanceAsync(long deviceId)
    {
        var device = await _context.Devices.FindAsync(deviceId);
        if (device != null)
        {
            device.LastMaintenance = DateTime.UtcNow;
            device.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    public async Task<Device?> GetDeviceDetailsAsync(long id, string? appCode = null)
    {
        var query = ApplyFilters(_context.Devices.AsQueryable(), appCode, null);
        return await query
            .Where(d => d.Id == id)
            .Include(d => d.Area)
            .Include(d => d.Project)
            .Include(d => d.Sensors)
            .Include(d => d.DataRecords)
            .Include(d => d.AlertRecords)
            .FirstOrDefaultAsync();
    }

    public async Task<IEnumerable<DeviceSensor>> GetSensorsAsync(long deviceId)
    {
        var device = await _context.Devices
            .Where(d => d.Id == deviceId)
            .Include(d => d.Sensors)
            .FirstOrDefaultAsync();
        
        return device?.Sensors ?? Enumerable.Empty<DeviceSensor>();
    }

    public async Task<IEnumerable<DeviceDataRecord>> GetDataRecordsAsync(long deviceId, DateTime? startTime = null, DateTime? endTime = null, int? limit = 100)
    {
        var query = _context.DeviceDataRecords.AsQueryable();
        
        query = query.Where(r => r.DeviceId == deviceId);
        
        if (startTime.HasValue)
        {
            query = query.Where(r => r.RecordTime >= startTime.Value);
        }
        
        if (endTime.HasValue)
        {
            query = query.Where(r => r.RecordTime <= endTime.Value);
        }
        
        query = query.OrderByDescending(r => r.RecordTime);
        
        if (limit.HasValue && limit.Value > 0)
        {
            query = query.Take(limit.Value);
        }
        
        return await query.ToListAsync();
    }

    public async Task<(int SensorCount, int DataRecordCount, int AlertCount)> GetDeviceStatsAsync(long deviceId)
    {
        var device = await _context.Devices
            .Where(d => d.Id == deviceId)
            .Select(d => new
            {
                SensorCount = d.Sensors.Count(),
                DataRecordCount = d.DataRecords.Count(),
                AlertCount = d.AlertRecords.Count()
            })
            .FirstOrDefaultAsync();
        
        return device != null 
            ? (device.SensorCount, device.DataRecordCount, device.AlertCount)
            : (0, 0, 0);
    }
}