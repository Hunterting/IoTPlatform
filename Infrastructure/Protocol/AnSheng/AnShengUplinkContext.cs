using System;
using IoTPlatform.Models;

namespace IoTPlatform.Infrastructure.Protocol.AnSheng;

/// <summary>
/// 上行报文的路由归属。
/// </summary>
public enum AnShengRouteKind
{
    /// <summary>设备主动上报的事件 —— 走责任链，双出口落库。</summary>
    Event = 0,

    /// <summary>平台下发命令的应答 —— 摘在途条目，顺带刷新档案。</summary>
    Response = 1,

    /// <summary>设备周期自动上报 —— 管道内只刷档案，落库 100% 交给既有数据桥（决策 B）。</summary>
    AutoReport = 2,

    /// <summary>无法识别（method 为空 / 解析失败）—— 只记 Warn 日志，不做任何动作。</summary>
    Ignored = 3
}

/// <summary>
/// 一次上行处理的上下文。由 <see cref="AnShengUplinkPipeline"/> 组装，贯穿 Router 与全部 Handler。
///
/// 【为什么要有这个类型（而不是直接传 <see cref="AnShengUplinkEventArgs"/>）】
///   静态总线给的只有 IMEI 与报文。而事件落库还需要 <see cref="DeviceId"/>、
///   <see cref="AppCode"/>、<see cref="Profile"/> —— 这三项都要查库。
///   若让每个 Handler 各自去查，同一条报文会被查 N 次，且「AppCode 该听谁的」这条规则
///   会散落在 7 个 Handler 里。集中在 Pipeline 查一次、装进上下文，是唯一可维护的做法。
///
/// 【为什么是 record】上下文一旦组装完成即为只读事实，禁止 Handler 中途篡改。
///   需要派生新上下文时用 <c>with</c> 表达式，语义上是「另一份事实」而非「改了原来那份」。
/// </summary>
/// <param name="Imei">设备 IMEI。总线保证非空。</param>
/// <param name="Message">
/// 已解析的报文；<b>可为 null</b>——设备回了非法 JSON 时 <c>AnShengUplinkHub</c> 仍会投递，
/// 由订阅方自行降级。此时 <see cref="AnShengRouteKind.Ignored"/> 分支接管。
/// </param>
/// <param name="RawPayload">原始 JSON 报文全文，取证用。</param>
/// <param name="ReceivedAt">平台接收时刻（UTC）。</param>
public sealed record AnShengUplinkContext(
    string Imei,
    AnShengMessage? Message,
    string? RawPayload,
    DateTime ReceivedAt)
{
    /// <summary>
    /// 报文方法名。
    ///
    /// 优先取自总线事件参数（即使报文体解析失败，topic/method 通常仍可得），
    /// 其次取报文体里的 <c>method</c>。两者都没有时为空串，路由判 Ignored。
    /// </summary>
    public string Method { get; init; } = Message?.Method ?? string.Empty;

    /// <summary>帧 ID；无则为 null。</summary>
    public string? FrameId { get; init; } = Message?.FrameId;

    /// <summary>
    /// 平台设备主键；未认领设备为 <c>null</c>。
    /// 为 null 时事件仍会落库（出口①），但不会投递规则引擎（出口② 需要 deviceId）。
    /// </summary>
    public long? DeviceId { get; init; }

    /// <summary>
    /// 租户码。
    ///
    /// ★ 由 Pipeline 按 <c>Device.AppCode → Profile.AppCode → Discovered.AppCode →
    /// Options.DefaultAppCode → ""</c> 的优先级解析（设计文档 §7.2）。
    /// <b>不得</b>由 Handler 自行推断——后台线程没有租户上下文，猜错会写出跨租户脏数据。
    /// </summary>
    public string AppCode { get; init; } = string.Empty;

    /// <summary>
    /// 设备能力档案；<b>可为 null</b>。
    ///
    /// 决策 A 之后，档案的唯一创建入口是认领流程。未认领设备、
    /// 以及 T5 之前的存量设备都没有档案行，这是正常业务状态，调用方必须走降级分支。
    /// </summary>
    public AnShengDeviceProfile? Profile { get; init; }

    /// <summary>设备原始时间戳转 UTC；报文未带或无法解析时为 null。</summary>
    public DateTime? DeviceTimestampUtc => Message?.TimestampUtc;

    /// <summary>报文是否可用（非 null 且 method 非空）。为 false 时路由判 Ignored。</summary>
    public bool HasUsableMessage => Message != null && !string.IsNullOrWhiteSpace(Method);
}

/// <summary>
/// 一次路由判定的结果。
/// </summary>
/// <param name="Kind">判定归属。</param>
/// <param name="Imei">设备 IMEI（回填，便于日志与断言直接取用）。</param>
/// <param name="Method">报文方法名。</param>
/// <param name="FrameId">帧 ID，可为 null。</param>
/// <param name="Reason">
/// 判定依据的人类可读说明。
/// 【为什么这不是可有可无的注释字段】三分支判据互相牵制（硬白名单 &gt; 软白名单 &gt; 在途 frameId），
/// 线上排查「这条报文为什么没触发告警」时，没有 Reason 就只能靠读代码反推。
/// </param>
public sealed record AnShengRouteResult(
    AnShengRouteKind Kind,
    string Imei,
    string Method,
    string? FrameId,
    string Reason)
{
    /// <summary>是否为事件分支。</summary>
    public bool IsEvent => Kind == AnShengRouteKind.Event;
}
