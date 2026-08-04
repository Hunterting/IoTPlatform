// T7 QA：命令闸门（AnShengCommandGuard）与掩码器的验收单测。
//
// 【为什么这个文件必须存在】
//   t7-command-refactor-design.md §9.2 把验收 #1 / #2 / #3 明确定为「单元测试」层的判据：
//   决策正确性由单测守护，链路贯通才由集成测试守护。工程师交付时这三条的单测缺失，
//   本文件补齐，使「拒绝理由是否判对」这件事有毫秒级、无 DB、无 MQTT 的回归锁。
//
// 【断言口径】一律断言「设计要求的行为」，而非「实现当前的输出」：
//   拒绝码取自 AnShengCommandRejectReason 枚举语义，固件门槛值取自协议原文 4.0.20，
//   插槽上界取自 Profile.SlotAmount —— 任何一条被实现改坏都应当在这里变红。

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using IoTPlatform.Configuration;
using IoTPlatform.Infrastructure.Protocol.AnSheng;
using IoTPlatform.Models;
using IoTPlatform.Services;
using Xunit;

namespace IoTPlatform.AnSheng.Tests;

/// <summary>
/// 命令闸门判定的验收单测（设计 §9.2 验收 #1 / #2 / #3 + §9.3 补充不变式）。
/// </summary>
public class AnShengCommandGuardTests
{
    private static readonly AnShengCommandGuard Guard = new();

    /// <summary>构造一个下发上下文；未指定的维度取「最宽松」值，确保用例只考察目标环节。</summary>
    private static AnShengCommandContext Ctx(
        string method,
        AnShengDeviceKind kind = AnShengDeviceKind.Switch4G,
        Dictionary<string, object?>? parameters = null,
        string? firmware = "AnSheng_Switch_V4.0.20",
        int? slotAmount = null,
        bool rejectWhenKindUnknown = false)
        => new()
        {
            DeviceId = 1,
            Imei = "864536072949900",
            Method = method,
            Kind = kind,
            KindFromProfile = true,
            Firmware = firmware,
            SlotAmount = slotAmount,
            RejectWhenKindUnknown = rejectWhenKindUnknown,
            Parameters = parameters
        };

    // ─────────────────────────────────────────────────────────────────────
    // 验收 #1：品类不支持 ⇒ RejectedByKind
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 验收 #1：WiFi 喇叭下发开关动作 <c>action</c> 必须被品类环节拦下。
    /// <c>action</c> 属能力组 G3（GroupSwitchAction = Switch4G | SwitchWiFi），喇叭不在其中。
    /// </summary>
    [Fact]
    public void Guard_SpeakerWiFi_Action_RejectedByKind()
    {
        var decision = Guard.Evaluate(Ctx(
            "action",
            AnShengDeviceKind.SpeakerWiFi,
            new Dictionary<string, object?> { ["slotNum"] = 1, ["action"] = "on" }));

        Assert.False(decision.Allowed);
        Assert.Equal(AnShengCommandRejectReason.RejectedByKind, decision.Reason);
        Assert.NotEmpty(decision.Errors);
        // 文案必须点名命令，前端才能给出可读提示。
        Assert.Contains(decision.Errors, e => e.Contains("action", StringComparison.Ordinal));
    }

    /// <summary>4G 喇叭同样不支持开关动作 —— 防止「只判了 WiFi 喇叭」的片面实现蒙混过关。</summary>
    [Fact]
    public void Guard_Speaker4G_Action_RejectedByKind()
    {
        var decision = Guard.Evaluate(Ctx(
            "action",
            AnShengDeviceKind.Speaker4G,
            new Dictionary<string, object?> { ["slotNum"] = 1, ["action"] = "on" }));

        Assert.False(decision.Allowed);
        Assert.Equal(AnShengCommandRejectReason.RejectedByKind, decision.Reason);
    }

    /// <summary>反向用例：开关类设备下发 <c>action</c> 必须放行，证明上面的拦截不是「一律拒绝」。</summary>
    [Theory]
    [InlineData(AnShengDeviceKind.Switch4G)]
    [InlineData(AnShengDeviceKind.SwitchWiFi)]
    public void Guard_SwitchKinds_Action_Allowed(AnShengDeviceKind kind)
    {
        var decision = Guard.Evaluate(Ctx(
            "action",
            kind,
            new Dictionary<string, object?> { ["slotNum"] = 1, ["action"] = "on" }));

        Assert.True(decision.Allowed, decision.ErrorMessage);
        Assert.Null(decision.Reason);
    }

