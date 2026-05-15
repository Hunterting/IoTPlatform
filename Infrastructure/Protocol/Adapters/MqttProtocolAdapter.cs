using IoTPlatform.Models;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Packets;
using MQTTnet.Protocol;
using System.Text;
using System.Text.Json;

namespace IoTPlatform.Infrastructure.Protocol.Adapters;

/// <summary>
/// MQTT 协议适配器
/// 用于通过 MQTT 协议与 IoT 设备通信
/// </summary>
public class MqttProtocolAdapter : IProtocolAdapter
{
    private readonly ILogger<MqttProtocolAdapter>? _logger;
    private readonly int _configId;
    private IMqttClient? _mqttClient;
    private MqttProtocolOptions? _options;
    private CancellationTokenSource? _dataCollectionCts;
    private bool _isCollecting;
    private bool _disposed;

    public string ProtocolType => "MQTT";
    public bool IsConnected => _mqttClient?.IsConnected ?? false;
    public int ConfigId => _configId;

    public event EventHandler<DeviceDataReceivedEventArgs>? DataReceived;
    public event EventHandler<DeviceCommandResponseEventArgs>? CommandResponse;
    public event EventHandler<bool>? ConnectionStateChanged;

    public MqttProtocolAdapter(int configId, ILogger<MqttProtocolAdapter>? logger = null)
    {
        _configId = configId;
        _logger = logger;
    }

