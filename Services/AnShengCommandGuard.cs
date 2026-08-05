// 创建于 T7-3。命令下发的<b>单点校验闸门</b>（设计文档 §5 / 决策 D1）。
//
// 【为什么必须是纯函数】
//   验收 #1（品类不支持）/ #2（slotNum 越界）/ #3（固件不足）要求断言
//   「拒绝原因 + MQTT 零发布」。若校验逻辑埋在 AnShengCommandService 里，
//   要验它就得起 WebApplicationFactory + 数据库 + MQTT 替身，一条用例几百毫秒，
//   而且「零发布」只能靠间接观察。
//
//   把决策抽成同步纯函数后：入参是一个 POCO，出参是一个 POCO，
//   三条验收各自是一个毫秒级单元测试，且「没走到下发」是<b>结构性保证</b>
//   （Guard 根本不持有适配器，物理上发不出去）。
//
// 【本类不注入任何东西】
//   没有 AppDbContext、没有 IProtocolAdapterFactory、没有 ILogger。
//   一旦有人往构造函数里加依赖，上面那条「毫秒级单测」的性质立刻失效 ——
//   这是审查时要盯死的一条红线（测试纪律：Guard 单测不得引用 AppDbContext / WebApplicationFactory）。
//
// 【谁来喂上下文】
//   AnShengCommandService 负责把「设备品类 / 固件 / 插槽数」查出来塞进 AnShengCommandContext。
//   品类走降级链：Profile.Kind → 适配器 GetDeviceKind(imei) → Unknown（决策 D7）。

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using IoTPlatform.Infrastructure.Protocol.AnSheng;
using IoTPlatform.Models;

namespace IoTPlatform.Services;

/// <summary>
/// 一次命令下发的<b>全部判据</b>，由 <see cref="AnShengCommandGuard"/> 消费。
///
/// 【为什么是 POCO 而不是一堆方法参数】判据会随协议演进不断增加
/// （T13 的二次确认、后续的限频、黑名单……）。用上下文对象承载，
/// 新增判据不会把 <see cref="AnShengCommandGuard.Evaluate"/> 的签名冲垮，
/// 也不会让每个调用点都跟着改。
/// </summary>
public sealed class AnShengCommandContext
{
    /// <summary>平台设备主键；未认领设备为 null（仅用于日志与记录）。</summary>
    public long? DeviceId { get; init; }

    /// <summary>目标设备 IMEI。</summary>
    public string Imei { get; init; } = string.Empty;

    /// <summary>安圣协议 method，如 <c>action</c>。</summary>
    public string Method { get; init; } = string.Empty;

    /// <summary>平铺下发参数，可为 null（无参命令）。</summary>
    public IReadOnlyDictionary<string, object?>? Parameters { get; init; }

    /// <summary>
    /// 设备品类。<see cref="AnShengDeviceKind.Unknown"/> 表示「无法判定」，
    /// 默认放行（<c>AnShengCommandSpec.IsSupportedBy(Unknown) == true</c> 是 T7 之前就有的安全阀）。
    /// </summary>
    public AnShengDeviceKind Kind { get; init; } = AnShengDeviceKind.Unknown;

    /// <summary>
    /// 品类是否来自能力档案（<c>AnShengDeviceProfile</c>）。
    /// <c>false</c> 表示是从适配器内存快照或默认值推来的 —— 仅用于日志分级，不影响判定。
    /// </summary>
    public bool KindFromProfile { get; init; }

    /// <summary>设备当前固件版本串；null / 无法解析时固件校验一律<b>放行</b>（不误拦截）。</summary>
    public string? Firmware { get; init; }

    /// <summary>
    /// 设备插槽数量；null 或非正数表示未知，此时<b>跳过</b>插槽越界校验。
    ///
    /// 【为什么未知就不校验】编造一个「默认 4 路」会把 8 路设备的合法请求打死。
    /// 越界校验的价值来自真实档案，没档案就退回协议层的 <c>minimum:0</c> 兜底。
    /// </summary>
    public int? SlotAmount { get; init; }

    /// <summary>
    /// 品类未知时是否拒绝下发；取自 <c>AnShengCommandOptions.RejectWhenKindUnknown</c>，默认 false。
    /// </summary>
    public bool RejectWhenKindUnknown { get; init; }

