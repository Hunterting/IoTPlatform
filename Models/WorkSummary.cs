using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IoTPlatform.Models;

[Table("work_summaries")]
public class WorkSummary
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    public long ProjectId { get; set; }
    [MaxLength(100)] public string? FeedbackPerson { get; set; }
    [MaxLength(100)] public string? Assignee { get; set; }
    [MaxLength(100)] public string? Assistant { get; set; }
    [MaxLength(2000)] public string? WorkContent { get; set; }
    public DateTime Date { get; set; }
    public string? AppCode { get; set; }

    [ForeignKey("ProjectId")] public virtual Project? Project { get; set; }
}
