using IoTPlatform.Infrastructure.Protocol.AnSheng;
using System.Threading;
using System.Threading.Tasks;

namespace IoTPlatform.Services;

/// <summary>
/// 一次设备探测的结果。
///
/// 【为什么是"返回失败对象"而不是"抛异常"】
///   探测失败是<b>预期内的业务分支</b>（设备离线、信号差、固件老），不是程序错误。
///   用异常表达会逼着每个调用方写 try/catch，还容易被上层的全局异常处理器
///   翻译成 HTTP 500 —— 而正确的语义是 HTTP 200 + 业务错误码。
/// </summary>
public sealed class AnShengProbeResult
{
    /// <summary>探测是否成功（两条指令至少 getDevInfo 拿到了有效应答）。</summary>
    public bool Success { get; init; }

    /// <summary><c>getDevInfo</c> 应答；未拿到为 <c>null</c>。</summary>
    public AnShengDevInfo? DevInfo { get; init; }

    /// <summary><c>getDevStatus</c> 应答；未拿到为 <c>null</c>。</summary>
    public AnShengDevStatus? DevStatus { get; init; }

    /// <summary>失败原因摘要；成功时为 <c>null</c>。</summary>
    public string? Error { get; init; }

    /// <summary>
    /// 构造成功结果。
    /// </summary>
    /// <param name="devInfo">设备信息应答。</param>
    /// <param name="devStatus">设备状态应答，可为空（状态超时不影响整体成功）。</param>
    /// <returns>成功结果。</returns>
    public static AnShengProbeResult Ok(AnShengDevInfo? devInfo, AnShengDevStatus? devStatus)
        => new() { Success = true, DevInfo = devInfo, DevStatus = devStatus };

    /// <summary>
    /// 构造失败结果。
    /// </summary>
    /// <param name="error">失败原因摘要。</param>
    /// <returns>失败结果。</returns>
    public static AnShengProbeResult Fail(string error)
        => new() { Success = false, Error = error };
}

/// <summary>
/// 安圣设备主动探测服务 —— 「问设备你是谁」。
///
/// 【生命周期】Singleton。它在构造时订阅 <c>AnShengUplinkHub</c>，
///   全进程只能有一份订阅，否则同一条上行会被消费多次。
///
/// 【它不碰数据库】探测只负责「发指令 + 等应答 + 解析」，
///   落库由 <c>IAnShengDeviceProfileService</c> 负责。
///   这样探测可以在数据库事务<b>之外</b>执行——5~10 秒的等待绝不能占着事务与连接。
/// </summary>
public interface IAnShengProbeService
{
    /// <summary>
    /// 串行探测一台设备：先 <c>getDevInfo</c>，再 <c>getDevStatus</c>。
    /// </summary>
    /// <param name="protocolConfigId">协议配置主键，用于取适配器。</param>
    /// <param name="imei">设备 IMEI。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>探测结果。<b>不抛业务异常</b>，失败以 <see cref="AnShengProbeResult.Success"/> = false 表达。</returns>
    Task<AnShengProbeResult> ProbeAsync(
        int protocolConfigId,
        string imei,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 清空所有在途等待。
    ///
    /// 【用途】集成测试用例间隔离。
    ///   本服务是 Singleton 且订阅静态总线，用例 A 遗留的未完成等待会串扰用例 B。
    ///   <b>不要</b>用 <c>AnShengUplinkHub.Reset()</c> 做隔离——那会连订阅一起清掉，
    ///   而 Singleton 不会被重建，后续用例将永久收不到上行。
    /// </summary>
    void ClearPending();
}
