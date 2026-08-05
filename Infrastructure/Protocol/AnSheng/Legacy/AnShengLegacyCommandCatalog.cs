namespace IoTPlatform.Infrastructure.Protocol.AnSheng.Legacy;

/// <summary>
/// Legacy 充电桩协议族命令目录（T14 协议族归位）。
///
/// 【定位】与 <see cref="AnShengCommandCatalog"/>（二开协议，36 条）<b>平级</b>的第二份目录，
/// 收录的方法全部显式标记 <see cref="AnShengProtocolFamily.ChargingPile"/>。
/// 两份目录之外的一切 method 都属于「不认识」——必须快速失败，不得静默按 Legacy 外发。
///
/// 【收录标准】确属旧版充电桩协议、且当前仍有真实链路在用的下行方法。
/// <b>不得</b>收录任何伪命令（<c>setSwitch</c> / <c>getSwitchStatus</c> /
/// <c>setSwitchConfig</c> / <c>getSwitchConfig</c>）——它们不属于任何协议，已在前后端物理删除。
/// 也<b>不得</b>与 <see cref="AnShengCommandCatalog"/> 重复登记：同名两处会造成
/// 「同一 method 两种报文结构」的歧义（<c>AnShengLegacyWhitelistTests</c> 有护栏用例）。
///
/// 【报文结构】param 包裹 + 毫秒字符串 timestamp，见 <see cref="AnShengLegacyCommandBuilder"/>。
/// </summary>
public static class AnShengLegacyCommandCatalog
{
    /// <summary>开始充电订单。</summary>
    public const string OrderStart = "orderStart";

    /// <summary>结束充电订单。</summary>
    public const string OrderEnd = "orderEnd";

    /// <summary>充电订单数据（平台下推 / 设备上报共用同一 method 名）。</summary>
    public const string OrderUp = "orderUp";

    private static readonly Dictionary<string, AnShengCommandSpec> CommandMap = BuildCatalog();

    private static readonly HashSet<string> MethodSet = new(CommandMap.Keys, StringComparer.Ordinal);

    /// <summary>全部 Legacy 命令规格（只读），键为 method 名。</summary>
    public static IReadOnlyDictionary<string, AnShengCommandSpec> Commands => CommandMap;

    /// <summary>全部 Legacy method 名（只读集合，Ordinal 比较）。</summary>
    public static IReadOnlyCollection<string> Methods => MethodSet;

    /// <summary>Legacy 命令总数（应为 3）。</summary>
    public static int Count => CommandMap.Count;

    /// <summary>
    /// 判断某方法是否属于 Legacy 充电桩协议族。<b>大小写敏感</b>（Ordinal）：
    /// <c>OrderStart</c> 不等于 <c>orderStart</c>，拼写错误必须被判成「不认识」。
    /// </summary>
    /// <param name="method">方法名，可为 null。</param>
    /// <returns>属于本协议族返回 true。</returns>
    public static bool Contains(string? method)
        => !string.IsNullOrWhiteSpace(method) && CommandMap.ContainsKey(method);

    /// <summary>
    /// 尝试获取 Legacy 命令规格。
    /// </summary>
    /// <param name="method">方法名。</param>
    /// <param name="spec">命中的规格；未命中为 null。</param>
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
    /// 获取 Legacy 命令规格，不存在返回 null。
    /// </summary>
    /// <param name="method">方法名。</param>
    /// <returns>命令规格或 null。</returns>
    public static AnShengCommandSpec? Get(string? method)
        => TryGet(method, out var spec) ? spec : null;

    /// <summary>
    /// 构建 Legacy 目录。
    ///
    /// 【为什么 <c>SupportedKinds = None</c>】<see cref="AnShengDeviceCapability"/> 的 4 个位
    /// 描述的是<b>二开设备</b>品类（4G/WiFi × 喇叭/开关），充电桩不属于其中任何一个。
    /// 本族命令<b>不参与</b>品类闸门（<c>AnShengCommandGuard</c> 对 Legacy 方法跳过规格校验），
    /// 因此这里如实填 None，而不是编一个「全支持」出来误导后续读者。
    ///
    /// 【为什么 <c>Params</c> 为空】Legacy 协议无正式参数规格文档（asopen.md 不含这些方法），
    /// 编一份规格出来只会凭空产生一道现网可能过不去的校验。
    /// 保持空集合 = 保持改造前「不校验参数」的既有行为（T14 最小变更）。
    /// </summary>
    /// <returns>method → 规格 的字典。</returns>
    private static Dictionary<string, AnShengCommandSpec> BuildCatalog()
    {
        var map = new Dictionary<string, AnShengCommandSpec>(StringComparer.Ordinal);

        void Add(string method, string title, string docAnchor)
        {
            map[method] = new AnShengCommandSpec
            {
                Method = method,
                Title = title,
                Direction = AnShengCommandDirection.Downlink,
                ProtocolFamily = AnShengProtocolFamily.ChargingPile,
                SupportedKinds = AnShengDeviceCapability.None,
                Params = Array.Empty<AnShengParamSpec>(),
                IsEvent = false,
                IsBeta = false,
                DocAnchor = docAnchor
            };
        }

        Add(OrderStart, "开始充电（Legacy 充电桩）", "Legacy/充电桩协议");
        Add(OrderEnd, "结束充电（Legacy 充电桩）", "Legacy/充电桩协议");
        Add(OrderUp, "充电订单数据（Legacy 充电桩）", "Legacy/充电桩协议");

        return map;
    }
}
