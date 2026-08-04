using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace IoTPlatform.Infrastructure.Protocol.AnSheng;

/// <summary>
/// 安圣上行报文 → <b>扁平数据点字典</b> 的归一化器（T6-2）。
///
/// 【为什么需要它 —— 问题的本质】
///   安圣设备把「每一路的电压/电流/功率/电量」放在 <c>EMdata</c> <b>数组</b>里，
///   把「每一路的开合状态」放在 <c>slots</c> <b>数组</b>里。
///   而平台的落库与规则引擎是<b>按字段名</b>工作的：
///   <c>DataCollectionService</c> 遍历 JSON 顶层属性做映射，<c>DataRule</c> 也按 key 取值。
///   数组永远命中不了任何映射，于是「第 1 路电压超过 250V 就告警」这类最基本的规则
///   在归一化之前<b>物理上无法配置</b>。
///   本类的核心工作就是把数组<b>展平</b>为 <c>slot1_voltage</c> / <c>slot1_state</c> 这样的一维键。
///
/// 【向后兼容是硬约束】
///   既有 <c>NormalizeDevStatus</c> 产出的 <c>total_power</c> / <c>total_energy</c> /
///   <c>total_current</c> / <c>avg_voltage</c> / <c>em_data</c> / <c>slots</c> / <c>tasks</c>
///   等旧键<b>必须原样保留</b>——线上已有 DataRule 依赖它们，删一个就是一次线上事故。
///   因此本类只做「加法」：旧键一个不少，再补上展平后的新键。
///   （设计文档 §10 落地检查清单第 7 条把这一点列为硬性自检项。）
///
/// 【生命周期】Scoped（注册见 <c>Program.cs</c>）。
///   本类<b>无可变状态</b>，Scoped 只是为了与同组的 Router / Dispatcher / Handler 保持一致，
///   便于将来需要注入 <c>AppDbContext</c> 时不必改注册。
///
/// 【与 <see cref="AnShengMessageParser"/> 的关系】
///   Normalizer 依赖 Parser（复用其报文体提取与宽松反序列化），Parser 在
///   <c>NormalizeForSensorData</c> 里回过头来使用 Normalizer。两者是<b>类型级</b>互相引用，
///   不构成 DI 循环：Parser 自建一份无日志的 Normalizer，Normalizer 由 DI 注入 Parser 单例。
/// </summary>
public sealed class AnShengDataNormalizer
{
    /// <summary>
    /// 事件报文里<b>已由信封统一输出</b>的原始键，透传阶段必须跳过，否则会写出重复语义的键。
    /// </summary>
    private static readonly HashSet<string> ReservedRawKeys = new(StringComparer.Ordinal)
    {
        "method", "imei", "result", "frameId", "timestamp"
    };

    /// <summary>
    /// 按键序号在不同固件上的字段名候选。
    ///
    /// 【为什么要容错】协议文档（asopen.md）的 <c>keyEvent</c> 应答只声明了
    /// <c>method</c> / <c>imei</c> / <c>timestamp</c> 三个字段，但多路按键设备实测会带序号，
    /// 且命名在各版本固件间不一致。这里按优先级取第一个命中的，取不到就不输出 <c>event_key</c>。
    /// </summary>
    private static readonly string[] KeyIndexFieldNames = { "key", "keyNum", "keyIndex", "keyNo" };

    /// <summary><c>recv485</c> 方法名（决策 2：走 DeviceDataRecord，不建 485 专用表）。</summary>
    private const string MethodRecv485 = "recv485";

    private readonly AnShengMessageParser _parser;
    private readonly ILogger<AnShengDataNormalizer>? _logger;

