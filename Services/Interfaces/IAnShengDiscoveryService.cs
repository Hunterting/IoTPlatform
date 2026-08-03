using IoTPlatform.Infrastructure.Protocol.AnSheng;
using IoTPlatform.Models;

namespace IoTPlatform.Services;

/// <summary>
/// 认领失败的机器可读错误码。
///
/// 【为什么要有这套常量】
///   业务失败的类型必须能被前端与测试<b>稳定地</b>分支判断。
///   靠比对中文 Message 会在每次改文案时集体假红；靠 HTTP 状态码又不够细
///   （"设备不存在"和"已被认领"都是 400，但前端处理方式完全不同）。
///   故引入这一层：Message 只给人看，ErrorCode 给程序看。
///
/// 【测试约定】断言 <c>ErrorCode</c>，<b>不要</b>断言 <c>Message</c>。
/// </summary>
public static class AnShengClaimErrorCodes
{
    /// <summary>待认领记录不存在（Id 或 IMEI 都没找到）。</summary>
    public const string DiscoveredNotFound = "DISCOVERED_NOT_FOUND";

    /// <summary>该设备已被认领过。</summary>
    public const string AlreadyClaimed = "ALREADY_CLAIMED";

    /// <summary>品类无法确定：既没人工指定，探测也没推断出来。</summary>
    public const string KindRequired = "KIND_REQUIRED";

    /// <summary>协议适配器不可用（未建立连接 / 协议配置缺失）。</summary>
    public const string AdapterUnavailable = "ADAPTER_UNAVAILABLE";

    /// <summary>探测失败（设备超时 / 未应答 / 应答无法解析）。</summary>
    public const string ProbeFailed = "PROBE_FAILED";

    /// <summary>探测结果与人工指定的品类冲突。</summary>
    public const string ProbeConflict = "PROBE_CONFLICT";

    /// <summary>落库失败（唯一键冲突 / 数据库异常）。</summary>
    public const string PersistFailed = "PERSIST_FAILED";
}

/// <summary>
/// 认领指令 —— 服务层的入参契约。
///
/// 【为什么不直接把 HTTP DTO 传进服务层】
///   <c>ClaimAnShengDeviceRequest</c> 属于表现层，字段随前端需要变动。
///   服务层若直接依赖它，一个只影响页面的字段调整就会波及领域逻辑与单元测试。
///   这里做一次显式转换，顺带把「AppCode 从哪来」这类表现层才知道的信息固化下来。
/// </summary>
public sealed class AnShengClaimCommand
{
    /// <summary>待认领记录主键。与 <see cref="Imei"/> 至少提供其一。</summary>
    public long? DiscoveredDeviceId { get; init; }

    /// <summary>待认领设备 IMEI。</summary>
    public string? Imei { get; init; }

    /// <summary>设备名称。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>人工指定的品类；<see cref="AnShengDeviceKind.Unknown"/> 表示不指定。</summary>
    public AnShengDeviceKind Kind { get; init; } = AnShengDeviceKind.Unknown;

    /// <summary>所属区域主键。</summary>
    public long? AreaId { get; init; }

    /// <summary>所属项目主键。</summary>
    public long? ProjectId { get; init; }

    /// <summary>协议配置主键；为空时由服务层自动选取。</summary>
    public long? ProtocolConfigId { get; init; }

    /// <summary>自动上报间隔（秒）。</summary>
    public int? GetDevStatusSec { get; init; }

    /// <summary>自动上报查询参数。</summary>
    public string? GetDevStatusQ { get; init; }

    /// <summary>当前租户码。由控制器从认证上下文取出后传入。</summary>
    public string AppCode { get; init; } = string.Empty;
}

/// <summary>
/// 认领结果 —— 服务层的出参契约。失败以本对象表达，<b>不抛业务异常</b>。
/// </summary>
public sealed class AnShengClaimResult
{
    /// <summary>是否成功。</summary>
    public bool Success { get; init; }

