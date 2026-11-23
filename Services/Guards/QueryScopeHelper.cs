using System.Linq.Expressions;
using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace IoTPlatform.Services.Guards;

/// <summary>
/// B4/M5 列表范围下推的值对象（设计 §2.6）。
///
/// <para><b>All</b>：三项皆空 = 不加任何过滤；<b>Multi</b>：CustomerIds/AppCodes 来自每次请求的 DB 授权集合；
/// <b>Single</b>：只使用 JWT 单值 AppCode（下级零回归）。</para>
///
/// <para>刻意保留 <see cref="AppCode"/> / <see cref="CustomerIds"/> 两个设计字段名，
/// 同时携带 <see cref="AppCodes"/> 以支撑"只有 AppCode 列、没有 CustomerId 列"的实体在 Multi 分支下也能下推。</para>
/// </summary>
/// <param name="AppCode">下级 claim 单值；All/Multi 为 null。</param>
/// <param name="CustomerIds">Multi 的权威客户 ID 集合；All/Single 为 null。</param>
/// <param name="AppCodes">Multi 的 AppCode 集合（由 DB 授权客户反查）；All 为空。</param>
public sealed record ListQueryScope(
    string? AppCode,
    IReadOnlyList<long>? CustomerIds,
    IReadOnlyList<string>? AppCodes)
{
    /// <summary>超管全量范围（不过滤）。</summary>
    public static ListQueryScope All => new(null, null, null);

    /// <summary>是否受限范围（需要下推过滤）。</summary>
    public bool IsRestricted => CustomerIds != null
                                || (AppCodes?.Count ?? 0) > 0
                                || !string.IsNullOrEmpty(AppCode);

    /// <summary>按 CustomerScope 三分支构造列表范围。</summary>
    public static ListQueryScope Create(CustomerScope scope, string? claimAppCode)
    {
        return scope.Mode switch
        {
            CustomerScopeMode.All => All,
            CustomerScopeMode.Multi => new ListQueryScope(null, scope.CustomerIds, scope.AppCodes),
            _ => new ListQueryScope(
                string.IsNullOrEmpty(claimAppCode) ? scope.AppCodes.FirstOrDefault() : claimAppCode,
                null,
                scope.AppCodes)
        };
    }
}

/// <summary>
/// 列表范围解析入口。<b>显式</b>接收 principal 与 ICustomerScopeService，
/// 绝不使用 AsyncLocal / HttpContext 隐式 principal（设计铁律 1）。
/// </summary>
public static class ListScopeResolver
{
    /// <summary>
    /// 从当前 principal 解析列表范围。scopeService 为 null 仅出现在单测显式传 null 的兼容构造中，
    /// 此时退化为原 claim 行为（下级零回归），生产 DI 永不为空。
    /// </summary>
    public static async Task<ListQueryScope> ResolveAsync(
        ICustomerScopeService? scopeService,
        ClaimsPrincipal principal)
    {
        var claimAppCode = principal?.FindFirst("AppCode")?.Value;
        if (scopeService == null)
        {
            return new ListQueryScope(claimAppCode, null, null);
        }

        var scope = await scopeService.ResolveAsync(principal).ConfigureAwait(false);
        return ListQueryScope.Create(scope, claimAppCode);
    }

    /// <summary>Controller 便捷入口：等价于 <see cref="ResolveAsync"/>。</summary>
    public static Task<ListQueryScope> ResolveListScopeAsync(
        this ControllerBase controller,
        ICustomerScopeService? scopeService)
    {
        return ResolveAsync(scopeService, controller.User);
    }
}

/// <summary>
/// IQueryable 通用范围下推：用表达式树按实体的 CustomerId / AppCode 属性生成 SQL WHERE，
/// 避免为每个 Service 复制一份 Contains 模板；无可用归属列且范围受限时 fail-closed（空结果）。
/// </summary>
public static class QueryableScopeExtensions
{
    /// <summary>按列表范围对象下推。</summary>
    public static IQueryable<T> ApplyQueryScope<T>(this IQueryable<T> query, ListQueryScope scope)
    {
        return query.ApplyQueryScope(scope.AppCode, scope.CustomerIds, scope.AppCodes);
    }

