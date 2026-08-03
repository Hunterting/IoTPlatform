using IoTPlatform.Infrastructure.Protocol;
using IoTPlatform.Infrastructure.Protocol.Adapters;
using IoTPlatform.Infrastructure.Protocol.AnSheng;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IoTPlatform.AnSheng.Tests;

/// <summary>
/// 假适配器投递应答的方式。
/// </summary>
public enum FakeReplyMode
{
    /// <summary>设备不吭声，用于验证超时分支。</summary>
    Silent = 0,

    /// <summary>
    /// 在 <c>SendCommandAsync</c> 内部<b>同步</b>发布应答。
    ///
    /// 这是「秒回设备」的极端建模：应答在下发调用尚未返回时就已到达。
    /// 若被测代码把「登记等待者」放在「下发」之后，这条应答就会掉进虚空，
    /// 探测必然超时。因此本模式是 <c>AnShengProbeService</c> 约束 1 的照妖镜。
    /// </summary>
    Inline = 1,

    /// <summary>
    /// 在<b>专用非线程池线程</b>上延迟发布应答。
    ///
    /// 用于验证约束 2（<c>RunContinuationsAsynchronously</c>）：
    /// 专用线程永远不可能是线程池线程，于是「等待方的后续代码跑在哪个线程」
    /// 就成了一个可判定的事实——跑在专用线程上即说明续体被同步劫持。
    /// </summary>
    DedicatedThread = 2
}

/// <summary>
/// 一次被记录下来的下发调用。
/// </summary>
/// <param name="DeviceId">下发时传入的设备主键。</param>
/// <param name="SerialNumber">下发时传入的序列号（安圣链路即 IMEI）。</param>
/// <param name="CommandType">命令方法名。</param>
/// <param name="Parameters">命令参数 JSON。</param>
/// <param name="ThreadId">执行下发的托管线程 ID。</param>
public sealed record RecordedSend(
    long DeviceId,
    string SerialNumber,
    string CommandType,
    string Parameters,
    int ThreadId);

/// <summary>
/// 安圣探测测试专用的协议适配器替身。
///
/// 【为什么手写而不是 Mock 框架】
///   本替身要做的事超出了「返回预设值」：它得在被调用的瞬间往<b>静态总线</b>发布应答，
///   还要能指定发布线程。用 Mock 框架的回调也能拼出来，但可读性远不如一个显式的类，
///   而且本仓测试项目当前没有引入 Mock 依赖——为一个类引入一整个框架不划算。
/// </summary>
public sealed class FakeAnShengAdapter : IProtocolAdapter
{
    /// <summary>安圣二开协议类型标识。</summary>
    public const string AnShengProtocolType = "ANSHENG_MQTT";

    private readonly object _sync = new();
    private readonly List<RecordedSend> _sent = new();
    private readonly ConcurrentDictionary<int, byte> _publisherThreadIds = new();
    private readonly List<Thread> _replyThreads = new();
    private readonly AnShengMessageParser _parser = new();

    /// <inheritdoc />
    public string ProtocolType => AnShengProtocolType;

    /// <inheritdoc />
    public bool IsConnected { get; set; } = true;

    /// <inheritdoc />
    public int ConfigId { get; set; } = 1;

    /// <summary>应答投递方式。</summary>
    public FakeReplyMode ReplyMode { get; set; } = FakeReplyMode.Inline;

    /// <summary>专用线程模式下，发布前的等待毫秒数。</summary>
    public int ReplyDelayMs { get; set; } = 40;

    /// <summary>
    /// 应答 JSON 工厂：入参 (imei, method)，返回 null 表示该方法设备不应答。
    /// 默认对 <c>getDevInfo</c> / <c>getDevStatus</c> 各回一条典型的 4G 四路开关报文。
    /// </summary>
    public Func<string, string, string?> ReplyJsonFactory { get; set; } = DefaultReplyJson;

