using System.ComponentModel.DataAnnotations;

namespace IoTPlatform.DTOs.Requests;

/// <summary>
/// 处理告警请求
/// </summary>
public class ProcessAlertRequest
{
    [Required(ErrorMessage = "操作类型不能为空")]
    [MaxLength(50, ErrorMessage = "操作类型长度不能超过50字符")]
    public string Action { get; set; } = string.Empty; // assign, process, resolve, ignore, remark

    [MaxLength(2000, ErrorMessage = "备注长度不能超过2000字符")]
    public string? Comment { get; set; }
}