    /// <summary>
    /// 是否允许规格外的未知参数，默认 true（协议后续可能新增字段，严了会挡住新固件）。
    /// </summary>
    public bool AllowUnknownParams { get; init; } = true;

    /// <summary>
    /// 本 method 是否属于 <b>Legacy 充电桩白名单</b>（<c>orderStart</c> / <c>orderEnd</c> / <c>orderUp</c>）。
    ///
    /// 【为什么必须有这个开关】这三个方法确属旧版充电桩协议、现网仍有真实链路在用，
    /// 但它们<b>不在</b> <c>AnShengCommandCatalog</c> 里（目录只收二开协议）。
    /// 若 Guard 一律按「目录里没有 ⇒ 未知方法」拒绝，等于把现网充电桩业务打死 ——
    /// 而 T7 是重构，不得改变存量可用性。
    ///
    /// 放行后 <c>Spec</c> 为 null，后续参数/插槽/固件环节<b>全部跳过</b>（无规格可依）；
    /// 真正的「默认拒绝」由适配器在构造报文前完成 —— T14 起改由
    /// <c>AnShengProtocolFamilyResolver</c> 做三态判定，两族都不认识即抛
    /// <c>NotSupportedException</c>，零报文出网。
    ///
    /// 【本开关该怎么赋值】调用方应写
    /// <c>AllowLegacyMethod = AnShengProtocolFamilyResolver.Resolve(method) == AnShengProtocolFamily.ChargingPile</c>，
    /// 即「确认它属于充电桩族」，而不是「它不在二开目录里」—— 后者会把拼写错误一并放行。
    /// </summary>
    public bool AllowLegacyMethod { get; init; }

    /// <summary>
    /// 调用方是否已完成二次确认。T7 恒不参与判定，为 T13 高危命令（<c>setMqtt</c> / <c>reboot</c>）预留。
    /// </summary>
    public bool Confirmed { get; init; }
}

/// <summary>
/// 闸门判定结果。<b>不可变</b>，且「放行」与「拒绝」互斥。
/// </summary>
public sealed class AnShengCommandDecision
{
    private AnShengCommandDecision(
        bool allowed,
        AnShengCommandRejectReason? reason,
        IReadOnlyList<string> errors,
        AnShengCommandSpec? spec,
        string? requiredFirmware)
    {
        Allowed = allowed;
        Reason = reason;
        Errors = errors;
        Spec = spec;
        RequiredFirmware = requiredFirmware;
    }

    /// <summary>是否放行。</summary>
    public bool Allowed { get; }

    /// <summary>
    /// 拒绝原因；<see cref="Allowed"/> 为 true 时恒为 null。
    /// <b>这是验收 #1/#2/#3 唯一该断言的字段</b> —— 文案会改，枚举不会。
    /// </summary>
    public AnShengCommandRejectReason? Reason { get; }

    /// <summary>逐条中文原因；放行时为空集合，永不为 null。</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>
    /// 命中的命令规格。
    /// 放行后由 <c>AnShengCommandService</c> 复用它（避免再查一次目录）。
    ///
    /// 为 null 有两种情形：① 被拒且没走到目录命中；② <b>Legacy 充电桩方法放行</b>
    /// （二开目录里本就没有它们的规格，见 <c>AnShengCommandContext.AllowLegacyMethod</c>）。
    ///
    /// 【T14 起不得再用本字段推断协议族】历史写法是「<c>Spec</c> 非 null → <c>BuildRaw</c>；
    /// null → 走 Legacy 构造」。这个推断在「被拒绝」时同样成立（那时 <c>Spec</c> 也是 null），
    /// 两种语义撞在同一个取值上，纯属巧合可用。协议族现在有<b>显式</b>判据：
    /// <c>AnShengProtocolFamilyResolver.Resolve(method)</c> —— 请用它选择报文构建方式。
    /// 本字段只回答「有没有规格可依」，不回答「这条命令属于哪个协议族」。
    /// </summary>
    public AnShengCommandSpec? Spec { get; }

    /// <summary>
    /// 满足本次下发所需的<b>最低</b>固件版本；仅
    /// <see cref="AnShengCommandRejectReason.RejectedByFirmware"/> 时有值。
    /// 前端据此提示「请先升级到 x.y.z」。
    /// </summary>
    public string? RequiredFirmware { get; }

    /// <summary>把 <see cref="Errors"/> 拼成一句话；放行时为 null。</summary>
    public string? ErrorMessage => Errors.Count == 0 ? null : string.Join("；", Errors);

