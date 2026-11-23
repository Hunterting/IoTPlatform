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
    /// <summary>旧签名保留：内部/下级路径零回归。</summary>
    Task<PagedResponse<GatewayDto>> GetGatewaysAsync(int page, int pageSize, string? keyword, string? appCode);

    /// <summary>B4/M5：Multi 授权客户集合范围下推。</summary>
    Task<PagedResponse<GatewayDto>> GetGatewaysAsync(int page, int pageSize, string? keyword, string? appCode, IReadOnlyCollection<long>? customerIds, IReadOnlyCollection<string>? appCodes = null) => GetGatewaysAsync(page, pageSize, keyword, appCode);

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
