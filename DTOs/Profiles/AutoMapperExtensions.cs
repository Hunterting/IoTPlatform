using AutoMapper;
using AutoMapper.QueryableExtensions;
using Microsoft.EntityFrameworkCore;

namespace IoTPlatform.DTOs.Profiles;

/// <summary>
/// AutoMapper扩展方法
/// </summary>
public static class AutoMapperExtensions
{
    /// <summary>
    /// 将查询投影到DTO（支持分页）
    /// </summary>
    /// <typeparam name="TSource">源类型</typeparam>
    /// <typeparam name="TDestination">目标类型</typeparam>
    /// <param name="query">查询对象</param>
    /// <param name="mapper">AutoMapper实例</param>
    /// <param name="pageNumber">页码</param>
    /// <param name="pageSize">每页大小</param>
    /// <returns>分页结果</returns>
    public static async Task<(IEnumerable<TDestination> Items, int TotalCount, int TotalPages)> ProjectToPagedAsync<TSource, TDestination>(
        this IQueryable<TSource> query,
        IMapper mapper,
        int pageNumber = 1,
        int pageSize = 10)
        where TDestination : class
    {
        if (pageNumber < 1) pageNumber = 1;
        if (pageSize < 1) pageSize = 10;

        var totalCount = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        var items = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ProjectTo<TDestination>(mapper.ConfigurationProvider)
            .ToListAsync();

        return (items, totalCount, totalPages);
    }

    /// <summary>
    /// 将查询投影到DTO列表
    /// </summary>
    public static async Task<IEnumerable<TDestination>> ProjectToListAsync<TSource, TDestination>(
        this IQueryable<TSource> query,
        IMapper mapper)
        where TDestination : class
    {
        return await query
            .ProjectTo<TDestination>(mapper.ConfigurationProvider)
            .ToListAsync();
    }

    /// <summary>
    /// 将查询投影到单个DTO
    /// </summary>
    public static async Task<TDestination?> ProjectToSingleOrDefaultAsync<TSource, TDestination>(
        this IQueryable<TSource> query,
        IMapper mapper)
        where TDestination : class
    {
        return await query
            .ProjectTo<TDestination>(mapper.ConfigurationProvider)
            .SingleOrDefaultAsync();
    }

    /// <summary>
    /// 将查询投影到单个DTO（如果找不到则返回默认值）
    /// </summary>
    public static async Task<TDestination?> ProjectToFirstOrDefaultAsync<TSource, TDestination>(
        this IQueryable<TSource> query,
        IMapper mapper)
        where TDestination : class
    {
        return await query
            .ProjectTo<TDestination>(mapper.ConfigurationProvider)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// 安全映射（如果源为null则返回null）
    /// </summary>
    public static TDestination? SafeMap<TSource, TDestination>(this IMapper mapper, TSource? source)
        where TDestination : class
        where TSource : class
    {
        return source == null ? null : mapper.Map<TDestination>(source);
    }

    /// <summary>
    /// 安全映射列表（如果源列表为null则返回空列表）
    /// </summary>
    public static IEnumerable<TDestination> SafeMapList<TSource, TDestination>(
        this IMapper mapper,
        IEnumerable<TSource>? source)
        where TDestination : class
        where TSource : class
    {
        return source == null ? Enumerable.Empty<TDestination>() : mapper.Map<IEnumerable<TDestination>>(source);
    }

    /// <summary>
    /// 更新现有对象（保留ID等关键字段）
    /// </summary>
    public static TDestination UpdateFrom<TSource, TDestination>(
        this TDestination destination,
        TSource source,
        IMapper mapper,
        params string[] ignoreProperties)
        where TDestination : class
        where TSource : class
    {
        var config = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<TSource, TDestination>()
                .ForAllMembers(opt => opt.Condition((src, dest, srcMember) => srcMember != null));
            
            // 忽略指定的属性
            foreach (var property in ignoreProperties)
            {
                cfg.CreateMap<TSource, TDestination>()
                    .ForMember(property, opt => opt.Ignore());
            }
        });

        var customMapper = config.CreateMapper();
        return customMapper.Map(source, destination);
    }

    /// <summary>
    /// 创建分页响应
    /// </summary>
    public static PagedResponse<TDestination> ToPagedResponse<TSource, TDestination>(
        this (IEnumerable<TSource> Items, int TotalCount, int TotalPages) pagedResult,
        IMapper mapper,
        int pageNumber,
        int pageSize)
        where TDestination : class
        where TSource : class
    {
        var items = mapper.Map<IEnumerable<TDestination>>(pagedResult.Items);
        
        return new PagedResponse<TDestination>
        {
            Items = items,
            TotalCount = pagedResult.TotalCount,
            TotalPages = pagedResult.TotalPages,
            PageNumber = pageNumber,
            PageSize = pageSize,
            HasPreviousPage = pageNumber > 1,
            HasNextPage = pageNumber < pagedResult.TotalPages
        };
    }
}

/// <summary>
/// 分页响应
/// </summary>
/// <typeparam name="T">数据类型</typeparam>
public class PagedResponse<T>
{
    public IEnumerable<T> Items { get; set; } = new List<T>();
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public bool HasPreviousPage { get; set; }
    public bool HasNextPage { get; set; }
}
