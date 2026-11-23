using IoTPlatform.Configuration;

namespace IoTPlatform.Services.Guards;

/// <summary>
/// B5/M8 按 id 改删的写入范围判据（三分支 + 旧签名 fail-closed 兜底）。
///
/// <para>范围一律由 Controller 显式调用 <c>ICustomerScopeService.ResolveAsync(User)</c> 解析后下推，
/// 本类不读取 HttpContext / AsyncLocal，也不信任 JWT 的 ManagedCustomerIds 快照。</para>
/// </summary>
public static class WriteScopeAssertion
{
    /// <summary>
    /// 断言当前调用方可写指定归属的实体；越权在写库之前抛
    /// <see cref="ForbiddenAccessException"/>。
    /// </summary>
    /// <param name="scope">Controller 下推的范围；null 表示旧的 4 参数签名调用（内部/下级零回归路径）。</param>
    /// <param name="entityCustomerId">实体的权威 CustomerId；无该列时传 null。</param>
    /// <param name="entityAppCode">实体的权威 AppCode。</param>
    /// <param name="claimAppCode">调用方 JWT 的 AppCode claim；Single 分支逐字沿用旧判据。</param>
    /// <param name="currentUserRole">调用方角色；仅旧签名路径使用。</param>
    /// <param name="actionLabel">异常消息中的动作描述，如"修改该区域"。</param>
    public static void EnsureCanWrite(
        CustomerScope? scope,
        long? entityCustomerId,
        string? entityAppCode,
        string? claimAppCode,
        string? currentUserRole,
        string actionLabel)
    {
        if (scope != null)
        {
            if (scope.Mode == CustomerScopeMode.All)
            {
                return; // 超管全量，不回退。
            }

            // 【B5 QA 强调】实体 AppCode 为空属于无归属脏数据，任何非超管一律 fail-closed；
            // 即便 CustomerId 落在授权集合内，也不得借本次放宽写这条记录。
            if (string.IsNullOrWhiteSpace(entityAppCode))
            {
                throw new ForbiddenAccessException($"无权{actionLabel}");
            }

            if (scope.Mode == CustomerScopeMode.Multi)
            {
                // 有权威 CustomerId 时只认 DB 授权集合；没有时才用 DB 反查派生的 AppCodes
                // 比对（CustomerScopeService.Multi 的 AppCodes 由 customerIds 查 customers 得到）。
                var inScope = entityCustomerId.HasValue
                    ? scope.CustomerIds.Contains(entityCustomerId.Value)
                    : !string.IsNullOrWhiteSpace(entityAppCode)
                      && scope.AppCodes.Contains(entityAppCode, StringComparer.Ordinal);
                if (!inScope)
                {
                    throw new ForbiddenAccessException($"无权{actionLabel}");
                }

                return;
            }

            // Single：下级零回归，逐字沿用 JWT claim 单值判据；claim 为空一律 fail-closed。
            if (string.IsNullOrEmpty(claimAppCode)
                || !string.Equals(entityAppCode, claimAppCode, StringComparison.Ordinal))
            {
                throw new ForbiddenAccessException($"无权{actionLabel}");
            }

            return;
        }

        // 旧签名（内部通道/存量单测）保留原单值语义，但空归属与空 claim 不再相等放行。
        if (currentUserRole == Roles.SUPER_ADMIN)
        {
            return;
        }

        if (string.IsNullOrEmpty(claimAppCode)
            || string.IsNullOrEmpty(entityAppCode)
            || !string.Equals(entityAppCode, claimAppCode, StringComparison.Ordinal))
        {
            throw new ForbiddenAccessException($"无权{actionLabel}");
        }
    }
}
