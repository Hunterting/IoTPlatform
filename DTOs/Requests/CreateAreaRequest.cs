using System.ComponentModel.DataAnnotations;

namespace IoTPlatform.DTOs.Requests;

/// <summary>
/// 创建区域请求
/// </summary>
public class CreateAreaRequest
{
    [Required(ErrorMessage = "区域名称不能为空")]
    [MaxLength(200, ErrorMessage = "区域名称长度不能超过200字符")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "区域类型不能为空")]
    [MaxLength(10, ErrorMessage = "区域类型长度不能超过10字符")]
    public string Type { get; set; } = "level1";

    [MaxLength(500, ErrorMessage = "图片URL长度不能超过500字符")]
    public string? Image { get; set; }

    public long? ParentId { get; set; }

    public long? CustomerId { get; set; }

    [MaxLength(500, ErrorMessage = "描述长度不能超过500字符")]
    public string? Description { get; set; }

    public int SortOrder { get; set; }
}
