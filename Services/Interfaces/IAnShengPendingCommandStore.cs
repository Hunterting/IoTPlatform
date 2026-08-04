// 创建于 T6（最小实现），增强于 T7-2（TCS 唤醒 / 命令记录联动 / 详细清扫）。
//
// T6 边界（决策 1）：只做「注册 / 查在途 / 摘除 / 惰性过期」四件事，
// 目的是让 AnShengMessageRouter 的 Response 分支能被实现与测试。
//
// T7-2 增强（本文件与 AnShengPendingCommandStore.cs，不新建文件）：
//   · PendingCommand 补 RecordId / Ttl —— 清扫宿主要靠 RecordId 才能回填 AnShengCommandRecord；
//   · 新增 RegisterAsync：登记的同时建立 TaskCompletionSource，让「同步等待应答」成为可能；
//   · 新增 SweepExpiredAsync(nowUtc, ct) 重载：返回被清出的条目集合（旧的 int 版本保留并委托它）；
//   · CompleteAsync / ClearAll / 清扫 三条路径都必须兑现或取消 TCS，杜绝永久挂起的等待者。
//
// ⚠️ T6 既有签名（TryRegister / IsInFlight / CompleteAsync / SweepExpiredAsync(ct) / ClearAll / Count）
//    一律<b>保留不改名</b>：AnShengMessageRouter.HandleResponseAsync 已在调用，
//    改名等于破坏 T6 已过 QA 的代码。

using System;
using System.Collections.Generic;
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
    /// <summary>
    /// 对应的 <c>AnShengCommandRecord.Id</c>（数据库主键）。
    ///
    /// 【为什么在途条目要带库主键】超时清扫宿主拿到过期条目后，必须把记录置为
    /// <c>Status=Timeout</c>。若只有 CommandId（业务 GUID）就得再查一次库；
    /// 带上主键可直接按 PK 定位，且能配合 <c>IgnoreQueryFilters</c> 绕开多租户全局过滤器
    /// （后台线程没有 AppCode 上下文，走过滤器会一行都查不到）。
    ///
    /// 未落库（T6 遗留调用、纯内存测试）时为 0，清扫宿主据此跳过回填。
    /// </summary>
    public long RecordId { get; init; }

    /// <summary>
    /// 本条命令的存活时长，即 <see cref="ExpiresAt"/> - <see cref="SentAt"/>。
    ///
    /// 冗余保存而不是每次相减，是为了让日志与诊断能直接读出「这条命令的 TTL 配的是 30s 还是 60s」
    /// （长耗时命令 getLogs / getEMStatistics 走 60s，见 <c>AnShengCommandOptions.ResolveTtl</c>）。
    /// </summary>
    public TimeSpan Ttl { get; init; }

    /// <summary>是否已过期（以当前 UTC 时刻判断）。</summary>
    public bool IsExpired => IsExpiredAt(DateTime.UtcNow);

    /// <summary>
    /// 是否相对指定时刻已过期。
    ///
    /// 清扫宿主一轮扫描内应使用<b>同一个</b> nowUtc，避免「扫到一半时钟往前走」
    /// 导致同一轮内前后条目的判定基准不一致。
    /// </summary>
    /// <param name="nowUtc">判定基准时刻（UTC）。</param>
    /// <returns>已过期返回 true。</returns>
    public bool IsExpiredAt(DateTime nowUtc) => nowUtc > ExpiresAt;

    /// <summary>
    /// 以「现在 + ttl」构造一条在途命令，省去调用方各自算过期时刻。
    /// </summary>
    /// <param name="commandId">平台命令主键。</param>
    /// <param name="imei">目标设备 IMEI。</param>
    /// <param name="frameId">帧 ID。</param>
    /// <param name="method">命令方法名。</param>
    /// <param name="ttl">存活时长。</param>
    /// <param name="recordId">对应的 <c>AnShengCommandRecord.Id</c>；未落库时传 0。</param>
    /// <returns>在途命令记录。</returns>
    public static PendingCommand Create(
        long commandId,
        string imei,
        string frameId,
        string method,
        TimeSpan ttl,
        long recordId = 0)
    {
        var now = DateTime.UtcNow;
        return new PendingCommand(commandId, imei, frameId, method, now, now.Add(ttl))
        {
            RecordId = recordId,
            Ttl = ttl
        };
    }
}

