using IoTPlatform.Data.Repositories.Interfaces;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Helpers;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;

namespace IoTPlatform.Services;

/// <summary>
/// 工单服务实现（使用仓储模式）
/// </summary>
public class WorkOrderService : IWorkOrderService
{
    private readonly IWorkOrderRepository _workOrderRepository;
    private readonly IDeviceRepository _deviceRepository;
    private readonly IAreaRepository _areaRepository;
    private readonly IUnitOfWork _unitOfWork;

    public WorkOrderService(
        IWorkOrderRepository workOrderRepository,
        IDeviceRepository deviceRepository,
        IAreaRepository areaRepository,
        IUnitOfWork unitOfWork)
    {
        _workOrderRepository = workOrderRepository;
        _deviceRepository = deviceRepository;
        _areaRepository = areaRepository;
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// 获取工单列表
    /// </summary>
    public async Task<PagedResponse<WorkOrderDto>> GetWorkOrdersAsync(int page, int pageSize, string? status, string? type, string? priority, string? appCode, List<long>? allowedAreaIds)
    {
        var query = _workOrderRepository.Query().Include(w => w.Device).Include(w => w.Area).Include(w => w.Customer);

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
        var query = _workOrderRepository.Query()
            .Include(w => w.Device)
            .Include(w => w.Area);

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
            var device = await _deviceRepository.GetByIdAsync(request.DeviceId.Value);
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
            var area = await _areaRepository.GetByIdAsync(request.AreaId.Value);
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

        await _workOrderRepository.AddAsync(workOrder);
        await _unitOfWork.SaveChangesAsync();

        // 记录日志
        await _workOrderRepository.AddLogAsync(workOrder.Id, request.Reporter ?? "System", "create", request.Description);

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
        var workOrder = await _workOrderRepository.GetByIdAsync(id, new[] { "Customer", "Device", "Area" });
        if (workOrder == null)
        {
            throw new InvalidOperationException("工单不存在");
        }

        workOrder.Title = request.Title;
        workOrder.Priority = request.Priority;
        workOrder.Description = request.Description;
        workOrder.EstimatedTime = request.EstimatedTime;
        workOrder.UpdatedAt = DateTime.UtcNow;

        await _workOrderRepository.UpdateAsync(workOrder);
        await _unitOfWork.SaveChangesAsync();


        return MapToDto(workOrder);
    }

    /// <summary>
    /// 分配工单
    /// </summary>
    public async Task<WorkOrderDto> AssignWorkOrderAsync(long id, string assignee)
    {
        var workOrder = await _workOrderRepository.GetByIdAsync(id, new[] { "Customer", "Device", "Area" });
        if (workOrder == null)
        {
            throw new InvalidOperationException("工单不存在");
        }

        workOrder.Status = "assigned";
        workOrder.Assignee = assignee;
        workOrder.AssignTime = DateTime.UtcNow;
        workOrder.UpdatedAt = DateTime.UtcNow;

        await _workOrderRepository.UpdateAsync(workOrder);
        await _unitOfWork.SaveChangesAsync();

        // 记录日志
        await _workOrderRepository.AddLogAsync(id, assignee, "assign", $"工单已分配给 {assignee}");
        await _unitOfWork.SaveChangesAsync();

        return MapToDto(workOrder);
    }

    /// <summary>
    /// 开始处理工单
    /// </summary>
    public async Task<WorkOrderDto> StartWorkOrderAsync(long id)
    {
        var workOrder = await _workOrderRepository.GetByIdAsync(id, new[] { "Customer", "Device", "Area" });
        if (workOrder == null)
        {
            throw new InvalidOperationException("工单不存在");
        }

        workOrder.Status = "in_progress";
        workOrder.UpdatedAt = DateTime.UtcNow;

        await _workOrderRepository.UpdateAsync(workOrder);
        await _unitOfWork.SaveChangesAsync();

        // 记录日志
        await _workOrderRepository.AddLogAsync(id, workOrder.Assignee ?? "System", "start", "工单开始处理");
        await _unitOfWork.SaveChangesAsync();

        return MapToDto(workOrder);
    }

    /// <summary>
    /// 解决工单
    /// </summary>
    public async Task<WorkOrderDto> ResolveWorkOrderAsync(long id, string resolveDescription)
    {
        var workOrder = await _workOrderRepository.GetByIdAsync(id, new[] { "Customer", "Device", "Area" });
        if (workOrder == null)
        {
            throw new InvalidOperationException("工单不存在");
        }

        workOrder.Status = "resolved";
        workOrder.ResolveDescription = resolveDescription;
        workOrder.ResolvedTime = DateTime.UtcNow;
        workOrder.UpdatedAt = DateTime.UtcNow;

        await _workOrderRepository.UpdateAsync(workOrder);
        await _unitOfWork.SaveChangesAsync();

        // 记录日志
        await _workOrderRepository.AddLogAsync(id, workOrder.Assignee ?? "System", "resolve", resolveDescription);
        await _unitOfWork.SaveChangesAsync();

        return MapToDto(workOrder);
    }

    /// <summary>
    /// 关闭工单
    /// </summary>
    public async Task<WorkOrderDto> CloseWorkOrderAsync(long id)
    {
        var workOrder = await _workOrderRepository.GetByIdAsync(id, new[] { "Customer", "Device", "Area" });
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

        await _workOrderRepository.UpdateAsync(workOrder);
        await _unitOfWork.SaveChangesAsync();

        // 记录日志
        await _workOrderRepository.AddLogAsync(id, "System", "close", "工单已关闭");
        await _unitOfWork.SaveChangesAsync();

        return MapToDto(workOrder);
    }

    /// <summary>
    /// 拒绝工单
    /// </summary>
    public async Task<WorkOrderDto> RejectWorkOrderAsync(long id, string reason)
    {
        var workOrder = await _workOrderRepository.GetByIdAsync(id, new[] { "Customer", "Device", "Area" });
        if (workOrder == null)
        {
            throw new InvalidOperationException("工单不存在");
        }

        workOrder.Status = "rejected";
        workOrder.UpdatedAt = DateTime.UtcNow;

        await _workOrderRepository.UpdateAsync(workOrder);
        await _unitOfWork.SaveChangesAsync();

        // 记录日志
        await _workOrderRepository.AddLogAsync(id, "System", "reject", reason);
        await _unitOfWork.SaveChangesAsync();

        return MapToDto(workOrder);
    }

    /// <summary>
    /// 获取工单日志
    /// </summary>
    public async Task<List<WorkOrderLogDto>> GetWorkOrderLogsAsync(long workOrderId)
    {
        var logs = await _workOrderRepository.GetLogsAsync(workOrderId);
        return logs.Select(l => new WorkOrderLogDto
        {
            Id = l.Id,
            WorkOrderId = l.WorkOrderId,
            Operator = l.Operator,
            Action = l.Action,
            Comment = l.Comment,
            CreatedAt = l.CreatedAt
        }).ToList();
    }

    /// <summary>
    /// 获取工单附件
    /// </summary>
    public async Task<List<WorkOrderAttachmentDto>> GetWorkOrderAttachmentsAsync(long workOrderId)
    {
        var attachments = await _workOrderRepository.GetAttachmentsAsync(workOrderId);
        return attachments.Select(a => new WorkOrderAttachmentDto
        {
            Id = a.Id,
            WorkOrderId = a.WorkOrderId,
            FileName = a.FileName,
            FileUrl = a.FileUrl,
            FileSize = a.FileSize,
            FileType = a.FileType,
            CreatedAt = a.CreatedAt
        }).ToList();
    }

    /// <summary>
    /// 添加附件
    /// </summary>
    public async Task<WorkOrderAttachmentDto> AddAttachmentAsync(long workOrderId, string fileName, string fileUrl, string fileSize, string fileType)
    {
        var attachment = await _workOrderRepository.AddAttachmentAsync(workOrderId, fileName, fileUrl, fileSize, fileType);
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
        await _workOrderRepository.DeleteAttachmentAsync(attachmentId);
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
            CustomerName = workOrder.Customer?.Name ?? string.Empty,
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