    /// <summary>发布时使用的 IMEI 覆盖值；为空则用下发时的 IMEI。用于制造「串台」场景。</summary>
    public string? PublishImeiOverride { get; set; }

    /// <summary>发布时使用的方法名覆盖值；为空则用下发的方法名。用于制造「答非所问」场景。</summary>
    public string? PublishMethodOverride { get; set; }

    /// <summary>非空时 <c>SendCommandAsync</c> 直接抛出该异常，模拟下发失败。</summary>
    public Exception? SendException { get; set; }

    /// <summary>已记录的下发调用（快照，线程安全）。</summary>
    public IReadOnlyList<RecordedSend> Sent
    {
        get
        {
            lock (_sync)
            {
                return _sent.ToList();
            }
        }
    }

    /// <summary>已记录的下发方法名序列。</summary>
    public IReadOnlyList<string> SentMethods => Sent.Select(s => s.CommandType).ToList();

    /// <summary>所有执行过 <c>Publish</c> 的线程 ID 集合。</summary>
    public IReadOnlyCollection<int> PublisherThreadIds => _publisherThreadIds.Keys.ToList();

    /// <summary>
    /// 构造一条典型的安圣应答 JSON。
    /// </summary>
    /// <param name="imei">设备 IMEI。</param>
    /// <param name="method">方法名。</param>
    /// <returns>应答 JSON；不认识的方法返回 null。</returns>
    public static string? DefaultReplyJson(string imei, string method) => method switch
    {
        "getDevInfo" =>
            $$"""
            {"method":"getDevInfo","result":"ok","imei":"{{imei}}","frameId":"fw-info-001",
             "version":"SWITCH-EC618X-R24-O-V4.0.8","slotAmount":4,"phaseAmount":1,
             "model":"Air780E","netType":"4G","iccid":"89860000000000000001"}
            """,
        "getDevStatus" =>
            $$"""
            {"method":"getDevStatus","result":"ok","imei":"{{imei}}","frameId":"fw-status-001",
             "netType":"4G","iccid":"89860000000000000002","signal":24,"temperature":"32.4",
             "slotAmount":4,"slots":[0,0,0,0]}
            """,
        _ => null
    };

    /// <inheritdoc />
    public Task<bool> ConnectAsync(string connectionString, CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    /// <inheritdoc />
    public Task DisconnectAsync() => Task.CompletedTask;

    /// <inheritdoc />
    public Task StartDataCollectionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StopDataCollectionAsync() => Task.CompletedTask;

    /// <summary>
    /// 记录下发，并按 <see cref="ReplyMode"/> 投递应答。
    /// </summary>
    /// <param name="deviceId">设备主键。</param>
    /// <param name="serialNumber">序列号（IMEI）。</param>
    /// <param name="commandType">方法名。</param>
    /// <param name="parameters">参数 JSON。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>命令 ID。</returns>
    public Task<string> SendCommandAsync(
        long deviceId,
        string serialNumber,
        string commandType,
        string parameters,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            _sent.Add(new RecordedSend(
                deviceId,
                serialNumber ?? string.Empty,
                commandType ?? string.Empty,
                parameters ?? string.Empty,
                Environment.CurrentManagedThreadId));
        }

        // 注意：本方法未标记 async，异常会同步冒泡给调用方——
        // 这正是要模拟的「下发当场失败」，而不是「返回一个失败的 Task」。
        if (SendException != null)
        {
            throw SendException;
        }

        if (ReplyMode != FakeReplyMode.Silent)
        {
            var json = ReplyJsonFactory?.Invoke(serialNumber ?? string.Empty, commandType ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(json))
            {
                DeliverReply(serialNumber ?? string.Empty, commandType ?? string.Empty, json!);
            }
        }

        return Task.FromResult(Guid.NewGuid().ToString("N"));
    }

