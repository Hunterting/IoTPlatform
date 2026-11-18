using IoTPlatform.Configuration;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Filters;
using IoTPlatform.Helpers;
using IoTPlatform.Services;
using Microsoft.AspNetCore.Mvc;

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
/// </summary>
[ApiController]
[Route("api/v1/ezviz")]
[PermissionAuthorize(Permissions.VIEW_MONITORING)]
public class EzvizController : ControllerBase
{
    private readonly IEzvizService _ezviz;
    private readonly ILogger<EzvizController> _logger;

    public EzvizController(IEzvizService ezviz, ILogger<EzvizController> logger)
    {
        _ezviz = ezviz;
        _logger = logger;
    }

    private string? AppCode => User.FindFirst("AppCode")?.Value;

    private ActionResult<ApiResponse<T>> Fail<T>(EzvizException ex)
    {
        var status = ex.EzvizCode switch
        {
            "NOT_CONFIGURED" => 503,
            "40301" => 403,
            _ => 502,
        };
        return ApiResponse<T>.Fail(status, ex.Message);
    }

    /// <summary>萤石配置状态（前端首屏调用，决定是否展示真实画面）</summary>
    [HttpGet("config/status")]
    public ActionResult<ApiResponse<EzvizConfigStatusDto>> GetConfigStatus()
        => ApiResponse<EzvizConfigStatusDto>.Success(_ezviz.GetConfigStatus());

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
            var result = await _ezviz.GetDevicesAsync(pageStart, pageSize, AppCode, includeUnbound, ct);
            return ApiResponse<EzvizPagedResult<EzvizDeviceDto>>.Success(result);
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
            var result = await _ezviz.GetCamerasAsync(deviceSerial, ct);
            return ApiResponse<List<EzvizCameraDto>>.Success(result);
        }
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
            var result = await _ezviz.GetLiveAddressAsync(request, ct);
            return ApiResponse<EzvizLiveAddressDto>.Success(result);
        }
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
            var result = await _ezviz.CaptureAsync(request, ct);
            return ApiResponse<EzvizCaptureDto>.Success(result);
        }
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
            var result = await _ezviz.GetPlaybackAsync(request, ct);
            return ApiResponse<List<EzvizPlaybackRecordDto>>.Success(result);
        }
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
            await _ezviz.PtzStartAsync(request, ct);
            return ApiResponse<object>.Success(new { ok = true }, "云台指令已下发");
        }
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
            await _ezviz.PtzStopAsync(request, ct);
            return ApiResponse<object>.Success(new { ok = true }, "云台已停止");
        }
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
            var result = await _ezviz.GetDeviceStatusAsync(deviceSerial, ct);
            return ApiResponse<EzvizDeviceStatusDto>.Success(result);
        }
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
            var result = await _ezviz.BindCameraAsync(request, AppCode, ct);
            return ApiResponse<EzvizDeviceDto>.Success(result, "摄像头已绑定区域");
        }
        catch (EzvizException ex) { return Fail<EzvizDeviceDto>(ex); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "萤石设备区域绑定失败");
            return ApiResponse<EzvizDeviceDto>.Error(ex.Message);
        }
    }
}
