using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace IoTPlatform.Infrastructure.Protocol.AnSheng;

/// <summary>
/// 安圣 MQTT 命令构建器（二开协议）。
///
/// 生成的报文严格遵循 <c>asopen.md</c>：
///   1. 参数<b>平铺</b>在 JSON 顶层，<b>没有</b> <c>param</c> 包裹对象；
///   2. <c>frameId</c> 为 16 位唯一字符串；
///   3. <c>timestamp</c> 为<b>秒级 int</b>，<b>仅 4G 款注入</b>，WiFi 款完全省略该字段；
///   4. 输出为<b>压缩</b>（无缩进、无多余空白）JSON，以节省流量；
///   5. 字段顺序固定为 method → imei → 业务参数 → frameId → timestamp，便于人工比对。
///
/// 典型输出：
/// <code>
/// {"method":"action","imei":"864536072949900","slotNum":1,"action":"on","frameId":"a1b2c3d4e5f60718","timestamp":1745396759}
/// </code>
/// </summary>
public class AnShengCommandBuilder
{
    /// <summary>frameId 固定长度（字符数）。</summary>
    public const int FrameIdLength = 16;

    private const string FrameIdAlphabet = "0123456789abcdef";

    /// <summary>压缩 JSON 序列化选项：不缩进、不转义中文、忽略 null。</summary>
    private static readonly JsonSerializerOptions MinifiedJson = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    /// <summary>frameId 序号，保证同一进程内单调递增，配合随机数确保唯一。</summary>
    private static long _frameSequence;

    private readonly ILogger<AnShengCommandBuilder>? _logger;

