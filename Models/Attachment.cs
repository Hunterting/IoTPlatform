using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using IoTPlatform.Data;

namespace IoTPlatform.Models;

/// <summary>
/// 通用附件实体 - 支持任意功能模块的文件附件
/// </summary>
[Table("attachments")]
public class Attachment : IHasAppCode
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    /// <summary>
    /// 功能模块：contracts、workorders、archives、devices 等
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string Module { get; set; } = string.Empty;

    /// <summary>
    /// 关联的业务ID（如项目ID、工单ID等）
    /// </summary>
    public long? BusinessId { get; set; }

    /// <summary>
    /// 附件名称（显示用）
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 原始文件名
    /// </summary>
    [MaxLength(500)]
    public string? OriginalName { get; set; }

    /// <summary>
    /// 文件扩展名
    /// </summary>
    [MaxLength(20)]
    public string? Extension { get; set; }

    /// <summary>
    /// 文件相对路径
    /// </summary>
    [MaxLength(1000)]
    public string? FileUrl { get; set; }

    /// <summary>
    /// 文件大小（格式化字符串）
    /// </summary>
    [MaxLength(20)]
    public string? FileSize { get; set; }

    /// <summary>
    /// 文件大小（字节）
    /// </summary>
    public long FileSizeBytes { get; set; }

    /// <summary>
    /// 文件类型/MIME类型
    /// </summary>
    [MaxLength(100)]
    public string? ContentType { get; set; }

    /// <summary>
    /// 上传时间
    /// </summary>
    public DateTime UploadDate { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 上传用户ID
    /// </summary>
    public long? UploadUserId { get; set; }

    /// <summary>
    /// 租户代码
    /// </summary>
    public string? AppCode { get; set; }

    /// <summary>
    /// 备注
    /// </summary>
    [MaxLength(500)]
    public string? Remark { get; set; }
}
