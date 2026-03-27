namespace IoTPlatform.DTOs.Responses;

/// <summary>
/// 区域响应DTO
/// </summary>
public class AreaDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? Image { get; set; }
    public long? ParentId { get; set; }
    public string? ParentName { get; set; }
    public long? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public string? AppCode { get; set; }
    public string? Description { get; set; }
    public int DeviceCount { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// 区域树节点DTO
/// </summary>
public class AreaTreeNodeDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public long? ParentId { get; set; }
    public int DeviceCount { get; set; }
    public List<AreaTreeNodeDto>? Children { get; set; }
}
