using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Helpers;

namespace IoTPlatform.Services;

/// <summary>
/// 插件服务接口
/// </summary>
public interface IPluginService
{
    /// <summary>
    /// 获取插件列表
    /// </summary>
    Task<PagedResponse<PluginDto>> GetPluginsAsync(int page, int pageSize, string? keyword, string? appCode);

    /// <summary>
    /// 获取插件详情
    /// </summary>
    Task<PluginDto?> GetPluginAsync(long id, string? appCode);

    /// <summary>
    /// 创建插件
    /// </summary>
    Task<PluginDto> CreatePluginAsync(CreatePluginRequest request);

    /// <summary>
    /// 更新插件
    /// </summary>
    Task<PluginDto> UpdatePluginAsync(long id, UpdatePluginRequest request, string? appCode);

    /// <summary>
    /// 删除插件
    /// </summary>
    Task DeletePluginAsync(long id, string? appCode);

    /// <summary>
    /// 启动插件
    /// </summary>
    Task StartPluginAsync(long id, string? appCode);

    /// <summary>
    /// 停止插件
    /// </summary>
    Task StopPluginAsync(long id, string? appCode);
}
