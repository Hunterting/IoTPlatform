namespace IoTPlatform.Configuration;

/// <summary>
/// 安圣设备探测参数。绑定配置节 <c>AnSheng:Probe</c>。
/// </summary>
public class AnShengProbeOptions
{
    /// <summary>配置节名称。</summary>
    public const string SectionName = "AnSheng:Probe";

    /// <summary>
    /// 单条指令的等待超时（毫秒），默认 5000。
    ///
    /// 【为什么是 5 秒而不是更长】
    ///   认领是同步 HTTP 请求，用户在页面上等着。探测串行两条指令，
    ///   最坏情况 2 × 超时 = 用户等待时长。5 秒 × 2 = 10 秒已是交互容忍上限。
    ///   设备侧正常应答在 1 秒内，5 秒足以覆盖网络抖动。
    /// </summary>
    public int TimeoutMs { get; set; } = 5000;

    /// <summary>
    /// 是否在认领时执行主动探测，默认 <c>true</c>。
    /// 置 <c>false</c> 可让认领跳过探测直接建档（应急开关，用于设备批量离线时仍需建档的场景）。
    /// </summary>
    public bool EnabledOnClaim { get; set; } = true;
}
