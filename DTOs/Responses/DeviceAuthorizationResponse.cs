namespace IoTPlatform.DTOs.Responses;

/// <summary>设备授权列表项（每台设备一行，含派生状态）</summary>
public class DeviceAuthorizationDto
{
    public long DeviceId { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public string? Model { get; set; }
    public string? SerialNumber { get; set; }
    public string? Category { get; set; }
    public long? AreaId { get; set; }
    public string? AreaName { get; set; }
    public string? AppCode { get; set; }
    public string DeviceStatus { get; set; } = string.Empty;

    /// <summary>授权记录 ID（无授权记录时为 null）</summary>
    public long? AuthorizationId { get; set; }
    /// <summary>派生状态：active / expiring_soon / expired / revoked / pending(未授权)</summary>
    public string AuthorizationStatus { get; set; } = "pending";
    public string StatusLabel { get; set; } = "待授权";
    public DateTime? AuthorizedAt { get; set; }
    public DateTime? ExpireAt { get; set; }
    public string? AuthorizedByName { get; set; }
    public string? Remark { get; set; }

    /// <summary>距到期天数（负数=已过期）</summary>
    public int? DaysUntilExpire { get; set; }
    public bool IsExpiringSoon { get; set; }
    /// <summary>是否处于「已授权且已过期」禁用状态</summary>
    public bool IsDisabled { get; set; }
}

/// <summary>授权统计</summary>
public class DeviceAuthorizationSummaryDto
{
    public int Total { get; set; }
    public int Active { get; set; }
    public int ExpiringSoon { get; set; }
    public int Expired { get; set; }
    public int Pending { get; set; }
    public int Revoked { get; set; }
    /// <summary>提前提醒天数阈值</summary>
    public int ExpiringSoonDays { get; set; } = 30;
}

// ── 请求 ──────────────────────────────────────────────────────────────────────

public class GrantDeviceAuthorizationRequest
{
    public long DeviceId { get; set; }
    /// <summary>到期时间（UTC）；留空则按 expireDays 计算</summary>
    public DateTime? ExpireAt { get; set; }
    /// <summary>授权天数（默认 365），ExpireAt 优先</summary>
    public int ExpireDays { get; set; } = 365;
    public string? AppCode { get; set; }
    public string? Remark { get; set; }
}

public class RevokeDeviceAuthorizationRequest
{
    public long DeviceId { get; set; }
    public string? AppCode { get; set; }
    public string? Remark { get; set; }
}
