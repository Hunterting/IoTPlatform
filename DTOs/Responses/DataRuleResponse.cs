namespace IoTPlatform.DTOs.Responses;

/// <summary>
/// 数据规则响应DTO
/// </summary>
public class DataRuleDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? RuleType { get; set; }
    public string? DataType { get; set; }
    public double? MinValue { get; set; }
    public double? MaxValue { get; set; }
    public string? Level { get; set; }
    public bool IsActive { get; set; }
    public long? DeviceId { get; set; }
    public string? DeviceName { get; set; }
    public long? AreaId { get; set; }
    public string? AreaName { get; set; }
    public string? AppCode { get; set; }
    public string? RuleExpression { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
