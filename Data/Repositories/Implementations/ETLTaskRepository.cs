using IoTPlatform.Data;
using IoTPlatform.Data.Repositories.Interfaces;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;

namespace IoTPlatform.Data.Repositories.Implementations
{
    public class ETLTaskRepository : Repository<ETLTask>, IETLTaskRepository
    {
        private static readonly ConcurrentDictionary<long, List<TaskExecutionHistory>> _executionHistoryCache = new();

        public ETLTaskRepository(AppDbContext context) : base(context)
        {
        }

        public async Task<IEnumerable<ETLTask>> GetByTaskTypeAsync(string taskType, string? appCode = null)
        {
            var query = _dbSet.Where(t => t.TaskType == taskType);

            if (!string.IsNullOrEmpty(appCode))
            {
                query = query.Where(t => t.AppCode == appCode);
            }

            return await query
                .Include(t => t.DataRule)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();
        }

        public async Task<IEnumerable<ETLTask>> GetByStatusAsync(string status, string? appCode = null)
        {
            var query = _dbSet.Where(t => t.Status == status);

            if (!string.IsNullOrEmpty(appCode))
            {
                query = query.Where(t => t.AppCode == appCode);
            }

            return await query
                .Include(t => t.DataRule)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();
        }

        public async Task<IEnumerable<ETLTask>> GetTasksToRunAsync(string? appCode = null)
        {
            var query = _dbSet.Where(t => t.IsActive && (t.Status == "ready" || t.Status == "running"));

            if (!string.IsNullOrEmpty(appCode))
            {
                query = query.Where(t => t.AppCode == appCode);
            }

            return await query
                .Include(t => t.DataRule)
                .OrderBy(t => t.CreatedAt)
                .ToListAsync();
        }

        public async Task UpdateTaskStatusAsync(long taskId, string status, DateTime? lastRunTime = null, DateTime? nextRunTime = null)
        {
            var task = await _dbSet.FindAsync(taskId);
            if (task != null)
            {
                task.Status = status;
            if (lastRunTime.HasValue)
            {
                task.LastRunTime = lastRunTime.Value;
            }
            if (nextRunTime.HasValue)
            {
                task.NextRunTime = nextRunTime.Value;
            }
                task.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }
        }

        public async Task<bool> NameExistsAsync(string name, string? appCode = null, long? excludeTaskId = null)
        {
            var query = _dbSet.Where(t => t.Name == name);

            if (!string.IsNullOrEmpty(appCode))
            {
                query = query.Where(t => t.AppCode == appCode);
            }

            if (excludeTaskId.HasValue)
            {
                query = query.Where(t => t.Id != excludeTaskId.Value);
            }

            return await query.AnyAsync();
        }

        public async Task<IEnumerable<TaskExecutionHistory>> GetExecutionHistoryAsync(long taskId, int limit = 10)
        {
            if (_executionHistoryCache.TryGetValue(taskId, out var history))
            {
                return history.OrderByDescending(h => h.ExecutionTime).Take(limit).ToList();
            }

            return new List<TaskExecutionHistory>();
        }

        public async Task RecordExecutionHistoryAsync(long taskId, string status, string? message = null, int? duration = null)
        {
            var history = _executionHistoryCache.GetOrAdd(taskId, _ => new List<TaskExecutionHistory>());

            var record = new TaskExecutionHistory
            {
                Id = history.Count > 0 ? history.Max(h => h.Id) + 1 : 1,
                TaskId = taskId,
                ExecutionTime = DateTime.UtcNow,
                Status = status,
                Message = message,
                Duration = duration
            };

            history.Add(record);

            if (history.Count > 100)
            {
                history.RemoveAll(h => h.Id <= history.Count - 100);
            }

            await Task.CompletedTask;
        }

        public async Task<ETLTaskStats> GetETLTaskStatsAsync(string? appCode = null)
        {
            var query = _dbSet.AsQueryable();

            if (!string.IsNullOrEmpty(appCode))
            {
                query = query.Where(t => t.AppCode == appCode);
            }

            var tasks = await query.ToListAsync();

            return new ETLTaskStats
            {
                TotalTasks = tasks.Count,
                ActiveTasks = tasks.Count(t => t.IsActive),
                PausedTasks = tasks.Count(t => !t.IsActive),
                FailedTasks = tasks.Count(t => t.Status == "failed"),
                CompletedTasks = tasks.Count(t => t.Status == "completed"),
                TransformTasks = tasks.Count(t => t.TaskType == "transform"),
                AggregationTasks = tasks.Count(t => t.TaskType == "aggregation"),
                ExportTasks = tasks.Count(t => t.TaskType == "export"),
                ScheduledTasks = tasks.Count(t => t.TriggerType == "scheduled"),
                ManualTasks = tasks.Count(t => t.TriggerType == "manual"),
                LastUpdate = DateTime.UtcNow
            };
        }

        public async Task<List<ETLTask>> GetByDataRuleIdAsync(Guid dataRuleId)
        {
            return await _dbSet
                .Where(t => t.DataRuleId == dataRuleId)
                .Include(t => t.DataRule)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();
        }

        public async Task<ETLTask?> GetLatestByDataRuleIdAsync(long dataRuleId)
        {
            return await _dbSet
                .Where(t => t.Id == dataRuleId)
                .Include(t => t.DataRule)
                .OrderByDescending(t => t.CreatedAt)
                .FirstOrDefaultAsync();
        }

        public async Task<int> GetPendingTaskCountAsync()
        {
            return await _dbSet.CountAsync(t => t.Status == "pending" || t.Status == "processing");
        }

        public async Task<bool> UpdateTaskStatusAsync(long taskId, string status, string? errorMessage = null)
        {
            var task = await GetByIdAsync(taskId);
            if (task == null)
            {
                return false;
            }

            task.Status = status;

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
