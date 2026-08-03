using System.Text.Json;

namespace IoTPlatform.Tools.AnShengFieldTest;

/// <summary>
/// Command-line + appsettings.json options for the field test tool.
///
/// Topic direction is expressed from the PLATFORM's point of view:
///   - UplinkTopicFilter    : the topic filter the platform SUBSCRIBES to (devices publish here).
///                            Tool default "/iot/server/#" (broad net). It does NOT inherit the
///                            platform's narrower appsettings "PublishTopicPattern" so a silent device
///                            that publishes under any sub-path is still captured. Override with --uplink-filter.
///   - DownlinkTopicTemplate: the topic the platform PUBLISHES to (devices subscribe here).
///                            Tool default "/iot/client/iot-board/{imei}". Override with --downlink-tmpl.
///
/// DO NOT flip these: PublishTopicPattern == device-publish == platform-subscribe.
/// </summary>
public sealed class FieldTestOptions
{
    public string Host { get; set; } = "120.79.3.248";
    public int Port { get; set; } = 18883;
    public string Username { get; set; } = "admin";
    public string Password { get; set; } = "public";

    /// <summary>Platform SUBSCRIBE filter (device uplink). Broad net by default so a silent device under any sub-path is still captured.</summary>
    public string UplinkTopicFilter { get; set; } = "/iot/server/#";

    /// <summary>Platform PUBLISH template (device downlink). {imei} is replaced per command.</summary>
    public string DownlinkTopicTemplate { get; set; } = "/iot/client/iot-board/{imei}";

    public int Qos { get; set; } = 1;
    public int KeepAliveSeconds { get; set; } = 30;

    public int ListenSeconds { get; set; } = 60;
    public bool ListenOnly { get; set; }

    /// <summary>Allow config commands (e.g. setTime). Off by default.</summary>
    public bool AllowConfig { get; set; }

    /// <summary>Allow control commands (action / delay tasks). Off by default.</summary>
    public bool AllowControl { get; set; }

    public int ResponseTimeoutSeconds { get; set; } = 10;
    public int ThrottleMs { get; set; } = 100;

    public string? Imei { get; set; }

    /// <summary>Explicit device kind, e.g. "Switch4G". If null, inferred from captured getDevInfo.</summary>
    public string? Kind { get; set; }

    public string OutputDirectory { get; set; } = "captures";
    public int SlotNum { get; set; } = 1;
    public int DelaySeconds { get; set; } = 10;

    /// <summary>
    /// Passive dwell window (seconds) inserted after the delay-task probe. Must comfortably cover
    /// the Q20 delay expiry (15s) AND at least two auto-report periods (2 x 30s) => default 70.
    /// </summary>
    public int DwellSeconds { get; set; } = 70;

    public bool ShowHelp { get; set; }
    public string? Notes { get; set; }

    /// <summary>Explicit path to appsettings.json (overrides auto-discovery).</summary>
    public string? AppSettingsPath { get; set; }

    public static string HelpText => """
        AnShengFieldTest - 4G switch real-device capture + dispatch tool

        USAGE:
          dotnet run --project tools/AnShengFieldTest -- [options]

        CONNECTION (default = confirmed device broker):
          --host <host>        MQTT broker host        (default 120.79.3.248)
          --port <port>        MQTT broker port        (default 18883)
          --user <user>        username                (default admin)
          --pass <pass>        password                (default public)

        TOPICS (platform point of view):
          --uplink-filter <f>  SUBSCRIBE filter        (default /iot/server/#)
          --downlink-tmpl <t>  PUBLISH template        (default /iot/client/iot-board/{imei})

        BEHAVIOR:
          --listen-sec <n>     listen duration seconds (default 60; 0 = skip listening)
          --listen-only        listen + read-only probe, NO control/config dispatch
          --imei <imei>        target device IMEI (default: first heard).
                                Explicit --imei skips device discovery; pair with --listen-sec 0
                                to dispatch immediately without waiting for uplink.
          --kind <kind>        device kind, e.g. Switch4G (default: infer)
          --slot <n>           delay-task slot number  (default 1)
          --delay <sec>        delay-task seconds      (default 10)
          --dwell-sec <n>      passive dwell window    (default 70; covers Q20 delayEvent + 2x30s auto-report)
          --resp-timeout <n>   response wait seconds   (default 10)
          --throttle <ms>      per-imei throttle ms     (default 100)
          --out <dir>          capture output dir      (default captures)
          --appsettings <p>    path to appsettings.json
          --notes <text>       free-text notes appended to report
          --allow-config       permit config commands (setTime)
          --allow-control      permit control commands (action/delay tasks)
          --help               show this help

        SAFETY:
          Read-only commands (getDevInfo/getDevStatus/getDelayTasks) always run.
          Config/Control commands are SKIPPED unless explicitly allowed.
          setTime 会把设备时钟拨 +1h 以验证 Q11/Q12；剧本末尾自动复位并二次确认(getDevStatus)。
          setAutoReport 会临时打开自动上报(30s)；剧本末尾还原为原始值（读不到原值就拒绝修改）。
          剧本末尾强制 action off + getDevStatus 读回断言 slots[0]==0，确保开关不被留在闭合状态。
          即便运行中断/异常/超时，finally 中也会依次执行三道安全网：
            (1) 强制断开开关并读回验证  (2) 复位时钟  (3) 还原自动上报配置
          Full script (物理动作真实开关，需用户放行): --allow-config --allow-control.
        """;

