using System.Linq.Expressions;
using IoTPlatform.Data.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace IoTPlatform.Data.Repositories.Implementations;

/// <summary>
/// 泛型仓储基类
/// </summary>
/// <typeparam name="T">实体类型</typeparam>
public class Repository<T> : IRepository<T> where T : class
{
    protected readonly AppDbContext _context;
    protected readonly DbSet<T> _dbSet;

    public Repository(AppDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _dbSet = context.Set<T>();
    }

    public async Task<T?> GetByIdAsync(long id, string[]? includeProperties = null, string? appCode = null, List<long>? allowedAreaIds = null)
    {
        var query = ApplyFilters(_dbSet.AsQueryable(), appCode, allowedAreaIds);
        query = IncludeProperties(query, includeProperties);
        
        return await query.FirstOrDefaultAsync(e => EF.Property<long>(e, "Id") == id);
    }

    public async Task<IEnumerable<T>> GetAllAsync(string[]? includeProperties = null, string? appCode = null, List<long>? allowedAreaIds = null)
    {
        var query = ApplyFilters(_dbSet.AsQueryable(), appCode, allowedAreaIds);
        query = IncludeProperties(query, includeProperties);
        
        return await query.ToListAsync();
    }

    public async Task<IEnumerable<T>> GetAsync(Expression<Func<T, bool>> predicate, string[]? includeProperties = null, 
        string? appCode = null, List<long>? allowedAreaIds = null)
    {
        var query = ApplyFilters(_dbSet.AsQueryable(), appCode, allowedAreaIds);
        query = query.Where(predicate);
        query = IncludeProperties(query, includeProperties);
        
        return await query.ToListAsync();
    }

    public async Task<(IEnumerable<T> Items, int TotalCount, int TotalPages)> GetPagedAsync(
        int pageNumber = 1, int pageSize = 10, Expression<Func<T, bool>>? predicate = null,
        Expression<Func<T, object>>? orderBy = null, bool isDescending = false,
        string[]? includeProperties = null, string? appCode = null, List<long>? allowedAreaIds = null)
    {
        if (pageNumber < 1) pageNumber = 1;
        if (pageSize < 1) pageSize = 10;
        
        var query = ApplyFilters(_dbSet.AsQueryable(), appCode, allowedAreaIds);
        
        if (predicate != null)
        {
            query = query.Where(predicate);
        }
        
        query = IncludeProperties(query, includeProperties);
        
        var totalCount = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
        
        if (orderBy != null)
        {
            query = isDescending ? query.OrderByDescending(orderBy) : query.OrderBy(orderBy);
        }
        
        var items = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
        
        return (items, totalCount, totalPages);
    }

    public IQueryable<T> GetQueryable(string? appCode = null, List<long>? allowedAreaIds = null)
    {
        return ApplyFilters(_dbSet.AsQueryable(), appCode, allowedAreaIds);
    }

    public async Task<T> AddAsync(T entity)
    {
        await _dbSet.AddAsync(entity);
        await _context.SaveChangesAsync();
        return entity;
    }

    public async Task AddRangeAsync(IEnumerable<T> entities)
    {
        await _dbSet.AddRangeAsync(entities);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateAsync(T entity)
    {
        _dbSet.Update(entity);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateRangeAsync(IEnumerable<T> entities)
    {
        _dbSet.UpdateRange(entities);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(T entity)
    {
        _dbSet.Remove(entity);
        await _context.SaveChangesAsync();
    }

    public async Task<bool> DeleteByIdAsync(long id, string? appCode = null, List<long>? allowedAreaIds = null)
    {
        var entity = await GetByIdAsync(id, appCode: appCode, allowedAreaIds: allowedAreaIds);
        if (entity == null) return false;
        
        await DeleteAsync(entity);
        return true;
    }

    public async Task DeleteRangeAsync(IEnumerable<T> entities)
    {
        _dbSet.RemoveRange(entities);
        await _context.SaveChangesAsync();
    }

    public async Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, string? appCode = null, List<long>? allowedAreaIds = null)
    {
        var query = ApplyFilters(_dbSet.AsQueryable(), appCode, allowedAreaIds);
        return await query.AnyAsync(predicate);
    }

    public async Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null, string? appCode = null, List<long>? allowedAreaIds = null)
    {
        var query = ApplyFilters(_dbSet.AsQueryable(), appCode, allowedAreaIds);
        
        if (predicate != null)
        {
            query = query.Where(predicate);
        }
        
        return await query.CountAsync();
    }

    /// <summary>
    /// 包含导航属性
    /// </summary>
    protected IQueryable<T> IncludeProperties(IQueryable<T> query, string[]? includeProperties)
    {
        if (includeProperties != null)
        {
            foreach (var includeProperty in includeProperties)
            {
                query = query.Include(includeProperty);
            }
        }
        return query;
    }

    /// <summary>
    /// 应用过滤器（多租户和区域权限）
    /// </summary>
    protected virtual IQueryable<T> ApplyFilters(IQueryable<T> query, string? appCode, List<long>? allowedAreaIds)
    {
        // 应用多租户过滤
        if (!string.IsNullOrEmpty(appCode))
        {
            var entityType = _context.Model.FindEntityType(typeof(T));
            if (entityType != null)
            {
                var appCodeProperty = entityType.FindProperty("AppCode");
                if (appCodeProperty != null)
                {
                    // 使用表达式树构建AppCode过滤
                    var parameter = Expression.Parameter(typeof(T), "e");
                    var property = Expression.Property(parameter, "AppCode");
                    var constant = Expression.Constant(appCode);
                    var equals = Expression.Equal(property, constant);
                    var lambda = Expression.Lambda<Func<T, bool>>(equals, parameter);
                    
                    query = query.Where(lambda);
                }
            }
        }
        
        // 应用区域权限过滤
        if (allowedAreaIds != null && allowedAreaIds.Any())
        {
            var entityType = _context.Model.FindEntityType(typeof(T));
            if (entityType != null)
            {
                var areaIdProperty = entityType.FindProperty("AreaId");
                if (areaIdProperty != null)
                {
                    // 使用表达式树构建AreaId过滤
                    var parameter = Expression.Parameter(typeof(T), "e");
                    var property = Expression.Property(parameter, "AreaId");
                    var constant = Expression.Constant(allowedAreaIds);
                    
                    // 构建 AreaId.HasValue && allowedAreaIds.Contains(AreaId.Value)
                    var hasValue = Expression.Property(property, "HasValue");
                    var value = Expression.Property(property, "Value");
                    var contains = Expression.Call(
                        typeof(List<long>).GetMethod("Contains", new[] { typeof(long) }),
                        constant,
                        value
                    );
                    var and = Expression.AndAlso(hasValue, contains);
                    var lambda = Expression.Lambda<Func<T, bool>>(and, parameter);
                    
                    query = query.Where(lambda);
                }
            }
        }
        
        return query;
    }
}
