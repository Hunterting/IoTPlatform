using IoTPlatform.Data.Repositories.Interfaces;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Helpers;
using IoTPlatform.Infrastructure.Protocol;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace IoTPlatform.Services;

/// <summary>
/// 协议配置服务实现（使用仓储模式）
/// </summary>
public class ProtocolConfigService : IProtocolConfigService
{
    private readonly IProtocolConfigRepository _protocolConfigRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IProtocolAdapterFactory _adapterFactory;
    private readonly ILogger<ProtocolConfigService>? _logger;

    public ProtocolConfigService(
        IProtocolConfigRepository protocolConfigRepository,
        IUnitOfWork unitOfWork,
        IProtocolAdapterFactory adapterFactory,
        ILogger<ProtocolConfigService>? logger = null)
    {
        _protocolConfigRepository = protocolConfigRepository;
        _unitOfWork = unitOfWork;
        _adapterFactory = adapterFactory;
        _logger = logger;
    }

    /// <summary>
    /// 获取协议配置列表
    /// </summary>
    public async Task<PagedResponse<ProtocolConfigDto>> GetProtocolConfigsAsync(int page, int pageSize, string? keyword, string? type, string? appCode)
    {
        var query = _protocolConfigRepository.GetQueryable();

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
                DeviceIdsJson = p.DeviceIds,
                ConfigJson = p.Config,
                Description = p.Description,
                AppCode = p.AppCode,
                CreatedAt = p.CreatedAt,
                UpdatedAt = p.UpdatedAt
            })
            .ToListAsync();

        // 手动解析JSON
        foreach (var config in configs)
        {
            if (!string.IsNullOrEmpty(config.DeviceIdsJson))
            {
                config.DeviceIds = JsonSerializer.Deserialize<List<long>>(config.DeviceIdsJson, new JsonSerializerOptions());
            }
            if (!string.IsNullOrEmpty(config.ConfigJson))
            {
                config.Config = JsonSerializer.Deserialize<Dictionary<string, object>>(config.ConfigJson, new JsonSerializerOptions());
            }
        }

        return PagedResponse<ProtocolConfigDto>.Create(configs, totalCount, page, pageSize);
    }

    /// <summary>
    /// 获取协议配置详情
    /// </summary>
    public async Task<ProtocolConfigDto?> GetProtocolConfigAsync(long id, string? appCode)
    {
        var query = _protocolConfigRepository.GetQueryable();

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

        await _protocolConfigRepository.AddAsync(config);
        await _unitOfWork.SaveChangesAsync();

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
        var config = await _protocolConfigRepository.GetByIdAsync(id);
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

        await _protocolConfigRepository.UpdateAsync(config);
        await _unitOfWork.SaveChangesAsync();

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
        var config = await _protocolConfigRepository.GetByIdAsync(id);
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

        await _protocolConfigRepository.DeleteAsync(config);
        await _unitOfWork.SaveChangesAsync();
    }

    /// <summary>
    /// 启动协议
    /// </summary>
    public async Task StartProtocolAsync(long id, string? appCode)
    {
        var config = await _protocolConfigRepository.GetByIdAsync(id);
        if (config == null)
        {
            throw new InvalidOperationException("协议配置不存在");
        }

        // 权限检查
        if (!string.IsNullOrEmpty(appCode) && config.AppCode != appCode)
        {
            throw new UnauthorizedAccessException("无权启动该协议配置");
        }

        // 如果已经激活，直接返回
        if (config.Status == "active")
        {
            return;
        }

        try
        {
            // 创建协议适配器
            var protocolType = config.Type.ToUpperInvariant();
            var adapter = _adapterFactory.CreateAdapter(protocolType, (int)config.Id);

            // 获取连接配置
            var connectionString = config.Config ?? "{}";

            // 添加 AppCode 到配置
            if (!string.IsNullOrEmpty(config.AppCode) && !connectionString.Contains("AppCode"))
            {
                var configDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(connectionString) ?? new();
                configDict["AppCode"] = JsonSerializer.SerializeToElement(config.AppCode);
                connectionString = JsonSerializer.Serialize(configDict);
            }

            // 连接并启动数据采集
            var connected = await adapter.ConnectAsync(connectionString);
            if (connected)
            {
                await adapter.StartDataCollectionAsync();
                _logger?.LogInformation("协议已启动: {Name}, Type={Type}", config.Name, config.Type);
            }
            else
            {
                throw new InvalidOperationException($"协议连接失败: {config.Name}");
            }

            // 更新状态
            config.Status = "active";
            config.UpdatedAt = DateTime.UtcNow;
            await _protocolConfigRepository.UpdateAsync(config);
            await _unitOfWork.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "启动协议失败: {Name}", config.Name);
            throw;
        }
    }

    /// <summary>
    /// 停止协议
    /// </summary>
    public async Task StopProtocolAsync(long id, string? appCode)
    {
        var config = await _protocolConfigRepository.GetByIdAsync(id);
        if (config == null)
        {
            throw new InvalidOperationException("协议配置不存在");
        }

        // 权限检查
        if (!string.IsNullOrEmpty(appCode) && config.AppCode != appCode)
        {
            throw new UnauthorizedAccessException("无权停止该协议配置");
        }

        // 如果已经停止，直接返回
        if (config.Status == "inactive")
        {
            return;
        }

        try
        {
            // 释放协议适配器
            _adapterFactory.ReleaseAdapter((int)config.Id);
            _logger?.LogInformation("协议已停止: {Name}, Type={Type}", config.Name, config.Type);

            // 更新状态
            config.Status = "inactive";
            config.UpdatedAt = DateTime.UtcNow;
            await _protocolConfigRepository.UpdateAsync(config);
            await _unitOfWork.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "停止协议失败: {Name}", config.Name);
            throw;
        }
    }
}
