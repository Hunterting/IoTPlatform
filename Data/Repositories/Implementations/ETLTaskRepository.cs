using IoTPlatform.Data;
using IoTPlatform.Data.Repositories.Interfaces;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;

namespace IoTPlatform.Data.Repositories.Implementations
{
    public class ETLTaskRepository : Repository<ETLTask>, IETLTaskRepository
    {
        public ETLTaskRepository(AppDbContext context) : base(context)
        {
        }

        public async Task<List<ETLTask>> GetByStatusAsync(string status)
        {
            return await _dbSet
                .Where(t => t.Status == status)
                .Include(t => t.DataRule)
                .OrderBy(t => t.CreatedAt)
                .ToListAsync();
        }

        public async Task<List<ETLTask>> GetByDataRuleIdAsync(Guid dataRuleId)
        {
            return await _dbSet
                .Where(t => t.DataRuleId == dataRuleId)
                .Include(t => t.DataRule)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();
        }

        public async Task<ETLTask?> GetLatestByDataRuleIdAsync(Guid dataRuleId)
        {
            return await _dbSet
                .Where(t => t.DataRuleId == dataRuleId)
                .Include(t => t.DataRule)
                .OrderByDescending(t => t.CreatedAt)
                .FirstOrDefaultAsync();
        }

        public async Task<int> GetPendingTaskCountAsync()
        {
            return await _dbSet.CountAsync(t => t.Status == "pending" || t.Status == "processing");
        }

        public async Task<bool> UpdateTaskStatusAsync(Guid taskId, string status, string? errorMessage = null)
        {
            var task = await GetByIdAsync(taskId);
            if (task == null)
            {
                return false;
            }

            task.Status = status;
            task.ErrorMessage = errorMessage;
            
            if (status == "completed" || status == "failed")
            {
                task.CompletedAt = DateTime.UtcNow;
            }

            return true;
        }

        public async Task<List<ETLTask>> GetFailedTasksAsync(DateTime? since = null)
        {
            var query = _dbSet.Where(t => t.Status == "failed");
            
            if (since.HasValue)
            {
                query = query.Where(t => t.CreatedAt >= since.Value);
            }

            return await query
                .Include(t => t.DataRule)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();
        }
    }
}