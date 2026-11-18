// T12：安圣水流量服务实现（查询 / 配置 / 实时回读）。
//
// 【这个类只做三件事】
//   ① 编排下发 —— 一律走 T7 的 AnShengCommandService.SendCommandAsync，
//      本类<b>不碰 MQTT、不碰 Builder、不碰 Guard、不碰在途表</b>（铁律③）；
//   ② 配置入库 —— getFlowConfig 应答按唯一键 (DeviceId, AppCode)
//      幂等 UPSERT 进 AnShengWaterFlowConfig（设备权威、平台只读镜像）；
//   ③ 实时入库 —— getFlowInfo 应答归一化成 waterflow / watertotal / wpulses 走既有 IDataCollectionService
//      落 DeviceDataRecord（零改动复用告警引擎与图表）。
//
// 【与 T11 电量计一致：没有乐观镜像】
//   水流量是「设备测、平台读」—— 平台凭空写一个流量值就是**造假数据**。
//   所以本类的所有写库动作都发生在<b>设备应答真的回来之后</b>（Router 钩子回调）。
//
// 【租户过滤器陷阱（设计 §7.1，本文件最易踩）】
//   ApplyFlowInfoReadbackAsync / ApplyFlowConfigReadbackAsync 由 AnShengMessageRouter 在
//   **后台作用域**调用，此时 ITenantContextAccessor.Current 为 null，AppDbContext 的全局查询
//   过滤器会把所有行滤成空集 —— 表现为「应答收到了、配置表却一行没写」。
//   因此这两条路径上的每一次查询都必须 IgnoreQueryFilters() + 按 DeviceId（全局唯一）显式定位，
//   新建行的 AppCode 显式取自 Devices 表。
//   反之 GetLatestFlowInfoAsync / GetFlowConfigMirrorAsync 只在 HTTP 作用域被调用，**刻意不加**
//   IgnoreQueryFilters —— 那是跨租户读的唯一防线，去掉就等于把别家租户的数据暴露出去。

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IoTPlatform.Data;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Models;
using IoTPlatform.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IoTPlatform.Services;

/// <summary>
/// 安圣水流量服务实现（T12）。
///
/// 【生命周期】Scoped —— 持有 <see cref="AppDbContext"/> 与 <see cref="IDataCollectionService"/>。
/// </summary>
public class AnShengWaterFlowService : IAnShengWaterFlowService
{
    /// <summary>安圣协议方法名：获取流量信息。</summary>
    private const string MethodGetFlowInfo = "getFlowInfo";

    /// <summary>安圣协议方法名：重置总流量。</summary>
    private const string MethodResetTotalFlow = "resetTotalFlow";

    /// <summary>安圣协议方法名：获取流量配置。</summary>
    private const string MethodGetFlowConfig = "getFlowConfig";

    /// <summary>安圣协议方法名：设置流量配置。</summary>
    private const string MethodSetFlowConfig = "setFlowConfig";

    /// <summary>安圣协议方法名：重置流量配置。</summary>
    private const string MethodResetFlowConfig = "resetFlowConfig";

    private readonly AppDbContext _db;
    private readonly IAnShengCommandService _cmd;
    private readonly IDataCollectionService _dataCollection;
    private readonly TimeProvider _time;
    private readonly ILogger<AnShengWaterFlowService> _logger;

    /// <summary>
    /// 构造水流量服务。
    /// </summary>
    /// <param name="db">数据库上下文（Scoped）。</param>
    /// <param name="cmd">命令下发服务（T7 单点入口，本服务唯一的出网通道）。</param>
    /// <param name="dataCollection">数据采集服务，实时读数经它落 <c>DeviceDataRecord</c>。</param>
    /// <param name="logger">日志。</param>
    /// <param name="timeProvider">
    /// 时间源，可选。DI 未注册 <see cref="TimeProvider"/> 时回落 <see cref="TimeProvider.System"/>；
    /// 单元测试可注入假时钟断言边界，无需真的等 24 小时。
    /// </param>
    public AnShengWaterFlowService(
        AppDbContext db,
        IAnShengCommandService cmd,
        IDataCollectionService dataCollection,
        ILogger<AnShengWaterFlowService> logger,
        TimeProvider? timeProvider = null)
    {
        _db = db;
        _cmd = cmd;
        _dataCollection = dataCollection;
        _logger = logger;
        _time = timeProvider ?? TimeProvider.System;
    }

