using System.Security.Claims;
using IoTPlatform.Models;

namespace IoTPlatform.Services.Guards;

/// <summary>
/// 萤石摄像头范围守卫接口（M7，设计 §2.7 / §3.2 B）。
///
/// <para>摄像头不直接带 CustomerId，归属通过 <c>StreamUrl = "ezviz:{serial}:{channel}"</c>
/// 中的 serial 关联到 cameras 行的 AppCode（B1 实施时读源码确认，见设计 §7.3）。</para>
/// <para>所有判定委托 <see cref="ICustomerScopeService"/>；不复用 EzvizService 内部已有的
/// "AppCode 非空即放行" 软校验（那是 M7 的反面实证）。</para>
/// </summary>
public interface IEzvizScopeGuard
{
    /// <summary>按 principal 范围解析可访问的摄像头（越权返回 null，返回前已完成归属过滤）。</summary>
    /// <param name="principal">当前请求 principal。</param>
    /// <param name="deviceSerial">萤石设备序列号。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>授权范围内的摄像头；未绑定或越权时返回 null。</returns>
    Task<Camera?> ResolveScopedCameraAsync(ClaimsPrincipal principal, string deviceSerial, CancellationToken ct = default);

    /// <summary>确保 principal 可查看/操控指定序列号的摄像头，越权或未绑定即抛异常。</summary>
    /// <param name="principal">当前请求 principal。</param>
    /// <param name="deviceSerial">萤石设备序列号。</param>
    /// <param name="ct">取消令牌。</param>
    /// <exception cref="ForbiddenAccessException">越权或摄像头未绑定任何授权租户。</exception>
    Task EnsureCameraAccessAsync(ClaimsPrincipal principal, string deviceSerial, CancellationToken ct = default);

    /// <summary>取 principal 可达 AppCode 集合（All 返回空集合表示不过滤，原则 E）。</summary>
    /// <param name="principal">当前请求 principal。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>AppCode 只读列表。</returns>
    Task<IReadOnlyList<string>> GetAllowedAppCodesAsync(ClaimsPrincipal principal, CancellationToken ct = default);
}
