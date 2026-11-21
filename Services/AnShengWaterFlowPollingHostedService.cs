// 水流量自动采集轮询宿主（功能补齐：平台此前无任何水流量自动采集）。
//
// 【为什么必须存在这个组件】
//   安圣水开关设备物理上<b>不会主动推送</b>流量数据 —— 平台侧必须周期性下发
//   getFlowInfo，设备应答后经既有入库链路落 device_data_records：
//     应答 → AnShengMessageRouter(isGetFlowInfo) → AnShengWaterFlowService.ApplyFlowInfoReadbackAsync
//          → DataCollectionService.ProcessDeviceDataAsync → INSERT device_data_records
//   本宿主只负责「周期下发」，入库链路零改动复用。
//
// 【为什么独立成宿主而不是搭车既有后台组件】
//   与 AnShengCommandSweepHostedService（D8 决策）同理：各后台组件的周期语义不同
//   （超时清扫是秒级、离线防抖是分钟级、本组件默认 60s），合并必然牺牲一方。
//
// 【与 CommandSweep 完全对齐的三条铁律】
//   1. Singleton（AddHostedService）⇒ 构造函数绝不注入 Scoped 服务（AppDbContext 等），
//      每轮用 IServiceScopeFactory.CreateScope 现开现关；
//   2. 后台线程无 HTTP 上下文 ⇒ ITenantContextAccessor.Current 为 null ⇒
//      AppDbContext 全局租户过滤器会把所有行滤成空集 —— 查询一律
//      IgnoreQueryFilters()（T6 §7.2 踩过的坑）；
//   3. ExecuteAsync 里异常绝不允许冒泡 —— BackgroundService 异常会让宿主静默停机，
//      整轮 try/catch + LogWarning + 继续下一轮。

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IoTPlatform.Configuration;
using IoTPlatform.Data;
using IoTPlatform.Infrastructure.Protocol.AnSheng;
using IoTPlatform.Models;
using IoTPlatform.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IoTPlatform.Services;

/// <summary>
/// 安圣水流量自动采集轮询宿主。
///
/// 【一句话职责】周期性筛选「已认领的水开关设备」，逐台下发 <c>getFlowInfo</c>，
/// 应答由 Router → <see cref="IAnShengWaterFlowService.ApplyFlowInfoReadbackAsync"/>
/// 自动落 <c>DeviceDataRecord</c>（waterflow / watertotal / wpulses）。
///
/// 【设备筛选判定 —— 为什么这样选】
///   1. <b>SerialNumber 非空</b>：安圣设备以 IMEI 为 SerialNumber 认领（Device.cs 注释），
///      命令链路（AnShengCommandService）第一步就按它找 IMEI，空则必然拒绝；
///   2. <b>ProtocolConfigId 非空</b>：认领流程（AnShengDiscoveryService.ClaimAsync 步骤 11①）
///      必写该字段，也是命令链路的前置校验 —— 与 SendCommandAsync 的判定完全对齐，
///      顺带把非安圣协议的水表（Modbus 等，EnergyTypes 同样可能含 water）挡在门外；
///   3. <b>EnergyTypes 精确含 "water"</b>：AnShengDeviceKind 枚举只有 喇叭/开关 四值，
///      <b>没有水品类</b> —— 「水开关」与「电开关」在协议层同型，唯一的区分信号是
///      用户认领时选择的能源类型（ClaimAsync:708 写入 Device.EnergyTypes，JSON 数组）。
///      取 Profile Kind 判水是不可行的（Kind 无水语义），故 EnergyTypes 是唯一可靠判据；
///   4. <b>Status != "offline"</b>：软性在线预判。AnShengOfflineDebouncer 在遗嘱去抖后
///      置 "offline"、DiscoveryService 在收到数据时置 "online"（:309），状态维护是现成的。
///      设备不在线时命令链路自带超时兜底，这里跳过只是减少无效下发与超时记录噪音；
///   5. <b>Profile 品类为喇叭则跳过</b>：getFlowInfo 在命令目录里属于 GroupSwitchAction
///      能力组（仅开关类支持），喇叭款下发必然失败 —— 纯粹是省无效命令。
///      Profile 缺失或 Unknown 时<b>仍然下发</b>（对齐 D7 决策：不得因档案缺失打死存量设备）。
///
/// 【生命周期】Singleton（<c>AddHostedService</c>）。每轮一个 scope、逐台顺序 await，
/// 不做并发下发 —— 命令链路自带节流（R3），并发只会在上游排队。
/// </summary>
public sealed class AnShengWaterFlowPollingHostedService : BackgroundService
{
    private const string OfflineStatus = "offline";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AnShengWaterFlowOptions _options;
    private readonly ILogger<AnShengWaterFlowPollingHostedService> _logger;

