using IoTPlatform.Models;

namespace IoTPlatform.Data.Repositories.Interfaces;

/// <summary>
/// 设备仓储接口
/// </summary>
public interface IDeviceRepository : IRepository<Device>
{
    /// <summary>
    /// 根据序列号获取设备
    /// </summary>
    /// <param name="serialNumber">序列号</param>
    /// <param name="appCode">应用代码</param>
    /// <returns>设备对象或null</returns>
    Task<Device?> GetBySerialNumberAsync(string serialNumber, string? appCode = null);
    
    /// <summary>
    /// 根据区域ID获取设备列表
    /// </summary>
    /// <param name="areaId">区域ID</param>
    /// <param name="appCode">应用代码</param>
    /// <returns>设备列表</returns>
    Task<IEnumerable<Device>> GetByAreaIdAsync(long areaId, string? appCode = null);
    
    /// <summary>
    /// 根据项目ID获取设备列表
    /// </summary>
    /// <param name="projectId">项目ID</param>
    /// <param name="appCode">应用代码</param>
    /// <returns>设备列表</returns>
    Task<IEnumerable<Device>> GetByProjectIdAsync(long projectId, string? appCode = null);
    
    /// <summary>
    /// 根据设备状态获取设备列表
    /// </summary>
    /// <param name="status">设备状态</param>
    /// <param name="appCode">应用代码</param>
    /// <returns>设备列表</returns>
    Task<IEnumerable<Device>> GetByStatusAsync(string status, string? appCode = null);
    
    /// <summary>
    /// 检查序列号是否已存在
    /// </summary>
    /// <param name="serialNumber">序列号</param>
    /// <param name="appCode">应用代码</param>
    /// <param name="excludeDeviceId">排除的设备ID（用于更新操作）</param>
    /// <returns>是否已存在</returns>
    Task<bool> SerialNumberExistsAsync(string serialNumber, string? appCode = null, long? excludeDeviceId = null);
    
    /// <summary>
    /// 更新设备状态
    /// </summary>
    /// <param name="deviceId">设备ID</param>
    /// <param name="status">新状态</param>
    Task UpdateStatusAsync(long deviceId, string status);
    
    /// <summary>
    /// 更新设备最后维护时间
    /// </summary>
    /// <param name="deviceId">设备ID</param>
    Task UpdateLastMaintenanceAsync(long deviceId);
    
    /// <summary>
    /// 获取设备详情（包含关联的区域、项目和传感器信息）
    /// </summary>
    /// <param name="id">设备ID</param>
    /// <param name="appCode">应用代码</param>
    /// <returns>设备详情或null</returns>
    Task<Device?> GetDeviceDetailsAsync(long id, string? appCode = null);
    
    /// <summary>
    /// 获取设备传感器列表
    /// </summary>
    /// <param name="deviceId">设备ID</param>
    /// <returns>传感器列表</returns>
    Task<IEnumerable<DeviceSensor>> GetSensorsAsync(long deviceId);
    
    /// <summary>
    /// 获取设备数据记录
    /// </summary>
    /// <param name="deviceId">设备ID</param>
    /// <param name="startTime">开始时间</param>
    /// <param name="endTime">结束时间</param>
    /// <param name="limit">限制数量</param>
    /// <returns>数据记录列表</returns>
    Task<IEnumerable<DeviceDataRecord>> GetDataRecordsAsync(long deviceId, DateTime? startTime = null, DateTime? endTime = null, int? limit = 100);
    
    /// <summary>
    /// 获取设备统计信息
    /// </summary>
    /// <param name="deviceId">设备ID</param>
    /// <returns>统计信息</returns>
    Task<(int SensorCount, int DataRecordCount, int AlertCount)> GetDeviceStatsAsync(long deviceId);
}
