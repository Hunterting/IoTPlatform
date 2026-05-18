using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Helpers;

namespace IoTPlatform.Services;

/// <summary>
/// 隧道服务接口
/// </summary>
public interface ITunnelService
{
    /// <summary>
    /// 获取隧道列表
    /// </summary>
    Task<PagedResponse<TunnelDto>> GetTunnelsAsync(int page, int pageSize, string? keyword, string? appCode);

    /// <summary>
    /// 获取隧道详情
    /// </summary>
    Task<TunnelDto?> GetTunnelAsync(long id, string? appCode);

    /// <summary>
    /// 创建隧道
    /// </summary>
    Task<TunnelDto> CreateTunnelAsync(CreateTunnelRequest request);

    /// <summary>
    /// 更新隧道
    /// </summary>
    Task<TunnelDto> UpdateTunnelAsync(long id, UpdateTunnelRequest request, string? appCode);

    /// <summary>
    /// 删除隧道
    /// </summary>
    Task DeleteTunnelAsync(long id, string? appCode);

    /// <summary>
    /// 连接隧道
    /// </summary>
    Task ConnectTunnelAsync(long id, string? appCode);

    /// <summary>
    /// 断开隧道
    /// </summary>
    Task DisconnectTunnelAsync(long id, string? appCode);
}