    /// <summary>
    /// 创建命令构建器。
    /// </summary>
    /// <param name="logger">可选日志器。</param>
    public AnShengCommandBuilder(ILogger<AnShengCommandBuilder>? logger = null)
    {
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────
    // frameId
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 生成 16 位唯一 frameId。
    /// 组成：8 位进程内递增序号（十六进制） + 8 位加密随机数（十六进制）。
    /// 与「毫秒时间戳」不同，可保证同一毫秒内的多条命令也不重复。
    /// </summary>
    /// <returns>长度恒为 16 的小写十六进制字符串。</returns>
    public static string NewFrameId()
    {
        var seq = (ulong)Interlocked.Increment(ref _frameSequence);

        Span<byte> randomBytes = stackalloc byte[4];
        RandomNumberGenerator.Fill(randomBytes);
        var random = BinaryPrimitives.ReadUInt32BigEndian(randomBytes);

        Span<char> buffer = stackalloc char[FrameIdLength];
        WriteHex(buffer[..8], (uint)(seq & 0xFFFFFFFF));
        WriteHex(buffer.Slice(8, 8), random);
        return new string(buffer);
    }

    private static void WriteHex(Span<char> target, uint value)
    {
        for (var i = target.Length - 1; i >= 0; i--)
        {
            target[i] = FrameIdAlphabet[(int)(value & 0xF)];
            value >>= 4;
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 通用构建
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 构建任意二开协议命令。
    /// </summary>
    /// <param name="imei">设备 IMEI。</param>
    /// <param name="method">协议方法名，必须存在于 <see cref="AnShengCommandCatalog"/> 中。</param>
    /// <param name="parameters">平铺参数字典，可为 null。值为 null 的项会被剔除。</param>
    /// <param name="kind">设备品类，决定是否注入 timestamp（仅 4G 注入）。</param>
    /// <param name="frameId">指定 frameId；为 null 时自动生成 16 位唯一值。</param>
    /// <returns>(FrameId, 压缩后的 JSON 报文)。</returns>
    /// <exception cref="ArgumentException"><paramref name="imei"/> 为空，或 <paramref name="method"/> 不在协议目录中。</exception>
    /// <exception cref="NotSupportedException">该品类不支持此命令，或该 method 是设备上行事件。</exception>
    public (string FrameId, string Payload) BuildCommand(
        string imei,
        string method,
        IReadOnlyDictionary<string, object?>? parameters = null,
        AnShengDeviceKind kind = AnShengDeviceKind.Unknown,
        string? frameId = null)
    {
        if (string.IsNullOrWhiteSpace(imei))
        {
            throw new ArgumentException("IMEI 不能为空", nameof(imei));
        }

        if (!AnShengCommandCatalog.TryGet(method, out var spec) || spec == null)
        {
            throw new ArgumentException(
                $"方法 {method} 不在安圣二开协议命令目录中（共 {AnShengCommandCatalog.Count} 条）", nameof(method));
        }

        if (spec.IsEvent)
        {
            throw new NotSupportedException($"{method} 是设备上报事件，平台不可下发");
        }

        if (!spec.IsSupportedBy(kind))
        {
            throw new NotSupportedException($"{kind.ToDisplayName()} 不支持命令 {method}");
        }

        return BuildRaw(imei, method, parameters, kind, frameId);
    }

    /// <summary>
    /// 不做目录校验地构建报文（供适配器在品类未知/协议扩展场景使用）。
    /// 报文结构仍严格遵循二开协议：平铺参数、16 位 frameId、4G 才注入秒级 timestamp、压缩 JSON。
    /// </summary>
    /// <param name="imei">设备 IMEI。</param>
    /// <param name="method">协议方法名。</param>
    /// <param name="parameters">平铺参数字典，可为 null。</param>
    /// <param name="kind">设备品类。</param>
    /// <param name="frameId">指定 frameId；为 null 时自动生成。</param>
    /// <returns>(FrameId, 压缩后的 JSON 报文)。</returns>
    public (string FrameId, string Payload) BuildRaw(
        string imei,
        string method,
        IReadOnlyDictionary<string, object?>? parameters = null,
        AnShengDeviceKind kind = AnShengDeviceKind.Unknown,
        string? frameId = null)
    {
        var actualFrameId = string.IsNullOrWhiteSpace(frameId) ? NewFrameId() : frameId;

        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["method"] = method,
            ["imei"] = imei
        };

        if (parameters != null)
        {
            foreach (var (key, value) in parameters)
            {
                if (value == null) continue;                       // 剔除空值，避免下发 null
                if (string.IsNullOrWhiteSpace(key)) continue;
                if (IsReservedKey(key)) continue;                  // 公共字段由构建器统一控制
                payload[key] = value;
            }
        }

        payload["frameId"] = actualFrameId;

        // 协议：timestamp 为秒级 int，WiFi 款不支持 → 仅 4G 款注入。
        // 注意：setTime 的 timestamp 是「业务参数」（要下发给设备的目标时间），
        //       此时不得用当前时钟覆盖，否则对时命令永远只能对到「当前时间」。
        if (kind.SupportsTimestamp() && !payload.ContainsKey("timestamp"))
        {
            payload["timestamp"] = AnShengTimestampConverter.NowUnixSeconds();
        }

        var json = JsonSerializer.Serialize(payload, MinifiedJson);

        _logger?.LogDebug("构建安圣命令: Method={Method}, IMEI={IMEI}, Kind={Kind}, FrameId={FrameId}",
            method, imei, kind, actualFrameId);

        return (actualFrameId, json);
    }

    /// <summary>
    /// 判断参数名是否为协议公共字段（由构建器统一注入，不允许调用方覆盖）。
    /// </summary>
    /// <param name="key">参数名。</param>
    /// <returns>是公共字段返回 true。</returns>
    private static bool IsReservedKey(string key)
        => string.Equals(key, "method", StringComparison.Ordinal)
           || string.Equals(key, "imei", StringComparison.Ordinal)
           || string.Equals(key, "frameId", StringComparison.Ordinal)
           || string.Equals(key, "result", StringComparison.Ordinal);

    // ─────────────────────────────────────────────────────────────
    // G1 通用命令
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 构建 <c>getDevInfo</c> — 获取设备基本信息。
    /// </summary>
    /// <param name="imei">设备 IMEI。</param>
    /// <param name="kind">设备品类。</param>
    /// <returns>(FrameId, JSON 报文)。</returns>
    public (string FrameId, string Payload) BuildGetDevInfo(
        string imei, AnShengDeviceKind kind = AnShengDeviceKind.Unknown)
        => BuildCommand(imei, "getDevInfo", null, kind);

    /// <summary>
    /// 构建 <c>getDevStatus</c> — 获取设备实时状态。
    /// </summary>
    /// <param name="imei">设备 IMEI。</param>
    /// <param name="query">查询字符串（<c>slots,EMdata,tasks</c> 的组合），空表示返回全部。</param>
    /// <param name="kind">设备品类。</param>
    /// <returns>(FrameId, JSON 报文)。</returns>
    public (string FrameId, string Payload) BuildGetDevStatus(
        string imei, string? query = null, AnShengDeviceKind kind = AnShengDeviceKind.Unknown)
    {
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(query))
        {
            parameters["q"] = query;
        }

        return BuildCommand(imei, "getDevStatus", parameters, kind);
    }

    /// <summary>
    /// 构建 <c>reboot</c> — 远程重启设备。
    /// </summary>
    /// <param name="imei">设备 IMEI。</param>
    /// <param name="kind">设备品类。</param>
    /// <returns>(FrameId, JSON 报文)。</returns>
    public (string FrameId, string Payload) BuildReboot(
        string imei, AnShengDeviceKind kind = AnShengDeviceKind.Unknown)
        => BuildCommand(imei, "reboot", null, kind);

    /// <summary>
    /// 构建 <c>getKeyConfig</c> — 获取按键配置。
    /// </summary>
    /// <param name="imei">设备 IMEI。</param>
    /// <param name="kind">设备品类。</param>
    /// <returns>(FrameId, JSON 报文)。</returns>
    public (string FrameId, string Payload) BuildGetKeyConfig(
        string imei, AnShengDeviceKind kind = AnShengDeviceKind.Unknown)
        => BuildCommand(imei, "getKeyConfig", null, kind);

    /// <summary>
    /// 构建 <c>setKeyConfig</c> — 设置按键配置。
    /// </summary>
    /// <param name="imei">设备 IMEI。</param>
    /// <param name="mode">按键模式：0-无动作；1-切换开关；2-离线切换开关。</param>
    /// <param name="uploadEnable">是否上报按键事件。</param>
    /// <param name="kind">设备品类。</param>
    /// <returns>(FrameId, JSON 报文)。</returns>
    public (string FrameId, string Payload) BuildSetKeyConfig(
        string imei, int mode, bool uploadEnable, AnShengDeviceKind kind = AnShengDeviceKind.Unknown)
        => BuildCommand(imei, "setKeyConfig", new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["mode"] = mode,
            ["uploadEnable"] = uploadEnable
        }, kind);

