namespace IoTPlatform.DTOs.Responses;

/// <summary>
/// 汇总统计DTO
/// </summary>
public class AnalyticsSummaryDto
{
    public int TotalDevices { get; set; }
    public int OnlineDevices { get; set; }
    public int TotalAlerts { get; set; }
    public int PendingAlerts { get; set; }
    public int TotalWorkOrders { get; set; }
    public int PendingWorkOrders { get; set; }
}

/// <summary>
/// 设备状态统计DTO
/// </summary>
public class DeviceStatusStatisticsDto
{
    public string Status { get; set; } = string.Empty;
    public int Count { get; set; }
}

/// <summary>
/// 告警趋势DTO
/// </summary>
public class AlertTrendDto
{
    public DateTime Date { get; set; }
    public string Level { get; set; } = string.Empty;
    public int Count { get; set; }
}

/// <summary>
/// 能耗统计DTO
/// </summary>
public class EnergyConsumptionDto
{
    public double TotalConsumption { get; set; }
    public double TotalCost { get; set; }
    public Dictionary<string, double> Breakdown { get; set; } = new();
}

// ─────────────────────────────────────────────────────────────────────────────
// 智能分析页「水 / 电 / 气」用能分析 DTO
//
// 设计口径（真实优先，不伪造累计电量）：
//  - 设备上报的是「瞬时功率 / 流量」采样值（安圣 EMdata.p ≈ kW、tasks.power ≈ kW），
//    DeviceDataRecord 没有可信的累计电量读数（ElectricKWh 历史全为 null，EMdata.e 恒为 0）。
//  - 因此每个时间桶的聚合值 = 该桶内采样值的【平均值】，口径为「平均功率/平均流量」，
//    单位 kW（电）/ m³·h⁻¹（水、气），而非 kWh 累计用量。
//  - summary.Total = 累计采样点数（真实计数），不冒充累计电量。
//  - 无任何采样时 HasData=false，全部点位为 0，由前端显示空态。
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>单个时间桶的聚合点</summary>
public class EnergyPointDto
{
    /// <summary>桶标签（日："1".."31"；月："1月".."12月"；年："2026年"）</summary>
    public string Label { get; set; } = string.Empty;
    /// <summary>该桶内采样值平均值（0 表示无数据）</summary>
    public double Value { get; set; }
    /// <summary>该桶内有效采样点数</summary>
    public int SampleCount { get; set; }
}

/// <summary>用能卡片汇总</summary>
public class EnergySummaryDto
{
    /// <summary>当日采样平均值（无当日数据则为 0）</summary>
    public double Today { get; set; }
    /// <summary>累计采样点数（真实计数，非累计电量）</summary>
    public int Total { get; set; }
    /// <summary>当前口径标签，例如「当日平均」</summary>
    public string TodayLabel { get; set; } = "当日平均";
    /// <summary>累计口径标签，例如「累计采样」</summary>
    public string TotalLabel { get; set; } = "累计采样";
}

/// <summary>用能分析完整响应（一次请求喂饱卡片 + 日历 + 月柱 + 年线）</summary>
public class EnergyAnalysisDto
{
    /// <summary>能源类型：electric / water / gas</summary>
    public string EnergyType { get; set; } = "electric";
    /// <summary>度量口径：power（平均功率）/ flow（平均流量）</summary>
    public string Metric { get; set; } = "power";
    /// <summary>单位：kW / m³/h</summary>
    public string Unit { get; set; } = "kW";
    public int Year { get; set; }
    public int Month { get; set; }
    public long? AreaId { get; set; }
    /// <summary>是否存在真实采样数据</summary>
    public bool HasData { get; set; }
    /// <summary>全范围有效采样点数</summary>
    public int SampleCount { get; set; }
    /// <summary>数据来源说明（便于界面如实标注）</summary>
    public string Source { get; set; } = "设备真实上报";
    public EnergySummaryDto Summary { get; set; } = new();
    /// <summary>日历视图：选中年月的每日聚合</summary>
    public List<EnergyPointDto> Daily { get; set; } = new();
    /// <summary>月度柱状图：选中年份 1-12 月聚合</summary>
    public List<EnergyPointDto> Monthly { get; set; } = new();
    /// <summary>年度折线图：最近 5 年聚合</summary>
    public List<EnergyPointDto> Yearly { get; set; } = new();
}
