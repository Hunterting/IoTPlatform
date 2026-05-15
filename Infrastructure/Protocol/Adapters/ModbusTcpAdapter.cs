using Microsoft.Extensions.Logging;
using System.Net.Sockets;
using System.Text.Json;

namespace IoTPlatform.Infrastructure.Protocol.Adapters;

/// <summary>
/// Modbus TCP 协议适配器
/// 用于通过 Modbus TCP 协议与工业设备通信
/// </summary>
public class ModbusTcpAdapter : IProtocolAdapter
{
    private readonly ILogger<ModbusTcpAdapter>? _logger;
    private readonly int _configId;
    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private ModbusTcpOptions? _options;
    private CancellationTokenSource? _dataCollectionCts;
    private bool _isCollecting;
    private bool _disposed;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public string ProtocolType => "ModbusTCP";
    public bool IsConnected => _tcpClient?.Connected ?? false;
    public int ConfigId => _configId;

    public event EventHandler<DeviceDataReceivedEventArgs>? DataReceived;
    public event EventHandler<DeviceCommandResponseEventArgs>? CommandResponse;
    public event EventHandler<bool>? ConnectionStateChanged;

    public ModbusTcpAdapter(int configId, ILogger<ModbusTcpAdapter>? logger = null)
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
                if (IsConnected)
                {
                    return true;
                }

                _options = JsonSerializer.Deserialize<ModbusTcpOptions>(connectionString)
                    ?? throw new ArgumentException("无效的 Modbus TCP 连接配置");

                _tcpClient = new TcpClient();
                _tcpClient.ReceiveTimeout = _options.TimeoutMs;
                _tcpClient.SendTimeout = _options.TimeoutMs;

                await _tcpClient.ConnectAsync(_options.Host, _options.Port, cancellationToken);
                _stream = _tcpClient.GetStream();

