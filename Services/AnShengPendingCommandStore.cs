// 创建于 T6（最小实现），增强于 T7（TTL 清扫 / 命令记录 / 唤醒等待者）。
//
// T6 只实现进程内 ConcurrentDictionary + 惰性过期。
// T7 在<b>本文件</b>内增强（不新建文件）：后台清扫作业、写 AnShengCommandRecord、
// TaskCompletionSource 唤醒、超时置 Status=Timeout。

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IoTPlatform.Infrastructure.Protocol.AnSheng;
using IoTPlatform.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace IoTPlatform.Services;

/// <summary>
/// <see cref="IAnShengPendingCommandStore"/> 的进程内实现（决策 1）。
///
/// 【为什么不用 Redis】
///   当前平台是单实例部署，在途窗口只有几十秒，条目量级是「同时在途的命令数」（个位数到百级）。
///   引入 Redis 会带来连接管理、序列化、故障降级三类新问题，收益为零。
///   多实例部署的方案已登记为待办 W1，届时只需替换本类实现，接口与调用方不动。
///
/// 【为什么惰性过期而不是定时清扫】
///   过期条目的唯一危害是「让本该判 AutoReport 的报文误判为 Response」。
///   而这个判断本身就发生在 <see cref="IsInFlight"/> 里——在那里顺手摘掉，
///   既不需要额外线程，也不会有「清扫周期内的判定窗口」问题。
///   <see cref="SweepExpiredAsync"/> 提供给 T7 的后台作业用于回收内存，不是正确性所必需。
///
/// 【线程安全】全部状态在一个 <see cref="ConcurrentDictionary{TKey,TValue}"/> 内，
///   注册用 <c>AddOrUpdate</c> 的比较-替换语义，摘除用 <c>TryRemove</c>，无额外锁。
/// </summary>
public sealed class AnShengPendingCommandStore : IAnShengPendingCommandStore
{
    private readonly ConcurrentDictionary<string, PendingCommand> _pending =
        new(StringComparer.Ordinal);

    private readonly ILogger<AnShengPendingCommandStore> _logger;

