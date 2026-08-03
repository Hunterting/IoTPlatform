using IoTPlatform.Infrastructure.Protocol.AnSheng;

namespace IoTPlatform.Tools.AnShengFieldTest;

/// <summary>
/// One captured uplink (device -&gt; platform) message.
/// Stores both the raw payload (verbatim) and the parsed <see cref="AnShengMessage"/>.
/// </summary>
public sealed class UplinkRecord
{
    /// <summary>UTC time the message was received by the tool.</summary>
    public DateTime ReceivedAtUtc { get; init; }

    /// <summary>The MQTT topic the message arrived on.</summary>
    public string Topic { get; init; } = string.Empty;

    /// <summary>The verbatim raw payload. NEVER mutated - kept for byte-exact regression baselines.</summary>
    public string Raw { get; init; } = string.Empty;

    /// <summary>Parsed message, or null when parsing failed.</summary>
    public AnShengMessage? Message { get; init; }

    /// <summary>Parse error text when <see cref="Message"/> is null.</summary>
    public string? ParseError { get; init; }

    /// <summary>High-level category, e.g. "command-response", "event", "will".</summary>
    public string Category { get; init; } = "unknown";

    /// <summary>True when this is a device last-will message.</summary>
    public bool IsWill { get; init; }

    /// <summary>Normalized (sensor-view) JSON payload when applicable.</summary>
    public string? Normalized { get; init; }

    public string? Imei => Message?.Imei;
    public string? FrameId => Message?.FrameId;
    public string? Method => Message?.Method;
}
