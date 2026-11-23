using System.Linq.Expressions;
using IoTPlatform.Data.Repositories.Interfaces;
using IoTPlatform.Services.Guards;
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

    /// <summary>旧签名逐字保留；内部/下级路径零回归。</summary>
    public IQueryable<T> GetQueryable(string? appCode = null, List<long>? allowedAreaIds = null)
    {
        return ApplyFilters(_dbSet.AsQueryable(), appCode, allowedAreaIds);
    }

    /// <summary>
    /// B4/M5：携带 Multi 授权客户集合的查询入口。
    /// <b>不把它当兜底</b>——appCode 为空而 customerIds 也为空时保持零过滤（超管语义），
    /// admin 的范围必须由 Controller 每次请求显式下推。
    /// </summary>
    public IQueryable<T> GetQueryable(
        string? appCode,
        List<long>? allowedAreaIds,
        IReadOnlyCollection<long>? customerIds,
        IReadOnlyCollection<string>? appCodes = null)
    {
        return ApplyFilters(_dbSet.AsQueryable(), appCode, allowedAreaIds, customerIds, appCodes);
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

                    // 使用 Enumerable.Contains<long>(IEnumerable<long>, long) 静态泛型方法构造表达式树。
                    // 原因：Expression.Call(typeof(List<long>).GetMethod("Contains",...), constant, value)
                    // 会被解析为「实例方法 + 实例参数」，而反射对该方法判定为静态，导致
                    // "Static method requires null instance" 异常，使所有携带 AllowedAreaIds 声明的租户
                    // 请求 GET /devices 直接 500。改用 Enumerable.Contains<long> 静态泛型方法后，
                    // EF Core 可将其正确翻译为 SQL IN 子句，且不再有实例/静态歧义。
                    var containsMethod = typeof(Enumerable)
                        .GetMethods()
                        .First(m => m.Name == "Contains" && m.GetParameters().Length == 2)
                        .MakeGenericMethod(typeof(long));
                    var contains = Expression.Call(containsMethod, constant, value);
                    var and = Expression.AndAlso(hasValue, contains);
                    var lambda = Expression.Lambda<Func<T, bool>>(and, parameter);
                    
                    query = query.Where(lambda);
                }
            }
        }
        
        return query;
    }

    /// <summary>
    /// B4/M5：在既有 AppCode / 区域过滤之上叠加授权客户集合过滤（CustomerId IN 或 AppCode IN）。
    /// 旧重载委托本方法并传 null customerIds，行为逐字不变。
    /// </summary>
    protected virtual IQueryable<T> ApplyFilters(
        IQueryable<T> query,
        string? appCode,
        List<long>? allowedAreaIds,
        IReadOnlyCollection<long>? customerIds,
        IReadOnlyCollection<string>? appCodes = null)
    {
        // 先沿用原有 AppCode / 区域过滤（下级 claim 单值路径零回归）。
        query = ApplyFilters(query, appCode, allowedAreaIds);

        // Multi：customerIds 来自 DB 授权集合，必须真正下推；
        // All：customerIds=null 且 appCode=null → ApplyQueryScope 不加任何 Where。
        return query.ApplyQueryScope(appCode, customerIds, appCodes);
    }
}
