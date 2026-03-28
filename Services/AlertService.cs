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
        try
        {
            await _unitOfWork.BeginTransactionAsync();
            
            // 生成告警编号
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

            await _alertRecordRepository.AddAsync(alert);
            await _unitOfWork.CommitAsync();

            return _mapper.Map<AlertDto>(alert);
        }
        catch
        {
            await _unitOfWork.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// 处理告警
    /// </summary>
    public async Task<AlertDto> ProcessAlertAsync(long id, ProcessAlertRequest request)
    {
        try
        {
            await _unitOfWork.BeginTransactionAsync();
            
            var alert = await _alertRecordRepository.GetByIdAsync(id);
            if (alert == null)
            {
                throw new InvalidOperationException("告警不存在");
            }

            var operatorName = "System"; // 可以从JWT获取

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
            
            await _alertRecordRepository.UpdateAsync(alert);
            await _unitOfWork.CommitAsync();

            return _mapper.Map<AlertDto>(alert);
        }
        catch
        {
            await _unitOfWork.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// 分配告警
    /// </summary>
    public async Task<AlertDto> AssignAlertAsync(long id, string assignee)
    {
        try
        {
            await _unitOfWork.BeginTransactionAsync();
            
            var alert = await _alertRecordRepository.GetByIdAsync(id);
            if (alert == null)
            {
                throw new InvalidOperationException("告警不存在");
            }

            alert.Status = "processing";
            alert.Assignee = assignee;
            alert.UpdatedAt = DateTime.UtcNow;

            await _alertRecordRepository.UpdateAsync(alert);
            await _unitOfWork.CommitAsync();

            return _mapper.Map<AlertDto>(alert);
        }
        catch
        {
            await _unitOfWork.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// 解决告警
    /// </summary>
    public async Task<AlertDto> ResolveAlertAsync(long id, string? remark)
    {
        try
        {
            await _unitOfWork.BeginTransactionAsync();
            
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
            await _unitOfWork.CommitAsync();

            return _mapper.Map<AlertDto>(alert);
        }
        catch
        {
            await _unitOfWork.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// 忽略告警
    /// </summary>
    public async Task<AlertDto> IgnoreAlertAsync(long id, string? remark)
    {
        try
        {
            await _unitOfWork.BeginTransactionAsync();
            
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
            await _unitOfWork.CommitAsync();

            return _mapper.Map<AlertDto>(alert);
        }
        catch
        {
            await _unitOfWork.RollbackAsync();
            throw;
        }
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
        try
        {
            await _unitOfWork.BeginTransactionAsync();

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

                await _alertRecordRepository.AddAsync(alert);
                await _unitOfWork.CommitAsync();

                // TODO: SignalR推送告警
            }

            // 可以添加更多告警规则...

            await _unitOfWork.CommitAsync();
        }
        catch
        {
            await _unitOfWork.RollbackAsync();
            throw;
        }
    }
}
