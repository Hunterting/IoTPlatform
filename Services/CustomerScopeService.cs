using System.Security.Claims;
using IoTPlatform.Configuration;
using IoTPlatform.Data.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace IoTPlatform.Services;

/// <summary>
/// 客户访问范围服务实现（设计 §6 T02.2）。
///
/// <para>【三分支】超管 All 短路；admin Multi <b>每次请求从 DB 读取授权集合</b>（claim 仅为快照，
/// 不参与任何授权判定，故超管刚追加/撤销的客户用旧 token 也立即生效）；下级 Single 只读 claim 单值，
/// Resolve/GetAppCodes 零 DB 访问；CanAccess 在无 CustomerId claim 时回退查 customers 表比对 AppCode，
/// 但始终不查 user_customers，路径零回归。</para>
///
/// <para>【空集合】Multi 分支授权集合为空时返回空 CustomerIds/AppCodes；返回 403 与否由调用方
/// （控制器/过滤器）决定，本服务只如实反映范围。</para>
/// </summary>
public sealed class CustomerScopeService : ICustomerScopeService
{
    private readonly IUserCustomerRepository _grants;
    private readonly ICustomerRepository _customers;
    private readonly IDeviceRepository? _devices;
    private readonly IProjectRepository? _projects;

    /// <summary>
    /// 兼容构造：仅供存量单测/内部仅需三分支解析的调用方使用。
    /// 新增的 5 个反查方法中，设备/项目反查依赖可选仓储；若经此构造创建且调用这两个方法会抛
    /// InvalidOperationException。生产 DI 走下方的四参数构造（[ActivatorUtilitiesConstructor]）。
    /// </summary>
    public CustomerScopeService(IUserCustomerRepository grants, ICustomerRepository customers)
        : this(grants, customers, null, null)
    {
    }

    /// <summary>
    /// 生产构造：新增 5 个归属反查方法所需的全部仓储。
    /// </summary>
    [Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructor]
    public CustomerScopeService(
        IUserCustomerRepository grants,
        ICustomerRepository customers,
        IDeviceRepository? devices,
        IProjectRepository? projects)
    {
        _grants = grants ?? throw new ArgumentNullException(nameof(grants));
        _customers = customers ?? throw new ArgumentNullException(nameof(customers));
        _devices = devices;
        _projects = projects;
    }

    /// <inheritdoc />
    public async Task<CustomerScope> ResolveAsync(ClaimsPrincipal principal, CancellationToken ct = default)
    {
        var role = GetRole(principal);

        // ① 超管：全量标记，CustomerIds/AppCodes 空表示"不过滤"。
        if (Roles.IsSuperAdmin(role))
        {
            return new CustomerScope(CustomerScopeMode.All, Array.Empty<long>(), Array.Empty<string>());
        }

        // ② admin：DB 为准。claim ManagedCustomerIds 只由 T03 的根上下文用于识别模式，
        //    这里绝不解析它做授权判定（设计 §5.2 / §7.2-4）。
        if (role == Roles.ADMIN)
        {
            var userId = GetUserId(principal);
            IReadOnlyList<long> customerIds = userId.HasValue
                ? await _grants.GetCustomerIdsAsync(userId.Value, ct).ConfigureAwait(false)
                : Array.Empty<long>();
            var appCodes = await ResolveAppCodesAsync(customerIds, ct).ConfigureAwait(false);
            return new CustomerScope(CustomerScopeMode.Multi, customerIds, appCodes);
        }

        // ③ 下级（含 customer_admin）：单值 claim，零 DB 访问。
        return BuildSingleScope(principal);
    }

