using System.Threading;
using System.Threading.Tasks;
using IoTPlatform.Data;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;

namespace IoTPlatform.Services;

/// <summary>
/// 设备注册的唯一性 / 冲突裁决。
///
/// 【为什么抽成公共方法】
///   设计文档 §3.4 / T2 / T3：路径①（认领）与路径②（直接新增）必须走<b>同一套</b>
///   IMEI / SerialNumber 冲突校验，否则会在 <c>discovered_ansheng_devices</c> 与
///   <c>devices</c> 两边各落一条（风险 R1）。本类把这套校验收敛为单一入口，
///   由 <see cref="DeviceService.CreateDeviceAsync"/>（T2）与
///   <see cref="AnShengDiscoveryService.ClaimAsync"/>（T3）共同调用。
///
/// 【校验维度】
///   ① 待认领池：<c>discovered_ansheng_devices</c> 已有同 IMEI 未认领记录 → 必须走认领。
///   ② 正式设备：<c>devices.SerialNumber</c> 已有同 IMEI → 冲突拒绝。
///   ③ 在线/IMEI 存在性（仅直接新增用）：若提供了 IMEI 且既无待认领记录、又无任何
///      活跃的 <c>ANSHENG_MQTT</c> 协议配置，视为凭空建孤儿设备，拒绝。
///
/// 【线程安全】纯静态、无状态，可并发调用。
/// </summary>
public static class DeviceRegistrationGuard
{
    /// <summary>冲突类型。</summary>
    public enum Conflict
    {
        /// <summary>无冲突。</summary>
        None,

        /// <summary>该 IMEI 已在待认领池（未认领），应走认领流程。</summary>
        PendingPool,

        /// <summary>该 IMEI/SerialNumber 已对应一条正式 <c>devices</c> 行。</summary>
        ExistingDevice,

        /// <summary>提供了 IMEI 但平台无任何可识别该设备的在线证据（无待认领记录且无活跃协议配置）。</summary>
        NoOnlinePresence,

        /// <summary>目标租户设备数已达/超过配额上限（租户设备数量配额需求，详见 DeviceQuotaGuard）。</summary>
        QuotaExceeded,
    }

    /// <summary>冲突裁决结果。</summary>
    /// <param name="Type">冲突类型；<see cref="Conflict.None"/> 表示通过。</param>
    /// <param name="Imei">被检查的 IMEI（即 SerialNumber）。</param>
    /// <param name="ExistingDeviceId">命中已存在设备时的主键；否则 <c>null</c>。</param>
    /// <param name="Message">面向人的提示；无冲突时为空串。</param>
    /// <param name="ActiveAnshengConfigExists">平台是否存在任一活跃的 <c>ANSHENG_MQTT</c> 协议配置。</param>
    public sealed record ConflictResult(
        Conflict Type,
        string? Imei,
        long? ExistingDeviceId,
        string Message,
        bool ActiveAnshengConfigExists);

    /// <summary>
    /// 对某一 IMEI（=SerialNumber）做注册冲突裁决。
    /// </summary>
    /// <param name="db">数据库上下文（应在<b>无全局租户过滤器</b>的作用域下，或调用方已确保隔离正确）。</param>
    /// <param name="imei">设备 IMEI / SerialNumber；为空表示纯手工录入设备（无 IMEI），直接放行。</param>
    /// <param name="includePendingPool">是否把待认领池同 IMEI 视为冲突。认领流程会<b>关闭</b>此项（它认领的正是这条待认领记录）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>裁决结果。</returns>
    public static async Task<ConflictResult> CheckAsync(
        AppDbContext db,
        string? imei,
        bool includePendingPool = true,
        CancellationToken ct = default)
    {
        // 先取一次「是否有活跃安圣协议配置」——直接新增的存在性闸门要用，
        // 且避免后面多次查库。
        var hasActiveConfig = await db.Set<ProtocolConfig>()
            .AsNoTracking()
            .AnyAsync(c => c.IsActive && c.ProtocolType == "ANSHENG_MQTT", ct)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(imei))
        {
            // 没有 IMEI 的手工设备：不去重、不查在线，直接放行（存在性闸门因无 IMEI 也不触发）。
            return new ConflictResult(Conflict.None, null, null, string.Empty, hasActiveConfig);
        }

