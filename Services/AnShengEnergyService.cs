// T11-2：安圣电量计服务实现（实时 / 统计 / 校准）。
//
// 【这个类只做三件事】
//   ① 编排下发 —— 一律走 T7 的 AnShengCommandService.SendCommandAsync，
//      本类<b>不碰 MQTT、不碰 Builder、不碰 Guard、不碰在途表</b>（铁律③）；
//   ② 统计入库 —— getEMStatistics 应答按唯一键 (DeviceId, SlotNum, Granularity, PeriodKey)
//      幂等 UPSERT 进 AnShengEmStatistic（验收 #1/#2/#3）；
//   ③ 实时入库 —— getEMRealtime 应答归一化成 slot{n}_* 走既有 IDataCollectionService
//      落 DeviceDataRecord（验收 #5），复用现成的告警引擎与图表，零改动。
//
// 【与 T8/T10 最大的不同：没有乐观镜像】
//   延时 / 定时任务是「平台写、设备执行」，可以先落一份乐观值让前端有东西看；
//   电量计是「设备测、平台读」—— 平台凭空写一个电量值就是**造假数据**。
//   所以本类的所有写库动作都发生在<b>设备应答真的回来之后</b>（Router 钩子回调）。
//
// 【租户过滤器陷阱（设计 §7.1，本文件最易踩）】
//   ApplyStatisticsReadbackAsync / ApplyRealtimeReadbackAsync 由 AnShengMessageRouter 在
//   **后台作用域**调用，此时 ITenantContextAccessor.Current 为 null，AppDbContext 的全局查询
//   过滤器会把所有行滤成空集 —— 表现为「应答收到了、聚合表却一行没写」。
//   因此这两条路径上的每一次查询都必须 IgnoreQueryFilters() + 按 DeviceId（全局唯一）显式定位，
//   新建行的 AppCode 显式取自 Devices 表。
//   反之 QueryStatisticsAsync 只在 HTTP 作用域被调用，**刻意不加** IgnoreQueryFilters ——
//   那是跨租户读的唯一防线，去掉就等于把别家租户的用电数据暴露出去。

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IoTPlatform.Configuration;
using IoTPlatform.Data;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Models;
using IoTPlatform.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IoTPlatform.Services;

/// <summary>
/// 安圣电量计服务实现（T11）。
///
/// 【生命周期】Scoped —— 持有 <see cref="AppDbContext"/> 与 <see cref="IDataCollectionService"/>。
/// </summary>
public class AnShengEnergyService : IAnShengEnergyService
{
    /// <summary>安圣协议方法名：获取电量计实时信息。</summary>
    private const string MethodGetEMRealtime = "getEMRealtime";

    /// <summary>安圣协议方法名：获取电量计统计信息。</summary>
    private const string MethodGetEMStatistics = "getEMStatistics";

    /// <summary>安圣协议方法名：清空电量计统计信息。</summary>
    private const string MethodClearEMStatistics = "clearEMStatistics";

    /// <summary>安圣协议方法名：获取校准参数。</summary>
    private const string MethodGetCalParams = "getCalParams";

    /// <summary>安圣协议方法名：设置校准参数。</summary>
    private const string MethodSetCalParams = "setCalParams";

    /// <summary>安圣协议方法名：重置校准参数。</summary>
    private const string MethodResetCalParams = "resetCalParams";

    /// <summary>安圣协议方法名：自动校准。</summary>
    private const string MethodAutoCal = "autoCal";

    /// <summary><see cref="AnShengDeviceEvent.Method"/> 列宽（varchar(32)）。</summary>
    private const int EventMethodMaxLength = 32;

    private readonly AppDbContext _db;
    private readonly IAnShengCommandService _cmd;
    private readonly IDataCollectionService _dataCollection;
    private readonly AnShengCommandOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<AnShengEnergyService> _logger;

