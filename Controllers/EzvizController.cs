using System.Security.Claims;
using IoTPlatform.Configuration;
using IoTPlatform.Data;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Filters;
using IoTPlatform.Helpers;
using IoTPlatform.Services;
using IoTPlatform.Services.Guards;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IoTPlatform.Controllers;

/// <summary>
/// 萤石开放平台（EZVIZ Open API）代理控制器 —— 视频监控页真实数据源。
///
/// 设计要点：
///  - appSecret 只在后端 EzvizService 持有，前端永远拿不到凭证。
///  - 未配置凭证时 config/status 返回 Configured=false；其余接口返回明确业务错误，
///    前端据此显示「未配置萤石凭证」的诚实提示，绝不返回任何模拟数据。
///  - 萤石告警走消息推送/回调，无「拉取告警列表」接口，本控制器不伪造告警；
///    实时报警由前端复用本平台 /alerts 接口。
///  - 【B1/M7】所有以摄像头为目标的端点先过 IEzvizScopeGuard 归属校验（原则 G），
///    越权返回业务码 403 且零萤石 API 副作用；绑定目标区域另过 IDeviceAccessGuard。
/// </summary>
[ApiController]
[Route("api/v1/ezviz")]
[PermissionAuthorize(Permissions.VIEW_MONITORING)]
public class EzvizController : ControllerBase
{
    private readonly IEzvizService _ezviz;
    private readonly ILogger<EzvizController> _logger;
    private readonly IEzvizScopeGuard _ezvizGuard;
    private readonly IDeviceAccessGuard _deviceGuard;
    private readonly ICustomerScopeService _scope;
    private readonly AppDbContext _db;

    public EzvizController(
        IEzvizService ezviz,
        ILogger<EzvizController> logger,
        IEzvizScopeGuard ezvizGuard,
        IDeviceAccessGuard deviceGuard,
        ICustomerScopeService scope,
        AppDbContext db)
    {
        _ezviz = ezviz;
        _logger = logger;
        _ezvizGuard = ezvizGuard;
        _deviceGuard = deviceGuard;
        _scope = scope;
        _db = db;
    }

    private string? AppCode => User.FindFirst("AppCode")?.Value;

    private ActionResult<ApiResponse<T>> Fail<T>(EzvizException ex)
    {
        var status = ex.EzvizCode switch
        {
            "NOT_CONFIGURED" => 503,
            "40301" => 403,
            // v3 device/video/encodeType 业务码按官方文档语义映射（HTTP 恒 200，业务成败看 code）
            "20011" => 422, // 设备不支持/异常
            "20008" => 408, // 设备响应超时
            "20018" => 403, // 不拥有设备
            "20007" => 412, // 设备不在线
            _ => 502,
        };
        return ApiResponse<T>.Fail(status, ex.Message);
    }

    /// <summary>萤石配置状态（前端首屏调用，决定是否展示真实画面）</summary>
    [HttpGet("config/status")]
    public ActionResult<ApiResponse<EzvizConfigStatusDto>> GetConfigStatus()
        => ApiResponse<EzvizConfigStatusDto>.Success(_ezviz.GetConfigStatus());

    /// <summary>
    /// 【B1/M7】摄像头归属前置校验：越权在萤石 HTTP 调用之前拒绝（原则 G）。
    /// </summary>
    private async Task EnsureCameraAsync(string deviceSerial, CancellationToken ct)
        => await _ezvizGuard.EnsureCameraAccessAsync(User, deviceSerial, ct);

    // ── 列表 ────────────────────────────────────────────────────────────────

    /// <summary>设备列表（device/list）</summary>
    [HttpGet("devices")]
    public async Task<ActionResult<ApiResponse<EzvizPagedResult<EzvizDeviceDto>>>> GetDevices(
        [FromQuery] int pageStart = 0,
        [FromQuery] int pageSize = 20,
        [FromQuery] bool includeUnbound = false,
        CancellationToken ct = default)
    {
        try
        {
            // 【B1/M7】列表在 Service 层用 IEzvizScopeGuard.GetAllowedAppCodesAsync 显式下推
            // （超管空集合=不过滤），不再依赖冻结的全局过滤器。
            var result = await _ezviz.GetDevicesAsync(
                pageStart, pageSize, AppCode, includeUnbound, User, ct);
            return ApiResponse<EzvizPagedResult<EzvizDeviceDto>>.Success(result);
        }
        catch (ForbiddenAccessException ex)
        {
            return ApiResponse<EzvizPagedResult<EzvizDeviceDto>>.Forbidden(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return ApiResponse<EzvizPagedResult<EzvizDeviceDto>>.Unauthorized(ex.Message);
        }
        catch (EzvizException ex) { return Fail<EzvizPagedResult<EzvizDeviceDto>>(ex); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "萤石设备列表查询失败");
            return ApiResponse<EzvizPagedResult<EzvizDeviceDto>>.Error(ex.Message);
        }
    }

