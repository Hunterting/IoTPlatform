using IoTPlatform.Infrastructure.Protocol.AnSheng;
using IoTPlatform.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace IoTPlatform.Services;

/// <summary>
/// 一次探测得到的设备能力快照。
///
/// 【为什么是 record 而不是直接传 <see cref="AnShengDevInfo"/> + <see cref="AnShengDevStatus"/>】
///   探测有两条报文来源（getDevInfo / getDevStatus），且两边字段互有重叠、互有缺失
///   （<c>slotAmount</c> 两边都可能出现，<c>iccid</c> 亦然）。若把两个协议 DTO 一路透传到落库层，
///   「哪个字段该听谁的」这条规则就会散落在各处。这里先归一成一个快照，
///   合并规则集中在 <c>MergeSnapshot</c> 一个地方。
/// </summary>
/// <param name="NetType">联网类型。</param>
/// <param name="SlotAmount">插槽数量。</param>
/// <param name="PhaseAmount">相位数量。</param>
/// <param name="Version">固件版本。</param>
/// <param name="Model">模组型号。</param>
/// <param name="Iccid">物联卡 ICCID。</param>
/// <param name="Signal">信号强度。</param>
public sealed record AnShengCapabilitySnapshot(
    string? NetType = null,
    int? SlotAmount = null,
    int? PhaseAmount = null,
    string? Version = null,
    string? Model = null,
    string? Iccid = null,
    int? Signal = null)
{
    /// <summary>空快照，表示「什么都没探到」。</summary>
    public static AnShengCapabilitySnapshot Empty { get; } = new();

    /// <summary>
    /// 由 <c>getDevInfo</c> 应答构造快照。<paramref name="info"/> 为 null 时返回 <see cref="Empty"/>。
    /// </summary>
    /// <param name="info">已解析的设备信息。</param>
    /// <returns>能力快照。</returns>
    public static AnShengCapabilitySnapshot FromDevInfo(AnShengDevInfo? info)
    {
        if (info == null)
        {
            return Empty;
        }

        return new AnShengCapabilitySnapshot(
            NetType: info.NetType,
            SlotAmount: info.SlotAmount,
            PhaseAmount: info.PhaseAmount,
            Version: info.Version,
            Model: info.Model,
            Iccid: info.Iccid);
    }

    /// <summary>
    /// 由 <c>getDevStatus</c> 应答构造快照。<paramref name="status"/> 为 null 时返回 <see cref="Empty"/>。
    ///
    /// 【SlotAmount 的取值优先级】显式的 <c>slotAmount</c> 优先；
    /// 没有时退而求其次用 <c>slots</c>/<c>EMdata</c> 数组长度（<c>SlotCount</c>），
    /// 但数组长度为 0 时视为「没探到」而非「确认 0 路」——
    /// 设备可能只是这一帧没带数组，报 0 会把开关款误判成喇叭款。
    /// </summary>
    /// <param name="status">已解析的设备状态。</param>
    /// <returns>能力快照。</returns>
    public static AnShengCapabilitySnapshot FromDevStatus(AnShengDevStatus? status)
    {
        if (status == null)
        {
            return Empty;
        }

        var slotAmount = status.SlotAmount
                         ?? (status.SlotCount > 0 ? status.SlotCount : (int?)null);

        return new AnShengCapabilitySnapshot(
            NetType: status.NetType,
            SlotAmount: slotAmount,
            Version: status.Version,
            Model: status.Model,
            Iccid: status.Iccid,
            Signal: status.Signal);
    }
}

