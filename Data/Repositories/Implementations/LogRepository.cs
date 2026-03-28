using IoTPlatform.Data;
using IoTPlatform.Data.Repositories.Interfaces;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;

namespace IoTPlatform.Data.Repositories.Implementations
{
    public class LogRepository : Repository<Log>, ILogRepository
    {
        public LogRepository(AppDbContext context) : base(context)
        {
        }

        public async Task<List<Log>> GetByLevelAsync(string level, int page = 1, int pageSize = 50)
        {
            return await _dbSet
                .Where(l => l.Level == level)
                .OrderByDescending(l => l.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }

        public async Task<List<Log>> GetBySourceAsync(string source, DateTime? startDate = null, DateTime? endDate = null)
        {
            var query = _dbSet.Where(l => l.Source == source);
            
            if (startDate.HasValue)
            {
                query = query.Where(l => l.CreatedAt >= startDate.Value);
            }
            
            if (endDate.HasValue)
            {
                query = query.Where(l => l.CreatedAt <= endDate.Value);
            }

            return await query
                .OrderByDescending(l => l.CreatedAt)
                .ToListAsync();
        }

        public async Task<List<Log>> SearchLogsAsync(string? searchText, string? level, string? source, DateTime? startDate, DateTime? endDate, int page = 1, int pageSize = 50)
        {
            var query = _dbSet.AsQueryable();
            
            if (!string.IsNullOrEmpty(searchText))
            {
                query = query.Where(l => 
                    l.Message.Contains(searchText) || 
                    (l.Exception != null && l.Exception.Contains(searchText)));
            }
            
            if (!string.IsNullOrEmpty(level))
            {
                query = query.Where(l => l.Level == level);
            }
            
            if (!string.IsNullOrEmpty(source))
            {
                query = query.Where(l => l.Source == source);
            }
            
            if (startDate.HasValue)
            {
                query = query.Where(l => l.CreatedAt >= startDate.Value);
            }
            
            if (endDate.HasValue)
            {
                query = query.Where(l => l.CreatedAt <= endDate.Value);
            }

            return await query
                .OrderByDescending(l => l.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }

        public async Task<int> GetLogCountAsync(string? level = null, string? source = null, DateTime? startDate = null, DateTime? endDate = null)
        {
            var query = _dbSet.AsQueryable();
            
            if (!string.IsNullOrEmpty(level))
            {
                query = query.Where(l => l.Level == level);
            }
            
            if (!string.IsNullOrEmpty(source))
            {
                query = query.Where(l => l.Source == source);
            }
            
            if (startDate.HasValue)
            {
                query = query.Where(l => l.CreatedAt >= startDate.Value);
            }
            
            if (endDate.HasValue)
            {
                query = query.Where(l => l.CreatedAt <= endDate.Value);
            }

            return await query.CountAsync();
        }

        public async Task CleanOldLogsAsync(DateTime cutoffDate, string? level = null)
        {
            var query = _dbSet.Where(l => l.CreatedAt < cutoffDate);
            
            if (!string.IsNullOrEmpty(level))
            {
                query = query.Where(l => l.Level == level);
            }

            await query.ExecuteDeleteAsync();
        }
    }
}