using System.Text.Json;
using System.Text.Json.Serialization;

namespace IoTPlatform.Infrastructure.Protocol.AnSheng;

/// <summary>
/// 安圣 MQTT 消息通用结构。
///
/// 二开协议约定：所有业务字段<b>平铺在 JSON 顶层</b>，不存在 <c>param</c> 包裹对象。
/// 公共字段：<c>method</c> / <c>result</c> / <c>imei</c> / <c>frameId</c> / <c>timestamp</c>。
/// </summary>
public class AnShengMessage
{
    /// <summary>方法名：getDevInfo、getDevStatus、action、connected、close 等。</summary>
    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    /// <summary>返回结果：<c>ok</c>-成功；<c>method unsupported</c>-设备不支持；其他-具体失败原因。</summary>
    [JsonPropertyName("result")]
    public string? Result { get; set; }

    /// <summary>设备 IMEI。</summary>
    [JsonPropertyName("imei")]
    public string Imei { get; set; } = string.Empty;

    /// <summary>消息帧 ID（用于请求-应答关联）。</summary>
    [JsonPropertyName("frameId")]
    public string? FrameId { get; set; }

    /// <summary>
    /// 原始 timestamp 数值（协议为<b>秒级</b>；宽松解析同时兼容毫秒与字符串数字）。
    /// WiFi 款不上报该字段，此时为 null。
    /// </summary>
    [JsonPropertyName("timestamp")]
    [JsonConverter(typeof(AnShengFlexibleInt64Converter))]
    public long? RawTimestamp { get; set; }

    /// <summary>由 <see cref="RawTimestamp"/> 归一化得到的 UTC 时间；无法解析时为 null。</summary>
    [JsonIgnore]
    public DateTime? TimestampUtc { get; set; }

    /// <summary>平台收到该报文的本地时刻（UTC）。</summary>
    [JsonIgnore]
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;

    /// <summary>原始 JSON 报文全文，供平铺字段解析与排障使用。</summary>
    [JsonIgnore]
    public string RawJson { get; set; } = string.Empty;

    /// <summary>
    /// 参数体。
    /// </summary>
    /// <remarks>
    /// 仅 Legacy 充电桩协议族（orderStart / orderEnd / orderUp / getDevStatus 旧格式）会使用该字段；
    /// 二开协议参数平铺在顶层，本字段恒为 null。请勿在新代码中使用。
    /// </remarks>
    [Obsolete("仅 Legacy 充电桩协议族使用，二开协议参数平铺在顶层")]
    [JsonPropertyName("param")]
    public JsonElement? Param { get; set; }

    /// <summary>是否为成功应答（result == "ok"）。</summary>
    [JsonIgnore]
    public bool IsOk => string.Equals(Result, AnShengCommandCatalog.ResultOk, StringComparison.OrdinalIgnoreCase);

    /// <summary>设备是否反馈「不支持该命令」。</summary>
    [JsonIgnore]
    public bool IsUnsupported => string.Equals(Result, AnShengCommandCatalog.ResultUnsupported,
        StringComparison.OrdinalIgnoreCase);

    /// <summary>是否为设备主动上报事件（含遗嘱 close）。</summary>
    [JsonIgnore]
    public bool IsEvent => AnShengCommandCatalog.IsEvent(Method);
}

/// <summary>
/// <c>getDevStatus</c> 应答（字段平铺在报文顶层）。
/// </summary>
public class AnShengDevStatus
{
    /// <summary>联网类型：<c>4G</c> / <c>WiFi</c>。</summary>
    [JsonPropertyName("netType")]
    public string? NetType { get; set; }

    /// <summary>物联卡 ICCID，4G 款支持。</summary>
    [JsonPropertyName("iccid")]
    public string? Iccid { get; set; }

    /// <summary>信号强度 1-31；4G 款建议 &gt; 10。</summary>
    [JsonPropertyName("signal")]
    public int? Signal { get; set; }

    /// <summary>温度（℃）。设备可能以字符串形式上报，如 <c>"32.4"</c>。</summary>
    [JsonPropertyName("temperature")]
    [JsonConverter(typeof(AnShengFlexibleDoubleConverter))]
    public double? Temperature { get; set; }

    /// <summary>GPS，格式：经度,纬度。</summary>
    [JsonPropertyName("gps")]
    public string? Gps { get; set; }

