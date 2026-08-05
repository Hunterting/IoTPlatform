// T7-3 重构：命令下发从「一次性 fire-and-forget」变为「有校验、有登记、有留痕」的完整受理流程。
//
// 【改了什么】
//   1. 删除 R1 静态字典 FrameIdCommandIdMap 及其三个静态方法。
//      实读证实它<b>只写不读</b>（生产代码零 ResolveCommandId 调用点），
//      是一处纯粹的内存泄漏源 —— 进程活多久就涨多久，且进程重启即丢，
//      本来就承担不了「命令关联」的职责。取而代之的是持久化的 AnShengCommandRecord.CommandId。
//   2. 校验从「散落在本方法里的三个 if」上收到 AnShengCommandGuard 单点判定。
//   3. 品类来源从「适配器内存快照」改为「能力档案优先」的降级链（决策 D7）。
//   4. 下发从「先发后登记」改为「先登记后下发」（硬约束 N1，靠 IAnShengDownlinkPort 实现）。
//   5. 全流程落 AnShengCommandRecord：Pending → Sent / Rejected / Failed。
//
// 【本文件不写的三种终态】
//   Succeeded / Failed(设备应答失败) 由 AnShengMessageRouter 写；Timeout 由 SweepHost 写（T7-4）。
//   互斥点是在途表 TryRemove 的 CAS 语义：谁摘到条目谁才有权写终态。

using System.Text.Json;
using IoTPlatform.Configuration;
using IoTPlatform.Data;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Infrastructure.Protocol;
using IoTPlatform.Infrastructure.Protocol.AnSheng;
using IoTPlatform.Infrastructure.Protocol.AnSheng.Legacy;
using IoTPlatform.Models;
using IoTPlatform.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IoTPlatform.Services;

/// <summary>
/// 安圣 MQTT 设备命令服务实现。
///
/// 职责（T7 重构后）：
///   1. 组装下发上下文（设备 / 品类 / 固件 / 插槽数），品类走「档案 → 适配器快照 → Unknown」降级链；
///   2. 交 <see cref="AnShengCommandGuard"/> 单点校验，拒绝即落 <c>Rejected</c> 记录并<b>零发布</b>；
///   3. 落 <see cref="AnShengCommandRecord"/>（Pending），拿到主键；
///   4. <b>先</b>登记在途表（带 TCS 与 RecordId）、<b>后</b>发 MQTT，消除应答早于登记的竞态；
///   5. 置 <c>Sent</c> 并算出 <c>TimeoutAt</c>，把终态交给应答路径与超时清扫。
///
/// 【生命周期】Scoped —— 持有 <see cref="AppDbContext"/>。
///   在途表是 Singleton，由构造注入没问题（Scoped 可以依赖 Singleton，反之不行）。
/// </summary>
public class AnShengCommandService : IAnShengCommandService
{
    private readonly AppDbContext _db;
    private readonly IProtocolAdapterFactory _adapterFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAnShengDeviceProfileService _profiles;
    private readonly AnShengCommandGuard _guard;
    private readonly IAnShengPendingCommandStore _pending;
    private readonly AnShengCommandOptions _options;
    private readonly ILogger<AnShengCommandService> _logger;

