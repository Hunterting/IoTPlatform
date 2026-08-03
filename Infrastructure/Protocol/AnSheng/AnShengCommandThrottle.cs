using System.Collections.Concurrent;
using System.Diagnostics;

namespace IoTPlatform.Infrastructure.Protocol.AnSheng;

/// <summary>
/// 安圣命令下发限流器（按 IMEI 维度）。
///
/// 协议原文：「一次给一台设备发送多个命令，每个命令之间最好间隔 100ms，防止命令粘连」。
/// 本实现为<b>单实例内存版</b>：同一 IMEI 的两次下发之间强制间隔 <see cref="MinIntervalMs"/> 毫秒，
/// 不同 IMEI 之间互不影响、完全并行。
///
/// 线程安全：内部使用 <see cref="ConcurrentDictionary{TKey,TValue}"/> + 每 IMEI 独立
/// <see cref="SemaphoreSlim"/>，保证同一 IMEI 的等待串行化。
/// </summary>
public sealed class AnShengCommandThrottle : IDisposable
{
    /// <summary>默认最小间隔（毫秒）。</summary>
    public const int DefaultMinIntervalMs = 100;

    /// <summary>空闲条目回收阈值：超过该时长未使用的 IMEI 槽位会被清理。</summary>
    private static readonly TimeSpan IdleEvictionThreshold = TimeSpan.FromMinutes(30);

    private readonly ConcurrentDictionary<string, ImeiSlot> _slots = new(StringComparer.Ordinal);
    private readonly int _minIntervalMs;
    private long _lastCleanupTicks;
    private bool _disposed;

    /// <summary>同一 IMEI 两次下发之间的最小间隔（毫秒）。</summary>
    public int MinIntervalMs => _minIntervalMs;

    /// <summary>当前被跟踪的 IMEI 数量。</summary>
    public int TrackedImeiCount => _slots.Count;

    /// <summary>
    /// 创建限流器。
    /// </summary>
    /// <param name="minIntervalMs">最小间隔毫秒数，小于 0 时按 0 处理；默认 100。</param>
    public AnShengCommandThrottle(int minIntervalMs = DefaultMinIntervalMs)
    {
        _minIntervalMs = minIntervalMs < 0 ? 0 : minIntervalMs;
        _lastCleanupTicks = Stopwatch.GetTimestamp();
    }

    /// <summary>
    /// 等待到可以向指定 IMEI 下发命令为止。
    /// 调用方在本方法返回后应立即执行 publish。
    /// </summary>
    /// <param name="imei">设备 IMEI，null/空串时不限流直接返回。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>实际等待的毫秒数。</returns>
    public async Task<int> WaitTurnAsync(string? imei, CancellationToken cancellationToken = default)
    {
        if (_disposed || _minIntervalMs == 0 || string.IsNullOrWhiteSpace(imei))
        {
            return 0;
        }

        var slot = _slots.GetOrAdd(imei, static _ => new ImeiSlot());

        await slot.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var waited = 0;
            var now = DateTime.UtcNow;

            if (slot.LastSentUtc != DateTime.MinValue)
            {
                var elapsed = (now - slot.LastSentUtc).TotalMilliseconds;
                if (elapsed < _minIntervalMs)
                {
                    var delayMs = (int)Math.Ceiling(_minIntervalMs - elapsed);
                    if (delayMs > 0)
                    {
                        await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                        waited = delayMs;
                    }
                }
            }

            slot.LastSentUtc = DateTime.UtcNow;
            return waited;
        }
        finally
        {
            slot.Gate.Release();
            TryCleanup();
        }
    }

    /// <summary>
    /// 重置指定 IMEI 的限流状态（例如设备重连后）。
    /// </summary>
    /// <param name="imei">设备 IMEI。</param>
    public void Reset(string? imei)
    {
        if (string.IsNullOrWhiteSpace(imei)) return;
        if (_slots.TryRemove(imei, out var slot))
        {
            slot.Gate.Dispose();
        }
    }

    /// <summary>
    /// 清空全部限流状态。
    /// </summary>
    public void Clear()
    {
        foreach (var key in _slots.Keys.ToList())
        {
            if (_slots.TryRemove(key, out var slot))
            {
                slot.Gate.Dispose();
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Clear();
    }

    /// <summary>
    /// 惰性清理长时间未使用的 IMEI 槽位，避免设备频繁更替导致内存增长。
    /// 每 5 分钟最多执行一次。
    /// </summary>
    private void TryCleanup()
    {
        var nowTicks = Stopwatch.GetTimestamp();
        var lastTicks = Interlocked.Read(ref _lastCleanupTicks);
        var elapsed = TimeSpan.FromSeconds((nowTicks - lastTicks) / (double)Stopwatch.Frequency);
        if (elapsed < TimeSpan.FromMinutes(5)) return;

        if (Interlocked.CompareExchange(ref _lastCleanupTicks, nowTicks, lastTicks) != lastTicks)
        {
            return;
        }

        var cutoff = DateTime.UtcNow - IdleEvictionThreshold;
        foreach (var pair in _slots.ToArray())
        {
            if (pair.Value.LastSentUtc >= cutoff) continue;
            if (pair.Value.Gate.CurrentCount == 0) continue; // 正在使用中，跳过
            if (_slots.TryRemove(pair.Key, out var removed))
            {
                removed.Gate.Dispose();
            }
        }
    }

    /// <summary>
    /// 单个 IMEI 的限流槽位。
    /// </summary>
    private sealed class ImeiSlot
    {
        /// <summary>串行化同一 IMEI 的等待。</summary>
        public SemaphoreSlim Gate { get; } = new(1, 1);

        /// <summary>上次实际下发时刻（UTC）。</summary>
        public DateTime LastSentUtc { get; set; } = DateTime.MinValue;
    }
}
