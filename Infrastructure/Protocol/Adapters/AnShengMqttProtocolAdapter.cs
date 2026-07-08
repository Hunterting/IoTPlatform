using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Packets;
using MQTTnet.Protocol;
using System.Text;
using System.Text.Json;
using IoTPlatform.Infrastructure.Protocol.AnSheng;

namespace IoTPlatform.Infrastructure.Protocol.Adapters;

/// <summary>
/// 安圣 MQTT 协议适配器
/// 实现 IProtocolAdapter，处理安圣设备的数据接收与命令下发
///
/// 安圣设备主题约定：
///   上行数据：   /devtoser/pub/{imei}
///   下行命令：   /sertodev/{imei}
///   掉线通知：   /devtoser/will/{imei}
///
/// 报文格式：{ "method": "...", "result": "...", "imei": "...", "frameId": "...", "timestamp": "..." }
/// </summary>
public class AnShengMqttProtocolAdapter : IProtocolAdapter
{
    private readonly ILogger<AnShengMqttProtocolAdapter>? _logger;
    private readonly int _configId;
    private IMqttClient? _mqttClient;
    private AnShengMqttProtocolOptions? _options;
    private AnShengMessageParser? _parser;
    private CancellationTokenSource? _dataCollectionCts;
    private bool _isCollecting;
    private bool _disposed;

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

    public AnShengMqttProtocolAdapter(int configId, ILogger<AnShengMqttProtocolAdapter>? logger = null)
    {
        _configId = configId;
        _logger = logger;
    }