    /// <summary>
    /// 构造命令服务。
    /// </summary>
    /// <param name="db">数据库上下文（Scoped）。</param>
    /// <param name="adapterFactory">协议适配器工厂。</param>
    /// <param name="scopeFactory">作用域工厂，供 <see cref="TriggerDiscoveryAsync"/> 脱离当前请求作用域使用。</param>
    /// <param name="profiles">设备能力档案服务，提供品类 / 固件 / 插槽数。</param>
    /// <param name="guard">下发闸门（纯函数）。</param>
    /// <param name="pending">在途命令表（Singleton）。</param>
    /// <param name="options">命令服务参数（TTL / 严格模式开关等）。</param>
    /// <param name="logger">日志。</param>
    public AnShengCommandService(
        AppDbContext db,
        IProtocolAdapterFactory adapterFactory,
        IServiceScopeFactory scopeFactory,
        IAnShengDeviceProfileService profiles,
        AnShengCommandGuard guard,
        IAnShengPendingCommandStore pending,
        IOptions<AnShengCommandOptions> options,
        ILogger<AnShengCommandService> logger)
    {
        _db = db;
        _adapterFactory = adapterFactory;
        _scopeFactory = scopeFactory;
        _profiles = profiles;
        _guard = guard;
        _pending = pending;
        _options = options?.Value ?? new AnShengCommandOptions();
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<AnShengCommandResponse> SendCommandAsync(
        long deviceId, string method,
        Dictionary<string, object?>? parameters = null,
        CancellationToken ct = default,
        string? commandId = null)
    {
        // CommandId 必须在最早期就确定：拒绝态也要能被 GET /commands/{id} 查到。
        var effectiveCommandId = string.IsNullOrWhiteSpace(commandId)
            ? Guid.NewGuid().ToString("N")
            : commandId!;

        AnShengCommandRecord? record = null;

        try
        {
            // ── 1. 设备与协议前置检查 ──────────────────────────────────────
            // 这三种失败发生在「连 IMEI / 租户码都拿不到」的阶段，无法构造合法记录行
            //（Imei / AppCode 都是 NOT NULL），故不落库、直接返回。
            var device = await _db.Devices
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == deviceId, ct);

            if (device == null)
            {
                return Failure(effectiveCommandId, $"设备 {deviceId} 不存在");
            }

            if (string.IsNullOrEmpty(device.SerialNumber))
            {
                return Failure(effectiveCommandId, $"设备 {deviceId} 未设置 IMEI（SerialNumber 为空）");
            }

            if (device.ProtocolConfigId == null)
            {
                return Failure(effectiveCommandId, $"设备 {deviceId} 未配置安圣协议（ProtocolConfigId 为空）");
            }

            var imei = device.SerialNumber;
            var appCode = device.AppCode;

            // ── 2. 适配器可用性 ───────────────────────────────────────────
            // 这里已经有 imei + appCode，可以落一条 Rejected 记录（RejectedByOffline），
            // 让「命令为什么没发出去」在流水里查得到，而不是只留一句日志。
            var adapter = _adapterFactory.GetAdapter((int)device.ProtocolConfigId.Value);
            if (adapter == null)
            {
                return await RejectAsync(
                    effectiveCommandId, appCode, deviceId, imei, method, parameters,
                    AnShengCommandRejectReason.RejectedByOffline,
                    new[] { $"安圣适配器未运行（ConfigId={device.ProtocolConfigId}）" },
                    errorCode: "ADAPTER_OFFLINE",
                    requiredFirmware: null,
                    ct);
            }

            if (!adapter.IsConnected)
            {
                return await RejectAsync(
                    effectiveCommandId, appCode, deviceId, imei, method, parameters,
                    AnShengCommandRejectReason.RejectedByOffline,
                    new[] { "安圣 MQTT 适配器未连接" },
                    errorCode: "ADAPTER_OFFLINE",
                    requiredFirmware: null,
                    ct);
            }

            // ── 3. 组装判据：协议族 + 品类降级链 + 固件 + 插槽数（决策 D7 / T14）────
            var profile = await _profiles.GetByDeviceIdAsync(deviceId, ct);
            var (kind, kindFromProfile) = ResolveKind(profile, imei);

            // T14：协议族是<b>显式</b>判定的三态结果（二开 / 充电桩 / 不认识）。
            // null 表示两份目录都不认识该 method —— Guard 会据此判 RejectedByUnknownMethod
            // 并零发布；改造前这里是「不在目录里就当 Legacy 真实下发」的兜底放行。
            var protocolFamily = AnShengProtocolFamilyResolver.Resolve(method);

            var context = new AnShengCommandContext
            {
                DeviceId = deviceId,
                Imei = imei,
                Method = method,
                Parameters = parameters,
                Kind = kind,
                KindFromProfile = kindFromProfile,
                Firmware = profile?.Version,
                SlotAmount = profile?.SlotAmount,
                RejectWhenKindUnknown = _options.RejectWhenKindUnknown,
                AllowLegacyMethod = protocolFamily == AnShengProtocolFamily.ChargingPile
            };

            // ── 4. 单点校验 ───────────────────────────────────────────────
            var decision = _guard.Evaluate(context);
            if (!decision.Allowed)
            {
                _logger.LogWarning(
                    "安圣命令被拒绝: CommandId={CommandId}, DeviceId={DeviceId}, IMEI={IMEI}, Method={Method}, "
                    + "Kind={Kind}, Reason={Reason}, Errors={Errors}",
                    effectiveCommandId, deviceId, imei, method, kind, decision.Reason, decision.ErrorMessage);

                return await RejectAsync(
                    effectiveCommandId, appCode, deviceId, imei, method, parameters,
                    decision.Reason ?? AnShengCommandRejectReason.RejectedByValidation,
                    decision.Errors,
                    errorCode: decision.Reason?.ToString(),
                    requiredFirmware: decision.RequiredFirmware,
                    ct);
            }

            // ── 5. 落 Pending 记录，拿到主键 ───────────────────────────────
            // 必须先落库：在途条目要带 RecordId，超时清扫才能按主键回填终态。
            var ttl = _options.ResolveTtl(method);
            record = new AnShengCommandRecord
            {
                AppCode = appCode,
                CommandId = effectiveCommandId,
                DeviceId = deviceId,
                Imei = imei,
                Method = method,
                Status = AnShengCommandStatus.Pending,
                RequestJson = AnShengCommandRecord.TruncateJson(
                    AnShengSecretMasker.MaskRequestJson(method, parameters)) ?? "{}",
                IssuedAt = DateTime.UtcNow
            };

            _db.Set<AnShengCommandRecord>().Add(record);
            await _db.SaveChangesAsync(ct);

            // ── 6. 先登记在途、后下发（硬约束 N1）─────────────────────────
            string frameId;

            if (adapter is IAnShengDownlinkPort port)
            {
                frameId = AnShengCommandBuilder.NewFrameId();

                var registration = await _pending.RegisterAsync(
                    imei, frameId,
                    PendingCommand.Create(record.Id, imei, frameId, method, ttl, record.Id));

                if (!registration.Registered)
                {
                    // 同 (imei, frameId) 已有未过期条目。frameId 含 8 位递增序号 + 8 位随机数，
                    // 撞上意味着别的地方在复用 frameId —— 属于必须暴露的异常，不静默重试。
                    await FailRecordAsync(record, "DUPLICATE_FRAME",
                        $"帧 {frameId} 已在途，本次下发已取消", ct);

                    return Failure(effectiveCommandId, $"帧 {frameId} 已在途，本次下发已取消");
                }

                try
                {
                    await port.PublishAsync(deviceId, imei, method, parameters, frameId, ct);
                }
                catch
                {
                    // 发布失败必须立刻摘掉在途条目，否则它会一直占位到 TTL，
                    // 期间该设备任何同 frameId 的上行都会被误判成本命令的应答。
                    await _pending.CompleteAsync(imei, frameId, null);
                    throw;
                }
            }
            else
            {
                // 降级路径（风险 R2）：适配器没实现下行接缝，只能先发后登记，
                // 回到 T7 之前的竞态水平 —— 极快的应答可能先于登记到达而被当成主动上报。
                // 功能不中断，但必须留 Warning，否则这种退化会悄无声息地长期存在。
                _logger.LogWarning(
                    "安圣适配器 {Adapter} 未实现 IAnShengDownlinkPort，本次下发退化为「先发后登记」，"
                    + "存在应答早于登记的竞态（风险 R2）。CommandId={CommandId}, IMEI={IMEI}, Method={Method}",
                    adapter.GetType().Name, effectiveCommandId, imei, method);

                var parametersJson = parameters != null
                    ? JsonSerializer.Serialize(parameters)
                    : string.Empty;

                frameId = await adapter.SendCommandAsync(deviceId, imei, method, parametersJson, ct);

                var registration = await _pending.RegisterAsync(
                    imei, frameId,
                    PendingCommand.Create(record.Id, imei, frameId, method, ttl, record.Id));

                if (!registration.Registered)
                {
                    // 报文已出网，不能再判失败；只能告警，让这条命令走超时兜底。
                    _logger.LogWarning(
                        "降级路径登记在途失败（同帧已存在）: CommandId={CommandId}, IMEI={IMEI}, FrameId={FrameId}",
                        effectiveCommandId, imei, frameId);
                }
            }

            // ── 7. 置 Sent，并算出超时判定时刻 ────────────────────────────
            var sentAt = DateTime.UtcNow;
            record.FrameId = frameId;
            record.Status = AnShengCommandStatus.Sent;
            record.SentAt = sentAt;
            record.TimeoutAt = sentAt.Add(ttl);
            await _db.SaveChangesAsync(ct);

            // ── 8. 用实际下发的 frameId 重建报文回显，保证 Payload 与 FrameId 一致 ──
            //
            // 【T14】按<b>显式协议族</b>选择报文结构，而不是「decision.Spec 是不是 null」这个
            // 间接信号 —— 后者与「拒绝时也没有 Spec」共用同一种取值，语义上是个巧合。
            // 走到这里 Guard 已放行，protocolFamily 必非 null；仍写默认分支以防未来新增族时漏改。
            var payload = protocolFamily == AnShengProtocolFamily.ChargingPile
                ? new AnShengLegacyCommandBuilder().BuildCommand(imei, method, parameters, frameId).Payload
                : new AnShengCommandBuilder().BuildRaw(imei, method, parameters, kind, frameId).Payload;

            _logger.LogInformation(
                "安圣命令已下发: CommandId={CommandId}, DeviceId={DeviceId}, IMEI={IMEI}, Method={Method}, "
                + "Family={Family}, Kind={Kind}, KindFromProfile={KindFromProfile}, FrameId={FrameId}, TtlSeconds={Ttl}",
                effectiveCommandId, deviceId, imei, method, protocolFamily, kind, kindFromProfile,
                frameId, ttl.TotalSeconds);

            return new AnShengCommandResponse
            {
                Success = true,
                CommandId = effectiveCommandId,
                FrameId = frameId,
                Payload = payload,
                SentAt = sentAt
            };
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "安圣命令下发失败（连接异常）: CommandId={CommandId}, DeviceId={DeviceId}, Method={Method}",
                effectiveCommandId, deviceId, method);

            await FailRecordAsync(record, "ADAPTER_ERROR", ex.Message, CancellationToken.None);
            return Failure(effectiveCommandId, $"适配器连接异常：{ex.Message}");
        }
        catch (NotSupportedException ex)
        {
            // 适配器的「默认拒绝」闸门（T4）：协议外方法被阻断在出网前。
            _logger.LogWarning(ex, "安圣命令被适配器拒绝: CommandId={CommandId}, DeviceId={DeviceId}, Method={Method}",
                effectiveCommandId, deviceId, method);

            await FailRecordAsync(record, "METHOD_NOT_SUPPORTED", ex.Message, CancellationToken.None);
            return Failure(effectiveCommandId, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "安圣命令下发失败: CommandId={CommandId}, DeviceId={DeviceId}, Method={Method}",
                effectiveCommandId, deviceId, method);

            await FailRecordAsync(record, "PUBLISH_FAILED", ex.Message, CancellationToken.None);
            return Failure(effectiveCommandId, $"下发失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 解析设备品类，按「能力档案 → 适配器内存快照 → Unknown」降级（决策 D7）。
    ///
    /// 【为什么档案优先】适配器的 <c>DeviceKinds</c> 是进程内内存缓存，重启即空、
    /// 且只有设备上行过才有值；档案是落库的结论，重启不丢、认领时就已确定。
    /// 反过来（快照优先）会出现「刚重启那几分钟品类全是 Unknown」的窗口。
    /// </summary>
    /// <param name="profile">设备能力档案，可为 null（存量设备未回填）。</param>
    /// <param name="imei">设备 IMEI。</param>
    /// <returns>品类，以及它是否来自档案。</returns>
    private static (AnShengDeviceKind Kind, bool FromProfile) ResolveKind(
        AnShengDeviceProfile? profile,
        string imei)
    {
        if (profile != null && profile.Kind != AnShengDeviceKind.Unknown)
        {
            return (profile.Kind, true);
        }

        // 注：此处使用完全限定名，避免与本命名空间下的同名类型产生 using 冲突。
        var fromAdapter = Infrastructure.Protocol.Adapters.AnShengMqttProtocolAdapter.GetDeviceKind(imei);
        return (fromAdapter, false);
    }

    /// <summary>
    /// 落一条 <c>Rejected</c> 记录并构造拒绝响应。
    ///
    /// 【不变式】走到这里意味着<b>零 MQTT 发布、零在途登记</b>，
    /// 因此 <c>FrameId</c> 与 <c>SentAt</c> 恒为 null —— 这两列就是验收 #1 的持久化证据。
    /// </summary>
    /// <param name="commandId">平台命令标识。</param>
    /// <param name="appCode">租户码。</param>
    /// <param name="deviceId">设备主键。</param>
    /// <param name="imei">设备 IMEI。</param>
    /// <param name="method">协议方法名。</param>
    /// <param name="parameters">原始下发参数（落库前会掩码）。</param>
    /// <param name="reason">拒绝原因。</param>
    /// <param name="errors">逐条中文原因。</param>
    /// <param name="errorCode">机器可读错误码。</param>
    /// <param name="requiredFirmware">所需最低固件版本，可为 null。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>拒绝响应。</returns>
    private async Task<AnShengCommandResponse> RejectAsync(
        string commandId,
        string appCode,
        long deviceId,
        string imei,
        string method,
        IReadOnlyDictionary<string, object?>? parameters,
        AnShengCommandRejectReason reason,
        IReadOnlyList<string> errors,
        string? errorCode,
        string? requiredFirmware,
        CancellationToken ct)
    {
        var message = errors.Count > 0 ? string.Join("；", errors) : reason.ToString();
        var now = DateTime.UtcNow;

        var record = new AnShengCommandRecord
        {
            AppCode = appCode,
            CommandId = commandId,
            DeviceId = deviceId,
            Imei = imei,
            Method = method,
            FrameId = null,
            Status = AnShengCommandStatus.Rejected,
            RejectReason = reason,
            RequestJson = AnShengCommandRecord.TruncateJson(
                AnShengSecretMasker.MaskRequestJson(method, parameters)) ?? "{}",
            ErrorCode = errorCode,
            ErrorMessage = AnShengCommandRecord.TruncateErrorMessage(message),
            IssuedAt = now,
            SentAt = null,
            CompletedAt = now
        };

        try
        {
            _db.Set<AnShengCommandRecord>().Add(record);
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // 留痕失败不该把「已经判定好的拒绝」升级成异常：调用方拿到的结论是一样的。
            _logger.LogError(ex,
                "写入命令拒绝记录失败（拒绝结论不受影响）: CommandId={CommandId}, Method={Method}, Reason={Reason}",
                commandId, method, reason);
        }

        return new AnShengCommandResponse
        {
            Success = false,
            CommandId = commandId,
            FrameId = null,
            RejectReason = reason,
            Errors = errors.Count > 0 ? errors : Array.Empty<string>(),
            RequiredFirmware = requiredFirmware,
            ErrorMessage = message,
            SentAt = now
        };
    }

    /// <summary>
    /// 把一条已落库的记录置为 <c>Failed</c> 终态。记录为 null（还没落库就失败）时静默返回。
    /// </summary>
    /// <param name="record">命令记录，可为 null。</param>
    /// <param name="errorCode">机器可读错误码。</param>
    /// <param name="message">失败原因。</param>
    /// <param name="ct">取消令牌。</param>
    private async Task FailRecordAsync(
        AnShengCommandRecord? record,
        string errorCode,
        string message,
        CancellationToken ct)
    {
        if (record == null) return;
        if (record.Status is AnShengCommandStatus.Succeeded
            or AnShengCommandStatus.Failed
            or AnShengCommandStatus.Timeout
            or AnShengCommandStatus.Rejected)
        {
            return;   // 已是终态：终态只写一次
        }

        var now = DateTime.UtcNow;
        record.Status = AnShengCommandStatus.Failed;
        record.ErrorCode = errorCode;
        record.ErrorMessage = AnShengCommandRecord.TruncateErrorMessage(message);
        record.CompletedAt = now;
        record.DurationMs = record.SentAt.HasValue
            ? (int)Math.Max(0, (now - record.SentAt.Value).TotalMilliseconds)
            : null;

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "写入命令失败终态失败: CommandId={CommandId}, Method={Method}",
                record.CommandId, record.Method);
        }
    }

