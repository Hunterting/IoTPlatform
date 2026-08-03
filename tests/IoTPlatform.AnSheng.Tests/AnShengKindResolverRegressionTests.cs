using IoTPlatform.Infrastructure.Protocol.AnSheng;
using Xunit;

namespace IoTPlatform.AnSheng.Tests;

/// <summary>
/// <see cref="AnShengDeviceKindResolver"/> 的<b>回归网</b>。
///
/// 【这个文件存在的唯一理由】
///   T5 要给品类推断加一条"看 slotAmount"的判据。最省事的写法是直接改 <c>Resolve</c>，
///   但 <c>Resolve</c> 已经被 T1/T2 的 MQTT 上行热路径（<c>LearnDeviceKind</c>）调用——
///   在没有任何回归保护的情况下改它，等于拿生产链路赌运气。
///
///   所以先有这张网，再动代码：本文件<b>只断言 <c>Resolve</c> 的既有行为</b>，
///   一条都不为新功能让步。只要 <c>Resolve</c> 的行为发生任何漂移，这里立刻变红。
///
/// 【维护约定】
///   新增能力请写到 <c>InferKind</c> 的用例里；<b>不要</b>为了让新功能通过而修改本文件里
///   任何一条 <c>Resolve</c> 断言。要改，先证明那是 bug 而不是行为变更。
/// </summary>
public class AnShengKindResolverRegressionTests
{
    // ─────────────────────────────────────────────────────────────
    // 一、Resolve 行为快照：netType × 产品线 的完整矩阵
    // ─────────────────────────────────────────────────────────────

    /// <summary>4G + 开关命名 ⇒ Switch4G。</summary>
    [Theory]
    [InlineData("4G", "SWITCH-EC618X-R24-O-V4.0.8", null)]
    [InlineData("4g", "switch-ec618x", null)]
    [InlineData("LTE", "SWITCH-X", null)]
    [InlineData(null, "SWITCH-X", "Air780E")]
    [InlineData(null, null, "EC618-SWITCH")]
    public void Resolve_Should_Return_Switch4G(string? netType, string? version, string? model)
    {
        Assert.Equal(AnShengDeviceKind.Switch4G, AnShengDeviceKindResolver.Resolve(netType, version, model));
    }

    /// <summary>4G + 喇叭命名 ⇒ Speaker4G。</summary>
    [Theory]
    [InlineData("4G", "SPEAKER-A1", null)]
    [InlineData("4G", "VOICE-A1", null)]
    [InlineData("LTE", "speaker-a1", null)]
    [InlineData(null, "SPEAKER-A1", "Air780E")]
    public void Resolve_Should_Return_Speaker4G(string? netType, string? version, string? model)
    {
        Assert.Equal(AnShengDeviceKind.Speaker4G, AnShengDeviceKindResolver.Resolve(netType, version, model));
    }

    /// <summary>WiFi + 开关命名 ⇒ SwitchWiFi。</summary>
    [Theory]
    [InlineData("WiFi", "SWITCH-ESP-V1", null)]
    [InlineData("wifi", "SWITCH-ESP-V1", null)]
    [InlineData(null, "SWITCH-ESP-V1", "ESP32")]
    [InlineData(null, null, "ESP8266-SWITCH")]
    public void Resolve_Should_Return_SwitchWiFi(string? netType, string? version, string? model)
    {
        Assert.Equal(AnShengDeviceKind.SwitchWiFi, AnShengDeviceKindResolver.Resolve(netType, version, model));
    }

    /// <summary>WiFi + 喇叭命名 ⇒ SpeakerWiFi。</summary>
    [Theory]
    [InlineData("WiFi", "SPEAKER-ESP-V1", null)]
    [InlineData("WiFi", "VOICE-ESP-V1", null)]
    [InlineData(null, "SPEAKER-ESP-V1", "ESP32")]
    public void Resolve_Should_Return_SpeakerWiFi(string? netType, string? version, string? model)
    {
        Assert.Equal(AnShengDeviceKind.SpeakerWiFi, AnShengDeviceKindResolver.Resolve(netType, version, model));
    }

