// 创建于 T6（最小实现），增强于 T7-2（TCS 唤醒 / 详细清扫 / RecordId 承载）。
//
// T6 只实现进程内 ConcurrentDictionary + 惰性过期。
// T7-2 在<b>本文件</b>内增强（不新建文件）：
//   · 字典值从裸 PendingCommand 换成 PendingEntry（命令 + TaskCompletionSource 内聚）；
//   · RegisterAsync 建立等待凭据，TryRegister 退化为它的同步子集（同一份实现）；
//   · CompleteAsync / 惰性过期 / 清扫 / ClearAll 四条摘除路径全部兑现或取消 TCS。

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
/// 【为什么惰性过期而不是只靠定时清扫】
///   过期条目的唯一危害是「让本该判 AutoReport 的报文误判为 Response」。
///   而这个判断本身就发生在 <see cref="IsInFlight"/> 里——在那里顺手摘掉，
///   既不需要额外线程，也不会有「清扫周期内的判定窗口」问题。
///   <see cref="SweepExpiredAsync(DateTime, CancellationToken)"/> 面向 T7-4 的后台宿主，
///   职责是「回收内存 + 给超时命令写终态」，不是关联正确性所必需。
///
/// 【线程安全】全部状态在一个 <see cref="ConcurrentDictionary{TKey,TValue}"/> 内，
///   注册用 <c>TryAdd</c>/<c>TryUpdate</c> 的比较-替换语义，摘除用带值比较的 CAS 删除，无额外锁。
///
/// 【终态互斥】摘除动作（<c>TryRemove</c> / <see cref="RemoveIfSame"/>）是终态归属的唯一裁决点：
///   Router 与清扫宿主并发时，谁摘到条目谁才写终态，另一方拿到 null 后什么都不做。
/// </summary>
public sealed class AnShengPendingCommandStore : IAnShengPendingCommandStore
{
    /// <summary>
    /// 在途条目 = 命令快照 + 应答兑现凭据。
    ///
    /// 【为什么把 TCS 和命令绑在同一个对象里】
    ///   若分成两张字典（命令表 + 等待者表），摘除时就得跨两张表做原子操作——做不到。
    ///   内聚成一个不可变引用对象后，「摘掉条目」和「拿到它的 TCS」天然是同一次 <c>TryRemove</c>。
    /// </summary>
    private sealed class PendingEntry
    {
        /// <summary>
        /// 创建在途条目，并同时建立应答兑现凭据。
        /// </summary>
        /// <param name="command">在途命令快照。</param>
        public PendingEntry(PendingCommand command)
        {
            Command = command;

            // ⚠️ RunContinuationsAsynchronously 是硬性要求，不是优化项：
            //   缺了它，TrySetResult 会在调用线程（= MQTT 上行接收线程）上<b>同步</b>执行
            //   等待者的后续代码（可能含 EF 写库、HTTP 回调）。轻则拖慢整条上行管道，
            //   重则等待者内部再同步等待上行时直接死锁。这是 TCS 最经典的坑。
            Completion = new TaskCompletionSource<AnShengMessage?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        /// <summary>在途命令快照（不可变）。</summary>
        public PendingCommand Command { get; }

        /// <summary>应答兑现凭据。</summary>
        public TaskCompletionSource<AnShengMessage?> Completion { get; }
    }

    private readonly ConcurrentDictionary<string, PendingEntry> _inFlight =
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
    public int Count => _inFlight.Count;

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
        => TryRegisterCore(imei, frameId, cmd, out _);

    /// <inheritdoc />
    public Task<AnShengPendingRegistration> RegisterAsync(string imei, string frameId, PendingCommand cmd)
    {
        if (!TryRegisterCore(imei, frameId, cmd, out var entry) || entry == null)
        {
            return Task.FromResult(AnShengPendingRegistration.Rejected(imei ?? string.Empty, frameId ?? string.Empty));
        }

        return Task.FromResult(new AnShengPendingRegistration(
            imei, frameId, registered: true, entry.Completion.Task));
    }

