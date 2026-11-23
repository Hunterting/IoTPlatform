using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Helpers;
using IoTPlatform.Models;

namespace IoTPlatform.Services;

/// <summary>
/// 数据规则服务接口
/// </summary>
public interface IDataRuleService
{
    /// <summary>
    /// 获取数据规则列表
    /// </summary>
    /// <summary>旧签名保留：内部/下级路径零回归。</summary>
    Task<PagedResponse<DataRuleDto>> GetDataRulesAsync(int page, int pageSize, string? keyword, string? ruleType, string? appCode);

    /// <summary>B4/M5：Multi 授权客户集合范围下推。</summary>
    Task<PagedResponse<DataRuleDto>> GetDataRulesAsync(int page, int pageSize, string? keyword, string? ruleType, string? appCode, IReadOnlyCollection<long>? customerIds, IReadOnlyCollection<string>? appCodes = null) => GetDataRulesAsync(page, pageSize, keyword, ruleType, appCode);

    /// <summary>
    /// 获取数据规则详情
    /// </summary>
    Task<DataRuleDto?> GetDataRuleAsync(long id, string? appCode);

    /// <summary>
    /// 创建数据规则
    /// </summary>
    Task<DataRuleDto> CreateDataRuleAsync(CreateDataRuleRequest request, CustomerScope? scope = null);

    /// <summary>
    /// 更新数据规则
    /// </summary>
    Task<DataRuleDto> UpdateDataRuleAsync(long id, UpdateDataRuleRequest request, string? appCode, CustomerScope? scope = null);

    /// <summary>
    /// 删除数据规则
    /// </summary>
    Task DeleteDataRuleAsync(long id, string? appCode, CustomerScope? scope = null);

    /// <summary>
    /// 执行规则
    /// </summary>
    Task<bool> ExecuteRuleAsync(DataRule rule, DeviceDataRecord data);
}
