using System.ComponentModel.DataAnnotations;

namespace IoTPlatform.DTOs.Requests;

/// <summary>
/// 创建档案请求
/// </summary>
public class CreateArchiveRequest
{
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? AppCode { get; set; }

    [Required]
    [MaxLength(50)]
    public string Type { get; set; } // floor_plan, 3d_model, photo, document

    [MaxLength(20)]
    public string? Size { get; set; }

    public DateTime? Date { get; set; }

    [MaxLength(50)]
    public string? Category { get; set; }

    public bool Is3DModel { get; set; }

    public long? AreaId { get; set; }

    [MaxLength(500)]
    public string? ImageUrl { get; set; }

    [MaxLength(500)]
    public string? FilePath { get; set; }

    public string? SceneConfig { get; set; } // JSON: SceneConfig with DeviceMarker3D[]
}