    /// <summary>
    /// 登记的唯一实现，<see cref="TryRegister"/> 与 <see cref="RegisterAsync"/> 共用。
    ///
    /// 两个入口共用一份代码，是为了杜绝「同步版和异步版的覆盖策略慢慢漂移」这类隐患。
    /// </summary>
    /// <param name="imei">设备 IMEI。</param>
    /// <param name="frameId">帧 ID。</param>
    /// <param name="cmd">在途命令记录。</param>
    /// <param name="entry">登记成功时输出新建的条目；失败时为 null。</param>
    /// <returns>登记成功返回 true。</returns>
    private bool TryRegisterCore(string imei, string frameId, PendingCommand cmd, out PendingEntry? entry)
    {
        entry = null;

        if (string.IsNullOrWhiteSpace(imei) || string.IsNullOrWhiteSpace(frameId) || cmd == null)
        {
            return false;
        }

        var key = BuildKey(imei, frameId);
        var candidate = new PendingEntry(cmd);

        // 先尝试直接添加——绝大多数情况下 key 不存在，一次原子操作即可完成。
        if (_inFlight.TryAdd(key, candidate))
        {
            entry = candidate;
            _logger.LogDebug(
                "[AnShengPending] 登记在途命令 imei={Imei} frameId={FrameId} method={Method} recordId={RecordId} expiresAt={ExpiresAt:O}",
                imei, frameId, cmd.Method, cmd.RecordId, cmd.ExpiresAt);
            return true;
        }

        // key 已存在：只有「已过期」才允许覆盖。
        // 未过期就覆盖会让先发的那条命令永远等不到应答关联，属于静默丢失。
        if (_inFlight.TryGetValue(key, out var existing) && existing.Command.IsExpired)
        {
            // 用 TryUpdate 做比较-替换：若这期间别的线程已经改过，就放弃本次覆盖，
            // 避免「两个线程都认为自己覆盖成功」。
            if (_inFlight.TryUpdate(key, candidate, existing))
            {
                // 被顶掉的旧条目若还有等待者，必须叫醒，否则它会一直挂到进程结束。
                existing.Completion.TrySetCanceled();

                entry = candidate;
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
        if (!_inFlight.TryGetValue(key, out var entry))
        {
            return false;
        }

        if (!entry.Command.IsExpired)
        {
            return true;
        }

        // 惰性过期：就地摘除。用带值比较的删除，避免摘掉别的线程刚放进来的新条目。
        if (RemoveIfSame(key, entry))
        {
            entry.Completion.TrySetCanceled();
        }

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
        if (!_inFlight.TryRemove(key, out var entry))
        {
            return Task.FromResult<PendingCommand?>(null);
        }

        if (entry.Command.IsExpired)
        {
            // 条目已过期：仍然摘掉（清理内存），但对调用方而言「没有匹配的在途命令」，
            // 终态由清扫宿主写 Timeout。等待者按取消处理，不能让它挂着。
            entry.Completion.TrySetCanceled();
            _logger.LogDebug(
                "[AnShengPending] 摘除时发现条目已过期，按未命中处理 imei={Imei} frameId={FrameId}",
                imei, frameId);
            return Task.FromResult<PendingCommand?>(null);
        }

        // 兑现等待者。TCS 建在 RunContinuationsAsynchronously 之上，
        // 续体不会在当前（上行接收）线程上同步执行。
        entry.Completion.TrySetResult(response);

        _logger.LogDebug(
            "[AnShengPending] 摘除在途命令 imei={Imei} frameId={FrameId} method={Method} recordId={RecordId} result={Result}",
            imei, frameId, entry.Command.Method, entry.Command.RecordId, response?.Result ?? "(null)");

        return Task.FromResult<PendingCommand?>(entry.Command);
    }

    /// <inheritdoc />
    public async Task<int> SweepExpiredAsync(CancellationToken ct = default)
    {
        var expired = await SweepExpiredAsync(DateTime.UtcNow, ct).ConfigureAwait(false);
        return expired.Count;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PendingCommand>> SweepExpiredDetailedAsync(CancellationToken ct = default)
        => SweepExpiredAsync(DateTime.UtcNow, ct);

    /// <inheritdoc />
    public Task<IReadOnlyList<PendingCommand>> SweepExpiredAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        var expired = new List<PendingCommand>();

        // ToArray() 生成快照再遍历：ConcurrentDictionary 的枚举器虽然是弱一致的，
        // 但边遍历边删仍可能漏掉条目；快照的成本在百级条目下可忽略。
        foreach (var pair in _inFlight.ToArray())
        {
            ct.ThrowIfCancellationRequested();

            if (!pair.Value.Command.IsExpiredAt(nowUtc))
            {
                continue;
            }

            // 只有真正摘到条目的一方才把它计入结果——这就是「终态互斥」的落点：
            // 若 Router 在同一瞬间摘走了它，这里拿不到，也就不会再写 Timeout。
            if (!RemoveIfSame(pair.Key, pair.Value))
            {
                continue;
            }

            pair.Value.Completion.TrySetCanceled();
            expired.Add(pair.Value.Command);
        }

        if (expired.Count > 0)
        {
            _logger.LogInformation("[AnShengPending] 清扫过期在途条目 {Count} 条", expired.Count);
        }

        return Task.FromResult<IReadOnlyList<PendingCommand>>(expired);
    }

    /// <inheritdoc />
    public void ClearAll()
    {
        foreach (var pair in _inFlight.ToArray())
        {
            if (RemoveIfSame(pair.Key, pair.Value))
            {
                // 必须取消，否则上一个用例遗留的 await 会永久挂起，
                // 表现为「下一个用例莫名超时」这类最难排查的测试串扰。
                pair.Value.Completion.TrySetCanceled();
            }
        }

        // 兜底：清掉快照之后新插入的条目（测试隔离场景下不会有等待者）。
        _inFlight.Clear();
    }

    /// <summary>
    /// 仅当字典里的值仍是 <paramref name="expected"/> 时才摘除。
    ///
    /// <c>ConcurrentDictionary.TryRemove(key, out _)</c> 是无条件删除，
    /// 在「A 线程判定过期 → B 线程重新登记 → A 线程执行删除」的交错下会误删新条目。
    /// <c>ICollection.Remove(KeyValuePair)</c> 重载带值比较，是 BCL 提供的原子 CAS 删除；
    /// <see cref="PendingEntry"/> 是引用类型且不重写 Equals，比较即引用相等，正是所需语义。
    /// </summary>
    /// <param name="key">复合 key。</param>
    /// <param name="expected">期望的当前值。</param>
    /// <returns>确实摘除返回 true。</returns>
    private bool RemoveIfSame(string key, PendingEntry expected)
    {
        var collection = (ICollection<KeyValuePair<string, PendingEntry>>)_inFlight;
        return collection.Remove(new KeyValuePair<string, PendingEntry>(key, expected));
    }
}