    /// <summary>
    /// 构造一个放行结果。
    /// </summary>
    /// <param name="spec">命中的命令规格；Legacy 充电桩方法为 null。</param>
    /// <returns>放行结果。</returns>
    public static AnShengCommandDecision Allow(AnShengCommandSpec? spec)
        => new(true, null, Array.Empty<string>(), spec, null);

    /// <summary>
    /// 构造一个拒绝结果。
    /// </summary>
    /// <param name="reason">拒绝原因。</param>
    /// <param name="errors">逐条中文原因。</param>
    /// <param name="spec">命中的命令规格，可为 null。</param>
    /// <param name="requiredFirmware">所需最低固件版本，可为 null。</param>
    /// <returns>拒绝结果。</returns>
    public static AnShengCommandDecision Reject(
        AnShengCommandRejectReason reason,
        IReadOnlyList<string> errors,
        AnShengCommandSpec? spec = null,
        string? requiredFirmware = null)
        => new(false, reason, errors.Count == 0 ? Array.Empty<string>() : errors, spec, requiredFirmware);

    /// <summary>
    /// 构造一个单条原因的拒绝结果。
    /// </summary>
    /// <param name="reason">拒绝原因。</param>
    /// <param name="error">中文原因。</param>
    /// <param name="spec">命中的命令规格，可为 null。</param>
    /// <param name="requiredFirmware">所需最低固件版本，可为 null。</param>
    /// <returns>拒绝结果。</returns>
    public static AnShengCommandDecision Reject(
        AnShengCommandRejectReason reason,
        string error,
        AnShengCommandSpec? spec = null,
        string? requiredFirmware = null)
        => new(false, reason, new[] { error }, spec, requiredFirmware);
}

/// <summary>
/// 安圣命令下发闸门 —— 「这条命令能不能发」的<b>唯一</b>判定处（决策 D1）。
///
/// 【生命周期】Scoped（与消费方 <c>AnShengCommandService</c> 一致）。
///   本类<b>无状态</b>，理论上 Singleton 亦可；选 Scoped 只是为了将来若需要注入
///   租户上下文/审计上下文时不必改注册。绝不可在此持有可变字段。
///
/// 【五个环节，短路求值】
/// <code>
///   ① CheckMethodKnown → RejectedByUnknownMethod   （目录没有 / 是上行事件）
///   ② CheckKind        → RejectedByKind            （品类不支持；严格模式下含 Unknown）
///   ③ CheckParams      → RejectedByValidation      （必填/类型/枚举/范围）
///   ④ CheckSlotRange   → RejectedByValidation      （slotNum 超出设备实际插槽数）
///   ⑤ CheckFirmware    → RejectedByFirmware        （命令级或参数级固件门槛）
///   ⑥ CheckConfirm     → 恒放行                     （T13 预留）
/// </code>
///
/// 【为什么固件放最后】固件不足是「设备该升级」，参数不合法是「调用方写错了」。
/// 先报后者更有指导性；否则老固件设备的一切参数错误都会被固件提示盖住。
/// </summary>
public sealed class AnShengCommandGuard
{
    /// <summary>
    /// 判定一条命令是否允许下发。<b>同步纯函数</b>：无 IO、无副作用、同输入必同输出。
    /// </summary>
    /// <param name="context">下发上下文。</param>
    /// <returns>判定结果。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> 为 null。</exception>
    public AnShengCommandDecision Evaluate(AnShengCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // ① 方法必须存在于协议目录，且必须是可下发方向。
        var methodDecision = CheckMethodKnown(context, out var spec);
        if (methodDecision != null) return methodDecision;

        // Legacy 充电桩方法：目录里没有规格，后续四个环节无从校验，直接放行。
        // 真正的外发闸门由适配器白名单兜底（T4）。
        if (spec == null) return AnShengCommandDecision.Allow(null);

        // ② 品类能力。
        var kindDecision = CheckKind(context, spec);
        if (kindDecision != null) return kindDecision;

        // 参数与固件共用一次校验，再按违规种类分流 —— 保证「同一份规格只解释一遍」。
        spec.ValidateParams(
            context.Parameters,
            context.Firmware,
            context.AllowUnknownParams,
            out var violations);

        // ③ 参数规格（剔除固件类违规，它们归第 ⑤ 环节）。
        var paramDecision = CheckParams(spec, violations);
        if (paramDecision != null) return paramDecision;

        // ④ 插槽越界（依赖设备档案的 SlotAmount，协议规格本身给不出上界）。
        var slotDecision = CheckSlotRange(context, spec);
        if (slotDecision != null) return slotDecision;

        // ⑤ 固件门槛。
        var firmwareDecision = CheckFirmware(spec, violations);
        if (firmwareDecision != null) return firmwareDecision;

        // ⑥ 二次确认（T7 恒放行）。
        var confirmDecision = CheckConfirm(context, spec);
        if (confirmDecision != null) return confirmDecision;

        return AnShengCommandDecision.Allow(spec);
    }

