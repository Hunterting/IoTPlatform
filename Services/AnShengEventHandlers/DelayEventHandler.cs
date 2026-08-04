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
/// <c>delayEvent</c>（延时到期）事件 Handler。
///
/// 业务动作：解析位路号 <c>slot_num</c>，连同展平的 <c>slot{n}_state</c> 快照落库 / 投递规则引擎。
///
/// 【T9 待办（设计 §8.7 W6）】延时任务的「镜像」（哪一路、何时到期、剩余时长）更新属 T9，
/// 本 Handler 仅留 TODO 锚点，不做任务状态维护。
/// </summary>
public sealed class DelayEventHandler : AnShengEventHandlerBase
{
    /// <summary>构造 Handler。</summary>
    public DelayEventHandler(
        AnShengDataNormalizer normalizer,
        IDataCollectionService collector,
        AppDbContext db,
        ILogger<DelayEventHandler> logger)
        : base(normalizer, collector, db, logger)
    {
    }

    /// <inheritdoc />
    public override string Method => "delayEvent";

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

        // TODO(W6/T9): 延时任务镜像更新 —— 记录 scheduled task 的状态，待 T9 落位。
        return Task.FromResult(new AnShengEventOutcome
        {
            DataPoints = dataPoints,
            SlotNum = slotNum,
            PersistEvent = true,
            DispatchToRules = true,
            Severity = AnShengEventSeverity.Info,
            Note = "delayEvent 任务镜像见待办 W6（T9）",
        });
    }
}
