using System.Text.Json;
using IoTPlatform.Infrastructure.Protocol.AnSheng;

namespace IoTPlatform.Tools.AnShengFieldTest;

/// <summary>Risk class of a script step, drives the safety gate.</summary>
public enum StepRisk
{
    /// <summary>Read-only probe (getDevInfo / getDevStatus / getDelayTasks / getAutoReport). Always allowed.</summary>
    ReadOnly,
    /// <summary>Configuration change (setTime / setAutoReport). Needs --allow-config.</summary>
    Config,
    /// <summary>Physical control (action / actions / startDelayTask / stopDelayTask). Needs --allow-control.</summary>
    Control
}

/// <summary>Whether a step deliberately moves the device clock (and therefore MUST be reset afterwards).</summary>
public enum ClockAction
{
    /// <summary>No clock manipulation.</summary>
    None,
    /// <summary>Deliberately offsets the device clock (e.g. +1h) to validate Q11/Q12; must be reset before exit.</summary>
    Offset,
    /// <summary>Resets the device clock back to current UTC.</summary>
    Reset
}

/// <summary>Whether a step changes the device auto-report configuration (must be restored afterwards).</summary>
public enum AutoReportAction
{
    /// <summary>No auto-report configuration change.</summary>
    None,
    /// <summary>Turns auto-report ON (or changes the interval); the original value must be restored before exit.</summary>
    Change,
    /// <summary>Restores the auto-report configuration to the captured original value.</summary>
    Restore
}

/// <summary>
/// Mutable state shared across script steps: captured "original" values that later steps must
/// restore, plus cross-step observations (e.g. the toggle sandwich) surfaced in the report.
/// </summary>
public sealed class ScriptContext
{
    // ---- G6: captured original auto-report configuration ----------------
    /// <summary>True once a getAutoReport response was parsed successfully (restore is only safe then).</summary>
    public bool AutoReportCaptured { get; set; }
    /// <summary>True once setAutoReport actually changed the configuration (restore is only needed then).</summary>
    public bool AutoReportChanged { get; set; }
    public int OrigGetDevStatusSec { get; set; }
    public int OrigOrderUpSec { get; set; }
    public int OrigRs485Sec { get; set; }
    public int OrigRs485BaudRate { get; set; } = 115200;
    public string? OrigGetDevStatusQ { get; set; }
    public int? OrigRs485SendWaitMs { get; set; }
    public List<string>? OrigRs485Array { get; set; }
    /// <summary>Verbatim getAutoReport response, kept for the report.</summary>
    public string? AutoReportOriginalRaw { get; set; }

    // ---- Q8: the toggle sandwich ----------------------------------------
    public int[]? SlotsBeforeToggle { get; set; }
    public int[]? SlotsInToggleResponse { get; set; }
    public int[]? SlotsAfterToggle { get; set; }

    // ---- Q20: the delay task we deliberately let expire -------------------
    public string? Q20DelayFrameId { get; set; }

    /// <summary>Free-form empirical findings appended by step capture hooks; rendered in the report.</summary>
    public List<string> Findings { get; } = new();

    /// <summary>Last observed slots array from any getDevStatus/action response.</summary>
    public int[]? LastObservedSlots { get; set; }
}

/// <summary>Builds a downlink command for a step using the production builder. Returns (frameId, payload).</summary>
public delegate (string FrameId, string Payload) BuildCommand(
    AnShengCommandBuilder builder, string imei, AnShengDeviceKind kind, ScriptContext ctx);

/// <summary>Asserts something about a parsed response. Returns null when OK, else the mismatch reason.</summary>
public delegate string? AssertResponse(JsonElement response, ScriptContext ctx);

/// <summary>Captures values from a parsed response into the shared context.</summary>
public delegate void CaptureResponse(JsonElement response, ScriptContext ctx);

/// <summary>Decides at runtime whether a step may run. Returns (false, reason) to skip it.</summary>
public delegate (bool Ok, string Reason) StepPrecondition(ScriptContext ctx);

/// <summary>One step in the dispatch script.</summary>
public sealed class ScriptStep
{
    public string Method { get; init; } = string.Empty;
    public string Group { get; init; } = string.Empty;
    public StepRisk Risk { get; init; }
    public string Purpose { get; init; } = string.Empty;

    /// <summary>Clock manipulation performed by this step (drives the guaranteed clock-reset safety net).</summary>
    public ClockAction Clock { get; init; } = ClockAction.None;

