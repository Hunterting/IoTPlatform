using System.Security.Claims;
using IoTPlatform.DTOs.Responses;

namespace IoTPlatform.Services;

/// <summary>
/// 萤石开放平台（EZVIZ Open API）后端代理服务。
///
/// 为什么必须走后端代理：appSecret 是敏感凭证，只能由服务端持有；
/// 前端只调用本平台 /api/v1/ezviz/*，由本服务拼接 accessToken 转发到 open.ys7.com。
/// accessToken 在进程内缓存，按 expireTime 提前 5 分钟刷新，避免每次请求都取 token。
///
/// 【B1/M7】每个方法均保留旧签名（内部通道），并新增带 principal 的重载：
/// 新重载用 IEzvizScopeGuard.GetAllowedAppCodesAsync 显式过滤摄像头归属，
/// 不再依赖冻结的全局查询过滤器；旧签名保持可调用，外部 HTTP 端点一律走新重载。
/// </summary>
public interface IEzvizService
{
    /// <summary>当前配置状态（未配置凭证时前端显示诚实提示，不伪造画面）</summary>
    EzvizConfigStatusDto GetConfigStatus();

    /// <summary>设备列表（device/list，含本地区域绑定信息；内部/旧通道）</summary>
    Task<EzvizPagedResult<EzvizDeviceDto>> GetDevicesAsync(int pageStart, int pageSize, string? appCode, bool includeUnbound = false, CancellationToken ct = default);

    /// <summary>【B1/M7】设备列表：按 principal 允许 AppCode 集合下推本地绑定过滤。</summary>
    Task<EzvizPagedResult<EzvizDeviceDto>> GetDevicesAsync(int pageStart, int pageSize, string? appCode, bool includeUnbound, ClaimsPrincipal principal, CancellationToken ct = default);

    /// <summary>通道列表（camera/list；内部/旧通道）</summary>
    Task<List<EzvizCameraDto>> GetCamerasAsync(string deviceSerial, CancellationToken ct = default);

    /// <summary>【B1/M7】通道列表：带摄像头归属校验。</summary>
    Task<List<EzvizCameraDto>> GetCamerasAsync(string deviceSerial, ClaimsPrincipal principal, CancellationToken ct = default);

    /// <summary>直播/回放地址（v2/live/address/get；内部/旧通道）</summary>
    Task<EzvizLiveAddressDto> GetLiveAddressAsync(EzvizLiveAddressRequest request, CancellationToken ct = default);

    /// <summary>【B1/M7】直播/回放地址：带摄像头归属校验。</summary>
    Task<EzvizLiveAddressDto> GetLiveAddressAsync(EzvizLiveAddressRequest request, ClaimsPrincipal principal, CancellationToken ct = default);

    /// <summary>抓拍（device/capture；内部/旧通道）</summary>
    Task<EzvizCaptureDto> CaptureAsync(EzvizCaptureRequest request, CancellationToken ct = default);

    /// <summary>【B1/M7】抓拍：带摄像头归属校验。</summary>
    Task<EzvizCaptureDto> CaptureAsync(EzvizCaptureRequest request, ClaimsPrincipal principal, CancellationToken ct = default);

    /// <summary>回放录像查询（video/by/time；内部/旧通道）</summary>
    Task<List<EzvizPlaybackRecordDto>> GetPlaybackAsync(EzvizPlaybackRequest request, CancellationToken ct = default);

    /// <summary>【B1/M7】回放录像查询：带摄像头归属校验。</summary>
    Task<List<EzvizPlaybackRecordDto>> GetPlaybackAsync(EzvizPlaybackRequest request, ClaimsPrincipal principal, CancellationToken ct = default);

    /// <summary>云台开始转动（device/ptz/start；内部/旧通道）</summary>
    Task PtzStartAsync(EzvizPtzRequest request, CancellationToken ct = default);

    /// <summary>【B1/M7】云台开始转动：带摄像头归属校验。</summary>
    Task PtzStartAsync(EzvizPtzRequest request, ClaimsPrincipal principal, CancellationToken ct = default);

    /// <summary>云台停止（device/ptz/stop；内部/旧通道）</summary>
    Task PtzStopAsync(EzvizPtzRequest request, CancellationToken ct = default);

    /// <summary>【B1/M7】云台停止：带摄像头归属校验。</summary>
    Task PtzStopAsync(EzvizPtzRequest request, ClaimsPrincipal principal, CancellationToken ct = default);

    /// <summary>设备状态（device/status/get；内部/旧通道）</summary>
    Task<EzvizDeviceStatusDto> GetDeviceStatusAsync(string deviceSerial, CancellationToken ct = default);

    /// <summary>【B1/M7】设备状态：带摄像头归属校验。</summary>
    Task<EzvizDeviceStatusDto> GetDeviceStatusAsync(string deviceSerial, ClaimsPrincipal principal, CancellationToken ct = default);

    /// <summary>绑定萤石设备到本地区域（写入 cameras 表；内部/旧通道）</summary>
    Task<EzvizDeviceDto> BindCameraAsync(EzvizBindCameraRequest request, string? appCode, CancellationToken ct = default);

    /// <summary>
    /// 【B1/M7】绑定摄像头：目标区域与既有绑定均须属于 principal 授权范围，
    /// 且 appCode 必须是服务端从授权区域派生的值。
    /// </summary>
    Task<EzvizDeviceDto> BindCameraAsync(EzvizBindCameraRequest request, string? appCode, ClaimsPrincipal principal, CancellationToken ct = default);

    /// <summary>
    /// 关闭设备视频加密（device/encrypt/off；内部/旧通道）。
    /// 设备侧配置变更，需调用方显式确认后再调用；已关闭（60016）按幂等成功处理。
    /// </summary>
    Task CloseEncryptionAsync(string deviceSerial, CancellationToken ct = default);

    /// <summary>【B1/M7】关闭设备视频加密：带摄像头归属校验。</summary>
    Task CloseEncryptionAsync(string deviceSerial, ClaimsPrincipal principal, CancellationToken ct = default);

    /// <summary>
    /// 远程把设备视频编码改为 H264/H265（v3 /api/v3/device/video/encodeType；内部/旧通道）。
    /// encodeType 仅接受 "H264"/"H265"；属设备侧配置变更，需调用方显式确认后再调用。
    /// </summary>
    Task SetVideoEncodeAsync(string deviceSerial, string encodeType, int channelNo = 1, CancellationToken ct = default);

    /// <summary>【B1/M7】远程切换视频编码：带摄像头归属校验。</summary>
    Task SetVideoEncodeAsync(string deviceSerial, string encodeType, int channelNo, ClaimsPrincipal principal, CancellationToken ct = default);

    /// <summary>
    /// 查询设备当前视频编码（v3 /api/v3/das/device/video/encode；内部/旧通道）。
    /// 返回 videoCode：1=标准H264，5=标准H265；查不到返回 null。
    /// </summary>
    Task<int?> GetVideoEncodeAsync(string deviceSerial, int channelNo = 1, CancellationToken ct = default);

    /// <summary>【B1/M7】查询视频编码：带摄像头归属校验。</summary>
    Task<int?> GetVideoEncodeAsync(string deviceSerial, int channelNo, ClaimsPrincipal principal, CancellationToken ct = default);
}
