using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using IoTPlatform.Configuration;
using IoTPlatform.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IoTPlatform.Services;

/// <summary>
/// 安圣设备离线去抖器（决策 3）。
///
/// 【为什么是独立 Singleton，而不是放进 Handler / Dispatcher】
///   Handler 是 Scoped，Scope 随一次上行处理结束即销毁，
///   而 <c>Task.Delay(30s)</c> 会跨越 Scope 生命周期 —— 放在 Handler 里
///   <c>AppDbContext</c> 早已释放。独立 Singleton 持有定时器，职责单一、可单测
///   （注入短窗口）、可被 <c>StaticStateResetter</c> 清理。
///
/// 【去抖语义】
///   · 收到 <c>close</c> → <see cref="Arm"/>：起一个 <c>CloseDebounceSeconds</c> 窗口；
///   · 窗口内收到 <c>connected</c> → <see cref="Cancel"/>：撤销离线（设备不乱跳）；
///   · 窗口到期无人撤销 → 调 <see cref="IAnShengDiscoveryService.OnDeviceOfflineAsync"/>
///     真正把设备置离线（验收 #5）。
///
/// 【并发安全】<see cref="ConcurrentDictionary{TKey,TValue}"/> + <c>CancellationTokenSource</c>
///   原子替换：同 IMEI 连续收到多次 close，旧定时器被取消，只保留最新的一个窗口。
/// </summary>
public sealed class AnShengOfflineDebouncer
{
    /// <summary>IMEI → 当前在途的去抖 CTS。</summary>
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _arms =
        new(StringComparer.Ordinal);

    private readonly IOptions<AnShengEventOptions> _options;
    private readonly IAnShengDiscoveryService _discovery;
    private readonly ILogger<AnShengOfflineDebouncer> _logger;

    /// <summary>
    /// 构造去抖器。
    /// </summary>
    /// <param name="options">事件管道配置（提供去抖窗口）。</param>
    /// <param name="discovery">发现服务（窗口到期时回调置离线）。Singleton，可直接注入。</param>
    /// <param name="logger">日志器。</param>
    public AnShengOfflineDebouncer(
        IOptions<AnShengEventOptions> options,
        IAnShengDiscoveryService discovery,
        ILogger<AnShengOfflineDebouncer> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 起一个离线去抖窗口（<c>close</c> 触发）。若同 IMEI 已有窗口，旧窗口被取消并替换。
    /// </summary>
    /// <param name="imei">设备 IMEI。</param>
    /// <param name="appCode">租户码（窗口到期置离线时透传给发现服务）。</param>
    public void Arm(string imei, string? appCode)
    {
        if (string.IsNullOrWhiteSpace(imei))
        {
            return;
        }

        var cts = new CancellationTokenSource();

        // 原子替换：旧 CTS 取消（其定时器会在 finally 自行释放），新的接管窗口。
        _arms.AddOrUpdate(imei, cts, (_, old) =>
        {
            try
            {
                old.Cancel();
            }
            catch (Exception)
            {
                // 忽略取消异常
            }

            return cts;
        });

        _ = ScheduleOfflineAsync(imei, appCode, cts);
    }

    /// <summary>
    /// 撤销离线去抖窗口（<c>connected</c> 触发）。设备不会被置离线。
    /// </summary>
    /// <param name="imei">设备 IMEI。</param>
    public void Cancel(string imei)
    {
        if (string.IsNullOrWhiteSpace(imei))
        {
            return;
        }

        if (_arms.TryRemove(imei, out var cts))
        {
            try
            {
                cts.Cancel();
            }
            catch (Exception)
            {
                // 忽略取消异常
            }

            // CancellationTokenSource.Dispose 幂等，重复调用安全。
            cts.Dispose();
        }
    }

    /// <summary>指定 IMEI 当前是否处于去抖窗口中。</summary>
    /// <param name="imei">设备 IMEI。</param>
    /// <returns>处于窗口中返回 true。</returns>
    public bool IsArmed(string imei)
        => !string.IsNullOrWhiteSpace(imei) && _arms.ContainsKey(imei);

    /// <summary>清空全部在途窗口（仅供测试隔离使用）。</summary>
    public void ClearAll()
    {
        foreach (var pair in _arms.ToArray())
        {
            if (_arms.TryRemove(pair.Key, out var cts))
            {
                try
                {
                    cts.Cancel();
                }
                catch (Exception)
                {
                    // 忽略
                }

                cts.Dispose();
            }
        }
    }

    /// <summary>
    /// 等待去抖窗口到期并（在确认本 CTS 仍是当前窗口的前提下）置离线。
    /// </summary>
    /// <param name="imei">设备 IMEI。</param>
    /// <param name="appCode">租户码。</param>
    /// <param name="cts">本窗口的取消令牌源。</param>
    private async Task ScheduleOfflineAsync(string imei, string? appCode, CancellationTokenSource cts)
    {
        try
        {
            var delayMs = _options.Value.EffectiveCloseDebounceSeconds * 1000;
            await Task.Delay(delayMs, cts.Token).ConfigureAwait(false);

            // 仅当本 CTS 仍是当前窗口时才置离线。
            // 若期间收到 connected（Cancel）或新的 close（Arm 替换），当前值不再是本 CTS，
            // 说明已被撤销或有更新的窗口接管，本窗口静默退出，不置离线。
            if (_arms.TryGetValue(imei, out var current) &&
                ReferenceEquals(current, cts) &&
                _arms.TryRemove(imei, out _))
            {
                await _discovery.OnDeviceOfflineAsync(imei, appCode).ConfigureAwait(false);

                _logger.LogInformation(
                    "[AnShengDebounce] 窗口到期，设备置离线 imei={Imei} appCode={AppCode}", imei, appCode);
            }
        }
        catch (OperationCanceledException)
        {
            // 被 connected / 新 close 取消：正常退出，不置离线。
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AnShengDebounce] 离线判定异常 imei={Imei}", imei);
        }
        finally
        {
            cts.Dispose();
        }
    }
}