    /// <summary>
    /// 创建归一化器。
    /// </summary>
    /// <param name="parser">报文解析器（Singleton）。用于提取报文体与解析 <c>getDevStatus</c>。</param>
    /// <param name="logger">可选日志器。</param>
    public AnShengDataNormalizer(AnShengMessageParser parser, ILogger<AnShengDataNormalizer>? logger = null)
    {
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────
    // 公开入口
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 按 method 自动选择归一化策略。
    /// </summary>
    /// <param name="message">已解析的报文；为 null 时返回空字典。</param>
    /// <returns>扁平数据点字典（键为最终落入 <c>SensorData</c> 的 JSON key）。</returns>
    public IDictionary<string, object?> Normalize(AnShengMessage? message)
    {
        if (message == null)
        {
            return NewPoints();
        }

        if (string.Equals(message.Method, AnShengMessageRouter.MethodGetDevStatus, StringComparison.Ordinal))
        {
            var status = _parser.ParseDevStatus(message);
            return status != null
                ? NormalizeDevStatus(message, status)
                : NormalizeGeneric(message);
        }

        if (AnShengMessageRouter.AllEventMethods.Contains(message.Method))
        {
            return NormalizeEvent(message);
        }

        return NormalizeGeneric(message);
    }

    /// <summary>
    /// <c>getDevStatus</c> 全量展平。
    ///
    /// 输出契约见设计文档「附录 B：归一化字段字典」。要点：
    /// <list type="bullet">
    ///   <item>位路序号 <c>n</c> <b>从 1 开始</b>，与既有 <c>em_data[].slot = i + 1</c> 约定一致；</item>
    ///   <item>展平键只在设备<b>确实上报了该量</b>时输出，避免用 null 污染 SensorData；</item>
    ///   <item>旧键（total_* / avg_voltage / em_data / slots / tasks）无条件保留。</item>
    /// </list>
    /// </summary>
    /// <param name="message">已解析的报文。</param>
    /// <param name="status">已解析的状态体。</param>
    /// <returns>扁平数据点字典。</returns>
    public IDictionary<string, object?> NormalizeDevStatus(AnShengMessage message, AnShengDevStatus status)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(status);

        var emData = status.EmData;

        // 汇总量：与既有实现逐字一致（emData 为空时 total_* 为 0、avg_voltage 为 null），
        // 任何微小差异都可能让线上依赖这些键的 DataRule 行为漂移。
        var totalPower = emData?.Sum(e => e.P ?? 0) ?? 0;
        var totalEnergy = emData?.Sum(e => e.E ?? 0) ?? 0;
        var totalCurrent = emData?.Sum(e => e.C ?? 0) ?? 0;
        double? avgVoltage = emData is { Count: > 0 } ? emData.Average(e => e.V ?? 0) : null;

        // slot_count：优先用数组长度（推算值，既有语义），数组缺失时回退设备显式声明的 slotAmount。
        // 附录 B 把来源标为 slotAmount，既有实现用数组长度；两者取「谁有值用谁」是唯一
        // 既不破坏既有断言、又满足附录 B 意图的取法。
        var slotCount = status.SlotCount > 0 ? status.SlotCount : status.SlotAmount ?? 0;

        var points = NewPoints();

        // ── 旧键区（一个都不能少）──
        points["method"] = message.Method;
        points["imei"] = message.Imei;
        points["result"] = message.Result;
        points["net_type"] = status.NetType;
        points["iccid"] = status.Iccid;
        points["signal"] = status.Signal;
        points["temperature"] = status.Temperature;
        points["gps"] = status.Gps;
        points["slot_count"] = slotCount;
        points["slots"] = status.Slots;
        points["total_power"] = totalPower;
        points["total_energy"] = totalEnergy;
        points["total_current"] = totalCurrent;
        points["avg_voltage"] = avgVoltage;
        points["em_data"] = emData?.Select((e, i) => new
        {
            slot = i + 1,
            v = e.V,
            c = e.C,
            p = e.P,
            e_kwh = e.E,
            pf = e.Pf
        }).ToList();
        points["tasks"] = status.Tasks?.Select(t => new
        {
            slot = t.SlotNum,
            type = t.Type,
            status = t.Status,
            time_sec = t.TimeSec,
            power_kwh = t.PowerKwh,
            total_sec = t.TotalSec,
            total_kwh = t.TotalKwh,
            voltage = t.Voltage,
            current = t.Current,
            power = t.Power,
            close_reason = t.CloseReason,
            remark = t.Remark
        }).ToList();
        points["raw_timestamp"] = message.RawTimestamp;
        points["timestamp_utc"] = message.TimestampUtc;

        // ── 新增键区（T6-2 的全部价值所在）──
        if (status.SlotAmount.HasValue)
        {
            points["slot_amount"] = status.SlotAmount.Value;
        }

        AddSlotStates(points, status.Slots);
        AddSlotMeterFields(points, emData);

        return points;
    }

