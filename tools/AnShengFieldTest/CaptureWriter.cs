using System.IO;
using System.Text;
using System.Text.Json;

namespace IoTPlatform.Tools.AnShengFieldTest;

/// <summary>
/// Writes captured uplink messages to a JSONL file, one object per line.
/// Every line keeps the raw payload verbatim so the capture can become a
/// byte-exact Phase 2 regression baseline.
///
/// Line shape:
///   {"ts":"ISO8601","topic":"...","raw":"&lt;verbatim&gt;","parsed":{...} | {"parseError":"..."}}
/// </summary>
public sealed class CaptureWriter : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly string _path;

    public CaptureWriter(string directory)
    {
        Directory.CreateDirectory(directory);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        _path = System.IO.Path.Combine(directory, $"uplink-{stamp}.jsonl");
        // UTF8 no BOM, auto-flush so partial captures survive a Ctrl+C.
        _writer = new StreamWriter(_path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true
        };
    }

    public string Path => _path;

    public void Write(UplinkRecord record)
    {
        var line = new Dictionary<string, object?>
        {
            ["ts"] = record.ReceivedAtUtc.ToString("o"),
            ["topic"] = record.Topic,
            ["raw"] = record.Raw
        };

        if (record.Message is not null)
        {
            var parsed = new Dictionary<string, object?>
            {
                ["method"] = record.Message.Method,
                ["imei"] = record.Message.Imei,
                ["result"] = record.Message.Result,
                ["frameId"] = record.Message.FrameId,
                ["rawTimestamp"] = record.Message.RawTimestamp,
                ["timestampUtc"] = record.Message.TimestampUtc?.ToString("o"),
                ["category"] = record.Category,
                ["isEvent"] = record.Message.IsEvent,
                ["isWill"] = record.IsWill,
                ["isOk"] = record.Message.IsOk,
                ["isUnsupported"] = record.Message.IsEvent ? false : (record.Message.Result == "method unsupported"),
                ["normalized"] = record.Normalized
            };
            line["parsed"] = parsed;
        }
        else
        {
            line["parsed"] = new Dictionary<string, object?> { ["parseError"] = record.ParseError ?? "unknown parse error" };
        }

        _writer.WriteLine(JsonSerializer.Serialize(line, new JsonSerializerOptions
        {
            WriteIndented = false,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }));
    }

    public void Dispose() => _writer.Dispose();
}
