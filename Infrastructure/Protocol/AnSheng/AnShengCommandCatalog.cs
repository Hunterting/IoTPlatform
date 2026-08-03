namespace IoTPlatform.Infrastructure.Protocol.AnSheng;

/// <summary>
/// 安圣「二开设备」MQTT 协议命令目录。
///
/// 数据来源：官方协议文档 <c>asopen.md</c>（唯一事实来源），共 36 个 <c>##</c> 方法小节。
/// 能力分组（文档中每组前的支持矩阵表格）：
///   G1 通用命令        4G喇叭√ 4G开关√ WiFi喇叭√ WiFi开关√
///   G2 MQTT 参数       4G喇叭√ 4G开关√ WiFi喇叭√ WiFi开关√
///   G3 开关动作/延时/电量实时/校准   4G喇叭× 4G开关√ WiFi喇叭× WiFi开关√
///   G4 定时任务/电量统计/日志/RS485  4G喇叭× 4G开关√ WiFi喇叭× WiFi开关×
///   G5 对时/物联卡预警  4G喇叭√ 4G开关√ WiFi喇叭× WiFi开关×
///
/// 重要约定：
///   1. 参数<b>平铺</b>在 JSON 顶层，不存在 <c>param</c> 包裹对象。
///   2. <c>timestamp</c> 为<b>秒级 int</b>，WiFi 款不支持（下发时不得携带）。
///   3. <c>frameId</c> 为字符串，用于请求-应答关联。
///   4. 设备离线由 MQTT 遗嘱消息 <c>{"imei":"x","method":"close"}</c> 表达，
///      <c>close</c> 不是 <c>##</c> 方法小节，因此不计入 <see cref="Count"/>，
///      但属于上行事件，计入 <see cref="EventMethods"/>。
/// </summary>
public static class AnShengCommandCatalog
{
    /// <summary>MQTT 遗嘱（离线）消息的 method 值。</summary>
    public const string WillMethod = "close";

    /// <summary>成功应答的 result 值。</summary>
    public const string ResultOk = "ok";

    /// <summary>设备不支持该命令时的 result 值。</summary>
    public const string ResultUnsupported = "method unsupported";

    private static readonly IReadOnlyList<string> ActionValues = new[] { "on", "off", "toggle" };
    private static readonly IReadOnlyList<string> StartActionValues = new[] { "on", "off", "toggle", "none" };

    private static readonly Dictionary<string, AnShengCommandSpec> CommandMap = BuildCatalog();

    private static readonly HashSet<string> EventMethodSet = new(StringComparer.Ordinal)
    {
        "connected",
        "keyEvent",
        "delayEvent",
        "timeEvent",
        "recv485",
        WillMethod
    };

    /// <summary>全部命令规格（只读），键为 method 名，共 36 条。</summary>
    public static IReadOnlyDictionary<string, AnShengCommandSpec> Commands => CommandMap;

    /// <summary>命令总数（应为 36）。</summary>
    public static int Count => CommandMap.Count;

    /// <summary>
    /// 全部上行事件 method（共 6 个）：
    /// connected / keyEvent / delayEvent / timeEvent / recv485 / close。
    /// 其中 <c>close</c> 来自 MQTT 遗嘱，不在 36 个方法小节内。
    /// </summary>
    public static IReadOnlyCollection<string> EventMethods => EventMethodSet;

    /// <summary>
    /// 尝试获取命令规格。
    /// </summary>
    /// <param name="method">方法名。</param>
    /// <param name="spec">命中的规格。</param>
    /// <returns>命中返回 true。</returns>
    public static bool TryGet(string? method, out AnShengCommandSpec? spec)
    {
        spec = null;
        if (string.IsNullOrWhiteSpace(method)) return false;
        if (!CommandMap.TryGetValue(method, out var found)) return false;
        spec = found;
        return true;
    }

    /// <summary>
    /// 获取命令规格，不存在返回 null。
    /// </summary>
    /// <param name="method">方法名。</param>
    /// <returns>命令规格或 null。</returns>
    public static AnShengCommandSpec? Get(string? method)
        => TryGet(method, out var spec) ? spec : null;