    /// <summary>
    /// 构造轮询宿主。
    /// </summary>
    /// <param name="scopeFactory">作用域工厂；每轮用它取 Scoped 的 <see cref="AppDbContext"/> 与 <see cref="IAnShengWaterFlowService"/>。</param>
    /// <param name="options">水流量轮询配置（间隔、开关）。</param>
    /// <param name="logger">日志器。</param>
    public AnShengWaterFlowPollingHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<AnShengWaterFlowOptions> options,
        ILogger<AnShengWaterFlowPollingHostedService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 后台循环：初始延迟（默认 20s，等协议适配器拉起）→ 每轮筛选下发 → 等待间隔。
    ///
    /// <c>PollEnabled=false</c> 时直接返回（集成测试姿势：关后台线程，手工调
    /// <see cref="PollOnceAsync"/>，杜绝与断言竞态 —— 同 SweepEnabled 的测试纪律）。
    /// </summary>
    /// <param name="stoppingToken">宿主停止令牌。</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.PollEnabled)
        {
            _logger.LogInformation(
                "[AnShengWaterFlowPolling] 后台轮询已禁用（AnSheng:WaterFlow:PollEnabled=false），" +
                "水流量不会自动采集，需由调用方手工触发 PollOnceAsync。");
            return;
        }

        var interval = TimeSpan.FromSeconds(_options.EffectivePollIntervalSeconds);
        var initialDelay = TimeSpan.FromSeconds(_options.EffectiveInitialDelaySeconds);
        _logger.LogInformation(
            "[AnShengWaterFlowPolling] 水流量自动采集已启动：初始延迟 {InitialDelay}，周期 {Interval}。",
            initialDelay, interval);

        // 初始延迟等适配器拉起（MQTT 连接 + 订阅就绪）。中途停机属正常收尾。
        try
        {
            await Task.Delay(initialDelay, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // ★ 生命线：异常冒出 ExecuteAsync 会让宿主静默停机，
                //   从此水流量再无自动采集且无任何显式报错。只记 Warning，下一轮继续。
                _logger.LogWarning(ex,
                    "[AnShengWaterFlowPolling] 单轮轮询失败，已跳过本轮，下一轮继续。");
            }

            try
            {
                await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("[AnShengWaterFlowPolling] 后台轮询已停止。");
    }

    /// <summary>
    /// 执行<b>一轮</b>采集：筛选目标设备 → 逐台下发 getFlowInfo → 汇总日志。
    ///
    /// 【为什么是 public】与 <c>AnShengCommandSweepHostedService.SweepOnceAsync</c> 同一范式：
    /// 它是本组件的语义单元，<see cref="ExecuteAsync"/> 只是「按周期反复调用它」的壳。
    /// 集成测试在 <c>PollEnabled=false</c> 下手工调它，验证的正是生产同一段代码路径。
    ///
    /// 【下发方式】复用 <see cref="IAnShengWaterFlowService.RequestFlowInfoAsync"/> ——
    /// 与手动端点（AnShengWaterFlowController POST water/flow/info）完全同一条路：
    /// 内部经 IAnShengCommandService.SendCommandAsync，先登记后下发、自带节流 / 在途表 /
    /// 超时兜底，方法返回只表示「出网受理结果」，不等设备应答（应答异步经 Router 入库）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>(下发受理数, 跳过数)；跳过含 喇叭类 / 精确水判定不通过 两类。</returns>
    public async Task<(int Sent, int Skipped)> PollOnceAsync(CancellationToken cancellationToken = default)
    {
        // 一轮一个 scope：DbContext 与 WaterFlowService 同域，逐台顺序使用，轮末随 scope 释放。
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var waterFlow = scope.ServiceProvider.GetRequiredService<IAnShengWaterFlowService>();

        // ── 1. SQL 预筛（IgnoreQueryFilters：后台作用域无租户上下文，带过滤器恒为空集）──
        // EnergyTypes 用 LIKE '%water%' 粗筛（MySQL 默认排序规则大小写不敏感），
        // 精确判定由内存里的 IsWaterEnergyDevice 把关（处理 JSON 数组 / 大小写 / 旧格式）。
        var candidates = await db.Devices
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(d => d.SerialNumber != null && d.SerialNumber != ""
                && d.ProtocolConfigId != null
                && d.EnergyTypes != null && d.EnergyTypes.Contains("water")
                && d.Status != OfflineStatus)
            .Select(d => new WaterFlowPollCandidate(d.Id, d.SerialNumber!, d.EnergyTypes))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (candidates.Count == 0)
        {
            // 空转零日志：60s 一轮的组件不能在无事发生时刷屏（同 Sweep 的纪律）。
            _logger.LogDebug("[AnShengWaterFlowPolling] 本轮无符合条件的设备，跳过。");
            return (0, 0);
        }

        // ── 2. Profile 品类过滤：喇叭类不支持 getFlowInfo（GroupSwitchAction），跳过 ──
        var imeis = candidates.Select(c => c.SerialNumber).Distinct().ToList();
        var speakerImeis = (await db.AnShengDeviceProfiles
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => imeis.Contains(p.Imei))
            .Select(p => new { p.Imei, p.Kind })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false))
            .Where(p => ShouldSkipForPolling(p.Kind))
            .Select(p => p.Imei)
            .ToHashSet(StringComparer.Ordinal);

