using System.Collections.Concurrent;
using IoTPlatform.Infrastructure.Protocol;
using IoTPlatform.Infrastructure.Protocol.Adapters;

namespace IoTPlatform.IntegrationTests.Infrastructure.Mqtt;

/// <summary>
/// <see cref="IProtocolAdapterFactory"/> 的测试替身（架构方案 §3.5）。
///
/// 【与生产实现的关键差异】
///   生产 <c>ProtocolAdapterFactory.GetAdapter(configId)</c> 在缓存未命中时返回 <c>null</c>，
///   而 <c>AnShengCommandService</c> 会因此直接返回「适配器未启动」。
///   测试里协议配置 Id 由 MySQL 自增生成、事先不可知，所以本替身
///   <b>默认对任意 configId 都返回同一个 <see cref="DefaultAdapter"/></b>，不返回 null。
///   这样用例无需关心真实 Id 就能走通下发链路。
///
/// 【为什么用单例默认适配器】
///   断言点集中在一处（<c>Fixture.Adapter.Sent</c>），避免用例先去猜「该断言哪个 configId 的适配器」。
///   确有多适配器串扰场景时，用 <see cref="GetOrCreateFor"/> 显式取分身。
///
/// ═══════════════════════════════════════════════════════════════════════
/// 【模拟进程重启开关 —— 为「短路判活」加固而增】
/// ═══════════════════════════════════════════════════════════════════════
///   <c>ProtocolConfigService</c> 的启停短路已从「只看 DB 的 Status」改为
///   「DB 状态 + 进程内适配器」双条件判活，新增了两条只有在<b>状态失配</b>时才走到的分支：
///     · 启动恢复：<c>Status=="active"</c> 但 <c>GetAdapter==null</c>（进程重启后适配器没恢复）→ 重新拉起；
///     · 停止清理：<c>Status=="inactive"</c> 但 <c>GetAdapter!=null</c>（残留适配器）→ 完整清理。
///   而本替身原先的 <c>GetAdapter</c> 恒返回非 null，<b>物理上无法构造出这两个失配现场</b>，
///   于是这两条分支在集成测试里覆盖率为零 —— 加固代码写了等于没验。
///
///   下列三个 <c>Simulate*</c> 开关就是用来制造失配现场的：
///     · <see cref="SimulateAdapterAbsent"/>  → 让 <c>GetAdapter</c> 对该 configId 返回 null；
///     · <see cref="SimulateAdapterPresent"/> → 撤销上一条，恢复默认；
///     · <see cref="SimulateAdapterRebuilt"/> → 让该 configId 换成<b>另一个实例</b>，
///       用于驱动订阅去重的「② 旧实例 → 解绑重挂」分支。
///   全部<b>默认关闭</b>：不调用任何 <c>Simulate*</c> 时，本类行为与加固前逐字相同，
///   既有用例零感知。<see cref="Reset"/> 会一并复位这些开关，避免跨用例泄漏。
/// </summary>
public sealed class FakeProtocolAdapterFactory : IProtocolAdapterFactory, IDisposable
{
    private readonly ConcurrentDictionary<int, RecordingAnShengAdapter> _extraAdapters = new();

    /// <summary>
    /// 被标记为「不在内存」的 configId 集合（值无意义，仅借 ConcurrentDictionary 当并发 Set 用）。
    /// 命中者 <see cref="GetAdapter"/> 返回 null，模拟进程重启后适配器尚未恢复。
    /// </summary>
    private readonly ConcurrentDictionary<int, byte> _absentConfigIds = new();

    /// <summary>
    /// 被 <see cref="SimulateAdapterRebuilt"/> 换下来的旧实例。
    ///
    /// 【为什么不当场 Dispose】
    ///   用例正是要拿着旧实例投递 <c>RaiseDataReceived</c>，验证「旧订阅确实被解绑、
    ///   不再有幽灵数据流入采集管道」。当场销毁就断了断言的手。
    ///   统一留到 <see cref="Reset"/> 时清理，既不泄漏也不妨碍断言。
    /// </summary>
    private readonly ConcurrentBag<RecordingAnShengAdapter> _retiredAdapters = new();

    /// <summary>已被 <see cref="ReleaseAdapter"/> 释放过的 configId，按发生顺序。</summary>
    private readonly ConcurrentQueue<int> _released = new();

    private bool _disposed;

    public FakeProtocolAdapterFactory()
    {
        DefaultAdapter = new RecordingAnShengAdapter(SharedTestConstants.ProtocolConfigId);
    }

    /// <summary>
    /// 缺省录制适配器。<c>GetAdapter</c>/<c>CreateAdapter</c> 在未显式登记分身时一律返回它。
    /// </summary>
    public RecordingAnShengAdapter DefaultAdapter { get; }

