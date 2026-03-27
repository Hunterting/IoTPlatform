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
    Task<ProtocolConfigDto> CreateProtocolConfigAsync(CreateProtocolConfigRequest request);

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
    /// 停止协议
    /// </summary>
    Task StopProtocolAsync(long id, string? appCode);
}
