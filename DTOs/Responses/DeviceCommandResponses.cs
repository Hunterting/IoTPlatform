using IoTPlatform.Models;

namespace IoTPlatform.DTOs.Responses;

/// <summary>
/// 设备指令响应
/// </summary>
public class DeviceCommandResponse
{
    /// <summary>
    /// 是否成功
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 错误信息
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 指令ID
    /// </summary>
    public string? CommandId { get; set; }

    /// <summary>
    /// 指令状态
    /// </summary>
    public string? Status { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime? CreatedAt { get; set; }
}

/// <summary>
/// 设备指令DTO
/// </summary>
public class DeviceCommandDto
{
    public long Id { get; set; }
    public string CommandId { get; set; } = string.Empty;
    public string? AppCode { get; set; }
    public long DeviceId { get; set; }
    public string? SerialNumber { get; set; }
    public string? DeviceName { get; set; }
    public string CommandType { get; set; } = string.Empty;
    public Dictionary<string, object>? Parameters { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Result { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int RetryCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public long? CreatedBy { get; set; }
    public string? CreatedByName { get; set; }
}

/// <summary>
/// 指令历史DTO
/// </summary>
public class CommandHistoryDto
{
    public long Id { get; set; }
    public long CommandId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? FromStatus { get; set; }
    public string? ToStatus { get; set; }
    public string? Description { get; set; }
    public string? Data { get; set; }
    public long? OperatorId { get; set; }
    public string? OperatorName { get; set; }
    public DateTime CreatedAt { get; set; }
}
