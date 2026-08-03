using IoTPlatform.Data;
using IoTPlatform.Infrastructure.Protocol.AnSheng;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IoTPlatform.Models;

/// <summary>
/// 品类来源 —— 记录 <see cref="AnShengDeviceProfile.Kind"/> 是怎么定下来的。
///
/// 【为什么需要它】
///   品类有三条来路：设备探测、上行报文自学习、人工指定。三者可信度不同，
///   若不区分来源，一次自学习就能把运维刚手工纠正的品类覆盖回去。
///   <c>Manual</c> 具备最高权威：一旦人工指定过，任何自动推断都不得改写（见
///   <c>AnShengDeviceProfileService.ResolveKind</c> 的一级判定）。
///
/// 【持久化】以 <c>int</c> 落库（<c>HasConversion&lt;int&gt;()</c>）。
///   MySQL 5.7.26 下不使用 ENUM 类型，避免后续增枚举值必须 ALTER TABLE。
/// </summary>
public enum AnShengKindSource
{
    /// <summary>尚未确定来源（Profile 刚建、还没跑过任何推断）。</summary>
    Unknown = 0,

    /// <summary>由认领时的主动探测（getDevInfo / getDevStatus）推断得出。</summary>
    Probe = 1,

    /// <summary>由设备上行报文自学习得出（可信度低于探测）。</summary>
    Uplink = 2,

    /// <summary>由人工（管理员认领 / 后台纠正）显式指定，权威最高。</summary>
    Manual = 3
}

/// <summary>
/// 探测状态 —— 记录一次「主动问设备要能力信息」的结果。
///
/// 【与认领流程的关系】
///   认领时探测失败不会抛异常，而是把 <see cref="ProbeFailed"/> + 错误摘要落到
///   <c>discovered_ansheng_devices</c>，并立即返回，<b>不创建 Device</b>。
///   这样运维能在待认领池里直接看到「这台设备为什么认领不了」。
///
/// 【持久化】同样以 <c>int</c> 落库。
/// </summary>
public enum AnShengProbeStatus
{
    /// <summary>从未探测过。</summary>
    NotProbed = 0,

    /// <summary>探测进行中（已下发指令、还没收齐回包）。</summary>
    Probing = 1,

    /// <summary>探测成功，能力信息已回填。</summary>
    Probed = 2,

    /// <summary>探测失败（超时 / 适配器不可用 / 设备回错）。</summary>
    ProbeFailed = 3
}

/// <summary>
/// 安圣设备能力档案（Profile）—— 一台安圣二开设备「是什么、能干什么」的唯一事实源。
///
/// 【设计取舍】
///   1. <b>以 IMEI 为主键语义</b>：设备在被认领成 <c>Device</c> 之前就已经在 MQTT 上说话了，
///      此时没有 DeviceId 可用。因此 Profile 用 <see cref="Imei"/> 做唯一键，
///      <see cref="DeviceId"/> 允许为空，认领成功后再回填。
///   2. <b>不做存量回填</b>（产品决策 Q5）：本次迁移只建表不写数据。
///      也就是说，T5 之前已认领的设备其 Profile 为 <c>null</c>。
///      <b>所有读取方必须容忍 null</b> —— 返回 null / 降级校验 / 打告警，绝不允许抛 NRE。
///   3. <b>能力字段冗余存储</b>：<see cref="SlotAmount"/> / <see cref="Version"/> 等字段
///      本可从上行报文实时取，但那要求设备在线。冗余一份让「设备离线时仍能渲染能力"」成立。
///
/// 【租户隔离】实现 <see cref="IHasAppCode"/>，由 <c>AppDbContext</c> 全局查询过滤器自动加
///   <c>WHERE AppCode = @current</c>，业务代码<b>不要</b>再手写 AppCode 条件。
/// </summary>
[Table("ansheng_device_profiles")]
public class AnShengDeviceProfile : IHasAppCode
{
    /// <summary>自增主键。</summary>
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    /// <summary>
    /// 租户码。为空字符串表示「尚未归属租户」（理论上不应出现，仅作为防御性默认值）。
    /// </summary>
    [MaxLength(50)]
    public string AppCode { get; set; } = string.Empty;

    /// <summary>
    /// 设备 IMEI —— 业务唯一键。数据库上建 UNIQUE 索引，保证一台设备只有一份档案。
    /// </summary>
    [Required, MaxLength(50)]
    public string Imei { get; set; } = string.Empty;

    /// <summary>
    /// 关联的正式设备主键。认领前为 <c>null</c>，认领成功后回填。
    /// 刻意不建外键约束：Profile 的生命周期长于 Device（设备可以被删除重认领）。
    /// </summary>
    public long? DeviceId { get; set; }

    /// <summary>设备品类。<see cref="AnShengDeviceKind.Unknown"/> 表示尚未判定成功。</summary>
    public AnShengDeviceKind Kind { get; set; } = AnShengDeviceKind.Unknown;

    /// <summary>品类来源，决定后续自动推断能否覆盖 <see cref="Kind"/>。</summary>
    public AnShengKindSource KindSource { get; set; } = AnShengKindSource.Unknown;

    /// <summary>联网类型（<c>4G</c> / <c>WiFi</c>），来自 getDevInfo 或 getDevStatus。</summary>
    [MaxLength(50)]
    public string? NetType { get; set; }

    /// <summary>
    /// 插槽数量。开关类设备的<b>权威判据</b>：<c>&gt; 0</c> 即可断定为开关款，
    /// 优先级高于 version / model 的前缀猜测。喇叭款通常为 0 或不上报。
    /// </summary>
    public int? SlotAmount { get; set; }

    /// <summary>相位数量，开关类设备上报。</summary>
    public int? PhaseAmount { get; set; }

    /// <summary>固件版本号，如 <c>SWITCH-EC618X-R24-O-V4.0.8</c>。</summary>
    [MaxLength(100)]
    public string? Version { get; set; }

    /// <summary>模组型号，如 <c>Air780E</c>。</summary>
    [MaxLength(100)]
    public string? Model { get; set; }

    /// <summary>物联卡 ICCID，仅 4G 款有值。</summary>
    [MaxLength(50)]
    public string? Iccid { get; set; }

    /// <summary>信号强度 1-31。</summary>
    public int? Signal { get; set; }

    /// <summary>最近一次探测状态。</summary>
    public AnShengProbeStatus ProbeStatus { get; set; } = AnShengProbeStatus.NotProbed;

    /// <summary>最近一次探测失败原因摘要；成功时清空为 <c>null</c>。</summary>
    [MaxLength(500)]
    public string? ProbeError { get; set; }

    /// <summary>最近一次探测时间（UTC）。从未探测过为 <c>null</c>。</summary>
    public DateTime? LastProbedAt { get; set; }

    /// <summary>档案创建时间（UTC）。</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>档案最后更新时间（UTC）。</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 该档案是否已经"可用"——品类已判定出来。
    /// 供上层做能力校验时快速短路：未判定则降级放行并打告警，而不是硬拒。
    /// </summary>
    [NotMapped]
    public bool IsKindResolved => Kind != AnShengDeviceKind.Unknown;
}
