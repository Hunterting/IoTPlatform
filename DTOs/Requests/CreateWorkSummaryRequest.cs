using System.ComponentModel.DataAnnotations;

namespace IoTPlatform.DTOs.Requests;

public class CreateWorkSummaryRequest
{
    [Required]
    public long ProjectId { get; set; }

    [MaxLength(100)]
    public string? FeedbackPerson { get; set; }

    [MaxLength(100)]
    public string? Assignee { get; set; }

    [MaxLength(100)]
    public string? Assistant { get; set; }

    [MaxLength(2000)]
    public string? WorkContent { get; set; }

    [Required]
    public DateTime Date { get; set; }

    public string? AppCode { get; set; }
}
