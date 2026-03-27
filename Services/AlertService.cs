using IoTPlatform.Data;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace IoTPlatform.Services;

/// <summary>
/// 告警服务实现
/// </summary>
public class AlertService : IAlertService
{
    private readonly AppDbContext _dbContext;

    public AlertService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// 获取告警列表
    /// </summary>
    public async Task<PagedResponse<AlertDto>> GetAlertsAsync(int page, int pageSize, string? status, string? level, string? alertType, string? appCode, List<long>? allowedAreaIds)
    {
        var query = _dbContext.AlertRecords
            .Include(a => a.Device)
            .Include(a => a.Area)
            .AsQueryable();

        // 租户过滤
        if (!string.IsNullOrEmpty(appCode))
        {
            query = query.Where(a => a.AppCode == appCode);
        }

        // 区域权限过滤
        if (allowedAreaIds != null && allowedAreaIds.Count > 0)
        {
            query = query.Where(a => a.AreaId == null || allowedAreaIds.Contains(a.AreaId.Value));
        }

        // 状态过滤
        if (!string.IsNullOrEmpty(status))
        {
            query = query.Where(a => a.Status == status);
        }

        // 级别过滤
        if (!string.IsNullOrEmpty(level))
        {
            query = query.Where(a => a.Level == level);
        }

        // 类型过滤
        if (!string.IsNullOrEmpty(alertType))
        {
            query = query.Where(a => a.AlertType == alertType);
        }

        var totalCount = await query.CountAsync();
        var alerts = await query
            .OrderByDescending(a => a.AlertTime)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AlertDto
            {
                Id = a.Id,
                AlertNo = a.AlertNo,
                DeviceName = a.DeviceName,
                DeviceCode = a.DeviceCode,
                Area = a.Area,
                AlertType = a.AlertType,
                Level = a.Level,
                Value = a.Value,
                Threshold = a.Threshold,
                AlertTime = a.AlertTime,
                Status = a.Status,
                Assignee = a.Assignee,
                ResolveTime = a.ResolveTime,
                Remark = a.Remark,
                DeviceId = a.DeviceId,
                AreaId = a.AreaId,
                CreatedAt = a.CreatedAt,
                UpdatedAt = a.UpdatedAt
            })
            .ToListAsync();

