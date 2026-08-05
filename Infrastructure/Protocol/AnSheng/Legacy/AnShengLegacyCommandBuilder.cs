using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace IoTPlatform.Infrastructure.Protocol.AnSheng.Legacy;

/// <summary>
/// Legacy 充电桩协议族命令构建器（T14 协议族归位）。
///
/// 【为什么要单独一个类】改造前这段逻辑内联在 <see cref="AnShengCommandBuilder"/> 尾部
/// （<c>BuildLegacy</c> / <c>BuildLegacyCommand</c> / <c>BuildOrderStart</c> / <c>BuildOrderEnd</c>）。
/// 两套<b>报文结构互不兼容</b>的协议共处一个类，读者很难一眼看出「哪个方法产出哪种结构」，
/// 也很容易在给二开协议加字段时顺手改到 Legacy 分支。归位到独立文件后，
/// 协议族边界变成<b>物理边界</b>：本文件只产出 param 包裹结构，一行二开逻辑都没有。
///
/// 【报文结构】与改造前<b>逐字节一致</b>（验收标准 2）：
/// <code>
/// {"method":"orderStart","imei":"864536072949900","frameId":"a1b2c3d4e5f60718","timestamp":"1745396759000","param":{"sn":"SN001","order":1}}
/// </code>
///   1. 业务参数一律包进 <c>param</c> 对象（<b>不</b>平铺），param 为空时整个键不出现；
///   2. <c>timestamp</c> 为<b>毫秒字符串</b>（二开协议是秒级 int，且仅 4G 注入）；
///   3. 字段顺序固定 method → imei → frameId → timestamp → param；
///   4. 压缩 JSON（无缩进）。
///
/// 【与二开协议构建器的分工】method 属于哪个协议族由
/// <see cref="AnShengProtocolFamilyResolver"/> 单点判定，调用方不得自己写 if。
/// </summary>
public sealed class AnShengLegacyCommandBuilder
{
    /// <summary>压缩 JSON 序列化选项：不缩进、不转义中文、保留 null 语义（与二开构建器一致）。</summary>
    private static readonly JsonSerializerOptions MinifiedJson = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    private readonly ILogger<AnShengLegacyCommandBuilder>? _logger;

    /// <summary>
    /// 创建 Legacy 充电桩命令构建器。
    /// </summary>
    /// <param name="logger">可选日志器。</param>
    public AnShengLegacyCommandBuilder(ILogger<AnShengLegacyCommandBuilder>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// 构建 Legacy 充电桩 <c>orderStart</c> — 开始充电。
    /// </summary>
    /// <param name="imei">设备 IMEI。</param>
    /// <param name="sn">订单序列号。</param>
    /// <param name="order">插槽编号（1-based）。</param>
    /// <param name="limit">限时时长（秒），可为 null（不下发该字段）。</param>
    /// <returns>(FrameId, JSON 报文)。</returns>
    /// <exception cref="ArgumentException"><paramref name="imei"/> 为空。</exception>
    public (string FrameId, string Payload) BuildOrderStart(
        string imei, string sn, int order = 1, int? limit = null)
    {
        var param = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["sn"] = sn,
            ["order"] = order
        };
        if (limit.HasValue) param["limit"] = limit.Value;

        return Build(imei, AnShengLegacyCommandCatalog.OrderStart, param, frameId: null);
    }