/// <summary>
/// 一次在途登记的结果 —— 既告诉调用方「登记成没成」，也给出「应答到达时会被兑现的凭据」。
///
/// 【为什么暴露 <c>Task</c> 而不是 <c>TaskCompletionSource</c>】
///   TCS 是「兑现方」的能力，只能属于在途表；调用方只应有「等待」的能力。
///   返回 <c>Task&lt;T&gt;</c> 还留出了分布式实现的空间：将来换成
///   「Redis Pub/Sub + 本地 TCS」或长轮询，只要兑现同一个 <c>Task&lt;T&gt;</c> 契约，调用方零改动。
/// </summary>
public sealed class AnShengPendingRegistration
{
    /// <summary>一个恒为「已取消」的应答任务，用于登记失败的场景。</summary>
    private static readonly Task<AnShengMessage?> CanceledCompletion =
        Task.FromCanceled<AnShengMessage?>(new CancellationToken(canceled: true));

    /// <summary>
    /// 构造登记结果。
    /// </summary>
    /// <param name="imei">设备 IMEI。</param>
    /// <param name="frameId">帧 ID。</param>
    /// <param name="registered">是否登记成功。</param>
    /// <param name="completion">应答兑现任务。</param>
    public AnShengPendingRegistration(
        string imei,
        string frameId,
        bool registered,
        Task<AnShengMessage?> completion)
    {
        Imei = imei ?? string.Empty;
        FrameId = frameId ?? string.Empty;
        Registered = registered;
        Completion = completion ?? CanceledCompletion;
    }

    /// <summary>设备 IMEI。</summary>
    public string Imei { get; }

    /// <summary>帧 ID。</summary>
    public string FrameId { get; }

    /// <summary>
    /// 是否登记成功。
    /// <c>false</c> 表示同 (imei, frameId) 已有未过期的在途条目，本次下发<b>不应继续</b>。
    /// </summary>
    public bool Registered { get; }

    /// <summary>
    /// 应答兑现任务：
    ///   · 收到应答 → 结果为该应答报文（解析失败时为 <c>null</c>）；
    ///   · 超时被清扫 / 条目被覆盖 / <c>ClearAll</c> → 任务被<b>取消</b>。
    /// 登记失败时是一个已取消的任务，绝不会让调用方永久挂起。
    /// </summary>
    public Task<AnShengMessage?> Completion { get; }

    /// <summary>
    /// 构造一个「登记失败」的结果。
    /// </summary>
    /// <param name="imei">设备 IMEI。</param>
    /// <param name="frameId">帧 ID。</param>
    /// <returns>Registered=false 且 Completion 已取消的登记结果。</returns>
    public static AnShengPendingRegistration Rejected(string imei, string frameId)
        => new(imei, frameId, registered: false, CanceledCompletion);

    /// <summary>
    /// 在给定时限内等待设备应答。
    ///
    /// 【为什么不抛异常】超时不是「错误」而是「业务终态之一」（Status=Timeout，由清扫宿主写入）。
    /// 让调用方用 try/catch 表达正常分支既啰嗦又易漏，因此统一以 <c>null</c> 表示「没等到」。
    ///
    /// 【为什么不用 Task.WaitAsync(TimeSpan)】BCL 版本超时会抛 <c>TimeoutException</c>，
    /// 与上面的口径相反；且它对「已取消」的任务仍会抛 <c>TaskCanceledException</c>。
    /// </summary>
    /// <param name="timeout">最长等待时长；小于等于零表示不等待，立即返回当前状态。</param>
    /// <param name="cancellationToken">取消令牌（取消后按「没等到」处理）。</param>
    /// <returns>应答报文；超时、被取消或登记失败时返回 <c>null</c>。</returns>
    public async Task<AnShengMessage?> WaitAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (!Registered)
        {
            return null;
        }

        if (Completion.IsCompleted)
        {
            return Completion.IsCompletedSuccessfully ? Completion.Result : null;
        }

        if (timeout <= TimeSpan.Zero)
        {
            return null;
        }

        // 用链接令牌把 Task.Delay 的定时器和外部取消绑在一起：
        // 应答先到时立刻 Cancel，避免定时器在 timeout 到期前一直挂在计时队列上。
        using var timerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var delayTask = Task.Delay(timeout, timerCts.Token);

        var winner = await Task.WhenAny(Completion, delayTask).ConfigureAwait(false);
        if (!ReferenceEquals(winner, Completion))
        {
            // 超时或被外部取消：不抛异常，交由清扫宿主写 Timeout 终态。
            return null;
        }