    /// <summary>
    /// ① 方法名必须在目录中，且不能是设备上行事件。
    /// </summary>
    /// <param name="context">下发上下文。</param>
    /// <param name="spec">命中的规格；未命中时为 null。</param>
    /// <returns>需要短路时返回拒绝结果；否则 null。</returns>
    private static AnShengCommandDecision? CheckMethodKnown(
        AnShengCommandContext context,
        out AnShengCommandSpec? spec)
    {
        spec = null;

        var method = context.Method?.Trim();
        if (string.IsNullOrEmpty(method))
        {
            return AnShengCommandDecision.Reject(
                AnShengCommandRejectReason.RejectedByUnknownMethod,
                "命令 method 不能为空");
        }

        if (!AnShengCommandCatalog.TryGet(method, out var found) || found == null)
        {
            // Legacy 充电桩协议族：目录里没有它们，但现网仍在用，由调用方显式放行。
            if (context.AllowLegacyMethod)
            {
                return null;   // spec 保持 null，Evaluate 会据此直接放行
            }

            return AnShengCommandDecision.Reject(
                AnShengCommandRejectReason.RejectedByUnknownMethod,
                $"协议目录中不存在命令 {method}");
        }

        if (found.IsEvent || found.Direction == AnShengCommandDirection.Uplink)
        {
            return AnShengCommandDecision.Reject(
                AnShengCommandRejectReason.RejectedByUnknownMethod,
                $"{method} 是设备上报事件，平台不可下发",
                found);
        }

        spec = found;
        return null;
    }

    /// <summary>
    /// ② 品类能力校验（验收 #1）。
    ///
    /// <see cref="AnShengDeviceKind.Unknown"/> 默认放行（决策 D7）：存量设备大多没有能力档案，
    /// 一律拒绝等于把现网老设备打死；<c>RejectWhenKindUnknown=true</c> 时才进严格模式。
    /// </summary>
    /// <param name="context">下发上下文。</param>
    /// <param name="spec">命中的规格。</param>
    /// <returns>需要短路时返回拒绝结果；否则 null。</returns>
    private static AnShengCommandDecision? CheckKind(
        AnShengCommandContext context,
        AnShengCommandSpec spec)
    {
        if (context.Kind == AnShengDeviceKind.Unknown)
        {
            if (context.RejectWhenKindUnknown)
            {
                return AnShengCommandDecision.Reject(
                    AnShengCommandRejectReason.RejectedByKind,
                    $"设备品类未知（无能力档案），严格模式下拒绝下发 {spec.Method}",
                    spec);
            }

            // 放行：IsSupportedBy(Unknown) 恒为 true，这里提前返回只是让意图显式。
            return null;
        }

        if (!spec.IsSupportedBy(context.Kind))
        {
            return AnShengCommandDecision.Reject(
                AnShengCommandRejectReason.RejectedByKind,
                $"{context.Kind.ToDisplayName()} 不支持命令 {spec.Method}",
                spec);
        }

        return null;
    }

    /// <summary>
    /// ③ 参数规格校验（验收 #2 的协议层部分）。固件类违规在此<b>跳过</b>，交由第 ⑤ 环节。
    /// </summary>
    /// <param name="spec">命中的规格。</param>
    /// <param name="violations">一次性算好的全部违规。</param>
    /// <returns>需要短路时返回拒绝结果；否则 null。</returns>
    private static AnShengCommandDecision? CheckParams(
        AnShengCommandSpec spec,
        IReadOnlyList<AnShengParamViolation> violations)
    {
        var blocking = violations
            .Where(v => v.Kind != AnShengParamViolationKind.FirmwareTooLow)
            .Select(v => v.Message)
            .ToArray();

        if (blocking.Length == 0) return null;

        return AnShengCommandDecision.Reject(
            AnShengCommandRejectReason.RejectedByValidation,
            blocking,
            spec);
    }

