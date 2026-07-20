using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace IoTPlatform.Infrastructure.Protocol.AnSheng;

/// <summary>
/// 安圣 MQTT 报文解析器
/// 负责：Topic 提取 IMEI → JSON 反序列化 → 分类 method → 解析具体数据
/// </summary>
public class AnShengMessageParser
{
    private readonly ILogger<AnShengMessageParser>? _logger;

    public AnShengMessageParser(ILogger<AnShengMessageParser>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// 从安圣 MQTT Topic 中提取 IMEI
    /// Topic 格式：/devtoser/pub/{imei} 或 /devtoser/will/{imei}
    /// </summary>
    public static string ExtractImeiFromTopic(string topic)
    {
        var parts = topic.Split('/');
        return parts.Length >= 4 ? parts[^1] : string.Empty;
    }

    /// <summary>
    /// 解析安圣消息 JSON 为结构化对象
    /// </summary>
    public AnShengMessage? Parse(string jsonPayload)
    {
        try
        {
            var message = JsonSerializer.Deserialize<AnShengMessage>(jsonPayload,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (message == null || string.IsNullOrEmpty(message.Method))
            {
                _logger?.LogWarning("安圣消息解析失败：空或缺少 method 字段");
                return null;
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
    /// 判断消息类别
    /// </summary>
    public AnShengMessageCategory GetCategory(AnShengMessage message)
    {
        return message.Method switch
        {
            "getDevStatus" => AnShengMessageCategory.DevStatus,
            "getDevInfo" => AnShengMessageCategory.DevInfo,
            "orderStart" => AnShengMessageCategory.OrderStart,
            "orderEnd" => AnShengMessageCategory.OrderEnd,
            "orderUp" => AnShengMessageCategory.OrderUp,
            "close" => AnShengMessageCategory.Close,
            "setSwitch" or "getSwitchStatus" or "setSwitchConfig"
                or "getSwitchConfig" or "reboot"
                => AnShengMessageCategory.OpenDeviceCommand,
            _ => AnShengMessageCategory.CommandResponse
        };
    }

    /// <summary>
    /// 解析 getDevStatus 的 param → AnShengDevStatus
    /// </summary>
    public AnShengDevStatus? ParseDevStatus(AnShengMessage message)
    {
        if (!message.Param.HasValue) return null;

        try
        {
            return JsonSerializer.Deserialize<AnShengDevStatus>(
                message.Param.Value.GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            _logger?.LogError(ex, "解析安圣设备状态数据失败");
            return null;
        }
    }

    /// <summary>
    /// 解析订单相关消息的 param → AnShengOrderData
    /// </summary>
    public AnShengOrderData? ParseOrderData(AnShengMessage message)
    {
        if (!message.Param.HasValue) return null;

        try
        {
            return JsonSerializer.Deserialize<AnShengOrderData>(
                message.Param.Value.GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            _logger?.LogError(ex, "解析安圣订单数据失败");
            return null;
        }
    }

    /// <summary>
    /// 解析 getDevInfo 的 param → AnShengDevInfo
    /// </summary>
    public AnShengDevInfo? ParseDevInfo(AnShengMessage message)
    {
        if (!message.Param.HasValue) return null;

        try
        {
            return JsonSerializer.Deserialize<AnShengDevInfo>(
                message.Param.Value.GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            _logger?.LogError(ex, "解析安圣设备信息失败");
            return null;
        }
    }

    /// <summary>
    /// 将安圣消息转换为标准化的 JSON（用于 SensorData 存储）
    /// 提取核心字段并扁平化为 DeviceDataRecord 可用的格式
    /// </summary>
    public string NormalizeForSensorData(AnShengMessage message, string topic)
    {
        try
        {
            var category = GetCategory(message);

            if (category == AnShengMessageCategory.DevStatus)
            {
                var status = ParseDevStatus(message);
                if (status == null) return message.Param?.GetRawText() ?? "{}";

                // 构建标准化 JSON：提取温度 + EMdata 聚合值
                var emData = status.EmData;
                var totalPower = emData?.Sum(e => e.P ?? 0) ?? 0;
                var totalEnergy = emData?.Sum(e => e.E ?? 0) ?? 0;
                var totalCurrent = emData?.Sum(e => e.C ?? 0) ?? 0;
                var avgVoltage = emData?.Count > 0
                    ? emData.Average(e => e.V ?? 0)
                    : (double?)null;

                return JsonSerializer.Serialize(new
                {
                    method = message.Method,
                    imei = message.Imei,
                    temperature = status.Temperature,
                    slots = status.Slots,
                    relay = status.Relay,
                    signal = status.Signal,
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
                    raw_timestamp = message.Timestamp
                });
            }

            if (category == AnShengMessageCategory.OrderStart ||
                category == AnShengMessageCategory.OrderEnd ||
                category == AnShengMessageCategory.OrderUp)
            {
                var orderData = ParseOrderData(message);
                if (orderData == null) return message.Param?.GetRawText() ?? "{}";

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
                    raw_timestamp = message.Timestamp
                });
            }

            if (category == AnShengMessageCategory.Close)
            {
                return JsonSerializer.Serialize(new
                {
                    method = "close",
                    imei = message.Imei,
                    raw_timestamp = message.Timestamp
                });
            }

            // 默认：直接透传原始 JSON
            return message.Param?.GetRawText() ?? JsonSerializer.Serialize(message);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "标准化安圣消息失败");
            return message.Param?.GetRawText() ?? "{}";
        }
    }
}
