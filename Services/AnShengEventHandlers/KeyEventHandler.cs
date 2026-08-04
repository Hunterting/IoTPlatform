using System.Collections.Generic;
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
/// <c>keyEvent</c>（按键）事件 Handler。
///
/// 业务动作：解析位路号 <c>slot_num</c>（多路机型在报文中带，单路机型可空），
/// 将其与展平后的 <c>slot{n}_state</c> 一并落库 / 投递规则引擎（验收 #4）。
/// </summary>
public sealed class KeyEventHandler : AnShengEventHandlerBase
{
    /// <summary>构造 Handler。</summary>
    public KeyEventHandler(
        AnShengDataNormalizer normalizer,
        IDataCollectionService collector,
        AppDbContext db,
        ILogger<KeyEventHandler> logger)
        : base(normalizer, collector, db, logger)
    {
    }

    /// <inheritdoc />
    public override string Method => "keyEvent";

    /// <inheritdoc />
    protected override Task<AnShengEventOutcome> OnHandleAsync(
        AnShengUplinkContext ctx, CancellationToken cancellationToken)
    {
        int? slotNum = null;
        IDictionary<string, object?>? dataPoints = null;

        if (ctx.Message != null)
        {
            dataPoints = NormalizeEvent(ctx.Message);
            if (dataPoints.TryGetValue("slot_num", out var v) && v is int i)
            {
                slotNum = i;
            }
        }

        return Task.FromResult(new AnShengEventOutcome
        {
            DataPoints = dataPoints,
            SlotNum = slotNum,
            PersistEvent = true,
            DispatchToRules = true,
            Severity = AnShengEventSeverity.Info,
        });
    }
}
