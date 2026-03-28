using IoTPlatform.Data.Repositories.Interfaces;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace IoTPlatform.Services;

/// <summary>
/// 数据采集服务实现（使用仓储模式）
/// </summary>
public class DataCollectionService : IDataCollectionService
{
    private readonly IRepository<DeviceDataRecord> _dataRecordRepository;
    private readonly IDataRuleRepository _dataRuleRepository;
    private readonly IAlertRecordRepository _alertRecordRepository;
    private readonly IDataRuleService _dataRuleService;
    private readonly IAlertService _alertService;
    private readonly IUnitOfWork _unitOfWork;

    public DataCollectionService(
        IRepository<DeviceDataRecord> dataRecordRepository,
        IDataRuleRepository dataRuleRepository,
        IAlertRecordRepository alertRecordRepository,
        IDataRuleService dataRuleService,
        IAlertService alertService,
        IUnitOfWork unitOfWork)
    {
        _dataRecordRepository = dataRecordRepository;
        _dataRuleRepository = dataRuleRepository;
        _alertRecordRepository = alertRecordRepository;
        _dataRuleService = dataRuleService;
        _alertService = alertService;
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// 处理接收到的设备数据
    /// </summary>
    public async Task ProcessDeviceDataAsync(long deviceId, string? appCode, string? sensorData, DateTime timestamp)
    {
        try
        {
            // 1. 保存设备数据记录
            var dataRecord = new DeviceDataRecord
            {
                DeviceId = deviceId,
                SensorData = sensorData,
                Timestamp = timestamp,
                AppCode = appCode
            };

            await _dataRecordRepository.AddAsync(dataRecord);
            await _unitOfWork.SaveChangesAsync();

            Log.Information("Device data saved: DeviceId={DeviceId}, AppCode={AppCode}", deviceId, appCode);

            // 2. 获取活跃的数据规则
            var query = _dataRuleRepository.GetQueryable()
                .Where(r => r.IsActive && r.AppCode == appCode)
                .Where(r => !r.DeviceId.HasValue || r.DeviceId == deviceId);

            var activeRules = await query.ToListAsync();

            // 3. 执行规则
            foreach (var rule in activeRules)
            {
                var ruleTriggered = await _dataRuleService.ExecuteRuleAsync(rule, dataRecord);

                if (ruleTriggered && rule.RuleType == "alert")
                {
                    // 规则触发，创建告警
                    await CreateAlertFromRuleAsync(rule, dataRecord);
                    Log.Warning("Alert triggered by rule: RuleId={RuleId}, DeviceId={DeviceId}", rule.Id, deviceId);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error processing device data: DeviceId={DeviceId}", deviceId);
        }
    }

    /// <summary>
    /// 根据规则创建告警
    /// </summary>
    private async Task CreateAlertFromRuleAsync(DataRule rule, DeviceDataRecord dataRecord)
    {
        // 解析传感器数据获取值
        var ruleEngine = new Services.Rules.RuleEngine();
        var sensorData = ruleEngine.ParseSensorData(dataRecord.SensorData ?? "{}");

        // 确定告警级别
        var alertLevel = rule.Level ?? "warning";
        var alertType = rule.DataType ?? "sensor_data";

        // 确定设备ID
        var deviceId = rule.DeviceId ?? dataRecord.DeviceId;

        // 创建告警记录
        var alert = new AlertRecord
        {
            AlertNo = Guid.NewGuid().ToString("N").Substring(0, 32),
            DeviceId = deviceId,
            AreaId = rule.AreaId,
            AlertType = alertType,
            Level = alertLevel,
            Remark = $"规则 [{rule.Name}] 触发：数据超出阈值",
            Status = "pending",
            Value = sensorData.ContainsKey(rule.DataType ?? "")
                ? double.Parse(sensorData[rule.DataType ?? ""]?.ToString() ?? "0")
                : null,
            Threshold = rule.MaxValue ?? rule.MinValue,
            AlertTime = DateTime.UtcNow,
            AppCode = rule.AppCode,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _alertRecordRepository.AddAsync(alert);
        await _unitOfWork.SaveChangesAsync();
    }
}