    // ─────────────────────────────────────────────────────────────────────
    // 验收 #2：slotNum 超出设备插槽数 ⇒ RejectedByValidation
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 验收 #2：设备 4 路，下发 <c>slotNum:9</c> 必须以 <c>RejectedByValidation</c> 拒绝。
    /// 上界来自设备档案而非协议目录（目录只声明 min:0，给不出上界）。
    /// </summary>
    [Fact]
    public void Guard_SlotNum_ExceedsSlotAmount_RejectedByValidation()
    {
        var decision = Guard.Evaluate(Ctx(
            "action",
            AnShengDeviceKind.Switch4G,
            new Dictionary<string, object?> { ["slotNum"] = 9, ["action"] = "on" },
            slotAmount: 4));

        Assert.False(decision.Allowed);
        Assert.Equal(AnShengCommandRejectReason.RejectedByValidation, decision.Reason);
        // 错误文案必须同时给出「越界的参数」与「设备实际路数」，否则运维无法自助定位。
        Assert.Contains(decision.Errors, e =>
            e.Contains("slotNum", StringComparison.Ordinal) && e.Contains("4", StringComparison.Ordinal));
    }

    /// <summary>
    /// 验收 #2 边界（防 off-by-one）：<c>slotNum == SlotAmount</c> 是合法上界，必须放行。
    /// 若实现误写成 <c>&gt;=</c>，最后一路继电器将永远打不开 —— 这是现网级事故。
    /// </summary>
    [Fact]
    public void Guard_SlotNum_EqualsSlotAmount_Allowed()
    {
        var decision = Guard.Evaluate(Ctx(
            "action",
            AnShengDeviceKind.Switch4G,
            new Dictionary<string, object?> { ["slotNum"] = 4, ["action"] = "on" },
            slotAmount: 4));

        Assert.True(decision.Allowed, decision.ErrorMessage);
    }

    /// <summary><c>slotNum:0</c> 约定为「所有插槽」，必须放行。</summary>
    [Fact]
    public void Guard_SlotNum_Zero_MeansAllSlots_Allowed()
    {
        var decision = Guard.Evaluate(Ctx(
            "action",
            AnShengDeviceKind.Switch4G,
            new Dictionary<string, object?> { ["slotNum"] = 0, ["action"] = "on" },
            slotAmount: 4));

        Assert.True(decision.Allowed, decision.ErrorMessage);
    }

    /// <summary>插槽数未知（无档案）时不得编造上界，否则会误伤存量设备。</summary>
    [Fact]
    public void Guard_SlotAmountUnknown_NoRangeCheck()
    {
        var decision = Guard.Evaluate(Ctx(
            "action",
            AnShengDeviceKind.Switch4G,
            new Dictionary<string, object?> { ["slotNum"] = 99, ["action"] = "on" },
            slotAmount: null));

        Assert.True(decision.Allowed, decision.ErrorMessage);
    }

    // ─────────────────────────────────────────────────────────────────────
    // 验收 #3：固件不足 ⇒ 拦截（决策 D5：拦截而非静默降级）
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 验收 #3：固件 4.0.8 下发 <c>getDevStatus{q:"slots"}</c> 必须以
    /// <c>RejectedByFirmware</c> 拦截，并回传门槛版本 4.0.20 供前端提示升级。
    /// 决策 D5 明确否决「悄悄剔除 q 参数照发」的降级方案 —— 静默语义偏差比失败更危险。
    /// </summary>
    [Fact]
    public void Guard_Firmware408_GetDevStatusWithQ_RejectedByFirmware()
    {
        var decision = Guard.Evaluate(Ctx(
            "getDevStatus",
            AnShengDeviceKind.Switch4G,
            new Dictionary<string, object?> { ["q"] = "slots" },
            firmware: "AnSheng_Switch_V4.0.8"));

        Assert.False(decision.Allowed);
        Assert.Equal(AnShengCommandRejectReason.RejectedByFirmware, decision.Reason);
        Assert.Equal("4.0.20", decision.RequiredFirmware);
    }