    /// <summary>
    /// 事件报文归一化（<c>connected</c> / <c>keyEvent</c> / <c>delayEvent</c> /
    /// <c>timeEvent</c> / <c>recv485</c> / <c>simCheck</c> / <c>close</c>）。
    ///
    /// 输出 = 公共信封 + 方法专属键 + 未识别顶层字段的<b>原样透传</b>。
    /// 透传是刻意的：固件迭代会加字段，硬编码白名单会让新字段静默丢失，
    /// 而 <c>SensorData</c> 是 longtext，多存几个键的成本可以忽略。
    /// </summary>
    /// <param name="message">已解析的报文。</param>
    /// <returns>扁平数据点字典。</returns>
    public IDictionary<string, object?> NormalizeEvent(AnShengMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var points = BuildEnvelope(message);
        points["event"] = message.Method;

        var consumed = new HashSet<string>(ReservedRawKeys, StringComparer.Ordinal);
        var body = AnShengMessageParser.GetBodyJson(message);
        if (string.IsNullOrWhiteSpace(body))
        {
            return points;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return points;
            }

            // 位路号：delayEvent / timeEvent 必带，keyEvent 多路机型会带。
            if (TryGetInt(root, "slotNum", out var slotNum))
            {
                points["slot_num"] = slotNum;
                consumed.Add("slotNum");
            }

            // 按键序号（固件命名不统一，取第一个命中的）。
            foreach (var candidate in KeyIndexFieldNames)
            {
                if (TryGetInt(root, candidate, out var keyIndex))
                {
                    points["event_key"] = keyIndex;
                    consumed.Add(candidate);
                    break;
                }
            }

            // 定时任务索引（timeEvent）。
            if (TryGetInt(root, "taskIndex", out var taskIndex))
            {
                points["task_index"] = taskIndex;
                consumed.Add("taskIndex");
            }

            // 插槽状态快照：既保留数组原样（旧消费方），又展平为 slot{n}_state（规则引擎可用）。
            if (root.TryGetProperty("slots", out var slotsElement) &&
                slotsElement.ValueKind == JsonValueKind.Array)
            {
                var slots = new List<int>(slotsElement.GetArrayLength());
                foreach (var item in slotsElement.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var state))
                    {
                        slots.Add(state);
                    }
                }

