using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IoTPlatform.Infrastructure.Protocol.AnSheng;
using IoTPlatform.Models;

namespace IoTPlatform.Services.Interfaces;

/// <summary>
/// 安圣事件责任链节点。
///
/// 【为什么用接口 + DI 集合注入，而不是 MediatR】
///   7 个事件各自一个 Handler，天然按 <see cref="Method"/> 做 O(1) 索引分发
///   （见 <see cref="AnShengEventDispatcher"/>），不值得为这点事引一个框架。
///   DI 的 <c>IEnumerable&lt;IAnShengEventHandler&gt;</c> 自动收集全部注册，
///   新增事件类型只要加一个 Handler 类 + 一行注册，零改分发器。
/// </summary>
public interface IAnShengEventHandler
{
    /// <summary>
    /// 本 Handler 负责的方法名（<c>connected</c> / <c>keyEvent</c> / …）。
    /// 用于 <see cref="AnShengEventDispatcher"/> 建索引；<b>必须唯一</b>。
    /// </summary>
    string Method { get; }

    /// <summary>
    /// 处理一条上行事件。
    /// </summary>
    /// <param name="ctx">已组装完成的上行上下文。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>本 Handler 的双出口行为描述。</returns>
    Task<AnShengEventOutcome> HandleAsync(AnShengUplinkContext ctx, CancellationToken ct);
}

/// <summary>
/// 单个事件 Handler 的「双出口」行为描述。
///
/// 由 <see cref="AnShengEventHandlerBase.HandleAsync"/> 的模板方法统一消费：
///   1. 若 <see cref="DataPoints"/> 为空，由基类调 <see cref="AnShengDataNormalizer"/> 生成；
///   2. <see cref="PersistEvent"/> 为真 → 写 <see cref="AnShengDeviceEvent"/>（出口①）；
///   3. <see cref="DispatchToRules"/> 为真且有 <c>DeviceId</c> → 投递 <c>IDataCollectionService</c>（出口②）。
///
/// 【为什么没有 Kind 字段】
///   事件类型由 <see cref="AnShengUplinkContext.Method"/> 唯一决定（见基类
///   <c>MethodToKind</c> 映射），不随每个 Handler 各自声明，避免「同一个方法两种 Kind」的漂移。
/// </summary>
public sealed record AnShengEventOutcome
{
    /// <summary>是否写入事件溯源表（出口①）。默认 true。</summary>
    public bool PersistEvent { get; init; } = true;

    /// <summary>是否投递规则引擎（出口②）。默认 true。</summary>
    public bool DispatchToRules { get; init; } = true;

    /// <summary>事件严重级别（int 落库）。默认 Info。</summary>
    public AnShengEventSeverity Severity { get; init; } = AnShengEventSeverity.Info;

    /// <summary>位路号；无位路概念的事件（如 <c>connected</c>）为 null。</summary>
    public int? SlotNum { get; init; }

    /// <summary>
    /// 归一化数据点；为 null 时由基类统一调用 <see cref="AnShengDataNormalizer"/>。
    /// 子类若已解析（如需要读取 <c>slot_num</c>），可直接返回以省一次归一化。
    /// </summary>
    public IDictionary<string, object?>? DataPoints { get; init; }

    /// <summary>备注（仅用于调试/日志，不落库）。</summary>
    public string? Note { get; init; }
}
