using System.ComponentModel.DataAnnotations;

namespace IoTPlatform.DTOs.Requests;

/// <summary>
/// 创建用户请求
/// </summary>
public class CreateUserRequest
{
    [Required(ErrorMessage = "姓名不能为空")]
    [MaxLength(100, ErrorMessage = "姓名长度不能超过100字符")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "邮箱不能为空")]
    [EmailAddress(ErrorMessage = "邮箱格式不正确")]
    [MaxLength(200, ErrorMessage = "邮箱长度不能超过200字符")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "密码不能为空")]
    [MinLength(6, ErrorMessage = "密码长度不能少于6位")]
    [MaxLength(100, ErrorMessage = "密码长度不能超过100字符")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "角色不能为空")]
    [MaxLength(50, ErrorMessage = "角色代码长度不能超过50字符")]
    public string Role { get; set; } = string.Empty;

    public long? CustomerId { get; set; }

    [MaxLength(200, ErrorMessage = "头像URL长度不能超过200字符")]
    public string? Avatar { get; set; }

    public string? AllowedAreaIds { get; set; }

    public bool IsActive { get; set; } = true;
}
