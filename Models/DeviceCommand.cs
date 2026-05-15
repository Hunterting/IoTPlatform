using IoTPlatform.Data;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IoTPlatform.Models;

/// <summary>
/// 设备指令状态枚举
/// </summary>
public enum CommandStatus
{
    /// <summary>
    /// 待发送
    /// </summary>
    Pending = 0,

    /// <summary>
    /// 已发送
    /// </summary>
    Sent = 1,

    /// <summary>
    /// 设备已接收
    /// </summary>
    Delivered = 2,

    /// <summary>
    /// 执行成功
    /// </summary>
    Success = 3,

    /// <summary>
    /// 执行失败
    /// </summary>
    Failed = 4,

    /// <summary>
    /// 超时
    /// </summary>
    Timeout = 5,

    /// <summary>
    /// 已取消
    /// </summary>
    Cancelled = 6
}

/// <summary>
/// 设备指令
/// 用于存储从平台下发给设备的控制指令
/// </summary>
[Table("device_commands")]
public class DeviceCommand : IHasAppCode
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    /// <summary>
    /// 命令唯一标识
    /// </summary>
    [Required, MaxLength(64)]
    public string CommandId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// 应用代码（租户标识）
    /// </summary>
    [MaxLength(50)]
    public string? AppCode { get; set; }

    /// <summary>
    /// 设备ID
    /// </summary>
    public long DeviceId { get; set; }

    /// <summary>
    /// 设备序列号
    /// </summary>
    [MaxLength(100)]
    public string? SerialNumber { get; set; }

    /// <summary>
    /// 命令类型
    /// </summary>
    [Required, MaxLength(50)]
    public string CommandType { get; set; } = string.Empty;

    /// <summary>
    /// 命令参数（JSON格式）
    /// </summary>
    public string? Parameters { get; set; }

    /// <summary>
    /// 命令状态
    /// </summary>
    public CommandStatus Status { get; set; } = CommandStatus.Pending;

    /// <summary>
    /// 发送时间
    /// </summary>
    public DateTime? SentAt { get; set; }

    /// <summary>
    /// 完成时间
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// 结果信息
    /// </summary>
    [MaxLength(500)]
    public string? Result { get; set; }

    /// <summary>
    /// 错误信息
    /// </summary>
    [MaxLength(1000)]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 超时时间（秒）
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// 重试次数
    /// </summary>
    public int RetryCount { get; set; } = 0;

    /// <summary>
    /// 最大重试次数
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 更新时间
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 创建人ID
    /// </summary>
    public long? CreatedBy { get; set; }

    /// <summary>
    /// 创建人名称
    /// </summary>
    [MaxLength(100)]
    public string? CreatedByName { get; set; }

    /// <summary>
    /// 关联的设备
    /// </summary>
    [ForeignKey("DeviceId")]
    public virtual Device? Device { get; set; }
}

/// <summary>
/// 设备指令历史
/// 用于记录设备指令的状态变更历史
/// </summary>
[Table("command_histories")]
public class CommandHistory : IHasAppCode
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    /// <summary>
    /// 应用代码（租户标识）
    /// </summary>
    [MaxLength(50)]
    public string? AppCode { get; set; }

    /// <summary>
    /// 关联的指令ID
    /// </summary>
    public long CommandId { get; set; }

    /// <summary>
    /// 历史记录类型
    /// </summary>
    public CommandHistoryType Type { get; set; }

    /// <summary>
    /// 状态（从）
    /// </summary>
    public CommandStatus? FromStatus { get; set; }

    /// <summary>
    /// 状态（到）
    /// </summary>
    public CommandStatus? ToStatus { get; set; }

    /// <summary>
    /// 描述
    /// </summary>
    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// 附加数据（JSON格式）
    /// </summary>
    public string? Data { get; set; }

    /// <summary>
    /// 操作人ID
    /// </summary>
    public long? OperatorId { get; set; }

    /// <summary>
    /// 操作人名称
    /// </summary>
    [MaxLength(100)]
    public string? OperatorName { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 关联的指令
    /// </summary>
    [ForeignKey("CommandId")]
    public virtual DeviceCommand? Command { get; set; }
}

/// <summary>
/// 指令历史类型枚举
/// </summary>
public enum CommandHistoryType
{
    /// <summary>
    /// 创建
    /// </summary>
    Created = 0,

    /// <summary>
    /// 发送
    /// </summary>
    Sent = 1,

    /// <summary>
    /// 接收
    /// </summary>
    Received = 2,

    /// <summary>
    /// 成功
    /// </summary>
    Success = 3,

    /// <summary>
    /// 失败
    /// </summary>
    Failed = 4,

    /// <summary>
    /// 超时
    /// </summary>
    Timeout = 5,

    /// <summary>
    /// 取消
    /// </summary>
    Cancelled = 6,

    /// <summary>
    /// 重试
    /// </summary>
    Retry = 7,

    /// <summary>
    /// 响应
    /// </summary>
    Response = 8
}
