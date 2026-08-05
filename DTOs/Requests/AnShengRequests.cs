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

// ─────────────────────────────────────────────────────────────
// T10 定时任务请求
// ─────────────────────────────────────────────────────────────

/// <summary>
/// 整表覆盖定时任务请求（T10 POST <c>{deviceId}/time-tasks</c>）。
///
/// 经 <c>SendCommandAsync("setTimeTasks", ...)</c> 下发。语义为「整表覆盖」——
/// 平台镜像会被请求里的全部插槽替换（验收 #3 的乐观镜像来源）。
/// </summary>
public class AnShengSetTimeTasksRequest
{
    /// <summary>
    /// 二次确认开关。定时任务是高危操作（会改掉设备上的全部定时），
    /// 必须显式 <c>confirm=true</c> 才下发，否则返回业务拒绝（验收 #2）。
    /// </summary>
    public bool Confirm { get; set; }

    /// <summary>
    /// 每个插槽的完整定时任务集合（按插槽升序）。整表覆盖语义：未列出的插槽其定时任务将被清空。
    /// </summary>
    public List<AnShengSlotTimeTaskSetRequest> Slots { get; set; } = new();

    /// <summary>
    /// 乐观并发令牌。来自 <c>GET /time-tasks</c> 返回的任意行的 <c>RowVersion</c>。
    /// 提供时若与服务器当前值不一致（被他人并发修改），返回 409（验收 #5）。
    /// </summary>
    public long? RowVersion { get; set; }
}

/// <summary>单插槽定时任务集合请求体（T10，整表覆盖用）。</summary>
public class AnShengSlotTimeTaskSetRequest
{
    /// <summary>插槽编号，从 1 开始。</summary>
    public int SlotNum { get; set; }

    /// <summary>普通定时任务列表，按设备数组顺序。</summary>
    public List<AnShengTimeTaskItemRequest> TimeTasks { get; set; } = new();

    /// <summary>循环定时任务列表，按设备数组顺序。</summary>
    public List<AnShengLoopTimeTaskItemRequest> LoopTimeTasks { get; set; } = new();
}

/// <summary>单条普通定时任务请求项。</summary>
public class AnShengTimeTaskItemRequest
{
    /// <summary>设备分配的任务 id；新建时为 null。</summary>
    public string? Id { get; set; }

    /// <summary>是否启用。</summary>
    public bool Enable { get; set; }

    /// <summary>每周生效的星期几（1-7）；空数组表示仅一次。</summary>
    public List<int>? WeekDays { get; set; }

    /// <summary>动作小时（0-23）。</summary>
    public int Hour { get; set; }

    /// <summary>动作分钟（0-59）。</summary>
    public int Minute { get; set; }

    /// <summary>动作：<c>on</c> / <c>off</c> / <c>toggle</c>。</summary>
    public string Action { get; set; } = "on";

    /// <summary>任务触发时是否上报 <c>timeEvent</c>。</summary>
    public bool UploadEnable { get; set; }
}

/// <summary>单条循环定时任务请求项。</summary>
public class AnShengLoopTimeTaskItemRequest
{
    /// <summary>设备分配的任务 id；新建时为 null。</summary>
    public string? Id { get; set; }

    /// <summary>是否启用。</summary>
    public bool Enable { get; set; }

    /// <summary>每周生效的星期几（1-7）；空数组表示仅一次。</summary>
    public List<int>? WeekDays { get; set; }

    /// <summary>每天循环开始的小时。</summary>
    public int SHour { get; set; }

    /// <summary>每天循环开始的分钟。</summary>
    public int SMinute { get; set; }

    /// <summary>每天循环结束的小时。</summary>
    public int EHour { get; set; }

    /// <summary>每天循环结束的分钟。</summary>
    public int EMinute { get; set; }

    /// <summary>循环中打开的分钟数。</summary>
    public int OnMins { get; set; }

    /// <summary>循环中关闭的分钟数。</summary>
    public int OffMins { get; set; }
}

