using System.ComponentModel.DataAnnotations;

namespace IoTPlatform.DTOs.Requests;

/// <summary>
/// 更新区域请求
/// </summary>
public class UpdateAreaRequest
{
    [Required(ErrorMessage = "区域名称不能为空")]
    [MaxLength(200, ErrorMessage = "区域名称长度不能超过200字符")]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500, ErrorMessage = "图片URL长度不能超过500字符")]
    public string? Image { get; set; }

    [MaxLength(500, ErrorMessage = "描述长度不能超过500字符")]
    public string? Description { get; set; }

    public int SortOrder { get; set; }
}