    public async Task<bool> ConnectAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_mqttClient?.IsConnected == true)
            {
                return true;
            }

            _options = JsonSerializer.Deserialize<MqttProtocolOptions>(connectionString)
                ?? throw new ArgumentException("无效的 MQTT 连接配置");

            var factory = new MqttFactory();
            _mqttClient = factory.CreateMqttClient();

            // 配置客户端选项
            var clientOptions = new MqttClientOptionsBuilder()
                .WithTcpServer(_options.Host, _options.Port)
                .WithCredentials(_options.Username, _options.Password)
                .WithClientId($"{_options.ClientIdPrefix}_{Guid.NewGuid():N}")
                .WithCleanSession(_options.CleanSession)
                .WithTimeout(TimeSpan.FromSeconds(_options.TimeoutSeconds))
                .WithKeepAlivePeriod(TimeSpan.FromSeconds(_options.KeepAliveSeconds))
                .Build();

            // 注册消息接收处理
            _mqttClient.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;
            _mqttClient.DisconnectedAsync += OnDisconnectedAsync;

            // 连接
            var response = await _mqttClient.ConnectAsync(clientOptions, cancellationToken);

            if (response.ResultCode == MqttClientConnectResultCode.Success)
            {
                _logger?.LogInformation("MQTT 协议适配器连接成功: {Host}:{Port}", _options.Host, _options.Port);
                ConnectionStateChanged?.Invoke(this, true);
                return true;
            }

            _logger?.LogError("MQTT 连接失败: {ResultCode}", response.ResultCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "MQTT 连接异常");
            return false;
        }
    }

    public async Task DisconnectAsync()
    {
        if (_mqttClient == null) return;

        try
        {
            await StopDataCollectionAsync();
            await _mqttClient.DisconnectAsync();
            _logger?.LogInformation("MQTT 协议适配器已断开连接");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "MQTT 断开连接异常");
        }
        finally
        {
            ConnectionStateChanged?.Invoke(this, false);
        }
    }

    public async Task StartDataCollectionAsync(CancellationToken cancellationToken = default)
    {
        if (_mqttClient?.IsConnected != true || _options == null)
        {
            throw new InvalidOperationException("MQTT 客户端未连接");
        }

        if (_isCollecting)
        {
            return;
        }

        _dataCollectionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _isCollecting = true;

        try
        {
            // 订阅数据主题
            foreach (var topic in _options.SubscribeTopics)
            {
                var qos = ConvertToMqttQoS(_options.QosLevel);
                var topicFilter = new MqttTopicFilterBuilder()
                    .WithTopic(topic)
                    .WithQualityOfServiceLevel(qos)
                    .Build();

                await _mqttClient.SubscribeAsync(topicFilter, _dataCollectionCts.Token);
                _logger?.LogInformation("已订阅 MQTT 主题: {Topic}", topic);
            }

            // 订阅指令响应主题（如果配置了）
            if (!string.IsNullOrEmpty(_options.CommandResponseTopic))
            {
                var responseTopicFilter = new MqttTopicFilterBuilder()
                    .WithTopic(_options.CommandResponseTopic)
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                    .Build();

                await _mqttClient.SubscribeAsync(responseTopicFilter, _dataCollectionCts.Token);
                _logger?.LogInformation("已订阅指令响应主题: {Topic}", _options.CommandResponseTopic);
            }

            _logger?.LogInformation("MQTT 数据采集已启动");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "MQTT 数据采集启动失败");
            _isCollecting = false;
            throw;
        }
    }

    public Task StopDataCollectionAsync()
    {
        _isCollecting = false;
        _dataCollectionCts?.Cancel();
        _dataCollectionCts?.Dispose();
        _dataCollectionCts = null;

        _logger?.LogInformation("MQTT 数据采集已停止");
        return Task.CompletedTask;
    }

    public async Task<string> SendCommandAsync(long deviceId, string serialNumber, string commandType, string parameters, CancellationToken cancellationToken = default)
    {
        if (_mqttClient?.IsConnected != true || _options == null)
        {
            throw new InvalidOperationException("MQTT 客户端未连接");
        }

        var commandId = Guid.NewGuid().ToString("N");
        var topic = string.Format(_options.CommandTopicTemplate, serialNumber);

        var commandPayload = new
        {
            CommandId = commandId,
            DeviceId = deviceId,
            SerialNumber = serialNumber,
            Type = commandType,
            Parameters = parameters,
            Timestamp = DateTime.UtcNow.ToString("O")
        };

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(JsonSerializer.Serialize(commandPayload))
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .WithRetainFlag(false)
            .Build();

        await _mqttClient.PublishAsync(message, cancellationToken);

        _logger?.LogInformation("MQTT 指令已发送: CommandId={CommandId}, Topic={Topic}", commandId, topic);
        return commandId;
    }

    public async Task ReadDataPointsAsync(long deviceId, string serialNumber, IEnumerable<string> dataPoints, CancellationToken cancellationToken = default)
    {
        if (_mqttClient?.IsConnected != true || _options == null)
        {
            throw new InvalidOperationException("MQTT 客户端未连接");
        }

        var readRequestId = Guid.NewGuid().ToString("N");
        var topic = string.Format(_options.ReadTopicTemplate ?? "device/{0}/read", serialNumber);

        var readPayload = new
        {
            RequestId = readRequestId,
            DeviceId = deviceId,
            SerialNumber = serialNumber,
            DataPoints = dataPoints,
            Timestamp = DateTime.UtcNow.ToString("O")
        };

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(JsonSerializer.Serialize(readPayload))
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .WithRetainFlag(false)
            .Build();

        await _mqttClient.PublishAsync(message, cancellationToken);

        _logger?.LogInformation("MQTT 数据读取请求已发送: RequestId={RequestId}, Topic={Topic}", readRequestId, topic);
    }

    private Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        try
        {
            var topic = e.ApplicationMessage.Topic;
            var payload = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment);

            _logger?.LogDebug("MQTT 消息接收: Topic={Topic}, Payload={Payload}", topic, payload);

            // 判断是数据消息还是指令响应
            if (_options != null && !string.IsNullOrEmpty(_options.CommandResponseTopic) && topic.StartsWith(_options.CommandResponseTopic))
            {
                // 尝试解析为指令响应
                try
                {
                    var response = JsonSerializer.Deserialize<CommandResponsePayload>(payload);
                    if (response != null)
                    {
                        CommandResponse?.Invoke(this, new DeviceCommandResponseEventArgs
                        {
                            DeviceId = response.DeviceId ?? 0,
                            CommandId = response.CommandId ?? string.Empty,
                            Status = response.Status ?? string.Empty,
                            ResponseData = payload,
                            RespondedAt = DateTime.UtcNow
                        });
                    }
                }
                catch
                {
                    // 解析失败，忽略
                }
            }
            else
            {
                DataReceived?.Invoke(this, new DeviceDataReceivedEventArgs
                {
                    SerialNumber = ExtractSerialNumber(topic),
                    Data = payload,
                    RawData = e.ApplicationMessage.PayloadSegment.Array,
                    ReceivedAt = DateTime.UtcNow,
                    ProtocolType = ProtocolType
                });
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "处理 MQTT 消息异常");
        }

        return Task.CompletedTask;
    }

    private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs e)
    {
        _logger?.LogWarning("MQTT 连接断开: {Reason}", e.Reason);
        ConnectionStateChanged?.Invoke(this, false);
        return Task.CompletedTask;
    }

    private string ExtractSerialNumber(string topic)
    {
        // 从主题中提取序列号，假设主题格式为 "device/{serialNumber}/..."
        var parts = topic.Split('/');
        if (parts.Length >= 2)
        {
            return parts[1];
        }
        return string.Empty;
    }

    private static MqttQualityOfServiceLevel ConvertToMqttQoS(int qosLevel)
    {
        return qosLevel switch
        {
            0 => MqttQualityOfServiceLevel.AtMostOnce,
            1 => MqttQualityOfServiceLevel.AtLeastOnce,
            2 => MqttQualityOfServiceLevel.ExactlyOnce,
            _ => MqttQualityOfServiceLevel.AtMostOnce
        };
    }

    public void Dispose()
    {
        if (_disposed) return;

        DisconnectAsync().GetAwaiter().GetResult();
        _mqttClient?.Dispose();
        _dataCollectionCts?.Dispose();
        _disposed = true;
    }
}

/// <summary>
/// MQTT 协议配置选项
/// </summary>
public class MqttProtocolOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1883;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string ClientIdPrefix { get; set; } = "iot_platform";
    public bool CleanSession { get; set; } = true;
    public int TimeoutSeconds { get; set; } = 30;
    public int KeepAliveSeconds { get; set; } = 60;
    public List<string> SubscribeTopics { get; set; } = new();
    public string? CommandTopicTemplate { get; set; }
    public string? CommandResponseTopic { get; set; }
    public string? ReadTopicTemplate { get; set; }
    public int QosLevel { get; set; } = 1;
}

/// <summary>
/// MQTT 指令响应载荷
/// </summary>
internal class CommandResponsePayload
{
    public long? DeviceId { get; set; }
    public string? CommandId { get; set; }
    public string? Status { get; set; }
    public object? Data { get; set; }
    public string? Timestamp { get; set; }
}
