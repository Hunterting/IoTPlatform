using System;
using System.ComponentModel.DataAnnotations;
using IoTPlatform.Data;

namespace IoTPlatform.Models;

/// <summary>
/// 安圣设备事件类型。
///
/// 【为什么以 int 落库】
///   MySQL 5.7.26 下不用原生 <c>ENUM</c>——新增枚举值就得 <c>ALTER TABLE</c> 锁表，
///   且 Pomelo 对 ENUM 的映射在跨版本升级时并不稳定。
///   沿用 <see cref="AnShengKindSource"/> / <see cref="AnShengProbeStatus"/> 的既有范式。
///
/// 【枚举值一经发布不得重排】数值即数据库里的存量数据，改序号等于改历史。
/// </summary>
public enum AnShengEventKind
{
    /// <summary>未识别（防御性默认值，正常链路不应出现）。</summary>
    Unknown = 0,

    /// <summary>设备上线（<c>connected</c>）。</summary>
    Connected = 1,

    /// <summary>设备遗嘱下线（<c>close</c>）。</summary>
    Close = 2,

    /// <summary>按键事件（<c>keyEvent</c>）。</summary>
    Key = 3,

    /// <summary>延时任务到期（<c>delayEvent</c>）。</summary>
    Delay = 4,

    /// <summary>定时任务到期（<c>timeEvent</c>）。</summary>
    Time = 5,

    /// <summary>RS485 透传上行（<c>recv485</c>）。</summary>
    Recv485 = 6,

    /// <summary>SIM 卡状态异常（<c>simCheck</c>，无在途 frameId 时按事件处理）。</summary>
    SimCheck = 7
}

/// <summary>
/// 事件严重级别。
///
/// 【为什么独立于 <see cref="AnShengEventKind"/>】
///   同一 Kind 未来可能按报文内容分级（例如 <c>simCheck</c> 的不同错误码对应
///   Warning / Critical）。提前留一列，比后加迁移便宜得多。
/// </summary>
public enum AnShengEventSeverity
{
    /// <summary>常规事件，仅供追溯。</summary>
    Info = 0,

    /// <summary>需要关注（如设备遗嘱下线、SIM 异常）。</summary>
    Warning = 1,

    /// <summary>严重故障，应立即处置。</summary>
    Critical = 2
}

/// <summary>
/// 安圣设备事件溯源表（T6 决策 3）。
///
/// 【定位】它是「事件旁路」的出口①。出口② 是 <c>IDataCollectionService</c>（规则引擎）。
///   两个出口互不回滚：规则引擎投递失败不应导致事件历史丢失，
///   失败原因写 <see cref="DispatchError"/> 留痕，可离线重放。
///
/// 【MySQL 5.7.26 兼容】
///   · 两个枚举列一律 <c>int</c>（<c>HasConversion&lt;int&gt;()</c>），禁用原生 ENUM；
///   · 不使用 CHECK 约束（5.7 静默忽略，制造「以为有校验」的假象），校验放应用层；
///   · <see cref="PayloadJson"/> / <see cref="RawJson"/> 用 <c>longtext</c>，只存不查、不进索引；
///   · 时间列统一 <c>datetime(6)</c> 存 UTC，禁 <c>timestamp</c>（2038 问题 + 时区隐式转换）。
///
/// 【多租户 ★ 最容易踩】
///   本类实现 <see cref="IHasAppCode"/>，会被 <c>AppDbContext.ConfigureGlobalQueryFilters</c>
///   自动追加 <c>WHERE AppCode = @current</c>。但管道跑在 MQTT 接收线程 / <c>Task.Run</c>，
///   <c>ITenantContextAccessor.Current</c> 为 null ⇒ 过滤器<b>不生效</b>。
///   因此写入路径必须<b>显式赋值</b> <see cref="AppCode"/>，EF 不会替你填。
/// </summary>
public class AnShengDeviceEvent : IHasAppCode
{
    /// <summary>主键。</summary>
    public long Id { get; set; }

    /// <summary>
    /// 租户码。后台线程写入路径上必须显式赋值，解析优先级见设计文档 §7.2：
    /// <c>Device.AppCode → AnShengDeviceProfile.AppCode → DiscoveredAnShengDevice.AppCode → 默认租户 → ""</c>。
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string AppCode { get; set; } = string.Empty;

    /// <summary>设备 IMEI。事件的唯一稳定标识——未认领设备也有 IMEI。</summary>
    [Required]
    [MaxLength(32)]
    public string Imei { get; set; } = string.Empty;