    /// <summary>
    /// 按 (appCode, customerIds, appCodes) 下推；三者语义与设计 §2.6 完全一致：
    /// customerIds 非空 → CustomerId IN；否则 appCodes/appCode 非空 → AppCode IN/=；
    /// 三者皆空（超管）→ 原查询逐字不变。
    /// </summary>
    public static IQueryable<T> ApplyQueryScope<T>(
        this IQueryable<T> query,
        string? appCode,
        IReadOnlyCollection<long>? customerIds,
        IReadOnlyCollection<string>? appCodes = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        var entityType = typeof(T);

        if (customerIds != null)
        {
            var customerIdProperty = entityType.GetProperty("CustomerId", BindingFlags.Public | BindingFlags.Instance);
            if (customerIdProperty != null && IsLongCompatible(customerIdProperty.PropertyType))
            {
                return query.Where(BuildCustomerIdPredicate<T>(
                    entityType, customerIdProperty, customerIds, CollectAppCodes(appCode, appCodes)));
            }

            // 没有 CustomerId 列的实体（设备/日志/配置类）用 DB 反查出的 AppCodes 下推。
            var fallbackCodes = CollectAppCodes(appCode, appCodes);
            return fallbackCodes.Count > 0
                ? query.Where(BuildAppCodePredicate<T>(entityType, fallbackCodes))
                : query.Where(static _ => false); // 受限范围但拿不到任何归属值 → fail-closed
        }

        var codes = CollectAppCodes(appCode, appCodes);
        return codes.Count > 0
            ? query.Where(BuildAppCodePredicate<T>(entityType, codes))
            : query; // 超管：零过滤
    }

    /// <summary>
    /// 配置/字典类列表专用：共享系统行（AppCode 为空 / "system" / IsSystem=true）对所有角色可读，
    /// 其余行按 scope 下推。不能泛化到工单/设备/用户（它们的 AppCode=null 是归属缺陷）。
    /// </summary>
    public static IQueryable<T> ApplyQueryScopeWithShared<T>(
        this IQueryable<T> query,
        string? appCode,
        IReadOnlyCollection<long>? customerIds,
        IReadOnlyCollection<string>? appCodes = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        var entityType = typeof(T);
        var scopePredicate = BuildScopePredicate<T>(entityType, appCode, customerIds, appCodes);
        if (scopePredicate == null)
        {
            return query; // 超管全量（共享行自然包含）
        }

        var sharedPredicate = BuildSharedPredicate<T>(entityType);
        if (sharedPredicate == null)
        {
            return query.Where(scopePredicate);
        }

        var scopeParam = scopePredicate.Parameters[0];
        var sharedBody = new ParameterReplaceVisitor(sharedPredicate.Parameters[0], scopeParam)
            .Visit(sharedPredicate.Body)!;
        var combined = Expression.Lambda<Func<T, bool>>(
            Expression.OrElse(sharedBody, scopePredicate.Body), scopeParam);
        return query.Where(combined);
    }

    /// <summary>构造纯范围谓词；超管（无范围）返回 null。</summary>
    private static Expression<Func<T, bool>>? BuildScopePredicate<T>(
        Type entityType,
        string? appCode,
        IReadOnlyCollection<long>? customerIds,
        IReadOnlyCollection<string>? appCodes)
    {
        if (customerIds != null)
        {
            var customerIdProperty = entityType.GetProperty("CustomerId", BindingFlags.Public | BindingFlags.Instance);
            if (customerIdProperty != null && IsLongCompatible(customerIdProperty.PropertyType))
            {
                return BuildCustomerIdPredicate<T>(
                    entityType, customerIdProperty, customerIds, CollectAppCodes(appCode, appCodes));
            }

            var fallbackCodes = CollectAppCodes(appCode, appCodes);
            return fallbackCodes.Count > 0
                ? BuildAppCodePredicate<T>(entityType, fallbackCodes)
                : (Expression<Func<T, bool>>)(_ => false);
        }

        var codes = CollectAppCodes(appCode, appCodes);
        return codes.Count > 0 ? BuildAppCodePredicate<T>(entityType, codes) : null;
    }

    /// <summary>共享行谓词：AppCode 为空 / "system" / IsSystem=true。</summary>
    private static Expression<Func<T, bool>>? BuildSharedPredicate<T>(Type entityType)
    {
        var parameter = Expression.Parameter(entityType, "e");
        var appCodeProperty = entityType.GetProperty("AppCode", BindingFlags.Public | BindingFlags.Instance);
        var isSystemProperty = entityType.GetProperty("IsSystem", BindingFlags.Public | BindingFlags.Instance);
        Expression? body = null;

        if (appCodeProperty != null && appCodeProperty.PropertyType == typeof(string))
        {
            var member = Expression.Property(parameter, appCodeProperty);
            body = Expression.OrElse(
                Expression.Equal(member, Expression.Constant(null, typeof(string))),
                Expression.Equal(member, Expression.Constant("system")));
        }
        if (isSystemProperty != null && isSystemProperty.PropertyType == typeof(bool))
        {
            var system = Expression.Equal(Expression.Property(parameter, isSystemProperty), Expression.Constant(true));
            body = body == null ? system : Expression.OrElse(body, system);
        }
        return body == null ? null : Expression.Lambda<Func<T, bool>>(body, parameter);
    }

    /// <summary>把共享谓词的参数替换为范围谓词的参数，避免 EF 无法翻译 Invoke。</summary>
    private sealed class ParameterReplaceVisitor : ExpressionVisitor
    {
        private readonly ParameterExpression _from;
        private readonly ParameterExpression _to;