/// <summary>
/// 单插槽定时任务请求（T10 POST <c>{deviceId}/time-tasks/{slotNum}</c>）。
///
/// <c>slotNum</c> 取自路由，<b>不</b>在请求体里；<c>slotNum &lt; 1</c> 或越界（&gt; 插槽数）由
/// 控制器 / Guard 在<b>下发前</b>拦截，返回 400 且不下发（验收 #7）。
/// </summary>
public class AnShengSetSlotTimeTasksRequest
{
    /// <summary>二次确认开关（同 <see cref="AnShengSetTimeTasksRequest.Confirm"/>，验收 #2）。</summary>
    public bool Confirm { get; set; }

    /// <summary>普通定时任务列表。</summary>
    public List<AnShengTimeTaskItemRequest> TimeTasks { get; set; } = new();

    /// <summary>循环定时任务列表。</summary>
    public List<AnShengLoopTimeTaskItemRequest> LoopTimeTasks { get; set; } = new();

    /// <summary>乐观并发令牌（同 <see cref="AnShengSetTimeTasksRequest.RowVersion"/>，验收 #5）。</summary>
    public long? RowVersion { get; set; }
}

// ─────────────────────────────────────────────────────────────
// T11 电量计（实时 / 统计 / 校准）请求
// ─────────────────────────────────────────────────────────────

/// <summary>
/// 拉取电量计统计请求（T11 POST <c>{deviceId}/energy/statistics/refresh</c>）。
/// 经 <c>SendCommandAsync("getEMStatistics", { q })</c> 下发；应答由 Router 钩子 UPSERT 进聚合表。
/// </summary>
public class AnShengGetEMStatisticsRequest
{
    /// <summary>
    /// 查询串：<c>all</c> / <c>month</c> / <c>day</c> / <c>hour</c> / <c>hourSum</c> / <c>total</c>，
    /// 可用逗号组合（如 <c>total,day,hour</c>）。留空表示不带该参数、由设备返回默认集合。
    /// </summary>
    public string? Q { get; set; }
}

/// <summary>
/// 清空电量计统计请求（T11 POST <c>{deviceId}/energy/statistics/clear</c>）。
///
/// 【只清设备，不清平台】设计 D5：平台聚合表<b>只累积保留</b>，本操作仅让设备归零，
/// 并在平台落一条 <see cref="IoTPlatform.Models.AnShengEventKind.EmCleared"/> 标记事件用于对账（验收 #4）。
/// </summary>
public class AnShengClearEMStatisticsRequest
{
    /// <summary>
    /// 二次确认开关。清零是不可逆的设备侧操作（清完就找不回来了），
    /// 必须显式 <c>confirm=true</c> 才下发，与 T10 定时任务整表覆盖同一口径。
    /// </summary>
    public bool Confirm { get; set; }

    /// <summary>插槽编号，从 1 开始；null 或 0 表示清空所有插槽。</summary>
    public int? SlotNum { get; set; }
}

/// <summary>
/// 设置电量计校准参数请求（T11 POST <c>{deviceId}/energy/cal-params</c>）。仅开关类放行（验收 #6）。
/// </summary>
public class AnShengSetCalParamsRequest
{
    /// <summary>
    /// 校准电阻值 <c>RL</c>。这是协议明确列出的唯一必备校准项，
    /// 单独开一个字段是为了让最常见的调用不必构造字典。
    /// </summary>
    public double? RL { get; set; }

    /// <summary>
    /// 完整校准参数字典（键名原样出网）。与 <see cref="RL"/> 同时提供时<b>以本字典为准</b>，
    /// 但字典里缺 <c>RL</c> 而 <see cref="RL"/> 有值时会自动补入 —— 避免「填了 RL 却没发出去」。
    /// </summary>
    public Dictionary<string, double> CalParams { get; set; } = new();
}

/// <summary>
/// 自动校准请求（T11 POST <c>{deviceId}/energy/cal-params/auto</c>）。仅开关类放行（验收 #6）。
/// </summary>
public class AnShengAutoCalRequest
{
    /// <summary>已知负载功率（W）。设备据此反推校准系数，必须是真实接在插槽上的负载功率。</summary>
    public double Power { get; set; }
}
