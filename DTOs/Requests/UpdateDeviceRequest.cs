using System.ComponentModel.DataAnnotations;

namespace IoTPlatform.DTOs.Requests;

/// <summary>
/// 更新设备请求
/// </summary>
public class UpdateDeviceRequest
{
    [Required(ErrorMessage = "设备名称不能为空")]
    [MaxLength(200, ErrorMessage = "设备名称长度不能超过200字符")]
    public string Name { get; set; } = string.Empty;

    [MaxLength(100, ErrorMessage = "型号长度不能超过100字符")]
    public string? Model { get; set; }

    [MaxLength(100, ErrorMessage = "序列号长度不能超过100字符")]
    public string? SerialNumber { get; set; }

    [MaxLength(100, ErrorMessage = "类别长度不能超过100字符")]
    public string? Category { get; set; }

    [MaxLength(200, ErrorMessage = "位置长度不能超过200字符")]
    public string? Location { get; set; }

    public long? AreaId { get; set; }

    public long? ProjectId { get; set; }

    [MaxLength(200, ErrorMessage = "项目名称长度不能超过200字符")]
    public string? ProjectName { get; set; }

    public string? EnergyTypes { get; set; }

    [Required(ErrorMessage = "状态不能为空")]
    [MaxLength(20, ErrorMessage = "状态长度不能超过20字符")]
    public string Status { get; set; } = "offline";

    public DateTime? InstallDate { get; set; }

    public DateTime? LastMaintenance { get; set; }

    [MaxLength(200, ErrorMessage = "供应商长度不能超过200字符")]
    public string? Supplier { get; set; }

    public DateTime? WarrantyDate { get; set; }

    public double? Power { get; set; }

    [MaxLength(20, ErrorMessage = "电压长度不能超过20字符")]
    public string? Voltage { get; set; }

    public bool MeterInstalled { get; set; }
}
