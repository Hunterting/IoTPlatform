using IoTPlatform.Infrastructure.Protocol.AnSheng;

namespace IoTPlatform.Tools.AnShengFieldTest;

/// <summary>Verdict for a single dispatched step.</summary>
public enum StepVerdict
{
    Pass,       // response received, frameId matched, schema consistent
    Mismatch,   // response received but schema/contract conflict
    Timeout,    // no matching response within timeout
    Skipped,    // blocked by safety gate (config/control not allowed)
    Error       // tool-side error building/sending
}

/// <summary>Result of executing one script step against the device.</summary>
public sealed class StepResult
{
    public int Index { get; init; }
    public string Method { get; init; } = string.Empty;
    public string Group { get; init; } = string.Empty;
    public StepRisk Risk { get; init; }
    public string Purpose { get; init; } = string.Empty;
    public AnShengDeviceKind Kind { get; init; }

    public string RequestPayload { get; init; } = string.Empty;
    public string? RequestFrameId { get; init; }
    public DateTime SentAtUtc { get; init; }
    public int ThrottleWaitedMs { get; init; }

    public string? ResponsePayload { get; init; }
    public string? ResponseFrameId { get; init; }
    public string? ResponseResult { get; init; }
    public long? RoundTripMs { get; init; }

    public bool FrameIdMatched { get; init; }
    public SchemaCheckResult? Schema { get; init; }
    public StepVerdict Verdict { get; init; }
    public string Remark { get; init; } = string.Empty;

    /// <summary>True when the step was declared as "expected to be rejected" (e.g. Q9 actions slots:[1,3,4]).</summary>
    public bool ExpectedFailure { get; init; }

    /// <summary>Message of the step-level assertion failure (e.g. slots[0] != 0), empty when OK/absent.</summary>
    public string AssertError { get; init; } = string.Empty;

    /// <summary>For a passive DWELL step: the window length in seconds (0 for normal command steps).</summary>
    public int DwellSeconds { get; init; }

    /// <summary>For a passive DWELL step: every uplink captured inside the window, verbatim.</summary>
    public List<string> DwellMessages { get; init; } = new();

    public string VerdictLabel => Verdict switch
    {
        StepVerdict.Pass => "PASS",
        StepVerdict.Mismatch => "MISMATCH",
        StepVerdict.Timeout => "TIMEOUT",
        StepVerdict.Skipped => "SKIPPED",
        StepVerdict.Error => "ERROR",
        _ => "?"
    };
}

/// <summary>A single local preflight self-check (no device contact).</summary>
public sealed class PreflightCheck
{
    public string Name { get; init; } = string.Empty;
    public bool Passed { get; init; }
    public string Detail { get; init; } = string.Empty;
}
