using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using IoTPlatform.Infrastructure.Protocol.AnSheng;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

namespace IoTPlatform.Tools.AnShengFieldTest;

/// <summary>Aggregated data passed to the report writer.</summary>
public sealed record FieldTestReportData
{
    public DateTime StartedUtc { get; init; } = DateTime.UtcNow;
    public FieldTestOptions Options { get; init; } = null!;
    public bool ConnectionOk { get; init; }
    public string ConnectionDetail { get; init; } = string.Empty;
    public IReadOnlyList<PreflightCheck> Preflight { get; init; } = Array.Empty<PreflightCheck>();
    public IReadOnlyList<string> HeardImeis { get; init; } = Array.Empty<string>();
    public Dictionary<string, int> ImeiMessageCounts { get; init; } = new();
    public List<UplinkRecord> AllUplinks { get; init; } = new();
    public List<StepResult> Steps { get; init; } = new();
    public string? ResolvedKind { get; init; }
    public string? CapturePath { get; init; }
    public string? ReportPath { get; init; }
    public int ExitCode { get; init; }

    /// <summary>Log lines emitted by the finally-block safety nets (switch-off / clock / auto-report).</summary>
    public IReadOnlyList<string> SafetyNetLog { get; init; } = Array.Empty<string>();

    /// <summary>Cross-step empirical findings collected by the script capture hooks.</summary>
    public IReadOnlyList<string> ScriptFindings { get; init; } = Array.Empty<string>();

    /// <summary>The final physical switch state read back at the very end, e.g. "[0]".</summary>
    public string FinalSlots { get; init; } = "(未读取)";

    /// <summary>True only when the final read-back positively confirmed every slot is OPEN (0).</summary>
    public bool FinalSwitchOffConfirmed { get; init; }
}

/// <summary>
/// Orchestrates the field test: preflight self-checks, connect, subscribe, capture
/// uplink, (optionally) dispatch the scripted commands, and collect results.
///
/// Three layers of safety:
///   1. PreflightRunner              - local self-checks, no device contact.
///   2. In-script finale steps       - action(off) + read-back assert, setTime(reset), setAutoReport(restore).
///   3. finally-block guaranteed nets - GuaranteedSwitchOffAsync (physical first),
///                                      GuaranteedClockResetAsync, GuaranteedAutoReportRestoreAsync.
/// </summary>
public sealed class FieldTestRunner
{
    private readonly FieldTestOptions _opt;
    private readonly AnShengCommandBuilder _builder = new();
    private readonly AnShengMessageParser _parser = new();
    private readonly AnShengCommandThrottle _throttle;
    private readonly ResponseSchemaChecker _schema = new();

    private IMqttClient? _client;
    private CaptureWriter? _capture;

    // frameId -> waiter for the matching response.
    private readonly ConcurrentDictionary<string, TaskCompletionSource<UplinkRecord>> _waiters = new();

    // IMEI bookkeeping during listen.
    private readonly ConcurrentDictionary<string, int> _imeiCounts = new();

    // Target resolved for dispatch + the safety nets.
    private string? _targetImei;
    private AnShengDeviceKind _targetKind = AnShengDeviceKind.Switch4G;

    /// <summary>True once a setTime(+1h) was published and not yet reset in-script.</summary>
    private bool _clockOffsetDispatched;

    /// <summary>True once ANY Control step was published; arms the guaranteed switch-off net.</summary>
    private bool _controlDispatched;

    /// <summary>Shared script state (captured originals + cross-step observations).</summary>
    private readonly ScriptContext _ctx = new();

    /// <summary>Human-readable trace of what the safety nets did; surfaced in the report.</summary>
    private readonly List<string> _safetyNetLog = new();

    private string _finalSlots = "(未读取)";
    private bool _finalSwitchOffConfirmed;

    public FieldTestRunner(FieldTestOptions opt)
    {
        _opt = opt;
        _throttle = new AnShengCommandThrottle(_opt.ThrottleMs);
    }

