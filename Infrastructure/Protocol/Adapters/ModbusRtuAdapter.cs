using Microsoft.Extensions.Logging;
using System.IO.Ports;
using System.Text.Json;

namespace IoTPlatform.Infrastructure.Protocol.Adapters;

/// <summary>
/// Modbus RTU 协议适配器
/// 用于通过 Modbus RTU 协议与工业设备通信（串口/RS485）
/// </summary>
public class ModbusRtuAdapter : IProtocolAdapter
{
    private readonly ILogger<ModbusRtuAdapter>? _logger;
    private readonly int _configId;
    private SerialPort? _serialPort;
    private ModbusRtuOptions? _options;
    private CancellationTokenSource? _dataCollectionCts;
    private bool _isCollecting;
    private bool _disposed;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public string ProtocolType => "ModbusRTU";
    public bool IsConnected => _serialPort?.IsOpen ?? false;
    public int ConfigId => _configId;

    public event EventHandler<DeviceDataReceivedEventArgs>? DataReceived;
    public event EventHandler<DeviceCommandResponseEventArgs>? CommandResponse;
    public event EventHandler<bool>? ConnectionStateChanged;

    public ModbusRtuAdapter(int configId, ILogger<ModbusRtuAdapter>? logger = null)
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

                _options = JsonSerializer.Deserialize<ModbusRtuOptions>(connectionString)
                    ?? throw new ArgumentException("无效的 Modbus RTU 连接配置");

                ValidateOptions(_options);

                _serialPort = new SerialPort
                {
                    PortName = _options.PortName,
                    BaudRate = _options.BaudRate,
                    DataBits = _options.DataBits,
                    StopBits = ConvertStopBits(_options.StopBits),
                    Parity = ConvertParity(_options.Parity),
                    ReadTimeout = _options.TimeoutMs,
                    WriteTimeout = _options.TimeoutMs
                };

                _serialPort.Open();
                _serialPort.DiscardInBuffer();
                _serialPort.DiscardOutBuffer();

