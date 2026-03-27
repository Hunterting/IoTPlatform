using System.ComponentModel.DataAnnotations;

namespace IoTPlatform.DTOs.Requests;

/// <summary>
/// 更新角色请求
/// </summary>
public class UpdateRoleRequest
{
    [Required(ErrorMessage = "角色名称不能为空")]
    [MaxLength(100, ErrorMessage = "角色名称长度不能超过100字符")]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500, ErrorMessage = "描述长度不能超过500字符")]
    public string? Description { get; set; }

    public List<string>? Permissions { get; set; }

    [Required(ErrorMessage = "数据范围不能为空")]
    [MaxLength(10, ErrorMessage = "数据范围长度不能超过10字符")]
    public string DataScope { get; set; } = "CUSTOM";
}
