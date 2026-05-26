using IoTPlatform.Services.Interfaces;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace IoTPlatform.Services;

/// <summary>
/// 时序数据保留策略服务（BackgroundService）
///
/// 职责：
/// - 定时执行数据保留策略，自动清理超期历史数据
/// - 支持多层级保留：明细数据短期保留 + 聚合数据长期保留
/// - 每个租户可配置独立的保留策略（未来扩展）
///
/// 执行模式：
/// - 应用启动后延迟首次执行（避免启动风暴）
/// - 之后按固定间隔循环执行
/// - 支持通过配置文件调整参数，无需重新编译
///
/// 配置项（appsettings.json → DataRetention 节）：
///   - Enabled: 是否启用
///   - DetailRetentionDays: 明细数据保留天数（默认 30 天）
///   - AggregationRetentionDays: 聚合数据保留天数（暂未实现聚合表，预留）
///   - CleanupIntervalHours: 清理执行间隔（默认 24 小时）
///   - BatchSize: 每批删除条数（默认 1000）
/// </summary>
public class DataRetentionHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DataRetentionHostedService> _logger;

    public DataRetentionHostedService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<DataRetentionHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var enabled = _configuration.GetValue<bool>("DataRetention:Enabled", true);
        if (!enabled)
        {
            _logger.LogInformation("数据保留策略服务已禁用（DataRetention:Enabled=false）");
            return;
        }

        // 首次执行延迟（应用启动后等待一段时间再开始清理）
        var initialDelayMinutes = _configuration.GetValue<int>("DataRetention:InitialDelayMinutes", 10);
        _logger.LogInformation("数据保留策略服务已启动，首次执行将在 {Delay} 分钟后进行", initialDelayMinutes);

        try
        {
            await Task.Delay(TimeSpan.FromMinutes(initialDelayMinutes), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return; // 服务正在关闭
        }

        // 主循环
        var intervalHours = _configuration.GetValue<int>("DataRetention:CleanupIntervalHours", 24);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ExecuteCleanupCycleAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "数据保留策略执行周期发生异常");
            }

            // 等待下一轮
            try
            {
                await Task.Delay(TimeSpan.FromHours(intervalHours), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("数据保留策略服务已停止");
    }

    /// <summary>
    /// 执行一轮完整的清理周期
    ///
    /// 流程：
    /// 1. 创建 Scope → 获取 ITimeSeriesStore（Scoped 服务）
    /// 2. 读取配置的保留天数和批大小
    /// 3. 调用 DeleteOlderThanAsync 分批删除过期数据
    /// 4. 记录统计日志
    /// </summary>
    private async Task ExecuteCleanupCycleAsync(CancellationToken ct)
    {
        _logger.LogInformation("━━━ 数据保留策略清理周期开始 ━━━");

        using var scope = _scopeFactory.CreateScope();
        var timeSeriesStore = scope.ServiceProvider.GetRequiredService<ITimeSeriesStore>();

        var retentionDays = _configuration.GetValue<int>("DataRetention:DetailRetentionDays", 30);
        var batchSize = _configuration.GetValue<int>("DataRetention:BatchSize", 1000);

        // 可选：按租户分别清理（当前实现全局清理）
        var perTenant = _configuration.GetValue<bool>("DataRetention:PerTenantCleanup", false);

        var sw = System.Diagnostics.Stopwatch.StartNew();

        int deletedCount;

        if (perTenant)
        {
            // 按租户逐个清理（需要查询所有 AppCode 列表）
            deletedCount = 0;
            _logger.LogWarning("按租户独立清理模式暂未完全实现，回退到全局清理");
            deletedCount = await timeSeriesStore.DeleteOlderThanAsync(retentionDays, batchSize: batchSize);
        }
        else
        {
            // 全局清理（所有租户一起处理）
            deletedCount = await timeSeriesStore.DeleteOlderThanAsync(retentionDays, batchSize: batchSize);
        }

        sw.Stop();

        // 输出本轮清理报告
        if (deletedCount > 0)
        {
            Log.Warning(
                "数据保留清理报告: Deleted={Deleted} 条, Retention={Days}天, " +
                "BatchSize={Batch}, Elapsed={ElapsedMs}ms",
                deletedCount, retentionDays, batchSize, sw.ElapsedMilliseconds);
        }
        else
        {
            _logger.LogInformation(
                "数据保留清理报告: 无需删除的数据 | Retention={Days}天, Elapsed={ElapsedMs}ms",
                retentionDays, sw.ElapsedMilliseconds);
        }

        // 输出当前存储统计（辅助运维）
        try
        {
            var stats = await timeSeriesStore.GetStatisticsAsync();
            _logger.LogInformation(
                "时序存储状态: TotalRecords={Total}, DeviceCount={Devices}, " +
                "Range=[{Earliest} ~ {Latest}]",
                stats.TotalRecords, stats.DeviceCount,
                stats.EarliestTimestamp?.ToString("yyyy-MM-dd HH:mm") ?? "N/A",
                stats.LatestTimestamp?.ToString("yyyy-MM-dd HH:mm:ss") ?? "N/A");
        }
        catch (Exception statsEx)
        {
            _logger.LogWarning(statsEx, "获取时序存储统计信息失败（非致命）");
        }

        _logger.LogInformation("━━━ 数据保留策略清理周期结束 ━━━");
    }
}

#region ── 配置模型（可选强类型绑定） ──

/// <summary>
/// 数据保留策略配置选项
/// 可用于 Options Pattern 强类型绑定
/// </summary>
public class DataRetentionOptions
{
    /// <summary>是否启用（默认 true）</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>明细数据保留天数（默认 30 天）</summary>
    public int DetailRetentionDays { get; set; } = 30;

    /// <summary>聚合数据保留天数（预留，默认 365 天）</summary>
    public int AggregationRetentionDays { get; set; } = 365;

    /// <summary>清理执行间隔小时数（默认 24 小时）</summary>
    public int CleanupIntervalHours { get; set; } = 24;

    /// <summary>首次执行延迟分钟数（默认 10 分钟）</summary>
    public int InitialDelayMinutes { get; set; } = 10;

    /// <summary>每批删除条数（默认 1000）</summary>
    public int BatchSize { get; set; } = 1000;

    /// <summary>是否按租户分别清理（默认 false）</summary>
    public bool PerTenantCleanup { get; set; } = false;
}

#endregion
