using System.Text.Json;
using IoTPlatform.Infrastructure.Protocol.AnSheng;

namespace IoTPlatform.Tools.AnShengFieldTest;

/// <summary>
/// Local, device-free self-checks that validate the platform-side packet structure
/// produced by the production builder/parser/catalog. Runs before contacting the broker.
/// Every check only touches production classes (no hand-written JSON).
/// </summary>
public static class PreflightRunner
{
    private const string Imei = "123456789012345"; // 15-digit synthetic IMEI for preflight only.
    private const long FixedTimestamp = 1700000000L; // 2023-11-14T22:13:20Z

    public static IReadOnlyList<PreflightCheck> Run(
        AnShengCommandBuilder builder,
        AnShengMessageParser parser,
        string imei,
        int slotNum)
    {
        var checks = new List<PreflightCheck>();
        var kind = AnShengDeviceKind.Switch4G;

        checks.Add(Safe("Catalog 命令总数 = 36", () =>
        {
            int count = AnShengCommandCatalog.Count;
            return (count == 36, $"AnShengCommandCatalog.Count = {count}");
        }));

        checks.Add(Safe("Catalog 事件方法 = 6", () =>
        {
            int ev = AnShengCommandCatalog.EventMethods.Count;
            return (ev == 6, $"EventMethods.Count = {ev}");
        }));

        checks.Add(Safe("setTime 业务 timestamp 不被系统时钟覆盖", () =>
        {
            var fixedUtc = DateTimeOffset.FromUnixTimeSeconds(FixedTimestamp).UtcDateTime;
            var (_, payload) = builder.BuildSetTime(imei, fixedUtc, kind);
            bool has = payload.Contains($"\"timestamp\":{FixedTimestamp}", StringComparison.Ordinal)
                       || payload.Contains($"\"timestamp\": {FixedTimestamp}", StringComparison.Ordinal);
            return (has, has
                ? $"下行报文保留业务 timestamp={FixedTimestamp}"
                : $"未在上行载荷中找到 timestamp={FixedTimestamp}；实际: {Truncate(payload, 200)}");
        }));

        checks.Add(Safe("参数平铺顶层（无 param 包裹）", () =>
        {
            var samples = new[]
            {
                builder.BuildGetDevInfo(imei, kind).Payload,
                builder.BuildAction(imei, slotNum, "on", null, kind).Payload,
                builder.BuildStartDelayTask(imei, slotNum, true, "on", "off", 10, kind).Payload
            };
            bool anyParam = samples.Any(p => RawHasTopLevelKey(p, "param"));
            return (!anyParam, anyParam ? "发现顶层 param 包裹（违反平铺约定）" : "三份样本均无顶层 param 包裹");
        }));

        checks.Add(Safe("frameId 16 位且唯一（200 次）", () =>
        {
            var set = new HashSet<string>();
            bool lenOk = true;
            for (int i = 0; i < 200; i++)
            {
                string id = AnShengCommandBuilder.NewFrameId();
                if (id.Length != AnShengCommandBuilder.FrameIdLength) lenOk = false;
                set.Add(id);
            }
            bool unique = set.Count == 200;
            return (lenOk && unique, $"长度合规={lenOk}, 200 次唯一={unique} (FrameIdLength={AnShengCommandBuilder.FrameIdLength})");
        }));

        checks.Add(Safe("4G 注入秒级 int timestamp", () =>
        {
            var (_, payload) = builder.BuildSetTime(imei, DateTimeOffset.FromUnixTimeSeconds(FixedTimestamp).UtcDateTime, kind);
            bool has = RawHasTopLevelKey(payload, "timestamp");
            bool isInt = false;
            if (has)
            {
                using var d = JsonDocument.Parse(payload);
                if (d.RootElement.TryGetProperty("timestamp", out var t) && t.ValueKind == JsonValueKind.Number)
                    isInt = true;
            }
            return (has && isInt, has ? $"timestamp 为数值: {isInt}" : "setTime 报文缺少 timestamp 字段");
        }));

        checks.Add(Safe("非 4G（WiFi）省略 timestamp", () =>
        {
            // setTime 在 Catalog 中为 Group4GOnly，WiFi 品类根本不支持，调用会抛 NotSupportedException
            // —— 这恰恰说明 Catalog 校验在正确工作。改用 WiFi 真正支持的 getDevInfo 验证 timestamp 省略逻辑。
            var wifi = Enum.GetValues<AnShengDeviceKind>().FirstOrDefault(k => k.IsWiFi());
            if (wifi == AnShengDeviceKind.Unknown)
                return (true, "当前无可用 WiFi 品类，跳过该验证（不影响结论）");
            var (_, payload) = builder.BuildGetDevInfo(imei, wifi);
            bool has = RawHasTopLevelKey(payload, "timestamp");
            return (!has, has ? $"{wifi.ToDisplayName()} 仍注入 timestamp（异常）" : $"{wifi.ToDisplayName()} 正确省略 timestamp");
        }));

        checks.Add(Safe("报文为压缩 JSON（无多余空白）", () =>
        {
            var (_, payload) = builder.BuildGetDevInfo(imei, kind);
            bool compact = !payload.Contains("\" :") && !payload.Contains(", ") && !payload.Contains("{ ");
            return (compact, compact ? "载荷已压缩" : $"载荷含多余空白: {Truncate(payload, 120)}");
        }));

        checks.Add(Safe("离线判定仅 method==\"close\"", () =>
        {
            bool willIsClose = AnShengCommandCatalog.WillMethod == "close";
            var willMsg = parser.Parse("{\"imei\":\"x\",\"method\":\"close\"}");
            var normalMsg = parser.Parse("{\"imei\":\"x\",\"method\":\"getDevStatus\",\"frameId\":\"0000000000000001\",\"result\":\"ok\"}");
            bool detectsWill = AnShengMessageParser.IsWillMessage(willMsg);
            bool ignoresNormal = !AnShengMessageParser.IsWillMessage(normalMsg);
            bool ok = willIsClose && detectsWill && ignoresNormal;
            return (ok, $"WillMethod=close:{willIsClose}, 识别 will:{detectsWill}, 正常消息非 will:{ignoresNormal}");
        }));

        checks.Add(Safe("剧本方法均入 Catalog 且 4G 开关支持", () =>
        {
            var steps = CommandScript.Build(slotNum, 10, 70);
            var commandSteps = steps.Where(s => s.DwellSeconds == 0).ToList();
            var missing = new List<string>();
            var unsupported = new List<string>();
            foreach (var s in commandSteps)
            {
                if (!AnShengCommandCatalog.Contains(s.Method))
                    missing.Add(s.Method);
                else if (!AnShengCommandCatalog.IsSupported(s.Method, AnShengDeviceKind.Switch4G))
                    unsupported.Add(s.Method);
            }
            bool ok = missing.Count == 0 && unsupported.Count == 0;
            return (ok,
                ok ? $"全部 {commandSteps.Count} 个剧本命令步骤均在 Catalog 中且 4G 开关支持"
                     + $"（另有 {steps.Count - commandSteps.Count} 个被动驻留步骤，不下发命令）"
                   : $"缺失: {string.Join(",", missing)}; 不支持: {string.Join(",", unsupported)}");
        }));

        // P0 回归护栏：第一轮剧本把开关永久留在了闭合状态。以后任何人改剧本，
        // 只要没有把「action off + getDevStatus 读回断言」放在最后，这条自检就会 FAIL。
        checks.Add(Safe("剧本以「强制断开 + 读回断言」收尾（P0 回归护栏）", () =>
        {
            var steps = CommandScript.Build(slotNum, 10, 70);
            var controlSteps = steps.Where(s => s.Risk == StepRisk.Control).ToList();
            if (controlSteps.Count == 0)
                return (true, "剧本不含 control 步骤，无需收尾断开");

            // 找到最后一个 control 步骤，它必须是 action(off)。
            var lastControl = controlSteps[^1];
            var lastControlIdx = steps.ToList().FindLastIndex(s => s.Risk == StepRisk.Control);
            bool endsWithOff = lastControl.Method == "action"
                               && lastControl.Purpose.Contains("确保开关断开", StringComparison.Ordinal);

            // 该 action(off) 之后必须还有一个带断言的 getDevStatus。
            bool hasReadBackAssert = steps
                .Skip(lastControlIdx + 1)
                .Any(s => s.Method == "getDevStatus" && s.Assert is not null);

            bool ok = endsWithOff && hasReadBackAssert;
            return (ok, ok
                ? $"剧本共 {controlSteps.Count} 个 control 步骤，最后一个是收尾 action off，其后有带断言的 getDevStatus 读回"
                : $"收尾缺失！最后一个 control = {lastControl.Method}（收尾 off={endsWithOff}），其后有读回断言={hasReadBackAssert}");
        }));

        return checks;
    }

    private static PreflightCheck Safe(string name, Func<(bool Passed, string Detail)> body)
    {
        try
        {
            var (passed, detail) = body();
            return new PreflightCheck { Name = name, Passed = passed, Detail = detail };
        }
        catch (Exception ex)
        {
            return new PreflightCheck { Name = name, Passed = false, Detail = $"自检抛出异常（API 假设可能不符）: {ex.GetType().Name}: {ex.Message}" };
        }
    }

    private static bool RawHasTopLevelKey(string raw, string key)
    {
        try
        {
            using var d = JsonDocument.Parse(raw);
            return d.RootElement.ValueKind == JsonValueKind.Object && d.RootElement.TryGetProperty(key, out _);
        }
        catch
        {
            return false;
        }
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "...";
}