    /// <summary>
    /// 构造在途命令表。
    /// </summary>
    /// <param name="logger">日志器。</param>
    public AnShengPendingCommandStore(ILogger<AnShengPendingCommandStore> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public int Count => _pending.Count;

    /// <summary>
    /// 组装字典 key。
    ///
    /// 【为什么必须带 imei】frameId 由设备侧生成，跨设备重复是常态
    /// （T7 验收 #4 就是「两台设备用相同 frameId 互不串扰」）。
    /// 只用 frameId 会造成跨设备串扰，且症状极难复现。
    /// </summary>
    /// <param name="imei">设备 IMEI。</param>
    /// <param name="frameId">帧 ID。</param>
    /// <returns>复合 key。</returns>
    public static string BuildKey(string imei, string frameId) => $"{imei}:{frameId}";

    /// <inheritdoc />
    public bool TryRegister(string imei, string frameId, PendingCommand cmd)
    {
        if (string.IsNullOrWhiteSpace(imei) || string.IsNullOrWhiteSpace(frameId) || cmd == null)
        {
            return false;
        }

        var key = BuildKey(imei, frameId);

        // 先尝试直接添加——绝大多数情况下 key 不存在，一次原子操作即可完成。
        if (_pending.TryAdd(key, cmd))
        {
            _logger.LogDebug(
                "[AnShengPending] 登记在途命令 imei={Imei} frameId={FrameId} method={Method} expiresAt={ExpiresAt:O}",
                imei, frameId, cmd.Method, cmd.ExpiresAt);
            return true;
        }

        // key 已存在：只有「已过期」才允许覆盖。
        // 未过期就覆盖会让先发的那条命令永远等不到应答关联，属于静默丢失。
        if (_pending.TryGetValue(key, out var existing) && existing.IsExpired)
        {
            // 用 TryUpdate 做比较-替换：若这期间别的线程已经改过，就放弃本次覆盖，
            // 避免「两个线程都认为自己覆盖成功」。
            if (_pending.TryUpdate(key, cmd, existing))
            {
                _logger.LogDebug(
                    "[AnShengPending] 覆盖已过期在途条目 imei={Imei} frameId={FrameId} method={Method}",
                    imei, frameId, cmd.Method);
                return true;
            }
        }

        _logger.LogWarning(
            "[AnShengPending] frameId 冲突，登记被拒绝 imei={Imei} frameId={FrameId} method={Method}",
            imei, frameId, cmd.Method);
        return false;
    }

    /// <inheritdoc />
    public bool IsInFlight(string imei, string frameId)
    {
        if (string.IsNullOrWhiteSpace(imei) || string.IsNullOrWhiteSpace(frameId))
        {
            return false;
        }

        var key = BuildKey(imei, frameId);
        if (!_pending.TryGetValue(key, out var cmd))
        {
            return false;
        }

        if (!cmd.IsExpired)
        {
            return true;
        }

        // 惰性过期：就地摘除。用带值比较的 TryRemove，避免摘掉别的线程刚放进来的新条目。
        RemoveIfSame(key, cmd);
        _logger.LogDebug(
            "[AnShengPending] 在途条目已过期并摘除 imei={Imei} frameId={FrameId}", imei, frameId);
        return false;
    }

    /// <inheritdoc />
    public Task<PendingCommand?> CompleteAsync(string imei, string frameId, AnShengMessage? response)
    {
        if (string.IsNullOrWhiteSpace(imei) || string.IsNullOrWhiteSpace(frameId))
        {
            return Task.FromResult<PendingCommand?>(null);
        }

        var key = BuildKey(imei, frameId);
        if (!_pending.TryRemove(key, out var cmd))
        {
            return Task.FromResult<PendingCommand?>(null);
        }

        if (cmd.IsExpired)
        {
            // 条目已过期：仍然摘掉（清理内存），但对调用方而言「没有匹配的在途命令」。
            _logger.LogDebug(
                "[AnShengPending] 摘除时发现条目已过期，按未命中处理 imei={Imei} frameId={FrameId}",
                imei, frameId);
            return Task.FromResult<PendingCommand?>(null);
        }

        _logger.LogDebug(
            "[AnShengPending] 摘除在途命令 imei={Imei} frameId={FrameId} method={Method} result={Result}",
            imei, frameId, cmd.Method, response?.Result ?? "(null)");

        // T7 增强锚点：此处补写 AnShengCommandRecord、唤醒 TaskCompletionSource。
        return Task.FromResult<PendingCommand?>(cmd);
    }

    /// <inheritdoc />
    public Task<int> SweepExpiredAsync(CancellationToken ct = default)
    {
        var removed = 0;

        // ToArray() 生成快照再遍历：ConcurrentDictionary 的枚举器虽然是弱一致的，
        // 但边遍历边删仍可能漏掉条目；快照的成本在百级条目下可忽略。
        foreach (var pair in _pending.ToArray())
        {
            ct.ThrowIfCancellationRequested();

            if (!pair.Value.IsExpired)
            {
                continue;
            }

            if (RemoveIfSame(pair.Key, pair.Value))
            {
                removed++;
            }
        }

        if (removed > 0)
        {
            _logger.LogInformation("[AnShengPending] 清扫过期在途条目 {Count} 条", removed);
        }

        return Task.FromResult(removed);
    }

    /// <inheritdoc />
    public void ClearAll() => _pending.Clear();

    /// <summary>
    /// 仅当字典里的值仍是 <paramref name="expected"/> 时才摘除。
    ///
    /// <c>ConcurrentDictionary.TryRemove(key, out _)</c> 是无条件删除，
    /// 在「A 线程判定过期 → B 线程重新登记 → A 线程执行删除」的交错下会误删新条目。
    /// <c>ICollection.Remove(KeyValuePair)</c> 重载带值比较，是 BCL 提供的原子 CAS 删除。
    /// </summary>
    /// <param name="key">复合 key。</param>
    /// <param name="expected">期望的当前值。</param>
    /// <returns>确实摘除返回 true。</returns>
    private bool RemoveIfSame(string key, PendingCommand expected)
    {
        var collection = (System.Collections.Generic.ICollection<
            System.Collections.Generic.KeyValuePair<string, PendingCommand>>)_pending;

        return collection.Remove(
            new System.Collections.Generic.KeyValuePair<string, PendingCommand>(key, expected));
    }
}
