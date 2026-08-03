using IoTPlatform.Configuration;
using IoTPlatform.Data;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Filters;
using IoTPlatform.Helpers;
using IoTPlatform.Infrastructure.Protocol.AnSheng;
using IoTPlatform.Models;
using IoTPlatform.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace IoTPlatform.Controllers;

/// <summary>
/// 安圣 MQTT 设备管理控制器
///
/// 提供安圣设备的发现、认领、自动上报配置、命令下发等 API
/// </summary>
[ApiController]
[Route("api/v1/ansheng")]
[PermissionAuthorize(Permissions.VIEW_DEVICES)]
public class AnShengController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IAnShengCommandService _commandService;
    private readonly IAnShengDiscoveryService _discoveryService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AnShengController> _logger;

    /// <summary>
    /// 构造函数。
    /// </summary>
    /// <param name="db">数据库上下文。</param>
    /// <param name="commandService">安圣指令服务。</param>
    /// <param name="discoveryService">安圣设备发现与认领编排服务。</param>
    /// <param name="scopeFactory">作用域工厂。</param>
    /// <param name="logger">日志器。</param>
    public AnShengController(
        AppDbContext db,
        IAnShengCommandService commandService,
        IAnShengDiscoveryService discoveryService,
        IServiceScopeFactory scopeFactory,
        ILogger<AnShengController> logger)
    {
        _db = db;
        _commandService = commandService;
        _discoveryService = discoveryService;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    // ─────────────────────────────────────────────
    // 设备发现
    // ─────────────────────────────────────────────

    /// <summary>
    /// 触发设备发现扫描（向所有未认领设备发送 getDevInfo 命令）
    /// </summary>
    [HttpPost("discover")]
    [PermissionAuthorize(Permissions.CREATE_DEVICES)]
    public async Task<ActionResult<ApiResponse>> TriggerDiscovery()
    {
        try
        {
            await _commandService.TriggerDiscoveryAsync();
            return ApiResponse.Success("设备发现扫描已触发");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "触发设备发现扫描失败");
            return ApiResponse.Error($"触发失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 获取待认领设备列表（分页）
    /// </summary>
    [HttpGet("discovered")]
    public async Task<ActionResult<ApiResponse<DiscoveredDeviceListResponse>>> GetDiscoveredDevices(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? keyword = null,
        [FromQuery] bool? isClaimed = null)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;

            var query = _db.Set<DiscoveredAnShengDevice>().AsQueryable();

            // 租户隔离
            if (!string.IsNullOrEmpty(appCode))
            {
                query = query.Where(d => d.AppCode == null || d.AppCode == appCode);
            }

            // 认领状态筛选
            if (isClaimed.HasValue)
            {
                query = query.Where(d => d.IsClaimed == isClaimed.Value);
            }

            // 关键词搜索（IMEI / 型号）
            if (!string.IsNullOrEmpty(keyword))
            {
                query = query.Where(d =>
                    d.Imei.Contains(keyword) ||
                    (d.Model != null && d.Model.Contains(keyword)));
            }

            // 排序
            query = query.OrderByDescending(d => d.LastSeenAt ?? d.DiscoveredAt);

            var total = await query.CountAsync();

            // 先取实体再在内存里投影：SuggestedKind 要调 InferKind 静态方法，
            // 放在 Select 里 EF 无法翻译成 SQL，会直接抛 translation 异常。
            var entities = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .AsNoTracking()
                .ToListAsync();

            var items = entities.Select(d =>
            {
                // 已探明品类优先；尚未探测的记录给出推断值，供认领弹窗做默认选中。
                var suggested = d.Kind != AnShengDeviceKind.Unknown
                    ? d.Kind
                    : AnShengDeviceKindResolver.InferKind(d.NetType, d.SlotAmount, d.Version, d.Model);

                return new DiscoveredAnShengDeviceDto
                {
                    Id = d.Id,
                    Imei = d.Imei,
                    Model = d.Model,
                    NetType = d.NetType,
                    DiscoveredAt = d.DiscoveredAt,
                    LastSeenAt = d.LastSeenAt,
                    IsClaimed = d.IsClaimed,
                    ClaimedDeviceId = d.ClaimedDeviceId,
                    Kind = d.Kind,
                    KindName = d.Kind.ToDisplayName(),
                    SuggestedKind = suggested,
                    SuggestedKindName = suggested.ToDisplayName(),
                    SlotAmount = d.SlotAmount,
                    Version = d.Version,
                    Iccid = d.Iccid,
                    ProbeStatus = d.ProbeStatus,
                    ProbeError = d.ProbeError,
                    LastProbedAt = d.LastProbedAt
                };
            }).ToList();

            var result = new DiscoveredDeviceListResponse
            {
                Items = items,
                Total = total,
                Page = page,
                PageSize = pageSize
            };

            return ApiResponse<DiscoveredDeviceListResponse>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取待认领设备列表失败");
            return ApiResponse<DiscoveredDeviceListResponse>.Error(ex.Message);
        }
    }

    // ─────────────────────────────────────────────
    // 设备认领
    // ─────────────────────────────────────────────

    /// <summary>
    /// 认领发现的安圣设备：探测设备能力 → 判定品类 → 建档 → 转为正式 Device。
    ///
    /// 【本方法只做三件事】DTO 校验 → 调 <see cref="IAnShengDiscoveryService.ClaimAsync"/> → 结果映射。
    /// 编排逻辑全部下沉到服务层，Controller 不碰事务、不碰探测、不碰品类判定。
    ///
    /// 【HTTP 语义】状态码恒为 200；业务成败看响应体 <c>Code</c>，
    /// 失败原因看 <c>Data.ErrorCode</c>（见 <see cref="AnShengClaimErrorCodes"/>）。
    /// </summary>
    /// <param name="request">认领请求。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>认领结果。</returns>
    [HttpPost("claim")]
    [PermissionAuthorize(Permissions.CREATE_DEVICES)]
    public async Task<ActionResult<ApiResponse<ClaimAnShengDeviceResponse>>> ClaimDevice(
        [FromBody] ClaimAnShengDeviceRequest request,
        CancellationToken ct)
    {
        // ── DTO 级校验：这两条不需要查库就能判定，先挡掉，省一次数据库往返 ──
        if (request.DiscoveredDeviceId is null && string.IsNullOrWhiteSpace(request.Imei))
        {
            return ApiResponse<ClaimAnShengDeviceResponse>.BadRequest(
                "DiscoveredDeviceId 与 Imei 必须提供其一",
                new ClaimAnShengDeviceResponse
                {
                    Success = false,
                    ErrorCode = AnShengClaimErrorCodes.DiscoveredNotFound,
                    ErrorMessage = "DiscoveredDeviceId 与 Imei 必须提供其一"
                });
        }

        if (request.Kind == AnShengDeviceKind.Unknown)
        {
            return ApiResponse<ClaimAnShengDeviceResponse>.BadRequest(
                "必须显式指定设备品类 Kind",
                new ClaimAnShengDeviceResponse
                {
                    Success = false,
                    ErrorCode = AnShengClaimErrorCodes.KindRequired,
                    ErrorMessage = "必须显式指定设备品类 Kind"
                });
        }

        try
        {
            var result = await _discoveryService.ClaimAsync(new AnShengClaimCommand
            {
                DiscoveredDeviceId = request.DiscoveredDeviceId,
                Imei = request.Imei,
                Name = request.Name,
                Kind = request.Kind,
                AreaId = request.AreaId,
                ProjectId = request.ProjectId,
                ProtocolConfigId = request.ProtocolConfigId,
                GetDevStatusSec = request.GetDevStatusSec,
                GetDevStatusQ = request.GetDevStatusQ,
                AppCode = User.FindFirst("AppCode")?.Value ?? string.Empty
            }, ct);

            var payload = new ClaimAnShengDeviceResponse
            {
                Success = result.Success,
                DeviceId = result.DeviceId,
                DeviceName = result.DeviceName,
                ErrorCode = result.ErrorCode,
                ErrorMessage = result.ErrorMessage,
                Kind = result.Kind,
                KindName = result.Kind.ToDisplayName(),
                ProfileId = result.ProfileId,
                ProbeStatus = result.ProbeStatus
            };

            if (result.Success)
            {
                return ApiResponse<ClaimAnShengDeviceResponse>.Success(payload, "设备认领成功");
            }

            // 错误码 → ApiResponse.Code 的映射见设计 §8.3。
            return MapClaimFailure(payload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "认领安圣设备失败: DiscoveredDeviceId={Id}, Imei={Imei}",
                request.DiscoveredDeviceId, request.Imei);

            return ApiResponse<ClaimAnShengDeviceResponse>.Error($"认领失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 把认领失败结果映射为带正确 <c>Code</c> 的响应体。
    /// </summary>
    /// <param name="payload">已填好错误码的响应体。</param>
    /// <returns>失败响应。</returns>
    private static ActionResult<ApiResponse<ClaimAnShengDeviceResponse>> MapClaimFailure(
        ClaimAnShengDeviceResponse payload)
    {
        var message = payload.ErrorMessage ?? "认领失败";

        return payload.ErrorCode switch
        {
            AnShengClaimErrorCodes.DiscoveredNotFound =>
                ApiResponse<ClaimAnShengDeviceResponse>.NotFound(message, payload),

            AnShengClaimErrorCodes.PersistFailed =>
                ApiResponse<ClaimAnShengDeviceResponse>.Error(message, payload),

            // ApiResponse 没有 409 工厂，按设计 §8.3 直填 Code。
            AnShengClaimErrorCodes.ProbeConflict =>
                ApiResponse<ClaimAnShengDeviceResponse>.Fail(409, message, payload),

            _ => ApiResponse<ClaimAnShengDeviceResponse>.BadRequest(message, payload)
        };
    }

    // ─────────────────────────────────────────────
    // 自动上报配置
    // ─────────────────────────────────────────────

    /// <summary>
    /// 配置并下发安圣设备自动上报参数
    /// </summary>
    [HttpPost("{deviceId:long}/auto-report")]
    [PermissionAuthorize(Permissions.UPDATE_DEVICES)]
    public async Task<ActionResult<ApiResponse<AnShengCommandResponse>>> ConfigureAutoReport(
        long deviceId,
        [FromBody] AnShengAutoReportRequest request)
    {
        try
        {
            var settings = new AnShengAutoReportSettings
            {
                GetDevStatusSec = request.GetDevStatusSec ?? 60,
                GetDevStatusQ = request.GetDevStatusQ,
                OrderUpSec = request.OrderUpSec ?? 300,
                Rs485Sec = request.Rs485Sec ?? 0
            };

            // 持久化到 AnShengDeviceConfig
            var config = await _db.Set<AnShengDeviceConfig>()
                .FirstOrDefaultAsync(c => c.DeviceId == deviceId);

            if (config == null)
            {
                var device = await _db.Devices.FindAsync(deviceId);
                if (device == null)
                {
                    return ApiResponse<AnShengCommandResponse>.NotFound("设备不存在");
                }

                config = new AnShengDeviceConfig
                {
                    DeviceId = deviceId,
                    AppCode = device.AppCode,
                    Imei = device.SerialNumber ?? string.Empty,
                    GetDevStatusSec = settings.GetDevStatusSec,
                    GetDevStatusQ = settings.GetDevStatusQ,
                    OrderUpSec = settings.OrderUpSec,
                    Rs485Sec = settings.Rs485Sec
                };
                _db.Set<AnShengDeviceConfig>().Add(config);
            }
            else
            {
                config.GetDevStatusSec = settings.GetDevStatusSec;
                config.GetDevStatusQ = settings.GetDevStatusQ;
                config.OrderUpSec = settings.OrderUpSec;
                config.Rs485Sec = settings.Rs485Sec;
                config.UpdatedAt = DateTime.UtcNow;
                _db.Set<AnShengDeviceConfig>().Update(config);
            }

            await _db.SaveChangesAsync();

            // 下发 setAutoReport 到设备
            var result = await _commandService.ConfigureAutoReportAsync(deviceId, settings);

            _logger.LogInformation(
                "安圣自动上报已配置: DeviceId={DeviceId}, Secs=({GetDevStatus},{OrderUp},{Rs485})",
                deviceId, settings.GetDevStatusSec, settings.OrderUpSec, settings.Rs485Sec);

            return ApiResponse<AnShengCommandResponse>.Success(result, "自动上报配置已下发");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "配置安圣自动上报失败: DeviceId={DeviceId}", deviceId);
            return ApiResponse<AnShengCommandResponse>.Error($"配置失败：{ex.Message}");
        }
    }

    // ─────────────────────────────────────────────
    // 命令下发
    // ─────────────────────────────────────────────

    /// <summary>
    /// 向指定安圣设备下发原生命令
    /// </summary>
    [HttpPost("{deviceId:long}/command")]
    [PermissionAuthorize(Permissions.UPDATE_DEVICES)]
    public async Task<ActionResult<ApiResponse<AnShengCommandResponse>>> SendCommand(
        long deviceId,
        [FromBody] AnShengCommandRequest request)
    {
        try
        {
            var result = await _commandService.SendCommandAsync(
                deviceId, request.Method, request.Parameters);

            if (!result.Success)
            {
                return ApiResponse<AnShengCommandResponse>.BadRequest(result.ErrorMessage ?? "命令下发失败");
            }

            return ApiResponse<AnShengCommandResponse>.Success(result, $"命令 {request.Method} 已下发");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "下发安圣命令失败: DeviceId={DeviceId}, Method={Method}", deviceId, request.Method);
            return ApiResponse<AnShengCommandResponse>.Error($"命令下发失败：{ex.Message}");
        }
    }

    // ─────────────────────────────────────────────
    // 二开设备通用控制
    //
    // 说明：原 /switch、/switch-status、/switch-config 三个端点依赖官方协议
    //      asopen.md 中并不存在的 setSwitch / getSwitchStatus / setSwitchConfig 方法，
    //      属历史臆造实现，已物理删除。开关通断请改用：
    //        POST /command  { "method": "action",  "parameters": { "slotNum": 1, "action": "on" } }
    //        POST /command  { "method": "actions", "parameters": { "slotNums": [1,2], "action": "off" } }
    //      状态查询请用 { "method": "getDevStatus", "parameters": { "q": "slots" } }。
    // ─────────────────────────────────────────────

    /// <summary>
    /// 远程重启二开设备
    /// </summary>
    [HttpPost("{deviceId:long}/reboot")]
    [PermissionAuthorize(Permissions.SEND_DEVICE_COMMANDS)]
    public async Task<ActionResult<ApiResponse<AnShengCommandResponse>>> RebootDevice(long deviceId)
    {
        try
        {
            var result = await _commandService.RebootDeviceAsync(deviceId);
            return result.Success
                ? ApiResponse<AnShengCommandResponse>.Success(result, "重启命令已下发")
                : ApiResponse<AnShengCommandResponse>.BadRequest(result.ErrorMessage ?? "重启失败");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "重启设备失败: DeviceId={DeviceId}", deviceId);
            return ApiResponse<AnShengCommandResponse>.Error($"重启失败：{ex.Message}");
        }
    }
}