        // ── 3. 逐台下发（顺序 await，命令链路内部自带节流）──
        var sent = 0;
        var skipped = 0;
        var rejected = 0;

        foreach (var candidate in candidates)
        {
            if (speakerImeis.Contains(candidate.SerialNumber) ||
                !IsWaterEnergyDevice(candidate.EnergyTypes))
            {
                skipped++;
                continue;
            }

            try
            {
                var result = await waterFlow.RequestFlowInfoAsync(candidate.Id, cancellationToken)
                    .ConfigureAwait(false);

                if (result.Accepted)
                {
                    sent++;
                }
                else
                {
                    // 命令链路拒绝（设备不存在 / 适配器未连 / 品类拦截等）—— 可观测，不中断本轮。
                    rejected++;
                    _logger.LogWarning(
                        "[AnShengWaterFlowPolling] getFlowInfo 下发被拒: DeviceId={DeviceId}, Reason={Reason}, Code={Code}",
                        candidate.Id, result.ErrorMessage, result.RejectReason);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 单台失败不拖垮整轮：其余设备照常采集。
                rejected++;
                _logger.LogWarning(ex,
                    "[AnShengWaterFlowPolling] 单台设备下发异常，跳过该台继续: DeviceId={DeviceId}",
                    candidate.Id);
            }
        }

        _logger.LogInformation(
            "[AnShengWaterFlowPolling] 本轮完成：候选 {Total} 台，下发受理 {Sent} 台，" +
            "被拒 {Rejected} 台，跳过 {Skipped} 台（喇叭类 / 非水能源）。",
            candidates.Count, sent, rejected, skipped);

        return (sent, skipped);
    }

    // ─────────────────────────────────────────────────────────────
    // 纯函数（public static：毫秒级、无 DB、无 MQTT 的单测锚点）
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 判定设备的能源类型里是否含 <c>water</c>（水开关的唯一可靠判据）。
    ///
    /// 【为什么必须解析 JSON 而不是裸 Contains】
    ///   EnergyTypes 是认领时经 DeviceService.NormalizeEnergyTypesJson 归一化的
    ///   JSON 数组字符串（如 <c>["electric","water"]</c>）。裸字符串 Contains("water")
    ///   会把 <c>"watermeter"</c> 这类元素误判成水设备；解析数组后做<b>元素级</b>的
    ///   大小写不敏感比对才精确。旧数据可能是裸值或逗号串（Normalize 的输入形态），
    ///   兜底按逗号拆分处理。
    /// </summary>
    /// <param name="energyTypes">设备能源类型（JSON 数组 / 逗号串 / 裸值），可为 null。</param>
    /// <returns>元素级含 water 时返回 true。</returns>
    public static bool IsWaterEnergyDevice(string? energyTypes)
    {
        if (string.IsNullOrWhiteSpace(energyTypes))
        {
            return false;
        }

        var input = energyTypes.Trim();

        try
        {
            if (input.StartsWith("[", StringComparison.Ordinal))
            {
                using var doc = JsonDocument.Parse(input);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var element in doc.RootElement.EnumerateArray())
                    {
                        if (element.ValueKind != JsonValueKind.String)
                        {
                            continue;
                        }

                        if (string.Equals(element.GetString()?.Trim(), "water", StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }

                    // 合法 JSON 数组但不含 water —— 精确否，不再落入兜底分支。
                    return false;
                }
            }
        }
        catch (JsonException)
        {
            // 畸形 JSON：落入兜底的逗号拆分（大概率也匹配不到，返回 false）。
        }

        // 兜底：裸值（"water"）或逗号串（"electric,water"）—— 与 NormalizeEnergyTypesJson
        // 接受的输入形态一致，避免历史数据因格式差异漏采。
        return input
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(part => string.Equals(part, "water", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 按档案品类判定是否跳过轮询。
    ///
    /// 仅<b>显式判定为喇叭</b>时跳过 —— getFlowInfo 属 GroupSwitchAction 能力组，
    /// 喇叭款结构性地不支持。Unknown / 无档案（<c>null</c> 传参方已处理）不跳过：
    /// 对齐 D7 决策（RejectWhenKindUnknown 默认 false 的同一哲学），
    /// 档案缺失不应打死存量设备的自动采集。
    /// </summary>
    /// <param name="kind">设备品类。</param>
    /// <returns>喇叭类返回 true（跳过）；开关类 / Unknown 返回 false。</returns>
    public static bool ShouldSkipForPolling(AnShengDeviceKind kind)
        => kind == AnShengDeviceKind.Speaker4G || kind == AnShengDeviceKind.SpeakerWiFi;

    /// <summary>一轮候选设备的最小投影（只取筛选与下发所需字段，AsNoTracking 不沾跟踪器）。</summary>
    /// <param name="Id">设备主键。</param>
    /// <param name="SerialNumber">IMEI（安圣设备认领时写入 SerialNumber）。</param>
    /// <param name="EnergyTypes">能源类型 JSON（内存精确水判定用）。</param>
    private sealed record WaterFlowPollCandidate(long Id, string SerialNumber, string? EnergyTypes);
}
