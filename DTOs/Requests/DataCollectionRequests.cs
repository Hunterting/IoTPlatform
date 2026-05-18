using System.ComponentModel.DataAnnotations;

namespace IoTPlatform.DTOs.Requests;

/// <summary>
/// 创建网关请求
/// </summary>
public class CreateGatewayRequest
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? GatewayType { get; set; }

    [Required]
    [MaxLength(50)]
    public string SourceProtocol { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string TargetProtocol { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public string? Config { get; set; }

    public string? Description { get; set; }
}

/// <summary>
/// 更新网关请求
/// </summary>
public class UpdateGatewayRequest
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? GatewayType { get; set; }

    [MaxLength(50)]
    public string? SourceProtocol { get; set; }

    [MaxLength(50)]
    public string? TargetProtocol { get; set; }

    [MaxLength(20)]
    public string? Status { get; set; }

    public bool? IsActive { get; set; }

    public string? Config { get; set; }

    public string? Description { get; set; }
}

/// <summary>
/// 创建隧道请求
/// </summary>
public class CreateTunnelRequest
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(20)]
    public string TunnelType { get; set; } = "P2P";

    public bool IsActive { get; set; } = true;

    public int LocalPort { get; set; }

    public int RemotePort { get; set; }

    [MaxLength(255)]
    public string? RemoteHost { get; set; }

    public bool Encryption { get; set; } = true;

    public string? Config { get; set; }

    public string? Description { get; set; }
}

/// <summary>
/// 更新隧道请求
/// </summary>
public class UpdateTunnelRequest
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(20)]
    public string? TunnelType { get; set; }

    [MaxLength(20)]
    public string? Status { get; set; }

    public bool? IsActive { get; set; }

    public int? LocalPort { get; set; }

    public int? RemotePort { get; set; }

    [MaxLength(255)]
    public string? RemoteHost { get; set; }

    public bool? Encryption { get; set; }

    public string? Config { get; set; }

    public string? Description { get; set; }
}

/// <summary>
/// 创建插件请求
/// </summary>
public class CreatePluginRequest
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string Version { get; set; } = "1.0.0";

    [MaxLength(50)]
    public string? PluginType { get; set; }

    public string? Description { get; set; }

    [MaxLength(100)]
    public string? Author { get; set; }

    [MaxLength(500)]
    public string? FilePath { get; set; }

    public string? Config { get; set; }

    public string? Dependencies { get; set; }
}

/// <summary>
/// 更新插件请求
/// </summary>
public class UpdatePluginRequest
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Version { get; set; }

    [MaxLength(50)]
    public string? Status { get; set; }

    public bool? IsActive { get; set; }

    [MaxLength(50)]
    public string? PluginType { get; set; }

    public string? Description { get; set; }

    [MaxLength(100)]
    public string? Author { get; set; }

    [MaxLength(500)]
    public string? FilePath { get; set; }

    public string? Config { get; set; }

    public string? Dependencies { get; set; }
}

/// <summary>
/// 创建数据库配置请求
/// </summary>
public class CreateDatabaseConfigRequest
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string DatabaseType { get; set; } = "MySQL";

    public bool IsActive { get; set; } = true;

    [Required]
    [MaxLength(255)]
    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 3306;

    [Required]
    [MaxLength(100)]
    public string DatabaseName { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? Username { get; set; }

    public string? Password { get; set; }

    public string? ConnectionString { get; set; }

    public string? Config { get; set; }

    public string? Description { get; set; }
}

/// <summary>
/// 更新数据库配置请求
/// </summary>
public class UpdateDatabaseConfigRequest
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? DatabaseType { get; set; }

    [MaxLength(20)]
    public string? Status { get; set; }

    public bool? IsActive { get; set; }

    [MaxLength(255)]
    public string? Host { get; set; }

    public int? Port { get; set; }

    [MaxLength(100)]
    public string? DatabaseName { get; set; }

    [MaxLength(100)]
    public string? Username { get; set; }

    public string? Password { get; set; }

    public string? ConnectionString { get; set; }

    public string? Config { get; set; }

    public string? Description { get; set; }
}

/// <summary>
/// 测试数据库连接请求
/// </summary>
public class TestDatabaseConnectionRequest
{
    [Required]
    [MaxLength(50)]
    public string DatabaseType { get; set; } = "MySQL";

    [Required]
    [MaxLength(255)]
    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 3306;

    [Required]
    [MaxLength(100)]
    public string DatabaseName { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? Username { get; set; }

    public string? Password { get; set; }

    public string? ConnectionString { get; set; }
}