    /// <summary>通道列表（camera/list）</summary>
    [HttpGet("devices/{deviceSerial}/cameras")]
    public async Task<ActionResult<ApiResponse<List<EzvizCameraDto>>>> GetCameras(
        string deviceSerial, CancellationToken ct = default)
    {
        try
        {
            await EnsureCameraAsync(deviceSerial, ct);
            var result = await _ezviz.GetCamerasAsync(deviceSerial, User, ct);
            return ApiResponse<List<EzvizCameraDto>>.Success(result);
        }
        catch (ForbiddenAccessException ex) { return ApiResponse<List<EzvizCameraDto>>.Forbidden(ex.Message); }
        catch (UnauthorizedAccessException ex) { return ApiResponse<List<EzvizCameraDto>>.Unauthorized(ex.Message); }
        catch (EzvizException ex) { return Fail<List<EzvizCameraDto>>(ex); }
        catch (Exception ex) { return ApiResponse<List<EzvizCameraDto>>.Error(ex.Message); }
    }

    /// <summary>直播/回放地址（v2/live/address/get）</summary>
    [HttpPost("live-address")]
    public async Task<ActionResult<ApiResponse<EzvizLiveAddressDto>>> GetLiveAddress(
        [FromBody] EzvizLiveAddressRequest request, CancellationToken ct = default)
    {
        try
        {
            await EnsureCameraAsync(request.DeviceSerial, ct);
            var result = await _ezviz.GetLiveAddressAsync(request, User, ct);
            return ApiResponse<EzvizLiveAddressDto>.Success(result);
        }
        catch (ForbiddenAccessException ex) { return ApiResponse<EzvizLiveAddressDto>.Forbidden(ex.Message); }
        catch (UnauthorizedAccessException ex) { return ApiResponse<EzvizLiveAddressDto>.Unauthorized(ex.Message); }
        catch (EzvizException ex) { return Fail<EzvizLiveAddressDto>(ex); }
        catch (Exception ex) { return ApiResponse<EzvizLiveAddressDto>.Error(ex.Message); }
    }

    /// <summary>抓拍（device/capture）→ 截图取证</summary>
    [HttpPost("capture")]
    public async Task<ActionResult<ApiResponse<EzvizCaptureDto>>> Capture(
        [FromBody] EzvizCaptureRequest request, CancellationToken ct = default)
    {
        try
        {
            await EnsureCameraAsync(request.DeviceSerial, ct);
            var result = await _ezviz.CaptureAsync(request, User, ct);
            return ApiResponse<EzvizCaptureDto>.Success(result);
        }
        catch (ForbiddenAccessException ex) { return ApiResponse<EzvizCaptureDto>.Forbidden(ex.Message); }
        catch (UnauthorizedAccessException ex) { return ApiResponse<EzvizCaptureDto>.Unauthorized(ex.Message); }
        catch (EzvizException ex) { return Fail<EzvizCaptureDto>(ex); }
        catch (Exception ex) { return ApiResponse<EzvizCaptureDto>.Error(ex.Message); }
    }

    /// <summary>回放录像查询（video/by/time）</summary>
    [HttpPost("playback")]
    public async Task<ActionResult<ApiResponse<List<EzvizPlaybackRecordDto>>>> GetPlayback(
        [FromBody] EzvizPlaybackRequest request, CancellationToken ct = default)
    {
        try
        {
            await EnsureCameraAsync(request.DeviceSerial, ct);
            var result = await _ezviz.GetPlaybackAsync(request, User, ct);
            return ApiResponse<List<EzvizPlaybackRecordDto>>.Success(result);
        }
        catch (ForbiddenAccessException ex) { return ApiResponse<List<EzvizPlaybackRecordDto>>.Forbidden(ex.Message); }
        catch (UnauthorizedAccessException ex) { return ApiResponse<List<EzvizPlaybackRecordDto>>.Unauthorized(ex.Message); }
        catch (EzvizException ex) { return Fail<List<EzvizPlaybackRecordDto>>(ex); }
        catch (Exception ex) { return ApiResponse<List<EzvizPlaybackRecordDto>>.Error(ex.Message); }
    }

