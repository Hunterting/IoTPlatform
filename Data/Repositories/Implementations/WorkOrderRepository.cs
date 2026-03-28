using IoTPlatform.Data.Repositories.Interfaces;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;

namespace IoTPlatform.Data.Repositories.Implementations;

/// <summary>
/// 工单仓储实现类
/// </summary>
public class WorkOrderRepository : Repository<WorkOrder>, IWorkOrderRepository
{
    public WorkOrderRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<WorkOrder?> GetByOrderNoAsync(string orderNo, string? appCode = null)
    {
        var query = ApplyFilters(_context.WorkOrders.AsQueryable(), appCode, null);
        return await query
            .Where(w => w.OrderNo == orderNo)
            .Include(w => w.Device)
            .Include(w => w.Area)
            .Include(w => w.Logs)
            .Include(w => w.Attachments)
            .FirstOrDefaultAsync();
    }

    public async Task<IEnumerable<WorkOrder>> GetByDeviceIdAsync(long deviceId, string? appCode = null, string? status = null)
    {
        var query = ApplyFilters(_context.WorkOrders.AsQueryable(), appCode, null);
        query = query.Where(w => w.DeviceId == deviceId);
        
        if (!string.IsNullOrEmpty(status))
        {
            query = query.Where(w => w.Status == status);
        }
        
        return await query
            .Include(w => w.Device)
            .Include(w => w.Area)
            .Include(w => w.Logs)
            .OrderByDescending(w => w.CreatedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<WorkOrder>> GetByAreaIdAsync(long areaId, string? appCode = null, string? status = null)
    {
        var query = ApplyFilters(_context.WorkOrders.AsQueryable(), appCode, null);
        query = query.Where(w => w.AreaId == areaId);
        
        if (!string.IsNullOrEmpty(status))
        {
            query = query.Where(w => w.Status == status);
        }
        
        return await query
            .Include(w => w.Device)
            .Include(w => w.Area)
            .Include(w => w.Logs)
            .OrderByDescending(w => w.CreatedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<WorkOrder>> GetByCustomerIdAsync(long customerId, string? appCode = null, string? status = null)
    {
        var query = ApplyFilters(_context.WorkOrders.AsQueryable(), appCode, null);
        query = query.Where(w => w.CustomerId == customerId);
        
        if (!string.IsNullOrEmpty(status))
        {
            query = query.Where(w => w.Status == status);
        }
        
        return await query
            .Include(w => w.Device)
            .Include(w => w.Area)
            .Include(w => w.Logs)
            .OrderByDescending(w => w.CreatedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<WorkOrder>> GetByTypeAsync(string type, string? appCode = null, string? status = null)
    {
        var query = ApplyFilters(_context.WorkOrders.AsQueryable(), appCode, null);
        query = query.Where(w => w.Type == type);
        
        if (!string.IsNullOrEmpty(status))
        {
            query = query.Where(w => w.Status == status);
        }
        
        return await query
            .Include(w => w.Device)
            .Include(w => w.Area)
            .Include(w => w.Logs)
            .OrderByDescending(w => w.CreatedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<WorkOrder>> GetByPriorityAsync(string priority, string? appCode = null, string? status = null)
    {
        var query = ApplyFilters(_context.WorkOrders.AsQueryable(), appCode, null);
        query = query.Where(w => w.Priority == priority);
        
        if (!string.IsNullOrEmpty(status))
        {
            query = query.Where(w => w.Status == status);
        }
        
        return await query
            .Include(w => w.Device)
            .Include(w => w.Area)
            .Include(w => w.Logs)
            .OrderByDescending(w => w.CreatedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<WorkOrder>> GetByStatusAsync(string status, string? appCode = null)
    {
        var query = ApplyFilters(_context.WorkOrders.AsQueryable(), appCode, null);
        return await query
            .Where(w => w.Status == status)
            .Include(w => w.Device)
            .Include(w => w.Area)
            .Include(w => w.Logs)
            .OrderByDescending(w => w.CreatedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<WorkOrder>> GetByAssigneeAsync(string assignee, string? appCode = null)
    {
        var query = ApplyFilters(_context.WorkOrders.AsQueryable(), appCode, null);
        return await query
            .Where(w => w.Assignee == assignee)
            .Include(w => w.Device)
            .Include(w => w.Area)
            .Include(w => w.Logs)
            .OrderByDescending(w => w.CreatedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<WorkOrder>> GetByReporterAsync(string reporter, string? appCode = null)
    {
        var query = ApplyFilters(_context.WorkOrders.AsQueryable(), appCode, null);
        return await query
            .Where(w => w.Reporter == reporter)
            .Include(w => w.Device)
            .Include(w => w.Area)
            .Include(w => w.Logs)
            .OrderByDescending(w => w.CreatedAt)
            .ToListAsync();
    }

    public async Task UpdateStatusAsync(long workOrderId, string status, DateTime? resolvedTime = null, DateTime? closedTime = null)
    {
        var workOrder = await _context.WorkOrders.FindAsync(workOrderId);
        if (workOrder != null)
        {
            workOrder.Status = status;
            workOrder.UpdatedAt = DateTime.UtcNow;
            
            if (status == "resolved" && resolvedTime.HasValue)
            {
                workOrder.ResolvedTime = resolvedTime.Value;
            }
            
            if (status == "closed" && closedTime.HasValue)
            {
                workOrder.ClosedTime = closedTime.Value;
            }
            
            await _context.SaveChangesAsync();
        }
    }

    public async Task AssignWorkOrderAsync(long workOrderId, string assignee, DateTime? estimatedTime = null)
    {
        var workOrder = await _context.WorkOrders.FindAsync(workOrderId);
        if (workOrder != null)
        {
            workOrder.Status = "assigned";
            workOrder.Assignee = assignee;
            workOrder.AssignTime = DateTime.UtcNow;
            workOrder.EstimatedTime = estimatedTime;
            workOrder.UpdatedAt = DateTime.UtcNow;
            
            await _context.SaveChangesAsync();
        }
    }

    public async Task<IEnumerable<WorkOrderLog>> GetLogsAsync(long workOrderId)
    {
        return await _context.WorkOrderLogs
            .Where(l => l.WorkOrderId == workOrderId)
            .OrderBy(l => l.CreatedAt)
            .ToListAsync();
    }

    public async Task AddLogAsync(long workOrderId, string operatorName, string action, string? comment = null)
    {
        var log = new WorkOrderLog
        {
            WorkOrderId = workOrderId,
            Operator = operatorName,
            Action = action,
            Comment = comment,
            CreatedAt = DateTime.UtcNow
        };
        
        await _context.WorkOrderLogs.AddAsync(log);
        await _context.SaveChangesAsync();
    }

    public async Task<IEnumerable<WorkOrderAttachment>> GetAttachmentsAsync(long workOrderId)
    {
        return await _context.WorkOrderAttachments
            .Where(a => a.WorkOrderId == workOrderId)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync();
    }

    public async Task AddAttachmentAsync(long workOrderId, string fileName, string fileUrl, string? fileSize = null, string? fileType = null)
    {
        var attachment = new WorkOrderAttachment
        {
            WorkOrderId = workOrderId,
            FileName = fileName,
            FileUrl = fileUrl,
            FileSize = fileSize,
            FileType = fileType,
            CreatedAt = DateTime.UtcNow
        };
        
        await _context.WorkOrderAttachments.AddAsync(attachment);
        await _context.SaveChangesAsync();
    }

    public async Task<WorkOrderStats> GetWorkOrderStatsAsync(DateTime? startTime = null, DateTime? endTime = null, string? appCode = null)
    {
        var query = ApplyFilters(_context.WorkOrders.AsQueryable(), appCode, null);
        
        if (startTime.HasValue)
        {
            query = query.Where(w => w.CreatedAt >= startTime.Value);
        }
        
        if (endTime.HasValue)
        {
            query = query.Where(w => w.CreatedAt <= endTime.Value);
        }
        
        var workOrders = await query.ToListAsync();
        
        var stats = new WorkOrderStats
        {
            TotalWorkOrders = workOrders.Count,
            PendingWorkOrders = workOrders.Count(w => w.Status == "pending"),
            AssignedWorkOrders = workOrders.Count(w => w.Status == "assigned"),
            InProgressWorkOrders = workOrders.Count(w => w.Status == "in_progress"),
            ResolvedWorkOrders = workOrders.Count(w => w.Status == "resolved"),
            ClosedWorkOrders = workOrders.Count(w => w.Status == "closed"),
            UrgentWorkOrders = workOrders.Count(w => w.Priority == "urgent"),
            HighPriorityWorkOrders = workOrders.Count(w => w.Priority == "high"),
            MediumPriorityWorkOrders = workOrders.Count(w => w.Priority == "medium"),
            LowPriorityWorkOrders = workOrders.Count(w => w.Priority == "low")
        };
        
        // 按工单类型统计
        stats.WorkOrdersByType = workOrders
            .GroupBy(w => w.Type)
            .ToDictionary(g => g.Key, g => g.Count());
        
        return stats;
    }

    public async Task<int> GetPendingWorkOrderCountAsync(string? appCode = null)
    {
        var query = ApplyFilters(_context.WorkOrders.AsQueryable(), appCode, null);
        return await query.CountAsync(w => w.Status == "pending");
    }

    public async Task<int> GetUrgentWorkOrderCountAsync(string? appCode = null)
    {
        var query = ApplyFilters(_context.WorkOrders.AsQueryable(), appCode, null);
        return await query.CountAsync(w => w.Priority == "urgent" && (w.Status == "pending" || w.Status == "assigned" || w.Status == "in_progress"));
    }
}