    /// <summary>Auto-report manipulation performed by this step (drives the guaranteed restore safety net).</summary>
    public AutoReportAction AutoReport { get; init; } = AutoReportAction.None;

    /// <summary>
    /// When &gt; 0 this is a passive DWELL step: no command is sent, the tool simply keeps the
    /// subscription open for N seconds and records every uplink that arrives in the window.
    /// </summary>
    public int DwellSeconds { get; init; }

    /// <summary>
    /// When true the step is EXPECTED to be rejected by the device. A non-ok <c>result</c> is then a
    /// PASS (the error code is recorded); an unexpected <c>ok</c> becomes a MISMATCH.
    /// </summary>
    public bool ExpectFailure { get; init; }

    /// <summary>Optional runtime gate; used to refuse changing state we could not read first.</summary>
    public StepPrecondition? Precondition { get; init; }

    /// <summary>Optional assertion over the response body.</summary>
    public AssertResponse? Assert { get; init; }

    /// <summary>Optional capture hook writing response values into the shared context.</summary>
    public CaptureResponse? Capture { get; init; }

    public BuildCommand Build { get; init; } =
        (_, _, _, _) => throw new InvalidOperationException("unassigned build");
}

/// <summary>
/// The strict-order dispatch script for the 4G switch real-device validation (round 2).
///
/// Order is fixed and MUST NOT be reordered:
///   G6  getAutoReport -> setAutoReport(30s) -> getAutoReport            (arm push mode first)
///   G1  getDevInfo -> getDevStatus
///   G5  setTime(+1h)                                                    (Q11/Q12)
///   G3  action(on) -> getDevStatus -> action(off) -> getDevStatus
///   Q8  getDevStatus -> action(toggle) -> getDevStatus -> action(off,sAction)
///   Q9  actions(slots:[1,3,4]) [expected failure] -> getDevStatus -> action(off)
///   Q13 action(action="none") -> getDevStatus                           (none value probe)
///   Q15 startDelayTask(enable omitted, sAction=on) -> getDelayTasks -> stopDelayTask -> action(off)
///   Q24 getSlotTimeTasks -> getSlotTimeTasks(slotNum) -> getTimeTasks
///   Q23 10 x getDevStatus @100ms                                        (throttle stress)
///   Q20 startDelayTask(on/off, 15s, NOT stopped)                        (Q20 delayEvent)
///   Q20 DWELL 20s (Q20-wait) -> getDevStatus (Q20-post)                 (wait for delayEvent)
///   G6  DWELL 70s                                                       (auto-report push window)
///   Q20 getDelayTasks
///   P0  action(off) -> getDevStatus [assert slots[0]==0]                (leave the switch OPEN)
///   G5  setTime(reset) -> getDevStatus
///   G6  setAutoReport(restore) -> getAutoReport
///
/// SAFETY: the two P0 steps guarantee the physical switch is left OPEN. They are additionally
/// backed by FieldTestRunner.GuaranteedSwitchOffAsync in a finally block.
/// </summary>
public static class CommandScript
{
    /// <summary>Delay used by the Q20 "let it expire" task (seconds). Must be &lt; dwell window.</summary>
    public const int Q20DelaySeconds = 15;

    /// <summary>Passive dwell window for Q20-wait: must cover the Q20DelaySeconds expiry plus margin.</summary>
    public const int Q20WaitSeconds = 20;

    /// <summary>Auto-report interval requested by G6. Protocol floor for a non-zero value is 30s.</summary>
    public const int AutoReportTestSeconds = 30;

    /// <summary>Number of back-to-back getDevStatus probes used by the Q23 throttle stress test.</summary>
    public const int Q23BurstCount = 10;

