using IoTPlatform.Data.Repositories.Interfaces;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;

namespace IoTPlatform.Data.Repositories.Implementations;

/// <summary>
/// 日志仓储实现类
/// </summary>
public class LogRepository : Repository<LoginLog>, ILogRepository
{
    public LogRepository(AppDbContext context) : base(context)
    {
    }

    public IQueryable<OperationLog> GetOperationLogsQuery(string? appCode = null, string? currentUserRole = null)
    {
        var query = _context.OperationLogs.AsQueryable();

        if (!string.IsNullOrEmpty(appCode))
        {
            query = query.Where(l => l.AppCode == appCode);
        }

        // 可以基于角色添加额外的过滤逻辑
        // if (currentUserRole != "admin") { ... }

        return query.OrderByDescending(l => l.CreatedAt);
    }

    public IQueryable<LoginLog> GetLoginLogsQuery(string? appCode = null, string? currentUserRole = null)
    {
        var query = _context.LoginLogs.AsQueryable();

        if (!string.IsNullOrEmpty(appCode))
        {
            query = query.Where(l => l.AppCode == appCode);
        }

        return query.OrderByDescending(l => l.LoginTime);
    }

    public async Task<IEnumerable<LoginLog>> GetLoginLogsAsync(long? userId = null, DateTime? startTime = null, DateTime? endTime = null,
        string? status = null, int? limit = 100)
    {
        var query = _context.LoginLogs.AsQueryable();

        if (userId.HasValue)
        {
            query = query.Where(l => l.UserId == userId.Value);
        }

        if (startTime.HasValue)
        {
            query = query.Where(l => l.LoginTime >= startTime.Value);
        }

        if (endTime.HasValue)
        {
            query = query.Where(l => l.LoginTime <= endTime.Value);
        }

        if (!string.IsNullOrEmpty(status))
        {
            query = query.Where(l => l.Status == status);
        }

        if (limit.HasValue)
        {
            query = query.Take(limit.Value);
        }

        return await query.OrderByDescending(l => l.LoginTime).ToListAsync();
    }

    public async Task<IEnumerable<LoginLog>> GetLoginLogsByEmailAsync(string email, DateTime? startTime = null, DateTime? endTime = null, int? limit = 100)
    {
        var query = _context.LoginLogs.Where(l => l.Email == email);

        if (startTime.HasValue)
        {
            query = query.Where(l => l.LoginTime >= startTime.Value);
        }

        if (endTime.HasValue)
        {
            query = query.Where(l => l.LoginTime <= endTime.Value);
        }

        if (limit.HasValue)
        {
            query = query.Take(limit.Value);
        }

        return await query.OrderByDescending(l => l.LoginTime).ToListAsync();
    }

    public async Task<IEnumerable<OperationLog>> GetOperationLogsAsync(long? userId = null, string? module = null, string? action = null,
        DateTime? startTime = null, DateTime? endTime = null, string? status = null, int? limit = 100)
    {
        var query = _context.OperationLogs.AsQueryable();

        if (userId.HasValue)
        {
            query = query.Where(l => l.UserId == userId.Value);
        }

        if (!string.IsNullOrEmpty(module))
        {
            query = query.Where(l => l.Module == module);
        }

        if (!string.IsNullOrEmpty(action))
        {
            query = query.Where(l => l.Action == action);
        }

        if (startTime.HasValue)
        {
            query = query.Where(l => l.CreatedAt >= startTime.Value);
        }

        if (endTime.HasValue)
        {
            query = query.Where(l => l.CreatedAt <= endTime.Value);
        }

        if (!string.IsNullOrEmpty(status))
        {
            query = query.Where(l => l.Status == status);
        }

        if (limit.HasValue)
        {
            query = query.Take(limit.Value);
        }

        return await query.OrderByDescending(l => l.CreatedAt).ToListAsync();
    }

    public async Task LogLoginAsync(long? userId, string? email, string? userName, string? ipAddress, string? userAgent,
        string status, string? failureReason = null, string? location = null)
    {
        var loginLog = new LoginLog
        {
            UserId = userId,
            Email = email,
            UserName = userName,
            IP = ipAddress,
            UserAgent = userAgent,
            Status = status,
            FailureReason = failureReason,
            Location = location,
            LoginTime = DateTime.UtcNow
        };

        _context.LoginLogs.Add(loginLog);
        await _context.SaveChangesAsync();
    }

