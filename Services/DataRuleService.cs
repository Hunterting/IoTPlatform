using IoTPlatform.Data.Repositories.Interfaces;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Helpers;
using IoTPlatform.Models;
using IoTPlatform.Services.Rules;
using IoTPlatform.Services.Guards;
using Microsoft.EntityFrameworkCore;

namespace IoTPlatform.Services;

/// <summary>
/// 数据规则服务实现（使用仓储模式）
/// </summary>
public class DataRuleService : IDataRuleService
{
    private readonly IDataRuleRepository _dataRuleRepository;
    private readonly IDeviceRepository _deviceRepository;
    private readonly IAreaRepository _areaRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly RuleEngine _ruleEngine;
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

    public DataRuleService(
        IDataRuleRepository dataRuleRepository,
        IDeviceRepository deviceRepository,
        IAreaRepository areaRepository,
        IUnitOfWork unitOfWork,
        ICustomerScopeGuard? scopeGuard = null)
    {
        _dataRuleRepository = dataRuleRepository;
        _deviceRepository = deviceRepository;
        _areaRepository = areaRepository;
        _unitOfWork = unitOfWork;
        _scopeGuard = scopeGuard;
        _ruleEngine = new RuleEngine();
    }

    /// <summary>
    /// 获取数据规则列表
    /// </summary>
        /// <summary>旧签名保留：委托到范围下推重载，下级/内部路径零回归。</summary>
    public Task<PagedResponse<DataRuleDto>> GetDataRulesAsync(int page, int pageSize, string? keyword, string? ruleType, string? appCode) => GetDataRulesAsync(page, pageSize, keyword, ruleType, appCode, null, null);