    /// <summary>
    /// ④ 插槽越界校验（验收 #2 的档案层部分）。
    ///
    /// 协议目录只能声明 <c>slotNum &gt;= 0</c>，「上界是几」取决于这台设备到底有几路 ——
    /// 那是 <c>AnShengDeviceProfile.SlotAmount</c> 才知道的事。
    /// 约定：<c>slotNum = 0</c> 表示「所有插槽」，故合法区间是 <c>[0, SlotAmount]</c>；
    /// 数组形式 <c>slotNums</c> 的子项从 1 起，合法区间是 <c>[1, SlotAmount]</c>。
    /// </summary>
    /// <param name="context">下发上下文。</param>
    /// <param name="spec">命中的规格。</param>
    /// <returns>需要短路时返回拒绝结果；否则 null。</returns>
    private static AnShengCommandDecision? CheckSlotRange(
        AnShengCommandContext context,
        AnShengCommandSpec spec)
    {
        var slotAmount = context.SlotAmount;
        if (slotAmount is not > 0) return null;          // 未知插槽数：不编造上界
        if (context.Parameters == null || context.Parameters.Count == 0) return null;

        var errors = new List<string>();

        if (context.Parameters.TryGetValue("slotNum", out var single))
        {
            var slot = TryReadInt(single);
            if (slot.HasValue && (slot.Value < 0 || slot.Value > slotAmount.Value))
            {
                errors.Add($"参数 slotNum 越界：设备共 {slotAmount.Value} 路，允许 0~{slotAmount.Value}（0 表示全部），实际 {slot.Value}");
            }
        }

        if (context.Parameters.TryGetValue("slotNums", out var multiple))
        {
            foreach (var item in EnumerateValues(multiple))
            {
                var slot = TryReadInt(item);
                if (slot.HasValue && (slot.Value < 1 || slot.Value > slotAmount.Value))
                {
                    errors.Add($"参数 slotNums 子项越界：设备共 {slotAmount.Value} 路，允许 1~{slotAmount.Value}，实际 {slot.Value}");
                }
            }
        }

        if (errors.Count == 0) return null;

        return AnShengCommandDecision.Reject(
            AnShengCommandRejectReason.RejectedByValidation,
            errors,
            spec);
    }

    /// <summary>
    /// ⑤ 固件门槛校验（验收 #3，决策 D5 选「直接拦截」而非静默降级）。
    /// </summary>
    /// <param name="spec">命中的规格。</param>
    /// <param name="violations">一次性算好的全部违规。</param>
    /// <returns>需要短路时返回拒绝结果；否则 null。</returns>
    private static AnShengCommandDecision? CheckFirmware(
        AnShengCommandSpec spec,
        IReadOnlyList<AnShengParamViolation> violations)
    {
        var firmwareViolations = violations
            .Where(v => v.Kind == AnShengParamViolationKind.FirmwareTooLow)
            .ToArray();

        if (firmwareViolations.Length == 0) return null;

        // 多条固件违规时取「门槛最高」的那个：升到它才能一次性满足全部要求。
        string? required = null;
        foreach (var violation in firmwareViolations)
        {
            if (string.IsNullOrWhiteSpace(violation.MinFirmware)) continue;
            if (required == null || AnShengFirmwareVersion.Compare(violation.MinFirmware, required) > 0)
            {
                required = violation.MinFirmware;
            }
        }

        return AnShengCommandDecision.Reject(
            AnShengCommandRejectReason.RejectedByFirmware,
            firmwareViolations.Select(v => v.Message).ToArray(),
            spec,
            required);
    }

    /// <summary>
    /// ⑥ 高危命令二次确认。<b>T7 恒放行</b>，为 T13 预留。
    ///
    /// 保留这个空环节而不是等到 T13 再插入，是为了让「拒绝原因枚举里的
    /// <see cref="AnShengCommandRejectReason.RejectedByConfirm"/> 到底该在哪一步产生」
    /// 现在就有确定答案，避免将来插错位置（比如插在品类之前，导致品类不支持的命令
    /// 先被要求确认，体验荒谬）。
    /// </summary>
    /// <param name="context">下发上下文。</param>
    /// <param name="spec">命中的规格。</param>
    /// <returns>T7 恒为 null。</returns>
    private static AnShengCommandDecision? CheckConfirm(
        AnShengCommandContext context,
        AnShengCommandSpec spec)
    {
        _ = context;
        _ = spec;
        return null;
    }

