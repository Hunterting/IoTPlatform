using System.Linq.Expressions;

namespace IoTPlatform.Data.Repositories.Interfaces;

/// <summary>
/// 泛型仓储接口
/// </summary>
/// <typeparam name="T">实体类型</typeparam>
public interface IRepository<T> where T : class
{
    /// <summary>
    /// 根据ID获取实体
    /// </summary>
    /// <param name="id">实体ID</param>
    /// <param name="includeProperties">要包含的导航属性</param>
    /// <param name="appCode">应用代码（用于多租户过滤）</param>
    /// <param name="allowedAreaIds">允许访问的区域ID列表（用于区域权限过滤）</param>
    /// <returns>实体对象或null</returns>
    Task<T?> GetByIdAsync(long id, string[]? includeProperties = null, string? appCode = null, List<long>? allowedAreaIds = null);
    
    /// <summary>
    /// 获取所有实体
    /// </summary>
    /// <param name="includeProperties">要包含的导航属性</param>
    /// <param name="appCode">应用代码（用于多租户过滤）</param>
    /// <param name="allowedAreaIds">允许访问的区域ID列表（用于区域权限过滤）</param>
    /// <returns>实体列表</returns>
    Task<IEnumerable<T>> GetAllAsync(string[]? includeProperties = null, string? appCode = null, List<long>? allowedAreaIds = null);
    
    /// <summary>
    /// 根据条件查询实体
    /// </summary>
    /// <param name="predicate">查询条件</param>
    /// <param name="includeProperties">要包含的导航属性</param>
    /// <param name="appCode">应用代码（用于多租户过滤）</param>
    /// <param name="allowedAreaIds">允许访问的区域ID列表（用于区域权限过滤）</param>
    /// <returns>符合条件的实体列表</returns>
    Task<IEnumerable<T>> GetAsync(
        Expression<Func<T, bool>> predicate, 
        string[]? includeProperties = null, 
        string? appCode = null, 
        List<long>? allowedAreaIds = null);
    
    /// <summary>
    /// 分页查询实体
    /// </summary>
    /// <param name="pageNumber">页码</param>
    /// <param name="pageSize">每页大小</param>
    /// <param name="predicate">查询条件</param>
    /// <param name="orderBy">排序字段</param>
    /// <param name="isDescending">是否降序</param>
    /// <param name="includeProperties">要包含的导航属性</param>
    /// <param name="appCode">应用代码（用于多租户过滤）</param>
    /// <param name="allowedAreaIds">允许访问的区域ID列表（用于区域权限过滤）</param>
    /// <returns>分页结果</returns>
    Task<(IEnumerable<T> Items, int TotalCount, int TotalPages)> GetPagedAsync(
        int pageNumber = 1, 
        int pageSize = 10, 
        Expression<Func<T, bool>>? predicate = null,
        Expression<Func<T, object>>? orderBy = null,
        bool isDescending = false,
        string[]? includeProperties = null,
        string? appCode = null,
        List<long>? allowedAreaIds = null);
    
    /// <summary>
    /// 获取查询对象（可用于进一步LINQ查询）
    /// </summary>
    /// <param name="appCode">应用代码（用于多租户过滤）</param>
    /// <param name="allowedAreaIds">允许访问的区域ID列表（用于区域权限过滤）</param>
    /// <returns>IQueryable查询对象</returns>
    IQueryable<T> GetQueryable(string? appCode = null, List<long>? allowedAreaIds = null);

    /// <summary>
    /// 获取查询对象（GetQueryable的别名方法）
    /// </summary>
    /// <param name="appCode">应用代码（用于多租户过滤）</param>
    /// <param name="allowedAreaIds">允许访问的区域ID列表（用于区域权限过滤）</param>
    /// <returns>IQueryable查询对象</returns>
    IQueryable<T> Query(string? appCode = null, List<long>? allowedAreaIds = null) => GetQueryable(appCode, allowedAreaIds);
    
    /// <summary>
    /// 添加实体
    /// </summary>
    /// <param name="entity">要添加的实体</param>
    /// <returns>添加后的实体</returns>
    Task<T> AddAsync(T entity);
    
    /// <summary>
    /// 批量添加实体
    /// </summary>
    /// <param name="entities">实体集合</param>
    Task AddRangeAsync(IEnumerable<T> entities);
    
    /// <summary>
    /// 更新实体
    /// </summary>
    /// <param name="entity">要更新的实体</param>
    Task UpdateAsync(T entity);
    
    /// <summary>
    /// 批量更新实体
    /// </summary>
    /// <param name="entities">实体集合</param>
    Task UpdateRangeAsync(IEnumerable<T> entities);
    
    /// <summary>
    /// 删除实体
    /// </summary>
    /// <param name="entity">要删除的实体</param>
    Task DeleteAsync(T entity);
    
    /// <summary>
    /// 根据ID删除实体
    /// </summary>
    /// <param name="id">实体ID</param>
    /// <param name="appCode">应用代码（用于多租户过滤）</param>
    /// <param name="allowedAreaIds">允许访问的区域ID列表（用于区域权限过滤）</param>
    /// <returns>是否删除成功</returns>
    Task<bool> DeleteByIdAsync(long id, string? appCode = null, List<long>? allowedAreaIds = null);
    
    /// <summary>
    /// 批量删除实体
    /// </summary>
    /// <param name="entities">实体集合</param>
    Task DeleteRangeAsync(IEnumerable<T> entities);
    
    /// <summary>
    /// 检查是否存在符合条件的实体
    /// </summary>
    /// <param name="predicate">查询条件</param>
    /// <param name="appCode">应用代码（用于多租户过滤）</param>
    /// <param name="allowedAreaIds">允许访问的区域ID列表（用于区域权限过滤）</param>
    /// <returns>是否存在</returns>
    Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, string? appCode = null, List<long>? allowedAreaIds = null);
    
    /// <summary>
    /// 获取符合条件的实体数量
    /// </summary>
    /// <param name="predicate">查询条件</param>
    /// <param name="appCode">应用代码（用于多租户过滤）</param>
    /// <param name="allowedAreaIds">允许访问的区域ID列表（用于区域权限过滤）</param>
    /// <returns>实体数量</returns>
    Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null, string? appCode = null, List<long>? allowedAreaIds = null);
}