    /// <summary>云台开始转动（device/ptz/start）</summary>
    [HttpPost("ptz/start")]
    public async Task<ActionResult<ApiResponse<object>>> PtzStart(
        [FromBody] EzvizPtzRequest request, CancellationToken ct = default)
    {
        try
        {
            await EnsureCameraAsync(request.DeviceSerial, ct);
            await _ezviz.PtzStartAsync(request, User, ct);
            return ApiResponse<object>.Success(new { ok = true }, "云台指令已下发");
        }
        catch (ForbiddenAccessException ex) { return ApiResponse<object>.Forbidden(ex.Message); }
        catch (UnauthorizedAccessException ex) { return ApiResponse<object>.Unauthorized(ex.Message); }
        catch (EzvizException ex) { return Fail<object>(ex); }
        catch (Exception ex) { return ApiResponse<object>.Error(ex.Message); }
    }

    /// <summary>云台停止（device/ptz/stop）</summary>
    [HttpPost("ptz/stop")]
    public async Task<ActionResult<ApiResponse<object>>> PtzStop(
        [FromBody] EzvizPtzRequest request, CancellationToken ct = default)
    {
        try
        {
            await EnsureCameraAsync(request.DeviceSerial, ct);
            await _ezviz.PtzStopAsync(request, User, ct);
            return ApiResponse<object>.Success(new { ok = true }, "云台已停止");
        }
        catch (ForbiddenAccessException ex) { return ApiResponse<object>.Forbidden(ex.Message); }
        catch (UnauthorizedAccessException ex) { return ApiResponse<object>.Unauthorized(ex.Message); }
        catch (EzvizException ex) { return Fail<object>(ex); }
        catch (Exception ex) { return ApiResponse<object>.Error(ex.Message); }
    }

    /// <summary>设备状态（device/status/get）</summary>
    [HttpGet("devices/{deviceSerial}/status")]
    public async Task<ActionResult<ApiResponse<EzvizDeviceStatusDto>>> GetDeviceStatus(
        string deviceSerial, CancellationToken ct = default)
    {
        try
        {
            await EnsureCameraAsync(deviceSerial, ct);
            var result = await _ezviz.GetDeviceStatusAsync(deviceSerial, User, ct);
            return ApiResponse<EzvizDeviceStatusDto>.Success(result);
        }
        catch (ForbiddenAccessException ex) { return ApiResponse<EzvizDeviceStatusDto>.Forbidden(ex.Message); }
        catch (UnauthorizedAccessException ex) { return ApiResponse<EzvizDeviceStatusDto>.Unauthorized(ex.Message); }
        catch (EzvizException ex) { return Fail<EzvizDeviceStatusDto>(ex); }
        catch (Exception ex) { return ApiResponse<EzvizDeviceStatusDto>.Error(ex.Message); }
    }