    /// <summary>
    /// Parse command line args, then layer in appsettings.json "AnShengMqtt" if found.
    /// Precedence: built-in defaults &lt; appsettings.json &lt; command line.
    /// </summary>
    public static FieldTestOptions Parse(string[] args)
    {
        var opt = new FieldTestOptions();

        // 1) appsettings.json (lowest precedence of the two sources we merge).
        string? appSettingsPath = opt.AppSettingsPath ?? FindAppSettingsPath();
        if (appSettingsPath is not null && File.Exists(appSettingsPath))
        {
            try
            {
                var json = File.ReadAllText(appSettingsPath);
                using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                });
                if (doc.RootElement.TryGetProperty("AnShengMqtt", out var mqtt))
                {
                    if (mqtt.TryGetProperty("Host", out var host) && host.ValueKind == JsonValueKind.String)
                        opt.Host = host.GetString()!;
                    if (mqtt.TryGetProperty("Port", out var port) && port.TryGetInt32(out var p))
                        opt.Port = p;
                    if (mqtt.TryGetProperty("Username", out var user) && user.ValueKind == JsonValueKind.String)
                        opt.Username = user.GetString()!;
                    if (mqtt.TryGetProperty("Password", out var pass) && pass.ValueKind == JsonValueKind.String)
                        opt.Password = pass.GetString()!;
                    // NOTE: topic filters are intentionally NOT inherited from appsettings.
                    // The platform's PublishTopicPattern is a narrow "/iot/server/iot-board/+" used by
                    // production; this tool deliberately uses a broad capture net ("/iot/server/#") and
                    // must not be forced back to the narrow pattern. Override only via CLI flags.
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[warn] failed to read appsettings '{appSettingsPath}': {ex.Message}");
            }
        }

        // 2) command line (highest precedence).
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            string? Next() => i + 1 < args.Length ? args[++i] : null;

            switch (a)
            {
                case "--help": opt.ShowHelp = true; break;
                case "--host": opt.Host = Next() ?? opt.Host; break;
                case "--port": if (int.TryParse(Next(), out var port)) opt.Port = port; break;
                case "--user": opt.Username = Next() ?? opt.Username; break;
                case "--pass": opt.Password = Next() ?? opt.Password; break;
                case "--uplink-filter": opt.UplinkTopicFilter = Next() ?? opt.UplinkTopicFilter; break;
                case "--downlink-tmpl": opt.DownlinkTopicTemplate = Next() ?? opt.DownlinkTopicTemplate; break;
                case "--listen-sec": if (int.TryParse(Next(), out var ls)) opt.ListenSeconds = ls; break;
                case "--listen-only": opt.ListenOnly = true; break;
                case "--imei": opt.Imei = Next(); break;
                case "--kind": opt.Kind = Next(); break;
                case "--slot": if (int.TryParse(Next(), out var slot)) opt.SlotNum = slot; break;
                case "--delay": if (int.TryParse(Next(), out var d)) opt.DelaySeconds = d; break;
                case "--dwell-sec": if (int.TryParse(Next(), out var dw)) opt.DwellSeconds = dw; break;
                case "--resp-timeout": if (int.TryParse(Next(), out var rt)) opt.ResponseTimeoutSeconds = rt; break;
                case "--throttle": if (int.TryParse(Next(), out var th)) opt.ThrottleMs = th; break;
                case "--out": opt.OutputDirectory = Next() ?? opt.OutputDirectory; break;
                case "--appsettings": opt.AppSettingsPath = Next(); break;
                case "--notes": opt.Notes = Next(); break;
                case "--allow-config": opt.AllowConfig = true; break;
                case "--allow-control": opt.AllowControl = true; break;
                default:
                    if (a.StartsWith("--", StringComparison.Ordinal))
                        Console.Error.WriteLine($"[warn] unknown option: {a}");
                    break;
            }
        }

        return opt;
    }

    /// <summary>
    /// Locate appsettings.json by walking up from this tool project looking for the
    /// main project marker (IoTPlatform.csproj). Honors an explicit --appsettings path.
    /// </summary>
    private static string? FindAppSettingsPath()
    {
        // Explicit override wins.
        // (Caller passes options.AppSettingsPath; handled by Parse before calling this.)
        // Kept simple: search upward for the main project folder.
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8; i++)
        {
            dir = Path.GetDirectoryName(dir);
            if (string.IsNullOrEmpty(dir))
                break;
            var candidate = Path.Combine(dir, "appsettings.json");
            if (File.Exists(candidate))
                return candidate;
            // Stop searching once we leave the repo root heuristic: presence of .git or .sln.
            if (Directory.EnumerateFiles(dir, "*.sln").Any())
            {
                candidate = Path.Combine(dir, "appsettings.json");
                return File.Exists(candidate) ? candidate : null;
            }
        }
        return null;
    }

    /// <summary>Resolve the downlink topic for a specific IMEI.</summary>
    public string DownlinkTopicFor(string imei) =>
        DownlinkTopicTemplate.Replace("{imei}", imei, StringComparison.Ordinal);
}
