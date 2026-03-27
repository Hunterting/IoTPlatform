namespace IoTPlatform.DTOs.Responses;

/// <summary>
/// 用户响应DTO
/// </summary>
public class UserDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public long? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public string? AppCode { get; set; }
    public string? Avatar { get; set; }
    public List<long>? AllowedAreaIds { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// 用户详情响应DTO
/// </summary>
public class UserDetailDto : UserDto
{
    public DateTime? LastLoginAt { get; set; }
}
