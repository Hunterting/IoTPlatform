using System.ComponentModel.DataAnnotations;

namespace IoTPlatform.DTOs.Requests;

/// <summary>
/// 更新档案请求
/// </summary>
public class UpdateArchiveRequest
{
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Type { get; set; }

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

    public string? SceneConfig { get; set; }
}