    // ─────────────────────────────────────────────────────────────
    // 下发编排（HTTP 作用域）
    // ─────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<AnShengEnergyResultDto> RequestFlowInfoAsync(
        long deviceId, CancellationToken ct = default)
    {
        var response = await _cmd.SendCommandAsync(deviceId, MethodGetFlowInfo, null, ct);
        return BuildResult(response);
    }

    /// <inheritdoc />
    public async Task<AnShengEnergyResultDto> ResetTotalFlowAsync(
        long deviceId, CancellationToken ct = default)
    {
        var response = await _cmd.SendCommandAsync(deviceId, MethodResetTotalFlow, null, ct);
        return BuildResult(response);
    }

    /// <inheritdoc />
    public async Task<AnShengEnergyResultDto> GetFlowConfigAsync(
        long deviceId, CancellationToken ct = default)
    {
        var response = await _cmd.SendCommandAsync(deviceId, MethodGetFlowConfig, null, ct);
        return BuildResult(response);
    }

    /// <inheritdoc />
    public async Task<AnShengEnergyResultDto> SetFlowConfigAsync(
        long deviceId, AnShengWaterFlowConfigRequest? request, CancellationToken ct = default)
    {
        Dictionary<string, object?>? parameters = null;

        if (request != null)
        {
            // 仅把非 null 的参数放进报文体：协议对未给的字段沿用设备默认值（k=11 / offset=0 / 等）。
            parameters = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (request.K.HasValue)
            {
                parameters["k"] = request.K.Value;
            }

            if (request.Offset.HasValue)
            {
                parameters["offset"] = request.Offset.Value;
            }

            if (request.MinFlow.HasValue)
            {
                parameters["minFlow"] = request.MinFlow.Value;
            }

            if (request.MaxFlow.HasValue)
            {
                parameters["maxFlow"] = request.MaxFlow.Value;
            }
        }

        var response = await _cmd.SendCommandAsync(deviceId, MethodSetFlowConfig, parameters, ct);
        return BuildResult(response);
    }

    /// <inheritdoc />
    public async Task<AnShengEnergyResultDto> ResetFlowConfigAsync(
        long deviceId, CancellationToken ct = default)
    {
        var response = await _cmd.SendCommandAsync(deviceId, MethodResetFlowConfig, null, ct);
        return BuildResult(response);
    }

    // ─────────────────────────────────────────────────────────────
    // 只读查询（HTTP 作用域）
    // ─────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<AnShengWaterFlowInfoDto?> GetLatestFlowInfoAsync(
        long deviceId, CancellationToken ct = default)
    {
        // 刻意<b>不</b> IgnoreQueryFilters：本方法只服务 HTTP 作用域，
        // 全局租户过滤器是这里唯一的跨租户读防线。
        var record = await _db.DeviceDataRecords
            .AsNoTracking()
            .Where(r => r.DeviceId == deviceId)
            .OrderByDescending(r => r.Timestamp)
            .FirstOrDefaultAsync(ct);

        if (record == null)
        {
            return null;
        }

        return new AnShengWaterFlowInfoDto
        {
            IFlow = record.WaterFlow ?? 0d,
            TFlow = record.WaterTotal ?? 0d,
            TPulses = record.WaterPulses,
            Imei = null,
            Timestamp = ToUnixSeconds(record.Timestamp)
        };
    }

    /// <inheritdoc />
    public async Task<AnShengWaterFlowConfigDto?> GetFlowConfigMirrorAsync(
        long deviceId, CancellationToken ct = default)
    {
        // 刻意<b>不</b> IgnoreQueryFilters：本方法只服务 HTTP 作用域，
        // 全局租户过滤器是这里唯一的跨租户读防线。
        var config = await _db.AnShengWaterFlowConfigs
            .AsNoTracking()
            .Where(c => c.DeviceId == deviceId)
            .OrderByDescending(c => c.SyncedAt)
            .FirstOrDefaultAsync(ct);

        if (config == null)
        {
            return null;
        }

        return new AnShengWaterFlowConfigDto
        {
            K = config.K,
            Offset = config.Offset,
            MinFlow = config.MinFlow,
            MaxFlow = config.MaxFlow,
            Imei = null,
            Timestamp = ToUnixSeconds(config.SyncedAt)
        };
    }

    // ─────────────────────────────────────────────────────────────
    // 应答写回（后台作用域 —— 一律 IgnoreQueryFilters + 显式 AppCode）
    // ─────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task ApplyFlowInfoReadbackAsync(
        long deviceId, AnShengWaterFlowInfoSnapshot snapshot, CancellationToken ct = default)
    {
        var appCode = await ResolveAppCodeAsync(deviceId, ct);
        if (appCode == null)
        {
            _logger.LogWarning(
                "水流量实时数据入库失败：设备不存在或无租户码，已跳过。DeviceId={DeviceId}", deviceId);
            return;
        }

        var payload = BuildFlowInfoSensorPayload(snapshot);
        if (payload == null)
        {
            _logger.LogDebug(
                "getFlowInfo 应答无任何有效读数，跳过入库: DeviceId={DeviceId}", deviceId);
            return;
        }

        // 设备时钟漂移是已知现象，复用 T11 那套「可信区间」判定：
        // 超出 [now-24h, now+5min] 一律回退平台时间，宁可少一点精度也不要污染时间轴。
        var receivedAt = UtcNow();
        var timestamp = AnShengDeviceEvent.ResolveOccurredAt(
            ToUtcOrNull(snapshot.Timestamp), receivedAt, out var usedFallback);

        if (usedFallback && snapshot.Timestamp.HasValue)
        {
            _logger.LogDebug(
                "getFlowInfo 设备时间戳不可信，已回退平台时间: DeviceId={DeviceId}, DeviceTs={DeviceTs}",
                deviceId, snapshot.Timestamp.Value);
        }

        // 走既有数据采集通路（设计：实时零改动复用 DataRule 告警引擎与现有图表）。
        await _dataCollection.ProcessDeviceDataAsync(deviceId, appCode, payload, timestamp);

        _logger.LogDebug(
            "水流量实时数据已入 DeviceDataRecord: DeviceId={DeviceId}, AppCode={AppCode}, Ts={Ts:O}",
            deviceId, appCode, timestamp);
    }

    /// <inheritdoc />
    public async Task ApplyFlowConfigReadbackAsync(
        long deviceId, AnShengWaterFlowConfigSnapshot snapshot, CancellationToken ct = default)
    {
        var appCode = await ResolveAppCodeAsync(deviceId, ct);
        if (appCode == null)
        {
            _logger.LogWarning(
                "水流量配置入库失败：设备不存在或无租户码，已跳过。DeviceId={DeviceId}", deviceId);
            return;
        }

        // 唯一键 (DeviceId, AppCode)：后台作用域必须 IgnoreQueryFilters + 显式按 DeviceId 定位。
        var existing = await _db.AnShengWaterFlowConfigs
            .IgnoreQueryFilters()
            .Where(c => c.DeviceId == deviceId)
            .ToListAsync(ct);

        // 设备换租户（重认领）后可能有旧租户行；按当前 AppCode 精确取，保证写到正确的租户下。
        var row = existing.FirstOrDefault(c => c.AppCode == appCode);
        if (row == null)
        {
            row = new AnShengWaterFlowConfig
            {
                AppCode = appCode,
                DeviceId = deviceId
            };
            _db.AnShengWaterFlowConfigs.Add(row);
        }

        var now = UtcNow();

        // AppCode 每次都以 Devices 表为准重写：设备换租户（重认领）后数据不会留在旧租户下。
        row.AppCode = appCode;
        row.K = snapshot.K;
        row.Offset = snapshot.Offset;
        row.MinFlow = snapshot.MinFlow;
        row.MaxFlow = snapshot.MaxFlow;
        row.SyncedAt = now;

        await _db.SaveChangesAsync(ct);

        _logger.LogDebug(
            "水流量配置已 UPSERT: DeviceId={DeviceId}, AppCode={AppCode}, K={K}, Offset={Offset}, "
            + "MinFlow={MinFlow}, MaxFlow={MaxFlow}, SyncedAt={SyncedAt:O}",
            deviceId, appCode, row.K, row.Offset, row.MinFlow, row.MaxFlow, now);
    }

    // ─────────────────────────────────────────────────────────────
    // 私有工具
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 把流量信息快照拍平成 <c>DeviceDataRecord.SensorData</c> 用的 JSON。
    ///
    /// 【命名必须与 <c>DataCollectionService.SensorFieldMappings</c> 对齐】
    ///   <c>waterflow</c> → WaterFlow、<c>watertotal</c> → WaterTotal、<c>wpulses</c> → WaterPulses
    ///   （后者的映射已由本项目在 DataCollectionService 中登记）。
    ///   名字对不上就不会被识别成传感器数据点，前端也就建不出流量曲线 / 累计读数。
    /// </summary>
    /// <param name="snapshot">流量信息快照。</param>
    /// <returns>SensorData JSON；恒非 null（瞬时 / 累计流量至少有两个数值）。</returns>
    private static string? BuildFlowInfoSensorPayload(AnShengWaterFlowInfoSnapshot snapshot)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["waterflow"] = snapshot.IFlow,
            ["watertotal"] = snapshot.TFlow
        };

        if (snapshot.TPulses.HasValue)
        {
            payload["wpulses"] = snapshot.TPulses.Value;
        }

        return JsonSerializer.Serialize(payload);
    }

    /// <summary>
    /// 取设备的租户码。<c>IgnoreQueryFilters()</c> 是刚需：后台作用域下带过滤器必然查不到设备，
    /// 表现为「应答到了却一行都没写」。
    /// </summary>
    /// <param name="deviceId">设备主键。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>租户码；设备不存在或租户码为空时返回 <c>null</c>。</returns>
    private async Task<string?> ResolveAppCodeAsync(long deviceId, CancellationToken ct)
    {
        var appCode = await _db.Devices
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(d => d.Id == deviceId)
            .Select(d => d.AppCode)
            .FirstOrDefaultAsync(ct);

        return string.IsNullOrWhiteSpace(appCode) ? null : appCode;
    }

    /// <summary>当前 UTC 时刻（走 <see cref="TimeProvider"/> 以便测试注入假时钟）。</summary>
    /// <returns>UTC 时间。</returns>
    private DateTime UtcNow() => _time.GetUtcNow().UtcDateTime;

    /// <summary>把秒级 UNIX 时间戳（设备时钟，可能为 null）归一化为 UTC <see cref="DateTime"/>。</summary>
    /// <param name="unixSeconds">秒级时间戳；为 null 或未解析时返回 null。</param>
    /// <returns>UTC 时间；越界或不可解析时为 null。</returns>
    private static DateTime? ToUtcOrNull(long? unixSeconds)
    {
        if (!unixSeconds.HasValue)
        {
            return null;
        }

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(unixSeconds.Value).UtcDateTime;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>把 <see cref="DateTime"/> 转成秒级 UNIX 时间戳（用于镜像 DTO 的 timestamp 字段）。</summary>
    /// <param name="value">时间（按 UTC 解释）。</param>
    /// <returns>秒级时间戳；为默认值时返回 null。</returns>
    private static long? ToUnixSeconds(DateTime value)
    {
        if (value == default)
        {
            return null;
        }

        return (long)(value.ToUniversalTime() - DateTime.UnixEpoch).TotalSeconds;
    }

    /// <summary>把 T7 的下发响应翻译成 T12 的结果 DTO（命令端点复用 T11 的 <see cref="AnShengEnergyResultDto"/>）。</summary>
    /// <param name="response">命令下发响应。</param>
    /// <returns>水流量命令结果。</returns>
    private static AnShengEnergyResultDto BuildResult(AnShengCommandResponse response)
        => new()
        {
            Accepted = response.Success,
            CommandId = response.CommandId,
            FrameId = response.FrameId,
            RejectReason = response.RejectReason,
            ErrorMessage = response.Success ? null : response.ErrorMessage,
            Payload = response.Success ? response.Payload : null
        };
}
