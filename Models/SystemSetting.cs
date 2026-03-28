using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IoTPlatform.Models;

[Table("system_settings")]
public class SystemSetting
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Required][MaxLength(100)] public string Key { get; set; } = string.Empty;
    [Required] public string? Value { get; set; }
    [MaxLength(200)] public string? Description { get; set; }
    [MaxLength(50)] public string? Category { get; set; }
    [MaxLength(20)] public string? ValueType { get; set; } // string, number, boolean, json
    public string? AppCode { get; set; }
    public int SortOrder { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