    /// <summary>
    /// 信息不足一律 Unknown。
    /// 这组用例守的是"宁可不判也不瞎判"的底线：判错品类会让能力校验放行设备根本不支持的命令。
    /// </summary>
    [Theory]
    [InlineData(null, null, null)]
    [InlineData("", "", "")]
    [InlineData("4G", null, null)]                   // 联网方式已知、产品线未知
    [InlineData("WiFi", null, null)]                 // 同上
    [InlineData("4G", "UNKNOWN-MODEL-X", null)]      // 命名两边都不像
    [InlineData(null, "SWITCH-X", null)]             // 产品线已知、联网方式未知
    [InlineData("5G", "SWITCH-X", null)]             // 不认识的 netType
    public void Resolve_Should_Return_Unknown_When_Evidence_Insufficient(
        string? netType, string? version, string? model)
    {
        Assert.Equal(AnShengDeviceKind.Unknown, AnShengDeviceKindResolver.Resolve(netType, version, model));
    }

    /// <summary>
    /// <c>Resolve</c> 只有一个必填参数，后两个可选 —— 签名不得改动。
    /// T1/T2 的调用方大量使用 <c>Resolve(netType)</c> 与 <c>Resolve(netType, null, model)</c> 两种形态。
    /// </summary>
    [Fact]
    public void Resolve_Should_Keep_Optional_Parameter_Signature()
    {
        Assert.Equal(AnShengDeviceKind.Unknown, AnShengDeviceKindResolver.Resolve("4G"));
        Assert.Equal(AnShengDeviceKind.Switch4G, AnShengDeviceKindResolver.Resolve("4G", "SWITCH-X"));
        Assert.Equal(AnShengDeviceKind.Switch4G, AnShengDeviceKindResolver.Resolve(null, null, "Air780E-SWITCH"));
    }

    // ─────────────────────────────────────────────────────────────
    // 二、判据提升为 public 后，语义必须与提升前一致
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>LooksLikeSwitch</c> 由原 <c>IsSwitchProduct</c> 提升而来，判据逐条对齐：
    /// version 以 SWITCH 开头，或 model 包含 SWITCH。
    /// </summary>
    [Theory]
    [InlineData("SWITCH-EC618X", null, true)]
    [InlineData("switch-ec618x", null, true)]
    [InlineData("X-SWITCH", null, false)]            // 只认前缀，不认包含
    [InlineData(null, "Air780E-SWITCH", true)]       // model 认包含
    [InlineData(null, "switch", true)]
    [InlineData("SPEAKER-A1", null, false)]
    [InlineData(null, null, false)]
    [InlineData("", "", false)]
    public void LooksLikeSwitch_Should_Match_Legacy_Semantics(string? version, string? model, bool expected)
    {
        Assert.Equal(expected, AnShengDeviceKindResolver.LooksLikeSwitch(version, model));
    }

    /// <summary>
    /// <c>LooksLikeSpeaker</c> 由原 <c>IsSpeakerProduct</c> 提升而来：
    /// version 以 SPEAKER/VOICE 开头，或 model 包含 SPEAKER/VOICE。
    /// </summary>
    [Theory]
    [InlineData("SPEAKER-A1", null, true)]
    [InlineData("VOICE-A1", null, true)]
    [InlineData("voice-a1", null, true)]
    [InlineData("X-SPEAKER", null, false)]           // 只认前缀
    [InlineData(null, "ESP32-VOICE", true)]
    [InlineData("SWITCH-A1", null, false)]
    [InlineData(null, null, false)]
    public void LooksLikeSpeaker_Should_Match_Legacy_Semantics(string? version, string? model, bool expected)
    {
        Assert.Equal(expected, AnShengDeviceKindResolver.LooksLikeSpeaker(version, model));
    }

    // ─────────────────────────────────────────────────────────────
    // 三、InferKind 的向后兼容契约
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 联网方式判不出来时，<c>InferKind</c> 必须原样委托给 <c>Resolve</c>。
    /// 这是"只加不改"承诺的核心：老链路即便误调新方法也不会有行为变化。
    /// </summary>
    [Theory]
    [InlineData(null, null, null)]
    [InlineData(null, "SWITCH-X", null)]
    [InlineData("5G", "SPEAKER-X", null)]
    [InlineData("", "VOICE-X", "")]
    public void InferKind_Should_Delegate_To_Resolve_When_NetType_Unrecognized(
        string? netType, string? version, string? model)
    {
        var expected = AnShengDeviceKindResolver.Resolve(netType, version, model);

        // slotAmount 给什么值都不影响：联网方式定不了就是定不了。
        Assert.Equal(expected, AnShengDeviceKindResolver.InferKind(netType, null, version, model));
        Assert.Equal(expected, AnShengDeviceKindResolver.InferKind(netType, 0, version, model));
        Assert.Equal(expected, AnShengDeviceKindResolver.InferKind(netType, 4, version, model));
    }