    /// <summary>插槽状态数组，按顺序从插槽 1 到 n；子项 <c>0</c>-关闭，<c>1</c>-打开。</summary>
    [JsonPropertyName("slots")]
    public List<int>? Slots { get; set; }

    /// <summary>插槽订单任务对象数组。</summary>
    [JsonPropertyName("tasks")]
    public List<AnShengSlotTask>? Tasks { get; set; }

    /// <summary>插槽电量计对象数组，按顺序从插槽 1 到 n。</summary>
    [JsonPropertyName("EMdata")]
    public List<AnShengEmData>? EmData { get; set; }

    /// <summary>模组型号，如 <c>Air780E</c>（部分固件上报）。</summary>
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    /// <summary>固件版本（部分固件在状态中一并上报）。</summary>
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    /// <summary>
    /// 插槽数量（部分固件在 <c>getDevStatus</c> 中也上报 <c>slotAmount</c>）。
    ///
    /// 【为什么状态报文里也要这个字段】
    ///   协议文档把 <c>slotAmount</c> 归在 <c>getDevInfo</c>，但实测固件在
    ///   <c>getDevStatus</c> 里同样会带。品类判定把「slotAmount &gt; 0」当作开关款的权威判据，
    ///   只认 getDevInfo 一处会让「设备只回了状态」的场景永远判不出品类。
    ///   这里补齐后，由 <c>AnShengDeviceProfileService.MergeSnapshot</c> 做双源合并。
    ///
    /// 【与 <see cref="SlotCount"/> 的区别】
    ///   <see cref="SlotCount"/> 是<b>推算值</b>（数 slots/EMdata 数组长度），设备不上报数组时为 0；
    ///   本字段是设备<b>显式声明</b>的数量，可信度更高，故不合并进 SlotCount 以免语义混淆。
    /// </summary>
    [JsonPropertyName("slotAmount")]
    public int? SlotAmount { get; set; }

    /// <summary>插槽数量：优先取 <see cref="Slots"/> 长度，其次取 <see cref="EmData"/> 长度。</summary>
    [JsonIgnore]
    public int SlotCount => Slots?.Count ?? EmData?.Count ?? 0;
}

/// <summary>
/// 插槽订单任务（<c>getDevStatus</c> 应答 <c>tasks</c> 数组元素）。
/// </summary>
public class AnShengSlotTask
{
    /// <summary>插槽编号，从 1 开始。</summary>
    [JsonPropertyName("slotNum")]
    public int? SlotNum { get; set; }

    /// <summary>订单类型：<c>TIME</c>-计时；<c>POWER</c>-计量。</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>订单状态：<c>idle</c>-空闲/结束；<c>working</c>-进行中。</summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>计时秒数（<c>type</c> 为 <c>TIME</c> 时有效）。</summary>
    [JsonPropertyName("timeSec")]
    public int? TimeSec { get; set; }

    /// <summary>计量电量（度，<c>type</c> 为 <c>POWER</c> 时有效）。</summary>
    [JsonPropertyName("powerKwh")]
    [JsonConverter(typeof(AnShengFlexibleDoubleConverter))]
    public double? PowerKwh { get; set; }

    /// <summary>计量最大秒数，0 为不限制。</summary>
    [JsonPropertyName("powerMaxSec")]
    public int? PowerMaxSec { get; set; }

    /// <summary>最大功率（W），超过则任务自动停止，0 为使用设备默认值。</summary>
    [JsonPropertyName("maxPower")]
    public int? MaxPower { get; set; }

    /// <summary>拔出自停开关。</summary>
    [JsonPropertyName("pullOutStop")]
    public bool? PullOutStop { get; set; }

    /// <summary>拔出自停功率阈值，0 为使用设备默认值。</summary>
    [JsonPropertyName("pullOutStopPower")]
    public int? PullOutStopPower { get; set; }

    /// <summary>订单启动后开始判断拔出自停的秒数。</summary>
    [JsonPropertyName("pullOutStopStartSec")]
    public int? PullOutStopStartSec { get; set; }

    /// <summary>充满自停开关。</summary>
    [JsonPropertyName("chargeFullStop")]
    public bool? ChargeFullStop { get; set; }

