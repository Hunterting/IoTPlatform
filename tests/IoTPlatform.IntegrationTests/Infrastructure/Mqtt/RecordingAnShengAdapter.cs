using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using IoTPlatform.Infrastructure.Protocol.Adapters;
using IoTPlatform.Infrastructure.Protocol.AnSheng;

namespace IoTPlatform.IntegrationTests.Infrastructure.Mqtt;

/// <summary>
/// 一条下发指令的录制快照。
/// </summary>
/// <param name="DeviceId">设备主键。</param>
/// <param name="SerialNumber">设备序列号（安圣即 IMEI）。</param>
/// <param name="CommandType">方法名，如 <c>getDevStatus</c>。</param>
/// <param name="Parameters">参数 JSON 原文（<c>AnShengCommandService</c> 传入的即为序列化后字符串）。</param>
/// <param name="FrameId">本次下发生成的 16 位 frameId，可用于关联上行回包。</param>
/// <param name="SentAt">录制时刻（UTC）。</param>
public sealed record SentCommand(
    long DeviceId,
    string SerialNumber,
    string CommandType,
    string Parameters,
    string FrameId,
    DateTime SentAt);

/// <summary>
/// 安圣 MQTT 适配器的「录制型」测试替身（架构方案 §3.5）。
///
/// 【定位】它替换的是 <c>IProtocolAdapter</c> 而不是 MQTTnet 的 IMqttClient ——
///   后者从未进入 DI 容器，无从替换；而 <c>AnShengCommandService</c> 恰好通过
///   <c>IProtocolAdapterFactory.GetAdapter(configId)</c> 拿适配器，这里正是唯一稳定的接缝。
///
/// 【断言锚点】
///   · 下行：<see cref="Sent"/> —— 断言「发了几条 / 发了什么」；
///   · 上行：<see cref="RaiseDataReceived"/> / <see cref="RaiseCommandResponse"/> ——
///     手工投递报文，驱动生产代码的事件订阅链路，无需真实 broker。
///
/// 【线程安全】xUnit 集合已禁并行，但事件回调可能在别的线程，故用 ConcurrentQueue 兜底。
/// </summary>
public sealed class RecordingAnShengAdapter : IProtocolAdapter
{
    private const string LegacyWhitelistFieldName = "LegacyMethodWhitelist";

    /// <summary>
    /// 生产适配器的 Legacy 白名单镜像。
    ///
    /// 【为什么用反射而不是手抄一份】
    ///   <c>AnShengMqttProtocolAdapter.LegacyMethodWhitelist</c> 是 <c>private static readonly</c>。
    ///   手抄常量必然随时间漂移——生产放行了新方法而测试替身不知道，用例就会假红；
    ///   反之则假绿。反射直接读同一份数据，从根上杜绝双份真相。
    ///   反射失败时回落到已知快照，并由 <see cref="WhitelistSource"/> 暴露实际来源供自检用例断言。
    /// </summary>
    private static readonly IReadOnlySet<string> ProductionLegacyWhitelist;

    /// <summary>白名单实际来源：<c>reflection</c>（已同步）或 <c>fallback</c>（反射失败，可能已漂移）。</summary>
    public static string WhitelistSource { get; }

    /// <summary>
    /// 静态构造：<see cref="LoadLegacyWhitelist"/> 用 out 参数同时回传「白名单」与「来源」，
    /// 而 out 局部变量的作用域不跨字段初始化器，故必须在静态构造里成对赋值。
    /// </summary>
    static RecordingAnShengAdapter()
    {
        ProductionLegacyWhitelist = LoadLegacyWhitelist(out var source);
        WhitelistSource = source;
    }

    /// <summary>生产 Legacy 白名单的只读视图，供用例断言与排障。</summary>
    public static IReadOnlySet<string> LegacyWhitelist => ProductionLegacyWhitelist;