    /// <summary>B4/M5：admin 的 appCode=null 不再等于零过滤。</summary>
    public async Task<PagedResponse<DataRuleDto>> GetDataRulesAsync(int page, int pageSize, string? keyword, string? ruleType, string? appCode, IReadOnlyCollection<long>? customerIds, IReadOnlyCollection<string>? appCodes = null)
    {
        var query = _dataRuleRepository.GetQueryable();
        query = query.ApplyQueryScopeWithShared(appCode, customerIds, appCodes);

        // 租户数据隔离
        if (!string.IsNullOrEmpty(appCode))
        {
            query = query.Where(d => d.AppCode == appCode);
        }

        // 规则类型筛选
        if (!string.IsNullOrEmpty(ruleType))
        {
            query = query.Where(d => d.RuleType == ruleType);
        }

        // 关键词搜索
        if (!string.IsNullOrEmpty(keyword))
        {
            query = query.Where(d =>
                d.Name.Contains(keyword) ||
                (d.Description != null && d.Description.Contains(keyword)));
        }

        var totalCount = await query.CountAsync();

        var rules = await query
            .Include(d => d.Device)
            .Include(d => d.Area)
            .OrderByDescending(d => d.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(d => new DataRuleDto
            {
                Id = d.Id,
                Name = d.Name,
                RuleType = d.RuleType,
                DataType = d.DataType,
                MinValue = d.MinValue,
                MaxValue = d.MaxValue,
                Level = d.Level,
                IsActive = d.IsActive,
                DeviceId = d.DeviceId,
                DeviceName = d.Device != null ? d.Device.Name : null,
                AreaId = d.AreaId,
                AreaName = d.Area != null ? d.Area.Name : null,
                AppCode = d.AppCode,
                RuleExpression = d.RuleExpression,
                Description = d.Description,
                CreatedAt = d.CreatedAt,
                UpdatedAt = d.UpdatedAt
            })
            .ToListAsync();

        return PagedResponse<DataRuleDto>.Create(rules, totalCount, page, pageSize);
    }

    /// <summary>
    /// 获取数据规则详情
    /// </summary>
    public async Task<DataRuleDto?> GetDataRuleAsync(long id, string? appCode)
    {
        var query = _dataRuleRepository.GetQueryable();

        // 租户数据隔离
        if (!string.IsNullOrEmpty(appCode))
        {
            query = query.Where(d => d.AppCode == appCode);
        }

        query = query
            .Include(d => d.Device)
            .Include(d => d.Area);

        var rule = await query.FirstOrDefaultAsync(d => d.Id == id);
        if (rule == null) return null;

        return new DataRuleDto
        {
            Id = rule.Id,
            Name = rule.Name,
            RuleType = rule.RuleType,
            DataType = rule.DataType,
            MinValue = rule.MinValue,
            MaxValue = rule.MaxValue,
            Level = rule.Level,
            IsActive = rule.IsActive,
            DeviceId = rule.DeviceId,
            DeviceName = rule.Device?.Name,
            AreaId = rule.AreaId,
            AreaName = rule.Area?.Name,
            AppCode = rule.AppCode,
            RuleExpression = rule.RuleExpression,
            Description = rule.Description,
            CreatedAt = rule.CreatedAt,
            UpdatedAt = rule.UpdatedAt
        };
    }

    /// <summary>
    /// 创建数据规则
    /// </summary>
    public async Task<DataRuleDto> CreateDataRuleAsync(CreateDataRuleRequest request, CustomerScope? scope = null)
    {
        var rule = new DataRule
        {
            Name = request.Name,
            RuleType = request.RuleType,
            DataType = request.DataType,
            MinValue = request.MinValue,
            MaxValue = request.MaxValue,
            Level = request.Level,
            IsActive = request.IsActive,
            DeviceId = request.DeviceId,
            AreaId = request.AreaId,
            AppCode = request.AppCode,
            RuleExpression = request.RuleExpression,
            Description = request.Description,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        if (scope != null)
        {
            await AssertWriteAsync(scope, rule.AppCode);
        }

        await _dataRuleRepository.AddAsync(rule);
        await _unitOfWork.SaveChangesAsync();

        // 重新加载以包含关联数据
        rule = await _dataRuleRepository.GetQueryable()
            .Include(d => d.Device)
            .Include(d => d.Area)
            .FirstOrDefaultAsync(d => d.Id == rule.Id);

        return new DataRuleDto
        {
            Id = rule.Id,
            Name = rule.Name,
            RuleType = rule.RuleType,
            DataType = rule.DataType,
            MinValue = rule.MinValue,
            MaxValue = rule.MaxValue,
            Level = rule.Level,
            IsActive = rule.IsActive,
            DeviceId = rule.DeviceId,
            DeviceName = rule.Device?.Name,
            AreaId = rule.AreaId,
            AreaName = rule.Area?.Name,
            AppCode = rule.AppCode,
            RuleExpression = rule.RuleExpression,
            Description = rule.Description,
            CreatedAt = rule.CreatedAt,
            UpdatedAt = rule.UpdatedAt
        };
    }

    /// <summary>
    /// 更新数据规则
    /// </summary>
    public async Task<DataRuleDto> UpdateDataRuleAsync(long id, UpdateDataRuleRequest request, string? appCode, CustomerScope? scope = null)
    {
        var rule = await _dataRuleRepository.GetQueryable()
            .Include(d => d.Device)
            .Include(d => d.Area)
            .FirstOrDefaultAsync(d => d.Id == id);
        if (rule == null)
        {
            throw new InvalidOperationException("数据规则不存在");
        }

        // 权限检查：新路径按 scope fail-closed；旧签名仅供内部通道沿用。
        if (scope != null)
        {
            await AssertWriteAsync(scope, rule.AppCode);
        }
        else if (!string.IsNullOrEmpty(appCode) && rule.AppCode != appCode)
        {
            throw new UnauthorizedAccessException("无权修改该数据规则");
        }

        rule.Name = request.Name;
        rule.RuleType = request.RuleType ?? rule.RuleType;
        rule.DataType = request.DataType ?? rule.DataType;
        rule.MinValue = request.MinValue ?? rule.MinValue;
        rule.MaxValue = request.MaxValue ?? rule.MaxValue;
        rule.Level = request.Level ?? rule.Level;
        rule.IsActive = request.IsActive;
        rule.RuleExpression = request.RuleExpression ?? rule.RuleExpression;
        rule.Description = request.Description ?? rule.Description;
        rule.UpdatedAt = DateTime.UtcNow;

        await _dataRuleRepository.UpdateAsync(rule);
        await _unitOfWork.SaveChangesAsync();

        return new DataRuleDto
        {
            Id = rule.Id,
            Name = rule.Name,
            RuleType = rule.RuleType,
            DataType = rule.DataType,
            MinValue = rule.MinValue,
            MaxValue = rule.MaxValue,
            Level = rule.Level,
            IsActive = rule.IsActive,
            DeviceId = rule.DeviceId,
            DeviceName = rule.Device?.Name,
            AreaId = rule.AreaId,
            AreaName = rule.Area?.Name,
            AppCode = rule.AppCode,
            RuleExpression = rule.RuleExpression,
            Description = rule.Description,
            CreatedAt = rule.CreatedAt,
            UpdatedAt = rule.UpdatedAt
        };
    }

    /// <summary>
    /// 删除数据规则
    /// </summary>
    public async Task DeleteDataRuleAsync(long id, string? appCode, CustomerScope? scope = null)
    {
        var rule = await _dataRuleRepository.GetByIdAsync(id);
        if (rule == null)
        {
            throw new InvalidOperationException("数据规则不存在");
        }

        // 权限检查：新路径按 scope fail-closed；旧签名仅供内部通道沿用。
        if (scope != null)
        {
            await AssertWriteAsync(scope, rule.AppCode);
        }
        else if (!string.IsNullOrEmpty(appCode) && rule.AppCode != appCode)
        {
            throw new UnauthorizedAccessException("无权删除该数据规则");
        }

        await _dataRuleRepository.DeleteAsync(rule);
        await _unitOfWork.SaveChangesAsync();
    }

    /// <summary>
    /// 执行规则
    /// </summary>
    public async Task<bool> ExecuteRuleAsync(DataRule rule, DeviceDataRecord data)
    {
        return await Task.FromResult(_ruleEngine.ExecuteRule(rule, data));
    }
}