    /// <summary>充满自停功率阈值，0 为使用设备默认值。</summary>
    [JsonPropertyName("chargeFullStopPower")]
    public int? ChargeFullStopPower { get; set; }

    /// <summary>
    /// 充满自停持续秒数。
    /// 注意：协议文档参数表写作 <c>chageFullStopSec</c>（缺少 r），而应答示例为 <c>chargeFullStopSec</c>，
    /// 此处以示例为准并通过 <see cref="ChargeFullStopSecTypo"/> 兼容文档拼写。
    /// </summary>
    [JsonPropertyName("chargeFullStopSec")]
    public int? ChargeFullStopSec { get; set; }

    /// <summary>兼容协议文档中的拼写 <c>chageFullStopSec</c>。</summary>
    [JsonPropertyName("chageFullStopSec")]
    public int? ChargeFullStopSecTypo { get; set; }

    /// <summary>订单启动后开始判断充满自停的秒数。</summary>
    [JsonPropertyName("chargeFullStopStartSec")]
    public int? ChargeFullStopStartSec { get; set; }

    /// <summary>订单备注，可用于记录订单编号。</summary>
    [JsonPropertyName("remark")]
    public string? Remark { get; set; }

    /// <summary>关闭原因：CLOSED / MANUAL_CLOSED / PULL_OUT_STOP_CLOSE 等。</summary>
    [JsonPropertyName("closeReason")]
    public string? CloseReason { get; set; }

    /// <summary>总运行秒数。</summary>
    [JsonPropertyName("totalSec")]
    public int? TotalSec { get; set; }

    /// <summary>总运行度数。</summary>
    [JsonPropertyName("totalKwh")]
    [JsonConverter(typeof(AnShengFlexibleDoubleConverter))]
    public double? TotalKwh { get; set; }

    /// <summary>有效电压（V）。</summary>
    [JsonPropertyName("voltage")]
    [JsonConverter(typeof(AnShengFlexibleDoubleConverter))]
    public double? Voltage { get; set; }

    /// <summary>有效电流（A）。</summary>
    [JsonPropertyName("current")]
    [JsonConverter(typeof(AnShengFlexibleDoubleConverter))]
    public double? Current { get; set; }

    /// <summary>有效功率（W）。</summary>
    [JsonPropertyName("power")]
    [JsonConverter(typeof(AnShengFlexibleDoubleConverter))]
    public double? Power { get; set; }

    /// <summary>多相电有效电压数组（V）。</summary>
    [JsonPropertyName("vs")]
    public List<double>? Vs { get; set; }

    /// <summary>多相电有效电流数组（A）。</summary>
    [JsonPropertyName("cs")]
    public List<double>? Cs { get; set; }

    /// <summary>多相电有效功率数组（W）。</summary>
    [JsonPropertyName("ps")]
    public List<double>? Ps { get; set; }