    /// <summary>
    /// 构建 <c>getAutoReport</c> — 获取自动上报配置（协议标注「测试中」）。
    /// </summary>
    /// <param name="imei">设备 IMEI。</param>
    /// <param name="kind">设备品类。</param>
    /// <returns>(FrameId, JSON 报文)。</returns>
    public (string FrameId, string Payload) BuildGetAutoReport(
        string imei, AnShengDeviceKind kind = AnShengDeviceKind.Unknown)
        => BuildCommand(imei, "getAutoReport", null, kind);

    /// <summary>
    /// 构建 <c>setAutoReport</c> — 配置设备自动上报间隔（协议标注「测试中」）。
    /// </summary>
    /// <param name="imei">设备 IMEI。</param>
    /// <param name="getDevStatusSec">状态自动上报间隔秒数，0-不上报，非 0 不得小于 30。</param>
    /// <param name="orderUpSec">订单数据自动上报间隔秒数，0-不上报，非 0 不得小于 30。</param>
    /// <param name="rs485Sec">RS485 自动上报间隔秒数，0-不上报，非 0 不得小于 30。</param>
    /// <param name="rs485BaudRate">RS485 波特率，默认 115200。</param>
    /// <param name="getDevStatusQ">状态上报查询字符串，可为 null。</param>
    /// <param name="rs485SendWaitMs">RS485 多命令间隔毫秒数，可为 null（设备默认 300）。</param>
    /// <param name="rs485Array">RS485 下发命令十六进制字符串数组，可为 null。</param>
    /// <param name="kind">设备品类。</param>
    /// <returns>(FrameId, JSON 报文)。</returns>
    public (string FrameId, string Payload) BuildSetAutoReport(
        string imei,
        int getDevStatusSec = 60,
        int orderUpSec = 300,
        int rs485Sec = 0,
        int rs485BaudRate = 115200,
        string? getDevStatusQ = null,
        int? rs485SendWaitMs = null,
        IReadOnlyList<string>? rs485Array = null,
        AnShengDeviceKind kind = AnShengDeviceKind.Unknown)
    {
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["getDevStatusSec"] = getDevStatusSec,
            ["orderUpSec"] = orderUpSec,
            ["rs485Sec"] = rs485Sec,
            ["rs485BaudRate"] = rs485BaudRate
        };

