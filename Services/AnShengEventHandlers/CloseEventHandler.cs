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
/// <c>close</c>（遗嘱）事件 Handler（决策 3）。
///
/// 业务动作：<see cref="AnShengOfflineDebouncer.Arm"/> 起一个
/// <c>CloseDebounceSeconds</c> 窗口。窗口到期且无人撤销才真正置离线——这是验收 #5
/// 「30s 内收到 connected 则不离线」的实现基础。
///
/// ★ 去抖必须由此 Handler 触发，<b>不能</b>由 <see cref="AnShengDiscoveryService"/>
/// 直连 <c>DeviceWill</c> 完成——后者会在 <c>close</c> 瞬间立即置离线，使窗口形同虚设。
/// </summary>
public sealed class CloseEventHandler : AnShengEventHandlerBase
{
    private readonly AnShengOfflineDebouncer _debouncer;

    /// <summary>构造 Handler。</summary>
    public CloseEventHandler(
        AnShengDataNormalizer normalizer,
        IDataCollectionService collector,
        AppDbContext db,
        AnShengOfflineDebouncer debouncer,
        ILogger<CloseEventHandler> logger)
        : base(normalizer, collector, db, logger)
    {
        _debouncer = debouncer;
    }

    /// <inheritdoc />
    public override string Method => "close";

    /// <inheritdoc />
    protected override Task<AnShengEventOutcome> OnHandleAsync(
        AnShengUplinkContext ctx, CancellationToken cancellationToken)
    {
        // 起离线去抖窗口；窗口到期无人撤销才真正置离线（验收 #5）。
        _debouncer.Arm(ctx.Imei, ctx.AppCode);

        return Task.FromResult(new AnShengEventOutcome
        {
            PersistEvent = true,
            DispatchToRules = true,
            Severity = AnShengEventSeverity.Warning,
        });
    }
}
