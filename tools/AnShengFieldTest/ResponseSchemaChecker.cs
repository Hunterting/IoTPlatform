using System.Text.Json;
using IoTPlatform.Infrastructure.Protocol.AnSheng;

namespace IoTPlatform.Tools.AnShengFieldTest;

/// <summary>Severity of a schema finding.</summary>
public enum FindingSeverity
{
    /// <summary>Hard conflict with the expected protocol contract.</summary>
    Error,
    /// <summary>Informational: field not covered by the current Catalog (candidate for Catalog extension).</summary>
    Info
}

/// <summary>One schema observation about a captured response.</summary>
public sealed class SchemaFinding
{
    public FindingSeverity Severity { get; init; }
    public string Message { get; init; } = string.Empty;
}

/// <summary>Result of checking a single response against the Catalog.</summary>
public sealed class SchemaCheckResult
{
    public List<SchemaFinding> Findings { get; } = new();
    public bool HasError => Findings.Any(f => f.Severity == FindingSeverity.Error);
    public string Summary => Findings.Count == 0
        ? "consistent"
        : string.Join("; ", Findings.Select(f => $"[{f.Severity}] {f.Message}"));
}

/// <summary>
/// Validates a captured device response against the production <see cref="AnShengCommandCatalog"/>.
///
/// The Catalog currently models the DOWNLINK (request) parameter schema only, so:
///   - Hard conflicts (missing result/frameId, legacy "param" wrapper, echoed-type mismatch) => ERROR.
///   - Response fields not declared in the Catalog => INFO (feed into "Catalog correction" suggestions).
/// </summary>
public sealed class ResponseSchemaChecker
{
    private static readonly HashSet<string> CommonResponseFields = new(StringComparer.Ordinal)
    {
        "method", "imei", "result", "frameId", "timestamp", "rawTimestamp", "ts", "ver", "model"
    };

    /// <summary>
    /// Check a raw response payload for a given method.
    /// </summary>
    /// <param name="method">The command method the response answers.</param>
    /// <param name="spec">The Catalog spec for the method (may be null if unknown).</param>
    /// <param name="responseRaw">The verbatim response JSON.</param>
    public SchemaCheckResult Check(string method, AnShengCommandSpec? spec, string responseRaw)
    {
        var result = new SchemaCheckResult();

        if (string.IsNullOrWhiteSpace(responseRaw))
        {
            result.Findings.Add(new SchemaFinding { Severity = FindingSeverity.Error, Message = "response payload is empty" });
            return result;
        }

        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(responseRaw);
            root = doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            result.Findings.Add(new SchemaFinding { Severity = FindingSeverity.Error, Message = $"response is not valid JSON: {ex.Message}" });
            return result;
        }

        // Common required fields for a command response.
        if (!root.TryGetProperty("result", out _))
            result.Findings.Add(new SchemaFinding { Severity = FindingSeverity.Error, Message = "missing required field 'result'" });
        if (!root.TryGetProperty("frameId", out _))
            result.Findings.Add(new SchemaFinding { Severity = FindingSeverity.Error, Message = "missing required field 'frameId' (cannot correlate)" });

        // Legacy "param" wrapper must not appear in v2 flattened protocol.
        if (root.TryGetProperty("param", out _))
            result.Findings.Add(new SchemaFinding { Severity = FindingSeverity.Error, Message = "legacy 'param' wrapper present (expected flattened top-level)" });

        // Declared request parameters: confirm echoed values keep a compatible type.
        var declared = spec?.Params ?? Enumerable.Empty<AnShengParamSpec>();
        var declaredNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in declared)
        {
            declaredNames.Add(p.Name);
            if (root.TryGetProperty(p.Name, out var elem))
            {
                if (!TypeCompatible(p.Type, elem))
                    result.Findings.Add(new SchemaFinding
                    {
                        Severity = FindingSeverity.Error,
                        Message = $"echoed param '{p.Name}' type mismatch: expected '{p.Type}', got {KindLabel(elem)}"
                    });
            }
        }

        // Undeclared response fields -> INFO (candidate for Catalog extension).
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in root.EnumerateObject())
            {
                if (CommonResponseFields.Contains(prop.Name) || declaredNames.Contains(prop.Name))
                    continue;
                result.Findings.Add(new SchemaFinding
                {
                    Severity = FindingSeverity.Info,
                    Message = $"undeclared response field '{prop.Name}' ({KindLabel(prop.Value)})"
                });
            }
        }

        return result;
    }

    private static bool TypeCompatible(AnShengParamType expectedType, JsonElement elem)
    {
        return expectedType switch
        {
            AnShengParamType.String => elem.ValueKind == JsonValueKind.String,
            AnShengParamType.Int or AnShengParamType.Double =>
                elem.ValueKind == JsonValueKind.Number,
            AnShengParamType.Bool => elem.ValueKind is JsonValueKind.True or JsonValueKind.False,
            AnShengParamType.Array => elem.ValueKind == JsonValueKind.Array,
            AnShengParamType.Object => elem.ValueKind == JsonValueKind.Object,
            _ => true // unknown declared type: do not flag.
        };
    }

    private static string KindLabel(JsonElement elem) => elem.ValueKind switch
    {
        JsonValueKind.String => "String",
        JsonValueKind.Number => elem.TryGetInt64(out var l) ? (l >= int.MinValue && l <= int.MaxValue ? "Number(Integer)" : "Number(Long)") : "Number(Double)",
        JsonValueKind.True or JsonValueKind.False => "Boolean",
        JsonValueKind.Array => $"Array<{ArrayElementLabel(elem)}>",
        JsonValueKind.Object => "Object",
        JsonValueKind.Null => "Null",
        _ => elem.ValueKind.ToString()
    };

    private static string ArrayElementLabel(JsonElement arr)
    {
        if (arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() == 0)
            return "?";
        var first = arr.EnumerateArray().First();
        return first.ValueKind switch
        {
            JsonValueKind.Number => "Number",
            JsonValueKind.String => "String",
            JsonValueKind.True or JsonValueKind.False => "Boolean",
            _ => first.ValueKind.ToString()
        };
    }
}
