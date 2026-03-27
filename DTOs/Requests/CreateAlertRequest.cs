using System.ComponentModel.DataAnnotations;

namespace IoTPlatform.DTOs.Requests;

/// <summary>
/// 创建告警请求
/// </summary>
public class CreateAlertRequest
{
    [Required(ErrorMessage = "设备名称不能为空")]
    [MaxLength(200, ErrorMessage = "设备名称长度不能超过200字符")]
    public string DeviceName { get; set; } = string.Empty;

    [MaxLength(50, ErrorMessage = "设备代码长度不能超过50字符")]
    public string? DeviceCode { get; set; }

    [MaxLength(200, ErrorMessage = "区域长度不能超过200字符")]
    public string? Area { get; set; }

    [Required(ErrorMessage = "告警类型不能为空")]
    [MaxLength(50, ErrorMessage = "告警类型长度不能超过50字符")]
    public string AlertType { get; set; } = string.Empty;

    [Required(ErrorMessage = "告警级别不能为空")]
    [MaxLength(20, ErrorMessage = "告警级别长度不能超过20字符")]
    public string Level { get; set; } = "info";

    public double? Value { get; set; }

    public double? Threshold { get; set; }

    [MaxLength(1000, ErrorMessage = "备注长度不能超过1000字符")]
    public string? Remark { get; set; }

    public long? DeviceId { get; set; }

    public long? AreaId { get; set; }
}