    private readonly ConcurrentQueue<SentCommand> _sent = new();
    private readonly ConcurrentQueue<string> _plannedFrameIds = new();
    private bool _disposed;

    public RecordingAnShengAdapter(int configId = SharedTestConstants.ProtocolConfigId)
    {
        ConfigId = configId;
    }

    /// <inheritdoc />
    public string ProtocolType => SharedTestConstants.ProtocolTypeAnSheng;

    /// <summary>
    /// 恒为 true。
    ///
    /// <c>AnShengCommandService.SendCommandAsync</c> 会在 <c>!adapter.IsConnected</c> 时
    /// 直接短路返回失败；测试要走到真实的目录校验 + 报文构建，就必须让它「已连接」。
    /// 需要验证断连分支的用例请显式改 <see cref="ForceDisconnected"/>。
    /// </summary>
    public bool IsConnected => !ForceDisconnected;

    /// <summary>置 true 可模拟「适配器已断连」，用于负向分支。<see cref="Reset"/> 会复位为 false。</summary>
    public bool ForceDisconnected { get; set; }

    /// <summary>
    /// 是否复刻生产适配器的「默认拒绝」协议护栏，默认 <c>true</c>。
    ///
    /// 生产 <c>AnShengMqttProtocolAdapter.SendCommandAsync</c> 对既不在
    /// <c>AnShengCommandCatalog</c>、也不在 Legacy 白名单内的方法会抛
    /// <see cref="NotSupportedException"/>。测试替身若不照做，就会把「协议外命令被放行」
    /// 这类严重缺陷测成绿色——所以默认开启。
    /// 只有在明确要绕过护栏观察下游行为时，才由用例显式置 false。
    /// </summary>
    public bool EnforceProtocolWhitelist { get; set; } = true;

    /// <inheritdoc />
    public int ConfigId { get; }

    /// <summary>已录制的全部下发指令，按发生顺序。</summary>
    public IReadOnlyList<SentCommand> Sent => _sent.ToArray();

    /// <summary>最后一条下发指令；一条都没有时返回 null。</summary>
    public SentCommand? LastSent => _sent.LastOrDefault();

    /// <summary>数据采集是否已启动（供「启动/停止」类断言使用）。</summary>
    public bool DataCollectionStarted { get; private set; }

    /// <inheritdoc />
    public event EventHandler<DeviceDataReceivedEventArgs>? DataReceived;

    /// <inheritdoc />
    public event EventHandler<DeviceCommandResponseEventArgs>? CommandResponse;

    /// <inheritdoc />
    public event EventHandler<bool>? ConnectionStateChanged;