    /// <summary>
    /// 尽力把一个 JSON 值读成整数。读不出来返回 null（类型问题已由参数规格校验负责报错）。
    /// </summary>
    /// <param name="value">原始值。</param>
    /// <returns>整数值；无法解释时为 null。</returns>
    private static int? TryReadInt(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case int i:
                return i;
            case long l when l is >= int.MinValue and <= int.MaxValue:
                return (int)l;
            case short s:
                return s;
            case byte b:
                return b;
            case JsonElement { ValueKind: JsonValueKind.Number } element:
                return element.TryGetInt32(out var parsed) ? parsed : null;
            case string text:
                return int.TryParse(text, out var fromText) ? fromText : null;
            default:
                try
                {
                    return Convert.ToInt32(value);
                }
                catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
                {
                    return null;
                }
        }
    }

    /// <summary>
    /// 把一个「可能是数组」的值展开成元素序列。非数组返回空序列。
    /// </summary>
    /// <param name="value">原始值。</param>
    /// <returns>元素序列，永不为 null。</returns>
    private static IEnumerable<object?> EnumerateValues(object? value)
    {
        switch (value)
        {
            case null:
                yield break;

            case JsonElement { ValueKind: JsonValueKind.Array } element:
                foreach (var item in element.EnumerateArray())
                {
                    yield return item;
                }

                yield break;

            case string:
                yield break;   // 字符串也是 IEnumerable，必须先排除

            case System.Collections.IEnumerable sequence:
                foreach (var item in sequence)
                {
                    yield return item;
                }

                yield break;

            default:
                yield break;
        }
    }
}

/// <summary>
/// 敏感值掩码器（T7 决策 D3）—— 「留痕打码、下发明文」这条口径的唯一实现处。
///
/// 【作用范围（只有这四处）】
/// <code>
///   AnShengCommandRecord.RequestJson   ← MaskRequestJson
///   AnShengCommandRecord.ResponseJson  ← MaskResponseJson
///   日志                                ← MaskParameters / MaskRequestJson
///   GET /api/v1/ansheng/commands/{id}  ← 直接读上面两列，天然已掩码
/// </code>
/// <b>实际发布到 MQTT 的报文不经过本类</b>：设备只认明文口令，打码等于把命令发废。
///
/// 【为什么必须深拷贝】
///   下发用的参数字典与留痕用的是<b>同一个对象引用</b>。若原地把 password 改成 "***"
///   再序列化，紧接着构建报文时拿到的就是打码后的值 —— 设备会收到一个字面量 "***" 的口令，
///   MQTT 直接连不上，而且这种 bug 只在「配了口令的那台设备」上出现，极难复现。
///   故本类的所有方法一律<b>产出新对象</b>，绝不修改入参。
///
/// 【敏感字段从哪来】<c>AnShengCommandCatalog</c> 的参数规格
/// （<c>AnShengParamSpec.IsSecret</c> / <c>SecretFields</c>）。不维护第二份关键字黑名单 ——
/// 两份清单必然会在某次协议更新后不一致，而不一致的那一半就是泄漏。
/// </summary>
public static class AnShengSecretMasker
{
    /// <summary>掩码替换值。固定字面量，便于日志检索与测试断言。</summary>
    public const string Mask = "***";

    /// <summary>报文无法解析成 JSON 且疑似含敏感字段时的整体替代文本（失败关闭）。</summary>
    public const string UnparsableMask = "[unparsable payload masked]";