        if (!string.IsNullOrWhiteSpace(getDevStatusQ)) parameters["getDevStatusQ"] = getDevStatusQ;
        if (rs485SendWaitMs.HasValue) parameters["rs485SendWaitMs"] = rs485SendWaitMs.Value;
        if (rs485Array is { Count: > 0 }) parameters["rs485Array"] = rs485Array;

        return BuildCommand(imei, "setAutoReport", parameters, kind);
    }

    // ─────────────────────────────────────────────────────────────
    // G3 开关动作 / 延时任务
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 构建 <c>action</c> — 单插槽开关动作。
    /// </summary>
    /// <param name="imei">设备 IMEI。</param>
    /// <param name="slotNum">插槽编号，从 1 开始；<c>0</c> 表示所有插槽。</param>
    /// <param name="action">动作：<c>on</c> / <c>off</c> / <c>toggle</c>。</param>
    /// <param name="hasStopDelayTask">是否同时停止延时任务，可为 null（不下发该字段）。</param>
    /// <param name="kind">设备品类。</param>
    /// <returns>(FrameId, JSON 报文)。</returns>
    public (string FrameId, string Payload) BuildAction(
        string imei, int slotNum, string action,
        bool? hasStopDelayTask = null, AnShengDeviceKind kind = AnShengDeviceKind.Unknown)
    {
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["slotNum"] = slotNum,
            ["action"] = action
        };
        if (hasStopDelayTask.HasValue) parameters["hasStopDelayTask"] = hasStopDelayTask.Value;

        return BuildCommand(imei, "action", parameters, kind);
    }

    /// <summary>
    /// 构建 <c>actions</c> — 多插槽开关动作。
    /// </summary>
    /// <param name="imei">设备 IMEI。</param>
    /// <param name="slotNums">插槽编号数组，子项从 1 开始。</param>
    /// <param name="action">动作：<c>on</c> / <c>off</c> / <c>toggle</c>。</param>
    /// <param name="hasStopDelayTask">是否同时停止延时任务，可为 null。</param>
    /// <param name="kind">设备品类。</param>
    /// <returns>(FrameId, JSON 报文)。</returns>
    public (string FrameId, string Payload) BuildActions(
        string imei, IReadOnlyList<int> slotNums, string action,
        bool? hasStopDelayTask = null, AnShengDeviceKind kind = AnShengDeviceKind.Unknown)
    {
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["slotNums"] = slotNums,
            ["action"] = action
        };
        if (hasStopDelayTask.HasValue) parameters["hasStopDelayTask"] = hasStopDelayTask.Value;

        return BuildCommand(imei, "actions", parameters, kind);
    }

    /// <summary>
    /// 构建 <c>startDelayTask</c> — 开始延时任务。
    /// </summary>
    /// <param name="imei">设备 IMEI。</param>
    /// <param name="slotNum">插槽编号，从 1 开始；<c>0</c> 表示所有插槽。</param>
    /// <param name="enable">是否启用。</param>
    /// <param name="startAction">开始动作：<c>on</c> / <c>off</c> / <c>toggle</c> / <c>none</c>。</param>
    /// <param name="endAction">延时结束动作：<c>on</c> / <c>off</c> / <c>toggle</c>。</param>
    /// <param name="seconds">延时秒数。</param>
    /// <param name="kind">设备品类。</param>
    /// <returns>(FrameId, JSON 报文)。</returns>
    public (string FrameId, string Payload) BuildStartDelayTask(
        string imei, int slotNum, bool enable, string startAction, string endAction, int seconds,
        AnShengDeviceKind kind = AnShengDeviceKind.Unknown)
        => BuildCommand(imei, "startDelayTask", new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["slotNum"] = slotNum,
            ["enable"] = enable,
            ["sAction"] = startAction,
            ["eAction"] = endAction,
            ["secs"] = seconds
        }, kind);

    /// <summary>
    /// 构建 <c>stopDelayTask</c> — 停止延时任务。
    /// </summary>
    /// <param name="imei">设备 IMEI。</param>
    /// <param name="slotNum">插槽编号，从 1 开始；<c>0</c> 表示所有插槽。</param>
    /// <param name="kind">设备品类。</param>
    /// <returns>(FrameId, JSON 报文)。</returns>
    public (string FrameId, string Payload) BuildStopDelayTask(
        string imei, int slotNum, AnShengDeviceKind kind = AnShengDeviceKind.Unknown)
        => BuildCommand(imei, "stopDelayTask", new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["slotNum"] = slotNum
        }, kind);

    /// <summary>
    /// 构建 <c>getDelayTasks</c> — 获取延时任务列表。
    /// </summary>
    /// <param name="imei">设备 IMEI。</param>
    /// <param name="kind">设备品类。</param>
    /// <returns>(FrameId, JSON 报文)。</returns>
    public (string FrameId, string Payload) BuildGetDelayTasks(
        string imei, AnShengDeviceKind kind = AnShengDeviceKind.Unknown)
        => BuildCommand(imei, "getDelayTasks", null, kind);

    /// <summary>
    /// 构建 <c>getEMRealtime</c> — 获取电量计实时信息。
    /// </summary>
    /// <param name="imei">设备 IMEI。</param>
    /// <param name="kind">设备品类。</param>
    /// <returns>(FrameId, JSON 报文)。</returns>
    public (string FrameId, string Payload) BuildGetEMRealtime(
        string imei, AnShengDeviceKind kind = AnShengDeviceKind.Unknown)
        => BuildCommand(imei, "getEMRealtime", null, kind);

    // ─────────────────────────────────────────────────────────────
    // G5 对时
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 构建 <c>setTime</c> — 设备对时。
    /// </summary>
    /// <param name="imei">设备 IMEI。</param>
    /// <param name="utcTime">要下发的时间；为 null 时取当前 UTC 时间。</param>
    /// <param name="kind">设备品类。</param>
    /// <returns>(FrameId, JSON 报文)。</returns>
    public (string FrameId, string Payload) BuildSetTime(
        string imei, DateTime? utcTime = null, AnShengDeviceKind kind = AnShengDeviceKind.Unknown)
    {
        var seconds = utcTime.HasValue
            ? AnShengTimestampConverter.ToUnixSeconds(utcTime.Value)
            : AnShengTimestampConverter.NowUnixSeconds();

        return BuildCommand(imei, "setTime", new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["timestamp"] = seconds
        }, kind);
    }

    // ─────────────────────────────────────────────────────────────
    // Legacy 充电桩协议族（非二开协议，保留以兼容既有链路）
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 构建 Legacy 充电桩 <c>orderStart</c> 命令。
    /// </summary>
    /// <remarks>
    /// 该命令不属于安圣二开协议（asopen.md 中无此方法），仅为兼容既有充电桩链路保留，
    /// 沿用旧的 <c>param</c> 包裹结构，请勿用于二开设备。
    /// </remarks>
    /// <param name="imei">设备 IMEI。</param>
    /// <param name="sn">订单序列号。</param>
    /// <param name="order">插槽编号（1-based）。</param>
    /// <param name="limit">限时时长（秒），可为 null。</param>
    /// <returns>(FrameId, JSON 报文)。</returns>
    [Obsolete("Legacy 充电桩协议族专用，二开设备请使用 BuildCommand/BuildAction 等目录驱动方法")]
    public (string FrameId, string Payload) BuildOrderStart(string imei, string sn, int order = 1, int? limit = null)
    {
        var param = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["sn"] = sn,
            ["order"] = order
        };
        if (limit.HasValue) param["limit"] = limit.Value;

        return BuildLegacy(imei, "orderStart", param);
    }

    /// <summary>
    /// 构建 Legacy 充电桩 <c>orderEnd</c> 命令。
    /// </summary>
    /// <remarks>该命令不属于安圣二开协议，仅为兼容既有充电桩链路保留。</remarks>
    /// <param name="imei">设备 IMEI。</param>
    /// <param name="sn">订单序列号。</param>
    /// <param name="reason">结束原因（app/manual/auto）。</param>
    /// <returns>(FrameId, JSON 报文)。</returns>
    [Obsolete("Legacy 充电桩协议族专用，二开设备请使用 BuildCommand/BuildAction 等目录驱动方法")]
    public (string FrameId, string Payload) BuildOrderEnd(string imei, string sn, string reason = "app")
        => BuildLegacy(imei, "orderEnd", new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["sn"] = sn,
            ["reason"] = reason
        });

    /// <summary>
    /// 构建任意 Legacy（非二开协议）命令，保留 <c>param</c> 包裹与毫秒字符串 timestamp。
    /// </summary>
    /// <remarks>
    /// 仅供充电桩协议族（<c>orderStart</c>/<c>orderEnd</c>/<c>orderUp</c>/<c>getDevStatus</c> 旧链路）
    /// 及未收录进 <see cref="AnShengCommandCatalog"/> 的历史命令使用。
    /// 安圣二开设备<b>必须</b>改用 <see cref="BuildCommand"/> / <see cref="BuildRaw"/>。
    /// </remarks>
    /// <param name="imei">设备 IMEI。</param>
    /// <param name="method">方法名。</param>
    /// <param name="param">参数对象，可为 null；为 null 或空时不输出 <c>param</c> 字段。</param>
    /// <param name="frameId">指定 frameId；为 null 时自动生成（T7-2 起支持外部预登记）。</param>
    /// <returns>(FrameId, 压缩后的 JSON 报文)。</returns>
    /// <exception cref="ArgumentException"><paramref name="imei"/> 为空。</exception>
    public (string FrameId, string Payload) BuildLegacyCommand(
        string imei, string method, IReadOnlyDictionary<string, object?>? param = null,
        string? frameId = null)
    {
        if (string.IsNullOrWhiteSpace(imei))
        {
            throw new ArgumentException("IMEI 不能为空", nameof(imei));
        }

        var copy = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (param != null)
        {
            foreach (var (key, value) in param)
            {
                if (string.IsNullOrWhiteSpace(key)) continue;
                copy[key] = value;
            }
        }

        return BuildLegacy(imei, method, copy, frameId);
    }

    /// <summary>
    /// Legacy 报文构建：保留 <c>param</c> 包裹与毫秒字符串 timestamp，确保充电桩链路行为不变。
    /// </summary>
    /// <param name="imei">设备 IMEI。</param>
    /// <param name="method">方法名。</param>
    /// <param name="param">参数对象（会被包裹进 <c>param</c>）。</param>
    /// <param name="frameId">
    /// 指定 frameId；为 null 时自动生成。
    /// T7-2 起下发可走「先登记在途、后发 MQTT」，此时 frameId 由调用方预先生成并登记，
    /// 这里<b>必须</b>沿用它——若仍自生成，登记的 key 与实际报文对不上，命令必然走到超时兜底。
    /// </param>
    /// <returns>(FrameId, JSON 报文)。</returns>
    private (string FrameId, string Payload) BuildLegacy(
        string imei, string method, Dictionary<string, object?> param, string? frameId = null)
    {
        var actualFrameId = string.IsNullOrWhiteSpace(frameId) ? NewFrameId() : frameId;
        var command = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["method"] = method,
            ["imei"] = imei,
            ["frameId"] = actualFrameId,
            ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString()
        };

        if (param.Count > 0)
        {
            command["param"] = param;
        }

        var payload = JsonSerializer.Serialize(command, MinifiedJson);
        _logger?.LogDebug("构建 Legacy 充电桩命令: Method={Method}, IMEI={IMEI}, FrameId={FrameId}",
            method, imei, actualFrameId);
        return (actualFrameId, payload);
    }
}