    public async Task<FieldTestReportData> RunAsync(CancellationToken ct)
    {
        var data = new FieldTestReportData { Options = _opt, StartedUtc = DateTime.UtcNow };

        // 1) Local preflight (no device contact).
        data = data with { Preflight = PreflightRunner.Run(_builder, _parser, _opt.Imei ?? "123456789012345", _opt.SlotNum) };
        PrintPreflight(data.Preflight);

        // 2) Connect.
        var (ok, detail) = await ConnectAsync(ct);
        data = data with { ConnectionOk = ok, ConnectionDetail = detail };
        if (!ok)
        {
            Console.WriteLine($"[conn] 无法连接 broker: {detail}");
            return data with { ExitCode = 3 };
        }
        Console.WriteLine($"[conn] 已连接 {_opt.Host}:{_opt.Port}");

        // 3) Subscribe to uplink filter.
        _capture = new CaptureWriter(_opt.OutputDirectory);
        data = data with { CapturePath = _capture.Path };
        await SubscribeAsync();
        Console.WriteLine($"[sub] 已订阅上行过滤: {_opt.UplinkTopicFilter} (QoS {_opt.Qos})");

        // 4) Listen phase.
        await ListenPhaseAsync(ct);
        var heard = _imeiCounts.Keys.OrderBy(k => k).ToList();
        data = data with { HeardImeis = heard, ImeiMessageCounts = new Dictionary<string, int>(_imeiCounts) };
        PrintHeardImeis(heard);

        // 5) Listen-only short-circuit.
        if (_opt.ListenOnly)
        {
            Console.WriteLine("[done] --listen-only：已采集上行样本，未下发任何命令。");
            return data with { ExitCode = 0, AllUplinks = SnapshotCaptured() };
        }

        // 6) No device heard and no explicit IMEI -> graceful exit.
        if (heard.Count == 0 && string.IsNullOrEmpty(_opt.Imei))
        {
            Console.WriteLine("[warn] 监听期间未收到任何设备上行，且未指定 --imei。");
            Console.WriteLine("       可能原因：设备离线 / 未触发上报 / 主题方向接反 / broker 无该设备连接。");
            Console.WriteLine("       处理建议：确认设备在线并上报；用 --imei 显式指定；复查主题方向（PublishTopicPattern=平台订阅）。");
            return data with { ExitCode = 2, AllUplinks = SnapshotCaptured() };
        }

        // 7) Resolve target IMEI + kind.
        var imei = _opt.Imei ?? heard[0];
        var kind = ResolveKind(imei);
        _targetImei = imei;
        _targetKind = kind;
        data = data with { ResolvedKind = kind.ToString() };
        Console.WriteLine($"[target] IMEI={imei}  Kind={kind}");

        // 8) Dispatch script.
        //    finally-based safety nets run in strict priority order:
        //      (a) physical safety  - force the switch OPEN and read it back;
        //      (b) clock            - undo the deliberate +1h offset;
        //      (c) auto-report      - restore the original push configuration.
        List<StepResult> steps = new();
        try
        {
            steps = await RunScriptAsync(imei, kind, ct);
        }
        finally
        {
            await GuaranteedSwitchOffAsync();
            await GuaranteedClockResetAsync();
            await GuaranteedAutoReportRestoreAsync();
        }

        data = data with
        {
            Steps = steps,
            AllUplinks = SnapshotCaptured(),
            SafetyNetLog = _safetyNetLog.ToList(),
            ScriptFindings = _ctx.Findings.ToList(),
            FinalSlots = _finalSlots,
            FinalSwitchOffConfirmed = _finalSwitchOffConfirmed
        };

        return data with { ExitCode = 0 };
    }

    // ---- connection -------------------------------------------------------

