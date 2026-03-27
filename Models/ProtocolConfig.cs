using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IoTPlatform.Models;

[Table("protocol_configs")]
public class ProtocolConfig
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string Type { get; set; } = string.Empty; // modbus, mqtt, opcua, http, tcp, bacnet
    [Required, MaxLength(20)] public string Status { get; set; } = "active"; // active, inactive
    public string? DeviceIds { get; set; } // JSON array
    public string? Config { get; set; } // JSON config
    [MaxLength(500)] public string? Description { get; set; }
    public string? AppCode { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
