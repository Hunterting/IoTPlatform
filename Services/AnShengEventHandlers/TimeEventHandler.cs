using System.Collections.Generic;
using System.Text.Json;
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
/// <c>timeEvent</c>（定时到期）事件 Handler（T10）。
///
/// 业务动作：解析位路号 <c>slot_num</c>、定时任务索引 <c>task_index</c>，连同设备权威的
/// <c>task</c> 对象，调 <see cref="IAnShengScheduleService.ApplyTimeEventAsync"/> 做<b>就地更新</b>
/// （验收 #4）。<b>不额外发任何命令</b>——timeEvent 是设备对已执行定时任务的主动上报。
///
/// 沿用 T6 双出口（出口①落 <see cref="AnShengDeviceEvent"/>、出口②投规则引擎），
/// 仅把原 <c>TODO(W6/T10)</c> 锚点替换为真实镜像写回。
/// </summary>
public sealed class TimeEventHandler : AnShengEventHandlerBase
{
    /// <summary>
    /// 定时任务调度服务（T10）。<b>可空</b>：仅当 <see cref="IAnShengScheduleService"/> 已注册时注入；
    /// 该服务已在 <c>Program.cs</c> 注册为 Scoped，正常运行时非空。
    /// </summary>
    private readonly IAnShengScheduleService? _schedule;

    /// <summary>构造 Handler。</summary>
    public TimeEventHandler(
        AnShengDataNormalizer normalizer,
        IDataCollectionService collector,
        AppDbContext db,
        ILogger<TimeEventHandler> logger,
        IAnShengScheduleService? schedule = null)
        : base(normalizer, collector, db, logger)
    {
        _schedule = schedule;
    }

    /// <inheritdoc />
    public override string Method => "timeEvent";

    /// <inheritdoc />
    protected override async Task<AnShengEventOutcome> OnHandleAsync(
        AnShengUplinkContext ctx, CancellationToken cancellationToken)
    {
        int? slotNum = null;
        int? taskIndex = null;
        List<int>? slots = null;
        IDictionary<string, object?>? dataPoints = null;
        AnShengTimeEventTask? task = null;

        if (ctx.Message != null)
        {
            dataPoints = NormalizeEvent(ctx.Message);
            if (dataPoints.TryGetValue("slot_num", out var sv) && sv is int si)
            {
                slotNum = si;
            }

            if (dataPoints.TryGetValue("task_index", out var ti) && ti is int tii)
            {
                taskIndex = tii;
            }

            // 同帧的 slots[] 已由 AnShengDataNormalizer 原样透传为 List<int>。
            if (dataPoints.TryGetValue("slots", out var slotsObj) && slotsObj is List<int> sl)
            {
                slots = sl;
            }

            // 设备权威的定时任务真值（task 对象）：直接从报文体解析，最稳。
            task = ParseTaskObject(ctx.Message);
        }

        // timeEvent 镜像就地更新（验收 #4）：按 (slotNum, kind, taskIndex) 定位并覆盖，
        // 不额外下发命令。缺定位信息或 task 对象时跳过，不抛异常。
        if (_schedule != null && ctx.DeviceId.HasValue && slotNum.HasValue && taskIndex.HasValue && task != null)
        {
            await _schedule.ApplyTimeEventAsync(
                ctx.DeviceId.Value, slotNum.Value, taskIndex.Value, task, slots, cancellationToken);
        }

        return new AnShengEventOutcome
        {
            DataPoints = dataPoints,
            SlotNum = slotNum,
            PersistEvent = true,
            DispatchToRules = true,
            Severity = AnShengEventSeverity.Info,
            Note = "timeEvent 定时任务镜像就地更新（验收 #4）"
        };
    }

    /// <summary>
    /// 从报文体解析 <c>task</c> 对象为 <see cref="AnShengTimeEventTask"/>。
    /// 解析失败（缺字段 / 非对象）返回 null，由上层跳过写回。
    /// </summary>
    private static AnShengTimeEventTask? ParseTaskObject(AnShengMessage message)
    {
        try
        {
            var body = AnShengMessageParser.GetBodyJson(message);
            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("task", out var taskEl)
                && taskEl.ValueKind == JsonValueKind.Object)
            {
                return AnShengTimeTaskParsing.ParseTimeEventTask(taskEl);
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