    /// <inheritdoc />
    public Task ReadDataPointsAsync(
        long deviceId,
        string serialNumber,
        IEnumerable<string> dataPoints,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <inheritdoc />
    public event EventHandler<DeviceDataReceivedEventArgs>? DataReceived;

    /// <inheritdoc />
    public event EventHandler<DeviceCommandResponseEventArgs>? CommandResponse;

    /// <inheritdoc />
    public event EventHandler<bool>? ConnectionStateChanged;

    /// <summary>
    /// 手工触发适配器事件，仅用于消除「事件从未被使用」的编译警告并保持接口语义完整。
    /// </summary>
    public void RaiseAdapterEvents()
    {
        DataReceived?.Invoke(this, new DeviceDataReceivedEventArgs { ProtocolType = ProtocolType });
        CommandResponse?.Invoke(this, new DeviceCommandResponseEventArgs());
        ConnectionStateChanged?.Invoke(this, IsConnected);
    }

    /// <summary>
    /// 等待所有应答线程结束，避免线程泄漏到下一个用例。
    /// </summary>
    /// <param name="timeoutMs">单个线程的等待上限。</param>
    public void JoinReplyThreads(int timeoutMs = 2000)
    {
        List<Thread> threads;
        lock (_sync)
        {
            threads = _replyThreads.ToList();
            _replyThreads.Clear();
        }

        foreach (var thread in threads)
        {
            thread.Join(timeoutMs);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        JoinReplyThreads();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 把一条应答投递到静态上行总线。
    /// </summary>
    /// <param name="imei">下发时的 IMEI。</param>
    /// <param name="method">下发时的方法名。</param>
    /// <param name="json">应答 JSON。</param>
    private void DeliverReply(string imei, string method, string json)
    {
        var publishImei = string.IsNullOrEmpty(PublishImeiOverride) ? imei : PublishImeiOverride;
        var publishMethod = string.IsNullOrEmpty(PublishMethodOverride) ? method : PublishMethodOverride;
        var message = _parser.Parse(json);

        if (ReplyMode == FakeReplyMode.Inline)
        {
            _publisherThreadIds.TryAdd(Environment.CurrentManagedThreadId, 0);
            AnShengUplinkHub.Publish(publishImei, publishMethod, message, json);
            return;
        }

        var delay = ReplyDelayMs;
        var thread = new Thread(() =>
        {
            Thread.Sleep(delay);
            _publisherThreadIds.TryAdd(Environment.CurrentManagedThreadId, 0);
            AnShengUplinkHub.Publish(publishImei, publishMethod, message, json);
        })
        {
            IsBackground = true,
            Name = $"ansheng-fake-reply-{method}"
        };

        lock (_sync)
        {
            _replyThreads.Add(thread);
        }

        thread.Start();
    }
}

/// <summary>
/// 协议适配器工厂替身：按 configId 返回预置的适配器，未登记的 configId 返回 null。
/// </summary>
public sealed class FakeProtocolAdapterFactory : IProtocolAdapterFactory
{
    private readonly Dictionary<int, IProtocolAdapter> _adapters = new();

    /// <summary>
    /// 登记一个 configId 对应的适配器。
    /// </summary>
    /// <param name="configId">协议配置主键。</param>
    /// <param name="adapter">适配器实例。</param>
    /// <returns>工厂自身，便于链式调用。</returns>
    public FakeProtocolAdapterFactory Register(int configId, IProtocolAdapter adapter)
    {
        _adapters[configId] = adapter;
        return this;
    }

    /// <inheritdoc />
    public IProtocolAdapter CreateAdapter(string protocolType, int configId)
        => GetAdapter(configId) ?? throw new InvalidOperationException($"测试替身未登记 configId={configId} 的适配器。");

    /// <inheritdoc />
    public IProtocolAdapter? GetAdapter(int configId)
        => _adapters.TryGetValue(configId, out var adapter) ? adapter : null;

    /// <inheritdoc />
    public void ReleaseAdapter(int configId) => _adapters.Remove(configId);

    /// <inheritdoc />
    public void ReleaseAll() => _adapters.Clear();
}