    /// <summary>
    /// 连接到安圣 MQTT Broker
    /// </summary>
    public async Task<bool> ConnectAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_mqttClient?.IsConnected == true)
            {
                return true;
            }

            _options = JsonSerializer.Deserialize<AnShengMqttProtocolOptions>(connectionString)
                ?? throw new ArgumentException("无效的安圣 MQTT 连接配置");

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
    /// 断开连接
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
    /// 开始数据采集：订阅安圣设备的数据主题和 Will 主题
    /// </summary>
    public async Task StartDataCollectionAsync(CancellationToken cancellationToken = default)
    {
        if (_mqttClient?.IsConnected != true || _options == null)
        {
            throw new InvalidOperationException("安圣 MQTT 客户端未连接");
        }

        if (_isCollecting)
        {
            return;
        }

        _dataCollectionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _isCollecting = true;

        // 初始化消息解析器
        _parser = new AnShengMessageParser(null);

        try
        {
            var qos = ConvertToMqttQoS(_options.QosLevel);

            // 订阅设备上行数据主题 /devtoser/pub/+
            // 设备主动上报数据（getDevStatus、orderStart、orderEnd、orderUp 等）
            var dataTopicFilter = new MqttTopicFilterBuilder()
                .WithTopic(_options.PublishTopicPattern)
                .WithQualityOfServiceLevel(qos)
                .Build();
            await _mqttClient.SubscribeAsync(dataTopicFilter, _dataCollectionCts.Token);
            _logger?.LogInformation("已订阅安圣数据主题: {Topic}", _options.PublishTopicPattern);

            // 订阅 Will 遗愿主题 /devtoser/will/+
            // 检测设备离线
            if (!string.IsNullOrEmpty(_options.WillTopicPattern))
            {
                var willTopicFilter = new MqttTopicFilterBuilder()
                    .WithTopic(_options.WillTopicPattern)
                    .WithQualityOfServiceLevel(qos)
                    .Build();
                await _mqttClient.SubscribeAsync(willTopicFilter, _dataCollectionCts.Token);
                _logger?.LogInformation("已订阅安圣 Will 主题: {Topic}", _options.WillTopicPattern);
            }

            _logger?.LogInformation("安圣 MQTT 数据采集已启动");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "安圣 MQTT 数据采集启动失败");
            _isCollecting = false;
            throw;
        }
    }

    /// <summary>
    /// 停止数据采集
    /// </summary>
    public Task StopDataCollectionAsync()
    {
        _isCollecting = false;
        _dataCollectionCts?.Cancel();
        _dataCollectionCts?.Dispose();
        _dataCollectionCts = null;

        _logger?.LogInformation("安圣 MQTT 数据采集已停止");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 向安圣设备下发命令
    /// serialNumber 即设备 IMEI
    /// </summary>
    public async Task<string> SendCommandAsync(long deviceId, string serialNumber, string commandType,
        string parameters, CancellationToken cancellationToken = default)
    {
        if (_mqttClient?.IsConnected != true || _options == null)
        {
            throw new InvalidOperationException("安圣 MQTT 客户端未连接");
        }

        // 安圣设备主题：/sertodev/{imei}
        var topic = _options.SubscribeTopicTemplate.Replace("{imei}", serialNumber);

        // 序列化命令 parameters 为 JSON 对象
        object? parsedParams = null;
        if (!string.IsNullOrEmpty(parameters))
        {
            try
            {
                parsedParams = JsonSerializer.Deserialize<object>(parameters);
            }
            catch
            {
                parsedParams = parameters;
            }
        }

        var frameId = Guid.NewGuid().ToString("N");
        var commandPayload = new Dictionary<string, object?>
        {
            ["method"] = commandType,
            ["imei"] = serialNumber,
            ["frameId"] = frameId,
            ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString()
        };

        if (parsedParams != null)
        {
            commandPayload["param"] = parsedParams;
        }

        var payloadJson = JsonSerializer.Serialize(commandPayload);
        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payloadJson)
            .WithQualityOfServiceLevel(ConvertToMqttQoS(_options.QosLevel))
            .WithRetainFlag(false)
            .Build();

        await _mqttClient.PublishAsync(message, cancellationToken);

        _logger?.LogInformation("安圣命令已发送: Method={Method}, IMEI={IMEI}, FrameId={FrameId}, Topic={Topic}",
            commandType, serialNumber, frameId, topic);
        return frameId;
    }

    /// <summary>
    /// 主动读取安圣设备数据点（发送 getDevStatus 命令）
    /// </summary>
    public async Task ReadDataPointsAsync(long deviceId, string serialNumber, IEnumerable<string> dataPoints,
        CancellationToken cancellationToken = default)
    {
        // 安圣设备通过 getDevStatus 获取所有数据点
        var q = dataPoints != null && dataPoints.Any()
            ? string.Join(",", dataPoints)
            : string.Empty;

        var param = new Dictionary<string, object?>
        {
            ["q"] = q
        };

        var parameters = JsonSerializer.Serialize(param);
        await SendCommandAsync(deviceId, serialNumber, "getDevStatus", parameters, cancellationToken);
    }

    /// <summary>
    /// 消息接收处理
    /// </summary>
    private Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        try
        {
            var topic = e.ApplicationMessage.Topic;
            var rawPayload = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment);

            _logger?.LogDebug("安圣 MQTT 消息: Topic={Topic}, Payload={Payload}", topic, rawPayload);

            // 提取 IMEI
            var imei = AnShengMessageParser.ExtractImeiFromTopic(topic);
            if (string.IsNullOrEmpty(imei))
            {
                _logger?.LogWarning("无法从主题中提取 IMEI: {Topic}", topic);
                return Task.CompletedTask;
            }

            // 判断消息类型
            if (_options != null && topic.StartsWith("/devtoser/will"))
            {
                // Will 离线消息
                HandleWillMessage(imei, rawPayload);
            }
            else
            {
                // 使用 AnShengMessageParser 解析并标准化数据
                string normalizedData;
                if (_parser != null)
                {
                    var message = _parser.Parse(rawPayload);
                    if (message != null)
                    {
                        normalizedData = _parser.NormalizeForSensorData(message, topic);
                    }
                    else
                    {
                        // 解析失败，使用原始 JSON
                        normalizedData = rawPayload;
                    }
                }
                else
                {
                    normalizedData = rawPayload;
                }

                // 数据消息 — 通过 DataReceived 事件桥接到 ProtocolConfigService
                DataReceived?.Invoke(this, new DeviceDataReceivedEventArgs
                {
                    DeviceId = 0L, // IMEI → DeviceId 映射在 ProtocolConfigService 中完成
                    SerialNumber = imei, // IMEI 即 SerialNumber
                    AppCode = string.Empty,
                    Data = normalizedData,
                    RawData = e.ApplicationMessage.PayloadSegment.Array,
                    ReceivedAt = DateTime.UtcNow,
                    ProtocolType = ProtocolType
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
    /// MQTT 断开连接处理
    /// </summary>
    private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs e)
    {
        _logger?.LogWarning("安圣 MQTT 连接断开: {Reason}", e.Reason);
        ConnectionStateChanged?.Invoke(this, false);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 处理 Will 离线消息
    /// 安圣 will 格式：{ "method": "close", "imei": "..." }
    /// </summary>
    private void HandleWillMessage(string imei, string payload)
    {
        try
        {
            _logger?.LogWarning("安圣设备离线 Will: IMEI={IMEI}, Payload={Payload}", imei, payload);

            // 触发静态 Will 事件，供 AnShengDiscoveryService 监听处理
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
    /// QoS 级别转换
    /// </summary>
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        DisconnectAsync().GetAwaiter().GetResult();
        _mqttClient?.Dispose();
        _dataCollectionCts?.Dispose();
    }
}
