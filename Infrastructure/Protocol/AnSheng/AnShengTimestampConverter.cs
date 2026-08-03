using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IoTPlatform.Infrastructure.Protocol.AnSheng;

/// <summary>
/// 安圣协议时间戳工具。
///
/// 协议规定：<c>timestamp</c> 为<b>秒级 int</b>，WiFi 款不上报也不接受该字段。
/// 但历史设备/固件存在多种写法，解析侧必须宽松兼容以下 4 种形态：
///   1. 秒级数字     <c>"timestamp": 1745396759</c>
///   2. 毫秒级数字   <c>"timestamp": 1745396759780</c>
///   3. 字符串数字   <c>"timestamp": "1745396759"</c> / <c>"1745396759780"</c>
///   4. 缺失 / null  （WiFi 款）
/// </summary>
public static class AnShengTimestampConverter
{
    /// <summary>判定为毫秒级的阈值：1e11 秒 ≈ 公元 5138 年，超过即认为是毫秒。</summary>
    private const long MillisecondThreshold = 100_000_000_000L;

    /// <summary>
    /// 将 <see cref="DateTime"/> 转为协议要求的秒级 Unix 时间戳。
    /// </summary>
    /// <param name="value">时间值；<see cref="DateTimeKind.Unspecified"/> 按 UTC 处理。</param>
    /// <returns>秒级时间戳。</returns>
    public static long ToUnixSeconds(DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

        return new DateTimeOffset(utc).ToUnixTimeSeconds();
    }

    /// <summary>
    /// 获取当前时刻的秒级 Unix 时间戳。
    /// </summary>
    /// <returns>秒级时间戳（10 位）。</returns>
    public static long NowUnixSeconds() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    /// <summary>
    /// 将原始时间戳数字（秒或毫秒）归一化为 UTC 时间。
    /// </summary>
    /// <param name="raw">原始时间戳数字。</param>
    /// <returns>UTC 时间；<paramref name="raw"/> 非正数时返回 null。</returns>
    public static DateTime? FromRaw(long? raw)
    {
        if (raw == null || raw.Value <= 0) return null;

        var value = raw.Value;
        try
        {
            return value >= MillisecondThreshold
                ? DateTimeOffset.FromUnixTimeMilliseconds(value).UtcDateTime
                : DateTimeOffset.FromUnixTimeSeconds(value).UtcDateTime;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>
    /// 宽松解析任意形态的 timestamp 值（数字 / 字符串数字 / null / 缺失）。
    /// </summary>
    /// <param name="element">JSON 值节点。</param>
    /// <param name="raw">解析出的原始数字（未做秒/毫秒归一化）。</param>
    /// <returns>解析成功返回 true。</returns>
    public static bool TryParse(JsonElement element, out long? raw)
    {
        raw = null;

        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                if (element.TryGetInt64(out var number))
                {
                    raw = number;
                    return true;
                }

                if (element.TryGetDouble(out var dbl))
                {
                    raw = (long)dbl;
                    return true;
                }

                return false;

            case JsonValueKind.String:
                return TryParse(element.GetString(), out raw);

            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return false;

            default:
                return false;
        }
    }

    /// <summary>
    /// 宽松解析字符串形态的 timestamp。
    /// </summary>
    /// <param name="text">字符串值，可为 null。</param>
    /// <param name="raw">解析出的原始数字。</param>
    /// <returns>解析成功返回 true。</returns>
    public static bool TryParse(string? text, out long? raw)
    {
        raw = null;
        if (string.IsNullOrWhiteSpace(text)) return false;

        if (long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            raw = value;
            return true;
        }

        if (double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var dbl))
        {
            raw = (long)dbl;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 从整条报文的 JSON 根节点中提取 timestamp。
    /// </summary>
    /// <param name="root">报文根节点。</param>
    /// <param name="raw">原始时间戳数字。</param>
    /// <param name="utc">归一化后的 UTC 时间。</param>
    /// <returns>存在且可解析返回 true。</returns>
    public static bool TryExtract(JsonElement root, out long? raw, out DateTime? utc)
    {
        raw = null;
        utc = null;

        if (root.ValueKind != JsonValueKind.Object) return false;
        if (!root.TryGetProperty("timestamp", out var element)) return false;
        if (!TryParse(element, out raw)) return false;

        utc = FromRaw(raw);
        return true;
    }
}

/// <summary>
/// <see cref="long"/>? 的宽松 JSON 转换器：同时接受数字与字符串数字。
/// 用于 <see cref="AnShengMessage.RawTimestamp"/> 等字段，避免固件差异导致整条报文反序列化失败。
/// </summary>
public sealed class AnShengFlexibleInt64Converter : JsonConverter<long?>
{
    /// <inheritdoc />
    public override long? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;

            case JsonTokenType.Number:
                if (reader.TryGetInt64(out var number)) return number;
                if (reader.TryGetDouble(out var dbl)) return (long)dbl;
                return null;

            case JsonTokenType.String:
                return AnShengTimestampConverter.TryParse(reader.GetString(), out var raw) ? raw : null;

            default:
                reader.Skip();
                return null;
        }
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, long? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
        {
            writer.WriteNumberValue(value.Value);
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}

/// <summary>
/// <see cref="double"/>? 的宽松 JSON 转换器：同时接受数字与字符串数字。
/// 安圣设备会把 <c>temperature</c>、<c>voltage</c>、<c>totalKwh</c> 等以字符串形式上报（如 <c>"32.4"</c>）。
/// </summary>
public sealed class AnShengFlexibleDoubleConverter : JsonConverter<double?>
{
    /// <inheritdoc />
    public override double? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;

            case JsonTokenType.Number:
                return reader.TryGetDouble(out var number) ? number : null;

            case JsonTokenType.String:
                var text = reader.GetString();
                if (string.IsNullOrWhiteSpace(text)) return null;
                return double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var dbl)
                    ? dbl
                    : null;

            default:
                reader.Skip();
                return null;
        }
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, double? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
        {
            writer.WriteNumberValue(value.Value);
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}
