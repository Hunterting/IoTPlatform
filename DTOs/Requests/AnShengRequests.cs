using System.ComponentModel.DataAnnotations;

namespace IoTPlatform.DTOs.Requests;

/// <summary>
/// 安圣设备认领请求
/// </summary>
public class ClaimAnShengDeviceRequest
{
    /// <summary>待认领设备 ID</summary>
    [Required]
    public long DiscoveredDeviceId { get; set; }

    /// <summary>设备名称</summary>
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>所属区域 ID（可选）</summary>
    public long? AreaId { get; set; }

    /// <summary>所属项目 ID（可选）</summary>
    public long? ProjectId { get; set; }

    /// <summary>协议配置 ID</summary>
    [Required]
    public long ProtocolConfigId { get; set; }

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
