namespace IoTPlatform.Configuration;

/// <summary>
/// 萤石开放平台（ys7 / EZVIZ Open API）接入配置。
///
/// 安全约定：
///  - AppSecret 属敏感凭证，只在后端持有，绝不下发前端，也不写入代码库明文。
///  - 优先读环境变量 Ezviz__AppKey / Ezviz__AppSecret（.NET 配置提供程序自动覆盖
///    appsettings），其次 appsettings.json 的 "Ezviz" 节。
///  - 未配置（IsConfigured=false）时，所有萤石代理接口返回明确状态，不伪造数据、不抛 500。
/// </summary>
public class EzvizOptions
{
    public const string SectionName = "Ezviz";

    /// <summary>开放平台网关，默认 https://open.ys7.com</summary>
    public string BaseUrl { get; set; } = "https://open.ys7.com";

    /// <summary>应用唯一标识 appKey</summary>
    public string AppKey { get; set; } = string.Empty;

    /// <summary>应用密钥 appSecret（敏感）</summary>
    public string AppSecret { get; set; } = string.Empty;

    /// <summary>默认直播协议：2=HLS(m3u8)、1=ezopen、3=RTMP。</summary>
    public int LiveProtocol { get; set; } = 2;

    /// <summary>
    /// 取流时是否声明客户端支持 H265：0=不要求（默认，保持既有行为），1=需要/支持 H265。
    /// 当 LiveProtocol=2(HLS) 且本值为 1 时，GetLiveAddressAsync 会自动附带 containerFormat=1(fMP4)。
    /// 可由环境变量 Ezviz__SupportH265 覆盖。
    /// </summary>
    public int SupportH265 { get; set; } = 0;

    /// <summary>直播地址有效期（秒），萤石允许范围见文档，默认 7200。</summary>
    public int LiveExpireSeconds { get; set; } = 7200;

    /// <summary>HTTP 调用超时（秒）。</summary>
    public int TimeoutSeconds { get; set; } = 15;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AppKey) && !string.IsNullOrWhiteSpace(AppSecret);
}
