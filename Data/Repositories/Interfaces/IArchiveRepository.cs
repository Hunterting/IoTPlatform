using IoTPlatform.Models;

namespace IoTPlatform.Data.Repositories.Interfaces;

/// <summary>
/// 档案仓储接口
/// </summary>
public interface IArchiveRepository : IRepository<Archive>
{
    /// <summary>
    /// 根据档案类型获取档案列表
    /// </summary>
    /// <param name="type">档案类型</param>
    /// <param name="appCode">应用代码</param>
    /// <returns>档案列表</returns>
    Task<IEnumerable<Archive>> GetByTypeAsync(string type, string? appCode = null);
    
    /// <summary>
    /// 根据档案分类获取档案列表
    /// </summary>
    /// <param name="category">档案分类</param>
    /// <param name="appCode">应用代码</param>
    /// <returns>档案列表</returns>
    Task<IEnumerable<Archive>> GetByCategoryAsync(string category, string? appCode = null);
    
    /// <summary>
    /// 根据区域ID获取档案列表
    /// </summary>
    /// <param name="areaId">区域ID</param>
    /// <param name="appCode">应用代码</param>
    /// <returns>档案列表</returns>
    Task<IEnumerable<Archive>> GetByAreaIdAsync(long areaId, string? appCode = null);
    
    /// <summary>
    /// 获取3D模型档案列表
    /// </summary>
    /// <param name="appCode">应用代码</param>
    /// <returns>3D模型档案列表</returns>
    Task<IEnumerable<Archive>> Get3DModelsAsync(string? appCode = null);
    
    /// <summary>
    /// 获取档案详情（包含关联的区域和设备标记信息）
    /// </summary>
    /// <param name="id">档案ID</param>
    /// <param name="appCode">应用代码</param>
    /// <returns>档案详情或null</returns>
    Task<Archive?> GetArchiveDetailsAsync(long id, string? appCode = null);
    
    /// <summary>
    /// 获取档案设备标记
    /// </summary>
    /// <param name="archiveId">档案ID</param>
    /// <returns>设备标记列表</returns>
    Task<IEnumerable<ArchiveDeviceMarker>> GetDeviceMarkersAsync(long archiveId);

    /// <summary>
    /// 获取档案设备标记（别名方法）
    /// </summary>
    /// <param name="archiveId">档案ID</param>
    /// <returns>设备标记列表</returns>
    Task<IEnumerable<ArchiveDeviceMarker>> GetMarkersAsync(long archiveId) => GetDeviceMarkersAsync(archiveId);
    
    /// <summary>
    /// 添加档案设备标记
    /// </summary>
    /// <param name="archiveId">档案ID</param>
    /// <param name="deviceId">设备ID</param>
    /// <param name="name">标记名称</param>
    /// <param name="deviceType">设备类型</param>
    /// <param name="model">设备型号</param>
    /// <param name="x">X坐标</param>
    /// <param name="y">Y坐标</param>
    /// <param name="z">Z坐标</param>
    /// <param name="sensors">传感器信息（JSON）</param>
    Task AddDeviceMarkerAsync(long archiveId, long? deviceId, string name, string? deviceType = null, string? model = null, 
        double x = 0, double y = 0, double z = 0, string? sensors = null);
    
    /// <summary>
    /// 更新档案设备标记
    /// </summary>
    /// <param name="markerId">标记ID</param>
    /// <param name="name">标记名称</param>
    /// <param name="deviceType">设备类型</param>
    /// <param name="model">设备型号</param>
    /// <param name="x">X坐标</param>
    /// <param name="y">Y坐标</param>
    /// <param name="z">Z坐标</param>
    /// <param name="sensors">传感器信息（JSON）</param>
    Task UpdateDeviceMarkerAsync(long markerId, string? name = null, string? deviceType = null, string? model = null,
        double? x = null, double? y = null, double? z = null, string? sensors = null);
    
    /// <summary>
    /// 删除档案设备标记
    /// </summary>
    /// <param name="markerId">标记ID</param>
    Task DeleteDeviceMarkerAsync(long markerId);
    
    /// <summary>
    /// 检查档案名称是否已存在
    /// </summary>
    /// <param name="name">档案名称</param>
    /// <param name="appCode">应用代码</param>
    /// <param name="excludeArchiveId">排除的档案ID（用于更新操作）</param>
    /// <returns>是否已存在</returns>
    Task<bool> NameExistsAsync(string name, string? appCode = null, long? excludeArchiveId = null);
    
    /// <summary>
    /// 获取档案统计信息
    /// </summary>
    /// <param name="appCode">应用代码</param>
    /// <returns>档案统计</returns>
    Task<ArchiveStats> GetArchiveStatsAsync(string? appCode = null);
}

/// <summary>
/// 档案统计信息
/// </summary>
public class ArchiveStats
{
    public int TotalArchives { get; set; }
    public int FloorPlanCount { get; set; }
    public int Model3DCount { get; set; }
    public int PhotoCount { get; set; }
    public int DocumentCount { get; set; }
    public int TotalDeviceMarkers { get; set; }
    public long TotalFileSize { get; set; } // 字节
    public DateTime LastUpdate { get; set; } = DateTime.UtcNow;
}