/// <summary>
/// 安圣设备能力档案服务 —— Profile 的唯一写入口与品类判定权威。
///
/// 【生命周期】Scoped。内部持有 <c>AppDbContext</c>，不可被 Singleton 直接注入；
///   后台服务请通过 <c>IServiceScopeFactory.CreateScope()</c> 取用。
///
/// 【null 容忍是硬约束】
///   产品决策 Q5 明确「存量不回填」——T5 之前认领的设备<b>没有</b> Profile 行。
///   因此 <see cref="GetByImeiAsync"/> / <see cref="GetByDeviceIdAsync"/> 返回 <c>null</c>
///   是完全正常的业务状态，调用方必须走降级分支（放行 + 告警），<b>不得</b>抛异常。
/// </summary>
public interface IAnShengDeviceProfileService
{
    /// <summary>
    /// 按 IMEI 查档案。
    /// </summary>
    /// <param name="imei">设备 IMEI。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>档案；不存在（含存量未回填）返回 <c>null</c>。</returns>
    Task<AnShengDeviceProfile?> GetByImeiAsync(string imei, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按设备主键查档案。
    /// </summary>
    /// <param name="deviceId">正式设备主键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>档案；不存在返回 <c>null</c>。</returns>
    Task<AnShengDeviceProfile?> GetByDeviceIdAsync(long deviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 取档案，没有就建一份空档案（不落库，由调用方统一 SaveChanges）。
    /// </summary>
    /// <param name="imei">设备 IMEI。</param>
    /// <param name="appCode">租户码。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>已存在或新建（已 Add 到变更追踪）的档案实例。</returns>
    Task<AnShengDeviceProfile> GetOrCreateAsync(
        string imei,
        string appCode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 判定品类。<b>一级 Manual 权威</b>：档案里已是人工指定且非 Unknown 时，
    /// 直接返回既有值，完全跳过推断。
    /// </summary>
    /// <param name="profile">现有档案；<c>null</c> 表示尚无档案。</param>
    /// <param name="snapshot">本次探测/上行得到的能力快照。</param>
    /// <param name="manualKind">人工指定的品类；<c>null</c> 或 Unknown 表示未指定。</param>
    /// <returns>判定结果（品类 + 来源）。</returns>
    (AnShengDeviceKind Kind, AnShengKindSource Source) ResolveKind(
        AnShengDeviceProfile? profile,
        AnShengCapabilitySnapshot snapshot,
        AnShengDeviceKind? manualKind = null);

    /// <summary>
    /// 把一次成功探测的结果写进档案（含品类判定、快照合并、静态品类字典同步）。
    /// 不调用 SaveChanges，由调用方决定事务边界。
    /// </summary>
    /// <param name="imei">设备 IMEI。</param>
    /// <param name="appCode">租户码。</param>
    /// <param name="snapshot">探测得到的能力快照。</param>
    /// <param name="manualKind">人工指定的品类，可为空。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>更新后的档案。</returns>
    Task<AnShengDeviceProfile> ApplyProbeAsync(
        string imei,
        string appCode,
        AnShengCapabilitySnapshot snapshot,
        AnShengDeviceKind? manualKind = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 记录一次探测失败。只更新状态/错因/时间三个字段，<b>不清空</b>已有能力信息——
    /// 上次探到的数据依然是当前最可信的已知事实。
    /// </summary>
    /// <param name="imei">设备 IMEI。</param>
    /// <param name="appCode">租户码。</param>
    /// <param name="error">失败原因摘要。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>更新后的档案。</returns>
    Task<AnShengDeviceProfile> ApplyProbeFailureAsync(
        string imei,
        string appCode,
        string? error,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 用上行报文自学习到的信息刷新档案（可信度低于探测，不会覆盖 Manual 来源）。
    /// 不调用 SaveChanges。
    ///
    /// 【决策 A（主理人裁定，必须采纳）】档案不存在时返回 <c>null</c>，<b>绝不建档</b>。
    /// 未认领设备继续留在 <c>DiscoveredAnShengDevice</c> 池等认领；
    /// 认领流程（T5 强制 <c>getDevInfo</c>+<c>getDevStatus</c>）才是档案的唯一创建入口。
    /// </summary>
    /// <param name="imei">设备 IMEI。</param>
    /// <param name="appCode">租户码。</param>
    /// <param name="snapshot">上行得到的能力快照。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>已存在的档案；不存在返回 <c>null</c>（不隐式创建）。</returns>
    Task<AnShengDeviceProfile?> RefreshAsync(
        string imei,
        string appCode,
        AnShengCapabilitySnapshot snapshot,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 认领成功后把档案挂到正式设备上。不调用 SaveChanges。
    /// </summary>
    /// <param name="profile">目标档案。</param>
    /// <param name="deviceId">正式设备主键。</param>
    void AttachDevice(AnShengDeviceProfile profile, long deviceId);
}