    /// <summary>
    /// 已录制的 <see cref="ReleaseAdapter"/> 调用序列（按发生顺序，可能含重复 configId）。
    ///
    /// 【用途】<c>StopProtocolAsync</c> 的「残留适配器清理」分支唯一的外部可观测副作用就是
    /// 释放适配器。本替身的 <see cref="DefaultAdapter"/> 释放后仍会被 <c>Resolve</c> 返回
    /// （生命周期与 TestServer 一致，见 <see cref="ReleaseAdapter"/>），
    /// 所以用例不能靠「释放后 GetAdapter 变 null」来断言，只能查这份调用留痕。
    /// </summary>
    public IReadOnlyList<int> ReleasedConfigIds => _released.ToArray();

    /// <inheritdoc />
    public IProtocolAdapter CreateAdapter(string protocolType, int configId)
    {
        // 与生产工厂语义对齐：创建即入缓存，此后 GetAdapter 必然非 null。
        // 因此这里要撤销「不在内存」标记，否则「恢复启动」用例跑完会留下一个
        // 「刚启动成功、GetAdapter 却仍说没有」的自相矛盾现场，
        // 紧随其后的 StopProtocolAsync 会被短路吞掉，用例作者根本看不出原因。
        _absentConfigIds.TryRemove(configId, out _);

        // 不按 protocolType 分流：测试里只关心安圣链路，其余协议同样落到录制替身，
        // 以免无关的后台代码路径因 NotSupportedException 炸掉整个 TestServer。
        return Resolve(configId);
    }

    /// <inheritdoc />
    public IProtocolAdapter? GetAdapter(int configId)
        => _absentConfigIds.ContainsKey(configId) ? null : Resolve(configId);

    /// <inheritdoc />
    public void ReleaseAdapter(int configId)
    {
        _released.Enqueue(configId);

        if (_extraAdapters.TryRemove(configId, out var adapter))
        {
            adapter.Dispose();
        }

        // 默认适配器不销毁：它的生命周期与 TestServer 一致，仅在用例间 Reset()。
        //
        // 【为什么释放后不自动置为「不在内存」】
        //   那会改变既有 919 条用例的默认行为：任何走过 ReleaseAdapter 的 configId
        //   随后 GetAdapter 都变 null，下发链路会集体退化成「适配器未启动」。
        //   需要「释放后不在内存」这个现场的用例，请显式调 SimulateAdapterAbsent(configId)。
    }

    /// <inheritdoc />
    public void ReleaseAll()
    {
        foreach (var key in _extraAdapters.Keys.ToList())
        {
            ReleaseAdapter(key);
        }

        DefaultAdapter.Reset();
    }

    /// <summary>
    /// 为指定 configId 显式登记一个独立分身（多设备/多配置串扰类用例专用）。
    /// 一旦登记，该 configId 不再落到 <see cref="DefaultAdapter"/>。
    /// </summary>
    public RecordingAnShengAdapter GetOrCreateFor(int configId) =>
        _extraAdapters.GetOrAdd(configId, id => new RecordingAnShengAdapter(id));

    #region ── 模拟进程重启：驱动「短路判活」与「订阅去重」的失配分支 ──

    /// <summary>
    /// 模拟「适配器不在进程内存里」：此后 <see cref="GetAdapter"/> 对该 configId 返回 <c>null</c>。
    ///
    /// 【对应的真实故障】
    ///   进程重启后 <c>ProtocolAdapterFactory._adapters</c> 清空，而库里的 <c>Status</c> 仍是 active，
    ///   两者失配。加固前只看 Status 的短路会把整个启动动作吞掉：接口返回 200、适配器却没起来。
    ///
    /// 【典型用法（覆盖缺口 A：判活恢复）】
    /// <code>
    /// Fixture.AdapterFactory.SimulateAdapterAbsent((int)Seed.ProtocolConfigId);
    /// // 库里保持 Status="active"，不要改成 inactive
    /// await protocolService.StartProtocolAsync(Seed.ProtocolConfigId, AppCode);
    /// // 断言：真的走了恢复启动，而不是短路返回
    /// Adapter.DataCollectionStarted.Should().BeTrue();
    /// </code>
    ///
    /// 【注意】<see cref="CreateAdapter"/> 会自动撤销该标记（创建即入缓存，与生产工厂一致）。
    /// 若要连续两次都命中「不在内存」，需在两次之间再调一次本方法。
    /// </summary>
    /// <param name="configId">协议配置主键。</param>
    public void SimulateAdapterAbsent(int configId) => _absentConfigIds[configId] = 0;

    /// <summary>
    /// 撤销 <see cref="SimulateAdapterAbsent"/>，让该 configId 恢复「内存里有适配器」的默认行为。
    /// 对从未标记过的 configId 调用是安全的空操作。
    /// </summary>
    /// <param name="configId">协议配置主键。</param>
    public void SimulateAdapterPresent(int configId) => _absentConfigIds.TryRemove(configId, out _);

