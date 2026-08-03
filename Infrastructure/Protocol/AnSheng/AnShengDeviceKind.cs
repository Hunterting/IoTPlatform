namespace IoTPlatform.Infrastructure.Protocol.AnSheng;

/// <summary>
/// 安圣「二开设备」品类。
/// 官方协议文档（asopen.md）中每个能力分组的支持矩阵均以这 4 个品类为列。
/// </summary>
public enum AnShengDeviceKind
{
    /// <summary>未识别品类。下发时按最保守策略处理（不注入 timestamp、不做能力拦截）。</summary>
    Unknown = 0,

    /// <summary>4G 喇叭（语音播报类，4G 联网）。</summary>
    Speaker4G = 1,

    /// <summary>4G 开关（插槽/继电器类，4G 联网）。</summary>
    Switch4G = 2,

    /// <summary>WiFi 喇叭（语音播报类，WiFi 联网）。</summary>
    SpeakerWiFi = 3,

    /// <summary>WiFi 开关（插槽/继电器类，WiFi 联网）。</summary>
    SwitchWiFi = 4
}

/// <summary>
/// 品类能力位掩码。命令目录用它描述「哪些品类支持本命令」。
/// 位定义与 <see cref="AnShengDeviceKind"/> 一一对应。
/// </summary>
[Flags]
public enum AnShengDeviceCapability
{
    /// <summary>无任何品类支持。</summary>
    None = 0,

    /// <summary>4G 喇叭。</summary>
    Speaker4G = 1 << 0,

    /// <summary>4G 开关。</summary>
    Switch4G = 1 << 1,

    /// <summary>WiFi 喇叭。</summary>
    SpeakerWiFi = 1 << 2,

    /// <summary>WiFi 开关。</summary>
    SwitchWiFi = 1 << 3,

    /// <summary>全部 4 个品类。</summary>
    All = Speaker4G | Switch4G | SpeakerWiFi | SwitchWiFi,

    /// <summary>能力组 G1「通用命令」：4 个品类全部支持。</summary>
    GroupCommon = All,

    /// <summary>能力组 G2「MQTT 参数」：4 个品类全部支持。</summary>
    GroupMqtt = All,

    /// <summary>能力组 G3「开关动作 / 延时任务 / 电量实时 / 校准」：仅开关类支持。</summary>
    GroupSwitchAction = Switch4G | SwitchWiFi,

    /// <summary>能力组 G4「定时任务 / 电量统计 / 日志 / RS485」：仅 4G 开关支持。</summary>
    GroupTimeTask = Switch4G,

    /// <summary>能力组 G5「对时 / 物联卡预警」：仅 4G 款支持。</summary>
    Group4GOnly = Speaker4G | Switch4G
}

/// <summary>
/// <see cref="AnShengDeviceKind"/> 扩展方法。
/// </summary>
public static class AnShengDeviceKindExtensions
{
    /// <summary>
    /// 将品类枚举转换为能力位。
    /// </summary>
    /// <param name="kind">设备品类。</param>
    /// <returns>对应的单一能力位；<see cref="AnShengDeviceKind.Unknown"/> 返回 <see cref="AnShengDeviceCapability.None"/>。</returns>
    public static AnShengDeviceCapability ToCapability(this AnShengDeviceKind kind)
    {
        return kind switch
        {
            AnShengDeviceKind.Speaker4G => AnShengDeviceCapability.Speaker4G,
            AnShengDeviceKind.Switch4G => AnShengDeviceCapability.Switch4G,
            AnShengDeviceKind.SpeakerWiFi => AnShengDeviceCapability.SpeakerWiFi,
            AnShengDeviceKind.SwitchWiFi => AnShengDeviceCapability.SwitchWiFi,
            _ => AnShengDeviceCapability.None
        };
    }

    /// <summary>
    /// 是否为 4G 联网款。
    /// </summary>
    /// <param name="kind">设备品类。</param>
    /// <returns>4G 喇叭 / 4G 开关返回 true。</returns>
    public static bool Is4G(this AnShengDeviceKind kind)
        => kind == AnShengDeviceKind.Speaker4G || kind == AnShengDeviceKind.Switch4G;

    /// <summary>
    /// 是否为 WiFi 联网款。
    /// </summary>
    /// <param name="kind">设备品类。</param>
    /// <returns>WiFi 喇叭 / WiFi 开关返回 true。</returns>
    public static bool IsWiFi(this AnShengDeviceKind kind)
        => kind == AnShengDeviceKind.SpeakerWiFi || kind == AnShengDeviceKind.SwitchWiFi;

    /// <summary>
    /// 是否为开关类（含插槽/继电器）。
    /// </summary>
    /// <param name="kind">设备品类。</param>
    /// <returns>4G 开关 / WiFi 开关返回 true。</returns>
    public static bool IsSwitch(this AnShengDeviceKind kind)
        => kind == AnShengDeviceKind.Switch4G || kind == AnShengDeviceKind.SwitchWiFi;

