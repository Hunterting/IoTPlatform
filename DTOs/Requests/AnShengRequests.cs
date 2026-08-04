using IoTPlatform.Infrastructure.Protocol.AnSheng;
using System.ComponentModel.DataAnnotations;

namespace IoTPlatform.DTOs.Requests;

/// <summary>
/// 安圣设备认领请求。
///
/// 【T5 契约变更说明】
///   1. <see cref="DiscoveredDeviceId"/> 与 <see cref="ProtocolConfigId"/> 由必填改为可空。
///      改动原因：新增了「按 IMEI 认领」这条路径——运维从设备铭牌上抄 IMEI 直接认领，
///      不必先去待认领池里翻页找主键。二者<b>至少提供其一</b>，
///      具体校验放在服务层（<c>AnShengDiscoveryService.ClaimAsync</c>）而不是 DataAnnotations，
///      因为「A 或 B 至少填一个」这种跨字段规则用特性表达既啰嗦又不好给出准确错误码。
///   2. 新增 <see cref="Kind"/>：允许运维在认领时<b>直接指定品类</b>。
///      一旦指定，其权威高于任何自动推断（<c>KindSource = Manual</c>），
///      后续上行自学习也不会把它改回去。
/// </summary>
public class ClaimAnShengDeviceRequest
{
    /// <summary>
    /// 待认领设备 ID。与 <see cref="Imei"/> 至少提供其一，同时提供时以本字段为准。
    /// </summary>
    public long? DiscoveredDeviceId { get; set; }

    /// <summary>
    /// 待认领设备 IMEI。当 <see cref="DiscoveredDeviceId"/> 未提供时使用。
    /// </summary>
    [MaxLength(50)]
    public string? Imei { get; set; }

    /// <summary>设备名称</summary>
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 人工指定的设备品类。
    /// <see cref="AnShengDeviceKind.Unknown"/>（默认值）表示「不指定，交给探测推断」。
    /// 指定非 Unknown 值时，该品类将以 Manual 来源落档，自动推断不得覆盖。
    /// </summary>
    public AnShengDeviceKind Kind { get; set; } = AnShengDeviceKind.Unknown;

    /// <summary>所属区域 ID（可选）</summary>
    public long? AreaId { get; set; }

    /// <summary>所属项目 ID（可选）</summary>
    public long? ProjectId { get; set; }

    /// <summary>
    /// 协议配置 ID。为空时由服务层按 IMEI 所属租户的安圣协议配置自动选取。
    /// </summary>
    public long? ProtocolConfigId { get; set; }

    /// <summary>自动上报间隔（秒），0=不开启。null/默认值=30</summary>
    public int? GetDevStatusSec { get; set; }

    /// <summary>自动上报查询参数（v4.0.20+），可选</summary>
    public string? GetDevStatusQ { get; set; }
}

/// <summary>
/// 安圣命令下发请求
/// </summary>
public class AnShengCommandRequest
{
    /// <summary>安圣方法名（如 getDevStatus, setAutoReport, orderStart, orderEnd）</summary>
    [Required, MaxLength(50)]
    public string Method { get; set; } = string.Empty;

    /// <summary>命令参数（可选）</summary>
    public Dictionary<string, object?>? Parameters { get; set; }
}

/// <summary>
/// 安圣自动上报配置请求
/// </summary>
public class AnShengAutoReportRequest
{
    /// <summary>状态上报间隔（秒）</summary>
    public int? GetDevStatusSec { get; set; } = 60;

    /// <summary>额外查询参数</summary>
    [MaxLength(255)]
    public string? GetDevStatusQ { get; set; }

    /// <summary>订单进度上报间隔（秒）</summary>
    public int? OrderUpSec { get; set; } = 300;

    /// <summary>RS485 轮询间隔（秒），0=关闭</summary>
    public int? Rs485Sec { get; set; } = 0;
}

// 注：SwitchControlRequest / SwitchStatusQueryRequest / SwitchConfigRequest 已删除。
// 它们服务于官方协议 asopen.md 中不存在的 setSwitch / getSwitchStatus / setSwitchConfig /
// getSwitchConfig 四个臆造方法。开关通断请改用 AnShengCommandRequest：
//   { "method": "action",  "parameters": { "slotNum": 1, "action": "on" } }
//   { "method": "actions", "parameters": { "slotNums": [1,2], "action": "off" } }

/// <summary>
/// 单插槽开关动作请求（T8）。经 <c>AnShengCommandService.SendCommandAsync("action", ...)</c> 下发，
/// 复用 T7 单点校验 / 在途登记 / 记录落库，零自造下发通道。
/// </summary>
public class AnShengActionRequest
{
    /// <summary>插槽编号，从 1 开始；<c>0</c> 表示所有插槽。</summary>
    public int SlotNum { get; set; }

    /// <summary>动作：<c>on</c> / <c>off</c> / <c>toggle</c>。</summary>
    public string Action { get; set; } = "on";

    /// <summary>是否同时停止延时任务，可为 null（不下发该字段）。</summary>
    public bool? HasStopDelayTask { get; set; }
}

/// <summary>
/// 多插槽开关动作请求（T8）。构造 <c>{"method":"actions","slotNums":[...],"action":"..."}</c> 数组报文。
/// </summary>
public class AnShengActionsRequest
{
    /// <summary>插槽编号数组，子项从 1 开始，非空。</summary>
    public int[] SlotNums { get; set; } = Array.Empty<int>();

    /// <summary>动作：<c>on</c> / <c>off</c> / <c>toggle</c>。</summary>
    public string Action { get; set; } = "on";

    /// <summary>是否同时停止延时任务，可为 null。</summary>
    public bool? HasStopDelayTask { get; set; }
}

/// <summary>
/// 开始延时任务请求（T8）。经 <c>SendCommandAsync("startDelayTask", ...)</c> 下发。
/// </summary>
public class AnShengStartDelayTaskRequest
{
    /// <summary>插槽编号，从 1 开始；<c>0</c> 表示所有插槽。</summary>
    public int SlotNum { get; set; }

    /// <summary>是否启用。</summary>
    public bool Enable { get; set; }

    /// <summary>开始动作：<c>on</c> / <c>off</c> / <c>toggle</c> / <c>none</c>。</summary>
    public string SAction { get; set; } = "on";

    /// <summary>结束动作：<c>on</c> / <c>off</c> / <c>toggle</c>。</summary>
    public string EAction { get; set; } = "off";

    /// <summary>延时秒数，&gt; 0。</summary>
    public int Secs { get; set; }
}

/// <summary>
/// 停止延时任务请求（T8）。经 <c>SendCommandAsync("stopDelayTask", { slotNum })</c> 下发。
/// </summary>
public class AnShengStopDelayTaskRequest
{
    /// <summary>插槽编号，从 1 开始。</summary>
    public int SlotNum { get; set; }
}
