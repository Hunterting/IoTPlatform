using IoTPlatform.Data;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Models;
using IoTPlatform.Services.Guards;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text.Json;

namespace IoTPlatform.Services;

/// <summary>
/// 统计分析服务实现
/// </summary>
public class AnalyticsService : IAnalyticsService
{
    private readonly AppDbContext _dbContext;

    public AnalyticsService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// 获取汇总统计
    /// </summary>
    public Task<AnalyticsSummaryDto> GetSummaryAsync(string? appCode)
        => GetSummaryAsync(appCode, null, null);

    /// <summary>B4/M5：admin 授权客户集合真正下推到三张统计源表。</summary>
    public async Task<AnalyticsSummaryDto> GetSummaryAsync(
        string? appCode, IReadOnlyCollection<long>? customerIds, IReadOnlyCollection<string>? appCodes = null)
    {
        var deviceQuery = _dbContext.Devices.AsQueryable();
        var alertQuery = _dbContext.AlertRecords.AsQueryable();
        var workOrderQuery = _dbContext.WorkOrders.AsQueryable();

        if (!string.IsNullOrEmpty(appCode))
        {
            deviceQuery = deviceQuery.Where(d => d.AppCode == appCode);
            alertQuery = alertQuery.Where(a => a.AppCode == appCode);
            workOrderQuery = workOrderQuery.Where(w => w.AppCode == appCode);
        }

        deviceQuery = deviceQuery.ApplyQueryScope(appCode, customerIds, appCodes);
        alertQuery = alertQuery.ApplyQueryScope(appCode, customerIds, appCodes);
        workOrderQuery = workOrderQuery.ApplyQueryScope(appCode, customerIds, appCodes);

        var totalDevices = await deviceQuery.CountAsync();
        var onlineDevices = await deviceQuery.CountAsync(d => d.Status == "online");
        var totalAlerts = await alertQuery.CountAsync();
        var pendingAlerts = await alertQuery.CountAsync(a => a.Status == "pending");
        var totalWorkOrders = await workOrderQuery.CountAsync();
        var pendingWorkOrders = await workOrderQuery.CountAsync(w => w.Status == "pending");

        return new AnalyticsSummaryDto
        {
            TotalDevices = totalDevices,
            OnlineDevices = onlineDevices,
            TotalAlerts = totalAlerts,
            PendingAlerts = pendingAlerts,
            TotalWorkOrders = totalWorkOrders,
            PendingWorkOrders = pendingWorkOrders
        };
    }

    /// <summary>
    /// 获取设备状态统计
    /// </summary>
    public async Task<List<DeviceStatusStatisticsDto>> GetDeviceStatusStatisticsAsync(string? appCode)
    {
        var query = _dbContext.Devices.AsQueryable();

        if (!string.IsNullOrEmpty(appCode))
        {
            query = query.Where(d => d.AppCode == appCode);
        }

        var statistics = await query
            .GroupBy(d => d.Status)
            .Select(g => new DeviceStatusStatisticsDto
            {
                Status = g.Key,
                Count = g.Count()
            })
            .ToListAsync();

        return statistics;
    }

    /// <summary>
    /// 获取告警趋势
    /// </summary>
    public async Task<List<AlertTrendDto>> GetAlertTrendAsync(DateTime startTime, DateTime endTime, string? appCode)
    {
        var query = _dbContext.AlertRecords
            .Where(a => a.AlertTime >= startTime && a.AlertTime <= endTime);

        if (!string.IsNullOrEmpty(appCode))
        {
            query = query.Where(a => a.AppCode == appCode);
        }

        var trend = await query
            .GroupBy(a => new { a.AlertTime.Date, a.Level })
            .Select(g => new AlertTrendDto
            {
                Date = g.Key.Date,
                Level = g.Key.Level,
                Count = g.Count()
            })
            .OrderBy(t => t.Date)
            .ToListAsync();

        return trend;
    }

    /// <summary>
    /// 获取能耗统计（基于设备真实上报采样值；无累计读数时不伪造累计电量，TotalConsumption 表示采样点累计功率之和 kW）
    /// </summary>
    public Task<EnergyConsumptionDto> GetEnergyConsumptionAsync(DateTime startTime, DateTime endTime, string? appCode)
        => GetEnergyConsumptionAsync(startTime, endTime, appCode, null, null);

    /// <summary>B4/M5：admin 授权客户集合下推到采样记录。</summary>
    public async Task<EnergyConsumptionDto> GetEnergyConsumptionAsync(
        DateTime startTime, DateTime endTime, string? appCode,
        IReadOnlyCollection<long>? customerIds, IReadOnlyCollection<string>? appCodes = null)
    {
        var query = _dbContext.DeviceDataRecords.AsNoTracking()
            .Where(r => r.Timestamp >= startTime && r.Timestamp <= endTime);
        query = query.ApplyQueryScope(appCode, customerIds, appCodes);

        var records = await query.Take(20000).ToListAsync();
        var breakdown = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        double total = 0;

        foreach (var r in records)
        {
            foreach (var type in new[] { "electric", "water", "gas" })
            {
                var v = ExtractEnergySample(r, type);
                if (v.HasValue)
                {
                    total += v.Value;
                    breakdown.TryGetValue(type, out var acc);
                    breakdown[type] = acc + v.Value;
                }
            }
        }

        return new EnergyConsumptionDto
        {
            TotalConsumption = Math.Round(total, 4),
            TotalCost = 0, // 无电价/水价/气价配置，不伪造费用
            Breakdown = breakdown.ToDictionary(kv => kv.Key, kv => Math.Round(kv.Value, 4))
        };
    }

