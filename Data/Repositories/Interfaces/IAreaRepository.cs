using IoTPlatform.Models;

namespace IoTPlatform.Data.Repositories.Interfaces;

/// <summary>
/// 区域仓储接口
/// </summary>
public interface IAreaRepository : IRepository<Area>
{
    /// <summary>
    /// 获取区域树
    /// </summary>
    /// <param name="customerId">客户ID</param>
    /// <param name="appCode">应用代码</param>
    /// <returns>区域树列表</returns>
    Task<IEnumerable<Area>> GetTreeAsync(long? customerId = null, string? appCode = null);
    
    /// <summary>
    /// 获取子区域列表
    /// </summary>
    /// <param name="parentId">父区域ID</param>
    /// <param name="appCode">应用代码</param>
    /// <returns>子区域列表</returns>
    Task<IEnumerable<Area>> GetChildrenAsync(long parentId, string? appCode = null);
    
    /// <summary>
    /// 获取区域路径（从根节点到当前节点）
    /// </summary>
    /// <param name="areaId">区域ID</param>
    /// <param name="appCode">应用代码</param>
    /// <returns>区域路径列表</returns>
    Task<IEnumerable<Area>> GetPathAsync(long areaId, string? appCode = null);
    
    /// <summary>
    /// 根据区域类型获取区域列表
    /// </summary>
    /// <param name="type">区域类型</param>
    /// <param name="appCode">应用代码</param>
    /// <returns>区域列表</returns>
    Task<IEnumerable<Area>> GetByTypeAsync(string type, string? appCode = null);
    
    /// <summary>
    /// 更新区域设备数量
    /// </summary>
    /// <param name="areaId">区域ID</param>
    /// <param name="increment">增量（正数增加，负数减少）</param>
    Task UpdateDeviceCountAsync(long areaId, int increment = 1);
    
    /// <summary>
    /// 检查区域名称是否已存在（在同一父级下）
    /// </summary>
    /// <param name="name">区域名称</param>
    /// <param name="parentId">父区域ID</param>
    /// <param name="customerId">客户ID</param>
    /// <param name="appCode">应用代码</param>
    /// <param name="excludeAreaId">排除的区域ID（用于更新操作）</param>
    /// <returns>是否已存在</returns>
    Task<bool> NameExistsAsync(string name, long? parentId, long? customerId, string? appCode = null, long? excludeAreaId = null);
    
    /// <summary>
    /// 获取区域设备列表
    /// </summary>
    /// <param name="areaId">区域ID</param>
    /// <returns>设备列表</returns>
    Task<IEnumerable<Device>> GetDevicesAsync(long areaId);
    
    /// <summary>
    /// 获取区域档案列表
    /// </summary>
    /// <param name="areaId">区域ID</param>
    /// <returns>档案列表</returns>
    Task<IEnumerable<Archive>> GetArchivesAsync(long areaId);
}
