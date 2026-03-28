using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IoTPlatform.Models;

[Table("data_rules")]
public class DataRule
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Required][MaxLength(100)] public string Name { get; set; } = string.Empty;
    [MaxLength(50)] public string? RuleType { get; set; } // alert, transform, validation
    [MaxLength(50)] public string? DataType { get; set; } // temperature, humidity, etc.
    public double? MinValue { get; set; }
    public double? MaxValue { get; set; }
    [MaxLength(20)] public string? Level { get; set; } // info, warning, critical
    public int Priority { get; set; }
    public bool IsActive { get; set; }
    public long? DeviceId { get; set; }
    public long? AreaId { get; set; }
    public string? AppCode { get; set; }
    [MaxLength(1000)] public string? RuleExpression { get; set; }
    [MaxLength(1000)] public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey("DeviceId")] public virtual Device? Device { get; set; }
    [ForeignKey("AreaId")] public virtual Area? Area { get; set; }
}
