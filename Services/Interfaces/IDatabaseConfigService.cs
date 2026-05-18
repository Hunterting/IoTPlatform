using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Helpers;

namespace IoTPlatform.Services;

/// <summary>
/// 数据库配置服务接口
/// </summary>
public interface IDatabaseConfigService
{
    /// <summary>
    /// 获取数据库配置列表
    /// </summary>
    Task<PagedResponse<DatabaseConfigDto>> GetDatabaseConfigsAsync(int page, int pageSize, string? keyword, string? databaseType, string? appCode);

    /// <summary>
    /// 获取数据库配置详情
    /// </summary>
    Task<DatabaseConfigDto?> GetDatabaseConfigAsync(long id, string? appCode);

    /// <summary>
    /// 创建数据库配置
    /// </summary>
    Task<DatabaseConfigDto> CreateDatabaseConfigAsync(CreateDatabaseConfigRequest request);

    /// <summary>
    /// 更新数据库配置
    /// </summary>
    Task<DatabaseConfigDto> UpdateDatabaseConfigAsync(long id, UpdateDatabaseConfigRequest request, string? appCode);

    /// <summary>
    /// 删除数据库配置
    /// </summary>
    Task DeleteDatabaseConfigAsync(long id, string? appCode);

    /// <summary>
    /// 测试数据库连接
    /// </summary>
    Task<bool> TestConnectionAsync(TestDatabaseConnectionRequest request);

    /// <summary>
    /// 测试已有配置的连接
    /// </summary>
    Task<bool> TestConnectionByIdAsync(long id, string? appCode);
}
