using IoTPlatform.Data;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace IoTPlatform.Services;

/// <summary>
/// 数据采集服务实现
/// </summary>
public class DataCollectionService : IDataCollectionService
{
    private readonly AppDbContext _dbContext;
    private readonly IDataRuleService _dataRuleService;
    private readonly IAlertService _alertService;

    public DataCollectionService(
        AppDbContext dbContext,
        IDataRuleService dataRuleService,
        IAlertService alertService)
    {
        _dbContext = dbContext;
        _dataRuleService = dataRuleService;
        _alertService = alertService;
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

            _dbContext.DeviceDataRecords.Add(dataRecord);
            await _dbContext.SaveChangesAsync();

            Log.Information("Device data saved: DeviceId={DeviceId}, AppCode={AppCode}", deviceId, appCode);

            // 2. 获取活跃的数据规则
            var activeRules = await _dbContext.DataRules
                .Where(r => r.IsActive && r.AppCode == appCode)
                .Where(r => !r.DeviceId.HasValue || r.DeviceId == deviceId)
                .ToListAsync();

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
            DeviceId = deviceId,
            AreaId = rule.AreaId,
            Type = alertType,
            Level = alertLevel,
            Message = $"规则 [{rule.Name}] 触发：数据超出阈值",
            Detail = dataRecord.SensorData,
            Status = "pending",
            Severity = "medium",
            Value = sensorData.ContainsKey(rule.DataType ?? "")
                ? sensorData[rule.DataType ?? ""].ToString()
                : null,
            Unit = null,
            AppCode = rule.AppCode,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.AlertRecords.Add(alert);
        await _dbContext.SaveChangesAsync();
    }
}
