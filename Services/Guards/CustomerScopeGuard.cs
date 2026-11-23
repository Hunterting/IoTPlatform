using System.Security.Claims;

namespace IoTPlatform.Services.Guards;

/// <summary>
/// 统一归属判据接口（设计 §3.3 AssertScope / §3.4 CanAccessEntityAsync）。
///
/// <para>Controller 与 Service 双保险共用同一判据，来源都是 <see cref="ICustomerScopeService"/>，不会漂移。</para>
/// </summary>
public interface ICustomerScopeGuard
{
    /// <summary>
    /// 判定 principal 是否可访问某实体（异步解析 scope）。
    /// 把旧的 "appCode 为空即跳过" 语义改为 "空归属按角色走另一条判据，拿不到就拒绝"。
    /// </summary>
    Task<bool> CanAccessEntityAsync(
        ClaimsPrincipal principal,
        long? customerId,
        string? entityAppCode,
        bool? isShared = null,
        CancellationToken ct = default);

    /// <summary>
    /// M3 按 id 改删专用判据：严格三分支，<b>不</b>带共享行放行，也不采信 Multi 的 claim 快照。
    /// 与 <see cref="CanAccessEntityAsync"/> 的区别：后者对 AppCode 为空的行按共享数据放行（字典/配置类），
    /// 工单/告警/项目/受控设备的 AppCode 为空属于归属缺陷，必须 fail-closed。
    /// </summary>
    /// <param name="principal">当前请求 principal（显式传递，禁止 AsyncLocal 隐式上下文）。</param>
    /// <param name="customerId">实体的权威 CustomerId（无该列时传 null，由 AppCode 反查）。</param>
    /// <param name="entityAppCode">实体的权威 AppCode。</param>
    /// <param name="ct">取消令牌。</param>
    Task<bool> CanAccessOwnedEntityAsync(
        ClaimsPrincipal principal,
        long? customerId,
        string? entityAppCode,
        CancellationToken ct = default);

    /// <summary>
    /// Service 层双保险断言（不自己解析 principal，接收 Controller 下推的 scope）。
    /// 越权抛 <see cref="ForbiddenAccessException"/>。
    /// </summary>
    void AssertScope(
        CustomerScope? scope,
        long? entityCustomerId,
        string? entityAppCode,
        bool? isShared = null);

    /// <summary>
    /// 只有 AppCode 列的配置类实体使用的读侧断言（设计 §3.3 / §3.5）。
    /// Multi 分支通过 AppCode 反查权威 CustomerId 后比较 DB 授权集合，绝不读 claim 快照。
    /// </summary>
    /// <param name="scope">Controller 下推的范围；null 且非共享行时 fail-closed。</param>
    /// <param name="entityAppCode">实体落库的权威 AppCode。</param>
    /// <param name="isSharedSystem">是否为显式系统共享行（如字典 AppCode="system"）。</param>
    /// <param name="allowSharedNull">AppCode 为空是否按共享配置行处理；工单/角色/设备必须传 false。</param>
    /// <param name="ct">取消令牌。</param>
    Task AssertReadScopeByAppCodeAsync(
        CustomerScope? scope,
        string? entityAppCode,
        bool isSharedSystem,
        bool allowSharedNull,
        CancellationToken ct = default);

    /// <summary>
    /// 配置类实体写/删断言：共享行仅超管可写（设计 §3.5），非共享行按 scope 判据。
    /// 越权在调用方写库/副作用之前抛 <see cref="ForbiddenAccessException"/>。
    /// </summary>
    Task AssertWriteScopeByAppCodeAsync(
        CustomerScope? scope,
        string? entityAppCode,
        bool isSharedSystem,
        bool allowSharedNull,
        CancellationToken ct = default);
}

/// <summary>
/// 统一归属判据实现（设计 §3.3 / §3.4）。
///
/// <para><b>共享行</b>判定复用 <see cref="ICustomerScopeService.IsSharedSystemRow"/> 单一事实来源；
/// 仅对字典/配置类实体传 isShared，附件/角色/设备绝不传 true（设计 §3.5）。</para>
/// </summary>
public sealed class CustomerScopeGuard : ICustomerScopeGuard
{
    private readonly ICustomerScopeService _scope;

    public CustomerScopeGuard(ICustomerScopeService scope)
    {
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
    }

