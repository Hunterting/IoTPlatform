using System.Security.Claims;
using AutoMapper;
using IoTPlatform.Data.Repositories.Interfaces;
using IoTPlatform.DTOs;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Helpers;
using IoTPlatform.Models;
using IoTPlatform.Services.Guards;

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
    private readonly ICustomerScopeGuard _scopeGuard;

    public AlertService(
        IAlertRecordRepository alertRecordRepository,
        IDeviceRepository deviceRepository,
        IAreaRepository areaRepository,
        IUnitOfWork unitOfWork,
        IMapper mapper,
        ICustomerScopeGuard scopeGuard)
    {
        _alertRecordRepository = alertRecordRepository;
        _deviceRepository = deviceRepository;
        _areaRepository = areaRepository;
        _unitOfWork = unitOfWork;
        _mapper = mapper;
        _scopeGuard = scopeGuard ?? throw new ArgumentNullException(nameof(scopeGuard));
    }

    /// <summary>按 DeviceId 反查设备所属 AppCode（用于后端产生的告警归属租户）。</summary>
    private async Task<string?> ResolveDeviceAppCodeAsync(long? deviceId)
    {
        if (!deviceId.HasValue) return null;
        try
        {
            var device = await _deviceRepository.GetByIdAsync(deviceId.Value);
            return device?.AppCode;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 获取告警列表
    /// </summary>
    /// <summary>旧签名保留：委托到范围下推重载。</summary>
    public Task<PagedResponse<AlertDto>> GetAlertsAsync(
        int page, int pageSize, string? status, string? level, string? alertType, string? appCode, List<long>? allowedAreaIds)
        => GetAlertsAsync(page, pageSize, status, level, alertType, appCode, allowedAreaIds, null, null);

    /// <summary>B4/M5：admin 授权客户集合真正下推。</summary>
    public async Task<PagedResponse<AlertDto>> GetAlertsAsync(
        int page, int pageSize, string? status, string? level, string? alertType, string? appCode, List<long>? allowedAreaIds,
        IReadOnlyCollection<long>? customerIds, IReadOnlyCollection<string>? appCodes = null)
    {
        var result = await _alertRecordRepository.GetAlertsWithDetailsAsync(
            status, level, alertType, appCode, allowedAreaIds, page, pageSize, customerIds, appCodes);
        
        var alertDtos = _mapper.Map<List<AlertDto>>(result.Data);
        
        return PagedResponse<AlertDto>.Create(alertDtos, result.TotalCount, page, pageSize);
    }

    /// <summary>
    /// 获取告警详情
    /// </summary>
    /// <summary>旧签名保留。</summary>
    public Task<AlertDto?> GetAlertAsync(long id, string? appCode, List<long>? allowedAreaIds)
        => GetAlertAsync(id, appCode, allowedAreaIds, null, null);

    /// <summary>B4/M5：admin 授权客户集合真正下推。</summary>
    public async Task<AlertDto?> GetAlertAsync(
        long id, string? appCode, List<long>? allowedAreaIds,
        IReadOnlyCollection<long>? customerIds, IReadOnlyCollection<string>? appCodes = null)
    {
        var alert = await _alertRecordRepository.GetAlertWithDetailsAsync(id, appCode, allowedAreaIds, customerIds, appCodes);
        return _mapper.Map<AlertDto?>(alert);
    }

    /// <summary>
    /// 创建告警（旧签名保留：内部采集链路零回归）。
    /// </summary>
    public Task<AlertDto> CreateAlertAsync(CreateAlertRequest request, string? appCode = null)
        => CreateAlertAsync(request, appCode, customerScopeVerified: true);

    /// <summary>
    /// M1 二次防御重载：Controller 已用 ICustomerScopeService 校验目标客户后传入 true。
    /// </summary>
    public async Task<AlertDto> CreateAlertAsync(
        CreateAlertRequest request,
        string? appCode,
        bool customerScopeVerified)
    {
        if (!customerScopeVerified)
        {
            throw new ForbiddenAccessException("无权在该客户下创建告警");
        }


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
            // 【租户隔离】告警归属租户：优先服务端解析的 JWT AppCode；
            // 为空时（超管/后台）按 DeviceId 反查设备所属租户，保证「按设备真实告警」也能被租户过滤。
            AppCode = !string.IsNullOrEmpty(appCode) ? appCode : await ResolveDeviceAppCodeAsync(request.DeviceId),
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
    /// <summary>旧签名保留。</summary>
    public Task<AlertSummaryDto> GetAlertSummaryAsync(DateTime? startTime, DateTime? endTime, string? appCode)
        => GetAlertSummaryAsync(startTime, endTime, appCode, null, null);

    /// <summary>B4/M5：admin 授权客户集合真正下推。</summary>
    public async Task<AlertSummaryDto> GetAlertSummaryAsync(
        DateTime? startTime, DateTime? endTime, string? appCode,
        IReadOnlyCollection<long>? customerIds, IReadOnlyCollection<string>? appCodes = null)
    {
        var summary = await _alertRecordRepository.GetAlertSummaryAsync(startTime, endTime, appCode, customerIds, appCodes);
        
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

    // ── M3 显式 principal 重载：归属校验在 UpdateAsync 之前 ──────────────────────

    /// <inheritdoc />
    public async Task<AlertDto> ProcessAlertAsync(long id, ProcessAlertRequest request, ClaimsPrincipal principal)
    {
        await EnsureOwnedByPrincipalAsync(id, principal).ConfigureAwait(false);
        return await ProcessAlertAsync(id, request).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<AlertDto> AssignAlertAsync(long id, string assignee, ClaimsPrincipal principal)
    {
        await EnsureOwnedByPrincipalAsync(id, principal).ConfigureAwait(false);
        return await AssignAlertAsync(id, assignee).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<AlertDto> ResolveAlertAsync(long id, string? remark, ClaimsPrincipal principal)
    {
        await EnsureOwnedByPrincipalAsync(id, principal).ConfigureAwait(false);
        return await ResolveAlertAsync(id, remark).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<AlertDto> IgnoreAlertAsync(long id, string? remark, ClaimsPrincipal principal)
    {
        await EnsureOwnedByPrincipalAsync(id, principal).ConfigureAwait(false);
        return await IgnoreAlertAsync(id, remark).ConfigureAwait(false);
    }

    /// <summary>
    /// M3 归属点查：告警表只有 AppCode 权威列，Multi 由统一判据按 1:1 映射反查 CustomerId。
    /// </summary>
    private async Task EnsureOwnedByPrincipalAsync(long alertId, ClaimsPrincipal principal)
    {
        var alert = await _alertRecordRepository.GetByIdAsync(alertId).ConfigureAwait(false);
        if (alert == null)
        {
            throw new InvalidOperationException("告警不存在");
        }

        if (!await _scopeGuard.CanAccessOwnedEntityAsync(principal, null, alert.AppCode).ConfigureAwait(false))
        {
            throw new ForbiddenAccessException("无权操作该告警");
        }
    }

    /// <summary>
    /// 按设备真实上报值检查阈值并产生告警（数据规则引擎之外的兜底）。
    ///
    /// 【口径】只依据 DeviceDataRecord 的类型化字段（Temperature/Humidity/PM25/CO2/CO/Smoke）。
    /// 安圣等设备的原始值主要落在 SensorData JSON，类型化列多为 null，因此本方法在无类型化值时静默跳过，
    /// 不伪造数据。告警落 AppCode / DeviceName / AreaName（区分租户、按真实设备）。
    /// 去重：同一设备同一 alertType 若已有 pending 告警则不重复创建，避免每 30s 上报刷屏。
    /// </summary>
    private static readonly (string AlertType, double Threshold, Func<DeviceDataRecord, double?> Getter, double CriticalAt)[] Thresholds =
    {
        ("temperature", 35.0,  d => d.Temperature,      40.0),
        ("humidity",    85.0,  d => d.Humidity,         95.0),
        ("pm25",        150.0, d => d.PM25,             250.0),
        ("co2",         2000.0,d => d.CO2,              3000.0),
        ("co",          50.0,  d => d.CO,               100.0),
        ("smoke",       50.0,  d => d.Smoke,            100.0),
    };

    public async Task CheckAlertRulesAsync(DeviceDataRecord data)
    {
        // 【BugFix】移除手动事务（见 CreateAlertAsync 说明）：Repository.AddAsync 已自带 SaveChanges。
        var now = DateTime.UtcNow;

        // 先收集本条数据触发的阈值项
        var triggered = new List<(string AlertType, double Threshold, double Value, string Level)>();
        foreach (var (alertType, threshold, getter, criticalAt) in Thresholds)
        {
            var value = getter(data);
            if (value.HasValue && value.Value > threshold)
            {
                triggered.Add((alertType, threshold, value.Value,
                    value.Value >= criticalAt ? "critical" : "warning"));
            }
        }
        if (triggered.Count == 0) return;

        // 去重：该设备已存在的 pending 告警类型
        var existingPending = await _alertRecordRepository.GetByDeviceIdAsync(data.DeviceId, data.AppCode, "pending");
        var pendingTypes = new HashSet<string>(
            (existingPending ?? Enumerable.Empty<AlertRecord>())
                .Select(a => a.AlertType), StringComparer.OrdinalIgnoreCase);

        foreach (var t in triggered)
        {
            if (pendingTypes.Contains(t.AlertType)) continue; // 已有未处理同类告警，跳过

            await _alertRecordRepository.AddAsync(new AlertRecord
            {
                AlertNo = $"ALT-{now:yyyyMMddHHmmss}-{data.DeviceId}-{t.AlertType}",
                DeviceName = data.DeviceName ?? data.Device?.Name ?? "未知设备",
                DeviceCode = data.Device?.SerialNumber,
                AreaName = data.AreaName,
                AlertType = t.AlertType,
                Level = t.Level,
                Value = t.Value,
                Threshold = t.Threshold,
                AlertTime = now,
                Status = "pending",
                DeviceId = data.DeviceId,
                AreaId = data.AreaId ?? data.Device?.AreaId,
                AppCode = data.AppCode ?? data.Device?.AppCode, // 区分租户
                Remark = $"设备「{data.DeviceName ?? "未知设备"}」{t.AlertType} 上报值 {t.Value:F2} 超过阈值 {t.Threshold:F2}",
                CreatedAt = now,
                UpdatedAt = now,
            });
            pendingTypes.Add(t.AlertType);
        }
    }
}