    /// <summary>
    /// 判断某方法是否属于二开协议目录。
    /// </summary>
    /// <param name="method">方法名。</param>
    /// <returns>属于目录返回 true。</returns>
    public static bool Contains(string? method)
        => !string.IsNullOrWhiteSpace(method) && CommandMap.ContainsKey(method);

    /// <summary>
    /// 判断指定品类是否支持指定方法。
    /// 方法不在目录中时返回 false（防止下发协议外的伪命令）。
    /// </summary>
    /// <param name="method">方法名。</param>
    /// <param name="kind">设备品类。</param>
    /// <returns>支持返回 true。</returns>
    public static bool IsSupported(string? method, AnShengDeviceKind kind)
    {
        if (!TryGet(method, out var spec) || spec == null) return false;
        return spec.IsSupportedBy(kind);
    }

    /// <summary>
    /// 判断某方法是否为设备主动上报事件（含遗嘱 <c>close</c>）。
    /// </summary>
    /// <param name="method">方法名。</param>
    /// <returns>是事件返回 true。</returns>
    public static bool IsEvent(string? method)
        => !string.IsNullOrWhiteSpace(method) && EventMethodSet.Contains(method);

    /// <summary>
    /// 列出指定品类支持的全部命令。
    /// </summary>
    /// <param name="kind">设备品类。</param>
    /// <param name="includeEvents">是否包含上行事件，默认 false（仅列可下发命令）。</param>
    /// <param name="includeBeta">是否包含「测试中」命令，默认 true。</param>
    /// <returns>按 method 名排序的命令列表。</returns>
    public static IReadOnlyList<AnShengCommandSpec> ListFor(
        AnShengDeviceKind kind,
        bool includeEvents = false,
        bool includeBeta = true)
    {
        return CommandMap.Values
            .Where(spec => spec.IsSupportedBy(kind))
            .Where(spec => includeEvents || !spec.IsEvent)
            .Where(spec => includeBeta || !spec.IsBeta)
            .OrderBy(spec => spec.Method, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// 列出目录中全部命令。
    /// </summary>
    /// <returns>按 method 名排序的命令列表。</returns>
    public static IReadOnlyList<AnShengCommandSpec> ListAll()
        => CommandMap.Values.OrderBy(spec => spec.Method, StringComparer.Ordinal).ToList();

    // ─────────────────────────────────────────────────────────────
    // 目录构建
    // ─────────────────────────────────────────────────────────────

    private static Dictionary<string, AnShengCommandSpec> BuildCatalog()
    {
        var list = new List<AnShengCommandSpec>
        {
            // ───────── G1 通用命令（4 个品类全部支持）─────────

            Spec("getDevInfo", "获取设备基本信息", AnShengDeviceCapability.GroupCommon,
                "获取设备基本信息（getDevInfo）"),

            Spec("getDevStatus", "获取设备实时状态信息", AnShengDeviceCapability.GroupCommon,
                "获取设备实时状态信息（getDevStatus）",
                P("q", AnShengParamType.String, false,
                    "查询字符串，不传或为空返回全部；可选 slots,EMdata,tasks", minFirmware: "4.0.20")),

            Event("connected", "设备连接MQTT成功事件上报", AnShengDeviceCapability.GroupCommon,
                "设备连接MQTT成功事件上报（connected）"),

            Event("keyEvent", "按键事件上报", AnShengDeviceCapability.GroupCommon,
                "按键事件上报（keyEvent）"),

            Spec("getKeyConfig", "获取按键配置", AnShengDeviceCapability.GroupCommon,
                "获取按键配置（getKeyConfig）"),

            Spec("setKeyConfig", "设置按键配置", AnShengDeviceCapability.GroupCommon,
                "设置按键配置（setKeyConfig）",
                P("mode", AnShengParamType.Int, true,
                    "按键模式。0-无动作；1-切换开关；2-离线切换开关，联网不动作", minimum: 0, maximum: 2),
                P("uploadEnable", AnShengParamType.Bool, true, "是否上报按键事件")),

            Spec("reboot", "重启", AnShengDeviceCapability.GroupCommon,
                "重启（reboot）"),

            Spec("getAutoReport", "获取自动上报配置", AnShengDeviceCapability.GroupCommon,
                "获取自动上报配置（getAutoReport）（测试中）", isBeta: true),

            Spec("setAutoReport", "设置自动上报配置", AnShengDeviceCapability.GroupCommon,
                "设置自动上报配置（setAutoReport）（测试中）", isBeta: true,
                paramSpecs: new[]
                {
                    P("getDevStatusSec", AnShengParamType.Int, true,
                        "设备实时状态自动上报间隔秒数。0-不上报，非 0 时不能小于 30", minimum: 0),
                    P("getDevStatusQ", AnShengParamType.String, false,
                        "自动上报查询字符串，可选 slots,EMdata,tasks", minFirmware: "4.0.20"),
                    P("orderUpSec", AnShengParamType.Int, true,
                        "订单数据自动上报间隔秒数。0-不上报，非 0 时不能小于 30", minimum: 0),
                    P("rs485Sec", AnShengParamType.Int, true,
                        "RS485 自动上报间隔秒数。0-不上报，非 0 时不能小于 30", minimum: 0),
                    P("rs485BaudRate", AnShengParamType.Int, true,
                        "RS485 串口波特率，2400~2000000，默认 115200", minimum: 2400, maximum: 2000000),
                    P("rs485SendWaitMs", AnShengParamType.Int, false,
                        "RS485 多个命令间隔毫秒数，默认 300", minimum: 0),
                    P("rs485Array", AnShengParamType.Array, false, "RS485 下发命令十六进制字符串数组")
                }),

            // ───────── G2 MQTT 参数（4 个品类全部支持）─────────

            Spec("getMqtt", "获取MQTT参数", AnShengDeviceCapability.GroupMqtt,
                "获取MQTT参数（getMqtt）"),

            Spec("setMqtt", "设置MQTT参数", AnShengDeviceCapability.GroupMqtt,
                "设置MQTT参数（setMqtt）",
                P("mqttParams", AnShengParamType.Object, true, "mqtt 参数对象"),
                P("reboot", AnShengParamType.Bool, false, "设置完是否重启")),

            // ───────── G3 开关动作 / 延时任务 / 电量实时 / 校准（仅开关类）─────────

            Spec("action", "插槽开关动作", AnShengDeviceCapability.GroupSwitchAction,
                "插槽开关动作（action）",
                P("slotNum", AnShengParamType.Int, true, "插槽编号，从 1 开始。0 表示所有插槽开关", minimum: 0),
                P("action", AnShengParamType.String, true, "开关动作。on/off/toggle", allowedValues: ActionValues),
                P("hasStopDelayTask", AnShengParamType.Bool, false, "是否停止延时任务")),

            Spec("actions", "多插槽开关动作", AnShengDeviceCapability.GroupSwitchAction,
                "多插槽开关动作（actions）",
                P("slotNums", AnShengParamType.Array, true, "插槽编号数组，子项值从 1 开始"),
                P("action", AnShengParamType.String, true, "开关动作。on/off/toggle", allowedValues: ActionValues),
                P("hasStopDelayTask", AnShengParamType.Bool, false, "是否停止延时任务")),

            Spec("getDelayTasks", "获取延时任务列表", AnShengDeviceCapability.GroupSwitchAction,
                "获取延时任务列表（getDelayTasks）"),

            Spec("startDelayTask", "开始延时任务", AnShengDeviceCapability.GroupSwitchAction,
                "开始延时任务（startDelayTask）",
                P("slotNum", AnShengParamType.Int, true, "插槽编号，从 1 开始。0 表示所有插槽开关", minimum: 0),
                P("enable", AnShengParamType.Bool, true, "是否启用"),
                P("sAction", AnShengParamType.String, true, "开始动作。on/off/toggle/none",
                    allowedValues: StartActionValues),
                P("eAction", AnShengParamType.String, true, "延时结束动作。on/off/toggle",
                    allowedValues: ActionValues),
                P("secs", AnShengParamType.Int, true, "延时秒数", minimum: 0)),

            Spec("stopDelayTask", "停止延时任务", AnShengDeviceCapability.GroupSwitchAction,
                "停止延时任务（stopDelayTask）",
                P("slotNum", AnShengParamType.Int, true, "插槽编号，从 1 开始。0 表示所有插槽开关", minimum: 0)),

            Event("delayEvent", "延时任务事件上报", AnShengDeviceCapability.GroupSwitchAction,
                "延时任务事件上报（delayEvent）"),

            Spec("getEMRealtime", "获取电量计实时信息", AnShengDeviceCapability.GroupSwitchAction,
                "获取电量计实时信息（getEMRealtime）"),

            Spec("getCalParams", "获取校准参数", AnShengDeviceCapability.GroupSwitchAction,
                "获取校准参数（getCalParams）"),

            Spec("setCalParams", "设置校准参数", AnShengDeviceCapability.GroupSwitchAction,
                "设置校准参数（setCalParams）",
                P("calParams", AnShengParamType.Object, true, "校准参数对象，含 RL 校准电阻值")),

            Spec("resetCalParams", "重置校准参数", AnShengDeviceCapability.GroupSwitchAction,
                "重置校准参数（resetCalParams）"),

            Spec("autoCal", "自动校准参数", AnShengDeviceCapability.GroupSwitchAction,
                "自动校准参数（autoCal）",
                P("power", AnShengParamType.Double, true, "自动校准的负载功率")),

            // ───────── G4 定时任务 / 电量统计 / 日志 / RS485（仅 4G 开关）─────────

            Spec("getTimeTasks", "获取所有定时任务", AnShengDeviceCapability.GroupTimeTask,
                "获取所有定时任务（getTimeTasks）"),

            Spec("setTimeTasks", "设置所有定时任务", AnShengDeviceCapability.GroupTimeTask,
                "设置所有定时任务（setTimeTasks）",
                P("tasks", AnShengParamType.Array, true,
                    "定时任务对象数组，按顺序从插槽/开关 1 到 n；每项含 loopTimeTasks / timeTasks")),

            Spec("getSlotTimeTasks", "获取单个插槽/开关定时任务", AnShengDeviceCapability.GroupTimeTask,
                "获取单个插槽/开关定时任务（getSlotTimeTasks）"),

            Spec("setSlotTimeTasks", "设置单个插槽/开关定时任务", AnShengDeviceCapability.GroupTimeTask,
                "设置单个插槽/开关定时任务（setSlotTimeTasks）",
                P("loopTimeTasks", AnShengParamType.Array, false, "循环定时任务对象数组"),
                P("timeTasks", AnShengParamType.Array, false, "普通定时任务对象数组")),

            Event("timeEvent", "上报定时任务事件", AnShengDeviceCapability.GroupTimeTask,
                "上报定时任务事件（timeEvent）", minFirmware: "5.0.1"),

            Spec("getEMStatistics", "获取电量计统计信息", AnShengDeviceCapability.GroupTimeTask,
                "获取电量计统计信息（getEMStatistics）",
                P("q", AnShengParamType.String, false,
                    "查询字符串。all/month/day/hour/hourSum/total，可组合，如 total,day,hour")),

            Spec("clearEMStatistics", "清空电量计统计信息", AnShengDeviceCapability.GroupTimeTask,
                "清空电量计统计信息（clearEMStatistics）",
                P("slotNum", AnShengParamType.Int, false, "插槽编号，不传或 0 表示清空所有插槽", minimum: 0)),

            Spec("getLogs", "获取日志", AnShengDeviceCapability.GroupTimeTask,
                "获取日志（getLogs）",
                P("num", AnShengParamType.Int, false, "最近日志条数，不传表示获取所有", minimum: 1)),

            Spec("send485", "发送RS485命令", AnShengDeviceCapability.GroupTimeTask,
                "发送RS48命令（send485）（测试中）", isBeta: true,
                paramSpecs: new[]
                {
                    P("baudRate", AnShengParamType.Int, false,
                        "串口波特率，2400~2000000，默认 115200", minimum: 2400, maximum: 2000000),
                    P("sendWaitMs", AnShengParamType.Int, false, "多个命令间隔毫秒数，默认 300", minimum: 0),
                    P("dataArray", AnShengParamType.Array, true, "RS485 十六进制命令字符串数组")
                }),

            Event("recv485", "接收RS485数据上传事件", AnShengDeviceCapability.GroupTimeTask,
                "接收RS48数据上传事件（recv485）（测试中）", isBeta: true),

            // ───────── G5 对时 / 物联卡预警（仅 4G 款）─────────

            Spec("setTime", "设置时间", AnShengDeviceCapability.Group4GOnly,
                "设置时间（setTime）",
                P("timestamp", AnShengParamType.Int, true, "秒级时间戳", minimum: 0)),

            Spec("getSimCheck", "获取开机物联卡预警信息", AnShengDeviceCapability.Group4GOnly,
                "获取开机物联卡预警信息（getSimCheck）"),

            Spec("setSimCheck", "设置开机物联卡预警信息", AnShengDeviceCapability.Group4GOnly,
                "设置开机物联卡预警信息（setSimCheck）",
                P("enabled", AnShengParamType.Bool, true, "true-启动，false-不启动"),
                P("leftDays", AnShengParamType.Int, true, "0-播报剩余天数；大于 0 则在剩余天数内播报", minimum: 0),
                P("dataBalance", AnShengParamType.Int, true,
                    "0-播报剩余流量；大于 0 则在剩余流量内播报（单位 MB）", minimum: 0)),

            Spec("simCheck", "物联卡预警", AnShengDeviceCapability.Group4GOnly,
                "物联卡预警（simCheck）")
        };

        var map = new Dictionary<string, AnShengCommandSpec>(list.Count, StringComparer.Ordinal);
        foreach (var spec in list)
        {
            map[spec.Method] = spec;
        }

        return map;
    }

    private static AnShengCommandSpec Spec(
        string method,
        string title,
        AnShengDeviceCapability supported,
        string docAnchor,
        params AnShengParamSpec[] paramSpecs)
        => new()
        {
            Method = method,
            Title = title,
            Direction = AnShengCommandDirection.Downlink,
            SupportedKinds = supported,
            Params = paramSpecs,
            IsEvent = false,
            IsBeta = false,
            MinFirmware = null,
            DocAnchor = docAnchor
        };

    private static AnShengCommandSpec Spec(
        string method,
        string title,
        AnShengDeviceCapability supported,
        string docAnchor,
        bool isBeta,
        string? minFirmware = null,
        AnShengParamSpec[]? paramSpecs = null)
        => new()
        {
            Method = method,
            Title = title,
            Direction = AnShengCommandDirection.Downlink,
            SupportedKinds = supported,
            Params = paramSpecs ?? Array.Empty<AnShengParamSpec>(),
            IsEvent = false,
            IsBeta = isBeta,
            MinFirmware = minFirmware,
            DocAnchor = docAnchor
        };

    private static AnShengCommandSpec Event(
        string method,
        string title,
        AnShengDeviceCapability supported,
        string docAnchor,
        bool isBeta = false,
        string? minFirmware = null)
        => new()
        {
            Method = method,
            Title = title,
            Direction = AnShengCommandDirection.Uplink,
            SupportedKinds = supported,
            Params = Array.Empty<AnShengParamSpec>(),
            IsEvent = true,
            IsBeta = isBeta,
            MinFirmware = minFirmware,
            DocAnchor = docAnchor
        };

    private static AnShengParamSpec P(
        string name,
        AnShengParamType type,
        bool required,
        string description,
        string? minFirmware = null,
        IReadOnlyList<string>? allowedValues = null,
        double? minimum = null,
        double? maximum = null)
        => AnShengParamSpec.Create(name, type, required, description, minFirmware, allowedValues, minimum, maximum);
}
