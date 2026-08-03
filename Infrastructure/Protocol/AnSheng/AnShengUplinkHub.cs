using System;

namespace IoTPlatform.Infrastructure.Protocol.AnSheng;

/// <summary>
/// 一条安圣设备上行报文的事件载荷。
/// </summary>
public class AnShengUplinkEventArgs : EventArgs
{
    /// <summary>设备 IMEI。</summary>
    public string Imei { get; init; } = string.Empty;

    /// <summary>报文方法名，如 <c>getDevInfo</c> / <c>getDevStatus</c>。</summary>
    public string Method { get; init; } = string.Empty;

    /// <summary>已解析的报文对象；解析失败时为 <c>null</c>。</summary>
    public AnShengMessage? Message { get; init; }

    /// <summary>原始 JSON payload，供排障与二次解析。</summary>
    public string? RawPayload { get; init; }

    /// <summary>收到时间（UTC）。</summary>
    public DateTime ReceivedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// 安圣上行报文总线 —— 进程内的静态发布/订阅点。
///
/// 【为什么需要它，而不是让探测服务直接订阅适配器】
///   探测服务要等的是「某台设备对某个方法的应答」。可它拿不到适配器实例：
///   适配器由 <c>IProtocolAdapterFactory</c> 按 configId 懒创建，
///   探测服务是 Singleton，启动时根本不知道会有哪些 configId、也不该去 new 一个。
///   适配器已有 <c>DeviceWill</c> 静态事件的先例，这里沿用同一模式：
///   <b>适配器只管发布，谁订阅、订阅几个，它一概不知</b>。
///
/// 【为什么放在协议层而不是服务层】
///   发布方（适配器）在 <c>Infrastructure.Protocol.Adapters</c>，
///   订阅方（探测服务）在 <c>Services</c>。总线放服务层会让基础设施反向依赖业务层。
///
/// 【异常隔离是硬要求】
///   <see cref="Publish"/> 跑在 MQTT 接收线程上。任何一个订阅者抛异常都必须
///   在这里被吞掉——否则一个订阅者的 bug 会打断整条 MQTT 上行链路，
///   造成全平台设备"集体离线"。这是本类唯一允许 catch-all 的地方。
/// </summary>
public static class AnShengUplinkHub
{
    /// <summary>
    /// 上行报文事件。订阅者应保证回调足够快且不抛异常。
    /// </summary>
    public static event EventHandler<AnShengUplinkEventArgs>? Uplink;

    /// <summary>
    /// 发布一条上行报文。
    ///
    /// 【调用位置】<c>AnShengMqttProtocolAdapter.OnMessageReceivedAsync</c> 中
    ///   <c>LearnDeviceKind</c> 之后、Will 判定之前。
    ///   放在 Will 判定之前是因为 Will 分支会提前 <c>return</c>，
    ///   放后面会让"设备刚上线就掉线"这类报文丢失。
    /// </summary>
    /// <param name="imei">设备 IMEI；为空则直接忽略（无法关联到任何等待者）。</param>
    /// <param name="method">报文方法名；为空则直接忽略。</param>
    /// <param name="message">已解析的报文对象，可为空。</param>
    /// <param name="rawPayload">原始 JSON payload，可为空。</param>
    public static void Publish(string? imei, string? method, AnShengMessage? message, string? rawPayload = null)
    {
        if (string.IsNullOrWhiteSpace(imei) || string.IsNullOrWhiteSpace(method))
        {
            return;
        }

        var handler = Uplink;
        if (handler == null)
        {
            return;
        }

        var args = new AnShengUplinkEventArgs
        {
            Imei = imei,
            Method = method,
            Message = message,
            RawPayload = rawPayload,
            ReceivedAt = DateTime.UtcNow
        };

        // 逐个调用而不是 handler.Invoke：多播委托里任何一个抛异常，
        // 后面的订阅者就再也收不到了。这里做到"一个坏订阅者不影响其他人"。
        foreach (var d in handler.GetInvocationList())
        {
            try
            {
                ((EventHandler<AnShengUplinkEventArgs>)d)(null, args);
            }
            catch
            {
                // 故意吞掉：本方法运行在 MQTT 接收线程，异常外泄会打断整条上行链路。
                // 订阅者自己该记日志的在自己那边记。
            }
        }
    }

    /// <summary>
    /// 清空所有订阅者。
    ///
    /// 【⚠ 仅供单元测试使用】
    ///   集成测试<b>绝对不能</b>调用本方法：<c>AnShengProbeService</c> 是 Singleton，
    ///   在构造时订阅本总线，而 TestServer 全程只有一个。
    ///   一旦清空，后续所有用例的探测都会永久超时。
    ///   集成测试的隔离手段是 <c>IAnShengProbeService.ClearPending()</c>。
    /// </summary>
    public static void Reset() => Uplink = null;
}