        public ParameterReplaceVisitor(ParameterExpression from, ParameterExpression to)
        {
            _from = from;
            _to = to;
        }

        protected override Expression VisitParameter(ParameterExpression node)
            => node == _from ? _to : base.VisitParameter(node);
    }

    private static List<string> CollectAppCodes(string? appCode, IReadOnlyCollection<string>? appCodes)
    {
        var result = new List<string>();
        if (appCodes != null)
        {
            result.AddRange(appCodes.Where(c => !string.IsNullOrWhiteSpace(c)));
        }
        if (!string.IsNullOrWhiteSpace(appCode))
        {
            result.Add(appCode);
        }
        return result.Distinct(StringComparer.Ordinal).ToList();
    }

    private static bool IsLongCompatible(Type type)
    {
        return type == typeof(long) || type == typeof(long?)
               || type == typeof(int) || type == typeof(int?);
    }

    /// <summary>
    /// Multi 分支谓词：<c>(CustomerId IN 授权集合) OR (CustomerId IS NULL AND AppCode IN 授权 AppCode 集合)</c>。
    /// 与详情判据 CanAccessOwnedEntityAsync 同源——后者在实体 CustomerId 为空时按 AppCode 反查客户再比授权集合。
    /// 两个集合都来自 scope（DB 权威），越集行（CustomerId 不在集合、AppCode 也不在集合）必然被过滤。
    /// </summary>
    private static Expression<Func<T, bool>> BuildCustomerIdPredicate<T>(
        Type entityType,
        PropertyInfo property,
        IReadOnlyCollection<long> customerIds,
        IReadOnlyCollection<string> authorizedAppCodes)
    {
        var parameter = Expression.Parameter(entityType, "e");
        Expression member = Expression.Property(parameter, property);
        bool nullableCustomerId = Nullable.GetUnderlyingType(property.PropertyType) != null;
        if (nullableCustomerId)
        {
            member = Expression.Property(member, "Value");
        }
        if (member.Type != typeof(long))
        {
            member = Expression.Convert(member, typeof(long));
        }

        var ids = customerIds.Distinct().ToList();
        Expression idContains = Expression.Call(
            typeof(Enumerable),
            nameof(Enumerable.Contains),
            new[] { typeof(long) },
            Expression.Constant(ids),
            member);
        if (nullableCustomerId)
        {
            // 先判空再取 .Value：InMemory Provider 会逐行解释表达式树，直接对 NULL 取 Value 会抛
            // "Nullable object must have a value"；同时该条件与 SQL 的 IS NOT NULL 语义一致。
            idContains = Expression.AndAlso(
                Expression.Property(Expression.Property(parameter, property), "HasValue"),
                idContains);
        }
        Expression body = idContains;

        if (authorizedAppCodes.Count > 0 && nullableCustomerId)
        {
            var appCodeProperty = entityType.GetProperty("AppCode", BindingFlags.Public | BindingFlags.Instance);
            if (appCodeProperty != null && appCodeProperty.PropertyType == typeof(string))
            {
                var appCodeMember = Expression.Property(parameter, appCodeProperty);
                Expression? appCodeEquals = null;
                foreach (var code in authorizedAppCodes.Distinct(StringComparer.Ordinal))
                {
                    var equals = Expression.Equal(appCodeMember, Expression.Constant(code));
                    appCodeEquals = appCodeEquals == null ? equals : Expression.OrElse(appCodeEquals, equals);
                }
                if (appCodeEquals != null)
                {
                    var customerIdIsNull = Expression.Equal(
                        Expression.Property(Expression.Property(parameter, property), "HasValue"),
                        Expression.Constant(false));
                    body = Expression.OrElse(body, Expression.AndAlso(customerIdIsNull, appCodeEquals));
                }
            }
        }

        return Expression.Lambda<Func<T, bool>>(body, parameter);
    }

    private static Expression<Func<T, bool>> BuildAppCodePredicate<T>(
        Type entityType,
        IReadOnlyCollection<string> appCodes)
    {
        var parameter = Expression.Parameter(entityType, "e");
        var property = entityType.GetProperty("AppCode", BindingFlags.Public | BindingFlags.Instance);
        if (property == null || property.PropertyType != typeof(string))
        {
            // 受限范围但实体无 AppCode 列：fail-closed（不返回任何行）。
            return Expression.Lambda<Func<T, bool>>(Expression.Constant(false), parameter);
        }

        Expression member = Expression.Property(parameter, property);
        Expression? body = null;
        foreach (var code in appCodes.Distinct(StringComparer.Ordinal))
        {
            var equals = Expression.Equal(member, Expression.Constant(code));
            body = body == null ? equals : Expression.OrElse(body, equals);
        }
        body ??= Expression.Constant(false);
        return Expression.Lambda<Func<T, bool>>(body, parameter);
    }
}
