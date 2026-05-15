using System.ComponentModel.DataAnnotations;

namespace IoTPlatform.DTOs.Requests;

public class CreateProjectRequest
{
    [Required]
    public long CustomerId { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Address { get; set; }

    public int DeviceCount { get; set; }

    public DateTime? OnlineDate { get; set; }

    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = "planning";

    public string? AppCode { get; set; }
}
