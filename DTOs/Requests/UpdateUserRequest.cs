using System.ComponentModel.DataAnnotations;

namespace IoTPlatform.DTOs.Requests;

/// <summary>
/// 更新用户请求
/// </summary>
public class UpdateUserRequest
{
    [Required(ErrorMessage = "姓名不能为空")]
    [MaxLength(100, ErrorMessage = "姓名长度不能超过100字符")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "邮箱不能为空")]
    [EmailAddress(ErrorMessage = "邮箱格式不正确")]
    [MaxLength(200, ErrorMessage = "邮箱长度不能超过200字符")]
    public string Email { get; set; } = string.Empty;

    public string? Role { get; set; }

    [MaxLength(200, ErrorMessage = "头像URL长度不能超过200字符")]
    public string? Avatar { get; set; }

    public string? AllowedAreaIds { get; set; }

    public bool IsActive { get; set; } = true;
}