    /// <inheritdoc />
    public async Task<bool> CanAccessCustomerAsync(
        ClaimsPrincipal principal,
        long customerId,
        CancellationToken ct = default)
    {
        var role = GetRole(principal);

        // ① 超管恒放行。
        if (Roles.IsSuperAdmin(role))
        {
            return true;
        }

        // ② admin：逐次 DB EXISTS——授权变更即时生效，不读 claim。
        if (role == Roles.ADMIN)
        {
            var userId = GetUserId(principal);
            return userId.HasValue
                && await _grants.ExistsAsync(userId.Value, customerId, ct).ConfigureAwait(false);
        }

        // ③ 下级：优先比对自身单值 CustomerId claim。
        //    存量库下级用户 CustomerId=NULL，JWT 仅有 AppCode claim（无 CustomerId）；
        //    若只比对 CustomerId 会把自身客户区域误判为 403，破坏 G3 下级零回归。
        var claimCustomerId = principal?.FindFirst("CustomerId")?.Value;
        if (long.TryParse(claimCustomerId, out var singleId))
        {
            return singleId == customerId;
        }

        //    无 CustomerId claim 时回退到 AppCode 比对，口径与 GetAppCodesAsync / Single scope 一致。
        //    允许查 customers 表，但显式 IgnoreQueryFilters() 不依赖租户全局过滤器；
        //    绝不触碰 user_customers（注入 ThrowingUserCustomerRepository 的测试可证明）。
        var claimAppCode = principal?.FindFirst("AppCode")?.Value;
        if (string.IsNullOrEmpty(claimAppCode))
        {
            return false;
        }

        var customerAppCode = await _customers.GetQueryable()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(c => c.Id == customerId)
            .Select(c => c.AppCode)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return string.Equals(customerAppCode, claimAppCode, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetAppCodesAsync(
        ClaimsPrincipal principal,
        CancellationToken ct = default)
    {
        var role = GetRole(principal);

        // 超管返回空集合表示不过滤；下游据此走全量查询。
        if (Roles.IsSuperAdmin(role))
        {
            return Array.Empty<string>();
        }

        // ResolveAsync 对 Multi 每次读 DB 并反查 AppCode，对 Single 只读 claim。
        var scope = await ResolveAsync(principal, ct).ConfigureAwait(false);
        return scope.AppCodes;
    }

    // ── 授权缺口统一修复新增：5 个服务端归属反查方法（设计 §3.2 A）──────────────────

    /// <inheritdoc />
    public async Task<long?> ResolveCustomerIdByAppCodeAsync(string appCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(appCode))
        {
            return null;
        }

        return await _customers.GetQueryable()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(c => c.AppCode == appCode)
            .Select(c => (long?)c.Id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<string?> ResolveAppCodeByCustomerIdAsync(long customerId, CancellationToken ct = default)
    {
        return await _customers.GetQueryable()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(c => c.Id == customerId)
            .Select(c => c.AppCode)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<long?> ResolveCustomerIdByDeviceIdAsync(long deviceId, CancellationToken ct = default)
    {
        if (_devices == null)
        {
            throw new InvalidOperationException(
                "CustomerScopeService 由兼容构造创建，未注入设备仓储，无法按设备反查归属；请使用四参数构造。");
        }

        // 设备无 CustomerId 列：先 IgnoreQueryFilters 点查设备 AppCode。
        var deviceAppCode = await _devices.GetQueryable()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(d => d.Id == deviceId)
            .Select(d => d.AppCode)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(deviceAppCode))
        {
            return null;
        }

        // AppCode↔Customer 1:1 不变（设计 §7.2-7）：按 AppCode 反查客户 ID。
        return await ResolveCustomerIdByAppCodeAsync(deviceAppCode, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<long?> ResolveCustomerIdByProjectIdAsync(long projectId, CancellationToken ct = default)
    {
        if (_projects == null)
        {
            throw new InvalidOperationException(
                "CustomerScopeService 由兼容构造创建，未注入项目仓储，无法按项目反查归属；请使用四参数构造。");
        }

        // 项目自带权威 CustomerId 列，IgnoreQueryFilters 后原值返回。
        return await _projects.GetQueryable()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(p => p.Id == projectId)
            .Select(p => (long?)p.CustomerId)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public bool IsSharedSystemRow(string? appCode, bool? isSystem)
    {
        // 设计 §3.5：共享行 = "无租户归属" 或 "显式系统行"。
        // 注意本判定只对字典/配置类实体调用；附件/角色/受控设备的 AppCode=null 是缺陷，不得调用本方法放行。
        return isSystem == true || string.IsNullOrWhiteSpace(appCode);
    }

    /// <summary>
    /// 由授权客户 ID 集合反查 AppCode（AppCode↔Customer 1:1 不变，设计 §7.2-7）。
    /// 显式 IgnoreQueryFilters()：admin 无 AppCode，不能让启动期冻结在其它租户的全局过滤器影响反查。
    /// </summary>
    private async Task<IReadOnlyList<string>> ResolveAppCodesAsync(
        IReadOnlyList<long> customerIds,
        CancellationToken ct)
    {
        if (customerIds == null || customerIds.Count == 0)
        {
            return Array.Empty<string>();
        }

        // 物化为 List：EF Core 对 List.Contains 的翻译稳定为 SQL IN；同时避免重复枚举 IReadOnlyList。
        var ids = customerIds.Distinct().ToList();
        var appCodes = await _customers.GetQueryable()
            .IgnoreQueryFilters()
            .Where(c => ids.Contains(c.Id))
            .OrderBy(c => c.Id)
            .Select(c => c.AppCode)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return appCodes;
    }

    /// <summary>下级单分支：只读 JWT 中的 CustomerId / AppCode，绝不查授权表。</summary>
    private static CustomerScope BuildSingleScope(ClaimsPrincipal principal)
    {
        IReadOnlyList<long> customerIds = Array.Empty<long>();
        IReadOnlyList<string> appCodes = Array.Empty<string>();

        var customerIdClaim = principal?.FindFirst("CustomerId")?.Value;
        if (long.TryParse(customerIdClaim, out var customerId))
        {
            customerIds = new[] { customerId };
        }

        var appCodeClaim = principal?.FindFirst("AppCode")?.Value;
        if (!string.IsNullOrEmpty(appCodeClaim))
        {
            appCodes = new[] { appCodeClaim };
        }

        return new CustomerScope(CustomerScopeMode.Single, customerIds, appCodes);
    }

    /// <summary>统一超管判据：Roles.IsSuperAdmin（设计 §7.2-2）。</summary>
    private static string? GetRole(ClaimsPrincipal principal)
    {
        return principal?.FindFirst(ClaimTypes.Role)?.Value;
    }

    /// <summary>统一用户 ID 判据：ClaimTypes.NameIdentifier（与 TenantContextAccessor / JwtHelper 一致）。</summary>
    private static long? GetUserId(ClaimsPrincipal principal)
    {
        var userIdClaim = principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return long.TryParse(userIdClaim, out var userId) ? userId : null;
    }
}