    private async Task<(bool Ok, string Detail)> ConnectAsync(CancellationToken ct)
    {
        try
        {
            var factory = new MqttFactory();
            _client = factory.CreateMqttClient();

            var clientId = "fieldtest_" + Guid.NewGuid().ToString("N")[..8];
            var options = new MqttClientOptionsBuilder()
                .WithTcpServer(_opt.Host, _opt.Port)
                .WithCredentials(_opt.Username, _opt.Password)
                .WithClientId(clientId)
                .WithCleanSession(true)
                .WithKeepAlivePeriod(TimeSpan.FromSeconds(_opt.KeepAliveSeconds))
                .Build();

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(TimeSpan.FromSeconds(15));
            await _client.ConnectAsync(options, linked.Token);
            return (true, $"clientId={clientId}");
        }
        catch (Exception ex)
        {
            return (false, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task SubscribeAsync()
    {
        if (_client is null) return;
        var qos = ToQos(_opt.Qos);
        var subscribeOptions = new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter(_opt.UplinkTopicFilter, qos)
            .Build();
        await _client.SubscribeAsync(subscribeOptions);
        _client.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;
    }

    // ---- capture ----------------------------------------------------------

    private Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        var raw = e.ApplicationMessage.ConvertPayloadToString();
        var topic = e.ApplicationMessage.Topic;
        var record = BuildRecord(topic, raw);

        // Persist + keep in-memory for reporting.
        _capture?.Write(record);
        lock (_captured) _captured.Add(record);

        // Track IMEI.
        if (!string.IsNullOrEmpty(record.Imei))
            _imeiCounts.AddOrUpdate(record.Imei, 1, (_, v) => v + 1);

        // Wake any pending response waiter that matches frameId.
        if (record.FrameId is not null && _waiters.TryRemove(record.FrameId, out var tcs))
            tcs.TrySetResult(record);

        return Task.CompletedTask;
    }

    private UplinkRecord BuildRecord(string topic, string raw)
    {
        try
        {
            var msg = _parser.Parse(raw);
            var category = _parser.GetCategory(msg!).ToString();
            var isWill = AnShengMessageParser.IsWillMessage(msg);
            string? normalized = null;
            if (msg is { IsEvent: true })
                normalized = _parser.NormalizeForSensorData(msg, topic);
            return new UplinkRecord
            {
                ReceivedAtUtc = DateTime.UtcNow,
                Topic = topic,
                Raw = raw,
                Message = msg,
                Category = category,
                IsWill = isWill,
                Normalized = normalized
            };
        }
        catch (Exception ex)
        {
            return new UplinkRecord
            {
                ReceivedAtUtc = DateTime.UtcNow,
                Topic = topic,
                Raw = raw,
                ParseError = $"{ex.GetType().Name}: {ex.Message}"
            };
        }
    }

    private async Task ListenPhaseAsync(CancellationToken ct)
    {
        // --listen-sec 0 (or negative) => skip the listen phase entirely.
        if (_opt.ListenSeconds <= 0)
        {
            Console.WriteLine("[listen] --listen-sec<=0，跳过监听阶段（显式 IMEI 直接下发）。");
            return;
        }

        Console.WriteLine($"[listen] 监听 {_opt.ListenSeconds}s，抓取上行（Ctrl+C 可提前结束）...");
        var elapsed = 0;
        var tick = Math.Max(1, Math.Min(10, _opt.ListenSeconds));
        try
        {
            while (elapsed < _opt.ListenSeconds)
            {
                var remaining = _opt.ListenSeconds - elapsed;
                var wait = Math.Min(tick, remaining);
                await Task.Delay(TimeSpan.FromSeconds(wait), ct);
                elapsed += wait;
                var total = _imeiCounts.Values.Sum();
                Console.WriteLine($"  ... 已 {elapsed}s，收到上行 {total} 条，IMEI {_imeiCounts.Count} 个");
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("  ... 监听被取消（Ctrl+C）");
        }
    }

    // ---- kind resolution --------------------------------------------------

    private AnShengDeviceKind ResolveKind(string imei)
    {
        if (!string.IsNullOrEmpty(_opt.Kind)
            && Enum.TryParse<AnShengDeviceKind>(_opt.Kind, ignoreCase: true, out var explicitKind))
        {
            return explicitKind;
        }

        // Infer from a captured getDevInfo/getDevStatus response.
        var sample = SnapshotCaptured()
            .FirstOrDefault(r => r.Message is not null
                                 && (r.Method == "getDevInfo" || r.Method == "getDevStatus"));
        if (sample?.Message?.RawJson is { } raw)
        {
            try
            {
                using var d = JsonDocument.Parse(raw);
                var root = d.RootElement;
                string? netType = Pick(root, "netType", "net");
                string? version = Pick(root, "version", "ver");
                string? model = Pick(root, "model");
                if (netType is not null || model is not null)
                {
                    return AnShengDeviceKindResolver.Resolve(netType ?? "", version ?? "", model ?? "");
                }
            }
            catch
            {
                // fall through to default
            }
        }
        return AnShengDeviceKind.Switch4G;
    }

    private static string? Pick(JsonElement root, params string[] names)
    {
        foreach (var n in names)
            if (root.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString();
        return null;
    }

    // ---- script dispatch --------------------------------------------------

    /// <summary>
    /// Executes the script. NEVER aborts on a step failure: every step is individually guarded so a
    /// deliberately-failing probe (e.g. Q9 actions slots:[1,3,4]) cannot prevent the safety finale
    /// from running.
    /// </summary>
    private async Task<List<StepResult>> RunScriptAsync(string imei, AnShengDeviceKind kind, CancellationToken ct)
    {
        var steps = CommandScript.Build(_opt.SlotNum, _opt.DelaySeconds, _opt.DwellSeconds);
        var results = new List<StepResult>();

        Console.WriteLine($"[script] 共 {steps.Count} 步（含 1 个 {_opt.DwellSeconds}s 驻留窗口）");

        for (int i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            var idx = i + 1;
            try
            {
                var result = await RunStepAsync(step, idx, imei, kind, ct);
                results.Add(result);
            }
            catch (Exception ex)
            {
                // Never let a single step abort the script - the finale must always be reached.
                results.Add(new StepResult
                {
                    Index = idx, Method = step.Method, Group = step.Group, Risk = step.Risk,
                    Purpose = step.Purpose, Kind = kind, Verdict = StepVerdict.Error,
                    ExpectedFailure = step.ExpectFailure,
                    Remark = $"步骤内部异常（已捕获并继续）: {ex.GetType().Name}: {ex.Message}"
                });
                Console.WriteLine($"[{idx:D2}] {step.Method,-16} ERROR (内部异常，继续执行) {ex.GetType().Name}: {ex.Message}");
            }
        }

        return results;
    }

    private async Task<StepResult> RunStepAsync(
        ScriptStep step, int idx, string imei, AnShengDeviceKind kind, CancellationToken ct)
    {
        // --- passive dwell window (no command) -----------------------------
        if (step.DwellSeconds > 0)
            return await RunDwellStepAsync(step, idx, kind, ct);

        // --- safety gate ---------------------------------------------------
        if (!CommandScript.IsAllowed(step.Risk, _opt.AllowConfig, _opt.AllowControl))
        {
            var reason = CommandScript.BlockedReason(step.Risk);
            Console.WriteLine($"[{idx:D2}] {step.Method,-16} SKIPPED ({reason})");
            return Skipped(step, idx, kind, reason);
        }

        // --- runtime precondition (e.g. refuse to change what we could not read) ---
        if (step.Precondition is not null)
        {
            var (pOk, pReason) = step.Precondition(_ctx);
            if (!pOk)
            {
                Console.WriteLine($"[{idx:D2}] {step.Method,-16} SKIPPED ({pReason})");
                return Skipped(step, idx, kind, pReason);
            }
        }

        // --- throttle ------------------------------------------------------
        var throttleStart = Environment.TickCount;
        await _throttle.WaitTurnAsync(imei, ct);
        var waited = Environment.TickCount - throttleStart;

        // --- build ---------------------------------------------------------
        (string FrameId, string Payload) request;
        try
        {
            request = step.Build(_builder, imei, kind, _ctx);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{idx:D2}] {step.Method,-16} ERROR (构造失败: {ex.GetType().Name}: {ex.Message})");
            return new StepResult
            {
                Index = idx, Method = step.Method, Group = step.Group, Risk = step.Risk,
                Purpose = step.Purpose, Kind = kind, ExpectedFailure = step.ExpectFailure,
                Verdict = StepVerdict.Error,
                Remark = $"构造下行报文失败: {ex.GetType().Name}: {ex.Message}"
            };
        }

        var payload = request.Payload;
        var frameId = request.FrameId;
        Console.WriteLine($"[{idx:D2}] {step.Method,-16} -> {payload}");

        // --- publish + wait --------------------------------------------------
        var sentAt = DateTime.UtcNow;
        UplinkRecord? response;
        try
        {
            response = await PublishAndWaitAsync(imei, payload, frameId, _opt.ResponseTimeoutSeconds, ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{idx:D2}] {step.Method,-16} ERROR (发布失败: {ex.Message})");
            return new StepResult
            {
                Index = idx, Method = step.Method, Group = step.Group, Risk = step.Risk,
                Purpose = step.Purpose, Kind = kind, RequestPayload = payload, RequestFrameId = frameId,
                SentAtUtc = sentAt, ThrottleWaitedMs = waited, ExpectedFailure = step.ExpectFailure,
                Verdict = StepVerdict.Error,
                Remark = $"发布失败: {ex.GetType().Name}: {ex.Message}"
            };
        }

        // Track state-changing dispatches for the safety nets (publish succeeded by now).
        if (step.Risk == StepRisk.Control) _controlDispatched = true;
        if (step.Clock == ClockAction.Offset) _clockOffsetDispatched = true;
        else if (step.Clock == ClockAction.Reset) _clockOffsetDispatched = false;

        var rtt = (long)(DateTime.UtcNow - sentAt).TotalMilliseconds;

        if (response is null)
        {
            Console.WriteLine($"[{idx:D2}] {step.Method,-16} TIMEOUT (frameId={frameId})");
            return new StepResult
            {
                Index = idx, Method = step.Method, Group = step.Group, Risk = step.Risk,
                Purpose = step.Purpose, Kind = kind, RequestPayload = payload, RequestFrameId = frameId,
                SentAtUtc = sentAt, ThrottleWaitedMs = waited, RoundTripMs = rtt,
                FrameIdMatched = false, ExpectedFailure = step.ExpectFailure,
                Verdict = StepVerdict.Timeout,
                Remark = $"{_opt.ResponseTimeoutSeconds}s 内未收到 frameId={frameId} 的应答"
            };
        }

        // --- schema check ----------------------------------------------------
        AnShengCommandCatalog.TryGet(step.Method, out var spec);
        var schema = _schema.Check(step.Method, spec, response.Raw);

        // --- capture + assert -------------------------------------------------
        var assertError = string.Empty;
        JsonElement? root = null;
        try
        {
            using var doc = JsonDocument.Parse(response.Raw);
            root = doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            assertError = $"响应不是合法 JSON，无法断言: {ex.Message}";
        }

        if (root is { } body)
        {
            try { step.Capture?.Invoke(body, _ctx); }
            catch (Exception ex) { assertError = AppendReason(assertError, $"capture 钩子异常: {ex.Message}"); }

            if (step.Assert is not null)
            {
                try
                {
                    var err = step.Assert(body, _ctx);
                    if (!string.IsNullOrEmpty(err)) assertError = AppendReason(assertError, err);
                }
                catch (Exception ex)
                {
                    assertError = AppendReason(assertError, $"assert 钩子异常: {ex.Message}");
                }
            }
        }

        // --- verdict -----------------------------------------------------------
        var resultText = response.Message?.Result;
        var deviceOk = string.Equals(resultText, "ok", StringComparison.OrdinalIgnoreCase);

        StepVerdict verdict;
        string remark;
        if (step.ExpectFailure)
        {
            // A rejection is the expected outcome; record the exact error literal for the vendor.
            verdict = deviceOk ? StepVerdict.Mismatch : StepVerdict.Pass;
            remark = deviceOk
                ? $"预期被拒绝但设备返回 ok（result=\"{resultText}\"）—— 与预期不符，需人工确认设备实际行为"
                : $"预期失败并确实被拒绝，设备错误码 result=\"{resultText ?? "(缺失)"}\"";
        }
        else if (!string.IsNullOrEmpty(assertError))
        {
            verdict = StepVerdict.Mismatch;
            remark = assertError;
        }
        else if (!deviceOk)
        {
            verdict = StepVerdict.Mismatch;
            remark = $"设备返回非 ok：result=\"{resultText ?? "(缺失)"}\"; {schema.Summary}";
        }
        else
        {
            verdict = schema.HasError ? StepVerdict.Mismatch : StepVerdict.Pass;
            remark = schema.Summary;
        }

        // Bookkeeping for the auto-report restore net.
        if (verdict == StepVerdict.Pass || step.ExpectFailure)
        {
            if (step.AutoReport == AutoReportAction.Change && deviceOk) _ctx.AutoReportChanged = true;
            else if (step.AutoReport == AutoReportAction.Restore && deviceOk) _ctx.AutoReportChanged = false;
        }

        var label = verdict switch
        {
            StepVerdict.Pass when step.ExpectFailure => "PASS(预期失败)",
            StepVerdict.Pass => "PASS",
            _ => verdict.ToString().ToUpperInvariant()
        };
        Console.WriteLine($"[{idx:D2}] {step.Method,-16} {label} rtt={rtt}ms result={resultText}" +
                          (string.IsNullOrEmpty(assertError) ? "" : $"  !! {assertError}"));

        return new StepResult
        {
            Index = idx, Method = step.Method, Group = step.Group, Risk = step.Risk,
            Purpose = step.Purpose, Kind = kind,
            RequestPayload = payload, RequestFrameId = frameId, SentAtUtc = sentAt,
            ThrottleWaitedMs = waited, RoundTripMs = rtt,
            ResponsePayload = response.Raw, ResponseFrameId = response.FrameId,
            ResponseResult = resultText, FrameIdMatched = true,
            Schema = schema, ExpectedFailure = step.ExpectFailure, AssertError = assertError,
            Verdict = verdict, Remark = remark
        };
    }

    /// <summary>Passive window: send nothing, just record every uplink that arrives.</summary>
    private async Task<StepResult> RunDwellStepAsync(ScriptStep step, int idx, AnShengDeviceKind kind, CancellationToken ct)
    {
        int baseline;
        lock (_captured) baseline = _captured.Count;

        Console.WriteLine($"[{idx:D2}] {"(dwell)",-16} 驻留监听 {step.DwellSeconds}s：{step.Purpose}");

        var elapsed = 0;
        var cancelled = false;
        try
        {
            while (elapsed < step.DwellSeconds)
            {
                var wait = Math.Min(10, step.DwellSeconds - elapsed);
                await Task.Delay(TimeSpan.FromSeconds(wait), ct);
                elapsed += wait;
                int seen;
                lock (_captured) seen = _captured.Count - baseline;
                Console.WriteLine($"     ... 驻留 {elapsed}/{step.DwellSeconds}s，窗口内收到上行 {seen} 条");
            }
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
            Console.WriteLine("     ... 驻留被取消（Ctrl+C）");
        }

        List<UplinkRecord> window;
        lock (_captured) window = _captured.Skip(baseline).ToList();

        var autoReports = window.Count(r => r.Method == "getDevStatus" && r.FrameId is null or "");
        var delayEvents = window.Count(r => r.Method is "delayEvent");
        var summary = $"窗口 {elapsed}s 内收到上行 {window.Count} 条" +
                      $"（delayEvent {delayEvents} 条 / 无 frameId 的 getDevStatus 推送 {autoReports} 条）" +
                      (cancelled ? "；窗口被提前取消" : string.Empty);
        Console.WriteLine($"     => {summary}");

        _ctx.Findings.Add($"驻留窗口结论：{summary}。" +
                          (delayEvents > 0
                              ? " Q20：确实收到 delayEvent。"
                              : " Q20：未收到 delayEvent（延时任务到期未产生事件上报，或事件走了别的 method）。") +
                          (window.Count == 0
                              ? " G6：自动上报窗口内 0 条 —— 设备在该配置下仍不主动推送。"
                              : string.Empty));

        return new StepResult
        {
            Index = idx, Method = step.Method, Group = step.Group, Risk = step.Risk,
            Purpose = step.Purpose, Kind = kind,
            DwellSeconds = elapsed,
            DwellMessages = window.Select(r => r.Raw).ToList(),
            Verdict = StepVerdict.Pass,
            Remark = summary
        };
    }

    /// <summary>Publish a payload and await the response with the matching frameId. Returns null on timeout.</summary>
    private async Task<UplinkRecord?> PublishAndWaitAsync(
        string imei, string payload, string? frameId, int timeoutSeconds, CancellationToken ct)
    {
        TaskCompletionSource<UplinkRecord>? tcs = null;
        if (!string.IsNullOrEmpty(frameId))
        {
            tcs = new TaskCompletionSource<UplinkRecord>(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters[frameId] = tcs;
        }

        try
        {
            var appMsg = new MqttApplicationMessageBuilder()
                .WithTopic(_opt.DownlinkTopicFor(imei))
                .WithPayload(Encoding.UTF8.GetBytes(payload))
                .WithQualityOfServiceLevel(ToQos(_opt.Qos))
                .Build();
            await _client!.PublishAsync(appMsg, ct);
        }
        catch
        {
            if (tcs is not null) _waiters.TryRemove(frameId!, out _);
            throw;
        }

        if (tcs is null) return null;

        using var to = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, to.Token);
        try
        {
            return await tcs.Task.WaitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            _waiters.TryRemove(frameId!, out _);
            return null;
        }
    }

    // ---- guaranteed safety nets (finally block; must never throw) ----------

    /// <summary>
    /// P0 physical safety net. Whenever ANY control command was dispatched during this run, force the
    /// switch OPEN before exiting and read the state back — regardless of exceptions, timeouts or
    /// Ctrl+C. This exists because the round-1 script left the switch permanently CLOSED:
    /// startDelayTask(sAction="on") closes immediately, and the subsequent stopDelayTask cancelled the
    /// task so its eAction="off" never fired.
    /// Runs BEFORE the clock reset: physical safety first, clock second.
    /// </summary>
    private async Task GuaranteedSwitchOffAsync()
    {
        if (!_controlDispatched)
        {
            Log("[safety][switch] 本轮未下发任何 control 命令，安全网未触发（无需强制断开）。");
            return;
        }
        if (_client is null || !_client.IsConnected || _targetImei is null)
        {
            Log("[safety][switch][WARN] 安全网已触发但 MQTT 连接不可用，无法强制断开！需人工确认开关状态。");
            return;
        }

        Log("[safety][switch] 安全网已触发：本轮执行过 control 步骤，强制下发 action off 并读回验证。");

        // Use a fresh, cancellation-free token: the net must still run after Ctrl+C.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_opt.ResponseTimeoutSeconds + 5));
        try
        {
            var (offFrame, offPayload) = _builder.BuildAction(_targetImei, _opt.SlotNum, "off", null, _targetKind);
            var offResp = await PublishAndWaitAsync(_targetImei, offPayload, offFrame, _opt.ResponseTimeoutSeconds, cts.Token);
            Log(offResp is null
                ? $"[safety][switch][WARN] action off 已发出但未收到应答 (frameId={offFrame})。"
                : $"[safety][switch] action off 应答: {offResp.Raw}");

            await Task.Delay(200, cts.Token);

            var (stFrame, stPayload) = _builder.BuildGetDevStatus(_targetImei, null, _targetKind);
            var stResp = await PublishAndWaitAsync(_targetImei, stPayload, stFrame, _opt.ResponseTimeoutSeconds, cts.Token);
            if (stResp is null)
            {
                _finalSlots = "(读回超时)";
                Log("[safety][switch][WARN] 读回 getDevStatus 超时，最终物理状态无法确认！");
                return;
            }

            using var doc = JsonDocument.Parse(stResp.Raw);
            var slots = CommandScript.ReadSlots(doc.RootElement);
            _finalSlots = CommandScript.FormatSlots(slots);
            _finalSwitchOffConfirmed = slots is { Length: > 0 } && slots.All(s => s == 0);

            Log(_finalSwitchOffConfirmed
                ? $"[safety][switch] 读回确认最终物理状态 slots={_finalSlots} —— 开关已断开。"
                : $"[safety][switch][WARN] 读回最终物理状态 slots={_finalSlots} —— 未确认全部断开，需人工介入！");
            Log($"[safety][switch] 读回原始报文: {stResp.Raw}");
        }
        catch (Exception ex)
        {
            Log($"[safety][switch][WARN] 强制断开流程异常，需人工确认开关状态: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Best-effort clock safety net: if the script deliberately offset the device clock (setTime +1h
    /// for Q11/Q12) but the in-script reset step did not run/complete, publish a restore setTime.
    /// Runs from a finally block; must not throw.
    /// </summary>
    private async Task GuaranteedClockResetAsync()
    {
        if (!_clockOffsetDispatched)
        {
            Log("[safety][clock] 时钟未处于偏移状态，安全网未触发。");
            return;
        }
        if (_client is null || !_client.IsConnected || _targetImei is null)
        {
            Log("[safety][clock][WARN] 安全网已触发但 MQTT 连接不可用，设备时钟可能仍偏移 +1h！");
            return;
        }

        Log("[safety][clock] 安全网已触发：剧本内复位未完成，强制复位设备时钟。");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_opt.ResponseTimeoutSeconds + 5));
        try
        {
            var (frame, payload) = _builder.BuildSetTime(_targetImei, DateTime.UtcNow, _targetKind);
            var resp = await PublishAndWaitAsync(_targetImei, payload, frame, _opt.ResponseTimeoutSeconds, cts.Token);
            Log(resp is null
                ? $"[safety][clock][WARN] setTime 复位已发出但未收到应答 (frameId={frame})。"
                : $"[safety][clock] 时钟已复位为当前 UTC，应答: {resp.Raw}");
        }
        catch (Exception ex)
        {
            Log($"[safety][clock][WARN] 时钟复位失败，需人工介入: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _clockOffsetDispatched = false;
        }
    }

    /// <summary>
    /// Auto-report safety net: G6 deliberately turns device push ON; if the in-script restore did not
    /// complete, put the original configuration back so we never leave the device pushing forever.
    /// </summary>
    private async Task GuaranteedAutoReportRestoreAsync()
    {
        if (!_ctx.AutoReportChanged)
        {
            Log("[safety][autoreport] 自动上报配置未被修改（或剧本内已复位），安全网未触发。");
            return;
        }
        if (_client is null || !_client.IsConnected || _targetImei is null)
        {
            Log("[safety][autoreport][WARN] 安全网已触发但 MQTT 连接不可用，设备可能仍在按测试间隔推送！");
            return;
        }
        if (!_ctx.AutoReportCaptured)
        {
            Log("[safety][autoreport][WARN] 无原始配置可还原（未成功读到 getAutoReport），放弃自动还原，需人工确认。");
            return;
        }

        Log($"[safety][autoreport] 安全网已触发：还原 getDevStatusSec={_ctx.OrigGetDevStatusSec}。");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_opt.ResponseTimeoutSeconds + 5));
        try
        {
            var (frame, payload) = _builder.BuildSetAutoReport(
                _targetImei,
                getDevStatusSec: _ctx.OrigGetDevStatusSec,
                orderUpSec: _ctx.OrigOrderUpSec,
                rs485Sec: _ctx.OrigRs485Sec,
                rs485BaudRate: _ctx.OrigRs485BaudRate,
                getDevStatusQ: _ctx.OrigGetDevStatusQ,
                rs485SendWaitMs: _ctx.OrigRs485SendWaitMs,
                rs485Array: _ctx.OrigRs485Array,
                kind: _targetKind);
            var resp = await PublishAndWaitAsync(_targetImei, payload, frame, _opt.ResponseTimeoutSeconds, cts.Token);
            Log(resp is null
                ? $"[safety][autoreport][WARN] setAutoReport 还原已发出但未收到应答 (frameId={frame})。"
                : $"[safety][autoreport] 自动上报配置已还原，应答: {resp.Raw}");
        }
        catch (Exception ex)
        {
            Log($"[safety][autoreport][WARN] 自动上报还原失败，需人工介入: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _ctx.AutoReportChanged = false;
        }
    }

    // ---- helpers ----------------------------------------------------------

    private readonly List<UplinkRecord> _captured = new();

    private List<UplinkRecord> SnapshotCaptured()
    {
        lock (_captured) return new List<UplinkRecord>(_captured);
    }

    private void Log(string line)
    {
        _safetyNetLog.Add(line);
        Console.WriteLine(line);
    }

    private static string AppendReason(string existing, string add) =>
        string.IsNullOrEmpty(existing) ? add : existing + "; " + add;

    private static StepResult Skipped(ScriptStep step, int idx, AnShengDeviceKind kind, string reason) => new()
    {
        Index = idx, Method = step.Method, Group = step.Group, Risk = step.Risk,
        Purpose = step.Purpose, Kind = kind, ExpectedFailure = step.ExpectFailure,
        Verdict = StepVerdict.Skipped, Remark = reason
    };

    private static MqttQualityOfServiceLevel ToQos(int q) => q switch
    {
        0 => MqttQualityOfServiceLevel.AtMostOnce,
        2 => MqttQualityOfServiceLevel.ExactlyOnce,
        _ => MqttQualityOfServiceLevel.AtLeastOnce
    };

    private static void PrintPreflight(IReadOnlyList<PreflightCheck> checks)
    {
        Console.WriteLine("[preflight] 本地自检（不接触设备）:");
        foreach (var c in checks)
            Console.WriteLine($"  [{(c.Passed ? "PASS" : "FAIL")}] {c.Name} :: {c.Detail}");
    }

    private static void PrintHeardImeis(IReadOnlyList<string> heard)
    {
        Console.WriteLine("[listen] 监听到 IMEI:");
        if (heard.Count == 0)
            Console.WriteLine("  （无）");
        else
            foreach (var imei in heard)
                Console.WriteLine($"  - {imei}");
    }
}