    /// <summary>
    /// 构建 Legacy 充电桩 <c>orderEnd</c> — 结束充电。
    /// </summary>
    /// <param name="imei">设备 IMEI。</param>
    /// <param name="sn">订单序列号。</param>
    /// <param name="reason">结束原因（<c>app</c> / <c>manual</c> / <c>auto</c>）。</param>
    /// <returns>(FrameId, JSON 报文)。</returns>
    /// <exception cref="ArgumentException"><paramref name="imei"/> 为空。</exception>
    public (string FrameId, string Payload) BuildOrderEnd(
        string imei, string sn, string reason = "app")
        => Build(imei, AnShengLegacyCommandCatalog.OrderEnd, new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["sn"] = sn,
            ["reason"] = reason
        }, frameId: null);

    /// <summary>
    /// 构建任意 Legacy 充电桩命令。
    ///
    /// 【T14 加固】<paramref name="method"/> 必须显式登记在
    /// <see cref="AnShengLegacyCommandCatalog"/> 中，否则<b>快速失败</b>。
    /// 改造前这里是无条件兜底构造 —— 任何拼写错误或协议外 method 都会被真实发往现网设备，
    /// 这正是 T14 要消除的隐患。
    /// </summary>
    /// <param name="imei">设备 IMEI。</param>
    /// <param name="method">Legacy 方法名，必须属于充电桩协议族。</param>
    /// <param name="param">参数对象，可为 null；为 null 或空时不输出 <c>param</c> 字段。</param>
    /// <param name="frameId">
    /// 指定 frameId；为 null 时自动生成。
    /// T7-2 起下发可走「先登记在途、后发 MQTT」，此时 frameId 由调用方预先生成并登记，
    /// 这里<b>必须</b>沿用它——若仍自生成，登记的 key 与实际报文对不上，命令必然走到超时兜底。
    /// </param>
    /// <returns>(FrameId, 压缩后的 JSON 报文)。</returns>
    /// <exception cref="ArgumentException"><paramref name="imei"/> 为空。</exception>
    /// <exception cref="NotSupportedException"><paramref name="method"/> 不属于 Legacy 充电桩协议族。</exception>
    public (string FrameId, string Payload) BuildCommand(
        string imei, string method,
        IReadOnlyDictionary<string, object?>? param = null,
        string? frameId = null)
    {
        EnsureChargingPileMethod(method);

        var copy = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (param != null)
        {
            foreach (var (key, value) in param)
            {
                if (string.IsNullOrWhiteSpace(key)) continue;
                copy[key] = value;
            }
        }

        return Build(imei, method, copy, frameId);
    }

    /// <summary>
    /// 校验 method 确属 Legacy 充电桩协议族，否则抛出 <see cref="NotSupportedException"/>。
    ///
    /// 【为什么是 public static】适配器与命令服务在<b>构造报文之前</b>就要做这道判定
    /// （越早失败，越不可能有报文外发）；同时它是纯函数，单测可直接断言。
    /// </summary>
    /// <param name="method">方法名，可为 null。</param>
    /// <exception cref="NotSupportedException">不属于 Legacy 充电桩协议族。</exception>
    public static void EnsureChargingPileMethod(string? method)
    {
        if (AnShengLegacyCommandCatalog.Contains(method)) return;

        throw new NotSupportedException(
            $"方法 {method} 不属于 Legacy 充电桩协议族"
            + $"（仅 {string.Join(" / ", AnShengLegacyCommandCatalog.Methods.OrderBy(m => m, StringComparer.Ordinal))}），"
            + "禁止下发。");
    }

    /// <summary>
    /// Legacy 报文构建：保留 <c>param</c> 包裹与毫秒字符串 timestamp，确保充电桩链路行为不变。
    /// </summary>
    /// <param name="imei">设备 IMEI。</param>
    /// <param name="method">方法名。</param>
    /// <param name="param">参数对象（会被包裹进 <c>param</c>）。</param>
    /// <param name="frameId">指定 frameId；为 null 时自动生成。</param>
    /// <returns>(FrameId, JSON 报文)。</returns>
    /// <exception cref="ArgumentException"><paramref name="imei"/> 为空。</exception>
    private (string FrameId, string Payload) Build(
        string imei, string method, Dictionary<string, object?> param, string? frameId)
    {
        if (string.IsNullOrWhiteSpace(imei))
        {
            throw new ArgumentException("IMEI 不能为空", nameof(imei));
        }

        // frameId 生成沿用二开构建器的静态实现：同一进程内单调递增 + 加密随机，
        // 两个协议族共用<b>同一个</b>发号器，避免两族命令在在途表里撞 key。
        var actualFrameId = string.IsNullOrWhiteSpace(frameId)
            ? AnShengCommandBuilder.NewFrameId()
            : frameId;

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