    public async Task LogOperationAsync(long userId, string? userName, string? role, string? module, string action, string? target = null,
        string? detail = null, string? ip = null, string status = "success", int? duration = null, string? appCode = null)
    {
        var operationLog = new OperationLog
        {
            UserId = userId,
            UserName = userName,
            Role = role,
            Module = module,
            Action = action,
            Target = target,
            Detail = detail,
            IP = ip,
            Status = status,
            Duration = duration,
            AppCode = appCode,
            CreatedAt = DateTime.UtcNow
        };

        _context.OperationLogs.Add(operationLog);
        await _context.SaveChangesAsync();
    }

    public async Task<LogStats> GetLogStatsAsync(DateTime? startTime = null, DateTime? endTime = null, string? appCode = null)
    {
        var loginQuery = _context.LoginLogs.AsQueryable();
        var operationQuery = _context.OperationLogs.AsQueryable();

        if (startTime.HasValue)
        {
            loginQuery = loginQuery.Where(l => l.LoginTime >= startTime.Value);
            operationQuery = operationQuery.Where(l => l.CreatedAt >= startTime.Value);
        }

        if (endTime.HasValue)
        {
            loginQuery = loginQuery.Where(l => l.LoginTime <= endTime.Value);
            operationQuery = operationQuery.Where(l => l.CreatedAt <= endTime.Value);
        }

        if (!string.IsNullOrEmpty(appCode))
        {
            operationQuery = operationQuery.Where(l => l.AppCode == appCode);
        }

        var loginLogs = await loginQuery.ToListAsync();
        var operationLogs = await operationQuery.ToListAsync();

        return new LogStats
        {
            TotalLogins = loginLogs.Count,
            SuccessfulLogins = loginLogs.Count(l => l.Status == "success"),
            FailedLogins = loginLogs.Count(l => l.Status == "failed"),
            TotalOperations = operationLogs.Count,
            SuccessfulOperations = operationLogs.Count(l => l.Status == "success"),
            FailedOperations = operationLogs.Count(l => l.Status == "failed"),
            UniqueUsers = loginLogs.Select(l => l.UserId).Distinct().Count(),
            LastUpdate = DateTime.UtcNow
        };
    }

    public async Task<int> CleanupOldLogsAsync(int daysToKeep = 90)
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-daysToKeep);

        var deletedLoginLogs = await _context.LoginLogs
            .Where(l => l.LoginTime < cutoffDate)
            .ExecuteDeleteAsync();

        var deletedOperationLogs = await _context.OperationLogs
            .Where(l => l.CreatedAt < cutoffDate)
            .ExecuteDeleteAsync();

        return deletedLoginLogs + deletedOperationLogs;
    }

    public async Task<DateTime?> GetLastLoginTimeAsync(long userId)
    {
        return await _context.LoginLogs
            .Where(l => l.UserId == userId && l.Status == "success")
            .OrderByDescending(l => l.LoginTime)
            .Select(l => l.LoginTime)
            .FirstOrDefaultAsync();
    }

    public async Task<int> GetUserLoginCountAsync(long userId, int days = 30)
    {
        var startDate = DateTime.UtcNow.AddDays(-days);
        return await _context.LoginLogs
            .CountAsync(l => l.UserId == userId && l.LoginTime >= startDate);
    }

    public async Task<IEnumerable<OperationFrequency>> GetOperationFrequencyAsync(DateTime? startTime = null, DateTime? endTime = null, string? appCode = null)
    {
        var query = _context.OperationLogs.AsQueryable();

        if (startTime.HasValue)
        {
            query = query.Where(l => l.CreatedAt >= startTime.Value);
        }

        if (endTime.HasValue)
        {
            query = query.Where(l => l.CreatedAt <= endTime.Value);
        }

        if (!string.IsNullOrEmpty(appCode))
        {
            query = query.Where(l => l.AppCode == appCode);
        }

        return await query
            .Where(l => l.Status == "success")
            .GroupBy(l => new { l.Module, l.Action })
            .Select(g => new OperationFrequency
            {
                Module = g.Key.Module ?? "",
                Action = g.Key.Action,
                Count = g.Count(),
                AvgDuration = (int)g.Where(l => l.Duration.HasValue).Average(l => l.Duration.Value),
                SuccessCount = g.Count(l => l.Status == "success"),
                FailCount = 0
            })
            .OrderByDescending(f => f.Count)
            .ToListAsync();
    }
}
