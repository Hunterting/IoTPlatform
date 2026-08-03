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
///   <b>对任意 configId 都返回同一个 <see cref="DefaultAdapter"/></b>，永不返回 null。
///   这样用例无需关心真实 Id 就能走通下发链路。
///
/// 【为什么用单例默认适配器】
///   断言点集中在一处（<c>Fixture.Adapter.Sent</c>），避免用例先去猜「该断言哪个 configId 的适配器」。
///   确有多适配器串扰场景时，用 <see cref="GetOrCreateFor"/> 显式取分身。
/// </summary>
public sealed class FakeProtocolAdapterFactory : IProtocolAdapterFactory, IDisposable
{
    private readonly ConcurrentDictionary<int, RecordingAnShengAdapter> _extraAdapters = new();
    private bool _disposed;

    public FakeProtocolAdapterFactory()
    {
        DefaultAdapter = new RecordingAnShengAdapter(SharedTestConstants.ProtocolConfigId);
    }

    /// <summary>
    /// 缺省录制适配器。<c>GetAdapter</c>/<c>CreateAdapter</c> 在未显式登记分身时一律返回它。
    /// </summary>
    public RecordingAnShengAdapter DefaultAdapter { get; }

    /// <inheritdoc />
    public IProtocolAdapter CreateAdapter(string protocolType, int configId)
    {
        // 不按 protocolType 分流：测试里只关心安圣链路，其余协议同样落到录制替身，
        // 以免无关的后台代码路径因 NotSupportedException 炸掉整个 TestServer。
        return Resolve(configId);
    }

    /// <inheritdoc />
    public IProtocolAdapter? GetAdapter(int configId) => Resolve(configId);

    /// <inheritdoc />
    public void ReleaseAdapter(int configId)
    {
        if (_extraAdapters.TryRemove(configId, out var adapter))
        {
            adapter.Dispose();
        }

        // 默认适配器不销毁：它的生命周期与 TestServer 一致，仅在用例间 Reset()。
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

    /// <summary>用例级复位：清默认适配器 + 销毁全部分身。</summary>
    public void Reset()
    {
        foreach (var key in _extraAdapters.Keys.ToList())
        {
            if (_extraAdapters.TryRemove(key, out var adapter))
            {
                adapter.Dispose();
            }
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
