using IoTPlatform.Data;
using IoTPlatform.Infrastructure.Protocol.Adapters;
using IoTPlatform.Infrastructure.Protocol.AnSheng;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace IoTPlatform.Services;

/// <summary>
/// <see cref="IAnShengDeviceProfileService"/> 的默认实现。
///
/// 【职责边界】
///   本服务<b>只管档案本身</b>：查、建、合并快照、判品类、同步静态品类字典。
///   它<b>不</b>发指令（那是 <c>IAnShengProbeService</c> 的事）、
///   <b>不</b>创建 Device（那是 <c>AnShengDiscoveryService.ClaimAsync</c> 的事）、
///   <b>不</b>调 SaveChanges（事务边界由编排方决定，避免在人家的事务中途提交）。
///
/// 【生命周期】Scoped —— 持有 <c>AppDbContext</c>。
/// </summary>
public class AnShengDeviceProfileService : IAnShengDeviceProfileService
{
    private readonly AppDbContext _db;
    private readonly ILogger<AnShengDeviceProfileService>? _logger;

    /// <summary>
    /// 构造函数。
    /// </summary>
    /// <param name="db">数据库上下文。</param>
    /// <param name="logger">日志器，可为空（便于单元测试直接 new）。</param>
    public AnShengDeviceProfileService(
        AppDbContext db,
        ILogger<AnShengDeviceProfileService>? logger = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<AnShengDeviceProfile?> GetByImeiAsync(
        string imei,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imei))
        {
            return null;
        }

