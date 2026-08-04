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
/// <c>timeEvent</c>（定时到期）事件 Handler。
///
/// 业务动作：解析位路号 <c>slot_num</c> 与定时任务索引 <c>task_index</c>，连同展平字段落库 / 投递规则引擎。
///
/// 【T10 待办（设计 §8.7 W6）】定时任务的「镜像」更新属 T10，本 Handler 仅留 TODO 锚点。
/// </summary>
public sealed class TimeEventHandler : AnShengEventHandlerBase
{
    /// <summary>构造 Handler。</summary>
    public TimeEventHandler(
        AnShengDataNormalizer normalizer,
        IDataCollectionService collector,
        AppDbContext db,
        ILogger<TimeEventHandler> logger)
        : base(normalizer, collector, db, logger)
    {
    }

    /// <inheritdoc />
    public override string Method => "timeEvent";

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

        // TODO(W6/T10): 定时任务镜像更新 —— 记录 scheduled task 的状态，待 T10 落位。
        return Task.FromResult(new AnShengEventOutcome
        {
            DataPoints = dataPoints,
            SlotNum = slotNum,
            PersistEvent = true,
            DispatchToRules = true,
            Severity = AnShengEventSeverity.Info,
            Note = "timeEvent 定时任务镜像见待办 W6（T10）",
        });
    }
}
