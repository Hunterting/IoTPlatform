using System.Text.Json.Serialization;

namespace IoTPlatform.DTOs.Responses;

/// <summary>
/// 角色响应DTO
/// </summary>
public class RoleDto
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    [JsonIgnore]
    public string? Permissions { get; set; }
    public List<string>? PermissionList { get; set; }
    public string DataScope { get; set; } = "CUSTOM";
    public string? AppCode { get; set; }
    public bool IsSystem { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
