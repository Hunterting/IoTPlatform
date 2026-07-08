using IoTPlatform.Data;
using IoTPlatform.Infrastructure.Protocol;
using IoTPlatform.Infrastructure.Protocol.AnSheng;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;

namespace IoTPlatform.Services;

/// <summary>
/// 安圣设备发现后台服务
///
/// 职责：
///   1. 定时扫描 DiscoveredAnShengDevice 表，对未认领设备发送 getDevInfo 获取基础信息
///   2. 监听适配器 Will 事件，更新设备离线状态
///   3. 在收到设备数据时（ProtocolConfigService 桥接通道），更新 LastSeenAt 在线时间
///
/// 调度：
///   - 默认每 5 分钟扫描一次未认领设备
///   - 启动后延迟 30 秒开始首次扫描（等待适配器就绪）
/// </summary>
public class AnShengDiscoveryService : BackgroundService, IAnShengDiscoveryService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IProtocolAdapterFactory _adapterFactory;
    private readonly ILogger<AnShengDiscoveryService> _logger;

    /// <summary>线程安全的在线状态缓存（IMEI → 最后在线时间），避免频繁查库</summary>
    private readonly ConcurrentDictionary<string, DateTime> _onlineStatus = new();

    /// <summary>
    /// 扫描间隔（默认 5 分钟）
    /// </summary>
    private static readonly TimeSpan ScanInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 离线判定的时间阈值：超过此时间未收到数据视为离线
    /// </summary>
    private static readonly TimeSpan OfflineThreshold = TimeSpan.FromMinutes(10);

    public AnShengDiscoveryService(
        IServiceScopeFactory scopeFactory,
        IProtocolAdapterFactory adapterFactory,
        ILogger<AnShengDiscoveryService> logger)
    {
        _scopeFactory = scopeFactory;
        _adapterFactory = adapterFactory;
        _logger = logger;
    }

    // ─────────────────────────────────────────────
    // BackgroundService 核心循环
    // ─────────────────────────────────────────────

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("安圣设备发现服务启动，扫描间隔={Interval}min, 离线阈值={Offline}min",
            ScanInterval.TotalMinutes, OfflineThreshold.TotalMinutes);

        // 订阅适配器 Will 事件
        Infrastructure.Protocol.Adapters.AnShengMqttProtocolAdapter.DeviceWill += OnAdapterDeviceWill;

        // 初始延迟，等待适配器就绪
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ScanUnclaimedDevicesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "安圣设备发现扫描异常（已隔离）");
            }

            await Task.Delay(ScanInterval, stoppingToken);
        }

        // 反订阅
        Infrastructure.Protocol.Adapters.AnShengMqttProtocolAdapter.DeviceWill -= OnAdapterDeviceWill;

        _logger.LogInformation("安圣设备发现服务已停止");
    }

    /// <summary>
    /// 适配器 Will 事件回调（桥接到 OnDeviceOfflineAsync）
    /// </summary>
    private void OnAdapterDeviceWill(object? sender, Infrastructure.Protocol.AnSheng.AnShengWillEventArgs e)
    {
        // fire-and-forget: 不阻塞适配器的消息处理线程
        _ = Task.Run(async () =>
        {
            try
            {
                await OnDeviceOfflineAsync(e.Imei, e.AppCode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Will 事件处理异常: IMEI={IMEI}", e.Imei);
            }
        });
    }

    // ─────────────────────────────────────────────
    // 定时扫描：向未认领设备发送 getDevInfo
    // ─────────────────────────────────────────────

    /// <summary>
    /// 扫描未认领设备，通过适配器发送 getDevInfo 命令
    /// </summary>
    private async Task ScanUnclaimedDevicesAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // 查询所有未认领且未超时的设备
        var unclaimedDevices = await db.Set<DiscoveredAnShengDevice>()
            .AsNoTracking()
            .Where(d => !d.IsClaimed)
            .OrderBy(d => d.DiscoveredAt)
            .Take(20) // 每轮最多 20 个，防止并发过高
            .ToListAsync(ct);

        if (unclaimedDevices.Count == 0)
        {
            _logger.LogDebug("安圣设备发现扫描：无未认领设备");
            return;
        }

        _logger.LogDebug("安圣设备发现扫描：发现 {Count} 个未认领设备", unclaimedDevices.Count);

        // 按 AppCode 分组，找到对应的适配器
        var activeConfigs = await db.Set<ProtocolConfig>()
            .AsNoTracking()
            .Where(c => c.IsActive && c.ProtocolType == "ANSHENG_MQTT")
            .ToListAsync(ct);

        if (activeConfigs.Count == 0)
        {
            _logger.LogDebug("安圣设备发现扫描：无活跃的 ANSHENG_MQTT 协议配置");
            return;
        }

        foreach (var device in unclaimedDevices)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                // 找到匹配的协议配置
                var config = activeConfigs.FirstOrDefault(c =>
                    string.IsNullOrEmpty(device.AppCode) ||
                    string.Equals(c.AppCode, device.AppCode, StringComparison.OrdinalIgnoreCase));

                if (config == null)
                {
                    _logger.LogDebug("未找到匹配的协议配置: IMEI={IMEI}, AppCode={AppCode}",
                        device.Imei, device.AppCode);
                    continue;
                }

                // 获取适配器
                var adapter = _adapterFactory.GetAdapter((int)config.Id);
                if (adapter == null || !adapter.IsConnected)
                {
                    _logger.LogDebug("适配器不可用: ConfigId={ConfigId}", config.Id);
                    continue;
                }

                // 通过适配器直接发送 getDevInfo（不经过 CommandService，因为设备未认领无 DeviceId）
                var frameId = await adapter.SendCommandAsync(
                    deviceId: 0L,
                    serialNumber: device.Imei,
                    commandType: "getDevInfo",
                    parameters: string.Empty,
                    cancellationToken: ct);

                _logger.LogInformation(
                    "已向未认领设备发送 getDevInfo: IMEI={IMEI}, FrameId={FrameId}",
                    device.Imei, frameId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "向未认领设备发送 getDevInfo 失败: IMEI={IMEI}", device.Imei);
            }
        }
    }

    // ─────────────────────────────────────────────
    // Will 离线处理
    // ─────────────────────────────────────────────

    /// <inheritdoc />
    public async Task OnDeviceOfflineAsync(string imei, string? appCode, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(imei)) return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            _logger.LogInformation("安圣设备 Will 离线: IMEI={IMEI}, AppCode={AppCode}", imei, appCode);

            // 更新 discovered device
            var discovered = await db.Set<DiscoveredAnShengDevice>()
                .FirstOrDefaultAsync(d => d.Imei == imei && d.AppCode == appCode, ct);

            if (discovered != null)
            {
                discovered.LastSeenAt = null; // 置空表示离线
                await db.SaveChangesAsync(ct);
            }

            // 更新已认领设备状态
            var claimedDevice = await db.Devices
                .FirstOrDefaultAsync(d => d.SerialNumber == imei, ct);

            if (claimedDevice != null)
            {
                claimedDevice.Status = "offline";
                claimedDevice.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                _logger.LogInformation("已更新设备离线状态: DeviceId={DeviceId}, IMEI={IMEI}",
                    claimedDevice.Id, imei);
            }

            // 清理内存缓存
            _onlineStatus.TryRemove(imei, out _);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理 Will 离线通知异常: IMEI={IMEI}", imei);
        }
    }

    // ─────────────────────────────────────────────
    // 上线/数据接收处理
    // ─────────────────────────────────────────────

    /// <inheritdoc />
    public async Task OnDeviceOnlineAsync(string imei, string? model, string? netType, string? appCode,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(imei)) return;

        try
        {
            var now = DateTime.UtcNow;

            // 内存缓存更新（轻量级，避免频繁查库）
            _onlineStatus[imei] = now;

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // 查询或创建 discovered device
            var discovered = await db.Set<DiscoveredAnShengDevice>()
                .FirstOrDefaultAsync(d => d.Imei == imei, ct);

            if (discovered == null)
            {
                // 首次发现：插入待认领池
                discovered = new DiscoveredAnShengDevice
                {
                    Imei = imei,
                    AppCode = appCode,
                    Model = model,
                    NetType = netType,
                    DiscoveredAt = now,
                    LastSeenAt = now,
                    IsClaimed = false
                };
                db.Set<DiscoveredAnShengDevice>().Add(discovered);

                _logger.LogInformation(
                    "发现新安圣设备: IMEI={IMEI}, Model={Model}, NetType={NetType}, AppCode={AppCode}",
                    imei, model, netType, appCode);
            }
            else
            {
                // 更新已有记录
                discovered.LastSeenAt = now;
                if (!string.IsNullOrEmpty(model) && string.IsNullOrEmpty(discovered.Model))
                    discovered.Model = model;
                if (!string.IsNullOrEmpty(netType) && string.IsNullOrEmpty(discovered.NetType))
                    discovered.NetType = netType;
                if (!string.IsNullOrEmpty(appCode) && string.IsNullOrEmpty(discovered.AppCode))
                    discovered.AppCode = appCode;
            }

            await db.SaveChangesAsync(ct);

            // 如果已认领，同步更新设备在线状态
            if (discovered.IsClaimed && discovered.ClaimedDeviceId != null)
            {
                var device = await db.Devices
                    .FirstOrDefaultAsync(d => d.Id == discovered.ClaimedDeviceId, ct);

                if (device != null && device.Status != "online")
                {
                    device.Status = "online";
                    device.UpdatedAt = now;
                    await db.SaveChangesAsync(ct);

                    _logger.LogDebug("已更新设备在线状态: DeviceId={DeviceId}, IMEI={IMEI}",
                        device.Id, imei);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理设备上线通知异常: IMEI={IMEI}", imei);
        }
    }

    // ─────────────────────────────────────────────
    // 辅助
    // ─────────────────────────────────────────────

    /// <summary>
    /// 获取设备在线状态（从内存缓存快速查询）
    /// </summary>
    public bool IsOnline(string imei, out DateTime? lastSeenAt)
    {
        if (_onlineStatus.TryGetValue(imei, out var last))
        {
            lastSeenAt = last;
            return DateTime.UtcNow - last < OfflineThreshold;
        }
        lastSeenAt = null;
        return false;
    }
}
