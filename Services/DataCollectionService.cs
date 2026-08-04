using IoTPlatform.Data.Repositories.Interfaces;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.RegularExpressions;

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
    private readonly IRepository<DeviceSensor> _deviceSensorRepository;
    private readonly IRepository<Device> _deviceRepository;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>
    /// 传感器 JSON key → DeviceDataRecord 物理量字段映射表
    /// 支持设备上报的各种命名风格，统一映射到标准字段
    /// </summary>
    private static readonly Dictionary<string, Action<DeviceDataRecord, double>> SensorFieldMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        // === 环境传感器 ===
        { "temperature",       (r, v) => r.Temperature       = v },
        { "temp",              (r, v) => r.Temperature       = v },
        { "humidity",          (r, v) => r.Humidity          = v },
        { "hum",               (r, v) => r.Humidity          = v },
        { "pm25",              (r, v) => r.PM25              = v },
        { "pm2.5",             (r, v) => r.PM25              = v },
        { "pm10",              (r, v) => r.PM10              = v },
        { "co2",               (r, v) => r.CO2               = v },
        { "co",                (r, v) => r.CO                = v },
        { "freshairvolume",    (r, v) => r.FreshAirVolume    = v },
        { "fresh_air_volume",  (r, v) => r.FreshAirVolume    = v },
        { "combustiblegas",    (r, v) => r.CombustibleGas    = v },
        { "combustible_gas",   (r, v) => r.CombustibleGas    = v },
        { "formaldehyde",      (r, v) => r.Formaldehyde      = v },
        { "smoke",             (r, v) => r.Smoke             = v },
        { "tvoc",              (r, v) => r.TVOC              = v },
        { "exhaustvolume",     (r, v) => r.ExhaustVolume     = v },
        { "exhaust_volume",    (r, v) => r.ExhaustVolume     = v },
        { "smokeconcentration",(r, v) => r.SmokeConcentration= v },
        { "smoke_concentration",(r, v) => r.SmokeConcentration= v },
        { "oilfume",           (r, v) => r.OilFume           = v },
        { "oil_fume",         (r, v) => r.OilFume           = v },
        { "noise",             (r, v) => r.Noise             = v },

        // === 水/电/气 能耗计量 ===
        { "waterflow",         (r, v) => r.WaterFlow         = v },
        { "water_flow",        (r, v) => r.WaterFlow         = v },
        { "watertotal",        (r, v) => r.WaterTotal        = v },
        { "water_total",       (r, v) => r.WaterTotal        = v },
        { "electricpower",     (r, v) => r.ElectricPower     = v },
        { "electric_power",    (r, v) => r.ElectricPower     = v },
        { "power",             (r, v) => r.ElectricPower     = v },
        { "electrickwh",       (r, v) => r.ElectricKWh       = v },
        { "electric_kwh",      (r, v) => r.ElectricKWh       = v },
        { "kwh",               (r, v) => r.ElectricKWh       = v },
        { "gasflow",           (r, v) => r.GasFlow           = v },
        { "gas_flow",          (r, v) => r.GasFlow           = v },
        { "gastotal",          (r, v) => r.GasTotal          = v },
        { "gas_total",         (r, v) => r.GasTotal          = v },

        // === 安圣 MQTT 标准化字段 ===
        { "total_power",       (r, v) => r.ElectricPower     = v },
        { "total_energy",      (r, v) => r.ElectricKWh       = v },
        { "total_current",     (r, v) => { /* Current not in schema; stored in SensorData */ } },
        { "avg_voltage",       (r, v) => { /* Voltage not on DeviceDataRecord; stored in SensorData */ } },
        { "energy",            (r, v) => r.ElectricKWh       = v },
        // signal（4G 信号强度 1-31）在 DeviceDataRecord 上没有专用列，值随 SensorData 落库。
        // 之所以仍然登记在映射表里，是为了让它<b>计入 mappedFields</b> 并驱动 DeviceSensor.LastValue
        // ——现场排障最常问的一句话就是「这台设备信号多少」，它必须是可查询的一等字段。
        { "signal",            (r, v) => { /* Signal not on DeviceDataRecord; stored in SensorData */ } },
    };

    /// <summary>
    /// 安圣逐位路数据点的字段名模式：<c>slot1_voltage</c> / <c>slot2_state</c> / <c>slot3_energy</c> …
    ///
    /// 【为什么用正则而不是穷举映射表】
    ///   位路数量由设备型号决定（1~n 路，现场见过 16 路），把 n×5 个键写死进字典
    ///   既写不全也维护不动。位路字段在 <c>DeviceDataRecord</c> 上<b>没有</b>对应列
    ///   （它们的值随 <c>SensorData</c> JSON 无损落库），正则的唯一职责是
    ///   「确认这是一个受支持的数据点」——从而计入 <c>mappedFields</c> 并更新
    ///   <c>DeviceSensor.LastValue</c>，让前端能按位路建传感器。
    /// </summary>
    private static readonly Regex SlotFieldRegex = new(
        @"^slot(\d+)_(state|voltage|current|power|energy|pf)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// 统一判定「该 JSON 属性是否为受支持的传感器数据点」。
    ///
    /// 判定顺序（与 T6-2 设计 §4.2 一致）：
    ///   1. 精确表 <see cref="SensorFieldMappings"/> 命中（含环境/能耗/安圣标准化字段，
    ///      以及 <c>signal</c> 等仅在 SensorData 落库的无列表字段）；
    ///   2. 位路展平字段兜底：<see cref="SlotFieldRegex"/> 命中
    ///      （<c>slot1_voltage</c> … <c>slot{n}_pf</c>），其数量由设备型号决定，无法穷举。
    ///
    /// 命中即视为「已识别的传感器字段」，由调用方计入 <c>mappedFieldNames</c>
    /// （驱动结构化日志），其值在 step ③ 原样进入 <c>lastValueUpdates</c>
    /// （驱动 <c>DeviceSensor.LastValue</c> 更新，前端按位路建传感器）。
    /// </summary>
    /// <param name="jsonProp">SensorData JSON 中的单个属性</param>
    /// <param name="fieldName">命中时输出规范化字段名（即 <paramref name="jsonProp"/> 的 key）</param>
    /// <returns>是否为受支持的传感器数据点</returns>
    private static bool TryResolveSensorField(JsonProperty jsonProp, [NotNullWhen(true)] out string? fieldName)
    {
        // ① 精确表优先：覆盖 total_power / avg_voltage / signal 等已知键
        if (SensorFieldMappings.ContainsKey(jsonProp.Name))
        {
            fieldName = jsonProp.Name;
            return true;
        }

        // ② 位路展平字段兜底：slot{n}_* 不进入精确表（数量未知），用正则识别
        if (SlotFieldRegex.IsMatch(jsonProp.Name))
        {
            fieldName = jsonProp.Name;
            return true;
        }

        fieldName = null;
        return false;
    }

    /// <summary>
    /// 能源类型策略定义
    /// 每种能源类型定义：关键字段集合、有效范围、数据级别标签
    /// </summary>
    private static readonly Dictionary<string, EnergyTypeStrategy> EnergyTypeStrategies = new Dictionary<string, EnergyTypeStrategy>(StringComparer.OrdinalIgnoreCase)
    {
        ["water"] = new EnergyTypeStrategy
        {
            DisplayName = "水计量",
            KeyFields  = new List<string> { "WaterFlow", "WaterTotal" },
            RelevantJsonKeys = new List<string> { "waterflow", "water_flow", "watertotal", "water_total" },
            ValidRanges = new Dictionary<string, (double Min, double Max)>
                            {
                                ["WaterFlow"]   = (0, 10000),
                                ["WaterTotal"]  = (0, double.MaxValue)
                            },
            DefaultLevel = "info",
        },
        ["electric"] = new EnergyTypeStrategy
        {
            DisplayName = "电计量",
            KeyFields  = new List<string> { "ElectricPower", "ElectricKWh" },
            RelevantJsonKeys = new List<string>
                                { "electricpower", "electric_power", "power", "electrickwh",
                                  "electric_kwh", "kwh", "voltage", "current", "powerfactor" },
            ValidRanges = new Dictionary<string, (double Min, double Max)>
                            {
                                ["ElectricPower"] = (0, 100000),
                                ["ElectricKWh"]  = (0, double.MaxValue)
                            },
            DefaultLevel = "info",
        },
        ["gas"] = new EnergyTypeStrategy
        {
            DisplayName = "气计量",
            KeyFields  = new List<string> { "GasFlow", "GasTotal" },
            RelevantJsonKeys = new List<string> { "gasflow", "gas_flow", "gastotal", "gas_total" },
            ValidRanges = new Dictionary<string, (double Min, double Max)>
                            {
                                ["GasFlow"]  = (0, 10000),
                                ["GasTotal"] = (0, double.MaxValue)
                            },
            DefaultLevel = "info",
        },
    };

    public DataCollectionService(
        IRepository<DeviceDataRecord> dataRecordRepository,
        IDataRuleRepository dataRuleRepository,
        IAlertRecordRepository alertRecordRepository,
        IDataRuleService dataRuleService,
        IAlertService alertService,
        IRepository<DeviceSensor> deviceSensorRepository,
        IRepository<Device> deviceRepository,
        IUnitOfWork unitOfWork)
    {
        _dataRecordRepository = dataRecordRepository;
        _dataRuleRepository = dataRuleRepository;
        _alertRecordRepository = alertRecordRepository;
        _dataRuleService = dataRuleService;
        _alertService = alertService;
        _deviceSensorRepository = deviceSensorRepository;
        _deviceRepository = deviceRepository;
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// 处理接收到的设备数据（含 EnergyTypes 差异化解析）
    ///
    /// 流程：
    /// 0. 查询设备元数据 → 解析 EnergyTypes → 确定设备类型策略
    /// 1. 解析 SensorData JSON → 映射到物理量字段（通用 + 类型专属）
    /// 2. 按能源类型校验数据有效范围 → 标记异常级别
    /// 3. 保存数据记录（enriched with DeviceName/AreaName/Level）
    /// 4. 更新设备传感器 LastValue
    /// 5. 执行活跃的数据规则
    /// </summary>
    public async Task ProcessDeviceDataAsync(long deviceId, string? appCode, string? sensorData, DateTime timestamp)
    {
        try
        {
            // ── Step 0: 查询设备元数据，确定能源类型策略 ──
            var device = await _deviceRepository.GetByIdAsync(deviceId, appCode: appCode);
            var energyTypes = ParseEnergyTypes(device?.EnergyTypes);
            var activeStrategies = energyTypes
                .Where(t => EnergyTypeStrategies.ContainsKey(t))
                .Select(t => EnergyTypeStrategies[t])
                .ToList();

            // 构建数据记录
            var dataRecord = new DeviceDataRecord
            {
                DeviceId = deviceId,
                SensorData = sensorData,
                Timestamp = timestamp,
                AppCode = appCode,
                // Enrichment: 从设备元数据填充名称和区域信息
                DeviceName = device?.Name,
                AreaId     = device?.AreaId,
                AreaName   = device?.Area?.Name,
                Status     = device?.Status ?? "unknown",
                Level      = activeStrategies.Count > 0 ? "info" : null // 默认级别，后续可能被覆盖
            };

            // 解析 SensorData JSON → 映射到专用字段 + 收集 LastValue 更新
            Dictionary<string, string>? lastValueUpdates = null;
            var mappedFieldNames = new List<string>(8);

            if (!string.IsNullOrWhiteSpace(sensorData))
            {
                try
                {
                    var jsonDoc = JsonDocument.Parse(sensorData);
                    lastValueUpdates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    bool hasOutOfRangeValue = false;

                    foreach (var jsonProp in jsonDoc.RootElement.EnumerateObject())
                    {
                        // ① 通用字段映射（环境监测 + 能耗计量 + 安圣标准化字段 + 位路展平字段）
                        //    TryResolveSensorField 统一决定「该 key 是否为受支持的传感器数据点」：
                        //    命中即计入 mappedFieldNames（驱动结构化日志与 LastValue 统计口径）。
                        if (TryResolveSensorField(jsonProp, out var resolvedField))
                        {
                            mappedFieldNames.Add(resolvedField);

                            // ①-a 精确表命中且为数值 → 映射到 DeviceDataRecord 物理量列
                            if (SensorFieldMappings.TryGetValue(jsonProp.Name, out var setter) &&
                                jsonProp.Value.ValueKind == JsonValueKind.Number)
                            {
                                var numValue = jsonProp.Value.GetDouble();
                                setter(dataRecord, numValue);

                                // ② 按 EnergyType 策略做范围校验
                                foreach (var strategy in activeStrategies)
                                {
                                    if (strategy.RelevantJsonKeys.Contains(jsonProp.Name, StringComparer.OrdinalIgnoreCase))
                                    {
                                        // 找到对应的属性名做范围检查（需要从 setter 反推或用映射表）
                                        if (TryGetMappedPropertyName(jsonProp.Name, out var propName) &&
                                            strategy.ValidRanges.TryGetValue(propName, out var range))
                                        {
                                            if (numValue < range.Min || numValue > range.Max)
                                            {
                                                hasOutOfRangeValue = true;
                                                Log.Warning("Energy data out of range: DeviceId={DeviceId}, Field={Field}, Value={Value}, Range=[{Min}~{Max}], Type={Type}",
                                                    deviceId, jsonProp.Name, numValue, range.Min, range.Max, strategy.DisplayName);
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        // ③ 收集所有 key-value 用于更新 LastValue（原样透传，含 signal / slot* / 旧键）
                        var valueStr = jsonProp.Value.ValueKind switch
                        {
                            JsonValueKind.Number => jsonProp.Value.GetDouble().ToString("F4"),
                            JsonValueKind.String => jsonProp.Value.GetString() ?? "",
                            JsonValueKind.True => "true",
                            JsonValueKind.False => "false",
                            _ => jsonProp.Value.ToString()
                        };
                        lastValueUpdates[jsonProp.Name] = valueStr;
                    }

                    // ④ 根据是否有超范围值调整数据记录级别
                    if (hasOutOfRangeValue)
                    {
                        dataRecord.Level = "warning";
                    }
                }
                catch (JsonException ex)
                {
                    Log.Warning(ex, "SensorData JSON 解析失败，原样存储: DeviceId={DeviceId}", deviceId);
                    dataRecord.Level = "error";
                }
            }

            // ── Step 2: 保存数据记录 ──
            await _dataRecordRepository.AddAsync(dataRecord);
            await _unitOfWork.SaveChangesAsync();

            // 日志：按能源类型输出结构化信息
            if (activeStrategies.Count > 0)
            {
                var typeNames = string.Join("+", activeStrategies.Select(s => s.DisplayName));
                Log.Information("Energy data collected: DeviceId={DeviceId}, Types={Types}, Fields={Fields}, DeviceName={Name}",
                    deviceId, typeNames, string.Join(",", mappedFieldNames), device?.Name ?? "Unknown");
            }
            else
            {
                Log.Debug("Generic sensor data collected: DeviceId={DeviceId}, MappedFields={MappedCount}",
                    deviceId, mappedFieldNames.Count);
            }

            // ── Step 3: 更新设备传感器 LastValue ──
            if (lastValueUpdates != null && lastValueUpdates.Count > 0)
            {
                await UpdateSensorLastValuesAsync(deviceId, appCode, lastValueUpdates);
            }

            // ── Step 4: 执行活跃的数据规则 ──
            var query = _dataRuleRepository.GetQueryable()
                .Where(r => r.IsActive && r.AppCode == appCode)
                .Where(r => !r.DeviceId.HasValue || r.DeviceId == deviceId);

            var activeRules = await query.ToListAsync();

            foreach (var rule in activeRules)
            {
                var ruleTriggered = await _dataRuleService.ExecuteRuleAsync(rule, dataRecord);

                if (ruleTriggered && rule.RuleType == "alert")
                {
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
    /// 根据解析出的传感器 key-values 更新对应 DeviceSensor.LastValue
    /// </summary>
    private async Task UpdateSensorLastValuesAsync(long deviceId, string? appCode, Dictionary<string, string> updates)
    {
        try
        {
            // 查询该设备下所有已注册的传感器
            var sensors = await _deviceSensorRepository.GetAsync(
                s => s.DeviceId == deviceId,
                appCode: appCode);

            var sensorList = sensors.ToList();
            if (sensorList.Count == 0) return;

            var updated = false;
            foreach (var sensor in sensorList)
            {
                // 按传感器名称或类型匹配 JSON 中的 key
                if (updates.TryGetValue(sensor.Name, out var newValue) ||
                    (sensor.SensorType != null && updates.TryGetValue(sensor.SensorType, out newValue)))
                {
                    sensor.LastValue = newValue;
                    // EF Core ChangeTracker 已跟踪此实体，属性修改会被自动检测
                    updated = true;
                }
            }

            if (updated)
            {
                await _unitOfWork.SaveChangesAsync();
                Log.Debug("Updated LastValue for {Count} sensors: DeviceId={DeviceId}",
                    sensorList.Count(s => s.LastValue != null), deviceId);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to update sensor LastValues: DeviceId={DeviceId}", deviceId);
            // 不抛出异常——LastValue 更新失败不应阻断主采集流程
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

    #region ── EnergyTypes 差异化解析辅助方法 ──

    /// <summary>
    /// 解析 Device.EnergyTypes（JSON 数组或逗号分隔字符串）为类型列表
    /// 输入示例: ["electric","water"] 或 "electric,gas,water"
    /// </summary>
    private static List<string> ParseEnergyTypes(string? energyTypesJson)
    {
        if (string.IsNullOrWhiteSpace(energyTypesJson))
            return new List<string>();

        try
        {
            // 尝试解析为 JSON 数组
            using var doc = JsonDocument.Parse(energyTypesJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
                return doc.RootElement.EnumerateArray()
                    .Select(e => e.GetString())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .Select(s => s!.Trim().ToLowerInvariant())
                    .ToList()!;
        }
        catch (JsonException)
        {
            // 不是 JSON，尝试逗号分隔
        }

        // 降级为逗号分隔
        return energyTypesJson.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim().ToLowerInvariant())
            .Where(t => !string.IsNullOrEmpty(t))
            .ToList();
    }

    /// <summary>
    /// 根据 JSON key 反向查找映射到的 DeviceDataRecord 属性名
    /// 用于在 EnergyType 策略的 ValidRanges 中做范围校验
    /// </summary>
    private static bool TryGetMappedPropertyName(string jsonKey, [NotNullWhen(true)] out string? propertyName)
    {
        // JSON key → C# 属性名的反向映射表
        var keyToProperty = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["waterflow"]      = "WaterFlow",     ["water_flow"]     = "WaterFlow",
            ["watertotal"]     = "WaterTotal",    ["water_total"]    = "WaterTotal",
            ["electricpower"]  = "ElectricPower", ["electric_power"] = "ElectricPower",
            ["power"]          = "ElectricPower",
            ["electrickwh"]    = "ElectricKWh",   ["electric_kwh"]   = "ElectricKWh",
            ["kwh"]            = "ElectricKWh",
            ["gasflow"]        = "GasFlow",       ["gas_flow"]       = "GasFlow",
            ["gastotal"]       = "GasTotal",      ["gas_total"]      = "GasTotal",
            ["temperature"]    = "Temperature",   ["temp"]           = "Temperature",
            ["humidity"]       = "Humidity",      ["hum"]            = "Humidity",
            ["pm25"]           = "PM25",          ["pm2.5"]          = "PM25",
            ["pm10"]           = "PM10",
            ["co2"]            = "CO2",
            ["co"]             = "CO",
        };

        return keyToProperty.TryGetValue(jsonKey, out propertyName);
    }

    #endregion
}

/// <summary>
/// 能源类型解析策略
/// 定义每种能源设备的数据处理行为
/// </summary>
public class EnergyTypeStrategy
{
    /// <summary>显示名称，用于日志</summary>
    public string DisplayName { get; set; } = "未知";
    /// <summary>该类型关注的核心 DeviceDataRecord 属性名列表</summary>
    public List<string> KeyFields { get; set; } = new();
    /// <summary>该类型相关的 JSON key 列表（用于判断数据是否属于此类型）</summary>
    public List<string> RelevantJsonKeys { get; set; } = new();
    /// <summary>各属性的有效范围 (Min, Max)，超出范围标记 warning</summary>
    public Dictionary<string, (double Min, double Max)> ValidRanges { get; set; } = new();
    /// <summary>默认数据级别</summary>
    public string DefaultLevel { get; set; } = "info";
}
