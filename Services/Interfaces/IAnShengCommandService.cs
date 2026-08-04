using IoTPlatform.DTOs.Responses;
using IoTPlatform.Models;

namespace IoTPlatform.Services;

/// <summary>
/// 安圣 MQTT 设备命令服务接口
/// 通过 IProtocolAdapterFactory 获取适配器并下发安圣协议命令
/// </summary>
public interface IAnShengCommandService
{
    /// <summary>
    /// 向安圣设备发送原生命令。
    ///
    /// 【T7 起本方法是「有校验、有登记、有留痕」的完整受理入口】
    ///   ① <c>AnShengCommandGuard</c> 单点校验（品类 / 参数 / 插槽 / 固件）；
    ///   ② 写 <c>AnShengCommandRecord</c>（Pending）；
    ///   ③ 先登记在途表、后发 MQTT（消除应答早于登记的竞态，硬约束 N1）；
    ///   ④ 置 Sent 并算出 <c>TimeoutAt</c>，交由应答路径或超时清扫写终态。
    /// 被 Guard 拒绝时<b>零 MQTT 发布、零在途登记、零 FrameId</b>，仅落一条 Rejected 记录。
    /// </summary>
    /// <param name="deviceId">设备 ID</param>
    /// <param name="method">安圣 method（如 getDevStatus, setAutoReport, orderStart）</param>
    /// <param name="parameters">参数字典，可为 null</param>
    /// <param name="ct">取消令牌</param>
    /// <param name="commandId">
    /// 平台命令标识（GUID 字符串）。由 <c>DeviceCommandService</c> 透传
    /// <c>DeviceCommand.CommandId</c>，使两表以同一个值软关联（决策 D6，取代已删除的静态
    /// <c>FrameIdCommandIdMap</c>）。传 null 时由本服务自行生成。
    /// <b>放在 <paramref name="ct"/> 之后</b>是为了不破坏既有位置参数调用点。
    /// </param>
    /// <returns>下发结果；被拒绝时带 <c>RejectReason</c> 与 <c>Errors</c>。</returns>
    Task<AnShengCommandResponse> SendCommandAsync(long deviceId, string method,
        Dictionary<string, object?>? parameters = null, CancellationToken ct = default,
        string? commandId = null);

    /// <summary>
    /// 配置设备自动上报间隔并下发 setAutoReport 命令
    /// </summary>
    /// <param name="deviceId">设备 ID</param>
    /// <param name="settings">自动上报设置（getDevStatusSec / orderUpSec / rs485Sec）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>包含 FrameId 和下发结果的响应</returns>
    Task<AnShengCommandResponse> ConfigureAutoReportAsync(long deviceId,
        AnShengAutoReportSettings settings, CancellationToken ct = default);

    /// <summary>
    /// 触发设备发现：广播 getDevStatus 命令到所有未知 IMEI
    /// </summary>
    /// <param name="ct">取消令牌</param>
    Task TriggerDiscoveryAsync(CancellationToken ct = default);

    // ─── 二开设备通用命令 ───
    // 开关通断请使用 SendCommandAsync(deviceId, "action", new() { ["slotNum"] = 1, ["action"] = "on" })，
    // 批量通断使用 "actions"。官方协议 asopen.md 中不存在 setSwitch / getSwitchStatus /
    // setSwitchConfig / getSwitchConfig，对应历史臆造接口已删除。

    /// <summary>远程重启二开设备</summary>
    Task<AnShengCommandResponse> RebootDeviceAsync(long deviceId, CancellationToken ct = default);

    /// <summary>
    /// 按平台命令标识查询单条命令记录（T7-5 只读 API <c>GET /commands/{commandId}</c> 用）。
    ///
    /// 【租户隔离】在请求作用域内调用，<c>AppDbContext</c> 全局过滤器会自动按当前租户过滤，
    /// 跨租户查询自然落空返回 null。
    /// </summary>
    /// <param name="commandId">平台命令标识（与 <c>DeviceCommand.CommandId</c> 同值）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>命令记录；不存在（含跨租户）返回 null。</returns>
    Task<AnShengCommandRecord?> GetRecordAsync(string commandId, CancellationToken ct = default);
}

/// <summary>
/// 安圣自动上报配置参数
/// </summary>
public class AnShengAutoReportSettings
{
    /// <summary>状态上报间隔（秒）</summary>
    public int? GetDevStatusSec { get; set; } = 60;

    /// <summary>额外查询参数（如 "temperature,EMdata"）</summary>
    public string? GetDevStatusQ { get; set; }

    /// <summary>订单进度上报间隔（秒）</summary>
    public int? OrderUpSec { get; set; } = 300;

    /// <summary>RS485 轮询间隔（秒），0=关闭</summary>
    public int? Rs485Sec { get; set; } = 0;
}
