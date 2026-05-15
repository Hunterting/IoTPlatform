namespace IoTPlatform.Infrastructure.Protocol.Adapters;

/// <summary>
/// 设备数据接收事件参数
/// </summary>
public class DeviceDataReceivedEventArgs : EventArgs
{
    /// <summary>
    /// 设备ID
    /// </summary>
    public long DeviceId { get; set; }

    /// <summary>
    /// 设备序列号
    /// </summary>
    public string SerialNumber { get; set; } = string.Empty;

    /// <summary>
    /// 应用代码
    /// </summary>
    public string AppCode { get; set; } = string.Empty;

    /// <summary>
    /// 数据内容（JSON格式）
    /// </summary>
    public string Data { get; set; } = string.Empty;

    /// <summary>
    /// 原始数据
    /// </summary>
    public byte[]? RawData { get; set; }

    /// <summary>
    /// 接收时间
    /// </summary>
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 协议类型
    /// </summary>
    public string ProtocolType { get; set; } = string.Empty;
}

/// <summary>
/// 设备指令响应事件参数
/// </summary>
public class DeviceCommandResponseEventArgs : EventArgs
{
    /// <summary>
    /// 设备ID
    /// </summary>
    public long DeviceId { get; set; }

    /// <summary>
    /// 命令ID
    /// </summary>
    public string CommandId { get; set; } = string.Empty;

    /// <summary>
    /// 命令状态
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// 响应数据
    /// </summary>
    public string? ResponseData { get; set; }

    /// <summary>
    /// 响应时间
    /// </summary>
    public DateTime RespondedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 协议类型枚举
/// </summary>
public enum ProtocolType
{
    /// <summary>
    /// MQTT协议
    /// </summary>
    MQTT,

    /// <summary>
    /// Modbus RTU 协议
    /// </summary>
    ModbusRTU,

    /// <summary>
    /// Modbus TCP 协议
    /// </summary>
    ModbusTCP,

    /// <summary>
    /// OPC UA 协议
    /// </summary>
    OpcUA,

    /// <summary>
    /// HTTP 轮询
    /// </summary>
    Http,

    /// <summary>
    /// CoAP 协议
    /// </summary>
    CoAP,

    /// <summary>
    /// 自定义协议
    /// </summary>
    Custom
}

/// <summary>
/// 协议适配器接口
/// 定义所有协议适配器必须实现的方法
/// </summary>
public interface IProtocolAdapter : IDisposable
{
    /// <summary>
    /// 协议类型
    /// </summary>
    string ProtocolType { get; }

    /// <summary>
    /// 是否已连接
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// 协议配置ID
    /// </summary>
    int ConfigId { get; }

    /// <summary>
    /// 连接到协议服务器
    /// </summary>
    /// <param name="connectionString">连接字符串（JSON格式）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否连接成功</returns>
    Task<bool> ConnectAsync(string connectionString, CancellationToken cancellationToken = default);

    /// <summary>
    /// 断开连接
    /// </summary>
    Task DisconnectAsync();

    /// <summary>
    /// 开始数据采集
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    Task StartDataCollectionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 停止数据采集
    /// </summary>
    Task StopDataCollectionAsync();

    /// <summary>
    /// 发送控制指令到设备
    /// </summary>
    /// <param name="deviceId">设备ID</param>
    /// <param name="serialNumber">设备序列号</param>
    /// <param name="commandType">命令类型</param>
    /// <param name="parameters">命令参数（JSON格式）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>命令ID</returns>
    Task<string> SendCommandAsync(long deviceId, string serialNumber, string commandType, string parameters, CancellationToken cancellationToken = default);

    /// <summary>
    /// 从设备读取数据（主动查询模式）
    /// </summary>
    /// <param name="deviceId">设备ID</param>
    /// <param name="serialNumber">设备序列号</param>
    /// <param name="dataPoints">数据点列表</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task ReadDataPointsAsync(long deviceId, string serialNumber, IEnumerable<string> dataPoints, CancellationToken cancellationToken = default);

    /// <summary>
    /// 设备数据接收事件
    /// </summary>
    event EventHandler<DeviceDataReceivedEventArgs>? DataReceived;

    /// <summary>
    /// 指令响应事件
    /// </summary>
    event EventHandler<DeviceCommandResponseEventArgs>? CommandResponse;

    /// <summary>
    /// 连接状态变更事件
    /// </summary>
    event EventHandler<bool>? ConnectionStateChanged;
}
