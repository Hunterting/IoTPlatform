namespace IoTPlatform.DTOs.Responses;

/// <summary>
/// 登录响应
/// </summary>
public class LoginResponse
{
    public string Token { get; set; } = string.Empty;
    public UserDto User { get; set; } = null!;
    public CustomerDto? CurrentCustomer { get; set; }
}

/// <summary>
/// 客户DTO
/// </summary>
public class CustomerDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string AppCode { get; set; } = string.Empty;
    public string? ContactPerson { get; set; }
    public string? ContactPhone { get; set; }
    public string? ContactEmail { get; set; }
    public string? Address { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public int DeviceCount { get; set; }
}
