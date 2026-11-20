using IoTPlatform.DTOs.Responses;

namespace IoTPlatform.Services;

/// <summary>
/// 萤石开放平台（EZVIZ Open API）后端代理服务。
///
/// 为什么必须走后端代理：appSecret 是敏感凭证，只能由服务端持有；
/// 前端只调用本平台 /api/v1/ezviz/*，由本服务拼接 accessToken 转发到 open.ys7.com。
/// accessToken 在进程内缓存，按 expireTime 提前 5 分钟刷新，避免每次请求都取 token。
/// </summary>
public interface IEzvizService
{
    /// <summary>当前配置状态（未配置凭证时前端显示诚实提示，不伪造画面）</summary>
    EzvizConfigStatusDto GetConfigStatus();

    /// <summary>设备列表（device/list，含本地区域绑定信息）</summary>
    Task<EzvizPagedResult<EzvizDeviceDto>> GetDevicesAsync(int pageStart, int pageSize, string? appCode, bool includeUnbound = false, CancellationToken ct = default);

    /// <summary>通道列表（camera/list）</summary>
    Task<List<EzvizCameraDto>> GetCamerasAsync(string deviceSerial, CancellationToken ct = default);

    /// <summary>直播/回放地址（v2/live/address/get）</summary>
    Task<EzvizLiveAddressDto> GetLiveAddressAsync(EzvizLiveAddressRequest request, CancellationToken ct = default);

    /// <summary>抓拍（device/capture）</summary>
    Task<EzvizCaptureDto> CaptureAsync(EzvizCaptureRequest request, CancellationToken ct = default);

    /// <summary>回放录像查询（video/by/time）</summary>
    Task<List<EzvizPlaybackRecordDto>> GetPlaybackAsync(EzvizPlaybackRequest request, CancellationToken ct = default);

    /// <summary>云台开始转动（device/ptz/start）</summary>
    Task PtzStartAsync(EzvizPtzRequest request, CancellationToken ct = default);

    /// <summary>云台停止（device/ptz/stop）</summary>
    Task PtzStopAsync(EzvizPtzRequest request, CancellationToken ct = default);

    /// <summary>设备状态（device/status/get）</summary>
    Task<EzvizDeviceStatusDto> GetDeviceStatusAsync(string deviceSerial, CancellationToken ct = default);

    /// <summary>绑定萤石设备到本地区域（写入 cameras 表）</summary>
    Task<EzvizDeviceDto> BindCameraAsync(EzvizBindCameraRequest request, string? appCode, CancellationToken ct = default);

    /// <summary>
    /// 关闭设备视频加密（device/encrypt/off）。
    /// 设备侧配置变更，需调用方显式确认后再调用；已关闭（60016）按幂等成功处理。
    /// </summary>
    Task CloseEncryptionAsync(string deviceSerial, CancellationToken ct = default);
}
