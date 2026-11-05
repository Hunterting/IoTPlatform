using AutoMapper;
using IoTPlatform.Data.Repositories.Interfaces;
using IoTPlatform.DTOs;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Helpers;
using IoTPlatform.Models;

namespace IoTPlatform.Services;

/// <summary>
/// 告警服务实现（使用仓储模式）
/// </summary>
public class AlertService : IAlertService
{
    private readonly IAlertRecordRepository _alertRecordRepository;
    private readonly IDeviceRepository _deviceRepository;
    private readonly IAreaRepository _areaRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    public AlertService(
        IAlertRecordRepository alertRecordRepository,
        IDeviceRepository deviceRepository,
        IAreaRepository areaRepository,
        IUnitOfWork unitOfWork,
        IMapper mapper)
    {
        _alertRecordRepository = alertRecordRepository;
        _deviceRepository = deviceRepository;
        _areaRepository = areaRepository;
        _unitOfWork = unitOfWork;
        _mapper = mapper;
    }

    /// <summary>
    /// 获取告警列表
    /// </summary>
    public async Task<PagedResponse<AlertDto>> GetAlertsAsync(int page, int pageSize, string? status, string? level, string? alertType, string? appCode, List<long>? allowedAreaIds)
    {
        var result = await _alertRecordRepository.GetAlertsWithDetailsAsync(
            status, level, alertType, appCode, allowedAreaIds, page, pageSize);
        
        var alertDtos = _mapper.Map<List<AlertDto>>(result.Data);
        
        return PagedResponse<AlertDto>.Create(alertDtos, result.TotalCount, page, pageSize);
    }

    /// <summary>
    /// 获取告警详情
    /// </summary>
    public async Task<AlertDto?> GetAlertAsync(long id, string? appCode, List<long>? allowedAreaIds)
    {
        var alert = await _alertRecordRepository.GetAlertWithDetailsAsync(id, appCode, allowedAreaIds);
        return _mapper.Map<AlertDto?>(alert);
    }

