namespace IoTPlatform.DTOs.Responses;

/// <summary>
/// 操作日志响应DTO
/// </summary>
public class OperationLogDto
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string? UserName { get; set; }
    public string? Role { get; set; }
    public DateTime Time { get; set; }
    public string? Module { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? Target { get; set; }
    public string? Detail { get; set; }
    public string? IP { get; set; }
    public string Status { get; set; } = string.Empty;
    public int? Duration { get; set; }
    public string? AppCode { get; set; }
}

/// <summary>
/// 登录日志响应DTO
/// </summary>
public class LoginLogDto
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string? UserName { get; set; }
    public string? Role { get; set; }
    public DateTime LoginTime { get; set; }
    public string? IP { get; set; }
    public string? UserAgent { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? FailReason { get; set; }
    public string? AppCode { get; set; }
}