    /// <summary>
    /// 构造一个「前置条件不满足」的失败响应（无 RejectReason —— 它不是 Guard 判的）。
    /// </summary>
    /// <param name="commandId">平台命令标识。</param>
    /// <param name="message">失败原因。</param>
    /// <returns>失败响应。</returns>
    private static AnShengCommandResponse Failure(string commandId, string message)
        => new()
        {
            Success = false,
            CommandId = commandId,
            ErrorMessage = message,
            Errors = new[] { message }
        };

    /// <inheritdoc />
    public async Task<AnShengCommandResponse> ConfigureAutoReportAsync(
        long deviceId,
        AnShengAutoReportSettings settings,
        CancellationToken ct = default)
    {
        var parameters = new Dictionary<string, object?>
        {
            ["getDevStatusSec"] = settings.GetDevStatusSec,
            ["orderUpSec"] = settings.OrderUpSec,
            ["rs485Sec"] = settings.Rs485Sec,
            ["rs485BaudRate"] = 115200,   // 设备默认波特率
            ["rs485SendWaitMs"] = 300      // 设备默认发送等待
        };

        if (!string.IsNullOrEmpty(settings.GetDevStatusQ))
        {
            parameters["getDevStatusQ"] = settings.GetDevStatusQ;
        }

        return await SendCommandAsync(deviceId, "setAutoReport", parameters, ct);
    }