                points["slots"] = slots;
                AddSlotStates(points, slots);
                consumed.Add("slots");
            }

            // RS485 透传帧（决策 2：不建专用表，无损落进 SensorData）。
            if (string.Equals(message.Method, MethodRecv485, StringComparison.Ordinal))
            {
                AddRs485Fields(points, root, message, consumed);
            }

            // 其余顶层字段原样透传（保留原始键名，不做 snake_case 改写，便于与报文对照排障）。
            foreach (var property in root.EnumerateObject())
            {
                if (consumed.Contains(property.Name) || points.ContainsKey(property.Name))
                {
                    continue;
                }

                points[property.Name] = ToClrValue(property.Value);
            }
        }
        catch (JsonException ex)
        {
            _logger?.LogWarning(ex,
                "[AnShengNormalizer] 事件报文体解析失败，仅输出信封 imei={Imei} method={Method}",
                message.Imei, message.Method);
        }

        return points;
    }

    /// <summary>
    /// 归一化并序列化为 JSON 字符串（供 <c>SensorData</c> / <c>PayloadJson</c> 落库）。
    /// </summary>
    /// <param name="message">已解析的报文。</param>
    /// <returns>JSON 字符串；失败返回 <c>"{}"</c>，绝不抛出。</returns>
    public string NormalizeToJson(AnShengMessage? message)
    {
        try
        {
            return ToJson(Normalize(message));
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[AnShengNormalizer] 归一化序列化失败 imei={Imei} method={Method}",
                message?.Imei, message?.Method);
            return "{}";
        }
    }

    /// <summary>
    /// 把数据点字典序列化为 JSON。
    /// </summary>
    /// <param name="points">数据点字典；为 null 返回 <c>"{}"</c>。</param>
    /// <returns>JSON 字符串。</returns>
    public static string ToJson(IDictionary<string, object?>? points)
        => points == null ? "{}" : JsonSerializer.Serialize(points);

    // ─────────────────────────────────────────────────────────────
    // 内部实现
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 创建数据点字典。
    /// 用 <see cref="StringComparer.Ordinal"/> 而非 OrdinalIgnoreCase：
    /// 键最终会成为 JSON 属性名，JSON 是<b>大小写敏感</b>的，忽略大小写会让透传阶段
    /// 把 <c>Data</c> 和 <c>data</c> 误判为同一个键从而丢字段。
    /// </summary>
    /// <returns>空字典。</returns>
    private static Dictionary<string, object?> NewPoints() => new(StringComparer.Ordinal);

    /// <summary>
    /// 构造所有报文共有的信封字段。
    /// </summary>
    /// <param name="message">已解析的报文。</param>
    /// <returns>含信封字段的字典。</returns>
    private static Dictionary<string, object?> BuildEnvelope(AnShengMessage message)
    {
        var points = NewPoints();
        points["method"] = message.Method;
        points["imei"] = message.Imei;

        if (!string.IsNullOrEmpty(message.Result))
        {
            points["result"] = message.Result;
        }

        if (!string.IsNullOrEmpty(message.FrameId))
        {
            points["frame_id"] = message.FrameId;
        }

        points["raw_timestamp"] = message.RawTimestamp;
        points["timestamp_utc"] = message.TimestampUtc;
        return points;
    }

    /// <summary>
    /// 非 <c>getDevStatus</c> 且非事件的报文（普通命令应答）：信封 + 顶层字段透传。
    /// </summary>
    /// <param name="message">已解析的报文。</param>
    /// <returns>扁平数据点字典。</returns>
    private IDictionary<string, object?> NormalizeGeneric(AnShengMessage message)
    {
        var points = BuildEnvelope(message);

        var body = AnShengMessageParser.GetBodyJson(message);
        if (string.IsNullOrWhiteSpace(body))
        {
            return points;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return points;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (ReservedRawKeys.Contains(property.Name) || points.ContainsKey(property.Name))
                {
                    continue;
                }

                points[property.Name] = ToClrValue(property.Value);
            }
        }
        catch (JsonException ex)
        {
            _logger?.LogWarning(ex,
                "[AnShengNormalizer] 报文体解析失败，仅输出信封 imei={Imei} method={Method}",
                message.Imei, message.Method);
        }

        return points;
    }

    /// <summary>
    /// 展平插槽开合状态为 <c>slot{n}_state</c>（n 从 1 开始）。
    /// </summary>
    /// <param name="points">目标字典。</param>
    /// <param name="slots">插槽状态数组，可为 null。</param>
    private static void AddSlotStates(IDictionary<string, object?> points, IReadOnlyList<int>? slots)
    {
        if (slots == null)
        {
            return;
        }

        for (var i = 0; i < slots.Count; i++)
        {
            points[$"slot{i + 1}_state"] = slots[i];
        }
    }

    /// <summary>
    /// 展平电量计数组为 <c>slot{n}_voltage</c> / <c>_current</c> / <c>_power</c> / <c>_energy</c>。
    /// 只输出设备确实上报的量：某一项为 null 时不写该键，避免规则引擎把「没上报」误当成 0。
    /// </summary>
    /// <param name="points">目标字典。</param>
    /// <param name="emData">电量计数组，可为 null。</param>
    private static void AddSlotMeterFields(IDictionary<string, object?> points, IReadOnlyList<AnShengEmData>? emData)
    {
        if (emData == null)
        {
            return;
        }

        for (var i = 0; i < emData.Count; i++)
        {
            var slot = i + 1;
            var item = emData[i];
            if (item == null)
            {
                continue;
            }

            if (item.V.HasValue) points[$"slot{slot}_voltage"] = item.V.Value;
            if (item.C.HasValue) points[$"slot{slot}_current"] = item.C.Value;
            if (item.P.HasValue) points[$"slot{slot}_power"] = item.P.Value;
            if (item.E.HasValue) points[$"slot{slot}_energy"] = item.E.Value;
            if (item.Pf.HasValue) points[$"slot{slot}_pf"] = item.Pf.Value;
        }
    }

    /// <summary>
    /// 提取 <c>recv485</c> 的透传帧字段（决策 2 定义的 4 个键）。
    /// </summary>
    /// <param name="points">目标字典。</param>
    /// <param name="root">报文体根元素。</param>
    /// <param name="message">已解析的报文。</param>
    /// <param name="consumed">已消费的原始键集合，会被就地追加。</param>
    private static void AddRs485Fields(
        IDictionary<string, object?> points,
        JsonElement root,
        AnShengMessage message,
        ISet<string> consumed)
    {
        if (root.TryGetProperty("data", out var dataElement) &&
            dataElement.ValueKind == JsonValueKind.String)
        {
            var hex = dataElement.GetString() ?? string.Empty;
            points["rs485_hex"] = hex;
            // 十六进制字符串每 2 字符 1 字节；奇数长度说明帧被截断，向下取整并保留原文供排障。
            points["rs485_len"] = hex.Length / 2;
            consumed.Add("data");
        }

        // 协议里叫 num（"对应多个命令的编号，从 1 开始"），语义即 485 通道/命令序号。
        if (TryGetInt(root, "num", out var portNum))
        {
            points["rs485_port"] = portNum;
            consumed.Add("num");
        }

        if (!string.IsNullOrEmpty(message.FrameId))
        {
            points["rs485_frame_id"] = message.FrameId;
        }
    }

    /// <summary>
    /// 宽松读取整数字段：同时接受 JSON 数字与数字字符串（部分固件把 int 写成 string）。
    /// </summary>
    /// <param name="root">报文体根元素。</param>
    /// <param name="propertyName">属性名。</param>
    /// <param name="value">读取到的整数。</param>
    /// <returns>命中且可转为整数返回 true。</returns>
    private static bool TryGetInt(JsonElement root, string propertyName, out int value)
    {
        value = 0;
        if (!root.TryGetProperty(propertyName, out var element))
        {
            return false;
        }

        return element.ValueKind switch
        {
            JsonValueKind.Number => element.TryGetInt32(out value),
            JsonValueKind.String => int.TryParse(element.GetString(), out value),
            _ => false
        };
    }

    /// <summary>
    /// <see cref="JsonElement"/> → CLR 值。
    ///
    /// ★ 对象与数组必须 <c>Clone()</c>：<see cref="JsonDocument"/> 在方法返回前就被 dispose，
    /// 未克隆的 <see cref="JsonElement"/> 之后再访问会抛
    /// <see cref="ObjectDisposedException"/>，且是在序列化时才炸，极难排查。
    /// </summary>
    /// <param name="element">JSON 元素。</param>
    /// <returns>对应的 CLR 值。</returns>
    private static object? ToClrValue(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var integer)
                ? integer
                : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => element.Clone()
        };
}
