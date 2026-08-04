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
/// 业务动作：解析位路号 <c>slot_num</c> 及同帧的 <c>slots[]</c> 快照，
/// 调 <see cref="IAnShengScheduleService.ApplyDelayEventAsync"/> 把对应插槽镜像置
/// <c>Enable=false</c> 并刷新 <see cref="AnShengDeviceProfile.SlotsSnapshot"/>（验收 #4）。
///
/// 沿用 T6 双出口（出口①落 <see cref="AnShengDeviceEvent"/>、出口②投规则引擎），
/// 仅把原 <c>TODO(W6/T9)</c> 锚点替换为真实镜像写回（设计 D-C）。
/// </summary>
public sealed class DelayEventHandler : AnShengEventHandlerBase
{
    /// <summary>
    /// 延时任务调度服务（T8）。<b>可空</b>：仅当 <see cref="IAnShengScheduleService"/> 已注册时注入；
    /// 该服务已在 <c>Program.cs</c> 注册为 Scoped，正常运行时非空。
    /// </summary>
    private readonly IAnShengScheduleService? _schedule;

    /// <summary>构造 Handler。</summary>
    public DelayEventHandler(
        AnShengDataNormalizer normalizer,
        IDataCollectionService collector,
        AppDbContext db,
        ILogger<DelayEventHandler> logger,
        IAnShengScheduleService? schedule = null)
        : base(normalizer, collector, db, logger)
    {
        _schedule = schedule;
    }

    /// <inheritdoc />
    public override string Method => "delayEvent";

    /// <inheritdoc />
    protected override async Task<AnShengEventOutcome> OnHandleAsync(
        AnShengUplinkContext ctx, CancellationToken cancellationToken)
    {
        int? slotNum = null;
        List<int>? slots = null;
        IDictionary<string, object?>? dataPoints = null;

        if (ctx.Message != null)
        {
            dataPoints = NormalizeEvent(ctx.Message);
            if (dataPoints.TryGetValue("slot_num", out var v) && v is int i)
            {
                slotNum = i;
            }

            // 同帧的 slots[] 已由 AnShengDataNormalizer 原样透传为 List<int>，
            // 直接取出即可，无需二次解析报文体。
            if (dataPoints.TryGetValue("slots", out var slotsObj) && slotsObj is List<int> sl)
            {
                slots = sl;
            }
        }

        // 延时任务镜像写回（验收 #4，设计 D-C）：
        // delayEvent 表示该插槽的延时任务已执行完毕并自行结束，
        // 由调度服务把对应行 Enable=false 并刷新插槽快照。
        if (_schedule != null && ctx.DeviceId.HasValue)
        {
            await _schedule.ApplyDelayEventAsync(ctx.DeviceId.Value, slotNum ?? 0, slots, cancellationToken);
        }

        return new AnShengEventOutcome
        {
            DataPoints = dataPoints,
            SlotNum = slotNum,
            PersistEvent = true,
            DispatchToRules = true,
            Severity = AnShengEventSeverity.Info,
            Note = "delayEvent 触发的延时任务镜像更新（Enable=false + SlotsSnapshot 刷新）",
        };
    }
}
