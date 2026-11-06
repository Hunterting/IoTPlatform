using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IoTPlatform.Helpers;

/// <summary>
/// 全局 DateTime JSON 转换器。
///
/// 【统一口径】
///  - 出网：一律 "yyyy-MM-dd HH:mm:ss"（本地墙上时间、精确到秒，无 T / Z / 微秒）。
///  - 入站：宽容解析多种格式；无时区标记的字符串按「本地时间」理解并转回 UTC 与出网对称，
///    带 Z / ±HH:mm 标记的按标准时区解析。
///
/// 【为什么这样改】
///  本项目 268 处写入使用 DateTime.UtcNow，MySQL 读回的 DateTime.Kind=Unspecified。
///  System.Text.Json 默认把这类值序列化成 "2026-11-05T11:17:00.123456"（UTC 值、不带时区后缀），
///  前端 new Date(str) 会按浏览器本地时区解析 → 中国用户看到的比真实时间少 8 小时，
///  且尾随 6 位微秒。用户要求「只需要秒、时间要准确、不需要时区转换那套」，
///  故在服务端一次性把 UTC 值转为本地墙上时间并截断到秒，前端按本地解析即可自洽，
///  数据库仍存 UTC（兼容历史数据与既有 944 个测试），无需刷库、无需改 268 处 UtcNow。
/// </summary>
public class LocalDateTimeJsonConverter : JsonConverter<DateTime>
{
    public const string Format = "yyyy-MM-dd HH:mm:ss";

    private static readonly string[] ExactFormats =
    {
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH:mm",
        "yyyy-MM-dd",
        "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-ddTHH:mm:ss.FFFFFFF",
    };

    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return default;
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("DateTime 需要字符串类型");

        var s = reader.GetString();
        if (string.IsNullOrWhiteSpace(s)) return default;

        // 带显式时区标记（Z 或 ±HH:mm / ±HHmm）：按标准解析，转 UTC 存储
        var hasOffset = s.EndsWith("Z", StringComparison.OrdinalIgnoreCase)
            || (s.Length >= 6 && (s[^6] == '+' || s[^6] == '-' || s[^5] == '+' || s[^5] == '-'));
        if (hasOffset &&
            DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
        {
            return dto.UtcDateTime;
        }

        // 无时区标记：优先精确格式（"yyyy-MM-dd HH:mm:ss" 等），按本地时间理解 → 转 UTC
        if (DateTime.TryParseExact(s, ExactFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var exact))
        {
            return DateTime.SpecifyKind(exact, DateTimeKind.Local).ToUniversalTime();
        }

        // 兜底：ISO / 其它常见格式
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            return DateTime.SpecifyKind(dt, DateTimeKind.Local).ToUniversalTime();
        }

        throw new JsonException($"无法解析 DateTime: {s}");
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        // DateTime.MinValue（默认值）不做时区偏移，避免出现 "0001-01-01 08:00:00" 这类怪值
        if (value == DateTime.MinValue)
        {
            writer.WriteStringValue("0001-01-01 00:00:00");
            return;
        }

        DateTime local = value.Kind switch
        {
            DateTimeKind.Local => value,
            DateTimeKind.Utc => value.ToLocalTime(),
            // Unspecified：项目约定一律是 UTC（UtcNow 写入 / MySQL 读回）
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc).ToLocalTime(),
        };

        writer.WriteStringValue(local.ToString(Format, CultureInfo.InvariantCulture));
    }
}