    /// <summary>
    /// 平台设备主键；<b>可空</b>。
    ///
    /// 【为什么允许 null】未认领设备（尤其 <c>connected</c> / <c>close</c>）同样要留事件痕迹，
    /// 不能因为「还没认领」就把事件丢掉——那恰恰是排查「设备为什么认领不上」最需要的证据。
    /// </summary>
    public long? DeviceId { get; set; }

    /// <summary>原始 method，保真存储（不做归一化，便于与设备日志逐字比对）。</summary>
    [Required]
    [MaxLength(32)]
    public string Method { get; set; } = string.Empty;

    /// <summary>事件类型（int 落库）。</summary>
    public AnShengEventKind Kind { get; set; } = AnShengEventKind.Unknown;

    /// <summary>严重级别（int 落库）。</summary>
    public AnShengEventSeverity Severity { get; set; } = AnShengEventSeverity.Info;

    /// <summary>位路号；无位路概念的事件（如 <c>connected</c>）为 null。</summary>
    public int? SlotNum { get; set; }

    /// <summary>帧 ID；<c>delayEvent</c> / <c>recv485</c> 可能携带，其余多为 null。</summary>
    [MaxLength(64)]
    public string? FrameId { get; set; }

    /// <summary>
    /// 事件发生时刻（业务时间轴，UTC）。
    ///
    /// 【取值规则 —— 验收 #4 的判据】
    /// <code>
    /// OccurredAt = DeviceTimestampUtc ?? ReceivedAt
    /// 条件：DeviceTimestampUtc 非 null 且落在 [ReceivedAt - 24h, ReceivedAt + 5min] 区间内
    /// 否则：回退 ReceivedAt，并在 PayloadJson 里打 "ts_fallback": true
    /// </code>
    /// 安圣设备时钟漂移是已知现象。事件时间轴若被脏时间戳污染，
    /// 运维时间线与 DataRule 的时间窗告警都会失真。
    /// <b>宁可回退到平台时间，也不写入不可信的业务时间。</b>
    /// </summary>
    public DateTime OccurredAt { get; set; }

    /// <summary>设备原始 timestamp 转 UTC；设备未上报或无法解析时为 null。用于诊断时钟漂移。</summary>
    public DateTime? DeviceTimestampUtc { get; set; }

    /// <summary>平台收到该报文的时刻（UTC）。用于链路延迟分析。</summary>
    public DateTime ReceivedAt { get; set; }

    /// <summary>归一化后的数据点快照（JSON）。供人看与规则引擎复现，<c>longtext</c>。</summary>
    public string? PayloadJson { get; set; }

    /// <summary>原始报文全文。取证与未来重放用，<c>longtext</c>。</summary>
    public string? RawJson { get; set; }

    /// <summary>出口② 是否成功投递到规则引擎。双出口必须可观测。</summary>
    public bool DispatchedToRules { get; set; }

    /// <summary>出口② 投递失败原因（已截断至 512 字符）。成功时为 null。</summary>
    [MaxLength(512)]
    public string? DispatchError { get; set; }

    /// <summary>记录创建时刻（UTC）。</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary><see cref="DispatchError"/> 的列长上限，供写入方截断时复用。</summary>
    public const int DispatchErrorMaxLength = 512;

    /// <summary>
    /// 依据 §3.1 的合理性校验规则计算 <see cref="OccurredAt"/>。
    ///
    /// 把规则收敛成一个纯函数放在实体上，而不是散落在 7 个 Handler 里——
    /// 时间语义只能有一个权威定义。
    /// </summary>
    /// <param name="deviceTimestampUtc">设备原始时间戳（UTC），可为 null。</param>
    /// <param name="receivedAt">平台接收时刻（UTC）。</param>
    /// <param name="usedFallback">出参：true 表示设备时间不可信、已回退到平台时间。</param>
    /// <returns>可信的事件发生时刻（UTC）。</returns>
    public static DateTime ResolveOccurredAt(
        DateTime? deviceTimestampUtc,
        DateTime receivedAt,
        out bool usedFallback)
    {
        // 允许「设备时钟落后 24 小时」——断网重连后补报是常见场景；
        // 只允许「超前 5 分钟」——未来时间几乎必然是时钟错误而非真实业务。
        var lowerBound = receivedAt.AddHours(-24);
        var upperBound = receivedAt.AddMinutes(5);

        if (deviceTimestampUtc.HasValue &&
            deviceTimestampUtc.Value >= lowerBound &&
            deviceTimestampUtc.Value <= upperBound)
        {
            usedFallback = false;
            return deviceTimestampUtc.Value;
        }

        usedFallback = true;
        return receivedAt;
    }
}
