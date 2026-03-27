using IoTPlatform.Data;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace IoTPlatform.Services;

/// <summary>
/// 协议配置服务实现
/// </summary>
public class ProtocolConfigService : IProtocolConfigService
{
    private readonly AppDbContext _dbContext;

    public ProtocolConfigService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// 获取协议配置列表
    /// </summary>
    public async Task<PagedResponse<ProtocolConfigDto>> GetProtocolConfigsAsync(int page, int pageSize, string? keyword, string? type, string? appCode)
    {
        var query = _dbContext.ProtocolConfigs.AsQueryable();

        // 租户数据隔离
        if (!string.IsNullOrEmpty(appCode))
        {
            query = query.Where(p => p.AppCode == appCode);
        }

        // 类型筛选
        if (!string.IsNullOrEmpty(type))
        {
            query = query.Where(p => p.Type == type);
        }

        // 关键词搜索
        if (!string.IsNullOrEmpty(keyword))
        {
            query = query.Where(p =>
                p.Name.Contains(keyword) ||
                (p.Description != null && p.Description.Contains(keyword)));
        }

        var totalCount = await query.CountAsync();

        var configs = await query
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new ProtocolConfigDto
            {
                Id = p.Id,
                Name = p.Name,
                Type = p.Type,
                Status = p.Status,
                DeviceIds = !string.IsNullOrEmpty(p.DeviceIds)
                    ? JsonSerializer.Deserialize<List<long>>(p.DeviceIds)
                    : null,
                Config = !string.IsNullOrEmpty(p.Config)
                    ? JsonSerializer.Deserialize<Dictionary<string, object>>(p.Config)
                    : null,
                Description = p.Description,
                AppCode = p.AppCode,
                CreatedAt = p.CreatedAt,
                UpdatedAt = p.UpdatedAt
            })
            .ToListAsync();

        return PagedResponse<ProtocolConfigDto>.Create(configs, totalCount, page, pageSize);
    }

    /// <summary>
    /// 获取协议配置详情
    /// </summary>
    public async Task<ProtocolConfigDto?> GetProtocolConfigAsync(long id, string? appCode)
    {
        var query = _dbContext.ProtocolConfigs.AsQueryable();

        // 租户数据隔离
        if (!string.IsNullOrEmpty(appCode))
        {
            query = query.Where(p => p.AppCode == appCode);
        }

        var config = await query.FirstOrDefaultAsync(p => p.Id == id);
        if (config == null) return null;

        return new ProtocolConfigDto
        {
            Id = config.Id,
            Name = config.Name,
            Type = config.Type,
            Status = config.Status,
            DeviceIds = !string.IsNullOrEmpty(config.DeviceIds)
                ? JsonSerializer.Deserialize<List<long>>(config.DeviceIds)
                : null,
            Config = !string.IsNullOrEmpty(config.Config)
                ? JsonSerializer.Deserialize<Dictionary<string, object>>(config.Config)
                : null,
            Description = config.Description,
            AppCode = config.AppCode,
            CreatedAt = config.CreatedAt,
            UpdatedAt = config.UpdatedAt
        };
    }

    /// <summary>
    /// 创建协议配置
    /// </summary>
    public async Task<ProtocolConfigDto> CreateProtocolConfigAsync(CreateProtocolConfigRequest request)
    {
        var config = new ProtocolConfig
        {
            Name = request.Name,
            Type = request.Type,
            Status = "inactive",
            Description = request.Description,
            AppCode = request.AppCode,
            DeviceIds = request.DeviceIds != null
                ? JsonSerializer.Serialize(request.DeviceIds)
                : null,
            Config = request.Config != null
                ? JsonSerializer.Serialize(request.Config)
                : null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.ProtocolConfigs.Add(config);
        await _dbContext.SaveChangesAsync();

        return new ProtocolConfigDto
        {
            Id = config.Id,
            Name = config.Name,
            Type = config.Type,
            Status = config.Status,
            DeviceIds = request.DeviceIds,
            Config = request.Config,
            Description = config.Description,
            AppCode = config.AppCode,
            CreatedAt = config.CreatedAt,
            UpdatedAt = config.UpdatedAt
        };
    }

    /// <summary>
    /// 更新协议配置
    /// </summary>
    public async Task<ProtocolConfigDto> UpdateProtocolConfigAsync(long id, UpdateProtocolConfigRequest request, string? appCode)
    {
        var config = await _dbContext.ProtocolConfigs.FindAsync(id);
        if (config == null)
        {
            throw new InvalidOperationException("协议配置不存在");
        }

        // 权限检查
        if (!string.IsNullOrEmpty(appCode) && config.AppCode != appCode)
        {
            throw new UnauthorizedAccessException("无权修改该协议配置");
        }

        config.Name = request.Name;
        config.Status = request.Status;
        config.Description = request.Description ?? config.Description;
        config.AppCode = config.AppCode;
        config.DeviceIds = request.DeviceIds != null
            ? JsonSerializer.Serialize(request.DeviceIds)
            : config.DeviceIds;
        config.Config = request.Config != null
            ? JsonSerializer.Serialize(request.Config)
            : config.Config;
        config.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        return new ProtocolConfigDto
        {
            Id = config.Id,
            Name = config.Name,
            Type = config.Type,
            Status = config.Status,
            DeviceIds = request.DeviceIds,
            Config = request.Config,
            Description = config.Description,
            AppCode = config.AppCode,
            CreatedAt = config.CreatedAt,
            UpdatedAt = config.UpdatedAt
        };
    }

    /// <summary>
    /// 删除协议配置
    /// </summary>
    public async Task DeleteProtocolConfigAsync(long id, string? appCode)
    {
        var config = await _dbContext.ProtocolConfigs.FindAsync(id);
        if (config == null)
        {
            throw new InvalidOperationException("协议配置不存在");
        }

        // 权限检查
        if (!string.IsNullOrEmpty(appCode) && config.AppCode != appCode)
        {
            throw new UnauthorizedAccessException("无权删除该协议配置");
        }

        // 检查是否有活跃状态
        if (config.Status == "active")
        {
            throw new InvalidOperationException("协议处于活跃状态，无法删除。请先停止协议。");
        }

        _dbContext.ProtocolConfigs.Remove(config);
        await _dbContext.SaveChangesAsync();
    }

    /// <summary>
    /// 启动协议
    /// </summary>
    public async Task StartProtocolAsync(long id, string? appCode)
    {
        var config = await _dbContext.ProtocolConfigs.FindAsync(id);
        if (config == null)
        {
            throw new InvalidOperationException("协议配置不存在");
        }

        // 权限检查
        if (!string.IsNullOrEmpty(appCode) && config.AppCode != appCode)
        {
            throw new UnauthorizedAccessException("无权启动该协议配置");
        }

        // TODO: 这里应该调用MQTT客户端服务或其他协议适配器启动协议
        config.Status = "active";
        config.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();
    }

    /// <summary>
    /// 停止协议
    /// </summary>
    public async Task StopProtocolAsync(long id, string? appCode)
    {
        var config = await _dbContext.ProtocolConfigs.FindAsync(id);
        if (config == null)
        {
            throw new InvalidOperationException("协议配置不存在");
        }

        // 权限检查
        if (!string.IsNullOrEmpty(appCode) && config.AppCode != appCode)
        {
            throw new UnauthorizedAccessException("无权停止该协议配置");
        }

        // TODO: 这里应该调用MQTT客户端服务或其他协议适配器停止协议
        config.Status = "inactive";
        config.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();
    }
}