    /// <summary>是否正在运行。</summary>
    [JsonIgnore]
    public bool IsWorking => string.Equals(Status, "working", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// 电量计数据（<c>EMdata</c> 数组元素）。
/// 单相设备使用 v/c/p/e；多相设备还可能包含 vs[]/cs[]/ps[] 数组。
/// </summary>
public class AnShengEmData
{
    /// <summary>有效电压（V）。</summary>
    [JsonPropertyName("v")]
    [JsonConverter(typeof(AnShengFlexibleDoubleConverter))]
    public double? V { get; set; }

    /// <summary>有效电流（A）。</summary>
    [JsonPropertyName("c")]
    [JsonConverter(typeof(AnShengFlexibleDoubleConverter))]
    public double? C { get; set; }

    /// <summary>有效功率（W）。</summary>
    [JsonPropertyName("p")]
    [JsonConverter(typeof(AnShengFlexibleDoubleConverter))]
    public double? P { get; set; }

    /// <summary>插槽总运行度数（kWh）。</summary>
    [JsonPropertyName("e")]
    [JsonConverter(typeof(AnShengFlexibleDoubleConverter))]
    public double? E { get; set; }

    /// <summary>功率因数（部分固件上报）。</summary>
    [JsonPropertyName("pf")]
    [JsonConverter(typeof(AnShengFlexibleDoubleConverter))]
    public double? Pf { get; set; }

    /// <summary>分相电压数组（多相设备）。</summary>
    [JsonPropertyName("vs")]
    public List<double>? Vs { get; set; }

    /// <summary>分相电流数组（多相设备）。</summary>
    [JsonPropertyName("cs")]
    public List<double>? Cs { get; set; }

    /// <summary>分相功率数组（多相设备）。</summary>
    [JsonPropertyName("ps")]
    public List<double>? Ps { get; set; }
}

/// <summary>
/// Legacy 充电桩协议族的订单数据（orderStart / orderEnd / orderUp 的 <c>param</c> 结构）。
/// 该结构不属于二开协议，仅为兼容既有充电桩链路保留。
/// </summary>
public class AnShengOrderData
{
    /// <summary>订单序列号。</summary>
    [JsonPropertyName("sn")]
    public string? Sn { get; set; }

    /// <summary>插槽编号（1-based）。</summary>
    [JsonPropertyName("order")]
    public int? Order { get; set; }

    /// <summary>状态码。</summary>
    [JsonPropertyName("state")]
    public int? State { get; set; }

    /// <summary>触发原因（app/manual/auto）。</summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    /// <summary>当前功率（W）。</summary>
    [JsonPropertyName("p")]
    [JsonConverter(typeof(AnShengFlexibleDoubleConverter))]
    public double? P { get; set; }

    /// <summary>累计电量（kWh）。</summary>
    [JsonPropertyName("e")]
    [JsonConverter(typeof(AnShengFlexibleDoubleConverter))]
    public double? E { get; set; }

    /// <summary>已计时长（秒）。</summary>
    [JsonPropertyName("timing")]
    public int? Timing { get; set; }

    /// <summary>限制时长（秒）。</summary>
    [JsonPropertyName("limit")]
    public int? Limit { get; set; }
}

/// <summary>
/// <c>getDevInfo</c> 应答（字段平铺在报文顶层）。
/// </summary>
public class AnShengDevInfo
{
    /// <summary>固件版本号，如 <c>SWITCH-EC618X-R24-O-V4.0.8</c>。</summary>
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    /// <summary>插槽数量，开关类设备支持。</summary>
    [JsonPropertyName("slotAmount")]
    public int? SlotAmount { get; set; }

    /// <summary>相位数量，开关类设备支持。</summary>
    [JsonPropertyName("phaseAmount")]
    public int? PhaseAmount { get; set; }

    /// <summary>模组型号（部分固件上报，非协议必备字段）。</summary>
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    /// <summary>联网类型（部分固件上报，非协议必备字段）。</summary>
    [JsonPropertyName("netType")]
    public string? NetType { get; set; }

    /// <summary>
    /// 物联卡 ICCID（部分 4G 固件在 <c>getDevInfo</c> 中一并上报）。
    ///
    /// 【为什么设备信息里也要这个字段】
    ///   <c>AnShengDevStatus</c> 已有 <c>iccid</c>，但认领探测是「先 getDevInfo 再 getDevStatus」，
    ///   若设备在 getDevStatus 阶段超时，只认状态报文就会丢掉 ICCID。
    ///   补齐后由 <c>MergeSnapshot</c> 做双源合并，两条路任一有值即可落库。
    /// </summary>
    [JsonPropertyName("iccid")]
    public string? Iccid { get; set; }
}

/// <summary>
/// 消息类别枚举，便于路由到不同的处理逻辑。
/// </summary>
public enum AnShengMessageCategory
{
    /// <summary>未知类型。</summary>
    Unknown,

    /// <summary>设备状态数据（getDevStatus 应答）。</summary>
    DevStatus,

    /// <summary>设备基础信息（getDevInfo 应答）。</summary>
    DevInfo,

    /// <summary>Legacy 充电桩：订单开始（orderStart）。</summary>
    OrderStart,

    /// <summary>Legacy 充电桩：订单结束（orderEnd）。</summary>
    OrderEnd,

    /// <summary>Legacy 充电桩：订单进度上报（orderUp 定时推送）。</summary>
    OrderUp,

    /// <summary>设备离线（MQTT 遗嘱，method == close）。</summary>
    Close,

    /// <summary>设备主动上报事件（connected / keyEvent / delayEvent / timeEvent / recv485）。</summary>
    Event,

    /// <summary>通用命令应答（除上述 method 外）。</summary>
    CommandResponse
}
