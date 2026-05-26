using IoTPlatform.Models;

namespace IoTPlatform.Services.Interfaces;

/// <summary>
/// 时序数据存储抽象接口
///
/// 设计目的：
/// - 将时序数据的写入/查询/聚合/过期清理操作标准化
/// - 当前基于 MySQL (DeviceDataRecord 表) 实现
/// - 预留扩展点：将来可无缝切换到 InfluxDB / TimescaleDB / QuestDB 等专业时序数据库
///
/// 使用场景：
/// - 数据采集模块的查询/聚合需求（图表、报表、趋势分析）
/// - 数据保留策略的自动清理
/// - 多维度时序数据分析
/// </summary>
public interface ITimeSeriesStore
{
    /// <summary>
    /// 写入一条设备数据记录
    /// </summary>
    /// <param name="record">数据记录实体</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task WriteAsync(DeviceDataRecord record, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量写入数据记录
    /// </summary>
    /// <param name="records">数据记录集合</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task WriteBatchAsync(IEnumerable<DeviceDataRecord> records, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按时间范围查询设备数据（分页）
    /// </summary>
    /// <param name="deviceId">设备ID</param>
    /// <param name="appCode">租户代码</param>
    /// <param name="from">起始时间</param>
    /// <param name="to">截止时间</param>
    /// <param name="page">页码（从1开始）</param>
    /// <param name="pageSize">每页条数</param>
    /// <returns>分页数据记录列表</returns>
    Task<(List<DeviceDataRecord> Items, int TotalCount)> QueryRangeAsync(
        long deviceId, string? appCode, DateTime from, DateTime to,
        int page = 1, int pageSize = 100);

    /// <summary>
    /// 获取设备的最新一条数据记录
    /// </summary>
    Task<DeviceDataRecord?> GetLatestAsync(long deviceId, string? appCode);

    /// <summary>
    /// 获取多台设备的最新数据（批量，用于仪表盘/实时监控）
    /// </summary>
    /// <param name="deviceIds">设备ID列表</param>
    /// <param name="appCode">租户代码</param>
    /// <returns>deviceId → 最新记录 的映射</returns>
    Task<Dictionary<long, DeviceDataRecord>> GetLatestBatchAsync(IEnumerable<long> deviceIds, string? appCode);

    /// <summary>
    /// 时序数据聚合（按时间窗口降采样）
    ///
    /// 支持 Avg / Max / Min / Sum / Count 五种聚合函数，
    /// 可指定参与聚合的字段和窗口大小。
    /// </summary>
    /// <param name="deviceId">设备ID</param>
    /// <param name="appCode">租户代码</param>
    /// <param name="fields">要聚合的物理量字段名（如 Temperature, ElectricPower）</param>
    /// <param name="from">起始时间</param>
    /// <param name="to">截止时间</param>
    /// <param name="intervalSeconds">聚合窗口大小（秒），如 300=5分钟, 3600=1小时</param>
    /// <param name="aggType">聚合类型: avg, max, min, sum, count</param>
    /// <returns>聚合结果：每个时间窗口各字段的聚合值</returns>
    Task<List<TimeSeriesAggregateResult>> AggregateAsync(
        long deviceId, string? appCode,
        IEnumerable<string> fields,
        DateTime from, DateTime to,
        int intervalSeconds,
        string aggType = "avg");

    /// <summary>
    /// 删除超过保留期的历史数据
    /// </summary>
    /// <param name="retentionDays">保留天数（如 30 = 保留最近30天，更早的删除）</param>
    /// <param name="appCode">租户代码（可选，不传则清理所有租户）</param>
    /// <param name="batchSize">每批删除数量（防止长事务锁表）</param>
    /// <returns>删除的总记录数</returns>
    Task<int> DeleteOlderThanAsync(int retentionDays, string? appCode = null, int batchSize = 1000);

    /// <summary>
    /// 获取数据统计信息（用于监控和运维）
    /// </summary>
    /// <param name="appCode">租户代码</param>
    Task<TimeSeriesStatistics> GetStatisticsAsync(string? appCode = null);
}

/// <summary>
/// 时序聚合结果（单条记录代表一个时间窗口）
/// </summary>
public class TimeSeriesAggregateResult
{
    /// <summary>时间窗口起始时间</summary>
    public DateTime WindowStart { get; set; }
    /// <summary>时间窗口结束时间</summary>
    public DateTime WindowEnd { get; set; }
    /// <summary>本窗口内的记录数</summary>
    public int Count { get; set; }
    /// <summary>各字段聚合值（字段名 → 聚合值）</summary>
    public Dictionary<string, double?> FieldValues { get; set; } = new();
}

/// <summary>
/// 时序存储统计信息
/// </summary>
public class TimeSeriesStatistics
{
    /// <summary>总记录数</summary>
    public long TotalRecords { get; set; }
    /// <summary>最早记录时间</summary>
    public DateTime? EarliestTimestamp { get; set; }
    /// <summary>最新记录时间</summary>
    public DateTime? LatestTimestamp { get; set; }
    /// <summary>涉及设备数</summary>
    public int DeviceCount { get; set; }
    /// <summary>按设备分布（设备ID → 记录数）</summary>
    public Dictionary<long, long> RecordsPerDevice { get; set; } = new();
}
