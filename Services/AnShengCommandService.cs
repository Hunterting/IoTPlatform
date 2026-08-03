using System.Collections.Concurrent;
using System.Text.Json;
using IoTPlatform.Data;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Infrastructure.Protocol;
using IoTPlatform.Infrastructure.Protocol.AnSheng;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IoTPlatform.Services;

/// <summary>
/// 安圣 MQTT 设备命令服务实现
///
/// 职责：
///   1. 通过 IProtocolAdapterFactory 获取安圣适配器
///   2. 依据 AnShengCommandCatalog 校验方法名、品类能力与参数
///   3. 调用适配器 SendCommandAsync 下发到 /iot/client/iot-board/{imei}
///   4. 维护 frameId ↔ commandId 映射关系，用于后续命令响应关联
/// </summary>
public class AnShengCommandService : IAnShengCommandService
{
    private readonly AppDbContext _db;
    private readonly IProtocolAdapterFactory _adapterFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AnShengCommandService> _logger;

    /// <summary>
    /// frameId ↔ commandId 映射表（线程安全）
    /// 适配器收到响应时，按 frameId 查找 commandId，回调 UpdateCommandStatusAsync
    /// </summary>
    private static readonly ConcurrentDictionary<string, string> FrameIdCommandIdMap = new();

    public AnShengCommandService(
        AppDbContext db,
        IProtocolAdapterFactory adapterFactory,
        IServiceScopeFactory scopeFactory,
        ILogger<AnShengCommandService> logger)
    {
        _db = db;
        _adapterFactory = adapterFactory;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<AnShengCommandResponse> SendCommandAsync(
        long deviceId, string method,
        Dictionary<string, object?>? parameters = null,
        CancellationToken ct = default)
    {
        try
        {
            // 1. 查询设备获取 IMEI 和 ProtocolConfigId
            var device = await _db.Devices
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == deviceId, ct);

            if (device == null)
                return new AnShengCommandResponse
                {
                    Success = false,
                    ErrorMessage = $"设备 {deviceId} 不存在"
                };

            if (string.IsNullOrEmpty(device.SerialNumber))
                return new AnShengCommandResponse
                {
                    Success = false,
                    ErrorMessage = $"设备 {deviceId} 未设置 IMEI（SerialNumber 为空）"
                };

            if (device.ProtocolConfigId == null)
                return new AnShengCommandResponse
                {
                    Success = false,
                    ErrorMessage = $"设备 {deviceId} 未配置安圣协议（ProtocolConfigId 为空）"
                };

            // 2. 获取适配器
            var adapter = _adapterFactory.GetAdapter((int)device.ProtocolConfigId.Value);
            if (adapter == null)
                return new AnShengCommandResponse
                {
                    Success = false,
                    ErrorMessage = $"安圣适配器未运行（ConfigId={device.ProtocolConfigId}）"
                };

            if (!adapter.IsConnected)
                return new AnShengCommandResponse
                {
                    Success = false,
                    ErrorMessage = "安圣 MQTT 适配器未连接"
                };

            // 3. 解析设备品类（决定是否注入 timestamp、以及命令能力校验）
            // 注：此处使用完全限定名，避免与本命名空间下同名的 AnShengAutoReportSettings 产生 using 冲突
            var kind = Infrastructure.Protocol.Adapters.AnShengMqttProtocolAdapter
                .GetDeviceKind(device.SerialNumber);

            // 4. 目录校验：方法是否存在、该品类是否支持、参数是否合法
            if (AnShengCommandCatalog.TryGet(method, out var spec) && spec != null)
            {
                if (spec.IsEvent)
                    return new AnShengCommandResponse
                    {
                        Success = false,
                        ErrorMessage = $"{method} 是设备上报事件，平台不可下发"
                    };

                if (!spec.IsSupportedBy(kind))
                    return new AnShengCommandResponse
                    {
                        Success = false,
                        ErrorMessage = $"{kind.ToDisplayName()} 不支持命令 {method}"
                    };

                var validation = spec.ValidateParams(parameters);
                if (!validation.IsValid)
                    return new AnShengCommandResponse
                    {
                        Success = false,
                        ErrorMessage = $"参数校验失败：{string.Join("；", validation.Errors)}"
                    };
            }

            // 5. 下发命令（报文由适配器用 AnShengCommandBuilder 统一构建并限流）
            var parametersJson = parameters != null
                ? JsonSerializer.Serialize(parameters)
                : string.Empty;

            var resultFrameId = await adapter.SendCommandAsync(
                deviceId, device.SerialNumber, method, parametersJson, ct);

            // 6. 用实际下发的 frameId 重建报文回显，保证 Payload 与 FrameId 一致
            var builder = new AnShengCommandBuilder();
            var payload = AnShengCommandCatalog.Contains(method)
                ? builder.BuildRaw(device.SerialNumber, method, parameters, kind, resultFrameId).Payload
                : builder.BuildLegacyCommand(device.SerialNumber, method, parameters).Payload;

            _logger.LogInformation(
                "安圣命令已下发: DeviceId={DeviceId}, IMEI={IMEI}, Method={Method}, Kind={Kind}, FrameId={FrameId}",
                deviceId, device.SerialNumber, method, kind, resultFrameId);

            return new AnShengCommandResponse
            {
                Success = true,
                FrameId = resultFrameId,
                Payload = payload,
                SentAt = DateTime.UtcNow
            };
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "安圣命令下发失败（连接异常）: DeviceId={DeviceId}, Method={Method}", deviceId, method);
            return new AnShengCommandResponse
            {
                Success = false,
                ErrorMessage = $"适配器连接异常：{ex.Message}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "安圣命令下发失败: DeviceId={DeviceId}, Method={Method}", deviceId, method);
            return new AnShengCommandResponse
            {
                Success = false,
                ErrorMessage = $"下发失败：{ex.Message}"
            };
        }
    }

