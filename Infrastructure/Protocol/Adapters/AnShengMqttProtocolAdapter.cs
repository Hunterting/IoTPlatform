using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Threading;
using IoTPlatform.Infrastructure.Protocol.AnSheng;

namespace IoTPlatform.Infrastructure.Protocol.Adapters;

/// <summary>
/// 安圣 MQTT 协议适配器。
/// 实现 <see cref="IProtocolAdapter"/>，处理安圣二开设备的数据接收与命令下发。
///
/// 主题约定（见 appsettings.json:AnShengMqtt）：
///   设备上行： <c>/iot/server/iot-board/{imei}</c>（业务数据与 will 遗愿<b>共用</b>）
///   平台下行： <c>/iot/client/iot-board/{imei}</c>
///
/// 下行报文严格遵循 asopen.md：参数平铺在顶层（无 <c>param</c> 包裹）、
/// <c>frameId</c> 为 16 位唯一串、<c>timestamp</c> 为秒级 int 且仅 4G 款注入、压缩 JSON、
/// 同一 IMEI 两条命令间隔 ≥100ms。
///
/// 离线判定：<b>不看主题前缀</b>（上下行 will 与数据同主题），只看 <c>method == "close"</c>。
/// </summary>
public class AnShengMqttProtocolAdapter : IProtocolAdapter
{
    private readonly ILogger<AnShengMqttProtocolAdapter>? _logger;
    private readonly int _configId;
    private readonly AnShengCommandBuilder _commandBuilder;
    private IMqttClient? _mqttClient;
    private AnShengMqttProtocolOptions? _options;
    private AnShengMessageParser? _parser;
    private AnShengCommandThrottle? _throttle;
    private CancellationTokenSource? _dataCollectionCts;
    private bool _isCollecting;
    private bool _disposed;

    // 重连控制
    private bool _reconnecting;
    private int _reconnectAttempt;
    private const int MaxReconnectAttempts = 10;
    private static readonly TimeSpan ReconnectBaseDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ReconnectMaxDelay = TimeSpan.FromSeconds(60);

    // 健康检查定时器（检测僵尸连接）
    private Timer? _healthCheckTimer;
    private static readonly TimeSpan HealthCheckInterval = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Legacy 充电桩协议族下行方法白名单（<b>默认拒绝、显式放行</b>）。
    ///
    /// 背景：历史实现在 method 未命中 <see cref="AnShengCommandCatalog"/> 时无条件走 Legacy 兜底，
    /// 导致任意「协议外」方法（含前端臆造的伪命令）都会被真实构造并下发到现网设备，
    /// 且调用方还会收到「成功」响应。此白名单用于阻断该路径。
    ///
    /// 收录标准：确属旧版充电桩协议、且当前仍有真实链路在用的下行方法。
    /// <b>不得</b>收录任何伪命令（如 setSwitch / getSwitchStatus / setSwitchConfig / getSwitchConfig），
    /// 它们本就不属于任何协议，已在前后端一并删除。
    /// 注：<c>getDevStatus</c> 等方法已登记在 <see cref="AnShengCommandCatalog"/> 中，
    /// 走目录分支，无需重复登记于此。
    /// </summary>
    private static readonly HashSet<string> LegacyMethodWhitelist = new(StringComparer.Ordinal)
    {
        "orderStart",
        "orderEnd",
        "orderUp"
    };

    /// <summary>
    /// IMEI → 设备品类缓存。
    /// 由上行 <c>getDevInfo</c>/<c>getDevStatus</c>/<c>connected</c> 报文自动学习，
    /// 也可由业务层通过 <see cref="RegisterDeviceKind"/> 主动登记（例如从数据库读取 model/netType）。
    /// 单实例内存版，满足当前单节点部署要求。
    /// </summary>
    private static readonly ConcurrentDictionary<string, AnShengDeviceKind> DeviceKinds =
        new(StringComparer.Ordinal);

    public string ProtocolType => "ANSHENG_MQTT";
    public bool IsConnected => _mqttClient?.IsConnected ?? false;
    public int ConfigId => _configId;

