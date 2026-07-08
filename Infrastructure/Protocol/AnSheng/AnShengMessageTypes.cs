using System.Text.Json.Serialization;

namespace IoTPlatform.Infrastructure.Protocol.AnSheng;

/// <summary>
/// 安圣 MQTT 消息通用结构
/// </summary>
public class AnShengMessage
{
    /// <summary>方法名：getDevStatus, orderStart, orderEnd, orderUp, close, setAutoReport, getDevInfo 等</summary>
    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    /// <summary>结果状态：success / fail</summary>
    [JsonPropertyName("result")]
    public string? Result { get; set; }

    /// <summary>设备 IMEI</summary>
    [JsonPropertyName("imei")]
    public string Imei { get; set; } = string.Empty;

    /// <summary>消息帧 ID（用于请求-响应关联）</summary>
    [JsonPropertyName("frameId")]
    public string? FrameId { get; set; }

    /// <summary>Unix 毫秒时间戳</summary>
    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }

    /// <summary>参数体（根据 method 不同而变化）</summary>
    [JsonPropertyName("param")]
    public System.Text.Json.JsonElement? Param { get; set; }
}

/// <summary>
/// getDevStatus 响应的 param 结构
/// </summary>
public class AnShengDevStatus
{
    /// <summary>设备温度（℃）</summary>
    [JsonPropertyName("temperature")]
    public double? Temperature { get; set; }

    /// <summary>插槽数量</summary>
    [JsonPropertyName("slots")]
    public int? Slots { get; set; }

    /// <summary>网络类型：WIFI / 4G / NB-IoT</summary>
    [JsonPropertyName("netType")]
    public string? NetType { get; set; }

    /// <summary>设备型号</summary>
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    /// <summary>固件版本</summary>
    [JsonPropertyName("fwVer")]
    public string? FwVer { get; set; }

    /// <summary>信号强度（dBm）</summary>
    [JsonPropertyName("signal")]
    public int? Signal { get; set; }

    /// <summary>电量计量数据数组（每插槽一项）</summary>
    [JsonPropertyName("EMdata")]
    public List<AnShengEmData>? EmData { get; set; }

    /// <summary>继电器状态（0=关, 1=开）</summary>
    [JsonPropertyName("relay")]
    public int? Relay { get; set; }
}

/// <summary>
/// 电量计量数据（EMdata 队列元素）
/// 单相设备使用 v/c/p/e；多相设备还可能包含 vs[]/cs[]/ps[] 数组
/// </summary>
public class AnShengEmData
{
    /// <summary>电压（V）</summary>
    [JsonPropertyName("v")]
    public double? V { get; set; }

    /// <summary>电流（A）</summary>
    [JsonPropertyName("c")]
    public double? C { get; set; }

    /// <summary>功率（W）</summary>
    [JsonPropertyName("p")]
    public double? P { get; set; }

    /// <summary>累计电量（kWh）</summary>
    [JsonPropertyName("e")]
    public double? E { get; set; }

    /// <summary>功率因数</summary>
    [JsonPropertyName("pf")]
    public double? Pf { get; set; }

    /// <summary>分相电压数组（多相设备）</summary>
    [JsonPropertyName("vs")]
    public List<double>? Vs { get; set; }

    /// <summary>分相电流数组（多相设备）</summary>
    [JsonPropertyName("cs")]
    public List<double>? Cs { get; set; }

    /// <summary>分相功率数组（多相设备）</summary>
    [JsonPropertyName("ps")]
    public List<double>? Ps { get; set; }
}

/// <summary>
/// 订单消息 param 结构（orderStart / orderEnd / orderUp）
/// </summary>
public class AnShengOrderData
{
    /// <summary>订单序列号</summary>
    [JsonPropertyName("sn")]
    public string? Sn { get; set; }

    /// <summary>插槽编号（1-based）</summary>
    [JsonPropertyName("order")]
    public int? Order { get; set; }

    /// <summary>状态码</summary>
    [JsonPropertyName("state")]
    public int? State { get; set; }

    /// <summary>触发原因（app/manual/auto）</summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    /// <summary>当前功率（W）</summary>
    [JsonPropertyName("p")]
    public double? P { get; set; }

    /// <summary>累计电量（kWh）</summary>
    [JsonPropertyName("e")]
    public double? E { get; set; }

    /// <summary>已计时长（秒）</summary>
    [JsonPropertyName("timing")]
    public int? Timing { get; set; }

    /// <summary>限制时长（秒）</summary>
    [JsonPropertyName("limit")]
    public int? Limit { get; set; }
}

/// <summary>
/// 设备基础信息（getDevInfo 响应）
/// </summary>
public class AnShengDevInfo
{
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("fwVer")]
    public string? FwVer { get; set; }

    [JsonPropertyName("netType")]
    public string? NetType { get; set; }

    [JsonPropertyName("slots")]
    public int? Slots { get; set; }
}

/// <summary>
/// 消息类别枚举，便于路由到不同的处理逻辑
/// </summary>
public enum AnShengMessageCategory
{
    /// <summary>未知类型</summary>
    Unknown,

    /// <summary>设备状态数据（getDevStatus 响应）</summary>
    DevStatus,

    /// <summary>设备基础信息（getDevInfo 响应）</summary>
    DevInfo,

    /// <summary>订单开始（orderStart）</summary>
    OrderStart,

    /// <summary>订单结束（orderEnd）</summary>
    OrderEnd,

    /// <summary>订单进度上报（orderUp 定时推送）</summary>
    OrderUp,

    /// <summary>设备离线（Will message）</summary>
    Close,

    /// <summary>通用命令响应（除上述 method 外）</summary>
    CommandResponse
}
