using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Helpers;

namespace IoTPlatform.Services;

/// <summary>
/// 用户服务接口
/// </summary>
public interface IUserService
{
    /// <summary>旧签名保留：内部调用/下级路径零回归。</summary>
    Task<PagedResponse<UserDto>> GetUsersAsync(int page, int pageSize, string? keyword, string? appCode, string? currentUserRole);

    /// <summary>
    /// B4/M5 列表范围下推重载：customerIds 为 Multi 授权客户集合（DB 权威），appCodes 为其反查值。
    /// 二者皆空时等价于旧签名（超管全量）。
    /// </summary>
    Task<PagedResponse<UserDto>> GetUsersAsync(int page, int pageSize, string? keyword, string? appCode, string? currentUserRole, IReadOnlyCollection<long>? customerIds, IReadOnlyCollection<string>? appCodes = null) => GetUsersAsync(page, pageSize, keyword, appCode, currentUserRole);

    Task<UserDto?> GetUserAsync(long id, string? appCode, string? currentUserRole);
    Task<UserDto> CreateUserAsync(CreateUserRequest request);

    /// <summary>
    /// 【多客户授权 T04】带 actingUser 与授权校验结果的建户重载（原签名保留，内部/历史调用零改动）。
    /// </summary>
    /// <param name="request">建户请求。</param>
    /// <param name="actingUserId">发起人用户 ID（M10 审计预留）。</param>
    /// <param name="customerScopeVerified">目标客户是否已通过 ICustomerScopeService 校验；false 时直接拒绝入库。</param>
    Task<UserDto> CreateUserAsync(CreateUserRequest request, long? actingUserId, bool customerScopeVerified);
    Task<UserDto> UpdateUserAsync(long id, UpdateUserRequest request, string? appCode, string? currentUserRole);

    /// <summary>B5/M8：Controller 显式解析范围后下推的三分支改删重载；未校验通过 fail-closed。</summary>
    Task<UserDto> UpdateUserAsync(long id, UpdateUserRequest request, string? appCode, CustomerScope scope, bool customerScopeVerified);
    Task DeleteUserAsync(long id, string? appCode, string? currentUserRole);

    /// <summary>B5/M8：Controller 显式解析范围后下推的三分支删除重载；未校验通过 fail-closed。</summary>
    Task DeleteUserAsync(long id, string? appCode, CustomerScope scope, bool customerScopeVerified);
    Task ChangePasswordAsync(long id, string oldPassword, string newPassword, string? appCode, string? currentUserRole);
}