        timerCts.Cancel();
        return Completion.IsCompletedSuccessfully ? Completion.Result : null;
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
///   ⚠️ 多实例部署下本实现会失效（A 实例发命令、B 实例收应答），已登记为待办 W1；
///   届时超时兜底仍然生效（各实例各扫各的），只是应答关联会退化。
///
/// 【T7 写入方】<c>AnShengCommandService</c> 在下发前调用 <see cref="RegisterAsync"/>；
///   <c>AnShengMessageRouter</c> 收到应答时调用 <see cref="CompleteAsync"/>；
///   <c>AnShengCommandSweepHostedService</c> 周期性调用 <see cref="SweepExpiredDetailedAsync"/>。
/// </summary>
public interface IAnShengPendingCommandStore
{
    /// <summary>
    /// 登记一条在途命令（同步子集，<b>丢弃</b>应答等待凭据）。
    ///
    /// 内部与 <see cref="RegisterAsync"/> 共用同一份实现，只是不把
    /// <c>TaskCompletionSource</c> 交给调用方，适用于「发完就走、靠 Router 回填记录」的路径。
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
    /// 登记一条在途命令，并返回<b>应答兑现凭据</b>（T7 主路径）。
    ///
    /// 「先登记后下发」的前半步：必须在 <c>IAnShengDownlinkPort.PublishAsync</c> <b>之前</b>调用，
    /// 否则设备应答可能先于登记到达，被路由误判为主动上报（硬约束 N1）。
    /// 若随后的下发失败，调用方应立即 <see cref="CompleteAsync"/> 摘除本条目，避免占位到 TTL。
    /// </summary>
    /// <param name="imei">目标设备 IMEI。</param>
    /// <param name="frameId">帧 ID。</param>
    /// <param name="cmd">在途命令记录。</param>
    /// <returns>登记结果；<c>Registered=false</c> 时不应继续下发。</returns>
    Task<AnShengPendingRegistration> RegisterAsync(string imei, string frameId, PendingCommand cmd);

    /// <summary>
    /// 判断某帧是否在途。<b>带惰性过期</b>：命中但已过期的条目会被就地摘除并返回 <c>false</c>。
    /// </summary>
    /// <param name="imei">设备 IMEI。</param>
    /// <param name="frameId">帧 ID。</param>
    /// <returns>在途且未过期返回 <c>true</c>。</returns>
    bool IsInFlight(string imei, string frameId);

    /// <summary>
    /// 摘除一条在途命令（收到应答时调用），并兑现该条目的应答等待者。
    ///
    /// 【终态互斥】本方法的 <c>TryRemove</c> 就是终态归属的 CAS 点：
    /// 谁摘到条目谁才有权写终态。Router 摘到 → 写 Succeeded/Failed；
    /// 清扫宿主摘到 → 写 Timeout。返回 <c>null</c> 表示没摘到，调用方<b>不得</b>写终态。
    /// </summary>
    /// <param name="imei">设备 IMEI。</param>
    /// <param name="frameId">帧 ID。</param>
    /// <param name="response">应答报文；可为 null（解析失败仍应摘条目，否则会一直占位到 TTL）。</param>
    /// <returns>被摘除的条目；不存在或已过期返回 <c>null</c>。</returns>
    Task<PendingCommand?> CompleteAsync(string imei, string frameId, AnShengMessage? response);

    /// <summary>
    /// 清扫全部已过期条目，返回清掉的条数。
    ///
    /// T6 遗留签名，内部委托 <see cref="SweepExpiredAsync(DateTime, CancellationToken)"/>。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>被清掉的条目数。</returns>
    Task<int> SweepExpiredAsync(CancellationToken ct = default);

    /// <summary>
    /// 以指定时刻为基准清扫过期条目，并返回<b>被清出的条目集合</b>。
    ///
    /// 【为什么要返回集合】清扫宿主需要每条的 <c>RecordId</c> 才能把
    /// <c>AnShengCommandRecord</c> 置为 <c>Status=Timeout</c>；只拿到条数无法回填。
    /// 【为什么 nowUtc 由调用方传入】一轮扫描共用同一基准时刻，判定可复现，也便于测试注入。
    /// </summary>
    /// <param name="nowUtc">判定基准时刻（UTC）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>被清出的条目集合；无过期条目时为空集合，永不为 null。</returns>
    Task<IReadOnlyList<PendingCommand>> SweepExpiredAsync(DateTime nowUtc, CancellationToken ct = default);

    /// <summary>
    /// <see cref="SweepExpiredAsync(DateTime, CancellationToken)"/> 的便捷形式，基准时刻取当前 UTC。
    /// 供清扫宿主与单元测试直接调用，避免每处都写 <c>DateTime.UtcNow</c>。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>被清出的条目集合。</returns>
    Task<IReadOnlyList<PendingCommand>> SweepExpiredDetailedAsync(CancellationToken ct = default);

    /// <summary>
    /// 清空全部条目，并<b>取消</b>每个条目的应答等待者。<b>仅供测试隔离使用</b>（见 <c>StaticStateResetter</c>）。
    ///
    /// 不取消等待者会让上一个用例遗留的 <c>await</c> 永久挂起，
    /// 表现为「某个测试偶发超时，且总是下一个用例受害」这类最难排查的串扰。
    /// </summary>
    void ClearAll();

    /// <summary>当前条目数（含尚未被惰性摘除的过期条目）。</summary>
    int Count { get; }
}
