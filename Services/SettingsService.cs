using IoTPlatform.Data;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;

namespace IoTPlatform.Services;

/// <summary>
/// 系统设置服务实现
/// </summary>
public class SettingsService : ISettingsService
{
    private readonly AppDbContext _dbContext;

    public SettingsService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// 获取系统设置列表
    /// </summary>
    public async Task<PagedResponse<SettingDto>> GetSettingsAsync(int page, int pageSize, string? category, string? keyword, string? appCode)
    {
        var query = _dbContext.SystemSettings.AsQueryable();

        // 租户数据隔离
        if (!string.IsNullOrEmpty(appCode))
        {
            query = query.Where(s => s.AppCode == appCode);
        }

        // 分类筛选
        if (!string.IsNullOrEmpty(category))
        {
            query = query.Where(s => s.Category == category);
        }

        // 关键词搜索
        if (!string.IsNullOrEmpty(keyword))
        {
            query = query.Where(s =>
                s.Key.Contains(keyword) ||
                (s.Description != null && s.Description.Contains(keyword)));
        }

        var totalCount = await query.CountAsync();

        var settings = await query
            .OrderBy(s => s.Category)
            .ThenBy(s => s.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new SettingDto
            {
                Id = s.Id,
                Key = s.Key,
                Value = s.Value,
                Description = s.Description,
                Category = s.Category,
                ValueType = s.ValueType ?? "string",
                AppCode = s.AppCode,
                CreatedAt = s.CreatedAt,
                UpdatedAt = s.UpdatedAt
            })
            .ToListAsync();

        return PagedResponse<SettingDto>.Create(settings, totalCount, page, pageSize);
    }

    /// <summary>
    /// 根据键获取设置
    /// </summary>
    public async Task<SettingDto?> GetSettingAsync(string key, string? appCode)
    {
        var query = _dbContext.SystemSettings.AsQueryable();

        // 租户数据隔离
        if (!string.IsNullOrEmpty(appCode))
        {
            query = query.Where(s => s.AppCode == appCode);
        }

        var setting = await query.FirstOrDefaultAsync(s => s.Key == key);
        if (setting == null) return null;

        return new SettingDto
        {
            Id = setting.Id,
            Key = setting.Key,
            Value = setting.Value,
            Description = setting.Description,
            Category = setting.Category,
            ValueType = setting.ValueType ?? "string",
            AppCode = setting.AppCode,
            CreatedAt = setting.CreatedAt,
            UpdatedAt = setting.UpdatedAt
        };
    }

    /// <summary>
    /// 根据分类获取设置
    /// </summary>
    public async Task<Dictionary<string, string?>> GetSettingsByCategoryAsync(string category, string? appCode)
    {
        var query = _dbContext.SystemSettings.AsQueryable();

        // 租户数据隔离
        if (!string.IsNullOrEmpty(appCode))
        {
            query = query.Where(s => s.AppCode == appCode);
        }

        // 分类筛选
        query = query.Where(s => s.Category == category);

        var settings = await query.ToListAsync();
        var result = new Dictionary<string, string?>();

        foreach (var setting in settings)
        {
            result[setting.Key] = setting.Value;
        }

        return result;
    }

    /// <summary>
    /// 创建系统设置
    /// </summary>
    public async Task<SettingDto> CreateSettingAsync(CreateSettingRequest request)
    {
        // 检查键是否已存在
        var exists = await _dbContext.SystemSettings
            .AnyAsync(s => s.Key == request.Key && s.AppCode == request.AppCode);
        if (exists)
        {
            throw new InvalidOperationException("设置键已存在");
        }

        var setting = new SystemSetting
        {
            Key = request.Key,
            Value = request.Value,
            Description = request.Description,
            Category = request.Category,
            ValueType = request.ValueType,
            AppCode = request.AppCode,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.SystemSettings.Add(setting);
        await _dbContext.SaveChangesAsync();

        return new SettingDto
        {
            Id = setting.Id,
            Key = setting.Key,
            Value = setting.Value,
            Description = setting.Description,
            Category = setting.Category,
            ValueType = setting.ValueType ?? "string",
            AppCode = setting.AppCode,
            CreatedAt = setting.CreatedAt,
            UpdatedAt = setting.UpdatedAt
        };
    }

    /// <summary>
    /// 更新系统设置
    /// </summary>
    public async Task<SettingDto> UpdateSettingAsync(string key, UpdateSettingRequest request, string? appCode)
    {
        var setting = await _dbContext.SystemSettings
            .FirstOrDefaultAsync(s => s.Key == key && (string.IsNullOrEmpty(appCode) || s.AppCode == appCode));

        if (setting == null)
        {
            throw new InvalidOperationException("设置不存在");
        }

        // 权限检查
        if (!string.IsNullOrEmpty(appCode) && setting.AppCode != appCode)
        {
            throw new UnauthorizedAccessException("无权修改该设置");
        }

        setting.Value = request.Value;
        setting.Description = request.Description ?? setting.Description;
        setting.ValueType = request.ValueType;
        setting.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        return new SettingDto
        {
            Id = setting.Id,
            Key = setting.Key,
            Value = setting.Value,
            Description = setting.Description,
            Category = setting.Category,
            ValueType = setting.ValueType ?? "string",
            AppCode = setting.AppCode,
            CreatedAt = setting.CreatedAt,
            UpdatedAt = setting.UpdatedAt
        };
    }

    /// <summary>
    /// 删除系统设置
    /// </summary>
    public async Task DeleteSettingAsync(string key, string? appCode)
    {
        var setting = await _dbContext.SystemSettings
            .FirstOrDefaultAsync(s => s.Key == key && (string.IsNullOrEmpty(appCode) || s.AppCode == appCode));

        if (setting == null)
        {
            throw new InvalidOperationException("设置不存在");
        }

        // 权限检查
        if (!string.IsNullOrEmpty(appCode) && setting.AppCode != appCode)
        {
            throw new UnauthorizedAccessException("无权删除该设置");
        }

        _dbContext.SystemSettings.Remove(setting);
        await _dbContext.SaveChangesAsync();
    }
}