    /// <summary>
    /// slotAmount 缺失、但<b>版本前缀能判出产品线</b>时，<c>InferKind</c> 与 <c>Resolve</c> 结论一致。
    ///
    /// 【为什么不能把这条推广到「无 slotAmount 即 ≡ Resolve」】
    ///   那个更强的命题与 N3 直接矛盾。N3 记载 <c>InferKind</c> 的存在意义，
    ///   就是让 <c>("WiFi", null, null)</c> 从 <c>Resolve</c> 的 <c>Unknown</c> 变成 <c>SpeakerWiFi</c>。
    ///   两者在「前缀判不出产品线」时<b>必须不同</b>，见下方 InferKind_Should_Fallback_To_Speaker_* 用例。
    /// </summary>
    [Theory]
    [InlineData("4G", "SWITCH-X", null)]
    [InlineData("4G", "SPEAKER-X", null)]
    [InlineData("WiFi", "SWITCH-X", null)]
    [InlineData("WiFi", "VOICE-X", null)]
    public void InferKind_Without_SlotAmount_Should_Equal_Resolve_When_Prefix_Recognizable(
        string? netType, string? version, string? model)
    {
        Assert.Equal(
            AnShengDeviceKindResolver.Resolve(netType, version, model),
            AnShengDeviceKindResolver.InferKind(netType, null, version, model));
    }

    // ─────────────────────────────────────────────────────────────
    // 四、InferKind 的新增能力（二级判据：slotAmount）
    // ─────────────────────────────────────────────────────────────

    /// <summary>slotAmount &gt; 0 ⇒ 开关款，且优先级高于版本号命名。</summary>
    [Theory]
    [InlineData("4G", 4, AnShengDeviceKind.Switch4G)]
    [InlineData("4G", 1, AnShengDeviceKind.Switch4G)]
    [InlineData("WiFi", 2, AnShengDeviceKind.SwitchWiFi)]
    public void InferKind_Should_Trust_Positive_SlotAmount(
        string netType, int slotAmount, AnShengDeviceKind expected)
    {
        Assert.Equal(expected, AnShengDeviceKindResolver.InferKind(netType, slotAmount, null, null));
    }

    /// <summary>slotAmount == 0 ⇒ 喇叭款。喇叭没有插槽概念，固件报 0 即"我不是开关"。</summary>
    [Theory]
    [InlineData("4G", AnShengDeviceKind.Speaker4G)]
    [InlineData("WiFi", AnShengDeviceKind.SpeakerWiFi)]
    public void InferKind_Should_Treat_Zero_SlotAmount_As_Speaker(string netType, AnShengDeviceKind expected)
    {
        Assert.Equal(expected, AnShengDeviceKindResolver.InferKind(netType, 0, null, null));
    }

    /// <summary>
    /// 设备自报的 slotAmount 是硬事实，命名前缀只是弱证据 —— 两者打架时以 slotAmount 为准。
    /// 现场确实出现过刷了开关固件的喇叭外壳（version 仍写着 SPEAKER）。
    /// </summary>
    [Fact]
    public void InferKind_SlotAmount_Should_Override_Naming_Hint()
    {
        Assert.Equal(
            AnShengDeviceKind.Switch4G,
            AnShengDeviceKindResolver.InferKind("4G", 4, "SPEAKER-A1", null));

        Assert.Equal(
            AnShengDeviceKind.Speaker4G,
            AnShengDeviceKindResolver.InferKind("4G", 0, "SWITCH-EC618X", null));
    }