    /// <summary>
    /// 构造电量计服务。
    /// </summary>
    /// <param name="db">数据库上下文（Scoped）。</param>
    /// <param name="cmd">命令下发服务（T7 单点入口，本服务唯一的出网通道）。</param>
    /// <param name="dataCollection">数据采集服务，实时读数经它落 <c>DeviceDataRecord</c>（验收 #5）。</param>
    /// <param name="options">命令服务参数（陈旧阈值等）。</param>
    /// <param name="logger">日志。</param>
    /// <param name="timeProvider">
    /// 时间源，可选。DI 未注册 <see cref="TimeProvider"/> 时回落 <see cref="TimeProvider.System"/>；
    /// 单元测试可注入假时钟断言 <c>IsStale</c> 边界，无需真的等 24 小时。
    /// </param>
    public AnShengEnergyService(
        AppDbContext db,
        IAnShengCommandService cmd,
        IDataCollectionService dataCollection,
        IOptions<AnShengCommandOptions> options,
        ILogger<AnShengEnergyService> logger,
        TimeProvider? timeProvider = null)
    {
        _db = db;
        _cmd = cmd;
        _dataCollection = dataCollection;
        _options = options?.Value ?? new AnShengCommandOptions();
        _logger = logger;
        _time = timeProvider ?? TimeProvider.System;
    }

