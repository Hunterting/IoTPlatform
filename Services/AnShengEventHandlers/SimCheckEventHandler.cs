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
/// <c>simCheck</c> 事件 Handler（决策 4）。
///
/// 业务动作：设备<b>主动</b>上报的 SIM 异常（无在途 frameId，Router 已判为 Event），
/// 以 <see cref="AnShengEventSeverity.Warning"/> 落库，并投递规则引擎交由 DataRule 配告警。
///
/// 用户<b>主动下发</b> simCheck 查询的应答（带在途 frameId）走 Response 分支（见 Router），
/// 不会进本 Handler——这正是「软白名单」分离的设计意图。
/// </summary>
public sealed class SimCheckEventHandler : AnShengEventHandlerBase
{
    /// <summary>构造 Handler。</summary>
    public SimCheckEventHandler(
        AnShengDataNormalizer normalizer,
        IDataCollectionService collector,
        AppDbContext db,
        ILogger<SimCheckEventHandler> logger)
        : base(normalizer, collector, db, logger)
    {
    }

    /// <inheritdoc />
    public override string Method => "simCheck";

    /// <inheritdoc />
    protected override Task<AnShengEventOutcome> OnHandleAsync(
        AnShengUplinkContext ctx, CancellationToken cancellationToken)
    {
        return Task.FromResult(new AnShengEventOutcome
        {
            PersistEvent = true,
            DispatchToRules = true,
            Severity = AnShengEventSeverity.Warning,
        });
    }
}