    /// <summary>验收 #3 旁路：不传受门槛约束的可选参数时，门槛不应生效。</summary>
    [Fact]
    public void Guard_ParamNotProvided_FirmwareGateSkipped()
    {
        var decision = Guard.Evaluate(Ctx(
            "getDevStatus",
            AnShengDeviceKind.Switch4G,
            new Dictionary<string, object?>(),
            firmware: "AnSheng_Switch_V4.0.8"));

        Assert.True(decision.Allowed, decision.ErrorMessage);
    }

    /// <summary>
    /// 验收 #3 旁路（D5 边界规则）：版本号解析不了就放行。
    /// 存量设备版本号格式不统一，「解析失败即拦截」会造成大面积误伤。
    /// </summary>
    [Theory]
    [InlineData("garbage")]
    [InlineData("")]
    [InlineData(null)]
    public void Guard_UnparsableOrMissingVersion_Allowed(string? firmware)
    {
        var decision = Guard.Evaluate(Ctx(
            "getDevStatus",
            AnShengDeviceKind.Switch4G,
            new Dictionary<string, object?> { ["q"] = "slots" },
            firmware: firmware));

        Assert.True(decision.Allowed, decision.ErrorMessage);
    }

    /// <summary>固件达标时同一条命令必须放行，证明门槛不是「一律拒绝」。</summary>
    [Fact]
    public void Guard_Firmware4020_GetDevStatusWithQ_Allowed()
    {
        var decision = Guard.Evaluate(Ctx(
            "getDevStatus",
            AnShengDeviceKind.Switch4G,
            new Dictionary<string, object?> { ["q"] = "slots" },
            firmware: "AnSheng_Switch_V4.0.20"));

        Assert.True(decision.Allowed, decision.ErrorMessage);
    }

    // ─────────────────────────────────────────────────────────────────────
    // §9.3 补充不变式
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>目录中不存在的方法必须以专属拒绝码回绝，不能混入 Validation。</summary>
    [Fact]
    public void Guard_UnknownMethod_RejectedByUnknownMethod()
    {
        var decision = Guard.Evaluate(Ctx("thisMethodDoesNotExist", AnShengDeviceKind.Switch4G));

        Assert.False(decision.Allowed);
        Assert.Equal(AnShengCommandRejectReason.RejectedByUnknownMethod, decision.Reason);
    }

    /// <summary>设备上报事件不可被平台下发（方向校验）。</summary>
    [Fact]
    public void Guard_UplinkEventMethod_CannotBeSent()
    {
        var decision = Guard.Evaluate(Ctx("keyEvent", AnShengDeviceKind.Switch4G));

        Assert.False(decision.Allowed);
        Assert.Equal(AnShengCommandRejectReason.RejectedByUnknownMethod, decision.Reason);
    }

    /// <summary>决策 D7：品类未知时默认放行，避免上线当天打死无档案的存量设备。</summary>
    [Fact]
    public void Guard_KindUnknown_DefaultAllows()
    {
        var decision = Guard.Evaluate(Ctx(
            "action",
            AnShengDeviceKind.Unknown,
            new Dictionary<string, object?> { ["slotNum"] = 1, ["action"] = "on" }));

        Assert.True(decision.Allowed, decision.ErrorMessage);
    }

    /// <summary>决策 D7：严格模式开关必须真的生效，否则配置项形同虚设。</summary>
    [Fact]
    public void Guard_KindUnknown_StrictMode_Rejects()
    {
        var decision = Guard.Evaluate(Ctx(
            "action",
            AnShengDeviceKind.Unknown,
            new Dictionary<string, object?> { ["slotNum"] = 1, ["action"] = "on" },
            rejectWhenKindUnknown: true));

        Assert.False(decision.Allowed);
        Assert.Equal(AnShengCommandRejectReason.RejectedByKind, decision.Reason);
    }

    /// <summary>参数取值不在允许集合内 ⇒ RejectedByValidation（与越界共用同一拒绝码）。</summary>
    [Fact]
    public void Guard_ActionValueNotAllowed_RejectedByValidation()
    {
        var decision = Guard.Evaluate(Ctx(
            "action",
            AnShengDeviceKind.Switch4G,
            new Dictionary<string, object?> { ["slotNum"] = 1, ["action"] = "explode" }));

        Assert.False(decision.Allowed);
        Assert.Equal(AnShengCommandRejectReason.RejectedByValidation, decision.Reason);
    }

