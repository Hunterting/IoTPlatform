using System.Threading;
using System.Threading.Tasks;
using IoTPlatform.Data;
using IoTPlatform.Infrastructure.Protocol.AnSheng;
using IoTPlatform.Models;
using IoTPlatform.Services;
using IoTPlatform.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace IoTPlatform.Services.AnShengEventHandlers;

/// <summary>
/// <c>connected</c> 事件 Handler（决策 3）。
///
/// 业务动作：
///   1. <see cref="AnShengOfflineDebouncer.Cancel"/> —— 撤销离线去抖窗口，
///      30s 内重连的设备不会被误置离线（验收 #5）；
///   2. <see cref="IAnShengDiscoveryService.OnDeviceOnlineAsync"/> —— 同步在线状态。
///
/// connected 不带能力快照，故不建档（决策 A）；档案刷新由 Router 在
/// <c>getDevStatus</c> 上行时统一处理，本 Handler 不重复。
/// </summary>
public sealed class ConnectedEventHandler : AnShengEventHandlerBase
{
    private readonly AnShengOfflineDebouncer _debouncer;
    private readonly IAnShengDiscoveryService _discovery;

    /// <summary>构造 Handler。</summary>
    public ConnectedEventHandler(
        AnShengDataNormalizer normalizer,
        IDataCollectionService collector,
        AppDbContext db,
        AnShengOfflineDebouncer debouncer,
        IAnShengDiscoveryService discovery,
        ILogger<ConnectedEventHandler> logger)
        : base(normalizer, collector, db, logger)
    {
        _debouncer = debouncer;
        _discovery = discovery;
    }

    /// <inheritdoc />
    public override string Method => "connected";

    /// <inheritdoc />
    protected override async Task<AnShengEventOutcome> OnHandleAsync(
        AnShengUplinkContext ctx, CancellationToken cancellationToken)
    {
        // ① 撤销离线去抖窗口：30s 内重连则设备不乱跳。
        _debouncer.Cancel(ctx.Imei);

        // ② 同步设备在线状态（更新 discovered / device 在线标记）。
        await _discovery.OnDeviceOnlineAsync(ctx.Imei, null, null, ctx.AppCode, cancellationToken)
            .ConfigureAwait(false);

        return new AnShengEventOutcome
        {
            PersistEvent = true,
            DispatchToRules = true,
            Severity = AnShengEventSeverity.Info,
        };
    }
}
