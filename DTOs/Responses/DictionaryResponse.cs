namespace IoTPlatform.DTOs.Responses;

/// <summary>
/// 字典类型响应DTO
/// </summary>
public class DictionaryTypeDto
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
    public string? AppCode { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// 字典项响应DTO
/// </summary>
public class DictionaryItemDto
{
    public long Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Sort { get; set; }
    public string? Description { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? AppCode { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
