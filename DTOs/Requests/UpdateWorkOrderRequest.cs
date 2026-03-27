using System.ComponentModel.DataAnnotations;

namespace IoTPlatform.DTOs.Requests;

/// <summary>
/// 更新工单请求
/// </summary>
public class UpdateWorkOrderRequest
{
    [Required(ErrorMessage = "标题不能为空")]
    [MaxLength(200, ErrorMessage = "标题长度不能超过200字符")]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "优先级不能为空")]
    [MaxLength(20, ErrorMessage = "优先级长度不能超过20字符")]
    public string Priority { get; set; } = "medium";

    [MaxLength(2000, ErrorMessage = "描述长度不能超过2000字符")]
    public string? Description { get; set; }

    public DateTime? EstimatedTime { get; set; }
}
