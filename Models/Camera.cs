using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IoTPlatform.Models;

[Table("cameras")]
public class Camera
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;
    public long? AreaId { get; set; }
    [MaxLength(500)] public string? StreamUrl { get; set; }
    [MaxLength(500)] public string? ImageUrl { get; set; }
    [MaxLength(20)] public string? Protocol { get; set; } // rtsp, rtmp, hls, http
    [MaxLength(50)] public string? Type { get; set; } // bullet, dome, ptz
    [MaxLength(20)] public string? Resolution { get; set; } // 720p, 1080p, 4k
    [Required, MaxLength(20)] public string Status { get; set; } = "online"; // online, offline
    public string? AppCode { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey("AreaId")] public virtual Area? Area { get; set; }
}