        // 不写 AppCode 条件：AppDbContext 的全局查询过滤器已自动附加。
        return await _db.AnShengDeviceProfiles
            .FirstOrDefaultAsync(p => p.Imei == imei, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<AnShengDeviceProfile?> GetByDeviceIdAsync(
        long deviceId,
        CancellationToken cancellationToken = default)
    {
        if (deviceId <= 0)
        {
            return null;
        }

        return await _db.AnShengDeviceProfiles
            .FirstOrDefaultAsync(p => p.DeviceId == deviceId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<AnShengDeviceProfile> GetOrCreateAsync(
        string imei,
        string appCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imei))
        {
            throw new ArgumentException("IMEI 不能为空。", nameof(imei));
        }

        var existing = await GetByImeiAsync(imei, cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        var now = DateTime.UtcNow;
        var profile = new AnShengDeviceProfile
        {
            Imei = imei,
            AppCode = appCode ?? string.Empty,
            Kind = AnShengDeviceKind.Unknown,
            KindSource = AnShengKindSource.Unknown,
            ProbeStatus = AnShengProbeStatus.NotProbed,
            CreatedAt = now,
            UpdatedAt = now
        };

        // 只入变更追踪，不落库。调用方负责在自己的事务里 SaveChanges。
        await _db.AnShengDeviceProfiles.AddAsync(profile, cancellationToken);
        return profile;
    }

    /// <inheritdoc />
    public (AnShengDeviceKind Kind, AnShengKindSource Source) ResolveKind(
        AnShengDeviceProfile? profile,
        AnShengCapabilitySnapshot snapshot,
        AnShengDeviceKind? manualKind = null)
    {
        snapshot ??= AnShengCapabilitySnapshot.Empty;

        // ── 一级：人工权威 ──────────────────────────────────────────
        // 本次调用显式带了人工品类 ⇒ 直接采信，来源标 Manual。
        if (manualKind.HasValue && manualKind.Value != AnShengDeviceKind.Unknown)
        {
            return (manualKind.Value, AnShengKindSource.Manual);
        }

        // 档案里已经是人工指定过的非 Unknown 值 ⇒ 任何自动推断都不得改写它。
        // 这条是「运维手工纠正后又被自学习覆盖回去」这类事故的唯一防线。
        if (profile != null
            && profile.KindSource == AnShengKindSource.Manual
            && profile.Kind != AnShengDeviceKind.Unknown)
        {
            return (profile.Kind, AnShengKindSource.Manual);
        }

        // ── 二/三级：按快照推断 ────────────────────────────────────
        // 快照字段缺失时，回落到档案里的历史值——上一次探到的仍是有效事实。
        var netType = FirstNonEmpty(snapshot.NetType, profile?.NetType);
        var version = FirstNonEmpty(snapshot.Version, profile?.Version);
        var model = FirstNonEmpty(snapshot.Model, profile?.Model);
        var slotAmount = snapshot.SlotAmount ?? profile?.SlotAmount;

        var inferred = AnShengDeviceKindResolver.InferKind(netType, slotAmount, version, model);

        if (inferred == AnShengDeviceKind.Unknown)
        {
            // 推不出来不代表要清空。保留档案里已有的判定结果与来源。
            return profile != null
                ? (profile.Kind, profile.KindSource)
                : (AnShengDeviceKind.Unknown, AnShengKindSource.Unknown);
        }

        return (inferred, AnShengKindSource.Probe);
    }

    /// <inheritdoc />
    public async Task<AnShengDeviceProfile> ApplyProbeAsync(
        string imei,
        string appCode,
        AnShengCapabilitySnapshot snapshot,
        AnShengDeviceKind? manualKind = null,
        CancellationToken cancellationToken = default)
    {
        snapshot ??= AnShengCapabilitySnapshot.Empty;

        var profile = await GetOrCreateAsync(imei, appCode, cancellationToken);

        // 判品类必须在合并快照之前：ResolveKind 需要「旧档案 + 新快照」两份输入，
        // 先合并会让旧值被覆盖，Manual 权威判定就失去了比对基准。
        var (kind, source) = ResolveKind(profile, snapshot, manualKind);

        MergeSnapshot(profile, snapshot);

        profile.Kind = kind;
        profile.KindSource = source;
        profile.ProbeStatus = AnShengProbeStatus.Probed;
        profile.ProbeError = null;
        profile.LastProbedAt = DateTime.UtcNow;
        profile.UpdatedAt = DateTime.UtcNow;

        SyncDeviceKindCache(profile);

        _logger?.LogInformation(
            "安圣设备档案已更新（探测）: IMEI={Imei}, Kind={Kind}, Source={Source}, SlotAmount={SlotAmount}",
            imei, kind.ToDisplayName(), source, profile.SlotAmount);

        return profile;
    }

    /// <inheritdoc />
    public async Task<AnShengDeviceProfile> ApplyProbeFailureAsync(
        string imei,
        string appCode,
        string? error,
        CancellationToken cancellationToken = default)
    {
        var profile = await GetOrCreateAsync(imei, appCode, cancellationToken);

        profile.ProbeStatus = AnShengProbeStatus.ProbeFailed;
        profile.ProbeError = Truncate(error, 500);
        profile.LastProbedAt = DateTime.UtcNow;
        profile.UpdatedAt = DateTime.UtcNow;

        // 刻意不动 Kind / SlotAmount / Version：
        // 这次没探到不等于上次探到的是假的，清空只会让能力校验从"降级"退化成"全错"。

        _logger?.LogWarning(
            "安圣设备探测失败: IMEI={Imei}, Error={Error}", imei, profile.ProbeError);

        return profile;
    }

    /// <inheritdoc />
    public async Task<AnShengDeviceProfile> RefreshAsync(
        string imei,
        string appCode,
        AnShengCapabilitySnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        snapshot ??= AnShengCapabilitySnapshot.Empty;

        var profile = await GetOrCreateAsync(imei, appCode, cancellationToken);

        var (kind, source) = ResolveKind(profile, snapshot);

        MergeSnapshot(profile, snapshot);

        profile.Kind = kind;

        // 自学习的可信度低于探测：只有在「推断确实产出了新结论」且
        // 「原来不是 Manual」时才把来源降级标记为 Uplink。
        if (source != AnShengKindSource.Manual && kind != AnShengDeviceKind.Unknown)
        {
            profile.KindSource = profile.KindSource == AnShengKindSource.Probe
                ? AnShengKindSource.Probe   // 探测结论比上行更权威，来源不降级
                : AnShengKindSource.Uplink;
        }
        else
        {
            profile.KindSource = source;
        }

        profile.UpdatedAt = DateTime.UtcNow;

        SyncDeviceKindCache(profile);

        _logger?.LogDebug(
            "安圣设备档案已刷新（上行）: IMEI={Imei}, Kind={Kind}, Source={Source}",
            imei, profile.Kind.ToDisplayName(), profile.KindSource);

        return profile;
    }

    /// <inheritdoc />
    public void AttachDevice(AnShengDeviceProfile profile, long deviceId)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (deviceId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(deviceId), "设备主键必须为正数。");
        }

        profile.DeviceId = deviceId;
        profile.UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 把快照合并进档案。
    ///
    /// 【唯一规则：新的非空值才覆盖，空值一律不清除既有数据】
    ///   探测是两段式的（getDevInfo → getDevStatus），两段各自只带部分字段。
    ///   若按"整体赋值"语义合并，第二段的 null 会把第一段刚写进去的 version 抹掉。
    ///   字符串还要额外挡住空白串——固件对"没有"的表达方式并不统一，
    ///   有的不带该键，有的带一个 <c>""</c>，两者都应视为「没探到」。
    /// </summary>
    /// <param name="profile">目标档案。</param>
    /// <param name="snapshot">待合并的快照。</param>
    private static void MergeSnapshot(AnShengDeviceProfile profile, AnShengCapabilitySnapshot snapshot)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.NetType))
        {
            profile.NetType = snapshot.NetType;
        }

        if (snapshot.SlotAmount.HasValue)
        {
            profile.SlotAmount = snapshot.SlotAmount;
        }

        if (snapshot.PhaseAmount.HasValue)
        {
            profile.PhaseAmount = snapshot.PhaseAmount;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.Version))
        {
            profile.Version = snapshot.Version;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.Model))
        {
            profile.Model = snapshot.Model;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.Iccid))
        {
            profile.Iccid = snapshot.Iccid;
        }

        if (snapshot.Signal.HasValue)
        {
            profile.Signal = snapshot.Signal;
        }
    }

    /// <summary>
    /// 把判定出的品类同步进适配器的进程级品类字典。
    ///
    /// 【为什么必须同步】
    ///   下发链路用 <c>AnShengMqttProtocolAdapter</c> 的静态字典决定
    ///   「这台设备的报文要不要注入 timestamp」「这条命令它支不支持」。
    ///   档案判出了品类却不同步，下发侧仍按 Unknown 走最保守策略，
    ///   等于品类判定白做。反之，同步一次之后即便设备当前离线也能正确下发。
    /// </summary>
    /// <param name="profile">已判定品类的档案。</param>
    private void SyncDeviceKindCache(AnShengDeviceProfile profile)
    {
        if (profile.Kind == AnShengDeviceKind.Unknown || string.IsNullOrWhiteSpace(profile.Imei))
        {
            return;
        }

        try
        {
            AnShengMqttProtocolAdapter.RegisterDeviceKind(profile.Imei, profile.Kind);
        }
        catch (Exception ex)
        {
            // 静态字典同步失败只影响下发优化，不应让认领事务回滚。
            _logger?.LogWarning(ex, "同步安圣设备品类缓存失败: IMEI={Imei}", profile.Imei);
        }
    }

    /// <summary>
    /// 返回第一个非空白字符串；全部为空返回 <c>null</c>。
    /// </summary>
    /// <param name="primary">优先候选。</param>
    /// <param name="fallback">兜底候选。</param>
    /// <returns>非空白字符串或 null。</returns>
    private static string? FirstNonEmpty(string? primary, string? fallback)
    {
        if (!string.IsNullOrWhiteSpace(primary))
        {
            return primary;
        }

        return string.IsNullOrWhiteSpace(fallback) ? null : fallback;
    }

    /// <summary>
    /// 按数据库列宽截断字符串，避免超长错误信息导致整条 SQL 失败。
    /// </summary>
    /// <param name="value">原始值。</param>
    /// <param name="maxLength">最大长度。</param>
    /// <returns>截断后的字符串；输入为空返回 null。</returns>
    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
