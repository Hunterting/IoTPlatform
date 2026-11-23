using System.Security.Claims;

namespace IoTPlatform.Services.Guards;

/// <summary>
/// 设备/区域/项目归属守卫接口（M4，设计 §2.5.2 / §3.2 B）。
///
/// <para>只做<b>租户归属判定</b>，与 <see cref="AnShengCommandGuard"/>（MQTT 协议/状态守卫）职责不同，
/// 两者不得合并。所有判定统一委托 <see cref="ICustomerScopeService"/>，不产生第二套判据（原则 A）。</para>
/// </summary>
public interface IDeviceAccessGuard
{
    /// <summary>确保 principal 可操作指定设备（deviceId → AppCode → CustomerId → CanAccess）。</summary>
    /// <param name="principal">当前请求 principal。</param>
    /// <param name="deviceId">目标设备 ID。</param>
    /// <param name="ct">取消令牌。</param>
    /// <exception cref="NotFoundException">设备不存在。</exception>
    /// <exception cref="ForbiddenAccessException">无权操作该设备。</exception>
    Task EnsureCanOperateAsync(ClaimsPrincipal principal, long deviceId, CancellationToken ct = default);

    /// <summary>批量版：确保 principal 可操作全部指定设备，避免逐个点查造成 N+1。</summary>
    /// <param name="principal">当前请求 principal。</param>
    /// <param name="deviceIds">目标设备 ID 集合。</param>
    /// <param name="ct">取消令牌。</param>
    /// <exception cref="NotFoundException">任一设备不存在。</exception>
    /// <exception cref="ForbiddenAccessException">任一设备越权。</exception>
    Task EnsureCanOperateAsync(ClaimsPrincipal principal, IReadOnlyCollection<long> deviceIds, CancellationToken ct = default);

    /// <summary>确保 principal 可访问指定区域（areaId → CustomerId → CanAccess）。</summary>
    /// <param name="principal">当前请求 principal。</param>
    /// <param name="areaId">目标区域 ID。</param>
    /// <param name="ct">取消令牌。</param>
    /// <exception cref="NotFoundException">区域不存在。</exception>
    /// <exception cref="ForbiddenAccessException">无权访问该区域。</exception>
    Task EnsureAreaAccessAsync(ClaimsPrincipal principal, long areaId, CancellationToken ct = default);

    /// <summary>
    /// 确保 principal 可访问指定项目（projectId → CustomerId → CanAccess）。
    /// ContractsController.UploadContract 在写库前调用本方法（M9 五端点之一）。
    /// </summary>
    /// <param name="principal">当前请求 principal。</param>
    /// <param name="projectId">目标项目 ID。</param>
    /// <param name="ct">取消令牌。</param>
    /// <exception cref="NotFoundException">项目不存在。</exception>
    /// <exception cref="ForbiddenAccessException">无权在该项目下上传。</exception>
    Task EnsureProjectAccessAsync(ClaimsPrincipal principal, long projectId, CancellationToken ct = default);
}