    /// <inheritdoc />
    public async Task<AnShengCommandResponse> ConfigureAutoReportAsync(
        long deviceId,
        AnShengAutoReportSettings settings,
        CancellationToken ct = default)
    {
        var parameters = new Dictionary<string, object?>
        {
            ["getDevStatusSec"] = settings.GetDevStatusSec,
            ["orderUpSec"] = settings.OrderUpSec,
            ["rs485Sec"] = settings.Rs485Sec,
            ["rs485BaudRate"] = 115200,   // 设备默认波特率
            ["rs485SendWaitMs"] = 300      // 设备默认发送等待
        };

        if (!string.IsNullOrEmpty(settings.GetDevStatusQ))
        {
            parameters["getDevStatusQ"] = settings.GetDevStatusQ;
        }

        return await SendCommandAsync(deviceId, "setAutoReport", parameters, ct);
    }

    /// <inheritdoc />
    public async Task TriggerDiscoveryAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // 1. 查询所有未认领设备
        var unclaimed = await db.Set<DiscoveredAnShengDevice>()
            .AsNoTracking()
            .Where(d => !d.IsClaimed)
            .OrderBy(d => d.DiscoveredAt)
            .Take(20)
            .ToListAsync(ct);

        if (unclaimed.Count == 0)
        {
            _logger.LogInformation("TriggerDiscoveryAsync: 无未认领设备");
            return;
        }

        // 2. 获取活跃的安圣协议配置
        var activeConfigs = await db.Set<ProtocolConfig>()
            .AsNoTracking()
            .Where(c => c.IsActive && c.ProtocolType == "ANSHENG_MQTT")
            .ToListAsync(ct);

        if (activeConfigs.Count == 0)
        {
            _logger.LogWarning("TriggerDiscoveryAsync: 无活跃的 ANSHENG_MQTT 协议配置");
            return;
        }

        _logger.LogInformation("TriggerDiscoveryAsync: 向 {Count} 个未认领设备发送 getDevInfo", unclaimed.Count);

        foreach (var device in unclaimed)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var config = activeConfigs.FirstOrDefault(c =>
                    string.IsNullOrEmpty(device.AppCode) ||
                    string.Equals(c.AppCode, device.AppCode, StringComparison.OrdinalIgnoreCase));

                if (config == null) continue;

                var adapter = _adapterFactory.GetAdapter((int)config.Id);
                if (adapter == null || !adapter.IsConnected) continue;

                await adapter.SendCommandAsync(
                    deviceId: 0L,
                    serialNumber: device.Imei,
                    commandType: "getDevInfo",
                    parameters: string.Empty,
                    cancellationToken: ct);

                _logger.LogDebug("TriggerDiscoveryAsync: 已发送 getDevInfo → IMEI={IMEI}", device.Imei);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "TriggerDiscoveryAsync: 发送 getDevInfo 失败（已跳过）IMEI={IMEI}", device.Imei);
            }
        }
    }

    // ─── 二开设备通用命令实现 ───
    // 注：setSwitch / getSwitchStatus / setSwitchConfig / getSwitchConfig 四个方法
    //     在官方协议 asopen.md 中并不存在（历史臆造实现），已物理删除。
    //     开关通断请改用 SendCommandAsync(deviceId, "action", { slotNum, action })。

    /// <inheritdoc />
    public async Task<AnShengCommandResponse> RebootDeviceAsync(
        long deviceId, CancellationToken ct = default)
        => await SendCommandAsync(deviceId, "reboot", null, ct);

    /// <summary>
    /// 注册 frameId ↔ commandId 映射
    /// 由 DeviceCommandService 在发送安圣命令后调用，以便后续响应匹配
    /// </summary>
    public static void RegisterFrameIdMapping(string frameId, string commandId)
    {
        FrameIdCommandIdMap.TryAdd(frameId, commandId);
    }

    /// <summary>
    /// 通过 frameId 查找 commandId
    /// 由适配器命令响应回调使用
    /// </summary>
    public static string? ResolveCommandId(string frameId)
    {
        FrameIdCommandIdMap.TryGetValue(frameId, out var commandId);
        return commandId;
    }

    /// <summary>
    /// 移除 frameId 映射
    /// </summary>
    public static void RemoveFrameIdMapping(string frameId)
    {
        FrameIdCommandIdMap.TryRemove(frameId, out _);
    }
}