    /// <summary>必填参数缺失 ⇒ RejectedByValidation。</summary>
    [Fact]
    public void Guard_MissingRequiredParam_RejectedByValidation()
    {
        var decision = Guard.Evaluate(Ctx(
            "action",
            AnShengDeviceKind.Switch4G,
            new Dictionary<string, object?> { ["slotNum"] = 1 }));

        Assert.False(decision.Allowed);
        Assert.Equal(AnShengCommandRejectReason.RejectedByValidation, decision.Reason);
    }

    /// <summary>Guard 是纯函数：同一入参连续判定必须得到一致结论。</summary>
    [Fact]
    public void Guard_IsPureFunction_RepeatedEvaluationIsStable()
    {
        var ctx = Ctx(
            "action",
            AnShengDeviceKind.SpeakerWiFi,
            new Dictionary<string, object?> { ["slotNum"] = 1, ["action"] = "on" });

        var first = Guard.Evaluate(ctx);
        var second = Guard.Evaluate(ctx);

        Assert.Equal(first.Allowed, second.Allowed);
        Assert.Equal(first.Reason, second.Reason);
        Assert.Equal(first.ErrorMessage, second.ErrorMessage);
    }
}

/// <summary>
/// 敏感参数掩码器的单测（决策 D3 + 风险登记 R7）。
/// </summary>
public class AnShengSecretMaskerTests
{
    /// <summary>构造一份带明文口令的 setMqtt 参数。</summary>
    private static Dictionary<string, object?> MqttParams(string password = "P@ssw0rd!")
        => new()
        {
            ["mqttParams"] = new Dictionary<string, object?>
            {
                ["host"] = "mqtt.example.com",
                ["port"] = 1883,
                ["clientId"] = "dev-001",
                ["username"] = "iot",
                ["password"] = password
            },
            ["reboot"] = true
        };

