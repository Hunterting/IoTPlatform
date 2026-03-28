using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IoTPlatform.Models;

[Table("work_order_attachments")]
public class WorkOrderAttachment
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    public long WorkOrderId { get; set; }
    [Required, MaxLength(200)] public string FileName { get; set; } = string.Empty;
    [MaxLength(500)] public string? FileUrl { get; set; }
    [MaxLength(20)] public string? FileSize { get; set; }
    [MaxLength(50)] public string? FileType { get; set; }
    public long FileSizeBytes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey("WorkOrderId")] public virtual WorkOrder? WorkOrder { get; set; }
}