    /// <inheritdoc />
    public async Task TriggerDiscoveryAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // 1. 查询所有未认领设备
        var unclaimed = await db.Set<DiscoveredAnShengDevice>()
            .AsNoTracking()
            .Where(d => !d.IsClaimed)
            .OrderBy(d => d.DiscoveredAt)
            .Take(20)
            .ToListAsync(ct);

        if (unclaimed.Count == 0)
        {
            _logger.LogInformation("TriggerDiscoveryAsync: 无未认领设备");
            return;
        }

        // 2. 获取活跃的安圣协议配置
        var activeConfigs = await db.Set<ProtocolConfig>()
            .AsNoTracking()
            .Where(c => c.IsActive && c.ProtocolType == "ANSHENG_MQTT")
            .ToListAsync(ct);

        if (activeConfigs.Count == 0)
        {
            _logger.LogWarning("TriggerDiscoveryAsync: 无活跃的 ANSHENG_MQTT 协议配置");
            return;
        }

        _logger.LogInformation("TriggerDiscoveryAsync: 向 {Count} 个未认领设备发送 getDevInfo", unclaimed.Count);

        foreach (var device in unclaimed)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var config = activeConfigs.FirstOrDefault(c =>
                    string.IsNullOrEmpty(device.AppCode) ||
                    string.Equals(c.AppCode, device.AppCode, StringComparison.OrdinalIgnoreCase));