    /// <summary>D3：落库用的 RequestJson 中口令必须变成固定三星，且不泄露原文长度。</summary>
    [Fact]
    public void Masker_SetMqttPassword_MaskedToStars()
    {
        var json = AnShengSecretMasker.MaskRequestJson("setMqtt", MqttParams("SuperLongSecret123456"));

        Assert.Contains("\"password\"", json, StringComparison.Ordinal);
        Assert.Contains(AnShengSecretMasker.Mask, json, StringComparison.Ordinal);
        Assert.DoesNotContain("SuperLongSecret123456", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// D3 红线 / R7 防线：掩码<b>绝不能</b>原地改写调用方的字典。
    /// 一旦原地改写，随后 PublishAsync 会把 <c>"***"</c> 当成真口令发给设备，
    /// 设备将连不上 broker —— 这正是 T13 要防的现网事故。
    /// </summary>
    [Fact]
    public void Masker_DoesNotMutateOriginalDictionary()
    {
        var original = MqttParams();
        var nested = (Dictionary<string, object?>)original["mqttParams"]!;

        _ = AnShengSecretMasker.MaskRequestJson("setMqtt", original);
        _ = AnShengSecretMasker.MaskParameters("setMqtt", original);

        // 顶层与嵌套层都必须保持明文
        Assert.Same(nested, original["mqttParams"]);
        Assert.Equal("P@ssw0rd!", nested["password"]);
        Assert.Equal("mqtt.example.com", nested["host"]);
    }

    /// <summary>掩码只打点名字段，排障必需的 host/port/clientId 必须原样保留。</summary>
    [Fact]
    public void Masker_KeepsNonSecretFieldsReadable()
    {
        var json = AnShengSecretMasker.MaskRequestJson("setMqtt", MqttParams());

        Assert.Contains("mqtt.example.com", json, StringComparison.Ordinal);
        Assert.Contains("1883", json, StringComparison.Ordinal);
        Assert.Contains("dev-001", json, StringComparison.Ordinal);
    }

    /// <summary>不传口令时不得凭空产生 "***" 占位，否则会把「没传」记成「传了但隐藏」。</summary>
    [Fact]
    public void Masker_MissingSecretParam_NoPlaceholder()
    {
        var parameters = new Dictionary<string, object?>
        {
            ["mqttParams"] = new Dictionary<string, object?>
            {
                ["host"] = "mqtt.example.com",
                ["port"] = 1883
            }
        };

        var json = AnShengSecretMasker.MaskRequestJson("setMqtt", parameters);

        Assert.DoesNotContain(AnShengSecretMasker.Mask, json, StringComparison.Ordinal);
    }

    /// <summary>
    /// setMqtt 的应答方向掩码 —— 敏感字段在 setMqtt 规格上有声明，这条应当通过。
    /// </summary>
    [Fact]
    public void Masker_SetMqttResponseJson_MasksSecrets()
    {
        const string reply = """
            {"method":"setMqtt","frameId":"00001","mqttParams":{"host":"h","password":"LeakMe"}}
            """;

        var masked = AnShengSecretMasker.MaskResponseJson("setMqtt", reply);

        Assert.NotNull(masked);
        Assert.DoesNotContain("LeakMe", masked!, StringComparison.Ordinal);
    }

    /// <summary>
    /// 【缺陷回归锁 · QA-1】getMqtt 的应答口令必须掩码。
    ///
    /// getMqtt 在协议目录里是一条<b>无参数</b>规格（口令只出现在应答里），
    /// 而 <c>SecretFieldNames</c> 只从 <c>spec.Params</c> 收集敏感字段名 ——
    /// 于是 getMqtt 的敏感字段集恒为空，应答口令<b>原样落库</b>到
    /// <c>AnShengCommandRecord.ResponseJson</c>，并经 <c>GET /commands/{id}</c> 明文返回。
    ///
    /// 这正是 AnShengSecretMasker 自己的文档注释所声称要防的「后门」，
    /// 也是决策 D3「作用范围 ✅ ResponseJson」的硬性要求。
    /// </summary>
    [Fact]
    public void Masker_GetMqttResponseJson_MustMaskPassword()
    {
        const string reply = """
            {"method":"getMqtt","frameId":"00001","mqttParams":{"host":"h","port":1883,"password":"LeakMe"}}
            """;

        var masked = AnShengSecretMasker.MaskResponseJson("getMqtt", reply);

        Assert.NotNull(masked);
        Assert.DoesNotContain("LeakMe", masked!, StringComparison.Ordinal);
        // 排障必需字段仍应可读
        Assert.Contains("1883", masked!, StringComparison.Ordinal);
    }

    /// <summary>无敏感字段的普通命令不应被掩码器改写内容。</summary>
    [Fact]
    public void Masker_NonSecretCommand_PassesThrough()
    {
        var parameters = new Dictionary<string, object?> { ["slotNum"] = 2, ["action"] = "on" };

        var masked = AnShengSecretMasker.MaskParameters("action", parameters);

        Assert.Equal(2, Convert.ToInt32(masked["slotNum"]));
        Assert.Equal("on", masked["action"]);
    }
}

/// <summary>
/// 命令服务配置项的单测（决策 D4：用单测守护「30 秒」，而不是让 CI 真等 30 秒）。
/// </summary>
public class AnShengCommandOptionsTests
{
    /// <summary>
    /// D4 代偿：默认 TTL 30s、长耗时命令 60s。
    /// 集成测试只验链路不验秒数，「30 秒」这个规格数字由本用例守护。
    /// </summary>
    [Theory]
    [InlineData("action", 30)]
    [InlineData("getDevStatus", 30)]
    [InlineData("reboot", 30)]
    [InlineData("getLogs", 60)]
    [InlineData("getEMStatistics", 60)]
    public void Options_ResolveTtl_MatchesSpecifiedDefaults(string method, int expectedSeconds)
    {
        var options = new AnShengCommandOptions();

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), options.ResolveTtl(method));
    }

    /// <summary>配置项默认值本身就是规格的一部分，改动必须是有意识的。</summary>
    [Fact]
    public void Options_Defaults_MatchDesign()
    {
        var options = new AnShengCommandOptions();

        Assert.Equal(30, options.DefaultTimeoutSeconds);
        Assert.Equal(60, options.LongRunningTimeoutSeconds);
        Assert.Equal(5, options.SweepIntervalSeconds);
        Assert.True(options.SweepEnabled);
        Assert.False(options.RejectWhenKindUnknown);
        Assert.Equal(90, options.RecordRetentionDays);
    }

