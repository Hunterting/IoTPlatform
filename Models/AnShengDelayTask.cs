using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using IoTPlatform.Data;

namespace IoTPlatform.Models;

/// <summary>
/// 安圣延时任务镜像（每设备每插槽一行，D6 Option A：设备权威 + 平台只读镜像）。
///
/// 【为什么每设备每插槽一行】
///   设备 <c>getDelayTasks</c> 应答的 <c>tasks[]</c> 是一个按插槽顺序的数组，下标 <c>i</c> 对应插槽 <c>i+1</c>。
///   平台镜像要能按插槽精准定位、并支持单插槽回写（<c>delayEvent</c> 只关一路），所以一行管一路。
///
/// 【租户隔离】实现 <see cref="IHasAppCode"/>，由 <c>AppDbContext</c> 全局查询过滤器自动加
///   <c>WHERE AppCode = @current</c>。但<b>上行链路（Router / Handler / 写后回读延时作用域）跑在后台作用域</b>，
///   <c>ITenantContextAccessor.Current</c> 为 null 导致过滤器不生效，因此这些路径<b>必须</b>
///   <c>IgnoreQueryFilters</c> + 显式按 <see cref="DeviceId"/>（全局唯一）定位（详见设计文档 §7.1）。
///
/// 【MySQL 5.7.26 兼容】
///   · <see cref="SAction"/> / <see cref="EAction"/> 存 varchar 字符串（on/off/toggle/none），
///     <b>不引入新持久化枚举</b>（决策 D-G）；
///   · 时间列 <c>datetime(6)</c> 存 UTC；<see cref="SyncedAt"/> 用于陈旧判定（&gt; 24h 标记 <c>IsStale</c>）。
/// </summary>
[Table("ansheng_delay_tasks")]
public class AnShengDelayTask : IHasAppCode
{
    /// <summary>内部主键。</summary>
    public long Id { get; set; }

    /// <summary>租户码。后台作用域写入路径<b>必须显式赋值</b>（全局过滤器不生效，EF 不会替你填）。</summary>
    [Required, MaxLength(50)]
    public string AppCode { get; set; } = string.Empty;

    /// <summary>关联正式设备主键；延时任务是该设备的镜像，故非空。</summary>
    public long DeviceId { get; set; }

    /// <summary>插槽编号，从 1 开始；= 设备 <c>tasks[]</c> 下标 + 1。</summary>
    public int SlotNum { get; set; }

    /// <summary>延时任务是否启用。<c>delayEvent</c> 注入时置 <c>false</c>。</summary>
    public bool Enable { get; set; }

    /// <summary>开始动作（on/off/toggle/none）。</summary>
    [MaxLength(16)]
    public string SAction { get; set; } = "none";

    /// <summary>结束动作（on/off/toggle）。</summary>
    [MaxLength(16)]
    public string EAction { get; set; } = "off";

    /// <summary>延时秒数。</summary>
    public int Secs { get; set; }

    /// <summary>任务计数（快照值，非实时）。</summary>
    public int Cnt { get; set; }

    /// <summary>镜像最后与设备同步的时刻（UTC）；陈旧判定用。</summary>
    public DateTime SyncedAt { get; set; }
}

/// <summary>
/// 一次 <c>getDelayTasks</c> 应答中单个延时任务项的只读视图（按数组下标对应插槽）。
///
/// 【不含 SlotNum】设备 <c>tasks[]</c> 不带 slotNum 字段，
///   <see cref="AnShengScheduleService"/> 在回读时按下标 +1 推导（设计文档 §7.7）。
/// </summary>
public sealed class AnShengDelayTaskItem
{
    /// <summary>是否启用。</summary>
    public bool Enable { get; init; }

    /// <summary>开始动作（on/off/toggle/none）。</summary>
    public string SAction { get; init; } = "none";

    /// <summary>结束动作（on/off/toggle）。</summary>
    public string EAction { get; init; } = "off";

    /// <summary>延时秒数。</summary>
    public int Secs { get; init; }

    /// <summary>任务计数快照。</summary>
    public int Cnt { get; init; }
}
