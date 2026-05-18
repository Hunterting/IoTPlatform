using IoTPlatform.Models;

namespace IoTPlatform.DTOs.Responses;

/// <summary>
/// 网关DTO
/// </summary>
public class GatewayDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? GatewayType { get; set; }
    public string SourceProtocol { get; set; } = string.Empty;
    public string TargetProtocol { get; set; } = string.Empty;
    public string Status { get; set; } = "offline";
    public bool IsActive { get; set; }
    public int Throughput { get; set; }
    public string? Config { get; set; }
    public string? Description { get; set; }
    public string? AppCode { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// 隧道DTO
/// </summary>
public class TunnelDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TunnelType { get; set; } = "P2P";
    public string Status { get; set; } = "disconnected";
    public bool IsActive { get; set; }
    public int LocalPort { get; set; }
    public int RemotePort { get; set; }
    public string? RemoteHost { get; set; }
    public bool Encryption { get; set; }
    public string? Bandwidth { get; set; }
    public string? Config { get; set; }
    public string? Description { get; set; }
    public string? AppCode { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// 插件DTO
/// </summary>
public class PluginDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0.0";
    public string Status { get; set; } = "stopped";
    public bool IsActive { get; set; }
    public string? PluginType { get; set; }
    public string? Description { get; set; }
    public string? Author { get; set; }
    public string? FilePath { get; set; }
    public string? Config { get; set; }
    public string? Dependencies { get; set; }
    public DateTime? InstalledAt { get; set; }
    public string? AppCode { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// 数据库配置DTO
/// </summary>
public class DatabaseConfigDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DatabaseType { get; set; } = "MySQL";
    public string Status { get; set; } = "disconnected";
    public bool IsActive { get; set; }
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string DatabaseName { get; set; } = string.Empty;
    public string? Username { get; set; }
    /// <summary>
    /// 是否已设置密码
    /// </summary>
    public bool HasPassword { get; set; }
    public string? Description { get; set; }
    public DateTime? LastTestAt { get; set; }
    public string? AppCode { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
