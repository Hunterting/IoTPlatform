using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Helpers;

namespace IoTPlatform.Services;

/// <summary>
/// 网关服务接口
/// </summary>
public interface IGatewayService
{
    /// <summary>
    /// 获取网关列表
    /// </summary>
    Task<PagedResponse<GatewayDto>> GetGatewaysAsync(int page, int pageSize, string? keyword, string? appCode);

    /// <summary>
    /// 获取网关详情
    /// </summary>
    Task<GatewayDto?> GetGatewayAsync(long id, string? appCode);

    /// <summary>
    /// 创建网关
    /// </summary>
    Task<GatewayDto> CreateGatewayAsync(CreateGatewayRequest request);

    /// <summary>
    /// 更新网关
    /// </summary>
    Task<GatewayDto> UpdateGatewayAsync(long id, UpdateGatewayRequest request, string? appCode);

    /// <summary>
    /// 删除网关
    /// </summary>
    Task DeleteGatewayAsync(long id, string? appCode);

    /// <summary>
    /// 启动网关
    /// </summary>
    Task StartGatewayAsync(long id, string? appCode);

    /// <summary>
    /// 停止网关
    /// </summary>
    Task StopGatewayAsync(long id, string? appCode);
}
