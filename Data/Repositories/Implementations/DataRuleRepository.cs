using IoTPlatform.Data.Repositories.Interfaces;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;

namespace IoTPlatform.Data.Repositories.Implementations;

/// <summary>
/// 数据规则仓储实现类
/// </summary>
public class DataRuleRepository : Repository<DataRule>, IDataRuleRepository
{
    public DataRuleRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<DataRule>> GetByRuleTypeAsync(string ruleType, string? appCode = null)
    {
        var query = ApplyFilters(_context.DataRules.AsQueryable(), appCode, null);
        return await query
            .Where(r => r.RuleType == ruleType)
            .Include(r => r.Device)
            .Include(r => r.Area)
            .ToListAsync();
    }

    public async Task<IEnumerable<DataRule>> GetByDataTypeAsync(string dataType, string? appCode = null)
    {
        var query = ApplyFilters(_context.DataRules.AsQueryable(), appCode, null);
        return await query
            .Where(r => r.DataType == dataType)
            .Include(r => r.Device)
            .Include(r => r.Area)
            .ToListAsync();
    }

    public async Task<IEnumerable<DataRule>> GetByDeviceIdAsync(long deviceId, string? appCode = null)
    {
        var query = ApplyFilters(_context.DataRules.AsQueryable(), appCode, null);
        return await query
            .Where(r => r.DeviceId == deviceId)
            .Include(r => r.Device)
            .Include(r => r.Area)
            .ToListAsync();
    }

    public async Task<IEnumerable<DataRule>> GetByAreaIdAsync(long areaId, string? appCode = null)
    {
        var query = ApplyFilters(_context.DataRules.AsQueryable(), appCode, null);
        return await query
            .Where(r => r.AreaId == areaId)
            .Include(r => r.Device)
            .Include(r => r.Area)
            .ToListAsync();
    }

    public async Task<IEnumerable<DataRule>> GetActiveRulesAsync(string? appCode = null)
    {
        var query = ApplyFilters(_context.DataRules.AsQueryable(), appCode, null);
        return await query
            .Where(r => r.IsActive)
            .Include(r => r.Device)
            .Include(r => r.Area)
            .ToListAsync();
    }

    public async Task UpdateActiveStatusAsync(long ruleId, bool isActive)
    {
        var rule = await _context.DataRules.FindAsync(ruleId);
        if (rule != null)
        {
            rule.IsActive = isActive;
            rule.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    public async Task<bool> NameExistsAsync(string name, string? appCode = null, long? excludeRuleId = null)
    {
        var query = ApplyFilters(_context.DataRules.AsQueryable(), appCode, null);
        
        query = query.Where(r => r.Name == name);
        
        if (excludeRuleId.HasValue)
        {
            query = query.Where(r => r.Id != excludeRuleId.Value);
        }
        
        return await query.AnyAsync();
    }

    public async Task<IEnumerable<DataRule>> GetByLevelAsync(string level, string? appCode = null)
    {
        var query = ApplyFilters(_context.DataRules.AsQueryable(), appCode, null);
        return await query
            .Where(r => r.Level == level)
            .Include(r => r.Device)
            .Include(r => r.Area)
            .ToListAsync();
    }

    public async Task<(int TotalRules, int ActiveRules, int CriticalRules)> GetDataRuleStatsAsync(string? appCode = null)
    {
        var query = ApplyFilters(_context.DataRules.AsQueryable(), appCode, null);
        var rules = await query.ToListAsync();
        
        return (
            rules.Count,
            rules.Count(r => r.IsActive),
            rules.Count(r => r.Level == "critical")
        );
    }
}