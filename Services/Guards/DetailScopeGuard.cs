using System.Reflection;
using System.Security.Claims;
using IoTPlatform.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IoTPlatform.Services.Guards;

/// <summary>
/// B4/M6 详情归属校验的通用点查入口（设计 §2.7）。
///
/// <para>所有 GET /{id} 先经本守卫按主键点查实体归属（CustomerId / AppCode / IsSystem），
/// 再委托 <see cref="ICustomerScopeGuard.CanAccessOwnedEntityAsync"/> 做三分支判定；
/// 越权由 Controller 映射为 HTTP 200 + ApiResponse 业务码 403。</para>
///
/// <para>点查一律 <c>IgnoreQueryFilters()</c>，不依赖启动期冻结的全局过滤器（铁律 5）。</para>
/// </summary>
public interface IDetailScopeGuard
{
    /// <summary>
    /// 校验 principal 是否可读指定实体。实体不存在时返回 true（由下游返回 404，避免信息泄露口径变化）。
    /// </summary>
    /// <typeparam name="TEntity">带 long 主键的实体类型。</typeparam>
    /// <param name="principal">当前请求 principal（显式传递）。</param>
    /// <param name="id">主键值。</param>
    /// <param name="allowShared">是否允许共享系统行（字典/配置类传 true；工单/设备/用户必须 false）。</param>
    /// <param name="ct">取消令牌。</param>
    Task<bool> CanReadAsync<TEntity>(
        ClaimsPrincipal principal,
        long id,
        bool allowShared = false,
        CancellationToken ct = default) where TEntity : class;

    /// <summary>字符串业务键实体的详情归属校验（如系统设置 Key）。</summary>
    Task<bool> CanReadByKeyAsync<TEntity>(
        ClaimsPrincipal principal,
        string key,
        string keyProperty = "Key",
        bool allowShared = false,
        CancellationToken ct = default) where TEntity : class;
}

/// <summary>
/// Controller 详情端点便捷入口：从当前 HttpContext 显式解析已注册的守卫（不使用 AsyncLocal / 隐式 principal）。
/// 单测未搭建 DI 容器时返回 true 保持旧行为；生产 DI 中守卫始终存在。
/// </summary>
public static class DetailGuardExtensions
{
    /// <summary>校验当前 principal 是否可读指定实体。</summary>
    public static async Task<bool> CanReadDetailAsync<TEntity>(
        this ControllerBase controller,
        long id,
        bool allowShared = false,
        CancellationToken ct = default) where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(controller);
        var guard = controller.HttpContext?.RequestServices?.GetService<IDetailScopeGuard>();
        if (guard == null)
        {
            return true; // 兼容未搭建 DI 的存量单测；生产环境守卫由 Program.cs 注册。
        }
        return await guard.CanReadAsync<TEntity>(controller.User, id, allowShared, ct).ConfigureAwait(false);
    }

    /// <summary>字符串业务键详情（系统设置 Key 等）的归属校验。</summary>
    public static async Task<bool> CanReadDetailByKeyAsync<TEntity>(
        this ControllerBase controller,
        string key,
        string keyProperty = "Key",
        bool allowShared = false,
        CancellationToken ct = default) where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(controller);
        var guard = controller.HttpContext?.RequestServices?.GetService<IDetailScopeGuard>();
        if (guard == null)
        {
            return true;
        }
        return await guard.CanReadByKeyAsync<TEntity>(controller.User, key, keyProperty, allowShared, ct)
            .ConfigureAwait(false);
    }
}

/// <inheritdoc />
public sealed class DetailScopeGuard : IDetailScopeGuard
{
    private readonly AppDbContext _db;
    private readonly ICustomerScopeGuard _guard;
    private readonly ICustomerScopeService _scope;

    public DetailScopeGuard(AppDbContext db, ICustomerScopeGuard guard, ICustomerScopeService scope)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _guard = guard ?? throw new ArgumentNullException(nameof(guard));
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
    }

    /// <inheritdoc />
    public async Task<bool> CanReadAsync<TEntity>(
        ClaimsPrincipal principal,
        long id,
        bool allowShared = false,
        CancellationToken ct = default) where TEntity : class
    {
        var entity = await _db.Set<TEntity>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => EF.Property<long>(e, "Id") == id, ct)
            .ConfigureAwait(false);

        if (entity == null)
        {
            return true; // 交给下游返回 404
        }

        var customerId = ReadLong(entity, "CustomerId");
        var appCode = ReadString(entity, "AppCode");
        var isSystem = ReadBool(entity, "IsSystem");

        if (allowShared && _scope.IsSharedReadRow(appCode, isSystem))
        {
            return true; // 字典/配置类共享行对所有角色可读（设计 §3.5）
        }

        return await _guard.CanAccessOwnedEntityAsync(principal, customerId, appCode, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> CanReadByKeyAsync<TEntity>(
        ClaimsPrincipal principal,
        string key,
        string keyProperty = "Key",
        bool allowShared = false,
        CancellationToken ct = default) where TEntity : class
    {
        var entity = await _db.Set<TEntity>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => EF.Property<string>(e, keyProperty) == key, ct)
            .ConfigureAwait(false);

        if (entity == null)
        {
            return true; // 交给下游返回 404
        }

        var customerId = ReadLong(entity, "CustomerId");
        var appCode = ReadString(entity, "AppCode");
        var isSystem = ReadBool(entity, "IsSystem");
        if (allowShared && _scope.IsSharedReadRow(appCode, isSystem))
        {
            return true;
        }
        return await _guard.CanAccessOwnedEntityAsync(principal, customerId, appCode, ct).ConfigureAwait(false);
    }

    private static long? ReadLong(object entity, string name)
    {
        var value = entity.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(entity);
        if (value is long l) return l;
        if (value is int i) return i;
        return null;
    }

    private static string? ReadString(object entity, string name)
    {
        return entity.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(entity) as string;
    }

    private static bool? ReadBool(object entity, string name)
    {
        var value = entity.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(entity);
        return value is bool b ? b : null;
    }
}