        return PagedResponse<AlertDto>.Create(alerts, totalCount, page, pageSize);
    }

    /// <summary>
    /// 获取告警详情
    /// </summary>
    public async Task<AlertDto?> GetAlertAsync(long id, string? appCode, List<long>? allowedAreaIds)
    {
        var query = _dbContext.AlertRecords
            .Include(a => a.Device)
            .Include(a => a.Area)
            .AsQueryable();

        // 租户过滤
        if (!string.IsNullOrEmpty(appCode))
        {
            query = query.Where(a => a.AppCode == appCode);
        }

        // 区域权限过滤
        if (allowedAreaIds != null && allowedAreaIds.Count > 0)
        {
            query = query.Where(a => a.AreaId == null || allowedAreaIds.Contains(a.AreaId.Value));
        }

        var alert = await query.FirstOrDefaultAsync(a => a.Id == id);
        if (alert == null) return null;

        return new AlertDto
        {
            Id = alert.Id,
            AlertNo = alert.AlertNo,
            DeviceName = alert.DeviceName,
            DeviceCode = alert.DeviceCode,
            Area = alert.Area,
            AlertType = alert.AlertType,
            Level = alert.Level,
            Value = alert.Value,
            Threshold = alert.Threshold,
            AlertTime = alert.AlertTime,
            Status = alert.Status,
            Assignee = alert.Assignee,
            ResolveTime = alert.ResolveTime,
            Remark = alert.Remark,
            DeviceId = alert.DeviceId,
            AreaId = alert.AreaId,
            CreatedAt = alert.CreatedAt,
            UpdatedAt = alert.UpdatedAt
        };
    }

    /// <summary>
    /// 创建告警
    /// </summary>
    public async Task<AlertDto> CreateAlertAsync(CreateAlertRequest request)
    {
        // 生成告警编号
        var alertNo = $"ALT{DateTime.UtcNow:yyyyMMddHHmmss}";

        var alert = new AlertRecord
        {
            AlertNo = alertNo,
            DeviceName = request.DeviceName,
            DeviceCode = request.DeviceCode,
            Area = request.Area,
            AlertType = request.AlertType,
            Level = request.Level,
            Value = request.Value,
            Threshold = request.Threshold,
            AlertTime = DateTime.UtcNow,
            Status = "pending",
            Remark = request.Remark,
            DeviceId = request.DeviceId,
            AreaId = request.AreaId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.AlertRecords.Add(alert);
        await _dbContext.SaveChangesAsync();

        return new AlertDto
        {
            Id = alert.Id,
            AlertNo = alert.AlertNo,
            DeviceName = alert.DeviceName,
            DeviceCode = alert.DeviceCode,
            Area = alert.Area,
            AlertType = alert.AlertType,
            Level = alert.Level,
            Value = alert.Value,
            Threshold = alert.Threshold,
            AlertTime = alert.AlertTime,
            Status = alert.Status,
            Assignee = alert.Assignee,
            ResolveTime = alert.ResolveTime,
            Remark = alert.Remark,
            DeviceId = alert.DeviceId,
            AreaId = alert.AreaId,
            CreatedAt = alert.CreatedAt,
            UpdatedAt = alert.UpdatedAt
        };
    }

    /// <summary>
    /// 处理告警
    /// </summary>
    public async Task<AlertDto> ProcessAlertAsync(long id, ProcessAlertRequest request)
    {
        var alert = await _dbContext.AlertRecords.FindAsync(id);
        if (alert == null)
        {
            throw new InvalidOperationException("告警不存在");
        }

        var operatorName = "System"; // 可以从JWT获取

        // 记录处理日志
        var log = new AlertProcessLog
        {
            AlertId = id,
            Operator = operatorName,
            Action = request.Action,
            Comment = request.Comment,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.AlertProcessLogs.Add(log);

        // 更新告警状态
        switch (request.Action.ToLower())
        {
            case "assign":
                alert.Status = "processing";
                break;
            case "process":
                alert.Status = "processing";
                break;
            case "resolve":
                alert.Status = "resolved";
                alert.ResolveTime = DateTime.UtcNow;
                break;
            case "ignore":
                alert.Status = "ignored";
                alert.ResolveTime = DateTime.UtcNow;
                break;
            case "remark":
                // 不改变状态
                break;
        }

        alert.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        return new AlertDto
        {
            Id = alert.Id,
            AlertNo = alert.AlertNo,
            DeviceName = alert.DeviceName,
            DeviceCode = alert.DeviceCode,
            Area = alert.Area,
            AlertType = alert.AlertType,
            Level = alert.Level,
            Value = alert.Value,
            Threshold = alert.Threshold,
            AlertTime = alert.AlertTime,
            Status = alert.Status,
            Assignee = alert.Assignee,
            ResolveTime = alert.ResolveTime,
            Remark = alert.Remark,
            DeviceId = alert.DeviceId,
            AreaId = alert.AreaId,
            CreatedAt = alert.CreatedAt,
            UpdatedAt = alert.UpdatedAt
        };
    }

    /// <summary>
    /// 分配告警
    /// </summary>
    public async Task<AlertDto> AssignAlertAsync(long id, string assignee)
    {
        var alert = await _dbContext.AlertRecords.FindAsync(id);
        if (alert == null)
        {
            throw new InvalidOperationException("告警不存在");
        }

        alert.Status = "processing";
        alert.Assignee = assignee;
        alert.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        return new AlertDto
        {
            Id = alert.Id,
            AlertNo = alert.AlertNo,
            DeviceName = alert.DeviceName,
            DeviceCode = alert.DeviceCode,
            Area = alert.Area,
            AlertType = alert.AlertType,
            Level = alert.Level,
            Value = alert.Value,
            Threshold = alert.Threshold,
            AlertTime = alert.AlertTime,
            Status = alert.Status,
            Assignee = alert.Assignee,
            ResolveTime = alert.ResolveTime,
            Remark = alert.Remark,
            DeviceId = alert.DeviceId,
            AreaId = alert.AreaId,
            CreatedAt = alert.CreatedAt,
            UpdatedAt = alert.UpdatedAt
        };
    }

    /// <summary>
    /// 解决告警
    /// </summary>
    public async Task<AlertDto> ResolveAlertAsync(long id, string? remark)
    {
        var alert = await _dbContext.AlertRecords.FindAsync(id);
        if (alert == null)
        {
            throw new InvalidOperationException("告警不存在");
        }

        alert.Status = "resolved";
        alert.ResolveTime = DateTime.UtcNow;
        alert.Remark = remark;
        alert.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        return new AlertDto
        {
            Id = alert.Id,
            AlertNo = alert.AlertNo,
            DeviceName = alert.DeviceName,
            DeviceCode = alert.DeviceCode,
            Area = alert.Area,
            AlertType = alert.AlertType,
            Level = alert.Level,
            Value = alert.Value,
            Threshold = alert.Threshold,
            AlertTime = alert.AlertTime,
            Status = alert.Status,
            Assignee = alert.Assignee,
            ResolveTime = alert.ResolveTime,
            Remark = alert.Remark,
            DeviceId = alert.DeviceId,
            AreaId = alert.AreaId,
            CreatedAt = alert.CreatedAt,
            UpdatedAt = alert.UpdatedAt
        };
    }

    /// <summary>
    /// 忽略告警
    /// </summary>
    public async Task<AlertDto> IgnoreAlertAsync(long id, string? remark)
    {
        var alert = await _dbContext.AlertRecords.FindAsync(id);
        if (alert == null)
        {
            throw new InvalidOperationException("告警不存在");
        }

        alert.Status = "ignored";
        alert.ResolveTime = DateTime.UtcNow;
        alert.Remark = remark;
        alert.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        return new AlertDto
        {
            Id = alert.Id,
            AlertNo = alert.AlertNo,
            DeviceName = alert.DeviceName,
            DeviceCode = alert.DeviceCode,
            Area = alert.Area,
            AlertType = alert.AlertType,
            Level = alert.Level,
            Value = alert.Value,
            Threshold = alert.Threshold,
            AlertTime = alert.AlertTime,
            Status = alert.Status,
            Assignee = alert.Assignee,
            ResolveTime = alert.ResolveTime,
            Remark = alert.Remark,
            DeviceId = alert.DeviceId,
            AreaId = alert.AreaId,
            CreatedAt = alert.CreatedAt,
            UpdatedAt = alert.UpdatedAt
        };
    }

    /// <summary>
    /// 获取告警处理日志
    /// </summary>
    public async Task<List<AlertProcessLogDto>> GetAlertLogsAsync(long alertId)
    {
        return await _dbContext.AlertProcessLogs
            .Where(l => l.AlertId == alertId)
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => new AlertProcessLogDto
            {
                Id = l.Id,
                AlertId = l.AlertId,
                Operator = l.Operator,
                Action = l.Action,
                Comment = l.Comment,
                CreatedAt = l.CreatedAt
            })
            .ToListAsync();
    }

    /// <summary>
    /// 获取告警汇总
    /// </summary>
    public async Task<AlertSummaryDto> GetAlertSummaryAsync(DateTime? startTime, DateTime? endTime, string? appCode)
    {
        var query = _dbContext.AlertRecords.AsQueryable();

        if (!string.IsNullOrEmpty(appCode))
        {
            query = query.Where(a => a.AppCode == appCode);
        }

        if (startTime.HasValue)
        {
            query = query.Where(a => a.AlertTime >= startTime.Value);
        }
        if (endTime.HasValue)
        {
            query = query.Where(a => a.AlertTime <= endTime.Value);
        }

        var totalAlerts = await query.CountAsync();
        var pendingAlerts = await query.CountAsync(a => a.Status == "pending");
        var processingAlerts = await query.CountAsync(a => a.Status == "processing");
        var resolvedAlerts = await query.CountAsync(a => a.Status == "resolved");
        var ignoredAlerts = await query.CountAsync(a => a.Status == "ignored");
        var criticalAlerts = await query.CountAsync(a => a.Level == "critical");
        var warningAlerts = await query.CountAsync(a => a.Level == "warning");
        var infoAlerts = await query.CountAsync(a => a.Level == "info");

        var alertsByType = new Dictionary<string, int>();
        var typeGroups = await query.GroupBy(a => a.AlertType)
            .Select(g => new { Type = g.Key, Count = g.Count() })
            .ToListAsync();
        foreach (var group in typeGroups)
        {
            alertsByType[group.Type] = group.Count;
        }

        return new AlertSummaryDto
        {
            TotalAlerts = totalAlerts,
            PendingAlerts = pendingAlerts,
            ProcessingAlerts = processingAlerts,
            ResolvedAlerts = resolvedAlerts,
            IgnoredAlerts = ignoredAlerts,
            CriticalAlerts = criticalAlerts,
            WarningAlerts = warningAlerts,
            InfoAlerts = infoAlerts,
            AlertsByType = alertsByType
        };
    }

    /// <summary>
    /// 检查告警规则（告警规则引擎）
    /// </summary>
    public async Task CheckAlertRulesAsync(DeviceDataRecord data)
    {
        // TODO: 实现告警规则引擎
        // 1. 根据设备类型和传感器配置获取告警规则
        // 2. 检查传感器数据是否超过阈值
        // 3. 如果超过阈值，创建告警记录
        // 4. 通过SignalR推送告警通知

        // 示例：温度告警
        if (data.Temperature.HasValue && data.Temperature.Value > 35)
        {
            var alert = new AlertRecord
            {
                AlertNo = $"ALT{DateTime.UtcNow:yyyyMMddHHmmss}",
                DeviceName = data.Device?.Name ?? "未知设备",
                DeviceCode = data.Device?.SerialNumber,
                AlertType = "temperature",
                Level = data.Temperature.Value > 40 ? "critical" : "warning",
                Value = data.Temperature.Value,
                Threshold = 35.0,
                AlertTime = DateTime.UtcNow,
                Status = "pending",
                DeviceId = data.DeviceId,
                AreaId = data.Device?.AreaId,
                AppCode = data.AppCode
            };

            _dbContext.AlertRecords.Add(alert);
            await _dbContext.SaveChangesAsync();

            // TODO: SignalR推送告警
        }

        // 可以添加更多告警规则...
    }
}
