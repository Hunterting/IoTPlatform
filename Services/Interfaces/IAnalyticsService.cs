using IoTPlatform.DTOs.Responses;

namespace IoTPlatform.Services;

/// <summary>
/// 统计分析服务接口
/// </summary>
public interface IAnalyticsService
{
    /// <summary>旧签名保留：内部/下级路径零回归。</summary>
    Task<AnalyticsSummaryDto> GetSummaryAsync(string? appCode);

    /// <summary>B4/M5：Multi 授权客户集合下推。</summary>
    Task<AnalyticsSummaryDto> GetSummaryAsync(string? appCode, IReadOnlyCollection<long>? customerIds, IReadOnlyCollection<string>? appCodes = null) => GetSummaryAsync(appCode);
    Task<List<DeviceStatusStatisticsDto>> GetDeviceStatusStatisticsAsync(string? appCode);
    Task<List<AlertTrendDto>> GetAlertTrendAsync(DateTime startTime, DateTime endTime, string? appCode);
    /// <summary>旧签名保留：内部/下级路径零回归。</summary>
    Task<EnergyConsumptionDto> GetEnergyConsumptionAsync(DateTime startTime, DateTime endTime, string? appCode);

    /// <summary>B4/M5：Multi 授权客户集合下推。</summary>
    Task<EnergyConsumptionDto> GetEnergyConsumptionAsync(DateTime startTime, DateTime endTime, string? appCode, IReadOnlyCollection<long>? customerIds, IReadOnlyCollection<string>? appCodes = null) => GetEnergyConsumptionAsync(startTime, endTime, appCode);

    /// <summary>
    /// 智能分析页用能分析：按能源类型 + 选中年月，聚合设备真实上报的瞬时功率/流量采样值。
    /// 一次返回卡片汇总 + 日历(日)/月柱(月)/年线(年)三组数据。
    /// 口径为「采样平均值」，不伪造累计电量；无采样时返回 HasData=false 与全 0 点位。
    /// </summary>
    /// <param name="energyType">electric / water / gas</param>
    /// <param name="year">选中年份</param>
    /// <param name="month">选中月份（1-12）</param>
    /// <param name="areaId">可选区域过滤</param>
    /// <param name="appCode">租户隔离（JWT AppCode；超管为 null 时不额外过滤）</param>
    /// <summary>旧签名保留：内部/下级路径零回归。</summary>
    Task<EnergyAnalysisDto> GetEnergyAnalysisAsync(string energyType, int year, int month, long? areaId, string? appCode);

    /// <summary>B4/M5：Multi 授权客户集合下推。</summary>
    Task<EnergyAnalysisDto> GetEnergyAnalysisAsync(string energyType, int year, int month, long? areaId, string? appCode, IReadOnlyCollection<long>? customerIds, IReadOnlyCollection<string>? appCodes = null) => GetEnergyAnalysisAsync(energyType, year, month, areaId, appCode);
}
