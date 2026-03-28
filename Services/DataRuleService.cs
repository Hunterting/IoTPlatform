using IoTPlatform.Data.Repositories.Interfaces;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Helpers;
using IoTPlatform.Models;
using IoTPlatform.Services.Rules;
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

    public DataRuleService(
        IDataRuleRepository dataRuleRepository,
        IDeviceRepository deviceRepository,
        IAreaRepository areaRepository,
        IUnitOfWork unitOfWork)
    {
        _dataRuleRepository = dataRuleRepository;
        _deviceRepository = deviceRepository;
        _areaRepository = areaRepository;
        _unitOfWork = unitOfWork;
        _ruleEngine = new RuleEngine();
    }

    /// <summary>
    /// 获取数据规则列表
    /// </summary>
    public async Task<PagedResponse<DataRuleDto>> GetDataRulesAsync(int page, int pageSize, string? keyword, string? ruleType, string? appCode)
    {
        var query = _dataRuleRepository.Query()
            .Include(d => d.Device)
            .Include(d => d.Area);

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
        var query = _dataRuleRepository.Query()
            .Include(d => d.Device)
            .Include(d => d.Area);

        // 租户数据隔离
        if (!string.IsNullOrEmpty(appCode))
        {
            query = query.Where(d => d.AppCode == appCode);
        }

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
    public async Task<DataRuleDto> CreateDataRuleAsync(CreateDataRuleRequest request)
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

        await _dataRuleRepository.AddAsync(rule);
        await _unitOfWork.SaveChangesAsync();

        // 重新加载以包含关联数据
        rule = await _dataRuleRepository.Query()
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
    public async Task<DataRuleDto> UpdateDataRuleAsync(long id, UpdateDataRuleRequest request, string? appCode)
    {
        var rule = await _dataRuleRepository.Query()
            .Include(d => d.Device)
            .Include(d => d.Area)
            .FirstOrDefaultAsync(d => d.Id == id);
        if (rule == null)
        {
            throw new InvalidOperationException("数据规则不存在");
        }

        // 权限检查
        if (!string.IsNullOrEmpty(appCode) && rule.AppCode != appCode)
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
    public async Task DeleteDataRuleAsync(long id, string? appCode)
    {
        var rule = await _dataRuleRepository.GetByIdAsync(id);
        if (rule == null)
        {
            throw new InvalidOperationException("数据规则不存在");
        }

        // 权限检查
        if (!string.IsNullOrEmpty(appCode) && rule.AppCode != appCode)
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
