using Xunit;

namespace IoTPlatform.AnSheng.Tests;

/// <summary>
/// 「触碰进程级静态状态」的测试集合。
///
/// 【为什么需要它】
///   xUnit 默认让<b>不同测试类</b>并行跑。而下列静态状态是进程唯一的：
///     · <c>AnShengUplinkHub.Uplink</c>（静态事件，<c>AnShengProbeServiceTests</c> 会 Reset/Publish）
///     · <c>AnShengMqttProtocolAdapter.DeviceKinds</c>（静态字典，<c>AnShengDeviceProfileServiceTests</c> 会 Clear/Register）
///   两个类若并行执行，A 的 <c>Reset()</c> 会掀掉 B 刚建立的订阅，测试结果随机漂移。
///   把它们归进同一个禁并行集合，是最省事且不改生产代码的隔离方式。
///
/// 【为什么不改成实例化的总线】
///   总线是静态的这件事本身是设计决定（见 <c>AnShengUplinkHub</c> 类注释：
///   适配器由工厂懒创建，Singleton 探测服务拿不到实例）。
///   为了测试便利去改生产架构是本末倒置，这里用测试侧的串行化解决。
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AnShengStaticStateCollection
{
    /// <summary>集合名称。</summary>
    public const string Name = "AnSheng-StaticState";
}