    /// <inheritdoc />
    public Task<bool> ConnectAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        ForceDisconnected = false;
        ConnectionStateChanged?.Invoke(this, true);
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task DisconnectAsync()
    {
        ForceDisconnected = true;
        ConnectionStateChanged?.Invoke(this, false);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StartDataCollectionAsync(CancellationToken cancellationToken = default)
    {
        DataCollectionStarted = true;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopDataCollectionAsync()
    {
        DataCollectionStarted = false;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<string> SendCommandAsync(
        long deviceId,
        string serialNumber,
        string commandType,
        string parameters,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var method = commandType ?? string.Empty;

        // 复刻生产护栏：默认拒绝、显式放行。必须在录制之前判断，
        // 否则被拒绝的命令仍会进 Sent，破坏「拒绝 ⇒ 零副作用」的断言语义。
        if (EnforceProtocolWhitelist &&
            !AnShengCommandCatalog.Contains(method) &&
            !ProductionLegacyWhitelist.Contains(method))
        {
            throw new NotSupportedException(
                $"方法 {method} 不属于安圣二开协议目录，也不在 Legacy 充电桩白名单内，禁止下发。");
        }

        // 优先使用用例预置的 frameId，便于「先约定 frameId 再投递上行回包」的场景。
        var frameId = _plannedFrameIds.TryDequeue(out var planned) && !string.IsNullOrWhiteSpace(planned)
            ? planned
            : AnShengCommandBuilder.NewFrameId();

        _sent.Enqueue(new SentCommand(
            deviceId,
            serialNumber ?? string.Empty,
            method,
            parameters ?? string.Empty,
            frameId,
            DateTime.UtcNow));

        return Task.FromResult(frameId);
    }

    /// <inheritdoc />
    public Task ReadDataPointsAsync(
        long deviceId,
        string serialNumber,
        IEnumerable<string> dataPoints,
        CancellationToken cancellationToken = default)
    {
        // 安圣走事件上报模式，无主动读点；保持空实现即可，不抛异常以免污染无关用例。
        return Task.CompletedTask;
    }

    /// <summary>
    /// 预置下一次（或接下来若干次）<see cref="SendCommandAsync"/> 返回的 frameId。
    /// 用于让用例先拿到 frameId，再用同一个 frameId 构造上行回包。
    /// </summary>
    public RecordingAnShengAdapter EnqueueResponse(string frameId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(frameId);
        _plannedFrameIds.Enqueue(frameId);
        return this;
    }

    /// <summary>手工投递一条设备上行数据，驱动生产代码的 <c>DataReceived</c> 订阅链路。</summary>
    public void RaiseDataReceived(
        long deviceId,
        string serialNumber,
        string payloadJson,
        string appCode = SharedTestConstants.AppCode)
    {
        DataReceived?.Invoke(this, new DeviceDataReceivedEventArgs
        {
            DeviceId = deviceId,
            SerialNumber = serialNumber,
            AppCode = appCode,
            Data = payloadJson,
            ProtocolType = ProtocolType,
            ReceivedAt = DateTime.UtcNow
        });
    }

    /// <summary>手工投递一条指令响应，驱动生产代码的 <c>CommandResponse</c> 订阅链路。</summary>
    public void RaiseCommandResponse(
        long deviceId,
        string commandId,
        string status,
        string? responseData = null)
    {
        CommandResponse?.Invoke(this, new DeviceCommandResponseEventArgs
        {
            DeviceId = deviceId,
            CommandId = commandId,
            Status = status,
            ResponseData = responseData,
            RespondedAt = DateTime.UtcNow
        });
    }

    /// <summary>
    /// 用例级复位：清录制、清预置 frameId、恢复连接状态。
    ///
    /// 【重要】这里不清事件订阅者。TestServer 全程只有一个，
    /// 生产代码若在启动时订阅过，清掉会让后续用例收不到上行。
    /// </summary>
    public void Reset()
    {
        while (_sent.TryDequeue(out _))
        {
        }

        while (_plannedFrameIds.TryDequeue(out _))
        {
        }

        ForceDisconnected = false;
        DataCollectionStarted = false;
        EnforceProtocolWhitelist = true;
    }

    /// <summary>
    /// 反射读取生产适配器的 Legacy 白名单；失败时回落到已知快照。
    /// </summary>
    private static IReadOnlySet<string> LoadLegacyWhitelist(out string source)
    {
        // 反射失败时的兜底快照。仅在生产字段被重命名/改私有结构时生效，
        // 届时 WhitelistSource 会变成 "fallback"，由自检用例报警。
        var fallback = new HashSet<string>(StringComparer.Ordinal)
        {
            "orderStart", "orderEnd", "orderUp"
        };

        try
        {
            var field = typeof(AnShengMqttProtocolAdapter).GetField(
                LegacyWhitelistFieldName,
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

            if (field?.GetValue(null) is not IEnumerable raw)
            {
                source = "fallback";
                return fallback;
            }

            var mirrored = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in raw)
            {
                if (item is string s)
                {
                    mirrored.Add(s);
                }
            }

            source = "reflection";
            return mirrored;
        }
        catch
        {
            source = "fallback";
            return fallback;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Reset();
    }
}
