using IoTPlatform.Data;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;

namespace IoTPlatform.Services;

/// <summary>
/// 工单服务实现
/// </summary>
public class WorkOrderService : IWorkOrderService
{
    private readonly AppDbContext _dbContext;

    public WorkOrderService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// 获取工单列表
    /// </summary>
    public async Task<PagedResponse<WorkOrderDto>> GetWorkOrdersAsync(int page, int pageSize, string? status, string? type, string? priority, string? appCode, List<long>? allowedAreaIds)
    {
        var query = _dbContext.WorkOrders
            .Include(w => w.Device)
            .Include(w => w.Area)
            .AsQueryable();

        // 租户过滤
        if (!string.IsNullOrEmpty(appCode))
        {
            query = query.Where(w => w.AppCode == appCode);
        }

        // 区域权限过滤
        if (allowedAreaIds != null && allowedAreaIds.Count > 0)
        {
            query = query.Where(w => w.AreaId == null || allowedAreaIds.Contains(w.AreaId.Value));
        }

        // 状态过滤
        if (!string.IsNullOrEmpty(status))
        {
            query = query.Where(w => w.Status == status);
        }

        // 类型过滤
        if (!string.IsNullOrEmpty(type))
        {
            query = query.Where(w => w.Type == type);
        }

        // 优先级过滤
        if (!string.IsNullOrEmpty(priority))
        {
            query = query.Where(w => w.Priority == priority);
        }

        var totalCount = await query.CountAsync();
        var workOrders = await query
            .OrderByDescending(w => w.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(w => new WorkOrderDto
            {
                Id = w.Id,
                OrderNo = w.OrderNo,
                Title = w.Title,
                Type = w.Type,
                Priority = w.Priority,
                Status = w.Status,
                CustomerId = w.CustomerId,
                DeviceId = w.DeviceId,
                AreaId = w.AreaId,
                DeviceName = w.DeviceName,
                DeviceCode = w.DeviceCode,
                AreaName = w.AreaName,
                Description = w.Description,
                Reporter = w.Reporter,
                ReportTime = w.ReportTime,
                Assignee = w.Assignee,
                AssignTime = w.AssignTime,
                EstimatedTime = w.EstimatedTime,
                ResolvedTime = w.ResolvedTime,
                ClosedTime = w.ClosedTime,
                ResolveDescription = w.ResolveDescription,
                ProjectName = w.ProjectName,
                CreatedAt = w.CreatedAt,
                UpdatedAt = w.UpdatedAt
            })
            .ToListAsync();

        return PagedResponse<WorkOrderDto>.Create(workOrders, totalCount, page, pageSize);
    }

    /// <summary>
    /// 获取工单详情
    /// </summary>
    public async Task<WorkOrderDto?> GetWorkOrderAsync(long id, string? appCode, List<long>? allowedAreaIds)
    {
        var query = _dbContext.WorkOrders
            .Include(w => w.Device)
            .Include(w => w.Area)
            .AsQueryable();

        // 租户过滤
        if (!string.IsNullOrEmpty(appCode))
        {
            query = query.Where(w => w.AppCode == appCode);
        }

        // 区域权限过滤
        if (allowedAreaIds != null && allowedAreaIds.Count > 0)
        {
            query = query.Where(w => w.AreaId == null || allowedAreaIds.Contains(w.AreaId.Value));
        }

        var workOrder = await query.FirstOrDefaultAsync(w => w.Id == id);
        if (workOrder == null) return null;

        return new WorkOrderDto
        {
            Id = workOrder.Id,
            OrderNo = workOrder.OrderNo,
            Title = workOrder.Title,
            Type = workOrder.Type,
            Priority = workOrder.Priority,
            Status = workOrder.Status,
            CustomerId = workOrder.CustomerId,
            DeviceId = workOrder.DeviceId,
            AreaId = workOrder.AreaId,
            DeviceName = workOrder.DeviceName,
            DeviceCode = workOrder.DeviceCode,
            AreaName = workOrder.AreaName,
            Description = workOrder.Description,
            Reporter = workOrder.Reporter,
            ReportTime = workOrder.ReportTime,
            Assignee = workOrder.Assignee,
            AssignTime = workOrder.AssignTime,
            EstimatedTime = workOrder.EstimatedTime,
            ResolvedTime = workOrder.ResolvedTime,
            ClosedTime = workOrder.ClosedTime,
            ResolveDescription = workOrder.ResolveDescription,
            ProjectName = workOrder.ProjectName,
            CreatedAt = workOrder.CreatedAt,
            UpdatedAt = workOrder.UpdatedAt
        };
    }

    /// <summary>
    /// 创建工单
    /// </summary>
    public async Task<WorkOrderDto> CreateWorkOrderAsync(CreateWorkOrderRequest request)
    {
        var orderNo = $"WO{DateTime.UtcNow:yyyyMMddHHmmss}";

        // 获取AppCode
        string? appCode = null;
        if (request.DeviceId.HasValue)
        {
            var device = await _dbContext.Devices.FindAsync(request.DeviceId.Value);
            if (device != null)
            {
                request.DeviceName = device.Name;
                request.DeviceCode = device.SerialNumber;
                request.AreaId = device.AreaId;
                request.ProjectName = device.ProjectName;
                appCode = device.AppCode;
            }
        }

        if (request.AreaId.HasValue)
        {
            var area = await _dbContext.Areas.FindAsync(request.AreaId.Value);
            if (area != null)
            {
                request.AreaName = area.Name;
            }
        }

        var workOrder = new WorkOrder
        {
            OrderNo = orderNo,
            Title = request.Title,
            Type = request.Type,
            Priority = request.Priority,
            Status = "pending",
            CustomerId = request.CustomerId,
            DeviceId = request.DeviceId,
            AreaId = request.AreaId,
            DeviceName = request.DeviceName,
            DeviceCode = request.DeviceCode,
            AreaName = request.AreaName,
            Description = request.Description,
            Reporter = request.Reporter,
            ReportTime = DateTime.UtcNow,
            EstimatedTime = request.EstimatedTime,
            ProjectName = request.ProjectName,
            AppCode = appCode,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.WorkOrders.Add(workOrder);

        // 记录日志
        var log = new WorkOrderLog
        {
            WorkOrderId = workOrder.Id,
            Operator = request.Reporter ?? "System",
            Action = "create",
            Comment = request.Description,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.WorkOrderLogs.Add(log);

        await _dbContext.SaveChangesAsync();

        return new WorkOrderDto
        {
            Id = workOrder.Id,
            OrderNo = workOrder.OrderNo,
            Title = workOrder.Title,
            Type = workOrder.Type,
            Priority = workOrder.Priority,
            Status = workOrder.Status,
            CustomerId = workOrder.CustomerId,
            DeviceId = workOrder.DeviceId,
            AreaId = workOrder.AreaId,
            DeviceName = workOrder.DeviceName,
            DeviceCode = workOrder.DeviceCode,
            AreaName = workOrder.AreaName,
            Description = workOrder.Description,
            Reporter = workOrder.Reporter,
            ReportTime = workOrder.ReportTime,
            Assignee = workOrder.Assignee,
            AssignTime = workOrder.AssignTime,
            EstimatedTime = workOrder.EstimatedTime,
            ResolvedTime = workOrder.ResolvedTime,
            ClosedTime = workOrder.ClosedTime,
            ResolveDescription = workOrder.ResolveDescription,
            ProjectName = workOrder.ProjectName,
            CreatedAt = workOrder.CreatedAt,
            UpdatedAt = workOrder.UpdatedAt
        };
    }

    /// <summary>
    /// 更新工单
    /// </summary>
    public async Task<WorkOrderDto> UpdateWorkOrderAsync(long id, UpdateWorkOrderRequest request)
    {
        var workOrder = await _dbContext.WorkOrders.FindAsync(id);
        if (workOrder == null)
        {
            throw new InvalidOperationException("工单不存在");
        }

        workOrder.Title = request.Title;
        workOrder.Priority = request.Priority;
        workOrder.Description = request.Description;
        workOrder.EstimatedTime = request.EstimatedTime;
        workOrder.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        return new WorkOrderDto
        {
            Id = workOrder.Id,
            OrderNo = workOrder.OrderNo,
            Title = workOrder.Title,
            Type = workOrder.Type,
            Priority = workOrder.Priority,
            Status = workOrder.Status,
            CustomerId = workOrder.CustomerId,
            DeviceId = workOrder.DeviceId,
            AreaId = workOrder.AreaId,
            DeviceName = workOrder.DeviceName,
            DeviceCode = workOrder.DeviceCode,
            AreaName = workOrder.AreaName,
            Description = workOrder.Description,
            Reporter = workOrder.Reporter,
            ReportTime = workOrder.ReportTime,
            Assignee = workOrder.Assignee,
            AssignTime = workOrder.AssignTime,
            EstimatedTime = workOrder.EstimatedTime,
            ResolvedTime = workOrder.ResolvedTime,
            ClosedTime = workOrder.ClosedTime,
            ResolveDescription = workOrder.ResolveDescription,
            ProjectName = workOrder.ProjectName,
            CreatedAt = workOrder.CreatedAt,
            UpdatedAt = workOrder.UpdatedAt
        };
    }

    /// <summary>
    /// 分配工单
    /// </summary>
    public async Task<WorkOrderDto> AssignWorkOrderAsync(long id, string assignee)
    {
        var workOrder = await _dbContext.WorkOrders.FindAsync(id);
        if (workOrder == null)
        {
            throw new InvalidOperationException("工单不存在");
        }

        workOrder.Status = "assigned";
        workOrder.Assignee = assignee;
        workOrder.AssignTime = DateTime.UtcNow;
        workOrder.UpdatedAt = DateTime.UtcNow;

        // 记录日志
        var log = new WorkOrderLog
        {
            WorkOrderId = id,
            Operator = assignee,
            Action = "assign",
            Comment = $"工单已分配给 {assignee}",
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.WorkOrderLogs.Add(log);

        await _dbContext.SaveChangesAsync();

        return MapToDto(workOrder);
    }

    /// <summary>
    /// 开始处理工单
    /// </summary>
    public async Task<WorkOrderDto> StartWorkOrderAsync(long id)
    {
        var workOrder = await _dbContext.WorkOrders.FindAsync(id);
        if (workOrder == null)
        {
            throw new InvalidOperationException("工单不存在");
        }

        workOrder.Status = "in_progress";
        workOrder.UpdatedAt = DateTime.UtcNow;

        // 记录日志
        var log = new WorkOrderLog
        {
            WorkOrderId = id,
            Operator = workOrder.Assignee ?? "System",
            Action = "start",
            Comment = "工单开始处理",
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.WorkOrderLogs.Add(log);

        await _dbContext.SaveChangesAsync();

        return MapToDto(workOrder);
    }

    /// <summary>
    /// 解决工单
    /// </summary>
    public async Task<WorkOrderDto> ResolveWorkOrderAsync(long id, string resolveDescription)
    {
        var workOrder = await _dbContext.WorkOrders.FindAsync(id);
        if (workOrder == null)
        {
            throw new InvalidOperationException("工单不存在");
        }

        workOrder.Status = "resolved";
        workOrder.ResolveDescription = resolveDescription;
        workOrder.ResolvedTime = DateTime.UtcNow;
        workOrder.UpdatedAt = DateTime.UtcNow;

        // 记录日志
        var log = new WorkOrderLog
        {
            WorkOrderId = id,
            Operator = workOrder.Assignee ?? "System",
            Action = "resolve",
            Comment = resolveDescription,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.WorkOrderLogs.Add(log);

        await _dbContext.SaveChangesAsync();

        return MapToDto(workOrder);
    }

    /// <summary>
    /// 关闭工单
    /// </summary>
    public async Task<WorkOrderDto> CloseWorkOrderAsync(long id)
    {
        var workOrder = await _dbContext.WorkOrders.FindAsync(id);
        if (workOrder == null)
        {
            throw new InvalidOperationException("工单不存在");
        }

        if (workOrder.Status != "resolved")
        {
            throw new InvalidOperationException("只有已解决的工单才能关闭");
        }

        workOrder.Status = "closed";
        workOrder.ClosedTime = DateTime.UtcNow;
        workOrder.UpdatedAt = DateTime.UtcNow;

        // 记录日志
        var log = new WorkOrderLog
        {
            WorkOrderId = id,
            Operator = "System",
            Action = "close",
            Comment = "工单已关闭",
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.WorkOrderLogs.Add(log);

        await _dbContext.SaveChangesAsync();

        return MapToDto(workOrder);
    }

    /// <summary>
    /// 拒绝工单
    /// </summary>
    public async Task<WorkOrderDto> RejectWorkOrderAsync(long id, string reason)
    {
        var workOrder = await _dbContext.WorkOrders.FindAsync(id);
        if (workOrder == null)
        {
            throw new InvalidOperationException("工单不存在");
        }

        workOrder.Status = "rejected";
        workOrder.UpdatedAt = DateTime.UtcNow;

        // 记录日志
        var log = new WorkOrderLog
        {
            WorkOrderId = id,
            Operator = "System",
            Action = "reject",
            Comment = reason,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.WorkOrderLogs.Add(log);

        await _dbContext.SaveChangesAsync();

        return MapToDto(workOrder);
    }

    /// <summary>
    /// 获取工单日志
    /// </summary>
    public async Task<List<WorkOrderLogDto>> GetWorkOrderLogsAsync(long workOrderId)
    {
        return await _dbContext.WorkOrderLogs
            .Where(l => l.WorkOrderId == workOrderId)
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => new WorkOrderLogDto
            {
                Id = l.Id,
                WorkOrderId = l.WorkOrderId,
                Operator = l.Operator,
                Action = l.Action,
                Comment = l.Comment,
                CreatedAt = l.CreatedAt
            })
            .ToListAsync();
    }

    /// <summary>
    /// 获取工单附件
    /// </summary>
    public async Task<List<WorkOrderAttachmentDto>> GetWorkOrderAttachmentsAsync(long workOrderId)
    {
        return await _dbContext.WorkOrderAttachments
            .Where(a => a.WorkOrderId == workOrderId)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new WorkOrderAttachmentDto
            {
                Id = a.Id,
                WorkOrderId = a.WorkOrderId,
                FileName = a.FileName,
                FileUrl = a.FileUrl,
                FileSize = a.FileSize,
                FileType = a.FileType,
                CreatedAt = a.CreatedAt
            })
            .ToListAsync();
    }

    /// <summary>
    /// 添加附件
    /// </summary>
    public async Task<WorkOrderAttachmentDto> AddAttachmentAsync(long workOrderId, string fileName, string fileUrl, string fileSize, string fileType)
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

        _dbContext.WorkOrderAttachments.Add(attachment);
        await _dbContext.SaveChangesAsync();

        return new WorkOrderAttachmentDto
        {
            Id = attachment.Id,
            WorkOrderId = attachment.WorkOrderId,
            FileName = attachment.FileName,
            FileUrl = attachment.FileUrl,
            FileSize = attachment.FileSize,
            FileType = attachment.FileType,
            CreatedAt = attachment.CreatedAt
        };
    }

    /// <summary>
    /// 删除附件
    /// </summary>
    public async Task DeleteAttachmentAsync(long attachmentId)
    {
        var attachment = await _dbContext.WorkOrderAttachments.FindAsync(attachmentId);
        if (attachment != null)
        {
            _dbContext.WorkOrderAttachments.Remove(attachment);
            await _dbContext.SaveChangesAsync();
        }
    }

    /// <summary>
    /// 映射到DTO
    /// </summary>
    private WorkOrderDto MapToDto(WorkOrder workOrder)
    {
        return new WorkOrderDto
        {
            Id = workOrder.Id,
            OrderNo = workOrder.OrderNo,
            Title = workOrder.Title,
            Type = workOrder.Type,
            Priority = workOrder.Priority,
            Status = workOrder.Status,
            CustomerId = workOrder.CustomerId,
            DeviceId = workOrder.DeviceId,
            AreaId = workOrder.AreaId,
            DeviceName = workOrder.DeviceName,
            DeviceCode = workOrder.DeviceCode,
            AreaName = workOrder.AreaName,
            Description = workOrder.Description,
            Reporter = workOrder.Reporter,
            ReportTime = workOrder.ReportTime,
            Assignee = workOrder.Assignee,
            AssignTime = workOrder.AssignTime,
            EstimatedTime = workOrder.EstimatedTime,
            ResolvedTime = workOrder.ResolvedTime,
            ClosedTime = workOrder.ClosedTime,
            ResolveDescription = workOrder.ResolveDescription,
            ProjectName = workOrder.ProjectName,
            CreatedAt = workOrder.CreatedAt,
            UpdatedAt = workOrder.UpdatedAt
        };
    }
}