    /// <summary>机器可读错误码，取值见 <see cref="AnShengClaimErrorCodes"/>；成功时为 null。</summary>
    public string? ErrorCode { get; init; }

    /// <summary>面向人的错误描述；成功时为 null。</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>创建出的正式设备主键。</summary>
    public long? DeviceId { get; init; }

    /// <summary>设备名称。</summary>
    public string? DeviceName { get; init; }

    /// <summary>最终落档的品类。</summary>
    public AnShengDeviceKind Kind { get; init; } = AnShengDeviceKind.Unknown;

    /// <summary>能力档案主键。</summary>
    public long? ProfileId { get; init; }

    /// <summary>本次认领的探测状态。</summary>
    public AnShengProbeStatus ProbeStatus { get; init; } = AnShengProbeStatus.NotProbed;

    /// <summary>
    /// 构造成功结果。
    /// </summary>
    /// <param name="deviceId">设备主键。</param>
    /// <param name="deviceName">设备名称。</param>
    /// <param name="kind">品类。</param>
    /// <param name="profileId">档案主键。</param>
    /// <param name="probeStatus">探测状态。</param>
    /// <returns>成功结果。</returns>
    public static AnShengClaimResult Ok(
        long deviceId,
        string deviceName,
        AnShengDeviceKind kind,
        long? profileId,
        AnShengProbeStatus probeStatus) => new()
        {
            Success = true,
            DeviceId = deviceId,
            DeviceName = deviceName,
            Kind = kind,
            ProfileId = profileId,
            ProbeStatus = probeStatus
        };

    /// <summary>
    /// 构造失败结果。
    /// </summary>
    /// <param name="errorCode">错误码，取 <see cref="AnShengClaimErrorCodes"/> 常量。</param>
    /// <param name="errorMessage">面向人的描述。</param>
    /// <param name="probeStatus">探测状态，默认未探测。</param>
    /// <returns>失败结果。</returns>
    public static AnShengClaimResult Fail(
        string errorCode,
        string errorMessage,
        AnShengProbeStatus probeStatus = AnShengProbeStatus.NotProbed) => new()
        {
            Success = false,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            ProbeStatus = probeStatus
        };
}

/// <summary>
/// 安圣设备发现服务接口
/// 负责：定时扫描未认领设备、处理 Will 离线通知、维护在线状态、认领编排
/// </summary>
public interface IAnShengDiscoveryService
{
    /// <summary>
    /// 收到 Will 离线通知时调用（由适配器事件驱动）
    /// </summary>
    /// <param name="imei">离线设备 IMEI</param>
    /// <param name="appCode">租户 AppCode</param>
    /// <param name="ct">取消令牌</param>
    Task OnDeviceOfflineAsync(string imei, string? appCode, CancellationToken ct = default);

    /// <summary>
    /// 注册设备上线（收到数据时调用）
    /// </summary>
    /// <param name="imei">IMEI</param>
    /// <param name="model">设备型号（可选，从报文解析）</param>
    /// <param name="netType">网络类型（可选）</param>
    /// <param name="appCode">租户 AppCode</param>
    /// <param name="ct">取消令牌</param>
    Task OnDeviceOnlineAsync(string imei, string? model, string? netType, string? appCode, CancellationToken ct = default);

    /// <summary>
    /// 认领一台待认领设备：探测能力 → 判定品类 → 建 Device / Profile / 上报配置。
    ///
    /// 【事务边界】探测在事务<b>之外</b>执行。探测要等 5~10 秒，
    /// 把它圈进事务会长时间持有连接与行锁，高并发下直接拖垮连接池。
    ///
    /// 【失败语义】任何业务失败都以 <see cref="AnShengClaimResult.Success"/> = false 返回，
    /// <b>不抛异常</b>。调用方据 <see cref="AnShengClaimResult.ErrorCode"/> 分支。
    /// </summary>
    /// <param name="command">认领指令。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>认领结果。</returns>
    Task<AnShengClaimResult> ClaimAsync(AnShengClaimCommand command, CancellationToken ct = default);
}
