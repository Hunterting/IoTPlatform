using IoTPlatform.Data.Repositories.Interfaces;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Helpers;
using IoTPlatform.Models;
using IoTPlatform.Services.Guards;
using Microsoft.EntityFrameworkCore;

namespace IoTPlatform.Services;

/// <summary>
/// 系统设置服务实现（使用仓储模式）
/// </summary>
public class SettingsService : ISettingsService
{
    private readonly ISystemSettingRepository _settingsRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICustomerScopeGuard? _scopeGuard;

    /// <summary>守卫依赖缺失时 fail-closed，禁止静默降级为无校验写入。</summary>
    private ICustomerScopeGuard RequireGuard() =>
        _scopeGuard ?? throw new ForbiddenAccessException("缺少客户范围守卫，拒绝写入");

    /// <summary>配置 AppCode="system" 属于平台共享行（设计 §3.5）。</summary>
    private static bool IsSystemRow(string? appCode) =>
        string.Equals(appCode, "system", StringComparison.OrdinalIgnoreCase);

    /// <summary>写侧守卫：共享行仅超管可写；租户行按 Controller 下推的 scope 判定。</summary>
    private Task AssertWriteAsync(CustomerScope? scope, string? appCode, CancellationToken ct = default) =>
        RequireGuard().AssertWriteScopeByAppCodeAsync(scope, appCode, IsSystemRow(appCode), true, ct);

    public SettingsService(
        ISystemSettingRepository settingsRepository,
        IUnitOfWork unitOfWork,
        ICustomerScopeGuard? scopeGuard = null)
    {
        _settingsRepository = settingsRepository;
        _unitOfWork = unitOfWork;
        _scopeGuard = scopeGuard;
    }

    /// <summary>
    /// 获取系统设置列表
    /// </summary>
        /// <summary>旧签名保留：委托到范围下推重载，下级/内部路径零回归。</summary>
    public Task<PagedResponse<SettingDto>> GetSettingsAsync(int page, int pageSize, string? category, string? keyword, string? appCode) => GetSettingsAsync(page, pageSize, category, keyword, appCode, null, null);

    /// <summary>B4/M5：admin 的 appCode=null 不再等于零过滤。</summary>
    public async Task<PagedResponse<SettingDto>> GetSettingsAsync(int page, int pageSize, string? category, string? keyword, string? appCode, IReadOnlyCollection<long>? customerIds, IReadOnlyCollection<string>? appCodes = null)
    {
        var query = _settingsRepository.GetQueryable();
        query = query.ApplyQueryScopeWithShared(appCode, customerIds, appCodes);

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
        var setting = await _settingsRepository.GetByKeyAsync(key, appCode);
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

    /// <summary>旧签名保留：内部/下级路径零回归。</summary>
    public Task<Dictionary<string, string?>> GetSettingsByCategoryAsync(string category, string? appCode)
        => GetSettingsByCategoryAsync(category, appCode, null, null);

    /// <summary>B4/M5：admin 的授权客户集合生效；共享系统设置（AppCode 为空/"system"）仍可读。</summary>
    public async Task<Dictionary<string, string?>> GetSettingsByCategoryAsync(
        string category, string? appCode,
        IReadOnlyCollection<long>? customerIds, IReadOnlyCollection<string>? appCodes = null)
    {
        var settings = (await _settingsRepository.GetByCategoryAsync(category, appCode)).ToList();
        var restricted = customerIds != null || (appCodes?.Count ?? 0) > 0 || !string.IsNullOrEmpty(appCode);
        if (restricted)
        {
            var allowed = new HashSet<string>(
                (appCodes ?? Array.Empty<string>()).Concat(
                    string.IsNullOrEmpty(appCode) ? Array.Empty<string>() : new[] { appCode }),
                StringComparer.Ordinal);
            settings = settings.Where(s =>
                string.IsNullOrWhiteSpace(s.AppCode) || s.AppCode == "system" || allowed.Contains(s.AppCode))
                .ToList();
        }

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
    public async Task<SettingDto> CreateSettingAsync(CreateSettingRequest request, CustomerScope? scope = null)
    {
        var exists = await _settingsRepository.KeyExistsAsync(request.Key, request.AppCode);
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

        if (scope != null)
        {
            await AssertWriteAsync(scope, setting.AppCode);
        }

        await _settingsRepository.AddAsync(setting);
        await _unitOfWork.SaveChangesAsync();

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
    /// 写侧按权威归属定位设置行：admin 的 appCode 为空时不能简单取 FirstOrDefault，
    /// 否则同名共享行会遮住租户自建行；遍历候选并用同一守卫筛选，选不中即 fail-closed。
    /// </summary>
    private async Task<SystemSetting> ResolveSettingForWriteAsync(string key, string? appCode, CustomerScope? scope)
    {
        if (scope == null)
        {
            var legacy = await _settingsRepository.GetByKeyAsync(key, appCode).ConfigureAwait(false);
            return legacy ?? throw new InvalidOperationException("设置不存在");
        }

        var candidates = await _settingsRepository.GetQueryable()
            .Where(s => s.Key == key)
            .ToListAsync()
            .ConfigureAwait(false);

        if (scope.Mode == CustomerScopeMode.All)
        {
            var selected = candidates.FirstOrDefault(s => s.AppCode == appCode) ?? candidates.FirstOrDefault();
            return selected ?? throw new InvalidOperationException("设置不存在");
        }

        foreach (var candidate in candidates)
        {
            try
            {
                await AssertWriteAsync(scope, candidate.AppCode).ConfigureAwait(false);
                return candidate;
            }
            catch (ForbiddenAccessException)
            {
                // 该候选不属于调用方，继续找下一个；全部不命中则统一 fail-closed。
            }
        }

        throw new ForbiddenAccessException("无权修改该设置");
    }

    /// <summary>
    /// 更新系统设置
    /// </summary>
    public async Task<SettingDto> UpdateSettingAsync(string key, UpdateSettingRequest request, string? appCode, CustomerScope? scope = null)
    {
        var setting = await ResolveSettingForWriteAsync(key, appCode, scope);

        // 旧签名沿用历史软校验；新路径已在 ResolveSettingForWriteAsync 中 fail-closed。
        if (scope == null && !string.IsNullOrEmpty(appCode) && setting.AppCode != appCode)
        {
            throw new UnauthorizedAccessException("无权修改该设置");
        }

        setting.Value = request.Value;
        setting.Description = request.Description ?? setting.Description;
        setting.ValueType = request.ValueType;
        setting.UpdatedAt = DateTime.UtcNow;

        await _settingsRepository.UpdateAsync(setting);
        await _unitOfWork.SaveChangesAsync();

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
    public async Task DeleteSettingAsync(string key, string? appCode, CustomerScope? scope = null)
    {
        var setting = await ResolveSettingForWriteAsync(key, appCode, scope);

        // 旧签名沿用历史软校验；新路径已在 ResolveSettingForWriteAsync 中 fail-closed。
        if (scope == null && !string.IsNullOrEmpty(appCode) && setting.AppCode != appCode)
        {
            throw new UnauthorizedAccessException("无权删除该设置");
        }

        await _settingsRepository.DeleteAsync(setting);
        await _unitOfWork.SaveChangesAsync();
    }
}