    // ─────────────────────────────────────────────────────────────
    // 下发编排（HTTP 作用域）
    // ─────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<AnShengEnergyResultDto> RequestRealtimeAsync(
        long deviceId, CancellationToken ct = default)
    {
        var response = await _cmd.SendCommandAsync(deviceId, MethodGetEMRealtime, null, ct);
        return BuildResult(response);
    }

    /// <inheritdoc />
    public async Task<AnShengEnergyResultDto> RequestStatisticsAsync(
        long deviceId, string? q = null, CancellationToken ct = default)
    {
        Dictionary<string, object?>? parameters = null;

        if (!string.IsNullOrWhiteSpace(q))
        {
            // 空白串不下发：安圣设备对「显式空串」与「没有该键」处理不一致，宁可不带。
            parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["q"] = q.Trim()
            };
        }

        var response = await _cmd.SendCommandAsync(deviceId, MethodGetEMStatistics, parameters, ct);
        return BuildResult(response);
    }

    /// <inheritdoc />
    public async Task<AnShengEnergyResultDto> ClearStatisticsAsync(
        long deviceId, int? slotNum, bool confirm, CancellationToken ct = default)
    {
        if (!confirm)
        {
            // 二次确认在服务层判定（与 T10 同口径）：命令零出网，控制器据此返回 HTTP 200 + Code=400。
            return BuildBusinessRejected("清空电量计统计是不可逆的设备侧操作，需 confirm=true 才会下发");
        }

        Dictionary<string, object?>? parameters = null;

        // 协议：不传或 0 表示清空所有插槽。此处把 0 归一成「不传」，报文更干净且语义等价。
        if (slotNum.HasValue && slotNum.Value > 0)
        {
            parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["slotNum"] = slotNum.Value
            };
        }

        var response = await _cmd.SendCommandAsync(deviceId, MethodClearEMStatistics, parameters, ct);

        if (!response.Success)
        {
            // 零发布 ⇒ 设备没被清 ⇒ 不能留下「已清零」的标记，否则对账时会指鹿为马。
            return BuildResult(response);
        }

        // 验收 #4：聚合表一行不删，只追加一条清零标记事件。
        await WriteClearMarkerEventAsync(deviceId, slotNum, response, ct);

        return BuildResult(response);
    }

    /// <inheritdoc />
    public async Task<AnShengEnergyResultDto> GetCalParamsAsync(
        long deviceId, CancellationToken ct = default)
    {
        var response = await _cmd.SendCommandAsync(deviceId, MethodGetCalParams, null, ct);
        return BuildResult(response);
    }

    /// <inheritdoc />
    public async Task<AnShengEnergyResultDto> SetCalParamsAsync(
        long deviceId, IReadOnlyDictionary<string, double> calParams, CancellationToken ct = default)
    {
        if (calParams == null || calParams.Count == 0)
        {
            return BuildBusinessRejected("calParams 不能为空，至少需要提供 RL（校准电阻值）");
        }

        // ★ 必须转成 JsonElement 再交给 Guard：
        //   AnShengParamSpec 对 Object 类型的运行时判定是
        //   「value is not string && value is not IEnumerable && !IsPrimitive(value)」，
        //   而 Dictionary<,> 恰恰实现了 IEnumerable —— 直接塞字典会被判成「类型不符」而拒发。
        //   JsonElement 走的是 MatchesJsonElement 分支（ValueKind == Object），才是合法姿势。
        var calParamsElement = JsonSerializer.SerializeToElement(
            calParams.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal));

        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["calParams"] = calParamsElement
        };

        var response = await _cmd.SendCommandAsync(deviceId, MethodSetCalParams, parameters, ct);
        return BuildResult(response);
    }

    /// <inheritdoc />
    public async Task<AnShengEnergyResultDto> ResetCalParamsAsync(
        long deviceId, CancellationToken ct = default)
    {
        var response = await _cmd.SendCommandAsync(deviceId, MethodResetCalParams, null, ct);
        return BuildResult(response);
    }

    /// <inheritdoc />
    public async Task<AnShengEnergyResultDto> AutoCalAsync(
        long deviceId, double power, CancellationToken ct = default)
    {
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["power"] = power
        };

        var response = await _cmd.SendCommandAsync(deviceId, MethodAutoCal, parameters, ct);
        return BuildResult(response);
    }

    // ─────────────────────────────────────────────────────────────
    // 只读查询（HTTP 作用域）
    // ─────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<List<AnShengEmStatisticDto>> QueryStatisticsAsync(
        long deviceId, int? slotNum = null, AnShengEmGranularity? granularity = null,
        CancellationToken ct = default)
    {
        // 刻意<b>不</b> IgnoreQueryFilters：本方法只服务 HTTP 作用域，
        // 全局租户过滤器是这里唯一的跨租户读防线（设计 §7.1 末段）。
        var query = _db.Set<AnShengEmStatistic>()
            .AsNoTracking()
            .Where(s => s.DeviceId == deviceId);

        if (slotNum.HasValue)
        {
            query = query.Where(s => s.SlotNum == slotNum.Value);
        }

        if (granularity.HasValue)
        {
            query = query.Where(s => s.Granularity == granularity.Value);
        }

        var rows = await query
            .OrderBy(s => s.SlotNum)
            .ThenBy(s => s.Granularity)
            .ThenBy(s => s.PeriodKey)
            .ToListAsync(ct);

        var now = UtcNow();
        var threshold = _options.EffectiveMirrorStaleThreshold;

        return rows
            .Select(s => new AnShengEmStatisticDto
            {
                SlotNum = s.SlotNum,
                Granularity = s.Granularity,
                PeriodKey = s.PeriodKey,
                Kwh = s.Kwh,
                SyncedAt = s.SyncedAt,
                IsStale = (now - s.SyncedAt) > threshold
            })
            .ToList();
    }

    // ─────────────────────────────────────────────────────────────
    // 应答写回（后台作用域 —— 一律 IgnoreQueryFilters + 显式 AppCode）
    // ─────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task ApplyStatisticsReadbackAsync(
        long deviceId, AnShengEmStatisticsSnapshot snapshot, CancellationToken ct = default)
    {
        if (snapshot == null || snapshot.Slots.Count == 0)
        {
            // 空 data[] 不等于「全部清零」：设备也可能只是这一帧没带数组。
            // 按「没探到」处理，保留既有聚合行（设计 D5：平台只累积，不跟随设备清空）。
            _logger.LogDebug(
                "getEMStatistics 应答不含 data[]，跳过聚合写回: DeviceId={DeviceId}", deviceId);
            return;
        }

        var appCode = await ResolveAppCodeAsync(deviceId, ct);
        if (appCode == null)
        {
            _logger.LogWarning(
                "电量计统计写回失败：设备不存在或无租户码，已跳过。DeviceId={DeviceId}, DataLength={DataLength}",
                deviceId, snapshot.DataLength);
            return;
        }

        // ── §7-R8：data[] 长度必须等于档案里的插槽数 ──
        // 插槽号是按下标 +1 推导出来的。长度对不上意味着推导必然错位，
        // 此时写进去的**每一行**都会挂到错误的插槽上 —— 这比「没数据」恶劣得多，
        // 因为它看上去完全正常，直到有人拿它去算电费。宁可整帧拒收并告警。
        var slotAmount = await _db.Set<AnShengDeviceProfile>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => p.DeviceId == deviceId)
            .Select(p => p.SlotAmount)
            .FirstOrDefaultAsync(ct);

        if (slotAmount.HasValue && slotAmount.Value > 0 && snapshot.DataLength != slotAmount.Value)
        {
            _logger.LogWarning(
                "getEMStatistics 应答 data[] 长度与档案插槽数不符，已拒绝入库（插槽号按下标推导，长度不符必然错位）。"
                + "DeviceId={DeviceId}, DataLength={DataLength}, SlotAmount={SlotAmount}",
                deviceId, snapshot.DataLength, slotAmount.Value);
            return;
        }

        if (!slotAmount.HasValue)
        {
            // 档案缺失（存量设备未认领 / T5 能力持久化尚未覆盖）时无法校验。
            // 这是已知缺口，用 Debug 记录而不是拒收 —— 拒收会让所有无档案设备永远没有统计。
            _logger.LogDebug(
                "设备无安圣能力档案，跳过 data[] 长度校验: DeviceId={DeviceId}, DataLength={DataLength}",
                deviceId, snapshot.DataLength);
        }

        var existing = await _db.Set<AnShengEmStatistic>()
            .IgnoreQueryFilters()
            .Where(s => s.DeviceId == deviceId)
            .ToListAsync(ct);

        // 不用 ToDictionary：UQ(DeviceId, SlotNum, Granularity, PeriodKey) 理论上保证唯一，
        // 但历史脏数据一旦重键就会让整条上行链路抛异常，代价远大于「后写覆盖先写」。
        var byKey = new Dictionary<(int SlotNum, AnShengEmGranularity Granularity, string PeriodKey),
            AnShengEmStatistic>(existing.Count);

        foreach (var row in existing)
        {
            byKey[(row.SlotNum, row.Granularity, row.PeriodKey)] = row;
        }

        var now = UtcNow();
        var inserted = 0;
        var updated = 0;

        foreach (var slot in snapshot.Slots)
        {
            if (slot.HourSumLength >= 0 && slot.HourSumLength != AnShengEmStatistic.HourSumSlotCount)
            {
                // 验收 #2 的反面：长度不符时解析器已经一个 HourSum 点都不产出，这里只负责喊出来。
                _logger.LogWarning(
                    "getEMStatistics 的 hourSumData 长度不是 {Expected}，该插槽半小时画像已整体丢弃。"
                    + "DeviceId={DeviceId}, SlotNum={SlotNum}, ActualLength={Actual}",
                    AnShengEmStatistic.HourSumSlotCount, deviceId, slot.SlotNum, slot.HourSumLength);
            }

            foreach (var point in slot.Points)
            {
                var periodKey = AnShengEmStatistic.ClampPeriodKey(point.PeriodKey);
                if (periodKey.Length == 0)
                {
                    // 没有周期键就无法参与唯一键去重 —— 写进去必然重复累积（验收 #1 的杀手）。
                    continue;
                }

                var key = (slot.SlotNum, point.Granularity, periodKey);

                if (!byKey.TryGetValue(key, out var row))
                {
                    row = new AnShengEmStatistic
                    {
                        AppCode = appCode,
                        DeviceId = deviceId,
                        SlotNum = slot.SlotNum,
                        Granularity = point.Granularity,
                        PeriodKey = periodKey
                    };
                    _db.Set<AnShengEmStatistic>().Add(row);
                    byKey[key] = row;
                    inserted++;
                }
                else
                {
                    updated++;
                }

                // AppCode 每次都以 Devices 表为准重写：设备换租户（重认领）后数据不会留在旧租户下。
                row.AppCode = appCode;
                row.Kwh = point.Kwh;
                row.SyncedAt = now;
            }
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogDebug(
            "电量计统计已 UPSERT: DeviceId={DeviceId}, AppCode={AppCode}, Slots={Slots}, "
            + "Inserted={Inserted}, Updated={Updated}, SyncedAt={SyncedAt:O}",
            deviceId, appCode, snapshot.Slots.Count, inserted, updated, now);
    }

    /// <inheritdoc />
    public async Task ApplyRealtimeReadbackAsync(
        long deviceId, IReadOnlyList<AnShengEmRealtimeSlot> slots,
        DateTime? deviceTimestampUtc = null, CancellationToken ct = default)
    {
        if (slots == null || slots.Count == 0)
        {
            _logger.LogDebug(
                "getEMRealtime 应答不含 data[]，跳过实时数据入库: DeviceId={DeviceId}", deviceId);
            return;
        }

        var appCode = await ResolveAppCodeAsync(deviceId, ct);
        if (appCode == null)
        {
            _logger.LogWarning(
                "电量计实时数据入库失败：设备不存在或无租户码，已跳过。DeviceId={DeviceId}", deviceId);
            return;
        }

        var payload = BuildRealtimeSensorPayload(slots);
        if (payload == null)
        {
            // 每个插槽的 v/c/p/e 全缺失 —— 写一条全 null 的记录只会污染曲线。
            _logger.LogDebug(
                "getEMRealtime 应答的 data[] 无任何有效读数，跳过入库: DeviceId={DeviceId}", deviceId);
            return;
        }

        // 设备时钟漂移是已知现象，复用事件表那套「可信区间」判定：
        // 超出 [now-24h, now+5min] 一律回退平台时间，宁可少一点精度也不要污染时间轴。
        var receivedAt = UtcNow();
        var timestamp = AnShengDeviceEvent.ResolveOccurredAt(deviceTimestampUtc, receivedAt, out var usedFallback);

        if (usedFallback && deviceTimestampUtc.HasValue)
        {
            _logger.LogDebug(
                "getEMRealtime 设备时间戳不可信，已回退平台时间: DeviceId={DeviceId}, DeviceTs={DeviceTs:O}",
                deviceId, deviceTimestampUtc.Value);
        }

        // 走既有数据采集通路（设计 D5：实时零改动复用 DataRule 告警引擎与现有图表）。
        await _dataCollection.ProcessDeviceDataAsync(deviceId, appCode, payload, timestamp);

        _logger.LogDebug(
            "电量计实时数据已入 DeviceDataRecord: DeviceId={DeviceId}, AppCode={AppCode}, Slots={Slots}, Ts={Ts:O}",
            deviceId, appCode, slots.Count, timestamp);
    }

    // ─────────────────────────────────────────────────────────────
    // 私有工具
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 把逐插槽实时读数拍平成 <c>DeviceDataRecord.SensorData</c> 用的 JSON。
    ///
    /// 【命名必须与 <c>DataCollectionService.SlotFieldRegex</c> 对齐】
    ///   <c>^slot(\d+)_(state|voltage|current|power|energy|pf)$</c> —— 名字对不上就不会被识别成
    ///   传感器数据点，前端也就建不出「插槽 3 的电压」这种传感器。
    ///
    /// 【为什么还要额外算 total_power / total_energy】
    ///   <c>DeviceDataRecord</c> 上<b>没有</b> slot 级的物理量列，逐插槽值只随 SensorData JSON 落库。
    ///   而 <c>ElectricPower</c> / <c>ElectricKWh</c> 两列是告警规则与能耗报表的既有口径，
    ///   必须有整机聚合值把它们填上，否则 T11 的实时数据对既有功能等于不存在。
    /// </summary>
    /// <param name="slots">逐插槽实时读数。</param>
    /// <returns>SensorData JSON；无任何有效读数时返回 <c>null</c>。</returns>
    private static string? BuildRealtimeSensorPayload(IReadOnlyList<AnShengEmRealtimeSlot> slots)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal);

        var voltageSum = 0d;
        var voltageCount = 0;
        var currentSum = 0d;
        var powerSum = 0d;
        var energySum = 0d;
        var hasAny = false;

        foreach (var slot in slots)
        {
            if (slot == null || slot.SlotNum < 1 || !slot.HasAnyValue)
            {
                continue;
            }

            hasAny = true;
            var prefix = $"slot{slot.SlotNum}_";

            if (slot.Voltage.HasValue)
            {
                payload[prefix + "voltage"] = slot.Voltage.Value;
                voltageSum += slot.Voltage.Value;
                voltageCount++;
            }

            if (slot.Current.HasValue)
            {
                payload[prefix + "current"] = slot.Current.Value;
                currentSum += slot.Current.Value;
            }

            if (slot.Power.HasValue)
            {
                payload[prefix + "power"] = slot.Power.Value;
                powerSum += slot.Power.Value;
            }

            if (slot.Energy.HasValue)
            {
                payload[prefix + "energy"] = slot.Energy.Value;
                energySum += slot.Energy.Value;
            }
        }

        if (!hasAny)
        {
            return null;
        }

        // 整机聚合口径：电压取平均（多路并联同一进线，求和没有物理意义），电流 / 功率 / 电量求和。
        if (voltageCount > 0)
        {
            payload["avg_voltage"] = voltageSum / voltageCount;
        }

        payload["total_current"] = currentSum;
        payload["total_power"] = powerSum;
        payload["total_energy"] = energySum;

        return JsonSerializer.Serialize(payload);
    }

    /// <summary>
    /// 写一条「电量计统计清零」标记事件（验收 #4）。
    ///
    /// 【为什么必须留痕】平台聚合表不跟随设备清空，于是从此刻起
    /// 「平台累计值 ≫ 设备读数」会成为常态。没有这条标记，日后对账只会得出
    /// 「平台数据错了」的错误结论。
    ///
    /// 【为什么吞异常】命令<b>已经成功出网</b>，设备已经被清了。标记写失败是记账问题，
    /// 不该把一个已经生效的操作报成失败让调用方重试（重试等于再清一次）。
    /// </summary>
    /// <param name="deviceId">设备主键。</param>
    /// <param name="slotNum">被清插槽；null / 0 表示全部。</param>
    /// <param name="response">命令下发响应（取 CommandId / FrameId 便于溯源）。</param>
    /// <param name="ct">取消令牌。</param>
    private async Task WriteClearMarkerEventAsync(
        long deviceId, int? slotNum, AnShengCommandResponse response, CancellationToken ct)
    {
        try
        {
            var identity = await _db.Devices
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(d => d.Id == deviceId)
                .Select(d => new { d.AppCode, d.SerialNumber })
                .FirstOrDefaultAsync(ct);

            if (identity == null || string.IsNullOrWhiteSpace(identity.AppCode))
            {
                _logger.LogWarning(
                    "清零标记事件未写入：设备不存在或无租户码。DeviceId={DeviceId}", deviceId);
                return;
            }

            // IMEI 优先取安圣档案（认领流程写入的权威值），回落 Device.SerialNumber
            // （既有约定：安圣设备的 IMEI 就存在 SerialNumber 上，见 Device.cs 注释）。
            var imei = await _db.Set<AnShengDeviceProfile>()
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(p => p.DeviceId == deviceId)
                .Select(p => p.Imei)
                .FirstOrDefaultAsync(ct);

            if (string.IsNullOrWhiteSpace(imei))
            {
                imei = identity.SerialNumber ?? string.Empty;
            }

            var now = UtcNow();
            var normalizedSlot = slotNum.HasValue && slotNum.Value > 0 ? slotNum.Value : (int?)null;

            var payload = JsonSerializer.Serialize(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["slotNum"] = normalizedSlot,
                ["scope"] = normalizedSlot.HasValue ? "slot" : "all",
                ["commandId"] = response.CommandId,
                ["frameId"] = response.FrameId,
                ["platformDataRetained"] = true
            });

            _db.Set<AnShengDeviceEvent>().Add(new AnShengDeviceEvent
            {
                AppCode = identity.AppCode,
                Imei = Truncate(imei, 32),
                DeviceId = deviceId,
                Method = Truncate(MethodClearEMStatistics, EventMethodMaxLength),
                Kind = AnShengEventKind.EmCleared,
                Severity = AnShengEventSeverity.Warning,
                SlotNum = normalizedSlot,
                FrameId = response.FrameId,
                DeviceTimestampUtc = null,
                OccurredAt = now,
                ReceivedAt = now,
                PayloadJson = payload,
                RawJson = response.Payload,
                DispatchedToRules = false,
                DispatchError = null,
                CreatedAt = now
            });

            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "电量计清零标记已记录（平台聚合数据保留）: DeviceId={DeviceId}, SlotNum={SlotNum}, CommandId={CommandId}",
                deviceId, normalizedSlot?.ToString() ?? "all", response.CommandId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "写入电量计清零标记事件失败（命令已成功出网，不影响本次操作结果）: DeviceId={DeviceId}", deviceId);
        }
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

    /// <summary>按列宽截断字符串，避免设备回包超长直接撞 <c>DbUpdateException</c>。</summary>
    /// <param name="value">原始串。</param>
    /// <param name="maxLength">列宽。</param>
    /// <returns>可安全落库的串。</returns>
    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= maxLength ? value : value.Substring(0, maxLength);
    }

    /// <summary>把 T7 的下发响应翻译成 T11 的结果 DTO。</summary>
    /// <param name="response">命令下发响应。</param>
    /// <returns>电量计命令结果。</returns>
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

    /// <summary>
    /// 构造「平台业务拒绝」结果（命令<b>未出网</b>）。
    ///
    /// <c>RejectReason</c> 刻意留空：那个枚举是 <c>AnShengCommandGuard</c> 的判定结论，
    /// 平台自己的前置校验冒用它会让调用方误以为是设备品类 / 固件问题。
    /// </summary>
    /// <param name="message">面向人的拒绝原因。</param>
    /// <returns>未受理结果。</returns>
    private static AnShengEnergyResultDto BuildBusinessRejected(string message)
        => new()
        {
            Accepted = false,
            CommandId = null,
            FrameId = null,
            RejectReason = null,
            ErrorMessage = message,
            Payload = null
        };
}
