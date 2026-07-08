using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using IoTPlatform.Infrastructure.Protocol.Adapters;

namespace IoTPlatform.Infrastructure.Protocol;

/// <summary>
/// 协议适配器工厂
/// 负责创建和管理协议适配器实例
/// </summary>
public interface IProtocolAdapterFactory
{
    /// <summary>
    /// 创建指定类型的协议适配器
    /// </summary>
    /// <param name="protocolType">协议类型</param>
    /// <param name="configId">配置ID</param>
    /// <returns>协议适配器实例</returns>
    IProtocolAdapter CreateAdapter(string protocolType, int configId);

    /// <summary>
    /// 获取已存在的适配器
    /// </summary>
    /// <param name="configId">配置ID</param>
    /// <returns>适配器实例，如果不存在则返回null</returns>
    IProtocolAdapter? GetAdapter(int configId);

    /// <summary>
    /// 释放指定配置的适配器
    /// </summary>
    /// <param name="configId">配置ID</param>
    void ReleaseAdapter(int configId);

    /// <summary>
    /// 释放所有适配器
    /// </summary>
    void ReleaseAll();
}

/// <summary>
/// 协议适配器工厂实现
/// </summary>
public class ProtocolAdapterFactory : IProtocolAdapterFactory
{
    private readonly ConcurrentDictionary<int, IProtocolAdapter> _adapters = new();
    private readonly ILoggerFactory? _loggerFactory;

    public ProtocolAdapterFactory(ILoggerFactory? loggerFactory = null)
    {
        _loggerFactory = loggerFactory;
    }

    /// <inheritdoc />
    public IProtocolAdapter CreateAdapter(string protocolType, int configId)
    {
        // 如果已存在，直接返回
        if (_adapters.TryGetValue(configId, out var existing))
        {
            return existing;
        }

        // 根据协议类型创建新的适配器
        var adapter = protocolType.ToUpperInvariant() switch
        {
            "MQTT" => CreateMqttAdapter(configId),
            "ANSHENG_MQTT" => CreateAnShengMqttAdapter(configId),
            "MODBUSTCP" or "MODBUS_TCP" => CreateModbusTcpAdapter(configId),
            "MODBUSRTU" or "MODBUS_RTU" => CreateModbusRtuAdapter(configId),
            "OPCUA" or "OPC_UA" => CreateOpcUaAdapter(configId),
            "HTTP" or "HTTP_POLL" => CreateHttpAdapter(configId),
            _ => throw new NotSupportedException($"不支持的协议类型: {protocolType}")
        };

        // 注册到缓存
        if (_adapters.TryAdd(configId, adapter))
        {
            _loggerFactory?.CreateLogger<ProtocolAdapterFactory>()
                .LogInformation("已创建协议适配器: Type={Type}, ConfigId={ConfigId}", protocolType, configId);
        }

        return adapter;
    }

    /// <inheritdoc />
    public IProtocolAdapter? GetAdapter(int configId)
    {
        _adapters.TryGetValue(configId, out var adapter);
        return adapter;
    }

    /// <inheritdoc />
    public void ReleaseAdapter(int configId)
    {
        if (_adapters.TryRemove(configId, out var adapter))
        {
            adapter.DisconnectAsync().Wait(TimeSpan.FromSeconds(5));
            adapter.Dispose();
            _loggerFactory?.CreateLogger<ProtocolAdapterFactory>()
                .LogInformation("已释放协议适配器: ConfigId={ConfigId}", configId);
        }
    }

    /// <inheritdoc />
    public void ReleaseAll()
    {
        foreach (var configId in _adapters.Keys.ToList())
        {
            ReleaseAdapter(configId);
        }
    }

    private IProtocolAdapter CreateMqttAdapter(int configId)
    {
        var logger = _loggerFactory?.CreateLogger<Infrastructure.Protocol.Adapters.MqttProtocolAdapter>();
        return new Infrastructure.Protocol.Adapters.MqttProtocolAdapter(configId, logger);
    }

    private IProtocolAdapter CreateModbusTcpAdapter(int configId)
    {
        var logger = _loggerFactory?.CreateLogger<Infrastructure.Protocol.Adapters.ModbusTcpAdapter>();
        return new Infrastructure.Protocol.Adapters.ModbusTcpAdapter(configId, logger);
    }

    private IProtocolAdapter CreateModbusRtuAdapter(int configId)
    {
        var logger = _loggerFactory?.CreateLogger<Infrastructure.Protocol.Adapters.ModbusRtuAdapter>();
        return new Infrastructure.Protocol.Adapters.ModbusRtuAdapter(configId, logger);
    }

    private IProtocolAdapter CreateOpcUaAdapter(int configId)
    {
        var logger = _loggerFactory?.CreateLogger<Infrastructure.Protocol.Adapters.OpcUaAdapter>();
        return new Infrastructure.Protocol.Adapters.OpcUaAdapter(configId, logger);
    }

    private IProtocolAdapter CreateHttpAdapter(int configId)
    {
        // HTTP 轮询适配器 - 暂未实现
        throw new NotImplementedException("HTTP 适配器暂未实现");
    }

    private IProtocolAdapter CreateAnShengMqttAdapter(int configId)
    {
        var logger = _loggerFactory?.CreateLogger<Adapters.AnShengMqttProtocolAdapter>();
        return new Adapters.AnShengMqttProtocolAdapter(configId, logger);
    }
}
