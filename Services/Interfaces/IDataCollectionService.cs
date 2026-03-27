using IoTPlatform.Models;

namespace IoTPlatform.Services;

/// <summary>
/// 数据采集服务接口
/// </summary>
public interface IDataCollectionService
{
    /// <summary>
    /// 处理接收到的设备数据
    /// </summary>
    Task ProcessDeviceDataAsync(long deviceId, string? appCode, string? sensorData, DateTime timestamp);
}