    /// <summary>Build the ordered script.</summary>
    /// <param name="slotNum">Target slot for control commands.</param>
    /// <param name="delaySeconds">Delay used by the first (stopped) delay-task probe.</param>
    /// <param name="dwellSeconds">Passive dwell window covering Q20 expiry + auto-report pushes.</param>
    public static IReadOnlyList<ScriptStep> Build(int slotNum, int delaySeconds, int dwellSeconds)
    {
        var steps = new List<ScriptStep>();

        // ───────────────────────────────────────────────────────────────
        // G6 自动上报（放最前：先把推送打开，后续所有步骤都能被推送窗口覆盖）
        // Q16：本机固件是否支持 getAutoReport / getDevStatus.q / uploadEnable
        // ───────────────────────────────────────────────────────────────
        steps.Add(new ScriptStep
        {
            Method = "getAutoReport", Group = "G6", Risk = StepRisk.ReadOnly,
            Purpose = "读取自动上报原始配置（Q16；改之前必须先读到，读不到就不改）",
            Build = (b, imei, kind, _) => b.BuildGetAutoReport(imei, kind),
            Capture = CaptureAutoReportOriginal
        });

        steps.Add(new ScriptStep
        {
            Method = "setAutoReport", Group = "G6", Risk = StepRisk.Config,
            Purpose = $"打开状态自动上报，间隔 {AutoReportTestSeconds}s（验证设备是否只会被轮询、能否主动推送）",
            AutoReport = AutoReportAction.Change,
            Precondition = ctx => ctx.AutoReportCaptured
                ? (true, string.Empty)
                : (false, "getAutoReport 未取得原始配置，拒绝修改（无法保证复位）"),
            Build = (b, imei, kind, ctx) => b.BuildSetAutoReport(
                imei,
                getDevStatusSec: AutoReportTestSeconds,
                orderUpSec: ctx.OrigOrderUpSec,
                rs485Sec: ctx.OrigRs485Sec,
                rs485BaudRate: ctx.OrigRs485BaudRate,
                getDevStatusQ: ctx.OrigGetDevStatusQ,
                rs485SendWaitMs: ctx.OrigRs485SendWaitMs,
                rs485Array: ctx.OrigRs485Array,
                kind: kind)
        });

        steps.Add(new ScriptStep
        {
            Method = "getAutoReport", Group = "G6", Risk = StepRisk.ReadOnly,
            Purpose = $"回读确认自动上报间隔已生效为 {AutoReportTestSeconds}s",
            Build = (b, imei, kind, _) => b.BuildGetAutoReport(imei, kind),
            Assert = (root, ctx) =>
            {
                if (!ctx.AutoReportChanged) return null; // setAutoReport 被跳过，不断言
                if (!TryGetInt(root, "getDevStatusSec", out var sec))
                    return "回读响应缺少 getDevStatusSec，无法确认自动上报是否生效";
                return sec == AutoReportTestSeconds
                    ? null
                    : $"自动上报间隔回读为 {sec}s，期望 {AutoReportTestSeconds}s";
            }
        });

        // ───────────────────────────────────────────────────────────────
        // G1 基础信息
        // ───────────────────────────────────────────────────────────────
        steps.Add(new ScriptStep
        {
            Method = "getDevInfo", Group = "G1", Risk = StepRisk.ReadOnly,
            Purpose = "读取设备静态信息",
            Build = (b, imei, kind, _) => b.BuildGetDevInfo(imei, kind)
        });
        steps.Add(new ScriptStep
        {
            Method = "getDevStatus", Group = "G1", Risk = StepRisk.ReadOnly,
            Purpose = "读取设备实时状态",
            Build = (b, imei, kind, _) => b.BuildGetDevStatus(imei, null, kind),
            Capture = CaptureSlots
        });

        // ───────────────────────────────────────────────────────────────
        // G5 时钟（Q11/Q12）
        // ───────────────────────────────────────────────────────────────
        steps.Add(new ScriptStep
        {
            Method = "setTime", Group = "G5", Risk = StepRisk.Config,
            Purpose = "将设备时钟拨 +1h 验证 Q11/Q12（业务 timestamp 不被系统时钟覆盖），随后必须复位",
            Clock = ClockAction.Offset,
            Build = (b, imei, kind, _) => b.BuildSetTime(imei, DateTime.UtcNow.AddHours(1), kind)
        });

        // ───────────────────────────────────────────────────────────────
        // G3 基础通断
        // ───────────────────────────────────────────────────────────────
        steps.Add(new ScriptStep
        {
            Method = "action", Group = "G3", Risk = StepRisk.Control,
            Purpose = "闭合开关 (on)",
            Build = (b, imei, kind, _) => b.BuildAction(imei, slotNum, "on", null, kind),
            Capture = CaptureSlots
        });
        steps.Add(new ScriptStep
        {
            Method = "getDevStatus", Group = "G3", Risk = StepRisk.ReadOnly,
            Purpose = "验证 on 后状态（期望 slots[0]==1）",
            Assert = (root, _) => AssertSlot0(root, 1, "action on"),
            Capture = CaptureSlots,
            Build = (b, imei, kind, _) => b.BuildGetDevStatus(imei, null, kind)
        });
        steps.Add(new ScriptStep
        {
            Method = "action", Group = "G3", Risk = StepRisk.Control,
            Purpose = "断开开关 (off)",
            Build = (b, imei, kind, _) => b.BuildAction(imei, slotNum, "off", null, kind),
            Capture = CaptureSlots
        });
        steps.Add(new ScriptStep
        {
            Method = "getDevStatus", Group = "G3", Risk = StepRisk.ReadOnly,
            Purpose = "验证 off 后状态（期望 slots[0]==0）",
            Assert = (root, _) => AssertSlot0(root, 0, "action off"),
            Capture = CaptureSlots,
            Build = (b, imei, kind, _) => b.BuildGetDevStatus(imei, null, kind)
        });

        // ───────────────────────────────────────────────────────────────
        // Q8：action 响应的 slots 是「动作前」还是「动作后」的状态？前后夹 getDevStatus
        // ───────────────────────────────────────────────────────────────
        steps.Add(new ScriptStep
        {
            Method = "getDevStatus", Group = "Q8", Risk = StepRisk.ReadOnly,
            Purpose = "Q8 三明治-前：toggle 之前的真实状态",
            Build = (b, imei, kind, _) => b.BuildGetDevStatus(imei, null, kind),
            Capture = (root, ctx) => { ctx.SlotsBeforeToggle = ReadSlots(root); CaptureSlots(root, ctx); }
        });
        steps.Add(new ScriptStep
        {
            Method = "action", Group = "Q8", Risk = StepRisk.Control,
            Purpose = "Q8 三明治-中：action=toggle（Catalog 声明的合法取值）",
            Build = (b, imei, kind, _) => b.BuildAction(imei, slotNum, "toggle", null, kind),
            Capture = (root, ctx) => { ctx.SlotsInToggleResponse = ReadSlots(root); CaptureSlots(root, ctx); }
        });
        steps.Add(new ScriptStep
        {
            Method = "getDevStatus", Group = "Q8", Risk = StepRisk.ReadOnly,
            Purpose = "Q8 三明治-后：toggle 之后的真实状态；据此判定 action.slots 的语义",
            Build = (b, imei, kind, _) => b.BuildGetDevStatus(imei, null, kind),
            Capture = (root, ctx) =>
            {
                ctx.SlotsAfterToggle = ReadSlots(root);
                CaptureSlots(root, ctx);
                ctx.Findings.Add(DescribeToggleSandwich(ctx));
            }
        });
        steps.Add(new ScriptStep
        {
            Method = "action", Group = "Q8", Risk = StepRisk.Control,
            Purpose = "Q8 附加：给 action 附带 Catalog 未声明的 sAction 字段（探测设备是否接受/忽略/报错），同时把开关归位为 off",
            Build = (b, imei, kind, _) => b.BuildRaw(imei, "action",
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["slotNum"] = slotNum,
                    ["action"] = "off",
                    ["sAction"] = "toggle"
                }, kind),
            Capture = CaptureSlots
        });

