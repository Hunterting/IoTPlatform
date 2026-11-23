using System.Security.Claims;
using IoTPlatform.Data;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;

namespace IoTPlatform.Services.Guards;

/// <summary>
/// 萤石摄像头范围守卫实现（M7，设计 §2.7）。
///
/// <para>【fail-closed】Multi/Single 分支拿不到授权 AppCode 集合时直接返回 null（无摄像头），
/// 不依赖 "cam != null" 这类软校验；超管 All 走不过滤路径（集合空=不过滤，原则 E）。</para>
/// </summary>
public sealed class EzvizScopeGuard : IEzvizScopeGuard
{
    private readonly AppDbContext _db;
    private readonly ICustomerScopeService _scope;

    public EzvizScopeGuard(AppDbContext db, ICustomerScopeService scope)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
    }

    /// <inheritdoc />
    public async Task<Camera?> ResolveScopedCameraAsync(
        ClaimsPrincipal principal, string deviceSerial, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(deviceSerial))
        {
            return null;
        }

        var scope = await _scope.ResolveAsync(principal, ct).ConfigureAwait(false);

        // StreamUrl 形如 "ezviz:{serial}:{channel}"；用 Contains 保持 provider 无关（InMemory/MySQL 均可）。
        var serialToken = $"ezviz:{deviceSerial}:";
        var query = _db.Cameras
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(c => c.StreamUrl != null && c.StreamUrl.Contains(serialToken));

        if (scope.Mode == CustomerScopeMode.All)
        {
            return await query.FirstOrDefaultAsync(ct).ConfigureAwait(false);
        }

        var allowed = ResolveAllowedAppCodes(principal, scope);
        if (allowed.Count == 0)
        {
            return null; // fail-closed：无授权租户 = 看不到任何摄像头。
        }

        return await query
            .Where(c => allowed.Contains(c.AppCode ?? string.Empty))
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task EnsureCameraAccessAsync(
        ClaimsPrincipal principal, string deviceSerial, CancellationToken ct = default)
    {
        var camera = await ResolveScopedCameraAsync(principal, deviceSerial, ct).ConfigureAwait(false);
        if (camera == null)
        {
            throw new ForbiddenAccessException("无权访问该摄像头或摄像头未绑定授权租户");
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> GetAllowedAppCodesAsync(
        ClaimsPrincipal principal, CancellationToken ct = default)
        => _scope.GetAppCodesAsync(principal, ct);

    /// <summary>
    /// Multi 用 scope 反查的集合；Single 用 claim 单值（下级零回归）；All 返回空（不过滤）。
    /// </summary>
    private static IReadOnlyList<string> ResolveAllowedAppCodes(ClaimsPrincipal principal, CustomerScope scope)
    {
        if (scope.Mode == CustomerScopeMode.Multi)
        {
            return scope.AppCodes;
        }

        var claimAppCode = principal?.FindFirst("AppCode")?.Value;
        return string.IsNullOrEmpty(claimAppCode)
            ? Array.Empty<string>()
            : new[] { claimAppCode };
    }
}
