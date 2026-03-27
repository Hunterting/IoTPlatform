using System.ComponentModel.DataAnnotations;

namespace IoTPlatform.DTOs.Requests;

/// <summary>
/// 更新数据规则请求
/// </summary>
public class UpdateDataRuleRequest
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? RuleType { get; set; }

    [MaxLength(50)]
    public string? DataType { get; set; }

    public double? MinValue { get; set; }

    public double? MaxValue { get; set; }

    [MaxLength(20)]
    public string? Level { get; set; }

    public bool IsActive { get; set; }

    [MaxLength(1000)]
    public string? RuleExpression { get; set; }

    [MaxLength(1000)]
    public string? Description { get; set; }
}