    /// <inheritdoc />
    public async Task<bool> CanAccessEntityAsync(
        ClaimsPrincipal principal,
        long? customerId,
        string? entityAppCode,
        bool? isShared = null,
        CancellationToken ct = default)
    {
        var scope = await _scope.ResolveAsync(principal, ct).ConfigureAwait(false);
        if (scope.Mode == CustomerScopeMode.All)
        {
            return true;
        }

        if (_scope.IsSharedSystemRow(entityAppCode, isShared))
        {
            return true;
        }

        if (scope.Mode == CustomerScopeMode.Multi)
        {
            return customerId.HasValue && scope.CustomerIds.Contains(customerId.Value);
        }

        // Single：claim 单值比对（下级零回归）；拿不到 claim 即拒绝（fail-closed）。
        var appCode = principal?.FindFirst("AppCode")?.Value;
        return !string.IsNullOrEmpty(appCode) && string.Equals(appCode, entityAppCode, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public async Task<bool> CanAccessOwnedEntityAsync(
        ClaimsPrincipal principal,
        long? customerId,
        string? entityAppCode,
        CancellationToken ct = default)
    {
        var scope = await _scope.ResolveAsync(principal, ct).ConfigureAwait(false);
        if (scope.Mode == CustomerScopeMode.All)
        {
            return true; // 超管全量（原则 G2）。
        }

        if (scope.Mode == CustomerScopeMode.Multi)
        {
            if (customerId.HasValue && scope.CustomerIds.Contains(customerId.Value))
            {
                return true;
            }

            // 实体只有 AppCode 列时（告警/受控设备/工作纪要）按 1:1 映射反查权威 CustomerId。
            // 判据始终是 DB 授权集合，绝不读 ManagedCustomerIds claim 快照。
            if (!customerId.HasValue && !string.IsNullOrWhiteSpace(entityAppCode))
            {
                var resolvedId = await _scope.ResolveCustomerIdByAppCodeAsync(entityAppCode, ct).ConfigureAwait(false);
                return resolvedId.HasValue && scope.CustomerIds.Contains(resolvedId.Value);
            }

            return false;
        }

        // Single：逐字沿用 JWT claim 单值判据（下级零回归），AppCode 为空归属的实体一律拒绝。
        var claimAppCode = principal?.FindFirst("AppCode")?.Value;
        if (!string.IsNullOrEmpty(claimAppCode)
            && string.Equals(claimAppCode, entityAppCode, StringComparison.Ordinal))
        {
            return true;
        }

        var claimCustomerId = principal?.FindFirst("CustomerId")?.Value;
        return customerId.HasValue
               && long.TryParse(claimCustomerId, out var parsedId)
               && parsedId == customerId.Value;
    }

    /// <inheritdoc />
    public void AssertScope(
        CustomerScope? scope,
        long? entityCustomerId,
        string? entityAppCode,
        bool? isShared = null)
    {
        if (scope?.Mode == CustomerScopeMode.All)
        {
            return; // 超管全量（原则 E）。
        }

        if (_scope.IsSharedSystemRow(entityAppCode, isShared))
        {
            return; // 共享行读放行（写侧是否仅超管由调用方按 §3.5 另行控制）。
        }

        if (scope == null)
        {
            throw new ForbiddenAccessException(); // 无范围上下文 = fail-closed。
        }

        if (scope.Mode == CustomerScopeMode.Multi)
        {
            if (!entityCustomerId.HasValue || !scope.CustomerIds.Contains(entityCustomerId.Value))
            {
                throw new ForbiddenAccessException();
            }

            return;
        }

        // Single：scope.AppCodes 首值即 claim 单值。
        var appCode = scope.AppCodes.FirstOrDefault();
        if (string.IsNullOrEmpty(appCode) || !string.Equals(appCode, entityAppCode, StringComparison.Ordinal))
        {
            throw new ForbiddenAccessException();
        }
    }

    /// <inheritdoc />
    public async Task AssertReadScopeByAppCodeAsync(
        CustomerScope? scope,
        string? entityAppCode,
        bool isSharedSystem,
        bool allowSharedNull,
        CancellationToken ct = default)
    {
        if (scope?.Mode == CustomerScopeMode.All)
        {
            return; // 超管全量。
        }

        // 共享行（字典/配置）读放行；allowSharedNull=false 用于 AppCode=null 属于归属缺陷的实体。
        if (IsSharedRow(entityAppCode, isSharedSystem, allowSharedNull))
        {
            return;
        }

        if (scope == null)
        {
            throw new ForbiddenAccessException(); // 无范围上下文 = fail-closed。
        }

        if (scope.Mode == CustomerScopeMode.Multi)
        {
            // 只有 AppCode 列时按 1:1 映射反查权威 CustomerId（DB 为准），不采信 claim 快照。
            var customerId = string.IsNullOrWhiteSpace(entityAppCode)
                ? null
                : await _scope.ResolveCustomerIdByAppCodeAsync(entityAppCode, ct).ConfigureAwait(false);
            if (!customerId.HasValue || !scope.CustomerIds.Contains(customerId.Value))
            {
                throw new ForbiddenAccessException();
            }

            return;
        }

        var claimAppCode = scope.AppCodes.FirstOrDefault();
        if (string.IsNullOrEmpty(claimAppCode)
            || !string.Equals(claimAppCode, entityAppCode, StringComparison.Ordinal))
        {
            throw new ForbiddenAccessException();
        }
    }

    /// <inheritdoc />
    public async Task AssertWriteScopeByAppCodeAsync(
        CustomerScope? scope,
        string? entityAppCode,
        bool isSharedSystem,
        bool allowSharedNull,
        CancellationToken ct = default)
    {
        if (scope == null)
        {
            throw new ForbiddenAccessException(); // 无范围上下文 = fail-closed。
        }

        if (scope.Mode == CustomerScopeMode.All)
        {
            return; // 超管可读写共享行。
        }

        if (IsSharedRow(entityAppCode, isSharedSystem, allowSharedNull))
        {
            // §3.5：共享 system 行对所有角色可读，但写/删仅超管。
            throw new ForbiddenAccessException("共享配置仅超级管理员可修改或删除");
        }

        await AssertReadScopeByAppCodeAsync(scope, entityAppCode, false, false, ct).ConfigureAwait(false);
    }

    /// <summary>共享行判据显式化：只由配置/字典类调用方标记，避免泛化到角色/设备。</summary>
    private static bool IsSharedRow(string? entityAppCode, bool isSharedSystem, bool allowSharedNull)
    {
        return isSharedSystem || (allowSharedNull && string.IsNullOrWhiteSpace(entityAppCode));
    }
}
