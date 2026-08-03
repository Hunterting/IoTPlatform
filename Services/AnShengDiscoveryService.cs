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
    /// <summary>认领相关日志的统一前缀，便于现场 grep（见设计 §8.4）。</summary>
    private const string ClaimLogTag = "[AnShengClaim]";

    /// <summary>安圣协议类型标识，与 <c>ProtocolConfig.ProtocolType</c> 取值一致。</summary>
    private const string AnShengProtocolType = "ANSHENG_MQTT";

    /// <summary>AppCode 缺失时的兜底租户码，与既有 <c>ClaimDevice</c> 行为保持一致。</summary>
    private const string FallbackAppCode = "system";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IProtocolAdapterFactory _adapterFactory;
    private readonly IAnShengProbeService _probeService;
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

    /// <summary>
    /// 构造函数。
    /// </summary>
    /// <param name="scopeFactory">作用域工厂。本服务是 Singleton，取 <c>AppDbContext</c> 一律经此工厂开作用域。</param>
    /// <param name="adapterFactory">协议适配器工厂。</param>
    /// <param name="probeService">同步探测服务（Singleton，可直接构造注入）。</param>
    /// <param name="logger">日志器。</param>
    public AnShengDiscoveryService(
        IServiceScopeFactory scopeFactory,
        IProtocolAdapterFactory adapterFactory,
        IAnShengProbeService probeService,
        ILogger<AnShengDiscoveryService> logger)
    {
        _scopeFactory = scopeFactory;
        _adapterFactory = adapterFactory;
        _probeService = probeService;
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

            // 登记设备品类：决定后续下发是否注入秒级 timestamp（仅 4G 款注入）
            Infrastructure.Protocol.Adapters.AnShengMqttProtocolAdapter.RegisterDeviceKind(
                imei,
                Infrastructure.Protocol.AnSheng.AnShengDeviceKindResolver.Resolve(netType, null, model));

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
    // 认领编排（设计 §3.8：★ 顺序即验收，不得调整）
    // ─────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<AnShengClaimResult> ClaimAsync(
        AnShengClaimCommand command,
        CancellationToken ct = default)
    {
        if (command == null)
        {
            return AnShengClaimResult.Fail(
                AnShengClaimErrorCodes.KindRequired, "认领指令不能为空。");
        }

        // ── 步骤 1：品类必填校验 ──
        // 放在最前面：品类决定 Category、决定后续指令目录，拿不到就没有继续的意义，
        // 更不该为此白白占用一次 5s 的设备探测。
        if (command.Kind == AnShengDeviceKind.Unknown)
        {
            return AnShengClaimResult.Fail(
                AnShengClaimErrorCodes.KindRequired, "必须显式指定设备品类 Kind。");
        }

        if (command.DiscoveredDeviceId is null && string.IsNullOrWhiteSpace(command.Imei))
        {
            return AnShengClaimResult.Fail(
                AnShengClaimErrorCodes.DiscoveredNotFound,
                "DiscoveredDeviceId 与 Imei 必须提供其一。");
        }

        // 本服务是 Singleton，DbContext 只能经作用域取得（设计 §8.4）。
        // 该作用域要横跨「探测前置查询 → 探测 → 落库事务」，故不能用 using 之外的短生命周期。
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var profileService = scope.ServiceProvider.GetRequiredService<IAnShengDeviceProfileService>();

        // ── 步骤 2：定位待认领记录 ──
        var discovered = await FindDiscoveredAsync(db, command, ct);
        if (discovered == null)
        {
            _logger.LogWarning("{Tag} 待认领记录不存在: Id={Id}, IMEI={Imei}",
                ClaimLogTag, command.DiscoveredDeviceId, command.Imei);
            return AnShengClaimResult.Fail(
                AnShengClaimErrorCodes.DiscoveredNotFound, "待认领设备不存在。");
        }

        // ── 步骤 3：已认领拦截 ──
        if (discovered.IsClaimed)
        {
            return AnShengClaimResult.Fail(
                AnShengClaimErrorCodes.AlreadyClaimed,
                $"设备 {discovered.Imei} 已被认领（DeviceId={discovered.ClaimedDeviceId}）。");
        }

        var imei = discovered.Imei;
        var appCode = FirstNonEmpty(command.AppCode, discovered.AppCode) ?? FallbackAppCode;

        // ── 步骤 4：IMEI 冲突拦截 ──
        // 与步骤 3 分开：discovered 没标记已认领、但 devices 表里已经有同 IMEI 行，
        // 说明数据被旁路写入过。此时仍按「已被认领」对外表达，语义对用户最直观。
        var conflictDevice = await db.Devices
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.SerialNumber == imei, ct);

        if (conflictDevice != null)
        {
            return AnShengClaimResult.Fail(
                AnShengClaimErrorCodes.AlreadyClaimed,
                $"IMEI {imei} 已存在对应设备（DeviceId={conflictDevice.Id}）。");
        }

        // ── 步骤 5：解析协议配置 ──
        var protocolConfigId = await ResolveProtocolConfigIdAsync(db, command, discovered, ct);
        if (protocolConfigId == null)
        {
            return AnShengClaimResult.Fail(
                AnShengClaimErrorCodes.AdapterUnavailable,
                $"未找到可用于 IMEI {imei} 的活跃 {AnShengProtocolType} 协议配置。");
        }

        // ── 步骤 6：置 Probing（可观测性）──
        // 非强制步骤，但探测要等 5~10s，期间列表若还显示 NotProbed，
        // 现场会误以为请求丢了而反复重试。
        discovered.ProbeStatus = AnShengProbeStatus.Probing;
        discovered.ProbeError = null;
        await db.SaveChangesAsync(ct);

        // ── 步骤 7：强制探测（★ 事务之外）──
        // 绝不能把这段等待圈进事务：5~10s 的行锁 + 连接占用，
        // 在几十路并发认领下足以打爆连接池（设计 §8.4 事务边界）。
        _logger.LogInformation("{Tag} 开始探测: IMEI={Imei}, ConfigId={ConfigId}",
            ClaimLogTag, imei, protocolConfigId);

        var probe = await _probeService.ProbeAsync((int)protocolConfigId.Value, imei, ct);

        // ── 步骤 8：写档案（成功写快照，失败只记状态）──
        AnShengDeviceProfile profile;
        if (probe.Success)
        {
            var snapshot = BuildSnapshot(probe);
            profile = await profileService.ApplyProbeAsync(
                imei, appCode, snapshot, command.Kind, ct);
        }
        else
        {
            profile = await profileService.ApplyProbeFailureAsync(imei, appCode, probe.Error, ct);
        }

        // ── 步骤 9：回写 discovered 的能力字段 ──
        ApplyProbeToDiscovered(discovered, profile, probe);
        await db.SaveChangesAsync(ct);

        // ── 步骤 10：探测失败即返回（★ Device 行绝不创建，验收 #4）──
        if (!probe.Success)
        {
            _logger.LogWarning("{Tag} 探测失败，终止认领: IMEI={Imei}, Error={Error}",
                ClaimLogTag, imei, probe.Error);

            return AnShengClaimResult.Fail(
                AnShengClaimErrorCodes.ProbeFailed,
                probe.Error ?? $"设备 {imei} 探测失败。",
                AnShengProbeStatus.ProbeFailed);
        }

        // ── 步骤 11：事务内完成四步写入 ──
        // 【与重试执行策略共存】DbContext 启用了 EnableRetryOnFailure（见 Program.cs），
        // EF Core 要求用户发起的事务必须包在 Database.CreateExecutionStrategy() 返回的
        // 「重试单元」内，否则 BeginTransactionAsync 会抛
        // “MySqlRetryingExecutionStrategy does not support user-initiated transactions”。
        // 这里用策略包裹整段事务：由策略负责在瞬时故障时整体重试。
        Device? device = null;
        AnShengDeviceConfig? autoReportConfig = null;

        try
        {
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async (token) =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                try
                {
                    // ① 建 Device —— Category 由品类派生，不再写死任何产品名（决策 Q8）
                    device = new Device
                    {
                        Name = command.Name,
                        SerialNumber = imei,
                        AppCode = appCode,
                        Status = "online",
                        Category = command.Kind.ToDisplayName(),
                        ProtocolConfigId = protocolConfigId,
                        AreaId = command.AreaId,
                        ProjectId = command.ProjectId,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    db.Devices.Add(device);
                    await db.SaveChangesAsync(ct);

                    // ② 回写待认领池
                    discovered.IsClaimed = true;
                    discovered.ClaimedDeviceId = device.Id;

                    // ③ 挂档案
                    profileService.AttachDevice(profile, device.Id);

                    // ④ 建自动上报配置（沿用既有语义：未显式关闭即开启）
                    if (command.GetDevStatusSec is > 0 or null)
                    {
                        autoReportConfig = new AnShengDeviceConfig
                        {
                            DeviceId = device.Id,
                            AppCode = device.AppCode,
                            Imei = imei,
                            GetDevStatusSec = command.GetDevStatusSec ?? 30,
                            GetDevStatusQ = command.GetDevStatusQ,
                            OrderUpSec = 300,
                            Rs485Sec = 0
                        };
                        db.Set<AnShengDeviceConfig>().Add(autoReportConfig);
                    }

                    await db.SaveChangesAsync(ct);
                    await transaction.CommitAsync(ct);
                }
                catch
                {
                    // 回滚本身也可能抛（连接已断），吞掉回滚异常，原异常交给外层记录与映射。
                    try
                    {
                        await transaction.RollbackAsync(ct);
                    }
                    catch (Exception rollbackEx)
                    {
                        _logger.LogError(rollbackEx, "{Tag} 事务回滚失败: IMEI={Imei}", ClaimLogTag, imei);
                    }

                    throw;
                }
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Tag} 认领落库失败: IMEI={Imei}", ClaimLogTag, imei);

            return AnShengClaimResult.Fail(
                AnShengClaimErrorCodes.PersistFailed,
                $"设备 {imei} 认领落库失败：{ex.Message}",
                AnShengProbeStatus.Probed);
        }

        // ── 步骤 12：同步静态品类缓存 ──
        // 必须在提交之后：提交前同步、事务却回滚了，内存里会留下一个数据库中并不存在的品类。
        Infrastructure.Protocol.Adapters.AnShengMqttProtocolAdapter.RegisterDeviceKind(imei, profile.Kind);

        // ── 步骤 13：fire-and-forget 下发 setAutoReport ──
        if (autoReportConfig != null)
        {
            DispatchAutoReport(
                device!.Id,
                autoReportConfig.GetDevStatusSec ?? 30,
                autoReportConfig.GetDevStatusQ);
        }

        _logger.LogInformation(
            "{Tag} 认领成功: IMEI={Imei}, DeviceId={DeviceId}, Kind={Kind}, Category={Category}, ProfileId={ProfileId}",
            ClaimLogTag, imei, device!.Id, profile.Kind, device!.Category, profile.Id);

        // ── 步骤 14：返回成功 ──
        return AnShengClaimResult.Ok(
            device!.Id, device!.Name, profile.Kind, profile.Id, AnShengProbeStatus.Probed);
    }

    /// <summary>
    /// 按主键或 IMEI 定位待认领记录。两者都给时以主键为准（设计 §3.8 DTO 注释）。
    /// </summary>
    /// <param name="db">数据库上下文。</param>
    /// <param name="command">认领指令。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>待认领记录；未命中返回 <c>null</c>。</returns>
    private static async Task<DiscoveredAnShengDevice?> FindDiscoveredAsync(
        AppDbContext db,
        AnShengClaimCommand command,
        CancellationToken ct)
    {
        var set = db.Set<DiscoveredAnShengDevice>();

        if (command.DiscoveredDeviceId is { } id)
        {
            return await set.FirstOrDefaultAsync(d => d.Id == id, ct);
        }

        var imei = command.Imei;
        return await set.FirstOrDefaultAsync(d => d.Imei == imei, ct);
    }

    /// <summary>
    /// 解析本次认领要用的协议配置：请求显式指定 → 按租户查唯一活跃安圣配置。
    /// </summary>
    /// <param name="db">数据库上下文。</param>
    /// <param name="command">认领指令。</param>
    /// <param name="discovered">待认领记录，用于兜底取 AppCode。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>协议配置主键；无可用配置返回 <c>null</c>。</returns>
    private static async Task<long?> ResolveProtocolConfigIdAsync(
        AppDbContext db,
        AnShengClaimCommand command,
        DiscoveredAnShengDevice discovered,
        CancellationToken ct)
    {
        if (command.ProtocolConfigId is > 0)
        {
            return command.ProtocolConfigId;
        }

        var appCode = FirstNonEmpty(command.AppCode, discovered.AppCode);

        var candidates = await db.Set<ProtocolConfig>()
            .AsNoTracking()
            .Where(c => c.IsActive && c.ProtocolType == AnShengProtocolType)
            .ToListAsync(ct);

        if (candidates.Count == 0)
        {
            return null;
        }

        // 优先同租户；租户对不上再退回「配置本身没绑租户」的公共配置。
        var matched = candidates.FirstOrDefault(c =>
                          appCode != null &&
                          string.Equals(c.AppCode, appCode, StringComparison.OrdinalIgnoreCase))
                      ?? candidates.FirstOrDefault(c => string.IsNullOrEmpty(c.AppCode));

        return matched?.Id;
    }

    /// <summary>
    /// 把探测结论回写到待认领记录，供 <c>/discovered</c> 列表直接展示，
    /// 免得前端为看一眼探测状态还要多查一次档案表。
    /// </summary>
    /// <param name="discovered">待认领记录。</param>
    /// <param name="profile">已更新的能力档案。</param>
    /// <param name="probe">探测结果。</param>
    private static void ApplyProbeToDiscovered(
        DiscoveredAnShengDevice discovered,
        AnShengDeviceProfile profile,
        AnShengProbeResult probe)
    {
        discovered.ProbeStatus = probe.Success
            ? AnShengProbeStatus.Probed
            : AnShengProbeStatus.ProbeFailed;
        discovered.ProbeError = probe.Success ? null : Truncate(probe.Error, 500);
        discovered.LastProbedAt = DateTime.UtcNow;

        if (!probe.Success)
        {
            // 探测失败时保留上一次探到的能力字段：旧数据仍是当前最可信的已知事实。
            return;
        }

        discovered.Kind = profile.Kind;

        // 「新值非空才覆盖」：档案里某字段本次没探到时，不要拿 null 把 discovered 上的旧值抹掉。
        if (profile.SlotAmount.HasValue) discovered.SlotAmount = profile.SlotAmount;
        if (!string.IsNullOrWhiteSpace(profile.Version)) discovered.Version = profile.Version;
        if (!string.IsNullOrWhiteSpace(profile.Iccid)) discovered.Iccid = profile.Iccid;
        if (!string.IsNullOrWhiteSpace(profile.NetType)) discovered.NetType = profile.NetType;
        if (!string.IsNullOrWhiteSpace(profile.Model)) discovered.Model = profile.Model;
    }

    /// <summary>
    /// 把探测的两条应答归并为一个能力快照。
    /// getDevInfo 先落，getDevStatus 后落且只补空位——
    /// 状态帧的 netType/version 有可能是设备重启中的中间态，不应盖掉信息帧的权威值。
    /// </summary>
    /// <param name="probe">探测结果。</param>
    /// <returns>归并后的能力快照。</returns>
    private static AnShengCapabilitySnapshot BuildSnapshot(AnShengProbeResult probe)
    {
        var fromInfo = AnShengCapabilitySnapshot.FromDevInfo(probe.DevInfo);
        var fromStatus = AnShengCapabilitySnapshot.FromDevStatus(probe.DevStatus);

        return new AnShengCapabilitySnapshot(
            NetType: FirstNonEmpty(fromInfo.NetType, fromStatus.NetType),
            SlotAmount: fromInfo.SlotAmount ?? fromStatus.SlotAmount,
            PhaseAmount: fromInfo.PhaseAmount ?? fromStatus.PhaseAmount,
            Version: FirstNonEmpty(fromInfo.Version, fromStatus.Version),
            Model: FirstNonEmpty(fromInfo.Model, fromStatus.Model),
            Iccid: FirstNonEmpty(fromInfo.Iccid, fromStatus.Iccid),
            Signal: fromInfo.Signal ?? fromStatus.Signal);
    }

    /// <summary>
    /// 认领成功后异步下发 setAutoReport。
    /// 必须自建作用域：HTTP 响应写回后，请求作用域内的 Scoped 服务已被释放。
    /// </summary>
    /// <param name="deviceId">设备主键。</param>
    /// <param name="getDevStatusSec">状态上报周期（秒）。</param>
    /// <param name="getDevStatusQ">状态上报查询参数。</param>
    private void DispatchAutoReport(long deviceId, int getDevStatusSec, string? getDevStatusQ)
    {
        var scopeFactory = _scopeFactory;

        _ = Task.Run(async () =>
        {
            using var innerScope = scopeFactory.CreateScope();
            var commandService = innerScope.ServiceProvider.GetRequiredService<IAnShengCommandService>();
            var taskLogger = innerScope.ServiceProvider
                .GetRequiredService<ILogger<AnShengDiscoveryService>>();

            try
            {
                await commandService.ConfigureAutoReportAsync(deviceId, new AnShengAutoReportSettings
                {
                    GetDevStatusSec = getDevStatusSec,
                    GetDevStatusQ = getDevStatusQ,
                    OrderUpSec = 300,
                    Rs485Sec = 0
                });

                taskLogger.LogInformation("{Tag} 认领后 setAutoReport 下发成功: DeviceId={DeviceId}, Sec={Sec}",
                    ClaimLogTag, deviceId, getDevStatusSec);
            }
            catch (Exception ex)
            {
                // 下发失败不影响认领结果：设备已经建好了，上报周期后续可在设备详情页重下。
                taskLogger.LogWarning(ex, "{Tag} 认领后 setAutoReport 下发失败: DeviceId={DeviceId}",
                    ClaimLogTag, deviceId);
            }
        });
    }

    // ─────────────────────────────────────────────
    // 辅助
    // ─────────────────────────────────────────────

    /// <summary>
    /// 取第一个非空白字符串。
    /// </summary>
    /// <param name="first">首选值。</param>
    /// <param name="second">备选值。</param>
    /// <returns>非空白的值；都为空返回 <c>null</c>。</returns>
    private static string? FirstNonEmpty(string? first, string? second)
        => !string.IsNullOrWhiteSpace(first) ? first
            : !string.IsNullOrWhiteSpace(second) ? second
                : null;

    /// <summary>
    /// 按列宽截断字符串，避免超长错因把 <c>SaveChanges</c> 顶失败。
    /// </summary>
    /// <param name="value">原始值。</param>
    /// <param name="maxLength">最大长度。</param>
    /// <returns>截断后的值。</returns>
    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }

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