                if (config == null) continue;

                var adapter = _adapterFactory.GetAdapter((int)config.Id);
                if (adapter == null || !adapter.IsConnected) continue;

                // 探测阶段不落命令记录：设备还没被认领，没有 DeviceId、也没有业务语义上的「操作」，
                // 这属于平台自发的发现流量，与「用户下发的命令」不是一回事（决策 D2 的表定位）。
                await adapter.SendCommandAsync(
                    deviceId: 0L,
                    serialNumber: device.Imei,
                    commandType: "getDevInfo",
                    parameters: string.Empty,
                    cancellationToken: ct);

                _logger.LogDebug("TriggerDiscoveryAsync: 已发送 getDevInfo → IMEI={IMEI}", device.Imei);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "TriggerDiscoveryAsync: 发送 getDevInfo 失败（已跳过）IMEI={IMEI}", device.Imei);
            }
        }
    }

    // ─── 二开设备通用命令实现 ───
    // 注：setSwitch / getSwitchStatus / setSwitchConfig / getSwitchConfig 四个方法
    //     在官方协议 asopen.md 中并不存在（历史臆造实现），已物理删除。
    //     开关通断请改用 SendCommandAsync(deviceId, "action", { slotNum, action })。

    /// <inheritdoc />
    public async Task<AnShengCommandResponse> RebootDeviceAsync(
        long deviceId, CancellationToken ct = default)
        => await SendCommandAsync(deviceId, "reboot", null, ct);

    /// <inheritdoc />
    public async Task<AnShengCommandRecord?> GetRecordAsync(string commandId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(commandId)) return null;

        // 请求作用域内 ITenantContextAccessor.Current 已就绪，全局租户过滤器自动生效，
        // 跨租户查询会自然落空（返回 null），无需手写 AppCode 条件。
        return await _db.Set<AnShengCommandRecord>()
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.CommandId == commandId, ct);
    }

    // ─── R1 已删除（T7-3）────────────────────────────────────────────────
    // 原有的 static ConcurrentDictionary<string,string> FrameIdCommandIdMap 及
    // RegisterFrameIdMapping / ResolveCommandId / RemoveFrameIdMapping 三个静态方法已物理删除。
    //
    // 删除依据：全仓检索证实生产代码中<b>零</b> ResolveCommandId 调用点 —— 它只被写入、从不被读取，
    // 却随每条命令无限增长（无过期、无上限），是一处确凿的内存泄漏；
    // 且静态状态跨进程重启即丢，本就承担不了「命令关联」的职责。
    //
    // 替代方案：AnShengCommandRecord.CommandId（唯一索引，与 DeviceCommand.CommandId 同值），
    // 以及在途表 IAnShengPendingCommandStore（key = imei:frameId，带 TTL 与 RecordId）。
    // 前者持久、后者带生命周期，两者都不会无界增长。
}
