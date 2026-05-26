using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using System.Text;
using System.Text.Json;

namespace IoTPlatform.Services;

/// <summary>
/// MQTT客户端服务实现（BackgroundService）
/// </summary>
public class MqttClientService : IMqttClientService, IDisposable
{
    private IMqttClient? _mqttClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MqttClientService> _logger;
    private bool _isDisposed;

    public event EventHandler<DeviceDataEventArgs>? OnDataReceived;
    public event EventHandler<CommandResponseEventArgs>? OnCommandResponse;

    public MqttClientService(IConfiguration configuration, ILogger<MqttClientService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// 启动MQTT客户端
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // 读取 MQTT 配置（与 appsettings.json 的 "MQTT" 节对齐）
        var mqttBroker = _configuration["MQTT:Server"] ?? _configuration["Mqtt:Broker"] ?? "localhost";
        var mqttPort = int.Parse(_configuration["MQTT:Port"] ?? _configuration["Mqtt:Port"] ?? "1883");
        var mqttClientId = _configuration["MQTT:ClientId"] ?? _configuration["Mqtt:ClientId"] ?? "IoTPlatformServer";
        var mqttUsername = _configuration["MQTT:Username"];
        var mqttPassword = _configuration["MQTT:Password"];

        var optionsBuilder = new MqttClientOptionsBuilder()
            .WithClientId(mqttClientId)
            .WithTcpServer(mqttBroker, mqttPort)
            .WithCleanSession()
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(30));

        // 仅在配置了凭据时启用认证
        if (!string.IsNullOrWhiteSpace(mqttUsername))
        {
            optionsBuilder.WithCredentials(mqttUsername, mqttPassword ?? "");
        }

        var options = optionsBuilder.Build();

        _mqttClient = new MqttFactory().CreateMqttClient();

        _mqttClient.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;
        _mqttClient.ConnectedAsync += OnConnectedAsync;
        _mqttClient.DisconnectedAsync += OnDisconnectedAsync;

        await _mqttClient.ConnectAsync(options, cancellationToken);

        // 订阅所有设备数据主题
        await SubscribeToAllDevicesAsync();

        _logger.LogInformation("MQTT Client started and connected to {Broker}:{Port}", mqttBroker, mqttPort);
    }

    /// <summary>
    /// 停止MQTT客户端
    /// </summary>
    public async Task StopAsync()
    {
        if (_mqttClient != null)
        {
            await _mqttClient.DisconnectAsync();
            _mqttClient.ApplicationMessageReceivedAsync -= OnMessageReceivedAsync;
            _mqttClient.ConnectedAsync -= OnConnectedAsync;
            _mqttClient.DisconnectedAsync -= OnDisconnectedAsync;
            _logger.LogInformation("MQTT Client stopped");
        }
    }

    /// <summary>
    /// 订阅主题
    /// </summary>
    public async Task SubscribeToTopicAsync(string topic)
    {
        if (_mqttClient == null || !_mqttClient.IsConnected)
        {
            _logger.LogWarning("MQTT Client is not connected. Cannot subscribe to topic: {Topic}", topic);
            return;
        }

        await _mqttClient.SubscribeAsync(topic);
        _logger.LogInformation("Subscribed to topic: {Topic}", topic);
    }

    /// <summary>
    /// 发布消息
    /// </summary>
    public async Task PublishMessageAsync(string topic, string payload)
    {
        if (_mqttClient == null || !_mqttClient.IsConnected)
        {
            _logger.LogWarning("MQTT Client is not connected. Cannot publish to topic: {Topic}", topic);
            return;
        }

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();

        await _mqttClient.PublishAsync(message);
        _logger.LogDebug("Published message to topic: {Topic}", topic);
    }

    /// <summary>
    /// 订阅所有设备数据主题
    /// </summary>
    private async Task SubscribeToAllDevicesAsync()
    {
        // 订阅通配符主题: appCode/+/data
        await SubscribeToTopicAsync("+/+/data");
        // 订阅所有设备指令响应主题: appCode/+/command/response
        await SubscribeToTopicAsync("+/+/command/response");
    }

    /// <summary>
    /// 消息接收处理
    /// </summary>
    private async Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        try
        {
            var topic = e.ApplicationMessage.Topic;
            var payload = e.ApplicationMessage.ConvertPayloadToString();

            _logger.LogDebug("Received message from topic: {Topic}, Payload: {Payload}", topic, payload);

            // 解析主题: appCode/deviceId/data 或 appCode/deviceId/command/response
            var topicParts = topic.Split('/');
            if (topicParts.Length >= 3)
            {
                var appCode = topicParts[0];
                var deviceIdStr = topicParts[1];
                var messageType = topicParts[2];

                if (!long.TryParse(deviceIdStr, out var deviceId))
                    return;

                if (messageType == "data")
                {
                    // 设备上报数据
                    OnDataReceived?.Invoke(this, new DeviceDataEventArgs
                    {
                        DeviceId = deviceId,
                        AppCode = appCode,
                        SensorData = payload,
                        Timestamp = DateTime.UtcNow
                    });
                }
                else if (messageType == "command" && topicParts.Length >= 4 && topicParts[3] == "response")
                {
                    // 设备指令响应: appCode/deviceId/command/response
                    try
                    {
                        var response = JsonSerializer.Deserialize<CommandResponsePayload>(payload,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                        if (response != null)
                        {
                            OnCommandResponse?.Invoke(this, new CommandResponseEventArgs
                            {
                                DeviceId = deviceId,
                                AppCode = appCode,
                                CommandId = response.CommandId ?? string.Empty,
                                Status = response.Status ?? "unknown",
                                ResultData = response.Data,
                                ErrorMessage = response.Error,
                                RespondedAt = DateTime.UtcNow
                            });
                        }
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning(ex, "解析指令响应JSON失败，Topic={Topic}", topic);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing MQTT message");
        }
    }

    /// <summary>
    /// 下发设备指令
    /// 主题格式：{appCode}/{deviceId}/command
    /// </summary>
    public async Task SendDeviceCommandAsync(string appCode, long deviceId, string commandId, string commandType, string? parameters)
    {
        var payload = JsonSerializer.Serialize(new
        {
            commandId,
            commandType,
            parameters = string.IsNullOrEmpty(parameters) ? null : JsonSerializer.Deserialize<object>(parameters),
            timestamp = DateTime.UtcNow
        });

        var topic = $"{appCode}/{deviceId}/command";
        await PublishMessageAsync(topic, payload);
        _logger.LogInformation("已下发指令 CommandId={CommandId}, Type={Type}, Topic={Topic}", commandId, commandType, topic);
    }

    /// <summary>
    /// 指令响应载荷结构
    /// </summary>
    private class CommandResponsePayload
    {
        public string? CommandId { get; set; }
        public string? Status { get; set; }
        public string? Data { get; set; }
        public string? Error { get; set; }
    }

    /// <summary>
    /// 连接成功处理
    /// </summary>
    private async Task OnConnectedAsync(MqttClientConnectedEventArgs e)
    {
        _logger.LogInformation("MQTT Client connected successfully");
        await Task.CompletedTask;
    }

    /// <summary>
    /// 断开连接处理
    /// </summary>
    private async Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs e)
    {
        _logger.LogWarning("MQTT Client disconnected. Reason: {Reason}", e.Reason);
        await Task.CompletedTask;
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (!_isDisposed)
        {
            StopAsync().Wait();
            _isDisposed = true;
        }
    }
}
