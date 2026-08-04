using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using IoTPlatform.Infrastructure.Protocol.Adapters;
using IoTPlatform.Infrastructure.Protocol.AnSheng;
using IoTPlatform.Services.Interfaces;

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
public sealed class RecordingAnShengAdapter : IProtocolAdapter, IAnShengDownlinkPort
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

    /// <summary>
    /// 自动上行应答表：方法名 → 报文工厂（入参 IMEI，返回 JSON；返回 null 表示本次不应答）。
    /// </summary>
    private readonly ConcurrentDictionary<string, Func<string, string?>> _autoUplinkReplies =
        new(StringComparer.Ordinal);

    private readonly AnShengMessageParser _uplinkParser = new();
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

        var imei = serialNumber ?? string.Empty;

        _sent.Enqueue(new SentCommand(
            deviceId,
            imei,
            method,
            parameters ?? string.Empty,
            frameId,
            DateTime.UtcNow));

        // 自动上行应答：必须在本方法内<b>同步</b>发布。
        // 生产 AnShengProbeService 遵守「先登记等待者、再下发」，所以此刻等待者一定已就位；
        // 反过来说，若哪天生产代码把顺序改反了，这里的同步应答会立刻让认领用例超时爆红——
        // 这正是我们想要的护栏，不要为了「保险」改成延迟发布。
        if (_autoUplinkReplies.TryGetValue(method, out var factory))
        {
            var payload = factory?.Invoke(imei);
            if (!string.IsNullOrWhiteSpace(payload))
            {
                RaiseAnShengUplink(imei, method, payload!);
            }
        }

        return Task.FromResult(frameId);
    }

    /// <inheritdoc />
    public Task<string> PublishAsync(
        long deviceId,
        string imei,
        string method,
        IReadOnlyDictionary<string, object?>? parameters,
        string frameId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // 接口契约（N1）：frameId 必须由调用方预先生成并登记在途，不得为空；
        // 为空意味着在途表无法与该帧关联，命令必然走到超时兜底，属调用方错误。
        if (string.IsNullOrWhiteSpace(frameId))
        {
            throw new ArgumentException(
                "frameId 不得为空：下行接缝要求调用方预先生成并已登记在途表。", nameof(frameId));
        }

        var methodName = method ?? string.Empty;

        // 复刻生产护栏：默认拒绝、显式放行。必须在录制之前判断，
        // 否则被拒绝的命令仍会进 Sent，破坏「拒绝 ⇒ 零副作用」的断言语义。
        if (EnforceProtocolWhitelist &&
            !AnShengCommandCatalog.Contains(methodName) &&
            !ProductionLegacyWhitelist.Contains(methodName))
        {
            throw new NotSupportedException(
                $"方法 {methodName} 不属于安圣二开协议目录，也不在 Legacy 充电桩白名单内，禁止下发。");
        }

        // frameId 优先级：入参 > 预置队列 > 自生成（设计 N1）。
        // 服务走下行接缝时必传非空的 frameId，故正常路径取入参；
        // 直接调替身且不传时，退化为与 SendCommandAsync 一致的策略（先消耗预置、再自生成）。
        var effectiveFrameId = !string.IsNullOrWhiteSpace(frameId)
            ? frameId
            : (_plannedFrameIds.TryDequeue(out var planned) && !string.IsNullOrWhiteSpace(planned)
                ? planned
                : AnShengCommandBuilder.NewFrameId());

        var deviceImei = imei ?? string.Empty;

        // 录制：与 SendCommandAsync 保持同构的快照（含 frameId），供断言。
        _sent.Enqueue(new SentCommand(
            deviceId,
            deviceImei,
            methodName,
            SerializeParameters(parameters),
            effectiveFrameId,
            DateTime.UtcNow));

        // 自动上行应答：必须在本方法内<b>同步</b>发布（与生产链路「先登记等待者、再下发」一致）。
        if (_autoUplinkReplies.TryGetValue(methodName, out var factory))
        {
            var payload = factory?.Invoke(deviceImei);
            if (!string.IsNullOrWhiteSpace(payload))
            {
                RaiseAnShengUplink(deviceImei, methodName, payload!);
            }
        }

        return Task.FromResult(effectiveFrameId);
    }

    /// <summary>
    /// 把参数字典序列化成 JSON 字符串用于录制（与 <see cref="SentCommand.Parameters"/> 形态一致）。
    /// </summary>
    private static string SerializeParameters(IReadOnlyDictionary<string, object?>? parameters)
    {
        if (parameters is null || parameters.Count == 0) return "{}";
        try
        {
            return JsonSerializer.Serialize(parameters);
        }
        catch (NotSupportedException)
        {
            // 参数里混入了不可序列化的对象（理论上不该发生）。失败关闭：宁可丢留痕也不能泄漏。
            return "{}";
        }
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

    /// <summary>
    /// 手工投递一条安圣上行报文到静态总线 <see cref="AnShengUplinkHub"/>。
    ///
    /// 【为什么替身必须自己发布】
    ///   生产链路里是 <c>AnShengMqttProtocolAdapter.OnMessageReceivedAsync</c> 收到 MQTT 报文后
    ///   调 <c>AnShengUplinkHub.Publish</c>。但集成测试把整个 <c>IProtocolAdapter</c> 换成了本替身，
    ///   那段生产代码<b>根本不会执行</b>。若替身不补上这一步，
    ///   <c>AnShengProbeService</c> 永远等不到应答，所有认领用例都会以「探测超时」告终。
    ///
    /// 【为什么直接调 Publish 而不是走 DataReceived 事件】
    ///   总线与 <c>DataReceived</c> 是两条独立通道：前者服务于探测的请求-应答关联，
    ///   后者服务于数据落库。绕道 <c>DataReceived</c> 既到不了探测服务，也会污染数据链路断言。
    /// </summary>
    /// <param name="imei">设备 IMEI。</param>
    /// <param name="method">报文方法名，如 <c>getDevInfo</c>。</param>
    /// <param name="payloadJson">报文 JSON 全文。</param>
    public void RaiseAnShengUplink(string imei, string method, string payloadJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imei);
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadJson);

        // 解析失败时 message 为 null，Publish 仍会投递（订阅方自行降级），
        // 这样「设备回了一坨非法 JSON」的场景也能被用例覆盖。
        var message = _uplinkParser.Parse(payloadJson);
        AnShengUplinkHub.Publish(imei, method, message, payloadJson);
    }

    /// <summary>
    /// 登记一条「收到该方法的下发就自动回上行」的规则，报文内容固定。
    /// </summary>
    /// <param name="method">触发的方法名。</param>
    /// <param name="payloadJson">固定应答 JSON。</param>
    /// <returns>适配器自身，便于链式调用。</returns>
    public RecordingAnShengAdapter AutoReplyUplink(string method, string payloadJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadJson);

        _autoUplinkReplies[method] = _ => payloadJson;
        return this;
    }

    /// <summary>
    /// 登记一条「收到该方法的下发就自动回上行」的规则，报文由工厂按 IMEI 生成。
    /// </summary>
    /// <param name="method">触发的方法名。</param>
    /// <param name="payloadFactory">报文工厂；返回 null 表示本次不应答（用于制造超时）。</param>
    /// <returns>适配器自身，便于链式调用。</returns>
    public RecordingAnShengAdapter AutoReplyUplink(string method, Func<string, string?> payloadFactory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentNullException.ThrowIfNull(payloadFactory);

        _autoUplinkReplies[method] = payloadFactory;
        return this;
    }

    /// <summary>
    /// 撤销某个方法的自动上行应答规则，使其恢复「设备不吭声」。
    /// </summary>
    /// <param name="method">方法名。</param>
    /// <returns>适配器自身，便于链式调用。</returns>
    public RecordingAnShengAdapter ClearAutoReplyUplink(string method)
    {
        _autoUplinkReplies.TryRemove(method, out _);
        return this;
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
    /// 【重要】这里不清事件订阅者，也<b>绝不</b>调用 <c>AnShengUplinkHub.Reset()</c>。
    /// TestServer 全程只有一个，<c>AnShengProbeService</c> 是 Singleton 且在构造时订阅静态总线；
    /// 一旦清空订阅，Singleton 不会被重建，后续所有用例的探测都会永久超时。
    /// 用例间的探测隔离由 <c>IAnShengProbeService.ClearPending()</c> 负责（见 StaticStateResetter）。
    /// </summary>
    public void Reset()
    {
        while (_sent.TryDequeue(out _))
        {
        }

        while (_plannedFrameIds.TryDequeue(out _))
        {
        }

        _autoUplinkReplies.Clear();
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