    /// <summary>
    /// 创建告警
    /// </summary>
    public async Task<AlertDto> CreateAlertAsync(CreateAlertRequest request)
    {
        // 【BugFix】原先用 IUnitOfWork.BeginTransactionAsync 手动开启事务，
        // 但本库启用了 EnableRetryOnFailure（MySqlRetryingExecutionStrategy），
        // 该策略禁止用户发起事务，导致本端点恒抛 InvalidOperationException → HTTP 400。
        // 单表单条写本就由 EF Core 的 SaveChangesAsync 隐式事务保证原子性，
        // 且 Repository.AddAsync 内部已 SaveChangesAsync，故移除多余手动事务。
        var alertNo = $"ALT{DateTime.UtcNow:yyyyMMddHHmmss}";

        var alert = new AlertRecord
        {
            AlertNo = alertNo,
            DeviceName = request.DeviceName,
            DeviceCode = request.DeviceCode,
            AreaName = request.Area,
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

        // AddAsync 内部已 SaveChangesAsync；不再 Begin/Commit/Rollback
        await _alertRecordRepository.AddAsync(alert);
        return _mapper.Map<AlertDto>(alert);
    }

    /// <summary>
    /// 处理告警
    /// </summary>
    public async Task<AlertDto> ProcessAlertAsync(long id, ProcessAlertRequest request)
    {
        // 【BugFix】移除手动事务（见 CreateAlertAsync 说明）
        var alert = await _alertRecordRepository.GetByIdAsync(id);
        if (alert == null)
        {
            throw new InvalidOperationException("告警不存在");
        }

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

        // UpdateAsync 内部已 SaveChangesAsync；不再 Begin/Commit/Rollback
        await _alertRecordRepository.UpdateAsync(alert);
        return _mapper.Map<AlertDto>(alert);
    }

    /// <summary>
    /// 分配告警
    /// </summary>
    public async Task<AlertDto> AssignAlertAsync(long id, string assignee)
    {
        // 【BugFix】移除手动事务（见 CreateAlertAsync 说明）
        var alert = await _alertRecordRepository.GetByIdAsync(id);
        if (alert == null)
        {
            throw new InvalidOperationException("告警不存在");
        }

        alert.Status = "processing";
        alert.Assignee = assignee;
        alert.UpdatedAt = DateTime.UtcNow;

        await _alertRecordRepository.UpdateAsync(alert);
        return _mapper.Map<AlertDto>(alert);
    }

    /// <summary>
    /// 解决告警
    /// </summary>
    public async Task<AlertDto> ResolveAlertAsync(long id, string? remark)
    {
        // 【BugFix】移除手动事务（见 CreateAlertAsync 说明）
        var alert = await _alertRecordRepository.GetByIdAsync(id);
        if (alert == null)
        {
            throw new InvalidOperationException("告警不存在");
        }

        alert.Status = "resolved";
        alert.ResolveTime = DateTime.UtcNow;
        alert.Remark = remark;
        alert.UpdatedAt = DateTime.UtcNow;

        await _alertRecordRepository.UpdateAsync(alert);
        return _mapper.Map<AlertDto>(alert);
    }

    /// <summary>
    /// 忽略告警
    /// </summary>
    public async Task<AlertDto> IgnoreAlertAsync(long id, string? remark)
    {
        // 【BugFix】移除手动事务（见 CreateAlertAsync 说明）
        var alert = await _alertRecordRepository.GetByIdAsync(id);
        if (alert == null)
        {
            throw new InvalidOperationException("告警不存在");
        }

        alert.Status = "ignored";
        alert.ResolveTime = DateTime.UtcNow;
        alert.Remark = remark;
        alert.UpdatedAt = DateTime.UtcNow;

        await _alertRecordRepository.UpdateAsync(alert);
        return _mapper.Map<AlertDto>(alert);
    }

    /// <summary>
    /// 获取告警处理日志
    /// </summary>
    public async Task<List<AlertProcessLogDto>> GetAlertLogsAsync(long alertId)
    {
        var logs = await _alertRecordRepository.GetAlertLogsAsync(alertId);
        return _mapper.Map<List<AlertProcessLogDto>>(logs);
    }

    /// <summary>
    /// 获取告警汇总
    /// </summary>
    public async Task<AlertSummaryDto> GetAlertSummaryAsync(DateTime? startTime, DateTime? endTime, string? appCode)
    {
        var summary = await _alertRecordRepository.GetAlertSummaryAsync(startTime, endTime, appCode);
        
        return new AlertSummaryDto
        {
            TotalAlerts = summary.TotalAlerts,
            PendingAlerts = summary.PendingAlerts,
            ProcessingAlerts = summary.ProcessingAlerts,
            ResolvedAlerts = summary.ResolvedAlerts,
            IgnoredAlerts = summary.IgnoredAlerts,
            CriticalAlerts = summary.CriticalAlerts,
            WarningAlerts = summary.WarningAlerts,
            InfoAlerts = summary.InfoAlerts,
            AlertsByType = summary.AlertsByType
        };
    }

    /// <summary>
    /// 检查告警规则（告警规则引擎）
    /// </summary>
    public async Task CheckAlertRulesAsync(DeviceDataRecord data)
    {
        // 【BugFix】移除手动事务（见 CreateAlertAsync 说明）。
        // 同时修掉原实现的两处问题：① 超阈值时 Commit 两次（第二次会因「没有活动的事务」抛异常）；
        // ② 未超阈值时也 Commit（空事务）。现改为 AddAsync 自带的一次 SaveChanges。
        // TODO: 实现告警规则引擎（阈值配置化、SignalR 推送等）
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

            await _alertRecordRepository.AddAsync(alert);
            // TODO: SignalR 推送告警
        }
        // 可以添加更多告警规则...
    }
}
