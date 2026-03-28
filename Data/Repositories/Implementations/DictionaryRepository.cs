using IoTPlatform.Data.Repositories.Interfaces;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;

namespace IoTPlatform.Data.Repositories.Implementations;

/// <summary>
/// 字典仓储实现类
/// </summary>
public class DictionaryRepository : Repository<DictionaryItem>, IDictionaryRepository
{
    public DictionaryRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<DictionaryTypeConfig?> GetDictionaryTypeByCodeAsync(string code, string? appCode = null)
    {
        var query = _context.DictionaryTypeConfigs.AsQueryable();

        if (!string.IsNullOrEmpty(appCode))
        {
            query = query.Where(d => d.AppCode == appCode);
        }

        return await query.FirstOrDefaultAsync(d => d.Code == code);
    }

    public async Task<IEnumerable<DictionaryTypeConfig>> GetDictionaryTypesAsync(string? appCode = null)
    {
        var query = _context.DictionaryTypeConfigs.AsQueryable();

        if (!string.IsNullOrEmpty(appCode))
        {
            query = query.Where(d => d.AppCode == appCode);
        }

        return await query
            .Where(d => d.IsActive)
            .OrderBy(d => d.SortOrder)
            .ThenBy(d => d.Id)
            .ToListAsync();
    }

    public async Task<IEnumerable<DictionaryItem>> GetByTypeCodeAsync(string typeCode, string? appCode = null)
    {
        var query = ApplyFilters(_context.DictionaryItems.AsQueryable(), appCode, null);
        return await query
            .Where(d => d.Type == typeCode)
            .OrderBy(d => d.Sort)
            .ThenBy(d => d.Id)
            .ToListAsync();
    }

    public async Task<DictionaryItem?> GetByTypeAndCodeAsync(string typeCode, string itemCode, string? appCode = null)
    {
        var query = ApplyFilters(_context.DictionaryItems.AsQueryable(), appCode, null);
        return await query
            .Where(d => d.Type == typeCode && d.Code == itemCode)
            .FirstOrDefaultAsync();
    }

    public async Task<IEnumerable<DictionaryItem>> GetByStatusAsync(string status, string? appCode = null)
    {
        var query = ApplyFilters(_context.DictionaryItems.AsQueryable(), appCode, null);
        return await query
            .Where(d => d.Status == status)
            .OrderBy(d => d.Type)
            .ThenBy(d => d.Sort)
            .ThenBy(d => d.Id)
            .ToListAsync();
    }

    public async Task<bool> TypeCodeExistsAsync(string code, string? appCode = null, long? excludeTypeId = null)
    {
        var query = _context.DictionaryTypeConfigs.AsQueryable();

        if (!string.IsNullOrEmpty(appCode))
        {
            query = query.Where(d => d.AppCode == appCode);
        }

        query = query.Where(d => d.Code == code);

        if (excludeTypeId.HasValue)
        {
            query = query.Where(d => d.Id != excludeTypeId.Value);
        }

        return await query.AnyAsync();
    }

    public async Task<bool> ItemCodeExistsAsync(string typeCode, string itemCode, string? appCode = null, long? excludeItemId = null)
    {
        var query = ApplyFilters(_context.DictionaryItems.AsQueryable(), appCode, null);

        query = query.Where(d => d.Type == typeCode && d.Code == itemCode);

        if (excludeItemId.HasValue)
        {
            query = query.Where(d => d.Id != excludeItemId.Value);
        }

        return await query.AnyAsync();
    }

    public async Task<IEnumerable<DictionaryItem>> GetActiveItemsByTypeAsync(string typeCode, string? appCode = null)
    {
        var query = ApplyFilters(_context.DictionaryItems.AsQueryable(), appCode, null);
        return await query
            .Where(d => d.Type == typeCode && d.Status == "active")
            .OrderBy(d => d.Sort)
            .ThenBy(d => d.Id)
            .ToListAsync();
    }

    public async Task UpdateItemStatusAsync(long itemId, string status)
    {
        var item = await _context.DictionaryItems.FindAsync(itemId);
        if (item != null)
        {
            item.Status = status;
            item.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    public async Task UpdateTypeStatusAsync(long typeId, bool isActive)
    {
        var type = await _context.DictionaryTypeConfigs.FindAsync(typeId);
        if (type != null)
        {
            type.IsActive = isActive;
            type.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    public async Task<DictionaryStats> GetDictionaryStatsAsync(string? appCode = null)
    {
        var typeQuery = _context.DictionaryTypeConfigs.AsQueryable();
        var itemQuery = ApplyFilters(_context.DictionaryItems.AsQueryable(), appCode, null);

        if (!string.IsNullOrEmpty(appCode))
        {
            typeQuery = typeQuery.Where(d => d.AppCode == appCode);
        }

        var types = await typeQuery.ToListAsync();
        var items = await itemQuery.ToListAsync();

        return new DictionaryStats
        {
            TotalTypes = types.Count,
            ActiveTypes = types.Count(d => d.IsActive),
            TotalItems = items.Count,
            ActiveItems = items.Count(d => d.Status == "active"),
            InactiveItems = items.Count(d => d.Status != "active"),
            LastUpdate = DateTime.UtcNow
        };
    }

    public async Task<IEnumerable<DictionaryItem>> GetByTypeAsync(string type, string? appCode = null)
    {
        var query = ApplyFilters(_context.DictionaryItems.AsQueryable(), appCode, null);
        return await query
            .Where(d => d.Type == type && d.Status == "active")
            .OrderBy(d => d.Sort)
            .ThenBy(d => d.Id)
            .ToListAsync();
    }

    public async Task<IEnumerable<DictionaryItem>> GetByTypesAsync(IEnumerable<string> types, string? appCode = null)
    {
        var query = ApplyFilters(_context.DictionaryItems.AsQueryable(), appCode, null);
        return await query
            .Where(d => types.Contains(d.Type) && d.Status == "active")
            .OrderBy(d => d.Sort)
            .ThenBy(d => d.Id)
            .ToListAsync();
    }

    public async Task<DictionaryItem?> GetByCodeAsync(string code, string type, string? appCode = null)
    {
        var query = ApplyFilters(_context.DictionaryItems.AsQueryable(), appCode, null);
        return await query
            .Where(d => d.Code == code && d.Type == type && d.Status == "active")
            .FirstOrDefaultAsync();
    }

    public async Task<bool> CodeExistsAsync(string code, string type, string? appCode = null, long? excludeItemId = null)
    {
        var query = ApplyFilters(_context.DictionaryItems.AsQueryable(), appCode, null);

        query = query.Where(d => d.Code == code && d.Type == type);

        if (excludeItemId.HasValue)
        {
            query = query.Where(d => d.Id != excludeItemId.Value);
        }

        return await query.AnyAsync();
    }

    public async Task<IEnumerable<DictionaryItem>> SearchItemsAsync(string keyword, string? appCode = null)
    {
        var query = ApplyFilters(_context.DictionaryItems.AsQueryable(), appCode, null);
        return await query
            .Where(d => d.Status == "active" &&
                (d.Name.Contains(keyword) ||
                 d.Code.Contains(keyword) ||
                 d.Description != null && d.Description.Contains(keyword)))
            .OrderBy(d => d.Type)
            .ThenBy(d => d.Sort)
            .ToListAsync();
    }

    public async Task<int> GetItemCountByTypeAsync(string type, string? appCode = null)
    {
        var query = ApplyFilters(_context.DictionaryItems.AsQueryable(), appCode, null);
        return await query.CountAsync(d => d.Type == type && d.Status == "active");
    }
}
