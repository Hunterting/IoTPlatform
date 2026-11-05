using System.Threading.Tasks;
using FluentAssertions;
using IoTPlatform.IntegrationTests.Infrastructure;
using IoTPlatform.IntegrationTests.Infrastructure.Mqtt;
using IoTPlatform.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IoTPlatform.IntegrationTests.Protocol;

/// <summary>
/// <see cref="ProtocolConfigAutoStartHostedService"/> 的集成验收：cold start 自动拉起。
///
/// 【为什么不直接驱动 IHostedService】
///   <see cref="Infrastructure.TestWebAppFactory"/> 的 RemoveBackgroundServices 会摘除除
///   <c>AnShengCommandSweepHostedService</c> 之外的所有 <see cref="IHostedService"/>，
///   因此宿主里的自动拉起逻辑在集成环境不会自动跑。这里直接调用
///   <see cref="IProtocolConfigService.AutoStartActiveProtocolsAsync"/>，检验
///   「跨租户枚举 IsActive 配置 + 逐个启动」这一真正被测单元。
///
/// 【断言口径】
///   <see cref="IoTPlatform.Data.Seed.SeedData"/> 播种的协议配置 IsActive=true 且 Status="active"。
///   AutoStart 经 <c>GetActiveProtocolConfigsForAutoStartAsync</c>
///   （IgnoreQueryFilters + Where IsActive）枚举到它，并调用 <c>StartProtocolCoreAsync</c>。
///   为让启动产生可观测副作用（而非被「DB=active 且适配器在内存」的双条件短路吞掉），
///   用例先 <see cref="FakeProtocolAdapterFactory.SimulateAdapterAbsent"/> —— 模拟进程重启后
///   适配器不在内存，逼出完整启动流程，最终断言
///   <see cref="RecordingAnShengAdapter.DataCollectionStarted"/>==true（与缺口 A-1 同一锚点）。
/// </summary>
[Collection(SharedTestConstants.CollectionName)]
public sealed class ProtocolConfigAutoStartTests : IntegrationTestBase
{
    /// <summary>本用例播种出的协议配置主键（生产侧按 <c>int</c> 使用）。</summary>
    private int ConfigId => (int)Seed.ProtocolConfigId;

    /// <summary>替身工厂：失配现场的开关面板。</summary>
    private FakeProtocolAdapterFactory AdapterFactory => Fixture.AdapterFactory;

    public ProtocolConfigAutoStartTests(DatabaseFixture fixture) : base(fixture)
    {
    }

    /// <summary>
    /// cold start 自动拉起：已激活协议配置必须被枚举并真正启动（采集启动 + 状态自洽）。
    ///
    /// 【为什么要先 SimulateAdapterAbsent】
    ///   替身默认对任意 configId 都返回内存中的适配器，若不打缺席标记，
    ///   <c>StartProtocolCoreAsync</c> 的「双条件短路」会直接 return，不产生任何可观测副作用，
    ///   用例就退化为「只验证没崩溃」的假绿。模拟进程重启后再自动拉起，
    ///   才会真实走「恢复启动」分支，把适配器真正建起来。
    /// </summary>
    [Fact(DisplayName = "cold start 自动拉起：已激活协议配置被枚举并真正启动(采集启动+状态自洽)")]
    public async Task AutoStart_Should_StartActiveProtocolConfigs()
    {
        // ── Arrange：模拟「适配器不在进程内存」（进程重启后适配器丢失）──
        AdapterFactory.SimulateAdapterAbsent(ConfigId);
        Adapter.DataCollectionStarted.Should().BeFalse("前置：尚未自动拉起，采集标志必须为 false");

        var initialState = await ReadProtocolStatusAsync();
        initialState.Should().Be(
            "active", "SeedData 播种的协议配置本就是 active 且 IsActive=true，必须被枚举命中");

        // ── Act：直接调用 AutoStart（宿主侧 IHostedService 在集成环境被摘除，不会自动跑）──
        await using (var scope = CreateScope())
        {
            var protocolService = scope.ServiceProvider.GetRequiredService<IProtocolConfigService>();
            await protocolService.AutoStartActiveProtocolsAsync();
        }

        // ── Assert ① 配置被真正拉起（恢复启动路径）──
        Adapter.DataCollectionStarted.Should().BeTrue(
            "AutoStart 必须枚举 IsActive=true 的配置并走完整启动流程；" +
            "若为 false，说明配置没被拉起（枚举漏了、被短路吞掉，或启动失败被静默吞下）");

        // ── Assert ② CreateAdapter 被调用（缺席标记被撤销，与缺口 A-1 同一证据链）──
        AdapterFactory.IsSimulatedAbsent(ConfigId).Should().BeFalse(
            "恢复启动会经 CreateAdapter 撤销缺席标记；标记仍在说明配置根本没被处理");

        // ── Assert ③ 收尾状态自洽 ──
        var finalState = await ReadProtocolStateAsync();
        finalState.Status.Should().Be("active", "自动拉起结束后 DB 状态应保持 active");
        finalState.IsActive.Should().BeTrue("Status 与 IsActive 必须同步，否则消费侧筛不到这条配置");
    }

    /// <summary>读取协议配置当前 <c>Status</c>。</summary>
    private Task<string> ReadProtocolStatusAsync() =>
        QueryDbAsync(async db =>
        {
            var status = await db.ProtocolConfigs
                .AsNoTracking()
                .Where(c => c.Id == Seed.ProtocolConfigId)
                .Select(c => c.Status)
                .SingleAsync();
            return status;
        });

    /// <summary>读取协议配置当前的 <c>(Status, IsActive)</c>。</summary>
    private Task<(string Status, bool IsActive)> ReadProtocolStateAsync() =>
        QueryDbAsync(async db =>
        {
            var row = await db.ProtocolConfigs
                .AsNoTracking()
                .Where(c => c.Id == Seed.ProtocolConfigId)
                .Select(c => new { c.Status, c.IsActive })
                .SingleAsync();
            return (row.Status, row.IsActive);
        });
}
