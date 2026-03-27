using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Helpers;
using IoTPlatform.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace IoTPlatform.Controllers;

/// <summary>
/// 认证控制器
/// </summary>
[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    /// <summary>
    /// 用户登录
    /// </summary>
    [HttpPost("login")]
    public async Task<ActionResult<ApiResponse<LoginResponse>>> Login([FromBody] LoginRequest request)
    {
        try
        {
            var result = await _authService.LoginAsync(request);
            return ApiResponse<LoginResponse>.Success(result, "登录成功");
        }
        catch (UnauthorizedAccessException ex)
        {
            return ApiResponse<LoginResponse>.Unauthorized(ex.Message);
        }
        catch (Exception ex)
        {
            return ApiResponse<LoginResponse>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 获取当前用户信息
    /// </summary>
    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<ApiResponse<LoginResponse>>> GetCurrentUser()
    {
        try
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim == null || !long.TryParse(userIdClaim.Value, out var userId))
            {
                return ApiResponse<LoginResponse>.Unauthorized("无效的令牌");
            }

            var result = await _authService.GetCurrentUserAsync(userId);
            if (result == null)
            {
                return ApiResponse<LoginResponse>.NotFound("用户不存在");
            }

            return ApiResponse<LoginResponse>.Success(result);
        }
        catch (Exception ex)
        {
            return ApiResponse<LoginResponse>.Error(ex.Message);
        }
    }

    /// <summary>
    /// 切换客户（仅超级管理员）
    /// </summary>
    [Authorize]
    [HttpPost("switch-customer")]
    public async Task<ActionResult<ApiResponse<LoginResponse>>> SwitchCustomer([FromBody] SwitchCustomerRequest request)
    {
        try
        {
            // 验证用户角色
            var roleClaim = User.FindFirst(ClaimTypes.Role);
            if (roleClaim?.Value != "super_admin")
            {
                return ApiResponse<LoginResponse>.Forbidden("只有超级管理员可以切换客户");
            }

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim == null || !long.TryParse(userIdClaim.Value, out var userId))
            {
                return ApiResponse<LoginResponse>.Unauthorized("无效的令牌");
            }

            var result = await _authService.SwitchCustomerAsync(userId, request.CustomerId);
            if (result == null)
            {
                return ApiResponse<LoginResponse>.NotFound("客户不存在");
            }

            return ApiResponse<LoginResponse>.Success(result, "切换成功");
        }
        catch (UnauthorizedAccessException ex)
        {
            return ApiResponse<LoginResponse>.Unauthorized(ex.Message);
        }
        catch (Exception ex)
        {
            return ApiResponse<LoginResponse>.Error(ex.Message);
        }
    }
}

/// <summary>
/// 切换客户请求
/// </summary>
public class SwitchCustomerRequest
{
    public long CustomerId { get; set; }
}
