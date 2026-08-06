using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace IoTPlatform.Infrastructure.Protocol.Adapters;

/// <summary>
/// OPC UA 协议适配器
/// 用于通过 OPC UA 协议与工业设备通信
/// 注意：需要安装 OPC Foundation 的 OPCUA.NET.Stack NuGet 包
/// </summary>
public class OpcUaAdapter : IProtocolAdapter
{
    private readonly ILogger<OpcUaAdapter>? _logger;
    private readonly int _configId;
    private OpcUaOptions? _options;
    private CancellationTokenSource? _dataCollectionCts;
    private bool _isCollecting;
    private bool _disposed;
    private bool _isConnected;
    private readonly SemaphoreSlim _lock = new(1, 1);

    // OPC UA 连接标识（实际使用时替换为真实的 OPC UA Client）
    private object? _session;
    private readonly Dictionary<string, string> _monitoredItems = new();

    public string ProtocolType => "OpcUA";
    public bool IsConnected => _isConnected;
    public int ConfigId => _configId;

    public event EventHandler<DeviceDataReceivedEventArgs>? DataReceived;
    public event EventHandler<DeviceCommandResponseEventArgs>? CommandResponse;
    public event EventHandler<bool>? ConnectionStateChanged;

    public OpcUaAdapter(int configId, ILogger<OpcUaAdapter>? logger = null)
    {
        _configId = configId;
        _logger = logger;
    }

