using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IoTPlatform.Models;

[Table("work_orders")]
public class WorkOrder
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Required, MaxLength(50)] public string OrderNo { get; set; } = string.Empty;
    [Required, MaxLength(200)] public string Title { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string Type { get; set; } = "maintenance"; // maintenance, repair, inspection, installation, other
    [Required, MaxLength(20)] public string Priority { get; set; } = "medium"; // low, medium, high, urgent
    [Required, MaxLength(20)] public string Status { get; set; } = "pending"; // pending, assigned, in_progress, resolved, closed, rejected
    public long? CustomerId { get; set; }
    public long? DeviceId { get; set; }
    public long? AreaId { get; set; }
    [MaxLength(200)] public string? DeviceName { get; set; }
    [MaxLength(50)] public string? DeviceCode { get; set; }
    [MaxLength(200)] public string? AreaName { get; set; }
    [MaxLength(2000)] public string? Description { get; set; }
    [MaxLength(100)] public string? Reporter { get; set; }
    public DateTime ReportTime { get; set; } = DateTime.UtcNow;
    [MaxLength(100)] public string? Assignee { get; set; }
    public DateTime? AssignTime { get; set; }
    public DateTime? EstimatedTime { get; set; }
    public DateTime? ResolvedTime { get; set; }
    public DateTime? ClosedTime { get; set; }
    [MaxLength(2000)] public string? ResolveDescription { get; set; }
    [MaxLength(100)] public string? ProjectName { get; set; }
    [MaxLength(100)] public string? CreatedBy { get; set; }
    public string? AppCode { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey("CustomerId")] public virtual Customer? Customer { get; set; }
    [ForeignKey("DeviceId")] public virtual Device? Device { get; set; }
    [ForeignKey("AreaId")] public virtual Area? Area { get; set; }
    public virtual ICollection<WorkOrderLog>? Logs { get; set; }
    public virtual ICollection<WorkOrderAttachment>? Attachments { get; set; }
}
