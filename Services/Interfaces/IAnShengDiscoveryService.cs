namespace IoTPlatform.Services;

/// <summary>
/// 安圣设备发现服务接口
/// 负责：定时扫描未认领设备、处理 Will 离线通知、维护在线状态
/// </summary>
public interface IAnShengDiscoveryService
{
    /// <summary>
    /// 收到 Will 离线通知时调用（由适配器事件驱动）
    /// </summary>
    /// <param name="imei">离线设备 IMEI</param>
    /// <param name="appCode">租户 AppCode</param>
    /// <param name="ct">取消令牌</param>
    Task OnDeviceOfflineAsync(string imei, string? appCode, CancellationToken ct = default);

    /// <summary>
    /// 注册设备上线（收到数据时调用）
    /// </summary>
    /// <param name="imei">IMEI</param>
    /// <param name="model">设备型号（可选，从报文解析）</param>
    /// <param name="netType">网络类型（可选）</param>
    /// <param name="appCode">租户 AppCode</param>
    /// <param name="ct">取消令牌</param>
    Task OnDeviceOnlineAsync(string imei, string? model, string? netType, string? appCode, CancellationToken ct = default);
}
