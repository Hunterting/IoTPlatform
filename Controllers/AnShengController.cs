using IoTPlatform.Configuration;
using IoTPlatform.Data;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Filters;
using IoTPlatform.Helpers;
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
    private readonly ILogger<AnShengController> _logger;

    public AnShengController(
        AppDbContext db,
        IAnShengCommandService commandService,
        ILogger<AnShengController> logger)
    {
        _db = db;
        _commandService = commandService;
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
            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(d => new DiscoveredAnShengDeviceDto
                {
                    Id = d.Id,
                    Imei = d.Imei,
                    Model = d.Model,
                    NetType = d.NetType,
                    DiscoveredAt = d.DiscoveredAt,
                    LastSeenAt = d.LastSeenAt,
                    IsClaimed = d.IsClaimed,
                    ClaimedDeviceId = d.ClaimedDeviceId
                })
                .ToListAsync();

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
    /// 认领发现的安圣设备：将待认领设备转为正式 Device
    /// </summary>
    [HttpPost("claim")]
    [PermissionAuthorize(Permissions.CREATE_DEVICES)]
    public async Task<ActionResult<ApiResponse<ClaimAnShengDeviceResponse>>> ClaimDevice(
        [FromBody] ClaimAnShengDeviceRequest request)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;

            // 1. 查询待认领设备
            var discovered = await _db.Set<DiscoveredAnShengDevice>()
                .FirstOrDefaultAsync(d => d.Id == request.DiscoveredDeviceId);

            if (discovered == null)
            {
                return ApiResponse<ClaimAnShengDeviceResponse>.BadRequest("待认领设备不存在");
            }

            if (discovered.IsClaimed)
            {
                return ApiResponse<ClaimAnShengDeviceResponse>.BadRequest("该设备已被认领");
            }

            // 2. 检查 IMEI 冲突（同一 AppCode 下是否已有同名 SerialNumber）
            var existingDevice = await _db.Devices
                .FirstOrDefaultAsync(d => d.SerialNumber == discovered.Imei
                    && (appCode == null || d.AppCode == appCode));

            if (existingDevice != null)
            {
                return ApiResponse<ClaimAnShengDeviceResponse>.BadRequest(
                    $"IMEI {discovered.Imei} 已存在对应设备（DeviceId={existingDevice.Id}）");
            }

            // 3. 创建正式设备
            var device = new Device
            {
                Name = request.Name,
                SerialNumber = discovered.Imei, // IMEI 存入 SerialNumber
                AppCode = appCode ?? discovered.AppCode,
                Status = "online", // 认领时视作在线
                Category = "安圣充电桩",
                ProtocolConfigId = request.ProtocolConfigId,
                AreaId = request.AreaId,
                ProjectId = request.ProjectId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _db.Devices.Add(device);
            await _db.SaveChangesAsync();

            // 4. 更新待认领池状态
            discovered.IsClaimed = true;
            discovered.ClaimedDeviceId = device.Id;
            _db.Set<DiscoveredAnShengDevice>().Update(discovered);
            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "安圣设备已认领: IMEI={IMEI}, DiscoveredId={DisId}, DeviceId={DevId}, Name={Name}",
                discovered.Imei, discovered.Id, device.Id, request.Name);

            return ApiResponse<ClaimAnShengDeviceResponse>.Success(new ClaimAnShengDeviceResponse
            {
                Success = true,
                DeviceId = device.Id,
                DeviceName = device.Name
            }, "设备认领成功");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "认领安圣设备失败: DiscoveredDeviceId={Id}", request.DiscoveredDeviceId);
            return ApiResponse<ClaimAnShengDeviceResponse>.Error($"认领失败：{ex.Message}");
        }
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
    // 二开设备开关控制
    // ─────────────────────────────────────────────

    /// <summary>
    /// 控制二开设备开关通断
    /// </summary>
    [HttpPost("{deviceId:long}/switch")]
    [PermissionAuthorize(Permissions.SEND_DEVICE_COMMANDS)]
    public async Task<ActionResult<ApiResponse<AnShengCommandResponse>>> ControlSwitch(
        long deviceId, [FromBody] SwitchControlRequest request)
    {
        try
        {
            var result = await _commandService.SendSwitchCommandAsync(deviceId, request.SwitchId, request.On);
            return result.Success
                ? ApiResponse<AnShengCommandResponse>.Success(result,
                    $"开关 {request.SwitchId} 已{(request.On ? "开启" : "关闭")}")
                : ApiResponse<AnShengCommandResponse>.BadRequest(result.ErrorMessage ?? "开关控制失败");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "开关控制失败: DeviceId={DeviceId}, SwitchId={Id}", deviceId, request.SwitchId);
            return ApiResponse<AnShengCommandResponse>.Error($"控制失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 查询二开设备开关状态
    /// </summary>
    [HttpGet("{deviceId:long}/switch-status")]
    public async Task<ActionResult<ApiResponse<AnShengCommandResponse>>> GetSwitchStatus(
        long deviceId, [FromQuery] int? switchId = null)
    {
        try
        {
            var result = await _commandService.GetSwitchStatusAsync(deviceId, switchId);
            return result.Success
                ? ApiResponse<AnShengCommandResponse>.Success(result)
                : ApiResponse<AnShengCommandResponse>.BadRequest(result.ErrorMessage ?? "查询失败");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询开关状态失败: DeviceId={DeviceId}", deviceId);
            return ApiResponse<AnShengCommandResponse>.Error($"查询失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 配置二开设备开关参数
    /// </summary>
    [HttpPost("{deviceId:long}/switch-config")]
    [PermissionAuthorize(Permissions.SEND_DEVICE_COMMANDS)]
    public async Task<ActionResult<ApiResponse<AnShengCommandResponse>>> ConfigureSwitch(
        long deviceId, [FromBody] SwitchConfigRequest request)
    {
        try
        {
            var result = await _commandService.ConfigureSwitchAsync(deviceId, request.SwitchId, request.Config);
            return result.Success
                ? ApiResponse<AnShengCommandResponse>.Success(result, "开关配置已下发")
                : ApiResponse<AnShengCommandResponse>.BadRequest(result.ErrorMessage ?? "配置失败");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "配置开关失败: DeviceId={DeviceId}", deviceId);
            return ApiResponse<AnShengCommandResponse>.Error($"配置失败：{ex.Message}");
        }
    }

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