    /// <summary>
    /// 序列化选项：不转义中文与常见符号，保证留痕可读（这些 JSON 只进数据库、不进 HTML）。
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    /// <summary>
    /// 取某个 method 下全部敏感字段名，三个来源合并：
    /// <code>
    ///   ① 顶层敏感参数名                 AnShengParamSpec.IsSecret
    ///   ② 对象型参数内部的敏感子字段名   AnShengParamSpec.SecretFields
    ///   ③ 设备应答中的敏感字段名         AnShengCommandSpec.ResponseSecretFields   ← T7 补全
    /// </code>
    ///
    /// 【为什么必须有 ③】<c>getMqtt</c> 是一条<b>无参数</b>命令，口令只出现在应答里。
    /// 只取 ①②，<c>SecretFieldNames("getMqtt")</c> 恒为空集 ⇒ <see cref="MaskResponseJson"/>
    /// 原样返回 ⇒ 设备回读的 <c>password</c> 明文落进
    /// <c>AnShengCommandRecord.ResponseJson</c>，再经 <c>GET /commands/{id}</c> 明文外泄。
    /// 这与本类文档「只掩下行不掩上行，等于留了一扇后门」的口径直接冲突（QA Round 1 · P0-2）。
    ///
    /// 【方向合并是安全的】本方法只被 <see cref="MaskResponseJson"/> 消费；
    /// 下发侧掩码走 <see cref="MaskParameters"/>（逐参数按规格判定，不经过本方法），
    /// 因此把应答敏感字段并进来<b>不会</b>影响下发报文，更不会打码到真正发出去的明文口令。
    /// </summary>
    /// <param name="method">安圣协议 method。</param>
    /// <returns>字段名集合（大小写不敏感）；无敏感字段时为空集合，永不为 null。</returns>
    public static IReadOnlyCollection<string> SecretFieldNames(string? method)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(method)) return names;
        if (!AnShengCommandCatalog.TryGet(method, out var spec) || spec == null) return names;

        foreach (var param in spec.Params)
        {
            if (param.IsSecret)
            {
                names.Add(param.Name);
            }

            if (param.SecretFields is { Count: > 0 })
            {
                foreach (var field in param.SecretFields)
                {
                    names.Add(field);
                }
            }
        }

        // ③ 应答方向的敏感字段（getMqtt.password 等只在上行出现的机密）。
        if (spec.ResponseSecretFields is { Count: > 0 })
        {
            foreach (var field in spec.ResponseSecretFields)
            {
                names.Add(field);
            }
        }

        return names;
    }

    /// <summary>
    /// 产出一份<b>掩码后的参数副本</b>，供日志与留痕使用。入参不被修改。
    /// </summary>
    /// <param name="method">安圣协议 method。</param>
    /// <param name="parameters">原始平铺参数，可为 null。</param>
    /// <returns>新的字典；入参为 null 时返回空字典。</returns>
    public static IReadOnlyDictionary<string, object?> MaskParameters(
        string? method,
        IReadOnlyDictionary<string, object?>? parameters)
    {
        var masked = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (parameters == null || parameters.Count == 0) return masked;

        AnShengCommandSpec? spec = null;
        if (!string.IsNullOrWhiteSpace(method))
        {
            AnShengCommandCatalog.TryGet(method, out spec);
        }

        foreach (var (key, value) in parameters)
        {
            var paramSpec = spec?.Params.FirstOrDefault(p => string.Equals(p.Name, key, StringComparison.Ordinal));

            if (paramSpec == null)
            {
                masked[key] = value;
                continue;
            }

            // 顶层敏感参数：整值替换。值本身为 null 时不产生占位（"缺失就是缺失"）。
            if (paramSpec.IsSecret)
            {
                masked[key] = value == null ? null : Mask;
                continue;
            }

            // 对象型参数：只打掉点名的子字段，其余原样保留（host/port/clientId 是排障必需）。
            if (paramSpec.SecretFields is { Count: > 0 })
            {
                masked[key] = MaskNestedFields(value, paramSpec.SecretFields);
                continue;
            }

            masked[key] = value;
        }

        return masked;
    }

    /// <summary>
    /// 产出下发参数的<b>掩码后 JSON</b>，直接写入 <c>AnShengCommandRecord.RequestJson</c>。
    /// </summary>
    /// <param name="method">安圣协议 method。</param>
    /// <param name="parameters">原始平铺参数，可为 null。</param>
    /// <returns>JSON 字符串；无参数时为 <c>{}</c>。</returns>
    public static string MaskRequestJson(string? method, IReadOnlyDictionary<string, object?>? parameters)
    {
        var masked = MaskParameters(method, parameters);
        try
        {
            return JsonSerializer.Serialize(masked, SerializerOptions);
        }
        catch (NotSupportedException)
        {
            // 参数里混进了不可序列化的对象（理论上不该发生）。失败关闭：宁可丢留痕也不能泄漏。
            return "{}";
        }
    }

    /// <summary>
    /// 对设备应答原文做掩码，写入 <c>AnShengCommandRecord.ResponseJson</c>。
    ///
    /// 【为什么应答也要掩码】<c>getMqtt</c> 会把当前 MQTT 配置<b>连口令一起</b>回读；
    /// 只掩下行不掩上行，等于留了一扇后门。
    /// </summary>
    /// <param name="method">安圣协议 method。</param>
    /// <param name="json">设备应答原文，可为 null。</param>
    /// <returns>掩码后的 JSON；无敏感字段时原样返回。</returns>
    public static string? MaskResponseJson(string? method, string? json)
        => MaskJson(json, SecretFieldNames(method));

    /// <summary>
    /// 按字段名递归掩码任意 JSON 文本。
    ///
    /// 【为什么按名字递归而不是按路径】设备应答的嵌套形状随固件版本变（有时 <c>mqttParams</c>
    /// 包一层、有时平铺）。按路径匹配会在设备改形状时静默失效；按名字递归的代价是
    /// 「同名的非敏感字段也会被打码」—— 这个方向的错误是安全的。
    /// </summary>
    /// <param name="json">原始 JSON 文本，可为 null。</param>
    /// <param name="secretNames">需要打码的字段名集合（大小写不敏感）。</param>
    /// <returns>掩码后的 JSON；无需掩码时原样返回。</returns>
    public static string? MaskJson(string? json, IReadOnlyCollection<string> secretNames)
    {
        if (string.IsNullOrWhiteSpace(json)) return json;
        if (secretNames == null || secretNames.Count == 0) return json;

        // 快速排除：文本里根本没出现任何敏感字段名，就不必解析（应答动辄几十 KB）。
        var mayContain = secretNames.Any(name =>
            json.Contains(name, StringComparison.OrdinalIgnoreCase));
        if (!mayContain) return json;

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            // 解析不了又疑似含敏感字段：失败关闭，整体替换。
            return UnparsableMask;
        }

        if (node == null) return json;

        MaskNode(node, secretNames);

        try
        {
            return node.ToJsonString(SerializerOptions);
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException)
        {
            return UnparsableMask;
        }
    }

    /// <summary>
    /// 把一个对象型参数值转成 <see cref="JsonObject"/> 副本，并打掉点名的子字段。
    /// </summary>
    /// <param name="value">原始值（可能是字典 / JsonElement / POCO）。</param>
    /// <param name="fields">需要打码的子字段名。</param>
    /// <returns>掩码后的新对象；无法解释为 JSON 对象时原样返回入参。</returns>
    private static object? MaskNestedFields(object? value, IReadOnlyList<string> fields)
    {
        if (value == null) return null;

        JsonNode? node;
        try
        {
            // SerializeToNode 天然产出一份全新的树，等价于深拷贝，绝不会碰到入参对象。
            node = JsonSerializer.SerializeToNode(value, SerializerOptions);
        }
        catch (NotSupportedException)
        {
            // 无法内省的对象：失败关闭，整体打码，绝不原样落库。
            return Mask;
        }

        if (node is not JsonObject obj) return value;

        foreach (var field in fields)
        {
            if (!obj.TryGetPropertyValue(field, out var existing)) continue;
            if (existing == null) continue;   // 值为 null：不产生占位

            obj[field] = JsonValue.Create(Mask);
        }

        return obj;
    }

    /// <summary>
    /// 递归遍历 JSON 树，就地把命中的属性值替换为 <see cref="Mask"/>。
    /// 传入的 <paramref name="node"/> 已经是本类自己解析出来的副本，就地改不影响任何外部对象。
    /// </summary>
    /// <param name="node">当前节点。</param>
    /// <param name="secretNames">需要打码的字段名集合。</param>
    private static void MaskNode(JsonNode node, IReadOnlyCollection<string> secretNames)
    {
        switch (node)
        {
            case JsonObject obj:
            {
                // 先收集 key，避免在枚举过程中改集合。
                var keys = obj.Select(pair => pair.Key).ToArray();
                foreach (var key in keys)
                {
                    var child = obj[key];
                    if (child == null) continue;   // 值为 null：不产生占位

                    if (secretNames.Contains(key, StringComparer.OrdinalIgnoreCase))
                    {
                        obj[key] = JsonValue.Create(Mask);
                        continue;
                    }

                    MaskNode(child, secretNames);
                }

                break;
            }

            case JsonArray array:
            {
                foreach (var item in array)
                {
                    if (item == null) continue;
                    MaskNode(item, secretNames);
                }

                break;
            }
        }
    }
}
