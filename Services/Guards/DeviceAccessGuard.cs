using System.Security.Claims;
using IoTPlatform.Data;
using Microsoft.EntityFrameworkCore;

namespace IoTPlatform.Services.Guards;

/// <summary>
/// 设备/区域/项目归属守卫实现（M4，设计 §2.5.2）。
///
/// <para>【统一判据】所有归属判定最终都落到 <see cref="ICustomerScopeService.CanAccessCustomerAsync"/>，
/// 该服务内部按 All/Multi/Single 三分支处理；Guard 自身不复制任何判据（原则 A）。</para>
/// <para>【点查】一律 <c>AsNoTracking().IgnoreQueryFilters()</c>，不依赖启动期冻结的全局过滤器（原则 C/铁律 5）。</para>
/// <para>【fail-closed】拿不到归属（设备/区域/项目缺 CustomerId 或 AppCode）一律抛
/// <see cref="ForbiddenAccessException"/>，绝不因空值短路放行。</para>
/// </summary>
public sealed class DeviceAccessGuard : IDeviceAccessGuard
{
    private readonly AppDbContext _db;
    private readonly ICustomerScopeService _scope;

    public DeviceAccessGuard(AppDbContext db, ICustomerScopeService scope)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
    }

    /// <inheritdoc />
    public async Task EnsureCanOperateAsync(
        ClaimsPrincipal principal, long deviceId, CancellationToken ct = default)
    {
        var device = await _db.Devices
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(d => d.Id == deviceId)
            .Select(d => new { d.AppCode })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (device == null)
        {
            throw new NotFoundException("设备不存在");
        }

        // 设备无 CustomerId 列：AppCode → CustomerId（1:1 不变）→ CanAccess。
        var customerId = string.IsNullOrWhiteSpace(device.AppCode)
            ? null
            : await _scope.ResolveCustomerIdByAppCodeAsync(device.AppCode, ct).ConfigureAwait(false);

        if (!customerId.HasValue || !await _scope.CanAccessCustomerAsync(principal, customerId.Value, ct).ConfigureAwait(false))
        {
            throw new ForbiddenAccessException("无权操作该设备");
        }
    }

    /// <inheritdoc />
    public async Task EnsureCanOperateAsync(
        ClaimsPrincipal principal, IReadOnlyCollection<long> deviceIds, CancellationToken ct = default)
    {
        if (deviceIds == null || deviceIds.Count == 0)
        {
            return;
        }

        var ids = deviceIds.Distinct().ToList();
        // 一次查询取回全部设备的 AppCode，避免 N+1。
        var devices = await _db.Devices
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(d => ids.Contains(d.Id))
            .Select(d => new { d.Id, d.AppCode })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (devices.Count != ids.Count)
        {
            throw new NotFoundException("设备不存在");
        }

        var scope = await _scope.ResolveAsync(principal, ct).ConfigureAwait(false);
        if (scope.Mode == CustomerScopeMode.All)
        {
            return; // 超管全量，空集合=不过滤（原则 E），绝不按集合空否分支。
        }

        foreach (var device in devices)
        {
            if (string.IsNullOrWhiteSpace(device.AppCode))
            {
                throw new ForbiddenAccessException("无权操作该设备");
            }

            var customerId = await _scope.ResolveCustomerIdByAppCodeAsync(device.AppCode, ct).ConfigureAwait(false);
            if (!customerId.HasValue)
            {
                throw new ForbiddenAccessException("无权操作该设备");
            }

            var allowed = scope.Mode == CustomerScopeMode.Multi
                ? scope.CustomerIds.Contains(customerId.Value)
                : await _scope.CanAccessCustomerAsync(principal, customerId.Value, ct).ConfigureAwait(false);
            if (!allowed)
            {
                throw new ForbiddenAccessException("无权操作该设备");
            }
        }
    }

    /// <inheritdoc />
    public async Task EnsureAreaAccessAsync(
        ClaimsPrincipal principal, long areaId, CancellationToken ct = default)
    {
        var area = await _db.Areas
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(a => a.Id == areaId)
            .Select(a => new { a.CustomerId, a.AppCode })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (area == null)
        {
            throw new NotFoundException("区域不存在");
        }

        // 区域优先用 CustomerId；缺列时按 AppCode 回退（fail-closed）。
        long? customerId = area.CustomerId;
        if (!customerId.HasValue && !string.IsNullOrWhiteSpace(area.AppCode))
        {
            customerId = await _scope.ResolveCustomerIdByAppCodeAsync(area.AppCode, ct).ConfigureAwait(false);
        }

        if (!customerId.HasValue || !await _scope.CanAccessCustomerAsync(principal, customerId.Value, ct).ConfigureAwait(false))
        {
            throw new ForbiddenAccessException("无权访问该区域");
        }
    }

    /// <inheritdoc />
    public async Task EnsureProjectAccessAsync(
        ClaimsPrincipal principal, long projectId, CancellationToken ct = default)
    {
        var project = await _db.Projects
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(p => p.Id == projectId)
            .Select(p => new { p.CustomerId })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (project == null)
        {
            throw new NotFoundException("项目不存在");
        }

        if (!await _scope.CanAccessCustomerAsync(principal, project.CustomerId, ct).ConfigureAwait(false))
        {
            throw new ForbiddenAccessException("无权在该项目下上传");
        }
    }
}
