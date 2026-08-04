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
    private readonly IAnShengDeviceProfileService _profileService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AnShengController> _logger;

    /// <summary>
    /// 构造函数。
    /// </summary>
    /// <param name="db">数据库上下文。</param>
    /// <param name="commandService">安圣指令服务。</param>
    /// <param name="discoveryService">安圣设备发现与认领编排服务。</param>
    /// <param name="profileService">安圣设备能力档案服务。</param>
    /// <param name="scopeFactory">作用域工厂。</param>
    /// <param name="logger">日志器。</param>
    public AnShengController(
        AppDbContext db,
        IAnShengCommandService commandService,
        IAnShengDiscoveryService discoveryService,
        IAnShengDeviceProfileService profileService,
        IServiceScopeFactory scopeFactory,
        ILogger<AnShengController> logger)
    {
        _db = db;
        _commandService = commandService;
        _discoveryService = discoveryService;
        _profileService = profileService;
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
                return ApiResponse<AnShengCommandResponse>.BadRequest(result.ErrorMessage ?? "命令下发失败", result);
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
                : ApiResponse<AnShengCommandResponse>.BadRequest(result.ErrorMessage ?? "重启失败", result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "重启设备失败: DeviceId={DeviceId}", deviceId);
            return ApiResponse<AnShengCommandResponse>.Error($"重启失败：{ex.Message}");
        }
    }

    // ─────────────────────────────────────────────
    // 只读查询 API（T7-5）
    //
    // 设计约定（设计文档 §7 T7-5）：
    //   · GET /catalog                      —— 默认返回全部 36 条静态规格；?deviceId= 是按品类计算的过滤视图。
    //   · GET /{deviceId}/profile           —— 设备能力档案（存量无档案时返回 404）。
    //   · GET /commands/{commandId}          —— 命令全生命周期记录，RequestJson/ResponseJson 二次掩码，永不明文口令。
    // ─────────────────────────────────────────────

    /// <summary>
    /// 获取安圣二开协议命令目录。
    ///
    /// 【默认视图】不传 <paramref name="deviceId"/> 时返回<b>全部 36 条静态规格</b>，不依赖任何设备档案
    /// （设计 §8-B：目录必须是纯静态数据，否则无设备查询就无法序列化）。
    ///
    /// 【过滤视图】传 <paramref name="deviceId"/> 时按该设备品类计算<b>过滤视图</b>：
    /// 返回条数 ≤ 36，且每条的 <c>supportedKinds</c> 都包含该设备品类。
    /// 设备无档案（存量未回填）时按 Unknown 处理，退化为「返回全部」。
    /// </summary>
    /// <param name="deviceId">可选设备主键，用于按品类过滤。</param>
    /// <returns>命令规格列表。</returns>
    [HttpGet("catalog")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AnShengCommandSpecDto>>>> GetCatalog(
        [FromQuery] long? deviceId = null)
    {
        try
        {
            IReadOnlyList<AnShengCommandSpecDto> items;

            if (deviceId is > 0)
            {
                // 过滤视图：按设备品类计算，条数只可能 ≤ 全量。
                var profile = await _profileService.GetByDeviceIdAsync(deviceId.Value, HttpContext.RequestAborted);
                var kind = profile?.Kind ?? AnShengDeviceKind.Unknown;
                items = AnShengCommandCatalog.ListFor(kind, includeEvents: true, includeBeta: true)
                    .Select(MapSpec)
                    .ToList();
            }
            else
            {
                // 默认视图：纯静态数据，与任何设备档案无关。
                items = AnShengCommandCatalog.ListAll()
                    .Select(MapSpec)
                    .ToList();
            }

            return ApiResponse<IReadOnlyList<AnShengCommandSpecDto>>.Success(items);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取安圣命令目录失败");
            return ApiResponse<IReadOnlyList<AnShengCommandSpecDto>>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 获取指定安圣设备的能⼒档案（品类 / 固件 / 插槽数等）。
    ///
    /// 【null 容忍】存量设备可能无档案，返回 404 + 业务错误码，不抛异常（产品决策 Q5）。
    /// </summary>
    /// <param name="deviceId">设备主键。</param>
    /// <returns>设备能力档案。</returns>
    [HttpGet("{deviceId:long}/profile")]
    public async Task<ActionResult<ApiResponse<AnShengDeviceProfileDto>>> GetProfile(long deviceId)
    {
        try
        {
            var profile = await _profileService.GetByDeviceIdAsync(deviceId, HttpContext.RequestAborted);
            if (profile is null)
            {
                return ApiResponse<AnShengDeviceProfileDto>.NotFound("未找到该设备的安圣能力档案");
            }

            return ApiResponse<AnShengDeviceProfileDto>.Success(MapProfile(profile));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取安圣设备档案失败: DeviceId={DeviceId}", deviceId);
            return ApiResponse<AnShengDeviceProfileDto>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 按平台命令标识查询单条命令的下发/应答全生命周期记录。
    ///
    /// 【安全红线】<c>RequestJson</c> / <c>ResponseJson</c> 在落库时已掩码，此处再经
    /// <see cref="AnShengSecretMasker"/> 二次掩码，<b>永不</b>返回明文口令（T7 决策 D3）。
    /// </summary>
    /// <param name="commandId">平台命令标识（GUID，与 <c>DeviceCommand.CommandId</c> 同值）。</param>
    /// <returns>命令记录。</returns>
    [HttpGet("commands/{commandId}")]
    public async Task<ActionResult<ApiResponse<AnShengCommandRecordDto>>> GetCommandRecord(string commandId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(commandId))
            {
                return ApiResponse<AnShengCommandRecordDto>.BadRequest("commandId 不能为空");
            }

            var record = await _commandService.GetRecordAsync(commandId, HttpContext.RequestAborted);
            if (record is null)
            {
                return ApiResponse<AnShengCommandRecordDto>.NotFound("未找到该命令记录");
            }

            return ApiResponse<AnShengCommandRecordDto>.Success(MapRecord(record));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询安圣命令记录失败: CommandId={CommandId}", commandId);
            return ApiResponse<AnShengCommandRecordDto>.Error(ex.Message);
        }
    }

    // ── 映射辅助 ──

    private static readonly AnShengDeviceKind[] ConcreteKinds =
    {
        AnShengDeviceKind.Speaker4G,
        AnShengDeviceKind.Switch4G,
        AnShengDeviceKind.SpeakerWiFi,
        AnShengDeviceKind.SwitchWiFi
    };

    private static AnShengCommandSpecDto MapSpec(AnShengCommandSpec spec)
    {
        return new AnShengCommandSpecDto
        {
            Method = spec.Method,
            Group = DeriveGroupLabel(spec.SupportedKinds),
            Description = spec.Title,
            IsEvent = spec.IsEvent,
            IsBeta = spec.IsBeta,
            MinFirmware = spec.MinFirmware,
            SupportedKinds = SupportedKindNames(spec.SupportedKinds),
            Params = spec.Params.Select(MapParam).ToList()
        };
    }

    private static AnShengParamSpecDto MapParam(AnShengParamSpec p)
    {
        return new AnShengParamSpecDto
        {
            Name = p.Name,
            Type = p.Type.ToString(),
            Required = p.Required,
            MinFirmware = p.MinFirmware,
            AllowedValues = p.AllowedValues?.ToList(),
            Minimum = p.Minimum,
            Maximum = p.Maximum,
            IsSecret = p.IsSecret
        };
    }

    /// <summary>
    /// 把能力位掩码展开成「具体品类名称」数组，便于前端按设备过滤与渲染。
    /// 例如 G3 开关组 → ["Switch4G", "SwitchWiFi"]。
    /// </summary>
    private static IReadOnlyList<string> SupportedKindNames(AnShengDeviceCapability capability)
    {
        var names = new List<string>(ConcreteKinds.Length);
        foreach (var kind in ConcreteKinds)
        {
            if ((capability & kind.ToCapability()) != AnShengDeviceCapability.None)
            {
                names.Add(kind.ToString());
            }
        }

        return names;
    }

    /// <summary>
    /// 由能力位掩码推导人类可读的能力分组标签。
    /// 目录每条规格的能力位是单一组常量（GroupCommon/GroupMqtt/GroupSwitchAction/GroupTimeTask/Group4GOnly），
    /// 用精确相等判定，避免 All 被误判成多个组。
    /// </summary>
    private static string DeriveGroupLabel(AnShengDeviceCapability capability)
    {
        if (capability == AnShengDeviceCapability.All) return "通用命令";
        if (capability == AnShengDeviceCapability.GroupSwitchAction) return "开关动作/延时/电量实时/校准";
        if (capability == AnShengDeviceCapability.GroupTimeTask) return "定时任务/电量统计/日志/RS485";
        if (capability == AnShengDeviceCapability.Group4GOnly) return "对时/物联卡预警";
        return "其他";
    }

    private static AnShengDeviceProfileDto MapProfile(AnShengDeviceProfile p)
    {
        return new AnShengDeviceProfileDto
        {
            Id = p.Id,
            Imei = p.Imei,
            DeviceId = p.DeviceId,
            Kind = p.Kind,
            KindName = p.Kind.ToDisplayName(),
            KindSource = p.KindSource,
            NetType = p.NetType,
            SlotAmount = p.SlotAmount,
            PhaseAmount = p.PhaseAmount,
            Version = p.Version,
            Model = p.Model,
            Iccid = p.Iccid,
            Signal = p.Signal,
            ProbeStatus = p.ProbeStatus,
            ProbeError = p.ProbeError,
            LastProbedAt = p.LastProbedAt
        };
    }

    private static AnShengCommandRecordDto MapRecord(AnShengCommandRecord r)
    {
        // 二次掩码：即便存量记录是 T7 之前明文落库的，也绝不外泄口令（T7 决策 D3）。
        // 已掩码的内容再次掩码是幂等的（字段名命中 → 仍替换为 "***"），不会破坏排障信息。
        var secretNames = AnShengSecretMasker.SecretFieldNames(r.Method);
        return new AnShengCommandRecordDto
        {
            CommandId = r.CommandId,
            DeviceId = r.DeviceId,
            Imei = r.Imei,
            Method = r.Method,
            FrameId = r.FrameId,
            Status = r.Status,
            RejectReason = r.RejectReason,
            RequestJson = AnShengSecretMasker.MaskJson(r.RequestJson, secretNames),
            ResponseJson = AnShengSecretMasker.MaskJson(r.ResponseJson, secretNames),
            ErrorCode = r.ErrorCode,
            ErrorMessage = r.ErrorMessage,
            IssuedAt = r.IssuedAt,
            SentAt = r.SentAt,
            CompletedAt = r.CompletedAt,
            TimeoutAt = r.TimeoutAt,
            DurationMs = r.DurationMs
        };
    }
}
