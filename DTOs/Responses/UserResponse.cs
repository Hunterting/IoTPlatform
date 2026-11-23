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

    /// <summary>
    /// admin 被授权的客户ID集合（多客户授权模型；下级/超管为 null）。
    /// 保留原 CustomerId / AppCode 字段供 M09 UI 阶段使用，本轮不下发 UI 改造。
    /// </summary>
    public List<long>? ManagedCustomerIds { get; set; }
    public bool IsActive { get; set; }
    public bool IsSuperAdmin { get; set; }
    public List<string> Permissions { get; set; } = new List<string>();
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