                _logger?.LogInformation("Modbus TCP 适配器连接成功: {Host}:{Port}", _options.Host, _options.Port);
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
            _logger?.LogError(ex, "Modbus TCP 连接异常");
            return false;
        }
    }

    public async Task DisconnectAsync()
    {
        await _lock.WaitAsync();
        try
        {
            await StopDataCollectionAsync();

            _stream?.Close();
            _stream?.Dispose();
            _tcpClient?.Close();
            _tcpClient?.Dispose();
            _stream = null;
            _tcpClient = null;

            _logger?.LogInformation("Modbus TCP 适配器已断开连接");
            ConnectionStateChanged?.Invoke(this, false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Modbus TCP 断开连接异常");
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task StartDataCollectionAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConnected || _options == null)
        {
            throw new InvalidOperationException("Modbus TCP 客户端未连接");
        }

        if (_isCollecting)
        {
            return;
        }

        _dataCollectionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _isCollecting = true;

        // 启动后台轮询任务
        _ = Task.Run(async () => await PollDataAsync(_dataCollectionCts.Token), _dataCollectionCts.Token);

        _logger?.LogInformation("Modbus TCP 数据采集已启动");
    }

    private async Task PollDataAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _isCollecting)
        {
            try
            {
                foreach (var device in _options?.Devices ?? Enumerable.Empty<ModbusDeviceConfig>())
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    await ReadDeviceDataAsync(device, cancellationToken);
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
                _logger?.LogError(ex, "Modbus TCP 数据轮询异常");
                await Task.Delay(5000, cancellationToken);
            }
        }
    }

    private async Task ReadDeviceDataAsync(ModbusDeviceConfig device, CancellationToken cancellationToken)
    {
        var data = new Dictionary<string, object>();

        foreach (var register in device.Registers)
        {
            try
            {
                var value = await ReadHoldingRegisterAsync(
                    device.SlaveId,
                    (ushort)register.Address,
                    (ushort)register.Count,
                    cancellationToken);

                data[register.Name] = value;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning("读取寄存器 {Address} 失败: {Message}", register.Address, ex.Message);
            }
        }

        if (data.Count > 0)
        {
            var args = new DeviceDataReceivedEventArgs
            {
                DeviceId = device.DeviceId,
                SerialNumber = device.SerialNumber,
                AppCode = _options?.AppCode ?? string.Empty,
                Data = JsonSerializer.Serialize(data),
                ProtocolType = ProtocolType,
                ReceivedAt = DateTime.UtcNow
            };

            DataReceived?.Invoke(this, args);
        }
    }

    private async Task<ushort[]> ReadHoldingRegisterAsync(byte slaveId, ushort startAddress, ushort count, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            // 构建 Modbus TCP 请求
            var transactionId = (ushort)DateTime.Now.Ticks;
            var request = BuildReadHoldingRegistersRequest(transactionId, slaveId, startAddress, count);

            // 发送请求
            await _stream!.WriteAsync(request, cancellationToken);

            // 接收响应
            var response = new byte[256];
            var bytesRead = await _stream.ReadAsync(response.AsMemory(0, 256), cancellationToken);

            if (bytesRead < 9)
            {
                throw new InvalidOperationException("Modbus 响应数据长度不足");
            }

            // 解析响应
            var byteCount = response[8];
            var values = new ushort[byteCount / 2];

            for (int i = 0; i < values.Length; i++)
            {
                values[i] = (ushort)((response[9 + i * 2] << 8) | response[10 + i * 2]);
            }

            return values;
        }
        finally
        {
            _lock.Release();
        }
    }

    private byte[] BuildReadHoldingRegistersRequest(ushort transactionId, byte slaveId, ushort startAddress, ushort count)
    {
        var request = new byte[12];

        // Transaction ID (2 bytes)
        request[0] = (byte)(transactionId >> 8);
        request[1] = (byte)transactionId;

        // Protocol ID (2 bytes) - always 0 for Modbus TCP
        request[2] = 0;
        request[3] = 0;

        // Length (2 bytes) - remaining bytes after this field
        request[4] = 0;
        request[5] = 6;

        // Unit ID
        request[6] = slaveId;

        // Function code (0x03 = Read Holding Registers)
        request[7] = 0x03;

        // Start Address (2 bytes)
        request[8] = (byte)(startAddress >> 8);
        request[9] = (byte)startAddress;

        // Quantity of Registers (2 bytes)
        request[10] = (byte)(count >> 8);
        request[11] = (byte)count;

        return request;
    }

    public Task StopDataCollectionAsync()
    {
        _isCollecting = false;
        _dataCollectionCts?.Cancel();
        _dataCollectionCts?.Dispose();
        _dataCollectionCts = null;

        _logger?.LogInformation("Modbus TCP 数据采集已停止");
        return Task.CompletedTask;
    }

    public async Task<string> SendCommandAsync(long deviceId, string serialNumber, string commandType, string parameters, CancellationToken cancellationToken = default)
    {
        if (!IsConnected || _options == null)
        {
            throw new InvalidOperationException("Modbus TCP 客户端未连接");
        }

        var commandId = Guid.NewGuid().ToString("N");
        var device = _options.Devices.FirstOrDefault(d => d.SerialNumber == serialNumber);

        if (device == null)
        {
            throw new ArgumentException($"未找到设备: {serialNumber}");
        }

        var commandParams = JsonSerializer.Deserialize<Dictionary<string, object>>(parameters) ?? new Dictionary<string, object>();

        // 根据命令类型执行不同的写入操作
        switch (commandType.ToLowerInvariant())
        {
            case "write_register":
                if (commandParams.TryGetValue("address", out var addr) && commandParams.TryGetValue("value", out var val))
                {
                    await WriteSingleRegisterAsync(device.SlaveId, (ushort)(int.Parse(addr.ToString()!)), (ushort)(int.Parse(val.ToString()!)), cancellationToken);
                }
                break;

            case "write_multiple":
                // 实现批量写入逻辑
                break;

            default:
                throw new NotSupportedException($"不支持的命令类型: {commandType}");
        }

        _logger?.LogInformation("Modbus TCP 指令已发送: CommandId={CommandId}, Device={SerialNumber}, Type={Type}",
            commandId, serialNumber, commandType);

        return commandId;
    }

    private async Task WriteSingleRegisterAsync(byte slaveId, ushort address, ushort value, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var transactionId = (ushort)DateTime.Now.Ticks;
            var request = BuildWriteSingleRegisterRequest(transactionId, slaveId, address, value);

            await _stream!.WriteAsync(request, cancellationToken);

            var response = new byte[12];
            await _stream.ReadAsync(response.AsMemory(0, 12), cancellationToken);

            if (response[7] != 0x06)
            {
                throw new InvalidOperationException("Modbus 写寄存器响应失败");
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private byte[] BuildWriteSingleRegisterRequest(ushort transactionId, byte slaveId, ushort address, ushort value)
    {
        var request = new byte[12];

        request[0] = (byte)(transactionId >> 8);
        request[1] = (byte)transactionId;
        request[2] = 0;
        request[3] = 0;
        request[4] = 0;
        request[5] = 6;
        request[6] = slaveId;
        request[7] = 0x06; // Write Single Register
        request[8] = (byte)(address >> 8);
        request[9] = (byte)address;
        request[10] = (byte)(value >> 8);
        request[11] = (byte)value;

        return request;
    }

    public async Task ReadDataPointsAsync(long deviceId, string serialNumber, IEnumerable<string> dataPoints, CancellationToken cancellationToken = default)
    {
        if (!IsConnected || _options == null)
        {
            throw new InvalidOperationException("Modbus TCP 客户端未连接");
        }

        var device = _options.Devices.FirstOrDefault(d => d.SerialNumber == serialNumber);
        if (device == null)
        {
            throw new ArgumentException($"未找到设备: {serialNumber}");
        }

        var data = new Dictionary<string, object>();
        var registers = device.Registers.Where(r => dataPoints.Contains(r.Name));

        foreach (var register in registers)
        {
            var values = await ReadHoldingRegisterAsync(device.SlaveId, (ushort)register.Address, (ushort)register.Count, cancellationToken);
            data[register.Name] = values[0];
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
            _stream?.Close();
            _stream?.Dispose();
            _tcpClient?.Close();
            _tcpClient?.Dispose();
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
/// Modbus TCP 协议配置选项
/// </summary>
public class ModbusTcpOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 502;
    public int TimeoutMs { get; set; } = 3000;
    public int PollIntervalMs { get; set; } = 5000;
    public List<ModbusDeviceConfig> Devices { get; set; } = new();
    public string? AppCode { get; set; }
}

/// <summary>
/// Modbus 设备配置
/// </summary>
public class ModbusDeviceConfig
{
    public long DeviceId { get; set; }
    public string SerialNumber { get; set; } = string.Empty;
    public byte SlaveId { get; set; } = 1;
    public List<ModbusRegisterConfig> Registers { get; set; } = new();
}

/// <summary>
/// Modbus 寄存器配置
/// </summary>
public class ModbusRegisterConfig
{
    public string Name { get; set; } = string.Empty;
    public int Address { get; set; }
    public int Count { get; set; } = 1;
    public string? Unit { get; set; }
    public double? Scale { get; set; }
}
