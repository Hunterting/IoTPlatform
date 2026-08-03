using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace IoTPlatform.Infrastructure.Protocol.AnSheng;

/// <summary>
/// 安圣 MQTT 报文解析器。
/// 职责：Topic 提取 IMEI → JSON 反序列化 → 分类 method → 解析具体数据 → 标准化落库。
///
/// 兼容两套报文形态：
///   1. 二开协议（asopen.md）：业务字段<b>平铺</b>在 JSON 顶层，无 <c>param</c>；
///   2. Legacy 充电桩协议：业务字段位于 <c>param</c> 对象内。
/// 解析时优先读 <c>param</c>（若存在），否则回退到顶层，保证两套链路都能工作。
/// </summary>
public class AnShengMessageParser
{
    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
    };

    private readonly ILogger<AnShengMessageParser>? _logger;

    /// <summary>
    /// 创建解析器。
    /// </summary>
    /// <param name="logger">可选日志器。</param>
    public AnShengMessageParser(ILogger<AnShengMessageParser>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// 从安圣 MQTT Topic 中提取 IMEI。
    /// 支持形如 <c>/iot/server/iot-board/{imei}</c>、<c>/devtoser/pub/{imei}</c> 的主题。
    /// 若主题末段不是 IMEI（例如设备配置为不带 %imei% 的固定主题），返回空串，
    /// 调用方应回退使用报文中的 <c>imei</c> 字段。
    /// </summary>
    /// <param name="topic">MQTT 主题。</param>
    /// <returns>IMEI 或空串。</returns>
    public static string ExtractImeiFromTopic(string? topic)
    {
        if (string.IsNullOrWhiteSpace(topic)) return string.Empty;

        var parts = topic.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return string.Empty;

        var last = parts[^1];

        // IMEI 为 14~17 位纯数字；不满足则认为主题不含 IMEI
        if (last.Length is >= 14 and <= 17 && last.All(char.IsDigit))
        {
            return last;
        }

        return string.Empty;
    }

    /// <summary>
    /// 解析安圣消息 JSON 为结构化对象。
    /// </summary>
    /// <param name="jsonPayload">原始 JSON 报文。</param>
    /// <returns>解析结果；报文非法或缺少 <c>method</c> 时返回 null。</returns>
    public AnShengMessage? Parse(string? jsonPayload)
    {
        if (string.IsNullOrWhiteSpace(jsonPayload))
        {
            _logger?.LogWarning("安圣消息解析失败：payload 为空");
            return null;
        }

        try
        {
            var message = JsonSerializer.Deserialize<AnShengMessage>(jsonPayload, DeserializeOptions);

            if (message == null || string.IsNullOrEmpty(message.Method))
            {
                _logger?.LogWarning("安圣消息解析失败：空或缺少 method 字段");
                return null;
            }

            message.RawJson = jsonPayload;
            message.ReceivedAt = DateTime.UtcNow;

            // 宽松时间戳：秒 / 毫秒 / 字符串数字 / 缺失 四种形态
            using (var document = JsonDocument.Parse(jsonPayload))
            {
                if (AnShengTimestampConverter.TryExtract(document.RootElement, out var raw, out var utc))
                {
                    message.RawTimestamp = raw;
                    message.TimestampUtc = utc;
                }
                else
                {
                    message.TimestampUtc = AnShengTimestampConverter.FromRaw(message.RawTimestamp);
                }
            }

            return message;
        }
        catch (JsonException ex)
        {
            _logger?.LogError(ex, "安圣消息 JSON 反序列化失败: {Payload}", jsonPayload);
            return null;
        }
    }

    /// <summary>
    /// 判断消息类别。
    /// </summary>
    /// <param name="message">已解析的消息。</param>
    /// <returns>消息类别。</returns>
    public AnShengMessageCategory GetCategory(AnShengMessage message)
    {
        if (message == null) return AnShengMessageCategory.Unknown;

        return message.Method switch
        {
            "getDevStatus" => AnShengMessageCategory.DevStatus,
            "getDevInfo" => AnShengMessageCategory.DevInfo,
            "orderStart" => AnShengMessageCategory.OrderStart,
            "orderEnd" => AnShengMessageCategory.OrderEnd,
            "orderUp" => AnShengMessageCategory.OrderUp,
            AnShengCommandCatalog.WillMethod => AnShengMessageCategory.Close,
            "connected" or "keyEvent" or "delayEvent" or "timeEvent" or "recv485"
                => AnShengMessageCategory.Event,
            _ => AnShengMessageCategory.CommandResponse
        };
    }

    /// <summary>
    /// 是否为设备离线（遗嘱）报文。
    /// 判定<b>只依据</b> <c>method == "close"</c>，不依赖主题前缀——
    /// 因为设备端 <c>willTopic</c> 与 <c>publishTopic</c> 允许配置为同一主题。
    /// </summary>
    /// <param name="message">已解析的消息。</param>
    /// <returns>是遗嘱消息返回 true。</returns>
    public static bool IsWillMessage(AnShengMessage? message)
        => message != null
           && string.Equals(message.Method, AnShengCommandCatalog.WillMethod, StringComparison.Ordinal);

    /// <summary>
    /// 解析 <c>getDevStatus</c> 应答。
    /// 二开协议字段平铺在顶层；Legacy 充电桩报文则位于 <c>param</c> 内。
    /// </summary>
    /// <param name="message">已解析的消息。</param>
    /// <returns>状态对象；无法解析返回 null。</returns>
    public AnShengDevStatus? ParseDevStatus(AnShengMessage message)
        => DeserializeBody<AnShengDevStatus>(message, "设备状态数据");

    /// <summary>
    /// 解析 <c>getDevInfo</c> 应答。
    /// </summary>
    /// <param name="message">已解析的消息。</param>
    /// <returns>设备信息对象；无法解析返回 null。</returns>
    public AnShengDevInfo? ParseDevInfo(AnShengMessage message)
        => DeserializeBody<AnShengDevInfo>(message, "设备信息");

    /// <summary>
    /// 解析 Legacy 充电桩订单消息（orderStart / orderEnd / orderUp）。
    /// </summary>
    /// <param name="message">已解析的消息。</param>
    /// <returns>订单数据；无法解析返回 null。</returns>
    public AnShengOrderData? ParseOrderData(AnShengMessage message)
        => DeserializeBody<AnShengOrderData>(message, "订单数据");

    /// <summary>
    /// 从消息体（优先 <c>param</c>，否则顶层 <c>RawJson</c>）反序列化目标类型。
    /// </summary>
    /// <typeparam name="T">目标类型。</typeparam>
    /// <param name="message">已解析的消息。</param>
    /// <param name="what">用于日志的中文描述。</param>
    /// <returns>反序列化结果或 null。</returns>
    private T? DeserializeBody<T>(AnShengMessage message, string what) where T : class
    {
        if (message == null) return null;

        var json = GetBodyJson(message);
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            return JsonSerializer.Deserialize<T>(json, DeserializeOptions);
        }
        catch (JsonException ex)
        {
            _logger?.LogError(ex, "解析安圣{What}失败: {Payload}", what, json);
            return null;
        }
    }

    /// <summary>
    /// 取消息体 JSON：Legacy 报文取 <c>param</c>，二开报文取整条 <c>RawJson</c>。
    /// </summary>
    /// <param name="message">已解析的消息。</param>
    /// <returns>消息体 JSON 字符串，可能为空串。</returns>
    private static string GetBodyJson(AnShengMessage message)
    {
#pragma warning disable CS0618 // Legacy 充电桩链路仍依赖 param
        if (message.Param.HasValue && message.Param.Value.ValueKind == JsonValueKind.Object)
        {
            return message.Param.Value.GetRawText();
        }
#pragma warning restore CS0618

        return message.RawJson;
    }

    /// <summary>
    /// 将安圣消息转换为标准化 JSON（用于 SensorData 存储）。
    /// </summary>
    /// <param name="message">已解析的消息。</param>
    /// <param name="topic">来源主题（仅用于排障，不参与判定）。</param>
    /// <returns>标准化后的 JSON 字符串。</returns>
    public string NormalizeForSensorData(AnShengMessage message, string topic)
    {
        if (message == null) return "{}";

        try
        {
            var category = GetCategory(message);

            if (category == AnShengMessageCategory.DevStatus)
            {
                return NormalizeDevStatus(message);
            }

            if (category is AnShengMessageCategory.OrderStart
                or AnShengMessageCategory.OrderEnd
                or AnShengMessageCategory.OrderUp)
            {
                return NormalizeOrder(message);
            }

            if (category == AnShengMessageCategory.Close)
            {
                return JsonSerializer.Serialize(new
                {
                    method = AnShengCommandCatalog.WillMethod,
                    imei = message.Imei,
                    raw_timestamp = message.RawTimestamp,
                    timestamp_utc = message.TimestampUtc
                });
            }

            if (category == AnShengMessageCategory.DevInfo)
            {
                var info = ParseDevInfo(message);
                return JsonSerializer.Serialize(new
                {
                    method = message.Method,
                    imei = message.Imei,
                    result = message.Result,
                    version = info?.Version,
                    slot_amount = info?.SlotAmount,
                    phase_amount = info?.PhaseAmount,
                    model = info?.Model,
                    net_type = info?.NetType,
                    raw_timestamp = message.RawTimestamp,
                    timestamp_utc = message.TimestampUtc
                });
            }

            // 事件与通用应答：直接透传原始报文（已是平铺 JSON）
            return GetBodyJson(message) is { Length: > 0 } body
                ? body
                : JsonSerializer.Serialize(message);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "标准化安圣消息失败: Topic={Topic}", topic);
            return GetBodyJson(message) is { Length: > 0 } fallback ? fallback : "{}";
        }
    }

    private string NormalizeDevStatus(AnShengMessage message)
    {
        var status = ParseDevStatus(message);
        if (status == null)
        {
            return GetBodyJson(message) is { Length: > 0 } body ? body : "{}";
        }

        var emData = status.EmData;
        var totalPower = emData?.Sum(e => e.P ?? 0) ?? 0;
        var totalEnergy = emData?.Sum(e => e.E ?? 0) ?? 0;
        var totalCurrent = emData?.Sum(e => e.C ?? 0) ?? 0;
        double? avgVoltage = emData is { Count: > 0 } ? emData.Average(e => e.V ?? 0) : null;

        return JsonSerializer.Serialize(new
        {
            method = message.Method,
            imei = message.Imei,
            result = message.Result,
            net_type = status.NetType,
            iccid = status.Iccid,
            signal = status.Signal,
            temperature = status.Temperature,
            gps = status.Gps,
            slot_count = status.SlotCount,
            slots = status.Slots,
            total_power = totalPower,
            total_energy = totalEnergy,
            total_current = totalCurrent,
            avg_voltage = avgVoltage,
            em_data = emData?.Select((e, i) => new
            {
                slot = i + 1,
                v = e.V,
                c = e.C,
                p = e.P,
                e_kwh = e.E,
                pf = e.Pf
            }),
            tasks = status.Tasks?.Select(t => new
            {
                slot = t.SlotNum,
                type = t.Type,
                status = t.Status,
                time_sec = t.TimeSec,
                power_kwh = t.PowerKwh,
                total_sec = t.TotalSec,
                total_kwh = t.TotalKwh,
                voltage = t.Voltage,
                current = t.Current,
                power = t.Power,
                close_reason = t.CloseReason,
                remark = t.Remark
            }),
            raw_timestamp = message.RawTimestamp,
            timestamp_utc = message.TimestampUtc
        });
    }

    private string NormalizeOrder(AnShengMessage message)
    {
        var orderData = ParseOrderData(message);
        if (orderData == null)
        {
            return GetBodyJson(message) is { Length: > 0 } body ? body : "{}";
        }

        return JsonSerializer.Serialize(new
        {
            method = message.Method,
            imei = message.Imei,
            sn = orderData.Sn,
            order = orderData.Order,
            state = orderData.State,
            reason = orderData.Reason,
            power = orderData.P,
            energy = orderData.E,
            timing_sec = orderData.Timing,
            limit_sec = orderData.Limit,
            raw_timestamp = message.RawTimestamp,
            timestamp_utc = message.TimestampUtc
        });
    }
}
