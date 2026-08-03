using System.Collections;
using System.Text.Json;

namespace IoTPlatform.Infrastructure.Protocol.AnSheng;

/// <summary>
/// 安圣协议参数的 JSON 类型。
/// </summary>
public enum AnShengParamType
{
    /// <summary>字符串。</summary>
    String = 0,

    /// <summary>整数。</summary>
    Int = 1,

    /// <summary>浮点数。</summary>
    Double = 2,

    /// <summary>布尔。</summary>
    Bool = 3,

    /// <summary>数组。</summary>
    Array = 4,

    /// <summary>对象。</summary>
    Object = 5
}

/// <summary>
/// 单个命令参数的规格说明（对应 asopen.md 中「命令参数」表格的一行）。
/// 注意：安圣二开协议的参数是<b>平铺在 JSON 顶层</b>的，不存在 <c>param</c> 包裹对象。
/// </summary>
public sealed class AnShengParamSpec
{
    /// <summary>参数名（JSON key），大小写敏感。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>是否必填。</summary>
    public bool Required { get; init; }

    /// <summary>参数 JSON 类型。</summary>
    public AnShengParamType Type { get; init; } = AnShengParamType.String;

    /// <summary>中文说明（取自协议文档）。</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>该参数要求的最低固件版本；为 null 表示无限制。</summary>
    public string? MinFirmware { get; init; }

    /// <summary>允许的枚举值集合；为 null 表示不做枚举校验。仅对 <see cref="AnShengParamType.String"/> 生效。</summary>
    public IReadOnlyList<string>? AllowedValues { get; init; }

    /// <summary>数值下限（含）；仅对 Int/Double 生效。</summary>
    public double? Minimum { get; init; }

    /// <summary>数值上限（含）；仅对 Int/Double 生效。</summary>
    public double? Maximum { get; init; }

    /// <summary>
    /// 创建一个参数规格。
    /// </summary>
    /// <param name="name">参数名。</param>
    /// <param name="type">参数类型。</param>
    /// <param name="required">是否必填。</param>
    /// <param name="description">中文说明。</param>
    /// <param name="minFirmware">最低固件版本，可为 null。</param>
    /// <param name="allowedValues">允许的枚举值，可为 null。</param>
    /// <param name="minimum">数值下限，可为 null。</param>
    /// <param name="maximum">数值上限，可为 null。</param>
    /// <returns>参数规格实例。</returns>
    public static AnShengParamSpec Create(
        string name,
        AnShengParamType type,
        bool required = false,
        string description = "",
        string? minFirmware = null,
        IReadOnlyList<string>? allowedValues = null,
        double? minimum = null,
        double? maximum = null)
    {
        return new AnShengParamSpec
        {
            Name = name,
            Type = type,
            Required = required,
            Description = description,
            MinFirmware = minFirmware,
            AllowedValues = allowedValues,
            Minimum = minimum,
            Maximum = maximum
        };
    }

    /// <summary>
    /// 校验给定值是否满足本参数规格。
    /// </summary>
    /// <param name="value">待校验值，可为 null。</param>
    /// <param name="error">校验失败时的中文原因。</param>
    /// <returns>通过校验返回 true。</returns>
    public bool Validate(object? value, out string? error)
    {
        error = null;

        if (value == null)
        {
            if (Required)
            {
                error = $"参数 {Name} 为必填项";
                return false;
            }

            return true;
        }

        if (!MatchesType(value))
        {
            error = $"参数 {Name} 类型应为 {Type}，实际为 {value.GetType().Name}";
            return false;
        }

        if (AllowedValues is { Count: > 0 } && Type == AnShengParamType.String)
        {
            var text = ExtractString(value);
            if (text != null && !AllowedValues.Contains(text, StringComparer.Ordinal))
            {
                error = $"参数 {Name} 取值应为 [{string.Join('|', AllowedValues)}]，实际为 {text}";
                return false;
            }
        }

        if (Minimum.HasValue || Maximum.HasValue)
        {
            var number = ExtractNumber(value);
            if (number.HasValue)
            {
                if (Minimum.HasValue && number.Value < Minimum.Value)
                {
                    error = $"参数 {Name} 不能小于 {Minimum.Value}";
                    return false;
                }

                if (Maximum.HasValue && number.Value > Maximum.Value)
                {
                    error = $"参数 {Name} 不能大于 {Maximum.Value}";
                    return false;
                }
            }
        }

        return true;
    }

    private bool MatchesType(object value)
    {
        if (value is JsonElement element) return MatchesJsonElement(element);

        return Type switch
        {
            AnShengParamType.String => value is string,
            AnShengParamType.Int => value is sbyte or byte or short or ushort or int or uint or long or ulong,
            AnShengParamType.Double => value is float or double or decimal
                                       or sbyte or byte or short or ushort or int or uint or long or ulong,
            AnShengParamType.Bool => value is bool,
            AnShengParamType.Array => value is IEnumerable and not string,
            AnShengParamType.Object => value is not string && value is not IEnumerable && !IsPrimitive(value),
            _ => true
        };
    }

    private bool MatchesJsonElement(JsonElement element)
    {
        return Type switch
        {
            AnShengParamType.String => element.ValueKind == JsonValueKind.String,
            AnShengParamType.Int => element.ValueKind == JsonValueKind.Number,
            AnShengParamType.Double => element.ValueKind == JsonValueKind.Number,
            AnShengParamType.Bool => element.ValueKind is JsonValueKind.True or JsonValueKind.False,
            AnShengParamType.Array => element.ValueKind == JsonValueKind.Array,
            AnShengParamType.Object => element.ValueKind == JsonValueKind.Object,
            _ => true
        };
    }

    private static bool IsPrimitive(object value)
        => value is bool or char or sbyte or byte or short or ushort or int or uint
            or long or ulong or float or double or decimal;

    private static string? ExtractString(object value)
    {
        if (value is string s) return s;
        if (value is JsonElement { ValueKind: JsonValueKind.String } element) return element.GetString();
        return null;
    }

    private static double? ExtractNumber(object value)
    {
        if (value is JsonElement element)
        {
            return element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var d) ? d : null;
        }

        try
        {
            return value switch
            {
                sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal
                    => Convert.ToDouble(value),
                _ => null
            };
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
        {
            return null;
        }
    }
}

/// <summary>
/// 参数校验结果。
/// </summary>
public sealed class AnShengValidationResult
{
    /// <summary>是否通过校验。</summary>
    public bool IsValid { get; init; }

    /// <summary>全部错误信息，通过时为空集合。</summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    /// <summary>校验通过的单例结果。</summary>
    public static AnShengValidationResult Ok { get; } = new() { IsValid = true };

    /// <summary>
    /// 构造一个失败结果。
    /// </summary>
    /// <param name="errors">错误信息集合。</param>
    /// <returns>失败结果。</returns>
    public static AnShengValidationResult Fail(IReadOnlyList<string> errors)
        => new() { IsValid = false, Errors = errors };

    /// <summary>
    /// 构造一个失败结果。
    /// </summary>
    /// <param name="error">单条错误信息。</param>
    /// <returns>失败结果。</returns>
    public static AnShengValidationResult Fail(string error)
        => new() { IsValid = false, Errors = new[] { error } };

    /// <summary>
    /// 拼接全部错误信息。
    /// </summary>
    /// <returns>以「；」分隔的错误串。</returns>
    public override string ToString() => IsValid ? "OK" : string.Join("；", Errors);
}
