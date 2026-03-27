using System.ComponentModel.DataAnnotations;

namespace IoTPlatform.DTOs.Requests;

/// <summary>
/// 创建数据规则请求
/// </summary>
public class CreateDataRuleRequest
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? RuleType { get; set; } // alert, transform, validation

    [MaxLength(50)]
    public string? DataType { get; set; } // temperature, humidity, etc.

    public double? MinValue { get; set; }

    public double? MaxValue { get; set; }

    [MaxLength(20)]
    public string? Level { get; set; } // info, warning, critical

    public bool IsActive { get; set; } = true;

    public long? DeviceId { get; set; }

    public long? AreaId { get; set; }

    [MaxLength(50)]
    public string? AppCode { get; set; }

    [MaxLength(1000)]
    public string? RuleExpression { get; set; }

    [MaxLength(1000)]
    public string? Description { get; set; }
}
