using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using System.Text;

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
        var mqttBroker = _configuration["Mqtt:Broker"] ?? "localhost";
        var mqttPort = int.Parse(_configuration["Mqtt:Port"] ?? "1883");
        var mqttClientId = _configuration["Mqtt:ClientId"] ?? "IoTPlatformServer";

        var options = new MqttClientOptionsBuilder()
            .WithClientId(mqttClientId)
            .WithTcpServer(mqttBroker, mqttPort)
            .WithCleanSession()
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
            .Build();

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
        var topic = "+/+/data";
        await SubscribeToTopicAsync(topic);
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

            // 解析主题: appCode/deviceId/data
            var topicParts = topic.Split('/');
            if (topicParts.Length >= 3)
            {
                var appCode = topicParts[0];
                var deviceIdStr = topicParts[1];

                if (long.TryParse(deviceIdStr, out var deviceId))
                {
                    // 触发数据接收事件
                    OnDataReceived?.Invoke(this, new DeviceDataEventArgs
                    {
                        DeviceId = deviceId,
                        AppCode = appCode,
                        SensorData = payload,
                        Timestamp = DateTime.UtcNow
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing MQTT message");
        }
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