        // ───────────────────────────────────────────────────────────────
        // Q9：actions（复数）slots:[1,3,4] —— 本机 slotAmount=1，预期失败
        // ───────────────────────────────────────────────────────────────
        steps.Add(new ScriptStep
        {
            Method = "actions", Group = "Q9", Risk = StepRisk.Control,
            Purpose = "Q9：actions 复数下发 slots:[1,3,4]（本机 slotAmount=1，预期被拒绝；记录错误码）",
            ExpectFailure = true,
            Build = (b, imei, kind, _) => b.BuildActions(imei, new[] { 1, 3, 4 }, "on", null, kind),
            Capture = CaptureSlots
        });
        steps.Add(new ScriptStep
        {
            Method = "getDevStatus", Group = "Q9", Risk = StepRisk.ReadOnly,
            Purpose = "Q9 善后：确认 actions 越界下发后开关的真实状态（不做断言，只如实记录）",
            Build = (b, imei, kind, _) => b.BuildGetDevStatus(imei, null, kind),
            Capture = (root, ctx) =>
            {
                CaptureSlots(root, ctx);
                ctx.Findings.Add($"Q9 越界 actions 之后的实际 slots = {FormatSlots(ReadSlots(root))}");
            }
        });
        steps.Add(new ScriptStep
        {
            Method = "action", Group = "Q9", Risk = StepRisk.Control,
            Purpose = "Q9 归位：无论 actions 是否部分生效，强制断开，保证后续步骤起点确定",
            Build = (b, imei, kind, _) => b.BuildAction(imei, slotNum, "off", null, kind),
            Capture = CaptureSlots
        });