    /// <summary>非法（0 或负数）配置不得退化成「立刻超时」或「死循环空转」。</summary>
    [Fact]
    public void Options_NonPositiveValues_FallBackToSafeMinimum()
    {
        var options = new AnShengCommandOptions
        {
            DefaultTimeoutSeconds = 0,
            LongRunningTimeoutSeconds = -1,
            SweepIntervalSeconds = 0
        };

        Assert.True(options.EffectiveDefaultTimeoutSeconds >= 1);
        Assert.True(options.EffectiveLongRunningTimeoutSeconds >= 1);
        Assert.True(options.EffectiveSweepIntervalSeconds >= 1);
    }
}

/// <summary>
/// 协议目录条数的防回归锁（验收 #6 的单测层）。
/// </summary>
public class AnShengCatalogCountTests
{
    /// <summary>
    /// 验收 #6 的单测层判据：目录恒为 36 条。
    /// 集成层断言 <c>GET /catalog</c> 返回 36 条，本用例保证「36」这个数字本身不被悄悄改动。
    /// </summary>
    [Fact]
    public void Catalog_Has36Specs()
    {
        Assert.Equal(36, AnShengCommandCatalog.Count);
        Assert.Equal(36, AnShengCommandCatalog.ListAll().Count);
    }

    /// <summary>
    /// 目录 36 条中 5 条是设备上行事件规格，其余 31 条是平台可下发命令。
    ///
    /// 注意「事件」有两个口径，不可混为一谈：
    ///   · <c>IsEvent==true</c> 的<b>规格</b>共 5 条（connected/keyEvent/delayEvent/timeEvent/recv485）；
    ///   · <see cref="AnShengCommandCatalog.EventMethods"/> 共 6 个，多出来的 <c>close</c> 是
    ///     MQTT 遗嘱消息，协议原文里没有对应的 <c>##</c> 小节，故不计入 36 条规格。
    /// </summary>
    [Fact]
    public void Catalog_EventAndDownlinkSplit_IsStable()
    {
        var eventSpecs = AnShengCommandCatalog.ListAll().Count(s => s.IsEvent);

        Assert.Equal(5, eventSpecs);
        Assert.Equal(31, AnShengCommandCatalog.ListAll().Count - eventSpecs);

        // 遗嘱 close 不是规格，但必须被算作上行事件方法，否则离线判定会漏。
        Assert.Equal(6, AnShengCommandCatalog.EventMethods.Count);
        Assert.Contains(AnShengCommandCatalog.WillMethod, AnShengCommandCatalog.EventMethods);
        Assert.False(AnShengCommandCatalog.Contains(AnShengCommandCatalog.WillMethod));
    }

    /// <summary>上行事件规格必须一律标注 Uplink 方向，两个标志不得脱节。</summary>
    [Fact]
    public void Catalog_EventSpecs_AreAllUplink()
    {
        foreach (var spec in AnShengCommandCatalog.ListAll().Where(s => s.IsEvent))
        {
            Assert.Equal(AnShengCommandDirection.Uplink, spec.Direction);
        }
    }

    /// <summary>
    /// 验收 #6 要求「字段完整」：每条规格都必须有 method / description / 支持品类，
    /// 否则前端渲染命令列表时会出现空白项。
    /// </summary>
    [Fact]
    public void Catalog_EverySpec_HasCompleteFields()
    {
        foreach (var spec in AnShengCommandCatalog.ListAll())
        {
            Assert.False(string.IsNullOrWhiteSpace(spec.Method), "method 不得为空");
            // 实现中描述字段名为 Title（设计类图写作 Description，属命名口径差异，非缺陷）
            Assert.False(string.IsNullOrWhiteSpace(spec.Title), $"{spec.Method} 缺少描述");
            Assert.False(string.IsNullOrWhiteSpace(spec.DocAnchor), $"{spec.Method} 缺少协议原文锚点");
            Assert.NotEqual(AnShengDeviceCapability.None, spec.SupportedKinds);
        }
    }
}
