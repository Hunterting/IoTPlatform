using IoTPlatform.IntegrationTests.Infrastructure;
using Xunit;

namespace IoTPlatform.IntegrationTests.Collections;

/// <summary>
/// 集成测试集合定义：整个测试运行共享同一个 <see cref="DatabaseFixture"/>
/// （= 一个一次性 MySQL schema + 一个 TestServer）。
///
/// 【为什么禁用并行】三重共享状态：
///   1. 生产侧静态字典（<c>AnShengMqttProtocolAdapter.DeviceKinds</c> / <c>AnShengCommandService.FrameIdCommandIdMap</c>）；
///   2. 单一测试 schema（Respawn 清库是全库级操作）；
///   3. EF 模型缓存会冻结租户过滤器的 AppCode。
/// 另见 <c>xunit.runner.json</c> 的 <c>parallelizeTestCollections: false</c>。
/// </summary>
[CollectionDefinition(SharedTestConstants.CollectionName, DisableParallelization = true)]
public sealed class IntegrationTestCollection : ICollectionFixture<DatabaseFixture>
{
    // 该类没有任何代码，仅作为 [CollectionDefinition] 与 ICollectionFixture<> 的载体。
    // xUnit 依据它把 DatabaseFixture 的生命周期提升到「集合级」。
}
