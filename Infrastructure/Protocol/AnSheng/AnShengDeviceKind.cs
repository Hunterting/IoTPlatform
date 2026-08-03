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
    /// 根据联网类型 + <b>插槽数量</b> + 版本号/型号推断设备品类（T5 增强版）。
    ///
    /// 【与 <see cref="Resolve"/> 的关系 —— 为什么新增重载而不是改原方法】
    ///   <see cref="Resolve"/> 已被 T1/T2 的 MQTT 上行热路径（<c>LearnDeviceKind</c>）调用，
    ///   改它等于在没有回归网的地方动生产代码。这里<b>只加不改</b>：
    ///   信息不足时本方法直接委托 <see cref="Resolve"/>，保证行为完全向后兼容。
    ///
    /// 【三级判据，从强到弱】
    ///   1. <b>联网类型</b>（netType / model）：判不出 4G 也判不出 WiFi ⇒ 退回 <see cref="Resolve"/>，
    ///      不做任何自作聪明的猜测。
    ///   2. <b>插槽数量</b>（slotAmount）：设备<b>显式声明</b>的值，权威度最高。
    ///      <c>&gt; 0</c> 判为开关款，<c>== 0</c> 判为喇叭款。
    ///      喇叭款没有插槽概念，固件要么不报、要么报 0，两种都能被正确落到喇叭分支。
    ///   3. <b>版本号/型号前缀</b>：slotAmount 缺失时的<b>弱提示</b>（<c>SWITCH-*</c> / <c>SPEAKER-*</c>）。
    ///      按 D3 第 3 级，前缀只是「兜底提示，<b>不作为判定依据</b>」，因此
    ///      <b>不像开关即按喇叭处理</b>，<b>不得</b>因「两边都不像」而返回 <see cref="AnShengDeviceKind.Unknown"/>：
    ///      netType 已识别时产品线是二元的（§7-R2 已关闭：netType 值域仅 {4G, WiFi}），
    ///      让弱证据拥有否决权会使本方法在 <c>("WiFi", null, null)</c> 上退化回 <see cref="Resolve"/>（见 N3）。
    /// </summary>
    /// <param name="netType">设备上报的 netType（<c>4G</c> / <c>WiFi</c>），可为空。</param>
    /// <param name="slotAmount">设备上报的插槽数量；<c>null</c> 表示设备未上报该字段。</param>
    /// <param name="version">设备上报的 version，可为空。</param>
    /// <param name="model">设备上报的 model，可为空。</param>
    /// <returns>
    /// 推断出的品类。<b>仅当联网方式（netType/model）也判不出来时</b>才可能返回
    /// <see cref="AnShengDeviceKind.Unknown"/>（此时已委托 <see cref="Resolve"/>）；
    /// netType 一旦识别，必定返回四个具体品类之一。
    /// </returns>
    public static AnShengDeviceKind InferKind(
        string? netType,
        int? slotAmount,
        string? version = null,
        string? model = null)
    {
        var is4G = IsFourG(netType, model);
        var isWiFi = IsWiFiNet(netType, model);

        // 一级：联网方式都判不出来。slotAmount 再准也定不了「4G开关」还是「WiFi开关」，
        // 直接委托原方法，行为与 T1/T2 完全一致。
        if (!is4G && !isWiFi)
        {
            return Resolve(netType, version, model);
        }

        bool isSwitch;

        if (slotAmount.HasValue)
        {
            // 二级：插槽数是设备自报的硬事实，直接采信，不再看版本号前缀。
            isSwitch = slotAmount.Value > 0;
        }
        else
        {
            // 三级：slotAmount 缺失，只能参考命名前缀。
            //
            // 【为什么这里不能返回 Unknown】——曾经的缺陷点，勿回退：
            //   netType 已经识别出来了，而安圣确认 netType 值域仅 {4G, WiFi}（§7-R2 已关闭），
            //   对应的产品线判定是二元的：不像开关，就按喇叭处理。
            //   D3 第 3 级白纸黑字写着版本前缀是「兜底提示，不作为判定依据」，
            //   一旦让「前缀两边都不像」触发 Unknown，等于把最弱的证据升格成一票否决权，
            //   并直接导致验收 #3 的 InferKind("WiFi", null, null) 判成 Unknown 而非 SpeakerWiFi
            //   —— 那样 InferKind 就在它被创造出来要解决的唯一场景上退化回了 Resolve（见 N3）。
            isSwitch = LooksLikeSwitch(version, model);
        }

        if (is4G)
        {
            return isSwitch ? AnShengDeviceKind.Switch4G : AnShengDeviceKind.Speaker4G;
        }

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

    /// <summary>
    /// 版本号/型号「看起来像开关款」。
    ///
    /// 【为什么从 private 提升为 public】
    ///   T5 的 <see cref="InferKind"/> 与 Profile 服务都需要单独复用这条判据
    ///   （例如 slotAmount 缺失时只想问「像不像开关」，而不想跑完整的品类推断）。
    ///   提升可见性而非复制一份，避免两处判据随时间漂移。
    ///
    /// 【它只是"像"，不是"是"】命名前缀是弱证据，
    ///   有 slotAmount 时务必优先采信 slotAmount（见 <see cref="InferKind"/> 二级判据）。
    /// </summary>
    /// <param name="version">设备上报的 version，可为空。</param>
    /// <param name="model">设备上报的 model，可为空。</param>
    /// <returns>命名特征指向开关款时返回 true。</returns>
    public static bool LooksLikeSwitch(string? version, string? model)
    {
        if (!string.IsNullOrWhiteSpace(version)
            && version.StartsWith("SWITCH", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(model)
               && model.Contains("SWITCH", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 版本号/型号「看起来像喇叭款」。理由同 <see cref="LooksLikeSwitch"/>。
    /// </summary>
    /// <param name="version">设备上报的 version，可为空。</param>
    /// <param name="model">设备上报的 model，可为空。</param>
    /// <returns>命名特征指向喇叭款时返回 true。</returns>
    public static bool LooksLikeSpeaker(string? version, string? model)
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

    // 原有私有判据保留为薄转发，确保 Resolve 的实现文本不被改动。
    private static bool IsSwitchProduct(string? version, string? model)
        => LooksLikeSwitch(version, model);

    private static bool IsSpeakerProduct(string? version, string? model)
        => LooksLikeSpeaker(version, model);
}
