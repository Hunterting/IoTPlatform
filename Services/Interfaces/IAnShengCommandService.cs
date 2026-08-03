using IoTPlatform.DTOs.Responses;

namespace IoTPlatform.Services;

/// <summary>
/// 安圣 MQTT 设备命令服务接口
/// 通过 IProtocolAdapterFactory 获取适配器并下发安圣协议命令
/// </summary>
public interface IAnShengCommandService
{
    /// <summary>
    /// 向安圣设备发送原生命令
    /// </summary>
    /// <param name="deviceId">设备 ID</param>
    /// <param name="method">安圣 method（如 getDevStatus, setAutoReport, orderStart）</param>
    /// <param name="parameters">参数字典，可为 null</param>
    /// <param name="ct">取消令牌</param>
    /// <returns></returns>
    Task<AnShengCommandResponse> SendCommandAsync(long deviceId, string method,
        Dictionary<string, object?>? parameters = null, CancellationToken ct = default);

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
