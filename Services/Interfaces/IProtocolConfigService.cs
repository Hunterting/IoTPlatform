using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Helpers;

namespace IoTPlatform.Services;

/// <summary>
/// 协议配置服务接口
/// </summary>
public interface IProtocolConfigService
{
    /// <summary>
    /// 获取协议配置列表
    /// </summary>
    Task<PagedResponse<ProtocolConfigDto>> GetProtocolConfigsAsync(int page, int pageSize, string? keyword, string? type, string? appCode);

    /// <summary>
    /// 获取协议配置详情
    /// </summary>
    Task<ProtocolConfigDto?> GetProtocolConfigAsync(long id, string? appCode);

    /// <summary>
    /// 创建协议配置
    /// </summary>
    /// <param name="request">协议配置创建请求</param>
    /// <param name="appCode">服务端从 JWT 解析的租户码，避免前端越权伪造归属</param>
    Task<ProtocolConfigDto> CreateProtocolConfigAsync(CreateProtocolConfigRequest request, string? appCode = null);

    /// <summary>
    /// 更新协议配置
    /// </summary>
    Task<ProtocolConfigDto> UpdateProtocolConfigAsync(long id, UpdateProtocolConfigRequest request, string? appCode);

    /// <summary>
    /// 删除协议配置
    /// </summary>
    Task DeleteProtocolConfigAsync(long id, string? appCode);

    /// <summary>
    /// 启动协议
    /// </summary>
    Task StartProtocolAsync(long id, string? appCode);

    /// <summary>
    /// 后端 cold start 时自动拉起所有已激活的协议配置（供 ProtocolConfigAutoStartHostedService 调用）。
    /// 单个配置拉起失败只记录警告，不影响其它配置与宿主启动。
    /// </summary>
    Task AutoStartActiveProtocolsAsync();

    /// <summary>
    /// 停止协议
    /// </summary>
    Task StopProtocolAsync(long id, string? appCode);
}