    public event EventHandler<DeviceDataReceivedEventArgs>? DataReceived;
    public event EventHandler<DeviceCommandResponseEventArgs>? CommandResponse;
    public event EventHandler<bool>? ConnectionStateChanged;

    /// <summary>
    /// 安圣设备 Will 离线事件（静态事件，DiscoveryService 全局监听）
    /// </summary>
    public static event EventHandler<AnSheng.AnShengWillEventArgs>? DeviceWill;

    /// <summary>
    /// 创建安圣 MQTT 适配器。
    /// </summary>
    /// <param name="configId">协议配置主键。</param>
    /// <param name="logger">可选日志器。</param>
    public AnShengMqttProtocolAdapter(int configId, ILogger<AnShengMqttProtocolAdapter>? logger = null)
    {
        _configId = configId;
        _logger = logger;
        _commandBuilder = new AnShengCommandBuilder(null);
    }

    // ─────────────────────────────────────────────────────────────
    // 设备品类登记
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 登记设备品类，供下发时决定是否注入 <c>timestamp</c>、以及命令能力校验。
    /// </summary>
    /// <param name="imei">设备 IMEI。</param>
    /// <param name="kind">设备品类；<see cref="AnShengDeviceKind.Unknown"/> 会被忽略。</param>
    public static void RegisterDeviceKind(string? imei, AnShengDeviceKind kind)
    {
        if (string.IsNullOrWhiteSpace(imei) || kind == AnShengDeviceKind.Unknown) return;
        DeviceKinds[imei] = kind;
    }

    /// <summary>
    /// 查询已登记的设备品类。
    /// </summary>
    /// <param name="imei">设备 IMEI。</param>
    /// <returns>已登记的品类；未知时返回 <see cref="AnShengDeviceKind.Unknown"/>。</returns>
    public static AnShengDeviceKind GetDeviceKind(string? imei)
    {
        if (string.IsNullOrWhiteSpace(imei)) return AnShengDeviceKind.Unknown;
        return DeviceKinds.TryGetValue(imei, out var kind) ? kind : AnShengDeviceKind.Unknown;
    }

    /// <summary>
    /// 清空品类缓存（单元测试与设备重置场景使用）。
    /// </summary>
    public static void ClearDeviceKinds() => DeviceKinds.Clear();