        var normalizedImei = imei.Trim();

        // ① 待认领池：同 IMEI 且尚未认领 → 必须回认领流程。
        if (includePendingPool)
        {
            var inPending = await db.Set<DiscoveredAnShengDevice>()
                .AsNoTracking()
                .AnyAsync(d => d.Imei == normalizedImei && !d.IsClaimed, ct)
                .ConfigureAwait(false);

            if (inPending)
            {
                return new ConflictResult(
                    Conflict.PendingPool,
                    normalizedImei,
                    null,
                    $"设备 IMEI {normalizedImei} 已在待认领池，请走认领流程（POST /api/v1/ansheng/claim）。",
                    hasActiveConfig);
            }
        }

        // ② 正式设备：同 SerialNumber（IMEI）已存在 → 冲突拒绝。
        var existing = await db.Devices
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.SerialNumber == normalizedImei, ct)
            .ConfigureAwait(false);

        if (existing != null)
        {
            return new ConflictResult(
                Conflict.ExistingDevice,
                normalizedImei,
                existing.Id,
                $"设备 IMEI/SerialNumber {normalizedImei} 已存在（DeviceId={existing.Id}），不可重复创建。",
                hasActiveConfig);
        }

        return new ConflictResult(Conflict.None, normalizedImei, null, string.Empty, hasActiveConfig);
    }

    /// <summary>
    /// 在线/IMEI 存在性闸门（仅直接新增路径调用）。
    ///
    /// 给定一次通过的 <paramref name="preliminary"/> 裁决，若调用方提供了 IMEI 却既无待认领记录、
    /// 又无任一活跃 <c>ANSHENG_MQTT</c> 协议配置，判定为「凭空建孤儿设备」并返回
    /// <see cref="Conflict.NoOnlinePresence"/>；否则沿用原裁决。
    /// </summary>
    /// <param name="preliminary">来自 <see cref="CheckAsync"/> 的初步裁决（应已通过 ①②）。</param>
    /// <returns>补充存在性结论后的裁决。</returns>
    public static ConflictResult ApplyOnlinePresenceGate(ConflictResult preliminary)
    {
        if (preliminary.Type != Conflict.None) return preliminary;

        if (!string.IsNullOrWhiteSpace(preliminary.Imei) && !preliminary.ActiveAnshengConfigExists)
        {
            return preliminary with
            {
                Type = Conflict.NoOnlinePresence,
                Message = "该设备未在上线 AnSheng 设备中发现（无待认领记录且无活跃 ANSHENG_MQTT 协议配置），" +
                          "请先由设备完成 MQTT 发现，或由租户在待认领页认领。"
            };
        }

        return preliminary;
    }
}

/// <summary>
/// 设备注册被服务端策略拒绝时抛出的异常。
///
/// 【为什么单独建一个异常类】
///   <see cref="DeviceService.CreateDeviceAsync"/> 既要表达「租户无权直接新增」（→ HTTP 403）、
///   也要表达「去重 / 存在性冲突」（→ HTTP 409）。二者都不是既有的
///   <see cref="InvalidOperationException"/>（被控制器映射为 400）。用携带
///   <see cref="StatusCode"/> 的专用异常，控制器即可按语义映射，而不靠解析中文 Message。
/// </summary>
public sealed class DeviceRegistrationException : Exception
{
    /// <summary>应回给客户端的 HTTP 业务状态码（如 403 / 409）。</summary>
    public int StatusCode { get; }

    /// <summary>冲突类型，便于测试与日志归类。</summary>
    public DeviceRegistrationGuard.Conflict Conflict { get; }

    /// <summary>
    /// 构造异常。
    /// </summary>
    /// <param name="statusCode">HTTP 业务状态码。</param>
    /// <param name="message">面向人的提示。</param>
    /// <param name="conflict">冲突类型，默认 <see cref="DeviceRegistrationGuard.Conflict.None"/>。</param>
    public DeviceRegistrationException(
        int statusCode,
        string message,
        DeviceRegistrationGuard.Conflict conflict = DeviceRegistrationGuard.Conflict.None)
        : base(message)
    {
        StatusCode = statusCode;
        Conflict = conflict;
    }
}
