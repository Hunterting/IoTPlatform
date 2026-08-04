// 创建于 T6（最小实现），增强于 T7（TTL 清扫 / 命令记录 / 唤醒等待者）。
//
// T6 边界（决策 1）：只做「注册 / 查在途 / 摘除 / 惰性过期」四件事，
// 目的是让 AnShengMessageRouter 的 Response 分支能被实现与测试。
// T7 增强点（改本文件与 AnShengPendingCommandStore.cs，不新建文件）：
//   · 后台清扫 IHostedService；
//   · 写 AnShengCommandRecord；
//   · TaskCompletionSource 唤醒同步等待者；
//   · 超时置 Status = Timeout。

using System;
using System.Threading;
using System.Threading.Tasks;
using IoTPlatform.Infrastructure.Protocol.AnSheng;

namespace IoTPlatform.Services.Interfaces;

/// <summary>
/// 一条在途（已下发、尚未收到应答）的安圣命令。
/// </summary>
/// <param name="CommandId">平台命令主键。T6 阶段测试可传 0；T7 由 <c>AnShengCommandService</c> 填真实值。</param>
/// <param name="Imei">目标设备 IMEI。</param>
/// <param name="FrameId">下发时生成的 16 位帧 ID。</param>
/// <param name="Method">命令方法名，如 <c>getDevStatus</c>。</param>
/// <param name="SentAt">下发时刻（UTC）。</param>
/// <param name="ExpiresAt">过期时刻（UTC）。到期后条目被惰性摘除，对应上行退化为 AutoReport。</param>
public sealed record PendingCommand(
    long CommandId,
    string Imei,
    string FrameId,
    string Method,
    DateTime SentAt,
    DateTime ExpiresAt)
{
    /// <summary>是否已过期（以当前 UTC 时刻判断）。</summary>
    public bool IsExpired => DateTime.UtcNow > ExpiresAt;

    /// <summary>
    /// 以「现在 + ttl」构造一条在途命令，省去调用方各自算过期时刻。
    /// </summary>
    /// <param name="commandId">平台命令主键。</param>
    /// <param name="imei">目标设备 IMEI。</param>
    /// <param name="frameId">帧 ID。</param>
    /// <param name="method">命令方法名。</param>
    /// <param name="ttl">存活时长。</param>
    /// <returns>在途命令记录。</returns>
    public static PendingCommand Create(
        long commandId,
        string imei,
        string frameId,
        string method,
        TimeSpan ttl)
    {
        var now = DateTime.UtcNow;
        return new PendingCommand(commandId, imei, frameId, method, now, now.Add(ttl));
    }
}

/// <summary>
/// 在途命令表 —— 「这条上行是不是我刚发出去那条命令的应答」的唯一判据来源。
///
/// 【为什么由 T6 定义而不是 T7】
///   接口本质上是「路由需要的能力契约」。由消费方（T6 的 <c>AnShengMessageRouter</c>）定义，
///   比由实现方（T7 的命令服务）定义更符合依赖倒置；
///   同时避免同一文件被两个任务先后新建导致的合并冲突。
///
/// 【key 设计】<c>$"{imei}:{frameId}"</c>。
///   frameId 由各设备各自生成，跨设备重复是常态。只用 frameId 做 key 会导致
///   「A 设备的应答摘掉了 B 设备的在途条目」这类极难复现的串扰。
///
/// 【生命周期】Singleton。进程内共享，不注入任何 Scoped 服务。
///   ⚠️ 多实例部署下本实现会失效（A 实例发命令、B 实例收应答），已登记为待办 W1。
///
/// 【T6 阶段的实际状态】生产环境写入方是 T7 的 <c>AnShengCommandService</c>，
///   T6 阶段在途表恒为空 ⇒ 所有带 frameId 的非事件报文都判 AutoReport，
///   与当前线上行为完全一致，<b>零回归</b>。
/// </summary>
public interface IAnShengPendingCommandStore
{
    /// <summary>
    /// 登记一条在途命令。
    /// </summary>
    /// <param name="imei">目标设备 IMEI。</param>
    /// <param name="frameId">帧 ID。</param>
    /// <param name="cmd">在途命令记录。</param>
    /// <returns>
    /// 登记成功返回 <c>true</c>；同 key 已存在<b>且未过期</b>返回 <c>false</c>。
    /// 同 key 已存在但已过期时，视为可覆盖，返回 <c>true</c>。
    /// </returns>
    bool TryRegister(string imei, string frameId, PendingCommand cmd);

    /// <summary>
    /// 判断某帧是否在途。<b>带惰性过期</b>：命中但已过期的条目会被就地摘除并返回 <c>false</c>。
    /// </summary>
    /// <param name="imei">设备 IMEI。</param>
    /// <param name="frameId">帧 ID。</param>
    /// <returns>在途且未过期返回 <c>true</c>。</returns>
    bool IsInFlight(string imei, string frameId);

    /// <summary>
    /// 摘除一条在途命令（收到应答时调用）。
    ///
    /// T6 实现只做「摘条目」；T7 会在此处补写 <c>AnShengCommandRecord</c> 与唤醒等待者。
    /// 之所以现在就定义成 <c>Task</c>，是为了 T7 增强时<b>不改签名、不改调用方</b>。
    /// </summary>
    /// <param name="imei">设备 IMEI。</param>
    /// <param name="frameId">帧 ID。</param>
    /// <param name="response">应答报文；可为 null（解析失败仍应摘条目，否则会一直占位到 TTL）。</param>
    /// <returns>被摘除的条目；不存在或已过期返回 <c>null</c>。</returns>
    Task<PendingCommand?> CompleteAsync(string imei, string frameId, AnShengMessage? response);

    /// <summary>
    /// 清扫全部已过期条目。
    ///
    /// T6 提供实现但<b>不挂后台作业</b>——在途表恒为空，惰性过期已足够；
    /// T7 会用 <c>IHostedService</c> 周期性调用它，并把超时命令置 <c>Status=Timeout</c>。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>被清掉的条目数。</returns>
    Task<int> SweepExpiredAsync(CancellationToken ct = default);

    /// <summary>清空全部条目。<b>仅供测试隔离使用</b>（见 <c>StaticStateResetter</c>）。</summary>
    void ClearAll();

    /// <summary>当前条目数（含尚未被惰性摘除的过期条目）。</summary>
    int Count { get; }
}