    public async Task<bool> ConnectAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        try
        {
            await _lock.WaitAsync(cancellationToken);
            try
            {
                if (_isConnected)
                {
                    return true;
                }

                _options = JsonSerializer.Deserialize<OpcUaOptions>(connectionString, ProtocolJsonOptions.CaseInsensitive)
                    ?? throw new ArgumentException("无效的 OPC UA 连接配置");

                ValidateOptions(_options);

                // TODO: 实际实现 OPC UA 连接
                // 实际使用时需要引用 OPC Foundation 的库：
                // Install-Package OPCFoundation.NetStandard.Opc.Ua
                //
                // 示例实现：
                // var selectedEndpoint = CoreClientUtils.SelectEndpoint(_options.EndpointUrl, false);
                // var endpointConfiguration = EndpointConfiguration.Create(_options);
                // var endpoint = new ConfiguredEndpoint(null, selectedEndpoint, endpointConfiguration);
                // _session = await Session.Create(
                //     new ApplicationConfiguration { ... },
                //     endpoint,
                //     false,
                //     "IoT Platform OPC UA Client",
                //     60000,
                //     new UserIdentity(new AnonymousIdentityToken()),
                //     null);

                _logger?.LogInformation("OPC UA 适配器连接成功: {EndpointUrl}", _options.EndpointUrl);
                _isConnected = true;
                ConnectionStateChanged?.Invoke(this, true);
                return true;
            }
            finally
            {
                _lock.Release();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "OPC UA 连接异常");
            return false;
        }
    }

    private void ValidateOptions(OpcUaOptions options)
    {
        if (string.IsNullOrEmpty(options.EndpointUrl))
            throw new ArgumentException("OPC UA 端点 URL 不能为空");

        if (!Uri.TryCreate(options.EndpointUrl, UriKind.Absolute, out _))
            throw new ArgumentException($"无效的 OPC UA 端点 URL: {options.EndpointUrl}");
    }

    public async Task DisconnectAsync()
    {
        await _lock.WaitAsync();
        try
        {
            await StopDataCollectionAsync();

            // TODO: 实际断开 OPC UA 连接
            // if (_session != null)
            // {
            //     await _session.CloseAsync();
            //     _session.Dispose();
            //     _session = null;
            // }

            _isConnected = false;
            _monitoredItems.Clear();

            _logger?.LogInformation("OPC UA 适配器已断开连接");
            ConnectionStateChanged?.Invoke(this, false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "OPC UA 断开连接异常");
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task StartDataCollectionAsync(CancellationToken cancellationToken = default)
    {
        if (!_isConnected || _options == null)
        {
            throw new InvalidOperationException("OPC UA 客户端未连接");
        }

        if (_isCollecting)
        {
            return;
        }

        // 设置监控项
        foreach (var node in _options.Nodes ?? Enumerable.Empty<OpcUaNodeConfig>())
        {
            var monitorId = Guid.NewGuid().ToString();
            _monitoredItems[monitorId] = node.NodeId;

            // TODO: 实际创建监控项
            // var subscription = _session.DefaultSubscription;
            // var monitoredItem = new MonitoredItem(subscription.DefaultItem)
            // {
            //     StartNodeId = node.NodeId,
            //     DisplayName = node.DisplayName,
            //     AttributeId = Attributes.Value
            // };
            // monitoredItem.Notification += OnMonitoredItemNotification;
            // subscription.AddItem(monitoredItem);
            // await subscription.ApplyChangesAsync();
        }

        _dataCollectionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _isCollecting = true;

        // 启动后台轮询任务（如果使用轮询模式而非订阅模式）
        if (_options.UsePollingMode)
        {
            _ = Task.Run(async () => await PollDataAsync(_dataCollectionCts.Token), _dataCollectionCts.Token);
        }

        _logger?.LogInformation("OPC UA 数据采集已启动");
    }

    private async Task PollDataAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _isCollecting)
        {
            try
            {
                foreach (var node in _options?.Nodes ?? Enumerable.Empty<OpcUaNodeConfig>())
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    await ReadNodeDataAsync(node, cancellationToken);
                }

                // 轮询间隔
                await Task.Delay(_options?.PollIntervalMs ?? 5000, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "OPC UA 数据轮询异常");
                await Task.Delay(5000, cancellationToken);
            }
        }
    }

    private async Task ReadNodeDataAsync(OpcUaNodeConfig node, CancellationToken cancellationToken)
    {
        try
        {
            // TODO: 实际读取 OPC UA 节点值
            // var value = await _session.ReadValueAsync(node.NodeId);

            // 模拟数据（实际使用时替换为真实读取）
            var data = new Dictionary<string, object>
            {
                { node.DisplayName ?? node.NodeId, GenerateSampleValue(node.DataType) }
            };

            var args = new DeviceDataReceivedEventArgs
            {
                DeviceId = node.DeviceId,
                SerialNumber = node.SerialNumber ?? string.Empty,
                AppCode = _options?.AppCode ?? string.Empty,
                Data = JsonSerializer.Serialize(data),
                ProtocolType = ProtocolType,
                ReceivedAt = DateTime.UtcNow
            };

            DataReceived?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("读取节点 {NodeId} 失败: {Message}", node.NodeId, ex.Message);
        }
    }

    private static object GenerateSampleValue(string? dataType)
    {
        var random = new Random();
        return dataType?.ToUpperInvariant() switch
        {
            "INT16" or "INT32" or "INT64" => random.Next(0, 1000),
            "UINT16" or "UINT32" or "UINT64" => random.Next(0, 1000),
            "FLOAT" or "DOUBLE" or "DECIMAL" => Math.Round(random.NextDouble() * 100, 2),
            "BOOLEAN" => random.Next(2) == 1,
            "STRING" => $"Value_{random.Next(1000)}",
            _ => random.Next(0, 100)
        };
    }

    public Task StopDataCollectionAsync()
    {
        _isCollecting = false;

        // TODO: 实际取消监控项订阅
        // foreach (var monitoredItem in _session.DefaultSubscription.MonitoredItems)
        // {
        //     await monitoredItem.RemoveAsync();
        // }

        _dataCollectionCts?.Cancel();
        _dataCollectionCts?.Dispose();
        _dataCollectionCts = null;

        _logger?.LogInformation("OPC UA 数据采集已停止");
        return Task.CompletedTask;
    }

    public async Task<string> SendCommandAsync(long deviceId, string serialNumber, string commandType, string parameters, CancellationToken cancellationToken = default)
    {
        if (!_isConnected || _options == null)
        {
            throw new InvalidOperationException("OPC UA 客户端未连接");
        }

        var commandId = Guid.NewGuid().ToString("N");
        var commandParams = JsonSerializer.Deserialize<Dictionary<string, object>>(parameters) ?? new Dictionary<string, object>();

        switch (commandType.ToLowerInvariant())
        {
            case "write_value":
                if (commandParams.TryGetValue("nodeId", out var nodeId) && commandParams.TryGetValue("value", out var value))
                {
                    await WriteNodeValueAsync(nodeId.ToString()!, value, cancellationToken);
                }
                break;

            case "call_method":
                if (commandParams.TryGetValue("methodId", out var methodId))
                {
                    await CallMethodAsync(methodId.ToString()!, commandParams, cancellationToken);
                }
                break;

            default:
                throw new NotSupportedException($"不支持的命令类型: {commandType}");
        }

        _logger?.LogInformation("OPC UA 指令已发送: CommandId={CommandId}, Device={SerialNumber}, Type={Type}",
            commandId, serialNumber, commandType);

        // 触发命令响应事件
        CommandResponse?.Invoke(this, new DeviceCommandResponseEventArgs
        {
            DeviceId = deviceId,
            CommandId = commandId,
            Status = "Success",
            RespondedAt = DateTime.UtcNow
        });

        return commandId;
    }

    private async Task WriteNodeValueAsync(string nodeId, object value, CancellationToken cancellationToken)
    {
        // TODO: 实际写入 OPC UA 节点值
        // var valueToWrite = new WrittenValue
        // {
        //     NodeId = nodeId,
        //     Value = ConvertToDataValue(value)
        // };
        // await _session.WriteAsync(new[] { valueToWrite });

        _logger?.LogInformation("OPC UA 写入节点: {NodeId} = {Value}", nodeId, value);
        await Task.CompletedTask;
    }

    private async Task CallMethodAsync(string methodId, Dictionary<string, object> parameters, CancellationToken cancellationToken)
    {
        // TODO: 实际调用 OPC UA 方法
        // var inputArguments = parameters.Values.Select(ConvertToVariant).ToArray();
        // var result = await _session.CallAsync(
        //     parameters["objectId"].ToString()!,
        //     methodId,
        //     inputArguments);

        _logger?.LogInformation("OPC UA 调用方法: {MethodId}", methodId);
        await Task.CompletedTask;
    }

    public async Task ReadDataPointsAsync(long deviceId, string serialNumber, IEnumerable<string> dataPoints, CancellationToken cancellationToken = default)
    {
        if (!_isConnected || _options == null)
        {
            throw new InvalidOperationException("OPC UA 客户端未连接");
        }

        var nodes = _options.Nodes?.Where(n => dataPoints.Contains(n.NodeId)) ?? Enumerable.Empty<OpcUaNodeConfig>();
        var data = new Dictionary<string, object>();

        foreach (var node in nodes)
        {
            // TODO: 实际读取 OPC UA 节点值
            // var value = await _session.ReadValueAsync(node.NodeId);
            data[node.DisplayName ?? node.NodeId] = GenerateSampleValue(node.DataType);
        }

        var args = new DeviceDataReceivedEventArgs
        {
            DeviceId = deviceId,
            SerialNumber = serialNumber,
            AppCode = _options.AppCode ?? string.Empty,
            Data = JsonSerializer.Serialize(data),
            ProtocolType = ProtocolType,
            ReceivedAt = DateTime.UtcNow
        };

        DataReceived?.Invoke(this, args);
    }

    public void Dispose()
    {
        if (_disposed) return;

        _dataCollectionCts?.Cancel();
        _dataCollectionCts?.Dispose();

        _lock.Wait();
        try
        {
            // TODO: 实际清理 OPC UA 会话
            // _session?.Dispose();
            _isConnected = false;
        }
        finally
        {
            _lock.Release();
            _lock.Dispose();
        }

        _disposed = true;
    }
}

