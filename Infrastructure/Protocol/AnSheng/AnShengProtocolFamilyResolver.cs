using IoTPlatform.Infrastructure.Protocol.AnSheng.Legacy;

namespace IoTPlatform.Infrastructure.Protocol.AnSheng;

/// <summary>
/// 协议族判定的<b>唯一真相来源</b>（T14）。
///
/// 【它替换了什么】改造前下发侧的判据是「<c>AnShengCommandCatalog.Contains(method)</c> 为假 ⇒ 按 Legacy 下发」——
/// 一条<b>兜底放行</b>策略。任何拼写错误（<c>orderStrat</c>）或协议外方法（<c>getSwitchConfig</c>）
/// 都会被真实构造成 Legacy 报文发往现网设备，调用方还会收到「成功」。
///
/// 【改造后】判定是<b>三态</b>的，且第三态只能失败：
/// <code>
///   method ∈ AnShengCommandCatalog        → OpenProtocol（参数平铺，秒级 int timestamp）
///   method ∈ AnShengLegacyCommandCatalog  → ChargingPile（param 包裹，毫秒字符串 timestamp）
///   其余                                   → 不认识，快速失败，零报文出网
/// </code>
/// 协议族取自规格上的显式字段 <see cref="AnShengCommandSpec.ProtocolFamily"/>，
/// 而不是「在不在某张表里」的副作用推断。
///
/// 【纯静态、无状态】可在适配器、命令服务、Guard、前端契约生成处任意调用，不引入依赖。
/// </summary>
public static class AnShengProtocolFamilyResolver
{
    /// <summary>
    /// 判定 method 所属协议族。
    /// </summary>
    /// <param name="method">协议方法名，可为 null。</param>
    /// <param name="family">命中的协议族；未命中时为 <see cref="AnShengProtocolFamily.OpenProtocol"/>（无意义，勿用）。</param>
    /// <param name="spec">命中的命令规格；未命中时为 null。</param>
    /// <returns>命中任一协议族返回 true；<b>返回 false 即「不认识」，调用方必须拒绝下发</b>。</returns>
    public static bool TryResolve(string? method, out AnShengProtocolFamily family, out AnShengCommandSpec? spec)
    {
        family = AnShengProtocolFamily.OpenProtocol;
        spec = null;

        if (string.IsNullOrWhiteSpace(method)) return false;

        if (AnShengCommandCatalog.TryGet(method, out var openSpec) && openSpec != null)
        {
            family = openSpec.ProtocolFamily;   // 显式字段，而非「命中了这张表」的推断
            spec = openSpec;
            return true;
        }

        if (AnShengLegacyCommandCatalog.TryGet(method, out var legacySpec) && legacySpec != null)
        {
            family = legacySpec.ProtocolFamily;
            spec = legacySpec;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 判定 method 所属协议族；不认识返回 null。
    /// </summary>
    /// <param name="method">协议方法名，可为 null。</param>
    /// <returns>协议族；两份目录都没有时返回 null。</returns>
    public static AnShengProtocolFamily? Resolve(string? method)
        => TryResolve(method, out var family, out _) ? family : null;

    /// <summary>
    /// 是否属于 Legacy 充电桩协议族（<c>orderStart</c> / <c>orderEnd</c> / <c>orderUp</c>）。
    /// </summary>
    /// <param name="method">协议方法名，可为 null。</param>
    /// <returns>属于充电桩协议族返回 true。</returns>
    public static bool IsChargingPile(string? method)
        => TryResolve(method, out var family, out _) && family == AnShengProtocolFamily.ChargingPile;

    /// <summary>
    /// 是否属于安圣二开协议族（<c>asopen.md</c> 的 36 条）。
    /// </summary>
    /// <param name="method">协议方法名，可为 null。</param>
    /// <returns>属于二开协议族返回 true。</returns>
    public static bool IsOpenProtocol(string? method)
        => TryResolve(method, out var family, out _) && family == AnShengProtocolFamily.OpenProtocol;

    /// <summary>
    /// 是否为平台<b>认识</b>的 method（属于任一协议族）。不认识的一律禁止下发。
    /// </summary>
    /// <param name="method">协议方法名，可为 null。</param>
    /// <returns>认识返回 true。</returns>
    public static bool IsKnown(string? method) => TryResolve(method, out _, out _);
}