    /// <summary>
    /// 【验收 #3】联网方式已识别、slotAmount <b>缺失（null）</b>、命名也判不出产品线 ⇒ <b>按喇叭款</b>，不得返回 Unknown。
    ///
    /// 依据：§7-R2（已关闭 2026-08-03）确认 netType 值域仅 {4G, WiFi}，netType 一旦识别，
    /// 产品线判定就是二元的；D3 第 3 级规定版本前缀只是「兜底提示，<b>不作为判定依据</b>」，
    /// 不能因为前缀两边都不像就一票否决成 Unknown。
    ///
    /// 【勿改回 Unknown】这里曾因实现里多写了一个早返回而断言成 Unknown，
    /// 导致 InferKind 在它唯一要解决的场景上退化回 Resolve（N3），验收 #3 未达成。
    /// 断言要对齐规格，不是对齐实现。
    /// </summary>
    [Theory]
    [InlineData("WiFi", AnShengDeviceKind.SpeakerWiFi)]   // 验收 #3 原文：InferKind("WiFi", null, null) == SpeakerWiFi
    [InlineData("4G", AnShengDeviceKind.Speaker4G)]
    public void InferKind_Should_Fallback_To_Speaker_When_Only_NetType_Known(
        string netType, AnShengDeviceKind expected)
    {
        Assert.Equal(expected, AnShengDeviceKindResolver.InferKind(netType, null, null, null));
    }

    /// <summary>
    /// slotAmount 缺失且版本/型号<b>判不出产品线</b>时，同样按喇叭款回落。
    /// 覆盖「有 version 但前缀不认识」与「无 version 仅有 model」两种真实上报形态。
    /// </summary>
    [Theory]
    [InlineData("4G", "UNKNOWN-X", null, AnShengDeviceKind.Speaker4G)]
    [InlineData("4G", null, "Air780E", AnShengDeviceKind.Speaker4G)]
    [InlineData("WiFi", "UNKNOWN-X", null, AnShengDeviceKind.SpeakerWiFi)]
    public void InferKind_Should_Fallback_To_Speaker_When_Prefix_Unrecognizable(
        string? netType, string? version, string? model, AnShengDeviceKind expected)
    {
        Assert.Equal(expected, AnShengDeviceKindResolver.InferKind(netType, null, version, model));
    }

    /// <summary>
    /// 【N3 的核心契约】在「netType 已识别 + 无 slotAmount + 前缀判不出」这一场景上，
    /// <c>InferKind</c> 必须<b>严格强于</b> <c>Resolve</c> —— 这正是新增该重载的全部理由。
    ///
    /// 这条用例是防止有人「为了让回归变绿」把 InferKind 改回委托 Resolve 的护栏：
    /// 一旦两者在此场景下重新相等，本用例立刻变红。
    /// </summary>
    [Fact]
    public void InferKind_Must_Be_Strictly_Stronger_Than_Resolve_On_N3_Scenario()
    {
        // Resolve 保持 T1/T2 语义不变：产品线未知 ⇒ Unknown（这条不许动）
        Assert.Equal(AnShengDeviceKind.Unknown, AnShengDeviceKindResolver.Resolve("WiFi", null, null));

        // InferKind 必须给出确定品类
        Assert.Equal(AnShengDeviceKind.SpeakerWiFi, AnShengDeviceKindResolver.InferKind("WiFi", null, null, null));

        Assert.NotEqual(
            AnShengDeviceKindResolver.Resolve("WiFi", null, null),
            AnShengDeviceKindResolver.InferKind("WiFi", null, null, null));
    }

    // ─────────────────────────────────────────────────────────────
    // 五、Category 派生依赖 ToDisplayName —— 决策 Q8 的取值锁定
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>Device.Category</c> 直接取 <c>Kind.ToDisplayName()</c>（决策 Q8）。
    /// 这四个中文串一旦变化，存量设备的 Category 就会与新建设备不一致，故在此锁死。
    /// </summary>
    [Theory]
    [InlineData(AnShengDeviceKind.Speaker4G, "4G喇叭")]
    [InlineData(AnShengDeviceKind.Switch4G, "4G开关")]
    [InlineData(AnShengDeviceKind.SpeakerWiFi, "WiFi喇叭")]
    [InlineData(AnShengDeviceKind.SwitchWiFi, "WiFi开关")]
    [InlineData(AnShengDeviceKind.Unknown, "未知品类")]
    public void ToDisplayName_Should_Be_Stable(AnShengDeviceKind kind, string expected)
    {
        Assert.Equal(expected, kind.ToDisplayName());
    }
}
