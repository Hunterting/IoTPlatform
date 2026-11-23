using System.Security.Claims;
using IoTPlatform.Configuration;

namespace IoTPlatform.Services.Guards;

/// <summary>
/// M2 写目标归属解析结果（设计 §2.3）。
/// </summary>
/// <param name="Allowed">是否允许继续写库。</param>
/// <param name="ErrorCode">拒绝时的业务码：400 参数错误 / 403 越权。</param>
/// <param name="AppCode">服务端派生的真实归属，绝不由请求头或请求体直接决定。</param>
/// <param name="ErrorMessage">拒绝说明。</param>
public sealed record WriteTargetResolution(
    bool Allowed,
    int ErrorCode,
    string? AppCode,
    string ErrorMessage)
{
    /// <summary>构造成功结果。</summary>
    public static WriteTargetResolution Success(string? appCode) =>
        new(true, 200, appCode, string.Empty);

    /// <summary>构造拒绝结果。</summary>
    public static WriteTargetResolution Failure(int code, string message) =>
        new(false, code, null, message);
}

/// <summary>
/// M2 带 AppCode 的创建/修改统一解析器（设计 §2.3）。
///
/// <para><b>请求体 AppCode 只是待校验目标，不是授权凭据</b>：admin 必须先按目标客户走
/// <see cref="ICustomerScopeService.CanAccessCustomerAsync"/>（DB 授权集合），通过后由服务端
/// 反查真实 AppCode 落库。下级沿用 JWT 单值 claim，超管可代租户创建但归属仍从目标客户派生。</para>
///
/// <para>禁止 AsyncLocal / 隐式 principal：principal 由 Controller 显式下推。</para>
/// </summary>
public static class WriteTargetResolver
{
    /// <summary>解析写目标归属，不写库、无副作用。</summary>
    /// <param name="scope">客户范围服务（DB 权威）。</param>
    /// <param name="user">当前请求 principal（显式参数）。</param>
    /// <param name="requestAppCode">请求体携带的待校验目标 AppCode。</param>
    /// <param name="customerId">请求体携带的目标客户 ID（没有该字段时传 null）。</param>
    /// <param name="ct">取消令牌。</param>
    public static async Task<WriteTargetResolution> ResolveAsync(
        ICustomerScopeService? scope,
        ClaimsPrincipal? user,
        string? requestAppCode,
        long? customerId = null,
        CancellationToken ct = default)
    {
        if (scope == null || user == null)
        {
            return WriteTargetResolution.Failure(403, "缺少客户范围上下文");
        }

        var role = user.FindFirst(ClaimTypes.Role)?.Value;
        if (Roles.IsSuperAdmin(role))
        {
            // 超管代租户创建：归属仍从目标客户派生；未带 CustomerId 时保留请求目标（可建共享配置）。
            var appCode = customerId.HasValue
                ? await scope.ResolveAppCodeByCustomerIdAsync(customerId.Value, ct).ConfigureAwait(false)
                : requestAppCode;
            return WriteTargetResolution.Success(appCode);
        }

        if (role == Roles.ADMIN)
        {
            // 【关键】不再 `?? request.AppCode`：请求体值只作为待校验目标，绝不直接成为落库归属。
            var targetId = customerId ?? (string.IsNullOrWhiteSpace(requestAppCode)
                ? null
                : await scope.ResolveCustomerIdByAppCodeAsync(requestAppCode, ct).ConfigureAwait(false));
            if (!targetId.HasValue || !await scope.CanAccessCustomerAsync(user, targetId.Value, ct).ConfigureAwait(false))
            {
                return WriteTargetResolution.Failure(403, "无权在该客户下创建配置");
            }

            var resolved = await scope.ResolveAppCodeByCustomerIdAsync(targetId.Value, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(resolved))
            {
                return WriteTargetResolution.Failure(403, "目标客户缺少 AppCode");
            }

            return WriteTargetResolution.Success(resolved);
        }

        // 下级：claim 单值，零回归；claim 缺失即拒绝（不可静默落成 AppCode=null 的共享行）。
        var claimAppCode = user.FindFirst("AppCode")?.Value;
        if (string.IsNullOrWhiteSpace(claimAppCode))
        {
            return WriteTargetResolution.Failure(403, "缺少租户归属");
        }

        return WriteTargetResolution.Success(claimAppCode);
    }
}