                _logger?.LogInformation("Modbus RTU 适配器连接成功: {PortName}@{BaudRate}",
                    _options.PortName, _options.BaudRate);
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
            _logger?.LogError(ex, "Modbus RTU 连接异常");
            return false;
        }
    }

    private void ValidateOptions(ModbusRtuOptions options)
    {
        if (string.IsNullOrEmpty(options.PortName))
            throw new ArgumentException("串口名称不能为空");

        if (options.BaudRate <= 0)
            throw new ArgumentException("波特率必须大于0");

        var validBaudRates = new[] { 1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200, 230400, 460800, 921600 };
        if (!validBaudRates.Contains(options.BaudRate))
            throw new ArgumentException($"不支持的波特率: {options.BaudRate}");

        if (options.DataBits < 5 || options.DataBits > 8)
            throw new ArgumentException($"不支持的数据位: {options.DataBits}");
    }

    private static StopBits ConvertStopBits(string stopBits)
    {
        return stopBits?.ToUpperInvariant() switch
        {
            "1" => StopBits.One,
            "1.5" => StopBits.OnePointFive,
            "2" => StopBits.Two,
            _ => StopBits.One
        };
    }

    private static Parity ConvertParity(string parity)
    {
        return parity?.ToUpperInvariant() switch
        {
            "NONE" or "N" => Parity.None,
            "ODD" or "O" => Parity.Odd,
            "EVEN" or "E" => Parity.Even,
            "MARK" or "M" => Parity.Mark,
            "SPACE" or "S" => Parity.Space,
            _ => Parity.None
        };
    }

    public async Task DisconnectAsync()
    {
        await _lock.WaitAsync();
        try
        {
            await StopDataCollectionAsync();

            if (_serialPort != null && _serialPort.IsOpen)
            {
                _serialPort.Close();
            }

            _serialPort?.Dispose();
            _serialPort = null;

            _logger?.LogInformation("Modbus RTU 适配器已断开连接");
            ConnectionStateChanged?.Invoke(this, false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Modbus RTU 断开连接异常");
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
            throw new InvalidOperationException("Modbus RTU 串口未连接");
        }

        if (_isCollecting)
        {
            return;
        }

        _dataCollectionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _isCollecting = true;

        // 启动后台轮询任务
        _ = Task.Run(async () => await PollDataAsync(_dataCollectionCts.Token), _dataCollectionCts.Token);

        _logger?.LogInformation("Modbus RTU 数据采集已启动");
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
                _logger?.LogError(ex, "Modbus RTU 数据轮询异常");
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
            // 构建 Modbus RTU 请求帧
            var request = BuildReadHoldingRegistersRequest(slaveId, startAddress, count);

            // 发送请求
            _serialPort!.Write(request, 0, request.Length);

            // 等待响应（帧分隔：3.5 字符时间）
            await Task.Delay(CalculateFrameDelay(request.Length + 5), cancellationToken);

            // 读取响应
            var response = new byte[512];
            var bytesRead = 0;
            var startTime = DateTime.Now;

            while (bytesRead < response.Length)
            {
                if (_serialPort.BytesToRead > 0)
                {
                    var available = _serialPort.BytesToRead;
                    var toRead = Math.Min(available, response.Length - bytesRead);
                    bytesRead += _serialPort.Read(response, bytesRead, toRead);
                }
                else
                {
                    // 检查是否超时
                    if ((DateTime.Now - startTime).TotalMilliseconds > _options?.TimeoutMs)
                    {
                        throw new TimeoutException("Modbus RTU 读取超时");
                    }
                    await Task.Delay(5, cancellationToken);
                }
            }

            // 验证 CRC
            if (!VerifyCrc(response, bytesRead))
            {
                throw new InvalidOperationException("Modbus RTU CRC 校验失败");
            }

            // 解析响应
            var byteCount = response[2];
            var values = new ushort[byteCount / 2];

            for (int i = 0; i < values.Length; i++)
            {
                values[i] = (ushort)((response[3 + i * 2] << 8) | response[4 + i * 2]);
            }

            return values;
        }
        finally
        {
            _lock.Release();
        }
    }

    private int CalculateFrameDelay(int byteCount)
    {
        if (_options == null) return 100;

        // 计算传输一个字符所需时间（毫秒）
        var bitsPerChar = 1 + _options.DataBits + (_options.StopBits == "1.5" ? 1.5 : _options.StopBits == "2" ? 2 : 1) + 1; // start + data + stop + parity
        var msPerChar = (bitsPerChar * 1000.0) / _options.BaudRate;

        // 3.5 字符时间 + 发送时间
        return Math.Max(1, (int)(msPerChar * (byteCount + 3.5)));
    }

    private byte[] BuildReadHoldingRegistersRequest(byte slaveId, ushort startAddress, ushort count)
    {
        var request = new byte[8];

        // Slave ID
        request[0] = slaveId;

        // Function code (0x03 = Read Holding Registers)
        request[1] = 0x03;

        // Start Address (2 bytes, big-endian)
        request[2] = (byte)(startAddress >> 8);
        request[3] = (byte)startAddress;

        // Quantity of Registers (2 bytes, big-endian)
        request[4] = (byte)(count >> 8);
        request[5] = (byte)count;

        // CRC (2 bytes, little-endian)
        var crc = CalculateCrc(request, 6);
        request[6] = (byte)crc;
        request[7] = (byte)(crc >> 8);

        return request;
    }

    private ushort CalculateCrc(byte[] data, int length)
    {
        ushort crc = 0xFFFF;

        for (int i = 0; i < length; i++)
        {
            crc ^= data[i];

            for (int j = 0; j < 8; j++)
            {
                if ((crc & 0x0001) != 0)
                {
                    crc >>= 1;
                    crc ^= 0xA001;
                }
                else
                {
                    crc >>= 1;
                }
            }
        }

        return crc;
    }

    private bool VerifyCrc(byte[] data, int length)
    {
        if (length < 5) return false;

        var receivedCrc = (ushort)(data[length - 1] | (data[length - 2] << 8));
        var calculatedCrc = CalculateCrc(data, length - 2);

        return receivedCrc == calculatedCrc;
    }

    public Task StopDataCollectionAsync()
    {
        _isCollecting = false;
        _dataCollectionCts?.Cancel();
        _dataCollectionCts?.Dispose();
        _dataCollectionCts = null;

        _logger?.LogInformation("Modbus RTU 数据采集已停止");
        return Task.CompletedTask;
    }

    public async Task<string> SendCommandAsync(long deviceId, string serialNumber, string commandType, string parameters, CancellationToken cancellationToken = default)
    {
        if (!IsConnected || _options == null)
        {
            throw new InvalidOperationException("Modbus RTU 串口未连接");
        }

        var commandId = Guid.NewGuid().ToString("N");
        var device = _options.Devices.FirstOrDefault(d => d.SerialNumber == serialNumber);

        if (device == null)
        {
            throw new ArgumentException($"未找到设备: {serialNumber}");
        }

        var commandParams = JsonSerializer.Deserialize<Dictionary<string, object>>(parameters) ?? new Dictionary<string, object>();

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

        _logger?.LogInformation("Modbus RTU 指令已发送: CommandId={CommandId}, Device={SerialNumber}, Type={Type}",
            commandId, serialNumber, commandType);

        return commandId;
    }

    private async Task WriteSingleRegisterAsync(byte slaveId, ushort address, ushort value, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var request = BuildWriteSingleRegisterRequest(slaveId, address, value);

            _serialPort!.Write(request, 0, request.Length);

            await Task.Delay(CalculateFrameDelay(request.Length + 8), cancellationToken);

            var response = new byte[8];
            var bytesRead = _serialPort.Read(response, 0, 8);

            if (bytesRead < 8)
            {
                throw new InvalidOperationException("Modbus RTU 写寄存器响应不完整");
            }

            if (!VerifyCrc(response, bytesRead))
            {
                throw new InvalidOperationException("Modbus RTU CRC 校验失败");
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private byte[] BuildWriteSingleRegisterRequest(byte slaveId, ushort address, ushort value)
    {
        var request = new byte[8];

        request[0] = slaveId;
        request[1] = 0x06; // Write Single Register
        request[2] = (byte)(address >> 8);
        request[3] = (byte)address;
        request[4] = (byte)(value >> 8);
        request[5] = (byte)value;

        var crc = CalculateCrc(request, 6);
        request[6] = (byte)crc;
        request[7] = (byte)(crc >> 8);

        return request;
    }

    public async Task ReadDataPointsAsync(long deviceId, string serialNumber, IEnumerable<string> dataPoints, CancellationToken cancellationToken = default)
    {
        if (!IsConnected || _options == null)
        {
            throw new InvalidOperationException("Modbus RTU 串口未连接");
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
            if (_serialPort != null && _serialPort.IsOpen)
            {
                _serialPort.Close();
            }
            _serialPort?.Dispose();
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
/// Modbus RTU 协议配置选项
/// </summary>
public class ModbusRtuOptions
{
    /// <summary>
    /// 串口名称，如 COM1, /dev/ttyUSB0
    /// </summary>
    public string PortName { get; set; } = "COM1";

    /// <summary>
    /// 波特率，默认 9600
    /// </summary>
    public int BaudRate { get; set; } = 9600;

    /// <summary>
    /// 数据位，默认 8
    /// </summary>
    public int DataBits { get; set; } = 8;

    /// <summary>
    /// 停止位：1, 1.5, 2
    /// </summary>
    public string StopBits { get; set; } = "1";

    /// <summary>
    /// 校验位：None, Odd, Even, Mark, Space
    /// </summary>
    public string Parity { get; set; } = "None";

    /// <summary>
    /// 超时时间（毫秒）
    /// </summary>
    public int TimeoutMs { get; set; } = 3000;

    /// <summary>
    /// 轮询间隔（毫秒）
    /// </summary>
    public int PollIntervalMs { get; set; } = 5000;

    /// <summary>
    /// 设备列表
    /// </summary>
    public List<ModbusDeviceConfig> Devices { get; set; } = new();

    /// <summary>
    /// 应用代码
    /// </summary>
    public string? AppCode { get; set; }
}
