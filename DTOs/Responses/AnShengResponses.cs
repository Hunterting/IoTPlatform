using IoTPlatform.Infrastructure.Protocol.AnSheng;
using IoTPlatform.Models;

namespace IoTPlatform.DTOs.Responses;

/// <summary>
/// 安圣命令下发响应
/// </summary>
public class AnShengCommandResponse
{
    /// <summary>是否成功</summary>
    public bool Success { get; set; }

    /// <summary>安圣 FrameId（请求-响应关联）</summary>
    public string? FrameId { get; set; }

    /// <summary>下发的命令 JSON（用于调试）</summary>
    public string? Payload { get; set; }

    /// <summary>错误信息</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>下发时间</summary>
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 安圣设备发现项
/// </summary>
public class DiscoveredAnShengDeviceDto
{
    /// <summary>ID</summary>
    public long Id { get; set; }

    /// <summary>IMEI</summary>
    public string Imei { get; set; } = string.Empty;

    /// <summary>设备型号</summary>
    public string? Model { get; set; }

    /// <summary>网络类型</summary>
    public string? NetType { get; set; }

    /// <summary>首次发现时间</summary>
    public DateTime DiscoveredAt { get; set; }

    /// <summary>最后在线时间</summary>
    public DateTime? LastSeenAt { get; set; }

    /// <summary>是否已认领</summary>
    public bool IsClaimed { get; set; }

    /// <summary>认领后设备 ID</summary>
    public long? ClaimedDeviceId { get; set; }

    // ── T5 新增：待认领阶段的能力快照，供列表页在"点认领之前"就展示设备是什么 ──

    /// <summary>已判定的设备品类（枚举值）。</summary>
    public AnShengDeviceKind Kind { get; set; } = AnShengDeviceKind.Unknown;

    /// <summary>品类中文名，如 <c>4G开关</c>；未判定为 <c>未知品类</c>。</summary>
    public string KindName { get; set; } = AnShengDeviceKind.Unknown.ToDisplayName();

    /// <summary>
    /// 建议品类 —— 基于当前已知信息实时推断的结果，仅供前端做默认选中项。
    /// 与 <see cref="Kind"/> 的区别：<see cref="Kind"/> 是已落库的结论，本字段是"猜"。
    /// </summary>
    public AnShengDeviceKind SuggestedKind { get; set; } = AnShengDeviceKind.Unknown;

    /// <summary>建议品类的中文名，供前端直接渲染，免得再维护一份枚举→中文的映射。</summary>
    public string SuggestedKindName { get; set; } = AnShengDeviceKind.Unknown.ToDisplayName();

    /// <summary>插槽数量；未知为 <c>null</c>。</summary>
    public int? SlotAmount { get; set; }

    /// <summary>固件版本号。</summary>
    public string? Version { get; set; }

    /// <summary>物联卡 ICCID。</summary>
    public string? Iccid { get; set; }

    /// <summary>探测状态（枚举值）。</summary>
    public AnShengProbeStatus ProbeStatus { get; set; } = AnShengProbeStatus.NotProbed;

    /// <summary>探测失败原因摘要；成功或未探测为 <c>null</c>。</summary>
    public string? ProbeError { get; set; }

    /// <summary>最近一次探测时间（UTC）。</summary>
    public DateTime? LastProbedAt { get; set; }
}

/// <summary>
/// 安圣设备认领响应。
///
/// 【错误表达约定】
///   本平台业务失败仍返回 HTTP 200，靠 <c>ApiResponse.Code</c> 表达状态。
///   而<b>具体是哪一类失败</b>由 <see cref="ErrorCode"/> 这个机器可读常量承载，
///   <c>ApiResponse.Message</c> 只面向人。前端与测试<b>必须</b>断言 <see cref="ErrorCode"/>，
///   断言 Message 文案会在每次改文案时假红。
/// </summary>
public class ClaimAnShengDeviceResponse
{
    /// <summary>认领是否成功</summary>
    public bool Success { get; set; }

    /// <summary>创建后的设备 ID</summary>
    public long? DeviceId { get; set; }

    /// <summary>设备名称</summary>
    public string? DeviceName { get; set; }

    /// <summary>
    /// 机器可读错误码，取值见 <c>AnShengClaimErrorCodes</c>。成功时为 <c>null</c>。
    /// </summary>
    public string? ErrorCode { get; set; }

    /// <summary>错误信息（面向人的描述，勿用于程序分支判断）</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>最终落档的设备品类。</summary>
    public AnShengDeviceKind Kind { get; set; } = AnShengDeviceKind.Unknown;

    /// <summary>品类中文名，同时也是写入 <c>Device.Category</c> 的值。</summary>
    public string? KindName { get; set; }

    /// <summary>能力档案主键；认领失败时为 <c>null</c>。</summary>
    public long? ProfileId { get; set; }

    /// <summary>本次认领的探测状态。</summary>
    public AnShengProbeStatus ProbeStatus { get; set; } = AnShengProbeStatus.NotProbed;
}

/// <summary>
/// 安圣设备发现列表分页响应
/// </summary>
public class DiscoveredDeviceListResponse
{
    /// <summary>设备列表</summary>
    public List<DiscoveredAnShengDeviceDto> Items { get; set; } = new();

    /// <summary>总记录数</summary>
    public int Total { get; set; }

    /// <summary>当前页码</summary>
    public int Page { get; set; }

    /// <summary>每页条数</summary>
    public int PageSize { get; set; }
}