    /// <summary>
    /// 该品类下发报文中是否应携带 <c>timestamp</c> 字段。
    /// 协议原文：「timestamp | int | 秒级时间戳，WiFi 款不支持」。
    /// 未识别品类按保守策略——不注入。
    /// </summary>
    /// <param name="kind">设备品类。</param>
    /// <returns>仅 4G 款返回 true。</returns>
    public static bool SupportsTimestamp(this AnShengDeviceKind kind) => kind.Is4G();

    /// <summary>
    /// 中文显示名，用于日志与错误提示。
    /// </summary>
    /// <param name="kind">设备品类。</param>
    /// <returns>中文名称。</returns>
    public static string ToDisplayName(this AnShengDeviceKind kind)
    {
        return kind switch
        {
            AnShengDeviceKind.Speaker4G => "4G喇叭",
            AnShengDeviceKind.Switch4G => "4G开关",
            AnShengDeviceKind.SpeakerWiFi => "WiFi喇叭",
            AnShengDeviceKind.SwitchWiFi => "WiFi开关",
            _ => "未知品类"
        };
    }
}

/// <summary>
/// 品类推断工具：根据设备上报的 <c>netType</c> / <c>version</c> / <c>model</c> 推断品类。
/// </summary>
public static class AnShengDeviceKindResolver
{
    /// <summary>
    /// 根据联网类型与版本号/型号推断设备品类。
    /// </summary>
    /// <param name="netType">设备上报的 netType（<c>4G</c> / <c>WiFi</c>），可为空。</param>
    /// <param name="version">设备上报的 version（如 <c>SWITCH-EC618X-R24-O-V4.0.8</c>），可为空。</param>
    /// <param name="model">设备上报的 model（如 <c>Air780E</c>），可为空。</param>
    /// <returns>推断出的品类；信息不足时返回 <see cref="AnShengDeviceKind.Unknown"/>。</returns>
    public static AnShengDeviceKind Resolve(string? netType, string? version = null, string? model = null)
    {
        var is4G = IsFourG(netType, model);
        var isWiFi = IsWiFiNet(netType, model);
        if (!is4G && !isWiFi) return AnShengDeviceKind.Unknown;

        var isSwitch = IsSwitchProduct(version, model);
        var isSpeaker = IsSpeakerProduct(version, model);

        if (!isSwitch && !isSpeaker)
        {
            // 联网方式已知但产品线未知：仍无法确定具体品类
            return AnShengDeviceKind.Unknown;
        }

        if (is4G) return isSwitch ? AnShengDeviceKind.Switch4G : AnShengDeviceKind.Speaker4G;
        return isSwitch ? AnShengDeviceKind.SwitchWiFi : AnShengDeviceKind.SpeakerWiFi;
    }

    /// <summary>
    /// 仅根据联网类型推断「是否 4G」，用于决定下发报文是否注入 timestamp。
    /// </summary>
    /// <param name="netType">设备上报的 netType。</param>
    /// <param name="model">设备上报的 model。</param>
    /// <returns>判定为 4G 时返回 true。</returns>
    public static bool IsFourG(string? netType, string? model = null)
    {
        if (!string.IsNullOrWhiteSpace(netType))
        {
            if (netType.Contains("4G", StringComparison.OrdinalIgnoreCase)) return true;
            if (netType.Contains("LTE", StringComparison.OrdinalIgnoreCase)) return true;
        }

        if (!string.IsNullOrWhiteSpace(model))
        {
            // 合宙 Air780E / EC618 系列均为 4G 模组
            if (model.Contains("Air7", StringComparison.OrdinalIgnoreCase)) return true;
            if (model.Contains("EC618", StringComparison.OrdinalIgnoreCase)) return true;
            if (model.Contains("EC7", StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    /// <summary>
    /// 仅根据联网类型推断「是否 WiFi」。
    /// </summary>
    /// <param name="netType">设备上报的 netType。</param>
    /// <param name="model">设备上报的 model。</param>
    /// <returns>判定为 WiFi 时返回 true。</returns>
    public static bool IsWiFiNet(string? netType, string? model = null)
    {
        if (!string.IsNullOrWhiteSpace(netType)
            && netType.Contains("WIFI", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(model))
        {
            if (model.Contains("ESP32", StringComparison.OrdinalIgnoreCase)) return true;
            if (model.Contains("ESP8266", StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    private static bool IsSwitchProduct(string? version, string? model)
    {
        if (!string.IsNullOrWhiteSpace(version)
            && version.StartsWith("SWITCH", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(model)
               && model.Contains("SWITCH", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSpeakerProduct(string? version, string? model)
    {
        if (!string.IsNullOrWhiteSpace(version)
            && (version.StartsWith("SPEAKER", StringComparison.OrdinalIgnoreCase)
                || version.StartsWith("VOICE", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(model)
               && (model.Contains("SPEAKER", StringComparison.OrdinalIgnoreCase)
                   || model.Contains("VOICE", StringComparison.OrdinalIgnoreCase));
    }
}
