using IoTPlatform.Configuration;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Filters;
using IoTPlatform.Helpers;
using IoTPlatform.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace IoTPlatform.Controllers;

/// <summary>
/// 用户管理控制器
/// </summary>
[ApiController]
[Route("api/v1/users")]
[PermissionAuthorize(Permissions.VIEW_USERS)]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;

    public UsersController(IUserService userService)
    {
        _userService = userService;
    }

    /// <summary>
    /// 获取用户列表
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResponse<UserDto>>>> GetUsers(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? keyword = null)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var role = User.FindFirst(ClaimTypes.Role)?.Value;

            var result = await _userService.GetUsersAsync(page, pageSize, keyword, appCode, role);
            return ApiResponse<PagedResponse<UserDto>>.Success(result);
        }
        catch (Exception ex)
        {
            return ApiResponse<PagedResponse<UserDto>>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 获取用户详情
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<UserDto>>> GetUser(long id)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var role = User.FindFirst(ClaimTypes.Role)?.Value;

            var result = await _userService.GetUserAsync(id, appCode, role);
            if (result == null)
            {
                return Ok(ApiResponse<UserDto>.NotFound("用户不存在"));
            }

            return ApiResponse<UserDto>.Success(result);
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<UserDto>.Error(ex.Message));
        }
    }

    /// <summary>
    /// 创建用户
    /// </summary>
    [HttpPost]
    [PermissionAuthorize(Permissions.CREATE_USERS)]
    public async Task<ActionResult<ApiResponse<UserDto>>> CreateUser([FromBody] CreateUserRequest request)
    {
        try
        {
            var result = await _userService.CreateUserAsync(request);
            return ApiResponse<UserDto>.Success(result, "用户创建成功");
        }
        catch (InvalidOperationException ex)
        {
            return Ok(ApiResponse<UserDto>.BadRequest(ex.Message));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<UserDto>.Error(ex.Message));
        }
    }

    /// <summary>
    /// 更新用户
    /// </summary>
    [HttpPut("{id}")]
    [PermissionAuthorize(Permissions.UPDATE_USERS)]
    public async Task<ActionResult<ApiResponse<UserDto>>> UpdateUser(long id, [FromBody] UpdateUserRequest request)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var role = User.FindFirst(ClaimTypes.Role)?.Value;

            var result = await _userService.UpdateUserAsync(id, request, appCode, role);
            return ApiResponse<UserDto>.Success(result, "用户更新成功");
        }
        catch (InvalidOperationException ex)
        {
            return Ok(ApiResponse<UserDto>.BadRequest(ex.Message));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Ok(ApiResponse<UserDto>.Forbidden(ex.Message));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<UserDto>.Error(ex.Message));
        }
    }

    /// <summary>
    /// 删除用户
    /// </summary>
    [HttpDelete("{id}")]
    [PermissionAuthorize(Permissions.DELETE_USERS)]
    public async Task<ActionResult<ApiResponse>> DeleteUser(long id)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var role = User.FindFirst(ClaimTypes.Role)?.Value;

            await _userService.DeleteUserAsync(id, appCode, role);
            return ApiResponse.Success("用户删除成功");
        }
        catch (InvalidOperationException ex)
        {
            return Ok(ApiResponse.BadRequest(ex.Message));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Ok(ApiResponse.Forbidden(ex.Message));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse.Error(ex.Message));
        }
    }

    /// <summary>
    /// 修改密码
    /// </summary>
    [HttpPost("{id}/change-password")]
    [Authorize]
    public async Task<ActionResult<ApiResponse>> ChangePassword(
        long id,
        [FromBody] ChangePasswordRequest request)
    {
        try
        {
            var appCode = User.FindFirst("AppCode")?.Value;
            var role = User.FindFirst(ClaimTypes.Role)?.Value;
            var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            // 只能修改自己的密码或超级管理员可以修改
            if (role != "super_admin" && currentUserId != id.ToString())
            {
                return Ok(ApiResponse.Forbidden("无权修改该用户密码"));
            }

            await _userService.ChangePasswordAsync(id, request.OldPassword, request.NewPassword, appCode, role);
            return Ok(ApiResponse.Success("密码修改成功"));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Ok(ApiResponse.Unauthorized(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return Ok(ApiResponse.BadRequest(ex.Message));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse.Error(ex.Message));
        }
    }
}

/// <summary>
/// 修改密码请求
/// </summary>
public class ChangePasswordRequest
{
    public string OldPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}
