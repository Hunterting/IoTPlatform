using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;

namespace IoTPlatform.Services;

/// <summary>
/// 用户服务接口
/// </summary>
public interface IUserService
{
    Task<PagedResponse<UserDto>> GetUsersAsync(int page, int pageSize, string? keyword, string? appCode, string? currentUserRole);
    Task<UserDto?> GetUserAsync(long id, string? appCode, string? currentUserRole);
    Task<UserDto> CreateUserAsync(CreateUserRequest request);
    Task<UserDto> UpdateUserAsync(long id, UpdateUserRequest request, string? appCode, string? currentUserRole);
    Task DeleteUserAsync(long id, string? appCode, string? currentUserRole);
    Task ChangePasswordAsync(long id, string oldPassword, string newPassword, string? appCode, string? currentUserRole);
}
