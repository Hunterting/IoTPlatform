using System.ComponentModel.DataAnnotations;

namespace IoTPlatform.DTOs.Requests;

/// <summary>
/// 发送设备指令请求
/// </summary>
public class SendDeviceCommandRequest
{
    /// <summary>
    /// 设备ID
    /// </summary>
    [Required]
    public long DeviceId { get; set; }

    /// <summary>
    /// 命令类型
    /// </summary>
    [Required, MaxLength(50)]
    public string CommandType { get; set; } = string.Empty;

    /// <summary>
    /// 命令参数（JSON格式）
    /// </summary>
    public Dictionary<string, object>? Parameters { get; set; }

    /// <summary>
    /// 超时时间（秒）
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// 最大重试次数
    /// </summary>
    public int MaxRetries { get; set; } = 3;
}

/// <summary>
/// 批量发送设备指令请求
/// </summary>
public class SendBatchDeviceCommandRequest
{
    /// <summary>
    /// 设备ID列表
    /// </summary>
    [Required]
    public List<long> DeviceIds { get; set; } = new();

    /// <summary>
    /// 命令类型
    /// </summary>
    [Required, MaxLength(50)]
    public string CommandType { get; set; } = string.Empty;

    /// <summary>
    /// 命令参数（JSON格式）
    /// </summary>
    public Dictionary<string, object>? Parameters { get; set; }

    /// <summary>
    /// 超时时间（秒）
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// 最大重试次数
    /// </summary>
    public int MaxRetries { get; set; } = 3;
}
