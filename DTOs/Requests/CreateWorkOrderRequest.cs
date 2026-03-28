using System.ComponentModel.DataAnnotations;

namespace IoTPlatform.DTOs.Requests;

/// <summary>
/// 创建工单请求
/// </summary>
public class CreateWorkOrderRequest
{
    [Required(ErrorMessage = "标题不能为空")]
    [MaxLength(200, ErrorMessage = "标题长度不能超过200字符")]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "工单类型不能为空")]
    [MaxLength(50, ErrorMessage = "工单类型长度不能超过50字符")]
    public string Type { get; set; } = "maintenance";

    [Required(ErrorMessage = "优先级不能为空")]
    [MaxLength(20, ErrorMessage = "优先级长度不能超过20字符")]
    public string Priority { get; set; } = "medium";

    public long? CustomerId { get; set; }

    public long? DeviceId { get; set; }

    [MaxLength(200)] public string? DeviceName { get; set; }

    [MaxLength(50)] public string? DeviceCode { get; set; }

    public long? AreaId { get; set; }

    [MaxLength(200)] public string? AreaName { get; set; }

    [MaxLength(2000, ErrorMessage = "描述长度不能超过2000字符")]
    public string? Description { get; set; }

    [MaxLength(100, ErrorMessage = "上报人长度不能超过100字符")]
    public string? Reporter { get; set; }

    public DateTime? EstimatedTime { get; set; }

    [MaxLength(100, ErrorMessage = "项目名称长度不能超过100字符")]
    public string? ProjectName { get; set; }
}
