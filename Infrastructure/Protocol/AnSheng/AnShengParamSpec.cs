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
    /// 本参数本身是否为敏感值（T7 决策 D3）。
    ///
    /// 【只影响「留痕」，不影响「下发」】为 <c>true</c> 时，
    /// <c>AnShengSecretMasker</c> 会在写 <c>AnShengCommandRecord.RequestJson</c> /
    /// <c>ResponseJson</c> / 打日志 / 出 <c>GET /commands/{id}</c> 之前，把该值替换为 <c>"***"</c>。
    /// 真正发布到 MQTT 的报文<b>仍是明文</b> —— 设备只认明文口令。
    /// </summary>
    public bool IsSecret { get; init; }

    /// <summary>
    /// 当本参数是 <see cref="AnShengParamType.Object"/> 时，其对象<b>内部</b>需要掩码的字段名集合。
    ///
    /// 【为什么需要它 —— setMqtt 的真实形状】
    ///   协议里 <c>setMqtt</c> 的顶层参数只有 <c>mqttParams</c>（对象）与 <c>reboot</c>（布尔），
    ///   口令 <c>password</c> 是<b>嵌在 <c>mqttParams</c> 里面</b>的，并不是一个顶层参数。
    ///   若只有 <see cref="IsSecret"/>，要么把整个 <c>mqttParams</c> 打码（连 host/port/clientId
    ///   这些排障必需的信息一并丢失），要么就没法打码。
    ///   因此这里用「父参数声明子字段」的方式，做到<b>精确到字段</b>的掩码。
    ///
    /// 为 null 或空表示对象内部无敏感字段。
    /// </summary>
    public IReadOnlyList<string>? SecretFields { get; init; }

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
    /// <param name="isSecret">本参数自身是否敏感（留痕时打码）。</param>
    /// <param name="secretFields">对象型参数内部需要打码的字段名，可为 null。</param>
    /// <returns>参数规格实例。</returns>
    public static AnShengParamSpec Create(
        string name,
        AnShengParamType type,
        bool required = false,
        string description = "",
        string? minFirmware = null,
        IReadOnlyList<string>? allowedValues = null,
        double? minimum = null,
        double? maximum = null,
        bool isSecret = false,
        IReadOnlyList<string>? secretFields = null)
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
            Maximum = maximum,
            IsSecret = isSecret,
            SecretFields = secretFields
        };
    }

    /// <summary>
    /// 校验给定值是否满足本参数规格（文案版，T7 之前的既有签名）。
    ///
    /// 内部委托 <see cref="ValidateDetailed"/>，只把结构化违规降级成一句中文。
    /// 保留它是为了不惊动既有调用方；新代码请用 <see cref="ValidateDetailed"/>，
    /// 因为拒绝<b>类别</b>（<c>AnShengCommandRejectReason</c>）只能从违规种类推导，文案推不出来。
    /// </summary>
    /// <param name="value">待校验值，可为 null。</param>
    /// <param name="error">校验失败时的中文原因。</param>
    /// <returns>通过校验返回 true。</returns>
    public bool Validate(object? value, out string? error)
    {
        var ok = ValidateDetailed(value, out var violation);
        error = violation?.Message;
        return ok;
    }

    /// <summary>
    /// 校验给定值是否满足本参数规格，失败时给出<b>结构化</b>违规描述。
    ///
    /// 【为什么要结构化】<c>AnShengCommandGuard</c> 必须区分「参数不合法」与「固件不够」——
    /// 前者是 <c>RejectedByValidation</c>，后者是 <c>RejectedByFirmware</c> 且响应要回带
    /// <c>RequiredFirmware</c> 引导用户升级。靠正则匹配错误文案去分类是最脆的做法。
    ///
    /// 注意：本方法<b>不做</b>固件校验（它不知道设备当前固件），固件由
    /// <see cref="AnShengCommandSpec.ValidateParams(IReadOnlyDictionary{string, object?}, string, bool, out IReadOnlyList{AnShengParamViolation})"/>
    /// 统一处理。
    /// </summary>
    /// <param name="value">待校验值，可为 null。</param>
    /// <param name="violation">校验失败时的结构化违规；通过时为 null。</param>
    /// <returns>通过校验返回 true。</returns>
    public bool ValidateDetailed(object? value, out AnShengParamViolation? violation)
    {
        violation = null;

        if (value == null)
        {
            if (Required)
            {
                violation = new AnShengParamViolation(
                    Name,
                    AnShengParamViolationKind.Missing,
                    $"参数 {Name} 为必填项");
                return false;
            }

            return true;
        }

        if (!MatchesType(value))
        {
            violation = new AnShengParamViolation(
                Name,
                AnShengParamViolationKind.TypeMismatch,
                $"参数 {Name} 类型应为 {Type}，实际为 {value.GetType().Name}");
            return false;
        }

        if (AllowedValues is { Count: > 0 } && Type == AnShengParamType.String)
        {
            var text = ExtractString(value);
            if (text != null && !AllowedValues.Contains(text, StringComparer.Ordinal))
            {
                violation = new AnShengParamViolation(
                    Name,
                    AnShengParamViolationKind.NotAllowed,
                    $"参数 {Name} 取值应为 [{string.Join('|', AllowedValues)}]，实际为 {text}");
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
                    violation = new AnShengParamViolation(
                        Name,
                        AnShengParamViolationKind.OutOfRange,
                        $"参数 {Name} 不能小于 {Minimum.Value}");
                    return false;
                }

                if (Maximum.HasValue && number.Value > Maximum.Value)
                {
                    violation = new AnShengParamViolation(
                        Name,
                        AnShengParamViolationKind.OutOfRange,
                        $"参数 {Name} 不能大于 {Maximum.Value}");
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
/// 参数违规的种类（T7 新增）。
///
/// 【为什么不用字符串】<c>AnShengCommandGuard</c> 要按种类映射到
/// <c>AnShengCommandRejectReason</c>：<see cref="FirmwareTooLow"/> → <c>RejectedByFirmware</c>，
/// 其余 → <c>RejectedByValidation</c>。这层映射必须是机器可读的编译期契约。
///
/// 【枚举值只能追加】它会随 <c>AnShengParamViolation</c> 出现在 API 响应里。
/// </summary>
public enum AnShengParamViolationKind
{
    /// <summary>必填参数缺失。</summary>
    Missing = 0,

    /// <summary>JSON 类型与规格不符。</summary>
    TypeMismatch = 1,

    /// <summary>取值不在允许的枚举集合内。</summary>
    NotAllowed = 2,

    /// <summary>数值越界（含 <c>slotNum</c> 超出设备实际插槽数）。</summary>
    OutOfRange = 3,

    /// <summary>设备固件版本低于该参数/命令要求的门槛。</summary>
    FirmwareTooLow = 4,

    /// <summary>传入了规格外的未知参数（仅当 <c>allowUnknownParams=false</c> 时产生）。</summary>
    UnknownParam = 5,

    /// <summary>该 method 是设备上行事件，平台不可下发。</summary>
    NotDownlinkable = 6
}

/// <summary>
/// 一条结构化的参数违规。
/// </summary>
/// <param name="ParamName">
/// 违规参数名。命令级违规（<see cref="AnShengParamViolationKind.FirmwareTooLow"/> 落在整条命令上、
/// 或 <see cref="AnShengParamViolationKind.NotDownlinkable"/>）时为空串。
/// </param>
/// <param name="Kind">违规种类。</param>
/// <param name="Message">面向人的中文说明。</param>
/// <param name="MinFirmware">
/// 仅 <see cref="AnShengParamViolationKind.FirmwareTooLow"/> 有值 —— 满足该参数/命令所需的<b>最低</b>固件版本。
/// 它会被原样回传到 <c>AnShengCommandResponse.RequiredFirmware</c>，供前端引导升级。
/// </param>
public sealed record AnShengParamViolation(
    string ParamName,
    AnShengParamViolationKind Kind,
    string Message,
    string? MinFirmware = null)
{
    /// <inheritdoc />
    public override string ToString() => Message;
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