    // ─────────────────────────────────────────────────────────────
    // 连接管理
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 连接到安圣 MQTT Broker。
    /// </summary>
    /// <param name="connectionString">序列化后的 <see cref="AnShengMqttProtocolOptions"/>。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>连接成功返回 true。</returns>
    public async Task<bool> ConnectAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_mqttClient?.IsConnected == true)
            {
                _reconnecting = false;
                _reconnectAttempt = 0;
                return true;
            }

            _options = JsonSerializer.Deserialize<AnShengMqttProtocolOptions>(connectionString)
                ?? throw new ArgumentException("无效的安圣 MQTT 连接配置");

            _throttle?.Dispose();
            _throttle = new AnShengCommandThrottle(_options.CommandMinIntervalMs);

            var factory = new MqttFactory();
            _mqttClient = factory.CreateMqttClient();

            var clientOptions = new MqttClientOptionsBuilder()
                .WithTcpServer(_options.Host, _options.Port)
                .WithCredentials(_options.Username, _options.Password)
                .WithClientId($"{_options.ClientIdPrefix}_{Guid.NewGuid():N}")
                .WithCleanSession(_options.CleanSession)
                .WithTimeout(TimeSpan.FromSeconds(_options.TimeoutSeconds))
                .WithKeepAlivePeriod(TimeSpan.FromSeconds(_options.KeepAliveSeconds))
                .Build();

            _mqttClient.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;
            _mqttClient.DisconnectedAsync += OnDisconnectedAsync;

            var response = await _mqttClient.ConnectAsync(clientOptions, cancellationToken);

            if (response.ResultCode == MqttClientConnectResultCode.Success)
            {
                _logger?.LogInformation("安圣 MQTT 协议适配器连接成功: {Host}:{Port}", _options.Host, _options.Port);
                ConnectionStateChanged?.Invoke(this, true);
                return true;
            }

            _logger?.LogError("安圣 MQTT 连接失败: {ResultCode}", response.ResultCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "安圣 MQTT 连接异常");
            return false;
        }
    }

    /// <summary>
    /// 断开连接。
    /// </summary>
    public async Task DisconnectAsync()
    {
        if (_mqttClient == null) return;

        try
        {
            await StopDataCollectionAsync();
            await _mqttClient.DisconnectAsync();
            _logger?.LogInformation("安圣 MQTT 协议适配器已断开连接");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "安圣 MQTT 断开连接异常");
        }
        finally
        {
            ConnectionStateChanged?.Invoke(this, false);
        }
    }

    /// <summary>
    /// 开始数据采集：订阅安圣设备上行主题。
    /// 安圣二开协议中 will 与业务数据共用同一主题，若两个 Pattern 相同则<b>只订阅一次</b>，
    /// 否则同一条报文会被 Broker 投递两次、导致重复入库。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task StartDataCollectionAsync(CancellationToken cancellationToken = default)
    {
        if (_mqttClient?.IsConnected != true || _options == null)
        {
            throw new InvalidOperationException("安圣 MQTT 客户端未连接");
        }

        if (_isCollecting)
        {
            _logger?.LogDebug("安圣 MQTT 数据采集已在运行中，跳过");
            return;
        }

        // 重连场景：清理旧的 CancellationTokenSource
        _dataCollectionCts?.Cancel();
        _dataCollectionCts?.Dispose();
        _dataCollectionCts = null;

        _dataCollectionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _isCollecting = true;

        // 初始化消息解析器
        _parser = new AnShengMessageParser(null);

        try
        {
            var qos = ConvertToMqttQoS(_options.QosLevel);
            var subscribed = new HashSet<string>(StringComparer.Ordinal);

            // 设备上行数据主题（getDevStatus / 事件 / 命令响应等）
            if (!string.IsNullOrWhiteSpace(_options.PublishTopicPattern)
                && subscribed.Add(_options.PublishTopicPattern))
            {
                await SubscribeTopicAsync(_options.PublishTopicPattern, qos, _dataCollectionCts.Token);
                _logger?.LogInformation("已订阅安圣数据主题: {Topic}", _options.PublishTopicPattern);
            }

            // Will 遗愿主题：与数据主题相同则跳过，避免重复订阅
            if (!string.IsNullOrWhiteSpace(_options.WillTopicPattern))
            {
                if (subscribed.Add(_options.WillTopicPattern))
                {
                    await SubscribeTopicAsync(_options.WillTopicPattern, qos, _dataCollectionCts.Token);
                    _logger?.LogInformation("已订阅安圣 Will 主题: {Topic}", _options.WillTopicPattern);
                }
                else
                {
                    _logger?.LogInformation(
                        "安圣 Will 主题与数据主题相同（{Topic}），跳过重复订阅，离线判定改为 method==\"{Method}\"",
                        _options.WillTopicPattern, AnShengCommandCatalog.WillMethod);
                }
            }

            _logger?.LogInformation("安圣 MQTT 数据采集已启动");

            // 启动健康检查定时器：每 15s 检查一次 MQTT 连接是否已变成僵尸
            StopHealthCheckTimer();
            _healthCheckTimer = new Timer(_ =>
            {
                try
                {
                    if (_mqttClient != null && !_mqttClient.IsConnected && !_disposed && !_reconnecting)
                    {
                        _logger?.LogWarning("健康检查检测到 MQTT 连接已断开，触发重连");
                        _ = Task.Run(() => ReconnectLoopAsync());
                    }
                }
                catch
                {
                    // 静默吞掉定时器异常
                }
            }, null, HealthCheckInterval, HealthCheckInterval);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "安圣 MQTT 数据采集启动失败");
            _isCollecting = false;
            throw;
        }
    }

    /// <summary>
    /// 订阅单个主题。
    /// </summary>
    /// <param name="topic">主题过滤器。</param>
    /// <param name="qos">QoS 级别。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task SubscribeTopicAsync(string topic, MqttQualityOfServiceLevel qos,
        CancellationToken cancellationToken)
    {
        var filter = new MqttTopicFilterBuilder()
            .WithTopic(topic)
            .WithQualityOfServiceLevel(qos)
            .Build();
        await _mqttClient!.SubscribeAsync(filter, cancellationToken);
    }

    /// <summary>
    /// 停止数据采集。
    /// </summary>
    public Task StopDataCollectionAsync()
    {
        _isCollecting = false;
        _dataCollectionCts?.Cancel();
        _dataCollectionCts?.Dispose();
        _dataCollectionCts = null;

        StopHealthCheckTimer();

        _logger?.LogInformation("安圣 MQTT 数据采集已停止");
        return Task.CompletedTask;
    }

    // ─────────────────────────────────────────────────────────────
    // 命令下发
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 向安圣设备下发命令，<paramref name="serialNumber"/> 即设备 IMEI。
    /// 报文由 <see cref="AnShengCommandBuilder"/> 统一构建，保证与 asopen.md 一致；
    /// 下发前经 <see cref="AnShengCommandThrottle"/> 按 IMEI 限流（≥100ms）。
    ///
    /// 报文结构选择遵循「默认拒绝、显式放行」：
    ///   1. 命中 <see cref="AnShengCommandCatalog"/> → 二开协议报文（参数平铺）；
    ///   2. 命中 <see cref="LegacyMethodWhitelist"/> → Legacy 充电桩报文（param 包裹）；
    ///   3. 其余一律抛出 <see cref="NotSupportedException"/>，不得下发协议外报文。
    /// </summary>
    /// <param name="deviceId">设备主键（仅用于日志）。</param>
    /// <param name="serialNumber">设备 IMEI。</param>
    /// <param name="commandType">协议方法名。</param>
    /// <param name="parameters">JSON 字符串形式的平铺参数，可为空。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>本次下发的 frameId（16 位）。</returns>
    /// <exception cref="InvalidOperationException">MQTT 未连接。</exception>
    /// <exception cref="NotSupportedException">
    /// <paramref name="commandType"/> 既不在二开协议目录中，也不在 Legacy 白名单内。
    /// </exception>
    public async Task<string> SendCommandAsync(long deviceId, string serialNumber, string commandType,
        string parameters, CancellationToken cancellationToken = default)
    {
        if (_mqttClient?.IsConnected != true || _options == null)
        {
            throw new InvalidOperationException("安圣 MQTT 客户端未连接");
        }

        var topic = _options.SubscribeTopicTemplate.Replace("{imei}", serialNumber);
        var flatParams = ParseParameters(parameters);
        var kind = GetDeviceKind(serialNumber);

        string frameId;
        string payloadJson;

        if (AnShengCommandCatalog.Contains(commandType))
        {
            // 二开协议：参数平铺、16 位 frameId、仅 4G 注入秒级 timestamp、压缩 JSON
            (frameId, payloadJson) = _commandBuilder.BuildCommand(serialNumber, commandType, flatParams, kind);
        }
        else if (LegacyMethodWhitelist.Contains(commandType))
        {
            // Legacy 充电桩协议族（orderStart / orderEnd / orderUp）：保留 param 包裹，行为不变
            _logger?.LogDebug("方法 {Method} 命中 Legacy 充电桩白名单，按 Legacy 报文结构下发", commandType);
            (frameId, payloadJson) = _commandBuilder.BuildLegacyCommand(serialNumber, commandType, flatParams);
        }
        else
        {
            // 默认拒绝：既不在二开协议目录、也不在 Legacy 白名单 → 快速失败，禁止外发协议外报文
            _logger?.LogWarning(
                "拒绝下发协议外命令: DeviceId={DeviceId}, IMEI={IMEI}, Method={Method}, Kind={Kind}。"
                + "该方法既未登记于 AnShengCommandCatalog，也不在 Legacy 充电桩白名单 [{Whitelist}] 内，已阻止外发。",
                deviceId, serialNumber, commandType, kind, string.Join(", ", LegacyMethodWhitelist));

            throw new NotSupportedException(
                $"方法 {commandType} 不属于安圣二开协议目录，也不在 Legacy 充电桩白名单内，禁止下发。");
        }

        // 协议要求：同一设备多条命令之间间隔 ≥100ms，防止命令粘连
        if (_throttle != null)
        {
            var waited = await _throttle.WaitTurnAsync(serialNumber, cancellationToken);
            if (waited > 0)
            {
                _logger?.LogDebug("安圣命令限流等待 {Waited}ms: IMEI={IMEI}, Method={Method}",
                    waited, serialNumber, commandType);
            }
        }

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payloadJson)
            .WithQualityOfServiceLevel(ConvertToMqttQoS(_options.QosLevel))
            .WithRetainFlag(false)
            .Build();

        await _mqttClient.PublishAsync(message, cancellationToken);

        _logger?.LogInformation(
            "安圣命令已发送: DeviceId={DeviceId}, Method={Method}, IMEI={IMEI}, Kind={Kind}, FrameId={FrameId}, Topic={Topic}, Payload={Payload}",
            deviceId, commandType, serialNumber, kind, frameId, topic, payloadJson);
        return frameId;
    }

    /// <summary>
    /// 将 JSON 字符串参数解析为平铺字典。
    /// 兼容历史调用方传入 <c>{"param":{...}}</c> 的情况——会自动解包一层。
    /// </summary>
    /// <param name="parameters">JSON 字符串，可为空。</param>
    /// <returns>平铺参数字典，永不为 null。</returns>
    private static Dictionary<string, object?> ParseParameters(string? parameters)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(parameters))
        {
            return result;
        }

        try
        {
            using var document = JsonDocument.Parse(parameters);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return result;
            }

            // 兼容旧调用：{"param":{...}} → 解包
            if (root.EnumerateObject().Count() == 1
                && root.TryGetProperty("param", out var wrapped)
                && wrapped.ValueKind == JsonValueKind.Object)
            {
                root = wrapped;
            }

            foreach (var property in root.EnumerateObject())
            {
                if (property.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                {
                    continue;
                }

                result[property.Name] = property.Value.Clone();
            }
        }
        catch (JsonException)
        {
            // 非 JSON 字符串：忽略，按无参数处理，避免污染报文结构
        }

        return result;
    }

    /// <summary>
    /// 主动读取安圣设备数据点（发送 <c>getDevStatus</c> 命令）。
    /// </summary>
    /// <param name="deviceId">设备主键。</param>
    /// <param name="serialNumber">设备 IMEI。</param>
    /// <param name="dataPoints">要查询的数据点集合；为空表示查询全部。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task ReadDataPointsAsync(long deviceId, string serialNumber, IEnumerable<string> dataPoints,
        CancellationToken cancellationToken = default)
    {
        var query = dataPoints != null && dataPoints.Any()
            ? string.Join(",", dataPoints)
            : string.Empty;

        // 协议：q 为空时不下发该字段，设备返回全部状态
        var parameters = string.IsNullOrWhiteSpace(query)
            ? "{}"
            : JsonSerializer.Serialize(new Dictionary<string, object?>(StringComparer.Ordinal) { ["q"] = query });

        await SendCommandAsync(deviceId, serialNumber, "getDevStatus", parameters, cancellationToken);
    }

    // ─────────────────────────────────────────────────────────────
    // 上行处理
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 消息接收处理。
    /// 离线判定只依据 <c>method == "close"</c>，不再依赖主题前缀。
    /// </summary>
    /// <param name="e">MQTT 消息事件参数。</param>
    private Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        try
        {
            var topic = e.ApplicationMessage.Topic;
            var rawPayload = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment);

            _logger?.LogDebug("安圣 MQTT 消息: Topic={Topic}, Payload={Payload}", topic, rawPayload);

            var message = _parser?.Parse(rawPayload);

            // IMEI 优先取报文内字段，其次从主题解析
            var imei = !string.IsNullOrWhiteSpace(message?.Imei)
                ? message!.Imei!
                : AnShengMessageParser.ExtractImeiFromTopic(topic);

            if (string.IsNullOrEmpty(imei))
            {
                _logger?.LogWarning("无法确定消息所属 IMEI: Topic={Topic}, Payload={Payload}", topic, rawPayload);
                return Task.CompletedTask;
            }

            // 学习设备品类，供后续下发决定是否注入 timestamp
            LearnDeviceKind(imei, message);

            // 广播上行报文到进程内总线，供探测服务等订阅方按 (imei, method) 关联应答。
            // 放在 Will 判定之前：Will 分支会提前 return，放后面会丢掉 close 报文。
            // Publish 内部已做异常隔离，订阅者抛异常不会波及本 MQTT 接收线程。
            AnSheng.AnShengUplinkHub.Publish(imei, message?.Method, message, rawPayload);

            // 离线判定：method == "close"（will 与数据同主题，不能用主题前缀判断）
            if (message != null && AnShengMessageParser.IsWillMessage(message))
            {
                HandleWillMessage(imei, rawPayload);
                return Task.CompletedTask;
            }

            var normalizedData = message != null
                ? _parser!.NormalizeForSensorData(message, topic)
                : rawPayload;

            DataReceived?.Invoke(this, new DeviceDataReceivedEventArgs
            {
                DeviceId = 0L, // IMEI → DeviceId 映射在 ProtocolConfigService 中完成
                SerialNumber = imei,
                AppCode = string.Empty,
                Data = normalizedData,
                RawData = e.ApplicationMessage.PayloadSegment.Array,
                ReceivedAt = DateTime.UtcNow,
                ProtocolType = ProtocolType
            });

            // 命令响应：带 frameId 且非事件的报文，桥接给上层匹配在途命令
            if (message != null
                && !string.IsNullOrWhiteSpace(message.FrameId)
                && !message.IsEvent)
            {
                CommandResponse?.Invoke(this, new DeviceCommandResponseEventArgs
                {
                    DeviceId = 0L, // IMEI → DeviceId 映射在 ProtocolConfigService 中完成
                    CommandId = message.FrameId!,
                    Status = message.IsOk ? "SUCCESS" : "FAILED",
                    ResponseData = rawPayload,
                    RespondedAt = DateTime.UtcNow
                });
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "处理安圣 MQTT 消息异常");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 从上行报文中学习设备品类（netType / model / version）。
    /// </summary>
    /// <param name="imei">设备 IMEI。</param>
    /// <param name="message">已解析的报文；为 null 时不处理。</param>
    private void LearnDeviceKind(string imei, AnShengMessage? message)
    {
        if (message == null || DeviceKinds.ContainsKey(imei)) return;

        try
        {
            string? netType = null, model = null, version = null;

            if (string.Equals(message.Method, "getDevInfo", StringComparison.Ordinal))
            {
                var info = _parser?.ParseDevInfo(message);
                netType = info?.NetType;
                model = info?.Model;
                version = info?.Version;
            }
            else if (string.Equals(message.Method, "getDevStatus", StringComparison.Ordinal))
            {
                var status = _parser?.ParseDevStatus(message);
                netType = status?.NetType;
                model = status?.Model;
                version = status?.Version;
            }

            var kind = AnShengDeviceKindResolver.Resolve(netType, version, model);
            if (kind != AnShengDeviceKind.Unknown)
            {
                DeviceKinds[imei] = kind;
                _logger?.LogInformation("识别安圣设备品类: IMEI={IMEI}, Kind={Kind}", imei, kind.ToDisplayName());
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "识别安圣设备品类失败: IMEI={IMEI}", imei);
        }
    }

    /// <summary>
    /// MQTT 断开连接处理。
    /// </summary>
    /// <param name="e">断开事件参数。</param>
    private async Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs e)
    {
        _logger?.LogWarning("安圣 MQTT 连接断开: {Reason}, ClientWasConnected={ClientWasConnected}",
            e.Reason, e.ClientWasConnected);

        ConnectionStateChanged?.Invoke(this, false);

        // 如果已主动 Dispose 或正在进行另一轮重连，跳过
        if (_disposed || _reconnecting) return;

        _ = Task.Run(() => ReconnectLoopAsync());
    }

    private async Task ReconnectLoopAsync()
    {
        if (_reconnecting) return;
        _reconnecting = true;
        _reconnectAttempt = 0;

        try
        {
            while (_reconnectAttempt < MaxReconnectAttempts && !_disposed)
            {
                _reconnectAttempt++;
                var delay = TimeSpan.FromMilliseconds(
                    Math.Min(ReconnectBaseDelay.TotalMilliseconds * Math.Pow(2, _reconnectAttempt - 1),
                             ReconnectMaxDelay.TotalMilliseconds));

                _logger?.LogInformation(
                    "安圣 MQTT 重连尝试 {Attempt}/{MaxAttempts}，等待 {Delay:F0}s...",
                    _reconnectAttempt, MaxReconnectAttempts, delay.TotalSeconds);

                await Task.Delay(delay);

                if (_disposed) break;

                try
                {
                    // 构建连接串（从 _options 重建 JSON）
                    var connStr = System.Text.Json.JsonSerializer.Serialize(_options);
                    var connected = await ConnectAsync(connStr);
                    if (connected)
                    {
                        _logger?.LogInformation("安圣 MQTT 重连成功（第 {Attempt} 次尝试）", _reconnectAttempt);

                        // 重连后重新订阅
                        if (_options != null)
                        {
                            await StartDataCollectionAsync(default);
                        }

                        _reconnectAttempt = 0;
                        break;
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "安圣 MQTT 重连失败（第 {Attempt}/{MaxAttempts} 次）",
                        _reconnectAttempt, MaxReconnectAttempts);
                }

                // 检查 MQTT 客户端是否已意外恢复连接（如自动恢复）
                if (_mqttClient?.IsConnected == true)
                {
                    _logger?.LogInformation("安圣 MQTT 连接已恢复");
                    _reconnectAttempt = 0;
                    break;
                }
            }

            if (_reconnectAttempt >= MaxReconnectAttempts)
            {
                _logger?.LogError("安圣 MQTT 重连失败：已尝试 {MaxAttempts} 次，放弃重连", MaxReconnectAttempts);
            }
        }
        finally
        {
            _reconnecting = false;
        }
    }

    /// <summary>
    /// 处理设备离线（will）报文，格式：<c>{"imei":"...","method":"close"}</c>。
    /// </summary>
    /// <param name="imei">设备 IMEI。</param>
    /// <param name="payload">原始报文。</param>
    private void HandleWillMessage(string imei, string payload)
    {
        try
        {
            _logger?.LogWarning("安圣设备离线 Will: IMEI={IMEI}, Payload={Payload}", imei, payload);

            DeviceWill?.Invoke(this, new AnSheng.AnShengWillEventArgs
            {
                Imei = imei,
                Payload = payload,
                ReceivedAt = DateTime.UtcNow,
                AppCode = null // AppCode 由事件消费者按 IMEI 反查
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "处理安圣 Will 消息异常");
        }
    }

    /// <summary>
    /// QoS 级别转换。
    /// </summary>
    /// <param name="qosLevel">配置中的 QoS 数值。</param>
    /// <returns>MQTTnet QoS 枚举。</returns>
    private static MqttQualityOfServiceLevel ConvertToMqttQoS(int qosLevel)
    {
        return qosLevel switch
        {
            0 => MqttQualityOfServiceLevel.AtMostOnce,
            1 => MqttQualityOfServiceLevel.AtLeastOnce,
            2 => MqttQualityOfServiceLevel.ExactlyOnce,
            _ => MqttQualityOfServiceLevel.AtLeastOnce
        };
    }

    /// <summary>
    /// 停止健康检查定时器。
    /// </summary>
    private void StopHealthCheckTimer()
    {
        _healthCheckTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _healthCheckTimer?.Dispose();
        _healthCheckTimer = null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        StopHealthCheckTimer();
        DisconnectAsync().GetAwaiter().GetResult();
        _mqttClient?.Dispose();
        _dataCollectionCts?.Dispose();
        _throttle?.Dispose();
    }
}