    /// <summary>把萤石设备绑定到本地区域（cameras 表）→ 添加摄像头/按区域筛选</summary>
    [HttpPost("bindings")]
    public async Task<ActionResult<ApiResponse<EzvizDeviceDto>>> BindCamera(
        [FromBody] EzvizBindCameraRequest request, CancellationToken ct = default)
    {
        try
        {
            // 【B1/M7】绑定目标区域必须授权；AppCode 从服务端点查的区域派生，不采信请求体/请求头。
            var appCode = await ResolveBindAppCodeAsync(request, ct);
            var result = await _ezviz.BindCameraAsync(request, appCode, User, ct);
            return ApiResponse<EzvizDeviceDto>.Success(result, "摄像头已绑定区域");
        }
        catch (ForbiddenAccessException ex) { return ApiResponse<EzvizDeviceDto>.Forbidden(ex.Message); }
        catch (UnauthorizedAccessException ex) { return ApiResponse<EzvizDeviceDto>.Unauthorized(ex.Message); }
        catch (EzvizException ex) { return Fail<EzvizDeviceDto>(ex); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "萤石设备区域绑定失败");
            return ApiResponse<EzvizDeviceDto>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 关闭设备视频加密（device/encrypt/off）。
    /// 设备侧配置变更，须在用户显式确认后调用；加密设备改用 HLS(protocol=2) 取流前使用，
    /// 避免萤石 HLS 不支持加密传输导致的 60019。已关闭（60016）视为幂等成功。
    /// </summary>
    [HttpPost("devices/{deviceSerial}/encrypt/off")]
    [PermissionAuthorize(Permissions.UPDATE_DEVICES)]
    public async Task<ActionResult<ApiResponse<object>>> CloseEncryption(
        string deviceSerial, CancellationToken ct = default)
    {
        try
        {
            await EnsureCameraAsync(deviceSerial, ct);
            await _ezviz.CloseEncryptionAsync(deviceSerial, User, ct);
            return ApiResponse<object>.Success(new { ok = true }, "设备视频加密已关闭");
        }
        catch (ForbiddenAccessException ex) { return ApiResponse<object>.Forbidden(ex.Message); }
        catch (UnauthorizedAccessException ex) { return ApiResponse<object>.Unauthorized(ex.Message); }
        catch (EzvizException ex) { return Fail<object>(ex); }
        catch (Exception ex) { return ApiResponse<object>.Error(ex.Message); }
    }

    /// <summary>
    /// 远程切换设备视频编码（v3 /api/v3/device/video/encodeType）。
    /// 设备侧配置变更，须在用户显式确认后调用；编码非 H264 的摄像头切为 H264 后浏览器即可播放。
    /// </summary>
    [HttpPost("devices/{deviceSerial}/encode-type")]
    [PermissionAuthorize(Permissions.UPDATE_DEVICES)]
    public async Task<ActionResult<ApiResponse<object>>> SetEncodeType(
        string deviceSerial, [FromBody] SetEncodeTypeRequest request, CancellationToken ct = default)
    {
        try
        {
            await EnsureCameraAsync(deviceSerial, ct);
            await _ezviz.SetVideoEncodeAsync(deviceSerial, request.EncodeType, request.ChannelNo, User, ct);
            return ApiResponse<object>.Success(new { ok = true }, $"设备视频编码已切换为 {request.EncodeType}");
        }
        catch (ForbiddenAccessException ex) { return ApiResponse<object>.Forbidden(ex.Message); }
        catch (UnauthorizedAccessException ex) { return ApiResponse<object>.Unauthorized(ex.Message); }
        catch (EzvizException ex) { return Fail<object>(ex); }
        catch (Exception ex) { return ApiResponse<object>.Error(ex.Message); }
    }

    /// <summary>查询设备当前视频编码（v3 /api/v3/das/device/video/encode）：1=H264，5=H265</summary>
    [HttpGet("devices/{deviceSerial}/encode-type")]
    public async Task<ActionResult<ApiResponse<int?>>> GetEncodeType(
        string deviceSerial, [FromQuery] int channelNo = 1, CancellationToken ct = default)
    {
        try
        {
            await EnsureCameraAsync(deviceSerial, ct);
            var result = await _ezviz.GetVideoEncodeAsync(deviceSerial, channelNo, User, ct);
            return ApiResponse<int?>.Success(result);
        }
        catch (ForbiddenAccessException ex) { return ApiResponse<int?>.Forbidden(ex.Message); }
        catch (UnauthorizedAccessException ex) { return ApiResponse<int?>.Unauthorized(ex.Message); }
        catch (EzvizException ex) { return Fail<int?>(ex); }
        catch (Exception ex) { return ApiResponse<int?>.Error(ex.Message); }
    }

    /// <summary>
    /// 解析绑定目标 AppCode：区域归属点查 + Guard 判定；无区域时仅允许授权内已绑定摄像头改信息。
    /// </summary>
    private async Task<string?> ResolveBindAppCodeAsync(EzvizBindCameraRequest request, CancellationToken ct)
    {
        if (request.AreaId.HasValue)
        {
            await _deviceGuard.EnsureAreaAccessAsync(User, request.AreaId.Value, ct);
            var appCode = await _db.Areas
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(a => a.Id == request.AreaId.Value)
                .Select(a => a.AppCode)
                .FirstOrDefaultAsync(ct);

            if (!string.IsNullOrWhiteSpace(appCode)) return appCode;
        }
        else
        {
            await _ezvizGuard.EnsureCameraAccessAsync(User, request.DeviceSerial, ct);
        }

        // 区域无 AppCode 的存量数据兜底：仅授权集合唯一时才能安全派生。
        var scope = await _scope.ResolveAsync(User, ct);
        if (scope.Mode == CustomerScopeMode.All) return null;
        if (scope.AppCodes.Count == 1) return scope.AppCodes[0];
        throw new ForbiddenAccessException("目标区域缺少租户归属，禁止绑定");
    }
}
