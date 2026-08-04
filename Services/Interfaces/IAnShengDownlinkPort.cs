// 创建于 T7-2。解决设计文档 §1.3 记录的硬约束 N1。
//
// 【为什么需要这个接口】
//   IProtocolAdapter.SendCommandAsync 的签名是「返回 frameId，但不接受 frameId 入参」——
//   frameId 由适配器内部的 AnShengCommandBuilder.NewFrameId() 生成，
//   调用方拿到它的那一刻，报文已经通过 MQTT 发出去了（F4）。
//
//   这导致「先登记在途、后下发」在物理上无法实现：
//     · 只能「先发后登记」；
//     · 而设备应答最快可在毫秒级返回（测试替身甚至是同步回上行）；
//     · 应答先于登记到达 ⇒ AnShengMessageRouter.Classify 查在途表落空
//       ⇒ 误判为 AutoReport ⇒ 命令记录永远等到 TTL 超时。
//
//   本接口把 frameId 的「生成权」上移到 AnShengCommandService：
//   Service 先 NewFrameId() → 先 RegisterAsync 登记在途 → 再 PublishAsync 下发，
//   竞态窗口被彻底消除。
//
// 【为什么是窄接口而不是改 IProtocolAdapter】
//   改 IProtocolAdapter 会波及 Modbus / OPC UA / 通用 MQTT 等全部适配器，
//   而 frameId 预登记是安圣二开协议独有的需求。按接口隔离原则单开窄接口，
//   只由 AnShengMqttProtocolAdapter 与测试替身 RecordingAnShengAdapter 实现。
//
// 【降级路径】Service 侧用 `adapter is IAnShengDownlinkPort port` 做模式匹配：
//   命中 → 先登记后下发；未命中 → 退回「先发后登记」并记 Warning（风险登记 R2），
//   功能不中断，只是回到 T7 之前的竞态水平。

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IoTPlatform.Services.Interfaces;

/// <summary>
/// 安圣下行接缝 —— 允许调用方<b>指定 frameId</b> 下发命令。
///
/// 与 <c>IProtocolAdapter.SendCommandAsync</c> 的唯一区别是多了 <c>frameId</c> 入参，
/// 从而让「先登记在途表、后发 MQTT」成为可能（见文件头 N1 说明）。
/// 报文构建与限流逻辑与 <c>SendCommandAsync</c> <b>完全共用同一份实现</b>，
/// 保证两个入口产出的报文除 frameId 外字节级一致。
/// </summary>
public interface IAnShengDownlinkPort
{
    /// <summary>
    /// 以指定 frameId 下发一条安圣命令。
    /// </summary>
    /// <param name="deviceId">设备主键（仅用于日志关联）。</param>
    /// <param name="imei">目标设备 IMEI。</param>
    /// <param name="method">协议方法名，如 <c>action</c>。</param>
    /// <param name="parameters">平铺参数字典，可为 null（无参命令）。值为 null 的项会被剔除。</param>
    /// <param name="frameId">
    /// 调用方预先生成并已登记进在途表的帧 ID；<b>不得为空</b>。
    /// 若为空则无法与在途条目关联，命令必然走到超时兜底，属于调用方错误。
    /// </param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>实际下发所用的 frameId（正常情况下与入参相同）。</returns>
    Task<string> PublishAsync(
        long deviceId,
        string imei,
        string method,
        IReadOnlyDictionary<string, object?>? parameters,
        string frameId,
        CancellationToken cancellationToken = default);
}
