using System.ComponentModel.DataAnnotations;

namespace IoTPlatform.DTOs.Requests;

/// <summary>
/// 注册设备到控制系统请求
/// </summary>
public class RegisterControlledDeviceRequest
{
    /// <summary>
    /// 设备ID
    /// </summary>
    [Required]
    public long DeviceId { get; set; }

    /// <summary>
    /// 备注信息
    /// </summary>
    [MaxLength(500)]
    public string? Remark { get; set; }

    /// <summary>
    /// 优先级（1-10）
    /// </summary>
    [Range(1, 10)]
    public int Priority { get; set; } = 5;
}

/// <summary>
/// 批量注册设备请求
/// </summary>
public class BatchRegisterControlledDeviceRequest
{
    /// <summary>
    /// 设备ID列表
    /// </summary>
    [Required]
    public List<long> DeviceIds { get; set; } = new();
}

/// <summary>
/// 更新受控设备请求
/// </summary>
public class UpdateControlledDeviceRequest
{
    /// <summary>
    /// 备注信息
    /// </summary>
    [MaxLength(500)]
    public string? Remark { get; set; }

    /// <summary>
    /// 优先级（1-10）
    /// </summary>
    [Range(1, 10)]
    public int? Priority { get; set; }

    /// <summary>
    /// 是否启用控制
    /// </summary>
    public bool? IsEnabled { get; set; }

    /// <summary>
    /// 是否收藏
    /// </summary>
    public bool? IsFavorite { get; set; }
}