    /// <summary>
    /// 查询某个 configId 当前是否被标记为「不在内存」。供用例做前置条件自检。
    /// </summary>
    /// <param name="configId">协议配置主键。</param>
    /// <returns>已标记返回 <c>true</c>。</returns>
    public bool IsSimulatedAbsent(int configId) => _absentConfigIds.ContainsKey(configId);

    /// <summary>
    /// 模拟「适配器被重建为新实例」：为该 configId 换上一个全新的录制适配器并返回它。
    ///
    /// 【对应的真实故障 —— 订阅去重的分支 ②】
    ///   进程内 stop→start、或进程重启后恢复启动，适配器会是<b>另一个对象</b>。
    ///   若 <c>ProtocolConfigService</c> 反注册时按 configId 重新 <c>GetAdapter</c> 取实例，
    ///   <c>-=</c> 就作用在错误的对象上：旧实例的 handler 永远摘不掉，成为幽灵订阅继续灌数据。
    ///   加固后改为用登记时保存的实例引用反注册，本方法正是用来验证这一点的。
    ///
    /// 【旧实例不会被销毁】
    ///   调用方在重建前持有的引用（<see cref="DefaultAdapter"/> 或上一个分身）依然可用，
    ///   请用它投递 <c>RaiseDataReceived</c> 来断言「旧订阅已解绑、不再产生落库副作用」。
    ///
    /// 【典型用法（覆盖缺口 C 的分支 ②）】
    /// <code>
    /// var oldAdapter = Fixture.Adapter;                       // 第一次启动时挂载的实例
    /// var newAdapter = Fixture.AdapterFactory.SimulateAdapterRebuilt(configId);
    /// await protocolService.StartProtocolAsync(id, AppCode);  // 触发「旧实例解绑 → 新实例挂载」
    /// oldAdapter.RaiseDataReceived(...);                      // 断言：不再落库（幽灵订阅已消除）
    /// newAdapter.RaiseDataReceived(...);                      // 断言：正常落库
    /// </code>
    ///
    /// 【副作用】该 configId 此后不再落到 <see cref="DefaultAdapter"/>；
    /// 同时自动撤销「不在内存」标记（重建意味着内存里现在有实例了）。
    /// </summary>
    /// <param name="configId">协议配置主键。</param>
    /// <returns>新建的录制适配器实例，供用例直接持有并断言。</returns>
    public RecordingAnShengAdapter SimulateAdapterRebuilt(int configId)
    {
        var rebuilt = new RecordingAnShengAdapter(configId);

        _extraAdapters.AddOrUpdate(
            configId,
            _ => rebuilt,
            (_, previous) =>
            {
                // 留到 Reset 统一销毁：用例还要用它断言「旧订阅已失效」。
                _retiredAdapters.Add(previous);
                return rebuilt;
            });

        _absentConfigIds.TryRemove(configId, out _);
        return rebuilt;
    }

    /// <summary>
    /// 按当前模拟开关解析该 configId 对应的录制适配器（不创建新实例）。
    ///
    /// 语义与 <see cref="GetAdapter"/> 完全一致，只是返回强类型，
    /// 免去用例写 <c>as RecordingAnShengAdapter</c>。被标记为「不在内存」时返回 <c>null</c>。
    /// </summary>
    /// <param name="configId">协议配置主键。</param>
    /// <returns>当前生效的录制适配器；模拟为缺席时返回 <c>null</c>。</returns>
    public RecordingAnShengAdapter? PeekAdapter(int configId)
        => _absentConfigIds.ContainsKey(configId) ? null : Resolve(configId);

    /// <summary>
    /// 判断某个 configId 是否被 <see cref="ReleaseAdapter"/> 释放过（缺口 B 的断言锚点）。
    /// </summary>
    /// <param name="configId">协议配置主键。</param>
    /// <returns>释放过返回 <c>true</c>。</returns>
    public bool WasReleased(int configId) => _released.Contains(configId);

    #endregion

    /// <summary>
    /// 用例级复位：清默认适配器 + 销毁全部分身 + 复位模拟开关与释放留痕。
    ///
    /// 【模拟开关必须一起复位】
    ///   否则用例 A 标记的「configId 不在内存」会泄漏给用例 B，
    ///   B 的下发链路会莫名其妙地全部退化成「适配器未启动」——
    ///   典型的「单跑绿、连跑红」，且失败点在 B 里毫无痕迹。
    /// </summary>
    public void Reset()
    {
        foreach (var key in _extraAdapters.Keys.ToList())
        {
            if (_extraAdapters.TryRemove(key, out var adapter))
            {
                adapter.Dispose();
            }
        }

        while (_retiredAdapters.TryTake(out var retired))
        {
            retired.Dispose();
        }

        _absentConfigIds.Clear();

        while (_released.TryDequeue(out _))
        {
        }

        DefaultAdapter.Reset();
    }

    private RecordingAnShengAdapter Resolve(int configId) =>
        _extraAdapters.TryGetValue(configId, out var adapter) ? adapter : DefaultAdapter;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Reset();
        DefaultAdapter.Dispose();
    }
}
