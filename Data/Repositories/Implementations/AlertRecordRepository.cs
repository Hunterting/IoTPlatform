using IoTPlatform.Data.Repositories.Interfaces;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;

namespace IoTPlatform.Data.Repositories.Implementations;

/// <summary>
/// 告警仓储实现类
/// </summary>
public class AlertRecordRepository : Repository<AlertRecord>, IAlertRecordRepository
{
    public AlertRecordRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<AlertRecord?> GetByAlertNoAsync(string alertNo, string? appCode = null)
    {
        var query = ApplyFilters(_context.AlertRecords.AsQueryable(), appCode, null);
        return await query
            .Where(a => a.AlertNo == alertNo)
            .Include(a => a.Device)
            .ThenInclude(d => d.Area)
            .Include(a => a.ProcessLogs)
            .FirstOrDefaultAsync();
    }

    public async Task<IEnumerable<AlertRecord>> GetByDeviceIdAsync(long deviceId, string? appCode = null, string? status = null)
    {
        var query = ApplyFilters(_context.AlertRecords.AsQueryable(), appCode, null);
        query = query.Where(a => a.DeviceId == deviceId);
        
        if (!string.IsNullOrEmpty(status))
        {
            query = query.Where(a => a.Status == status);
        }
        
        return await query
            .Include(a => a.Device)
            .Include(a => a.ProcessLogs)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<AlertRecord>> GetByAreaIdAsync(long areaId, string? appCode = null, string? status = null)
    {
        var query = ApplyFilters(_context.AlertRecords.AsQueryable(), appCode, null);
        query = query.Where(a => a.AreaId == areaId);
        
        if (!string.IsNullOrEmpty(status))
        {
            query = query.Where(a => a.Status == status);
        }
        
        return await query
            .Include(a => a.Device)
            .Include(a => a.ProcessLogs)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<AlertRecord>> GetByAlertTypeAsync(string alertType, string? appCode = null, string? status = null)
    {
        var query = ApplyFilters(_context.AlertRecords.AsQueryable(), appCode, null);
        query = query.Where(a => a.AlertType == alertType);
        
        if (!string.IsNullOrEmpty(status))
        {
            query = query.Where(a => a.Status == status);
        }
        
        return await query
            .Include(a => a.Device)
            .Include(a => a.ProcessLogs)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<AlertRecord>> GetByLevelAsync(string level, string? appCode = null, string? status = null)
    {
        var query = ApplyFilters(_context.AlertRecords.AsQueryable(), appCode, null);
        query = query.Where(a => a.Level == level);
        
        if (!string.IsNullOrEmpty(status))
        {
            query = query.Where(a => a.Status == status);
        }
        
        return await query
            .Include(a => a.Device)
            .Include(a => a.ProcessLogs)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<AlertRecord>> GetByStatusAsync(string status, string? appCode = null)
    {
        var query = ApplyFilters(_context.AlertRecords.AsQueryable(), appCode, null);
        return await query
            .Where(a => a.Status == status)
            .Include(a => a.Device)
            .Include(a => a.ProcessLogs)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<AlertRecord>> GetByAssigneeAsync(string assignee, string? appCode = null)
    {
        var query = ApplyFilters(_context.AlertRecords.AsQueryable(), appCode, null);
        return await query
            .Where(a => a.Assignee == assignee)
            .Include(a => a.Device)
            .Include(a => a.ProcessLogs)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();
    }

    public async Task UpdateStatusAsync(long alertId, string status, DateTime? resolveTime = null)
    {
        var alert = await _context.AlertRecords.FindAsync(alertId);
        if (alert != null)
        {
            alert.Status = status;
            alert.UpdatedAt = DateTime.UtcNow;
            
            if (status == "resolved" && resolveTime.HasValue)
            {
                alert.ResolveTime = resolveTime.Value;
            }
            
            await _context.SaveChangesAsync();
        }
    }

    public async Task AssignAlertAsync(long alertId, string assignee)
    {
        var alert = await _context.AlertRecords.FindAsync(alertId);
        if (alert != null)
        {
            alert.Assignee = assignee;
            alert.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    public async Task<IEnumerable<AlertProcessLog>> GetProcessLogsAsync(long alertId)
    {
        return await _context.AlertProcessLogs
            .Where(l => l.AlertRecordId == alertId)
            .OrderBy(l => l.CreatedAt)
            .ToListAsync();
    }

    public async Task AddProcessLogAsync(long alertId, string operatorName, string action, string? comment = null)
    {
        var log = new AlertProcessLog
        {
            AlertRecordId = alertId,
            ProcessedBy = operatorName,
            Action = action,
            Comment = comment,
            CreatedAt = DateTime.UtcNow
        };
        
        await _context.AlertProcessLogs.AddAsync(log);
        await _context.SaveChangesAsync();
    }

    public async Task<AlertStats> GetAlertStatsAsync(DateTime? startTime = null, DateTime? endTime = null, string? appCode = null)
    {
        var query = ApplyFilters(_context.AlertRecords.AsQueryable(), appCode, null);
        
        if (startTime.HasValue)
        {
            query = query.Where(a => a.CreatedAt >= startTime.Value);
        }
        
        if (endTime.HasValue)
        {
            query = query.Where(a => a.CreatedAt <= endTime.Value);
        }
        
        var alerts = await query.ToListAsync();
        
        var stats = new AlertStats
        {
            TotalAlerts = alerts.Count,
            PendingAlerts = alerts.Count(a => a.Status == "pending"),
            ProcessingAlerts = alerts.Count(a => a.Status == "processing"),
            ResolvedAlerts = alerts.Count(a => a.Status == "resolved"),
            IgnoredAlerts = alerts.Count(a => a.Status == "ignored"),
            CriticalAlerts = alerts.Count(a => a.Level == "critical"),
            WarningAlerts = alerts.Count(a => a.Level == "warning"),
            InfoAlerts = alerts.Count(a => a.Level == "info")
        };
        
        // 按告警类型统计
        stats.AlertsByType = alerts
            .GroupBy(a => a.AlertType)
            .ToDictionary(g => g.Key, g => g.Count());
        
        return stats;
    }

    public async Task<int> GetPendingAlertCountAsync(string? appCode = null)
    {
        var query = ApplyFilters(_context.AlertRecords.AsQueryable(), appCode, null);
        return await query.CountAsync(a => a.Status == "pending");
    }

    public async Task<int> GetCriticalAlertCountAsync(string? appCode = null)
    {
        var query = ApplyFilters(_context.AlertRecords.AsQueryable(), appCode, null);
        return await query.CountAsync(a => a.Level == "critical" && (a.Status == "pending" || a.Status == "processing"));
    }

    public async Task<IEnumerable<AlertRecord>> GetAlertsWithDetailsAsync(string? appCode = null, DateTime? startTime = null, DateTime? endTime = null, int limit = 100)
    {
        var query = ApplyFilters(_context.AlertRecords.AsQueryable(), appCode, null);

        if (startTime.HasValue)
        {
            query = query.Where(a => a.AlertTime >= startTime.Value);
        }

        if (endTime.HasValue)
        {
            query = query.Where(a => a.AlertTime <= endTime.Value);
        }

        return await query
            .Include(a => a.Device)
                .ThenInclude(d => d.Area)
            .Include(a => a.ProcessLogs)
            .OrderByDescending(a => a.AlertTime)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<AlertRecord?> GetAlertWithDetailsAsync(long alertId, string? appCode = null)
    {
        var query = ApplyFilters(_context.AlertRecords.AsQueryable(), appCode, null);
        return await query
            .Include(a => a.Device)
                .ThenInclude(d => d.Area)
            .Include(a => a.ProcessLogs)
            .Include(a => a.WorkOrders)
            .FirstOrDefaultAsync(a => a.Id == alertId);
    }

    public async Task<IEnumerable<AlertProcessLog>> GetAlertLogsAsync(long alertId)
    {
        return await _context.AlertProcessLogs
            .Where(l => l.AlertRecordId == alertId)
            .OrderBy(l => l.CreatedAt)
            .ToListAsync();
    }

    public async Task<AlertSummaryDto> GetAlertSummaryAsync(DateTime? startTime = null, DateTime? endTime = null, string? appCode = null)
    {
        var query = ApplyFilters(_context.AlertRecords.AsQueryable(), appCode, null);

        if (startTime.HasValue)
        {
            query = query.Where(a => a.AlertTime >= startTime.Value);
        }

        if (endTime.HasValue)
        {
            query = query.Where(a => a.AlertTime <= endTime.Value);
        }

        var alerts = await query.ToListAsync();

        var summary = new AlertSummaryDto
        {
            TotalAlerts = alerts.Count,
            PendingAlerts = alerts.Count(a => a.Status == "pending"),
            ProcessingAlerts = alerts.Count(a => a.Status == "processing"),
            ResolvedAlerts = alerts.Count(a => a.Status == "resolved"),
            IgnoredAlerts = alerts.Count(a => a.Status == "ignored"),
            CriticalAlerts = alerts.Count(a => a.Level == "critical"),
            WarningAlerts = alerts.Count(a => a.Level == "warning"),
            InfoAlerts = alerts.Count(a => a.Level == "info")
        };

        // 按告警类型统计
        summary.AlertsByType = alerts
            .GroupBy(a => a.AlertType)
            .ToDictionary(g => g.Key, g => g.Count());

        summary.LastUpdate = DateTime.UtcNow;

        return summary;
    }

    public async Task<PagedResult<AlertRecord>> GetAlertsWithDetailsAsync(string? status = null, string? level = null, string? alertType = null, string? appCode = null, List<long>? allowedAreaIds = null, int page = 1, int pageSize = 20)
    {
        var query = ApplyFilters(_context.AlertRecords.AsQueryable(), appCode, allowedAreaIds);

        if (!string.IsNullOrEmpty(status))
        {
            query = query.Where(a => a.Status == status);
        }

        if (!string.IsNullOrEmpty(level))
        {
            query = query.Where(a => a.Level == level);
        }

        if (!string.IsNullOrEmpty(alertType))
        {
            query = query.Where(a => a.AlertType == alertType);
        }

        var totalCount = await query.CountAsync();

        var alerts = await query
            .Include(a => a.Device)
                .ThenInclude(d => d.Area)
            .Include(a => a.ProcessLogs)
            .Include(a => a.WorkOrders)
            .OrderByDescending(a => a.AlertTime)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<AlertRecord>
        {
            Data = alerts,
            TotalCount = totalCount
        };
    }

    public async Task<AlertRecord?> GetAlertWithDetailsAsync(long id, string? appCode = null, List<long>? allowedAreaIds = null)
    {
        var query = ApplyFilters(_context.AlertRecords.AsQueryable(), appCode, allowedAreaIds);
        return await query
            .Include(a => a.Device)
                .ThenInclude(d => d.Area)
            .Include(a => a.ProcessLogs)
            .Include(a => a.WorkOrders)
                .ThenInclude(w => w.Attachments)
            .Include(a => a.WorkOrders)
                .ThenInclude(w => w.Logs)
            .FirstOrDefaultAsync(a => a.Id == id);
    }
}