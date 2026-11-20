namespace IoTPlatform.DTOs.Responses;

// ─────────────────────────────────────────────────────────────────────────────
// 萤石开放平台代理 DTO（与 open.ys7.com 出网字段对齐，camelCase 由 ASP.NET 序列化）
// 数据来源：docs/萤石开放平台_API_云接入_OPEN_API.docx
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>萤石配置状态（前端据此决定显示真实画面还是诚实提示）</summary>
public class EzvizConfigStatusDto
{
    public bool Configured { get; set; }
    public string BaseUrl { get; set; } = string.Empty;
    public int LiveProtocol { get; set; }
    /// <summary>当前 H265 取流开关（供前端联调确认后端已生效）</summary>
    public int SupportH265 { get; set; }
    public string Message { get; set; } = string.Empty;
}

/// <summary>萤石通用出网信封 {code, msg, data}</summary>
public class EzvizEnvelope<T>
{
    public string? Code { get; set; }
    public string? Msg { get; set; }
    public T? Data { get; set; }
}

/// <summary>token/get 返回 data</summary>
public class EzvizTokenData
{
    public string? AccessToken { get; set; }
    public long ExpireTime { get; set; }
}

/// <summary>设备列表项（device/list）</summary>
public class EzvizDeviceDto
{
    public string? DeviceSerial { get; set; }
    public string? DeviceName { get; set; }
    public string? DeviceType { get; set; }
    /// <summary>1-在线，0-离线</summary>
    public int Status { get; set; }
    public int Defence { get; set; }
    public string? DeviceVersion { get; set; }
    public string? ParentCategory { get; set; }
    public string? NetAddress { get; set; }
    public long? AddTime { get; set; }
    public long? UpdateTime { get; set; }
    /// <summary>本地绑定的区域 ID（来自 cameras 表，萤石本身无区域概念）</summary>
    public long? AreaId { get; set; }
    /// <summary>本地绑定的区域名</summary>
    public string? AreaName { get; set; }

    // ── 通道/分享设备补充字段（来自 camera/list） ──
    /// <summary>通道号（IPC 一般为 1）</summary>
    public int ChannelNo { get; set; } = 1;
    /// <summary>设备封面图（萤石 picUrl，未开播/离线时展示）</summary>
    public string? PicUrl { get; set; }
    /// <summary>是否被分享设备（isShared=2）</summary>
    public bool IsShared { get; set; }
    /// <summary>来源：owned(device/list) / owned-channel / shared(camera/list)</summary>
    public string Source { get; set; } = "owned";
    /// <summary>该设备是否具备可操作权限（分享设备常被拒）</summary>
    public bool Operable { get; set; } = true;
}

/// <summary>设备列表分页信封</summary>
public class EzvizDeviceListData
{
    public List<EzvizDeviceDto>? Data { get; set; }
    public EzvizPage? Page { get; set; }
}

public class EzvizPage
{
    public int Total { get; set; }
    public int Size { get; set; }
    public int Page { get; set; }
}

public class EzvizPagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

/// <summary>通道项（camera/list）</summary>
public class EzvizCameraDto
{
    public string? DeviceSerial { get; set; }
    public int ChannelNo { get; set; }
    public string? ChannelName { get; set; }
    public int Status { get; set; }
    public string? PicUrl { get; set; }
    public bool IsEncrypt { get; set; }
}

/// <summary>直播地址（v2/live/address/get）</summary>
public class EzvizLiveAddressDto
{
    public string? Id { get; set; }
    public string? Url { get; set; }
    public long ExpireTime { get; set; }
    public string? DeviceSerial { get; set; }
    public int ChannelNo { get; set; }
}

/// <summary>抓拍（device/capture）</summary>
public class EzvizCaptureDto
{
    public string? PicUrl { get; set; }
    public string? DeviceSerial { get; set; }
    public int ChannelNo { get; set; }
}

/// <summary>设备状态（device/status/get）</summary>
public class EzvizDeviceStatusDto
{
    public string? DeviceSerial { get; set; }
    /// <summary>原始 status 值（1在线/0离线）</summary>
    public int Status { get; set; }
    public string? StatusText { get; set; }
    public int Defence { get; set; }
}

/// <summary>回放录像片段（video/by/time）</summary>
public class EzvizPlaybackRecordDto
{
    public string? DeviceSerial { get; set; }
    public int ChannelNo { get; set; }
    public long? StartTime { get; set; }
    public long? EndTime { get; set; }
    public int RecType { get; set; }
    public string? FileId { get; set; }
    public string? FileName { get; set; }
    public string? CoverPic { get; set; }
    public string? DownloadPath { get; set; }
    public long? FileSize { get; set; }
    public long? VideoLong { get; set; }
}

// ── 请求 ──────────────────────────────────────────────────────────────────────

public class EzvizLiveAddressRequest
{
    public string DeviceSerial { get; set; } = string.Empty;
    public int ChannelNo { get; set; } = 1;
    /// <summary>1-预览，2-回放（回放需 startTime/stopTime）</summary>
    public int Type { get; set; } = 1;
    /// <summary>按请求覆盖 H265 开关；null=沿用 EzvizOptions.SupportH265 全局配置。任意非0视为1。</summary>
    public int? SupportH265 { get; set; }
    /// <summary>码流质量：1=主码流、2=子码流；null=萤石默认。宫格高路数软解负载过高时可传 2。</summary>
    public int? Quality { get; set; }
    public string? StartTime { get; set; }
    public string? StopTime { get; set; }
    /// <summary>设备验证码（设备开启视频加密时必填，对应萤石 ewcode）</summary>
    public string? EwCode { get; set; }
}

public class EzvizCaptureRequest
{
    public string DeviceSerial { get; set; } = string.Empty;
    public int ChannelNo { get; set; } = 1;
}

public class EzvizPlaybackRequest
{
    public string DeviceSerial { get; set; } = string.Empty;
    public int ChannelNo { get; set; } = 1;
    /// <summary>起始时间（毫秒时间戳或 yyyy-MM-dd HH:mm:ss，见文档）</summary>
    public string StartTime { get; set; } = string.Empty;
    public string EndTime { get; set; } = string.Empty;
    /// <summary>回放源：0-系统自动选择</summary>
    public int RecType { get; set; } = 0;
}

public class EzvizPtzRequest
{
    public string DeviceSerial { get; set; } = string.Empty;
    public int ChannelNo { get; set; } = 1;
    /// <summary>0-上 1-下 2-左 3-右（文档 direction）</summary>
    public int Direction { get; set; }
    /// <summary>0-慢 1-适中 2-快</summary>
    public int Speed { get; set; } = 1;
}

/// <summary>把萤石设备绑定到本地区域（cameras 表）</summary>
public class EzvizBindCameraRequest
{
    public string DeviceSerial { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long? AreaId { get; set; }
    public int ChannelNo { get; set; } = 1;
    /// <summary>设备验证码（设备开启视频加密时必填，对应萤石 ewcode）</summary>
    public string? EwCode { get; set; }
}

/// <summary>
/// 远程切换设备视频编码（v3 /api/v3/device/video/encodeType）。
/// EncodeType 仅接受 "H264"/"H265"；ChannelNo 对应萤石 localIndex，默认 1。
/// </summary>
public class SetEncodeTypeRequest
{
    public string EncodeType { get; set; } = "H264";
    public int ChannelNo { get; set; } = 1;
}