/// <summary>
/// OPC UA 协议配置选项
/// </summary>
public class OpcUaOptions
{
    /// <summary>
    /// OPC UA 服务器端点 URL
    /// </summary>
    public string EndpointUrl { get; set; } = "opc.tcp://localhost:4840";

    /// <summary>
    /// 连接超时（毫秒）
    /// </summary>
    public int TimeoutMs { get; set; } = 30000;

    /// <summary>
    /// 轮询间隔（毫秒），仅在 UsePollingMode 为 true 时使用
    /// </summary>
    public int PollIntervalMs { get; set; } = 5000;

    /// <summary>
    /// 是否使用轮询模式（否则使用订阅模式）
    /// </summary>
    public bool UsePollingMode { get; set; } = false;

    /// <summary>
    /// 安全策略
    /// </summary>
    public string SecurityPolicy { get; set; } = "None";

    /// <summary>
    /// 安全模式
    /// </summary>
    public string SecurityMode { get; set; } = "SignAndEncrypt";

    /// <summary>
    /// 证书路径（可选）
    /// </summary>
    public string? CertificatePath { get; set; }

    /// <summary>
    /// 用户名（可选）
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// 密码（可选）
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// 要监控的节点列表
    /// </summary>
    public List<OpcUaNodeConfig>? Nodes { get; set; }

    /// <summary>
    /// 应用代码
    /// </summary>
    public string? AppCode { get; set; }
}

/// <summary>
/// OPC UA 节点配置
/// </summary>
public class OpcUaNodeConfig
{
    /// <summary>
    /// 节点 ID（如 ns=2;s=TemperatureSensor）
    /// </summary>
    public string NodeId { get; set; } = string.Empty;

    /// <summary>
    /// 显示名称
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// 数据类型
    /// </summary>
    public string? DataType { get; set; }

    /// <summary>
    /// 设备 ID
    /// </summary>
    public long DeviceId { get; set; }

    /// <summary>
    /// 设备序列号
    /// </summary>
    public string? SerialNumber { get; set; }

    /// <summary>
    /// 采样间隔（毫秒）
    /// </summary>
    public int SamplingIntervalMs { get; set; } = 1000;
}
