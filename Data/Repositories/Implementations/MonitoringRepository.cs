using IoTPlatform.Data;
using IoTPlatform.Data.Repositories.Interfaces;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;

namespace IoTPlatform.Data.Repositories.Implementations
{
    public class MonitoringRepository : Repository<Monitoring>, IMonitoringRepository
    {
        public MonitoringRepository(AppDbContext context) : base(context)
        {
        }

        public async Task<Monitoring?> GetByDeviceIdAsync(Guid deviceId)
        {
            return await _dbSet
                .FirstOrDefaultAsync(m => m.DeviceId == deviceId);
        }

        public async Task<List<Monitoring>> GetByStatusAsync(string status)
        {
            return await _dbSet
                .Where(m => m.Status == status)
                .Include(m => m.Device)
                .ThenInclude(d => d!.Area)
                .OrderBy(m => m.CreatedAt)
                .ToListAsync();
        }

        public async Task<List<Monitoring>> GetByAreaIdAsync(Guid areaId)
        {
            return await _dbSet
                .Where(m => m.Device != null && m.Device.AreaId == areaId)
                .Include(m => m.Device)
                .ThenInclude(d => d!.Area)
                .OrderByDescending(m => m.LastUpdated)
                .ToListAsync();
        }

        public async Task<List<Monitoring>> GetActiveMonitoringsAsync()
        {
            return await _dbSet
                .Where(m => m.Status == "active" || m.Status == "warning")
                .Include(m => m.Device)
                .ThenInclude(d => d!.Area)
                .OrderByDescending(m => m.LastUpdated)
                .ToListAsync();
        }

        public async Task<bool> UpdateDeviceStatusAsync(Guid deviceId, string status, DateTime? lastUpdated = null)
        {
            var monitoring = await GetByDeviceIdAsync(deviceId);
            if (monitoring == null)
            {
                return false;
            }

            monitoring.Status = status;
            monitoring.LastUpdated = lastUpdated ?? DateTime.UtcNow;
            
            return true;
        }

        public async Task<List<Monitoring>> GetWithMetricsAsync(DateTime? since = null)
        {
            var query = _dbSet
                .Include(m => m.Device)
                .ThenInclude(d => d!.Area);
            
            if (since.HasValue)
            {
                query = query.Where(m => m.LastUpdated >= since.Value);
            }

            return await query
                .OrderByDescending(m => m.LastUpdated)
                .ToListAsync();
        }

        public async Task<int> GetMonitoringCountByStatusAsync(string status)
        {
            return await _dbSet.CountAsync(m => m.Status == status);
        }
    }
}