        // ───────────────────────────────────────────────────────────────
        // Q13：action 的 none 值是否合法（控制命令传 none 是否被接受/忽略/报错）
        // ───────────────────────────────────────────────────────────────
        steps.Add(new ScriptStep
        {
            Method = "action", Group = "Q13", Risk = StepRisk.Control,
            Purpose = "Q13：action=\"none\"（探测设备是否接受/忽略/报错 non-toggle 取值）",
            Build = (b, imei, kind, _) => b.BuildAction(imei, slotNum, "none", null, kind),
            Capture = CaptureSlots
        });
        steps.Add(new ScriptStep
        {
            Method = "getDevStatus", Group = "Q13", Risk = StepRisk.ReadOnly,
            Purpose = "Q13 后：确认 action=\"none\" 是否改变了开关状态",
            Build = (b, imei, kind, _) => b.BuildGetDevStatus(imei, null, kind),
            Capture = (root, ctx) =>
            {
                CaptureSlots(root, ctx);
                ctx.Findings.Add($"Q13 action=none 之后的实际 slots = {FormatSlots(ReadSlots(root))}");
            }
        });

        // ───────────────────────────────────────────────────────────────
        // Q15：startDelayTask 省略 enable 是否真必填（BuildRaw 绕过 Catalog 校验）
        // ───────────────────────────────────────────────────────────────
        steps.Add(new ScriptStep
        {
            Method = "startDelayTask", Group = "Q15", Risk = StepRisk.Control,
            Purpose = "Q15：省略 enable 字段（BuildRaw 绕过 Catalog 必填校验，验证设备端默认值）；sAction=on 真实闭合，需后续清理",
            Build = (b, imei, kind, _) => b.BuildRaw(imei, "startDelayTask",
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["slotNum"] = slotNum,
                    ["sAction"] = "on",
                    ["eAction"] = "off",
                    ["secs"] = 5
                }, kind),
            Capture = CaptureSlots
        });
        steps.Add(new ScriptStep
        {
            Method = "getDelayTasks", Group = "Q15", Risk = StepRisk.ReadOnly,
            Purpose = "Q15：回读确认省略 enable 的任务是否真的建立了（enable 默认值是什么？）",
            Build = (b, imei, kind, _) => b.BuildGetDelayTasks(imei, kind)
        });
        steps.Add(new ScriptStep
        {
            Method = "stopDelayTask", Group = "Q15", Risk = StepRisk.Control,
            Purpose = "Q15 清理：停止该探测任务",
            Build = (b, imei, kind, _) => b.BuildStopDelayTask(imei, slotNum, kind)
        });
        steps.Add(new ScriptStep
        {
            Method = "action", Group = "Q15", Risk = StepRisk.Control,
            Purpose = "Q15 清理：sAction=on 已闭合开关，stopDelayTask 取消后 eAction 永不执行，必须手动 action off 断开",
            Build = (b, imei, kind, _) => b.BuildAction(imei, slotNum, "off", null, kind),
            Capture = CaptureSlots
        });

        // ───────────────────────────────────────────────────────────────
        // Q24：单插槽定时任务如何指定插槽（文档整组缺 slotNum）
        // ───────────────────────────────────────────────────────────────
        steps.Add(new ScriptStep
        {
            Method = "getSlotTimeTasks", Group = "Q24", Risk = StepRisk.ReadOnly,
            Purpose = "Q24 (a)：getSlotTimeTasks 不带 slotNum（Catalog 有该命令但无 Build* 方法，走 BuildCommand）",
            Build = (b, imei, kind, _) => b.BuildCommand(imei, "getSlotTimeTasks", null, kind)
        });
        steps.Add(new ScriptStep
        {
            Method = "getSlotTimeTasks", Group = "Q24", Risk = StepRisk.ReadOnly,
            Purpose = "Q24 (b)：getSlotTimeTasks 带 slotNum（Catalog 未声明该参数，走 BuildRaw 探测设备是否认）",
            Build = (b, imei, kind, _) => b.BuildRaw(imei, "getSlotTimeTasks",
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["slotNum"] = slotNum }, kind)
        });
        steps.Add(new ScriptStep
        {
            Method = "getTimeTasks", Group = "Q24", Risk = StepRisk.ReadOnly,
            Purpose = "Q24 (c)：getTimeTasks 全局版，与 (a) 逐字段对比，判定两命令是否重复",
            Build = (b, imei, kind, _) => b.BuildCommand(imei, "getTimeTasks", null, kind)
        });

        // ───────────────────────────────────────────────────────────────
        // Q23：连续下发 100ms 节流压测（10 条 getDevStatus 背靠背）
        // ───────────────────────────────────────────────────────────────
        for (int i = 1; i <= Q23BurstCount; i++)
        {
            var seq = i;
            steps.Add(new ScriptStep
            {
                Method = "getDevStatus", Group = "Q23", Risk = StepRisk.ReadOnly,
                Purpose = $"Q23 限流压测 {seq}/{Q23BurstCount}：背靠背下发，节流器保证 >=100ms/IMEI",
                Build = (b, imei, kind, _) => b.BuildGetDevStatus(imei, null, kind),
                Capture = CaptureSlots
            });
        }

        // ───────────────────────────────────────────────────────────────
        // Q20：延时任务到期不 stop，驻留抓 delayEvent（on -> off，天然安全）
        // ───────────────────────────────────────────────────────────────
        steps.Add(new ScriptStep
        {
            Method = "startDelayTask", Group = "Q20", Risk = StepRisk.Control,
            Purpose = $"Q20：sAction=on / eAction=off / secs={Q20DelaySeconds}，**不 stop**，等它自然到期（到期即断开，天然安全）",
            Build = (b, imei, kind, ctx) =>
            {
                var r = b.BuildStartDelayTask(imei, slotNum, true, "on", "off", Q20DelaySeconds, kind);
                ctx.Q20DelayFrameId = r.FrameId;
                return r;
            },
            Capture = CaptureSlots
        });

        // ───────────────────────────────────────────────────────────────
        // Q20-wait：独立 20s 驻留等待 delayEvent（第 Q20DelaySeconds s 到期）
        // ───────────────────────────────────────────────────────────────
        steps.Add(new ScriptStep
        {
            Method = "(dwell)", Group = "Q20", Risk = StepRisk.ReadOnly,
            Purpose = $"Q20-wait：驻留监听 {Q20WaitSeconds}s，等待 delayEvent 主动上报（第 {Q20DelaySeconds}s 到期）",
            DwellSeconds = Q20WaitSeconds
        });

        steps.Add(new ScriptStep
        {
            Method = "getDevStatus", Group = "Q20", Risk = StepRisk.ReadOnly,
            Purpose = "Q20-post：确认 eAction 到期已执行 off（期望 slots[0]==0）",
            Assert = (root, _) => AssertSlot0(root, 0, "Q20 eAction off 到期"),
            Capture = CaptureSlots,
            Build = (b, imei, kind, _) => b.BuildGetDevStatus(imei, null, kind)
        });

        // ───────────────────────────────────────────────────────────────
        // 驻留窗口：G6 自动上报推送（30s x >=2 个周期）
        // ───────────────────────────────────────────────────────────────
        steps.Add(new ScriptStep
        {
            Method = "(dwell)", Group = "G6", Risk = StepRisk.ReadOnly,
            Purpose = $"驻留监听 {dwellSeconds}s：抓至少 2 个自动上报周期（{AutoReportTestSeconds}s）",
            DwellSeconds = dwellSeconds
        });

        steps.Add(new ScriptStep
        {
            Method = "getDelayTasks", Group = "Q20", Risk = StepRisk.ReadOnly,
            Purpose = "Q20 收尾：确认到期后的任务列表形态（Q14：无任务时 tasks 是什么）",
            Build = (b, imei, kind, _) => b.BuildGetDelayTasks(imei, kind)
        });

        // ───────────────────────────────────────────────────────────────
        // P0 安全收尾：确保开关断开（第一轮缺失，导致开关被永久留在闭合状态）
        // ───────────────────────────────────────────────────────────────
        steps.Add(new ScriptStep
        {
            Method = "action", Group = "P0", Risk = StepRisk.Control,
            Purpose = "剧本收尾：确保开关断开",
            Build = (b, imei, kind, _) => b.BuildAction(imei, slotNum, "off", null, kind),
            Capture = CaptureSlots
        });
        steps.Add(new ScriptStep
        {
            Method = "getDevStatus", Group = "P0", Risk = StepRisk.ReadOnly,
            Purpose = "剧本收尾断言：读回确认 slots[0] == 0（开关确已断开），不为 0 判 MISMATCH",
            Assert = (root, _) => AssertSlot0(root, 0, "剧本收尾 action off"),
            Capture = (root, ctx) =>
            {
                CaptureSlots(root, ctx);
                ctx.Findings.Add($"剧本收尾读回的最终物理状态 slots = {FormatSlots(ReadSlots(root))}");
            },
            Build = (b, imei, kind, _) => b.BuildGetDevStatus(imei, null, kind)
        });

        // ───────────────────────────────────────────────────────────────
        // G5 时钟复位
        // ───────────────────────────────────────────────────────────────
        steps.Add(new ScriptStep
        {
            Method = "setTime", Group = "G5", Risk = StepRisk.Config,
            Purpose = "复位设备时钟为当前 UTC（setTime 拨 +1h 验 Q11/Q12 后必须复位）",
            Clock = ClockAction.Reset,
            Build = (b, imei, kind, _) => b.BuildSetTime(imei, DateTime.UtcNow, kind)
        });
        steps.Add(new ScriptStep
        {
            Method = "getDevStatus", Group = "G5", Risk = StepRisk.ReadOnly,
            Purpose = "二次确认时钟已复位",
            Build = (b, imei, kind, _) => b.BuildGetDevStatus(imei, null, kind),
            Capture = CaptureSlots
        });

        // ───────────────────────────────────────────────────────────────
        // G6 自动上报复位（改了必须复位）
        // ───────────────────────────────────────────────────────────────
        steps.Add(new ScriptStep
        {
            Method = "setAutoReport", Group = "G6", Risk = StepRisk.Config,
            Purpose = "复位自动上报配置为剧本开始前的原始值（改了必须复位）",
            AutoReport = AutoReportAction.Restore,
            Precondition = ctx => ctx.AutoReportChanged
                ? (true, string.Empty)
                : (false, "自动上报未被修改，无需复位"),
            Build = (b, imei, kind, ctx) => b.BuildSetAutoReport(
                imei,
                getDevStatusSec: ctx.OrigGetDevStatusSec,
                orderUpSec: ctx.OrigOrderUpSec,
                rs485Sec: ctx.OrigRs485Sec,
                rs485BaudRate: ctx.OrigRs485BaudRate,
                getDevStatusQ: ctx.OrigGetDevStatusQ,
                rs485SendWaitMs: ctx.OrigRs485SendWaitMs,
                rs485Array: ctx.OrigRs485Array,
                kind: kind)
        });
        steps.Add(new ScriptStep
        {
            Method = "getAutoReport", Group = "G6", Risk = StepRisk.ReadOnly,
            Purpose = "二次确认自动上报配置已复位为原始值",
            Build = (b, imei, kind, _) => b.BuildGetAutoReport(imei, kind),
            Assert = (root, ctx) =>
            {
                if (!ctx.AutoReportChanged) return null;
                if (!TryGetInt(root, "getDevStatusSec", out var sec))
                    return "复位回读响应缺少 getDevStatusSec，无法确认已复位";
                return sec == ctx.OrigGetDevStatusSec
                    ? null
                    : $"自动上报间隔复位回读为 {sec}s，期望还原为 {ctx.OrigGetDevStatusSec}s";
            }
        });

        return steps;
    }

    // ---- capture / assert helpers ----------------------------------------

    /// <summary>Reads the auto-report original configuration so the restore step can put it back verbatim.</summary>
    private static void CaptureAutoReportOriginal(JsonElement root, ScriptContext ctx)
    {
        ctx.AutoReportOriginalRaw = root.GetRawText();

        // Only treat it as captured when the primary field is actually present; otherwise the
        // firmware does not really implement getAutoReport and we must NOT touch the config.
        if (!TryGetInt(root, "getDevStatusSec", out var statusSec))
        {
            ctx.AutoReportCaptured = false;
            ctx.Findings.Add("Q16：getAutoReport 响应中不含 getDevStatusSec —— 视为固件未真正实现该命令，已放弃修改自动上报配置。");
            return;
        }

        ctx.OrigGetDevStatusSec = statusSec;
        ctx.OrigOrderUpSec = TryGetInt(root, "orderUpSec", out var orderSec) ? orderSec : 0;
        ctx.OrigRs485Sec = TryGetInt(root, "rs485Sec", out var rs485Sec) ? rs485Sec : 0;
        ctx.OrigRs485BaudRate = TryGetInt(root, "rs485BaudRate", out var baud) ? baud : 115200;

        ctx.OrigGetDevStatusQ = root.TryGetProperty("getDevStatusQ", out var q) && q.ValueKind == JsonValueKind.String
            ? q.GetString()
            : null;
        ctx.OrigRs485SendWaitMs = TryGetInt(root, "rs485SendWaitMs", out int wait) ? wait : (int?)null;

        if (root.TryGetProperty("rs485Array", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            var list = new List<string>();
            foreach (var e in arr.EnumerateArray())
                if (e.ValueKind == JsonValueKind.String && e.GetString() is { } s)
                    list.Add(s);
            ctx.OrigRs485Array = list.Count > 0 ? list : null;
        }

        ctx.AutoReportCaptured = true;
        ctx.Findings.Add($"Q16：getAutoReport 原始配置 getDevStatusSec={ctx.OrigGetDevStatusSec}, " +
                         $"orderUpSec={ctx.OrigOrderUpSec}, rs485Sec={ctx.OrigRs485Sec}, rs485BaudRate={ctx.OrigRs485BaudRate}。");
    }

    /// <summary>Keeps the most recently observed slots array in the context.</summary>
    private static void CaptureSlots(JsonElement root, ScriptContext ctx)
    {
        var slots = ReadSlots(root);
        if (slots is not null) ctx.LastObservedSlots = slots;
    }

    /// <summary>Assert slots[0] equals the expected value. Returns null when OK.</summary>
    private static string? AssertSlot0(JsonElement root, int expected, string afterWhat)
    {
        var slots = ReadSlots(root);
        if (slots is null)
            return $"响应中没有 slots 数组，无法验证 {afterWhat} 之后的开关状态";
        if (slots.Length == 0)
            return $"slots 为空数组，无法验证 {afterWhat} 之后的开关状态";
        return slots[0] == expected
            ? null
            : $"{afterWhat} 之后 slots[0]={slots[0]}，期望 {expected}（slots={FormatSlots(slots)}）";
    }

    /// <summary>Extract the slots array (state per slot) from a response, or null when absent.</summary>
    public static int[]? ReadSlots(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (!root.TryGetProperty("slots", out var slots) || slots.ValueKind != JsonValueKind.Array) return null;
        var list = new List<int>();
        foreach (var e in slots.EnumerateArray())
            list.Add(e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var v) ? v : -1);
        return list.ToArray();
    }

    /// <summary>Render a slots array as "[0]" / "[1,0]" / "(缺失)".</summary>
    public static string FormatSlots(int[]? slots) =>
        slots is null ? "(缺失)" : "[" + string.Join(",", slots) + "]";

    /// <summary>Interpret the Q8 toggle sandwich: does action.slots report pre- or post-action state?</summary>
    private static string DescribeToggleSandwich(ScriptContext ctx)
    {
        var before = FormatSlots(ctx.SlotsBeforeToggle);
        var mid = FormatSlots(ctx.SlotsInToggleResponse);
        var after = FormatSlots(ctx.SlotsAfterToggle);

        string verdict;
        if (ctx.SlotsInToggleResponse is null)
            verdict = "action 响应未带 slots，无法判定";
        else if (ctx.SlotsBeforeToggle is not null && ctx.SlotsAfterToggle is not null
                 && before == after)
            verdict = "toggle 前后真实状态相同，本轮无法区分前/后语义（需再跑一次错开状态）";
        else if (mid == after)
            verdict = "action 响应的 slots 是【动作后】的状态";
        else if (mid == before)
            verdict = "action 响应的 slots 是【动作前】的状态";
        else
            verdict = "action 响应的 slots 既不等于动作前也不等于动作后，语义异常，需向安圣确认";

        return $"Q8 toggle 三明治：前={before} / action响应={mid} / 后={after} → {verdict}";
    }

    private static bool TryGetInt(JsonElement root, string name, out int value)
    {
        value = 0;
        if (root.ValueKind != JsonValueKind.Object) return false;
        if (!root.TryGetProperty(name, out var e)) return false;
        if (e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out value)) return true;
        if (e.ValueKind == JsonValueKind.String && int.TryParse(e.GetString(), out value)) return true;
        return false;
    }

    /// <summary>Whether a step risk is permitted by the current flags.</summary>
    public static bool IsAllowed(StepRisk risk, bool allowConfig, bool allowControl) => risk switch
    {
        StepRisk.ReadOnly => true,
        StepRisk.Config => allowConfig,
        StepRisk.Control => allowControl,
        _ => false
    };

    /// <summary>Human-readable reason a step is blocked (empty when allowed).</summary>
    public static string BlockedReason(StepRisk risk) => risk switch
    {
        StepRisk.ReadOnly => string.Empty,
        StepRisk.Config => "config 命令需要 --allow-config",
        StepRisk.Control => "control 命令需要 --allow-control",
        _ => "未知风险级别"
    };
}