    /// <summary>
    /// 智能分析页用能分析（真实优先）：按 energyType/选中年月聚合设备上报采样值。
    ///
    /// 数据来源优先级：
    ///  ① DeviceDataRecord 类型化列（ElectricPower/WaterFlow/GasFlow …）——「补上报」后新数据会写入；
    ///  ② 若类型化列为 null，则从 SensorData 原始 JSON 解析安圣 EMdata[].p / tasks[].power —— 兼容历史数据。
    /// 每个时间桶取采样【平均值】，口径是「平均功率/平均流量」，不是 kWh 累计电量。
    /// </summary>
    public Task<EnergyAnalysisDto> GetEnergyAnalysisAsync(
        string energyType, int year, int month, long? areaId, string? appCode)
        => GetEnergyAnalysisAsync(energyType, year, month, areaId, appCode, null, null);

    /// <summary>B4/M5：admin 授权客户集合下推到用能分析采样记录。</summary>
    public async Task<EnergyAnalysisDto> GetEnergyAnalysisAsync(
        string energyType, int year, int month, long? areaId, string? appCode,
        IReadOnlyCollection<long>? customerIds, IReadOnlyCollection<string>? appCodes = null)
    {
        var type = NormalizeEnergyType(energyType);
        var isElectric = type == "electric";
        var metric = isElectric ? "power" : "flow";
        var unit = isElectric ? "kW" : "m³/h";

        var dto = new EnergyAnalysisDto
        {
            EnergyType = type,
            Metric = metric,
            Unit = unit,
            Year = year,
            Month = month,
            AreaId = areaId,
            Source = isElectric
                ? "设备真实上报 · 平均功率(kW)"
                : "设备真实上报 · 平均流量(m³/h)",
            Summary = new EnergySummaryDto
            {
                TodayLabel = "当日平均",
                TotalLabel = "累计采样",
            },
        };

        // 查询范围：覆盖「最近 5 年」+「选中年整年」+「选中月」，一次拉取后在内存聚合。
        var minYear = Math.Min(year - 4, year);
        var rangeStart = new DateTime(minYear, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var rangeEnd = new DateTime(Math.Max(year, DateTime.UtcNow.Year), 12, 31, 23, 59, 59, DateTimeKind.Utc);

        var query = _dbContext.DeviceDataRecords.AsNoTracking()
            .Where(r => r.Timestamp >= rangeStart && r.Timestamp <= rangeEnd);
        query = query.ApplyQueryScope(appCode, customerIds, appCodes);
        if (areaId.HasValue)
        {
            query = query.Where(r => r.AreaId == areaId.Value);
        }

        var records = await query.Take(50000).ToListAsync();

        // (timestamp, value) 有效采样序列
        var samples = new List<(DateTime Time, double Value)>(records.Count);
        foreach (var r in records)
        {
            var v = ExtractEnergySample(r, type);
            if (v.HasValue) samples.Add((r.Timestamp, v.Value));
        }

        dto.SampleCount = samples.Count;
        dto.HasData = samples.Count > 0;

        // ① 日历视图：选中年月的每日平均
        int daysInMonth = DateTime.DaysInMonth(year, month);
        dto.Daily = Enumerable.Range(1, daysInMonth)
            .Select(day => AggregateBucket(samples, s => s.Time.Year == year && s.Time.Month == month && s.Time.Day == day, day.ToString(CultureInfo.InvariantCulture)))
            .ToList();

        // ② 月度柱状图：选中年份 1-12 月平均
        dto.Monthly = Enumerable.Range(1, 12)
            .Select(m => AggregateBucket(samples, s => s.Time.Year == year && s.Time.Month == m, $"{m}月"))
            .ToList();

        // ③ 年度折线图：最近 5 年
        dto.Yearly = Enumerable.Range(year - 4, 5)
            .Select(y => AggregateBucket(samples, s => s.Time.Year == y, $"{y}年"))
            .ToList();

        // 汇总：当日平均（真实 UTC 当天）/ 累计采样数
        var today = DateTime.UtcNow.Date;
        var todayBucket = AggregateBucket(samples, s => s.Time.Date == today, "today");
        dto.Summary.Today = Math.Round(todayBucket.Value, 4);
        dto.Summary.Total = samples.Count;

        return dto;
    }

    private static string NormalizeEnergyType(string? energyType)
    {
        return (energyType ?? "electric").Trim().ToLowerInvariant() switch
        {
            "water" => "water",
            "gas" => "gas",
            _ => "electric"
        };
    }

    private static EnergyPointDto AggregateBucket(
        List<(DateTime Time, double Value)> samples,
        Func<(DateTime Time, double Value), bool> predicate,
        string label)
    {
        double sum = 0;
        int count = 0;
        foreach (var s in samples)
        {
            if (!predicate(s)) continue;
            sum += s.Value;
            count++;
        }
        return new EnergyPointDto
        {
            Label = label,
            Value = count > 0 ? Math.Round(sum / count, 4) : 0,
            SampleCount = count
        };
    }

    /// <summary>
    /// 从一条 DeviceDataRecord 提取指定能源类型的采样值。
    /// 优先类型化列；为空则解析 SensorData（含安圣 EMdata/tasks 这类「JSON 字符串数组」）。
    /// 返回 null 表示该条记录没有此能源类型的真实采样，聚合时会被跳过（不补 0、不伪造）。
    /// </summary>
    private static double? ExtractEnergySample(DeviceDataRecord r, string energyType)
    {
        // ① 类型化列
        double? typed = energyType switch
        {
            "electric" => r.ElectricPower ?? r.ElectricKWh,
            "water" => r.WaterFlow ?? r.WaterTotal,
            "gas" => r.GasFlow ?? r.GasTotal,
            _ => null
        };
        if (typed.HasValue) return typed.Value;

        // ② SensorData 原始 JSON
        if (string.IsNullOrWhiteSpace(r.SensorData)) return null;
        try
        {
            using var doc = JsonDocument.Parse(r.SensorData);
            var root = doc.RootElement;

            if (energyType == "electric")
            {
                // 安圣 EMdata: "[{\"c\":..,\"p\":0.122,\"e\":0,\"v\":..}]" → 瞬时功率 p(kW)
                var em = FirstNestedNumber(root, "EMdata", new[] { "p", "power" });
                if (em.HasValue) return em.Value;
                // 安圣 tasks: "[{\"power\":\"0.161\",\"voltage\":..}]" → power(kW)
                var task = FirstNestedNumber(root, "tasks", new[] { "power", "p" });
                if (task.HasValue) return task.Value;
                // 通用顶层键
                return TopLevelNumber(root, "power", "electricPower", "electric_power", "total_power", "p");
            }

            if (energyType == "water")
            {
                var nested = FirstNestedNumber(root, "waterData", new[] { "flow", "value", "waterFlow" });
                if (nested.HasValue) return nested.Value;
                return TopLevelNumber(root, "waterFlow", "water_flow", "waterTotal", "water_total", "flow");
            }

            // gas
            var gasNested = FirstNestedNumber(root, "gasData", new[] { "flow", "value", "gasFlow" });
            if (gasNested.HasValue) return gasNested.Value;
            return TopLevelNumber(root, "gasFlow", "gas_flow", "gasTotal", "gas_total");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>读取顶层键的数值（number 或数字字符串）</summary>
    private static double? TopLevelNumber(JsonElement root, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!root.TryGetProperty(key, out var el)) continue;
            var v = ToDouble(el);
            if (v.HasValue) return v.Value;
        }
        return null;
    }

    /// <summary>
    /// 读取「值本身是 JSON 字符串数组」或「数组」属性里第一个对象的指定数值键。
    /// 例：SensorData.EMdata = "[{\"p\":0.122,...}]"（字符串）或 [...](数组)。
    /// </summary>
    private static double? FirstNestedNumber(JsonElement root, string propName, string[] valueKeys)
    {
        if (!root.TryGetProperty(propName, out var el)) return null;

        JsonElement arr;
        if (el.ValueKind == JsonValueKind.String)
        {
            var raw = el.GetString();
            if (string.IsNullOrWhiteSpace(raw)) return null;
            try
            {
                using var inner = JsonDocument.Parse(raw);
                arr = inner.RootElement.Clone();
            }
            catch (JsonException) { return null; }
        }
        else if (el.ValueKind == JsonValueKind.Array)
        {
            arr = el;
        }
        else if (el.ValueKind == JsonValueKind.Object)
        {
            arr = el; // 单对象也兼容
        }
        else
        {
            return null;
        }

        JsonElement target;
        if (arr.ValueKind == JsonValueKind.Array)
        {
            using var enumerator = arr.EnumerateArray();
            if (!enumerator.MoveNext()) return null;
            target = enumerator.Current;
        }
        else
        {
            target = arr;
        }

        if (target.ValueKind != JsonValueKind.Object) return ToDouble(target);
        foreach (var key in valueKeys)
        {
            if (!target.TryGetProperty(key, out var val)) continue;
            var v = ToDouble(val);
            if (v.HasValue) return v.Value;
        }
        return null;
    }

    private static double? ToDouble(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out var d) && double.IsFinite(d)) return d;
        if (el.ValueKind == JsonValueKind.String)
        {
            var s = el.GetString();
            if (!string.IsNullOrWhiteSpace(s) &&
                double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
                double.IsFinite(parsed)) return parsed;
        }
        return null;
    }
}
