// 水流量自动采集轮询宿主的纯函数验收单测。
//
// 【为什么测纯函数而不是整宿主】
//   AnShengWaterFlowPollingHostedService 的循环 / scope / 下发部分依赖
//   AppDbContext + IAnShengWaterFlowService + 命令链路，属于集成测试的领地；
//   而「间隔钳制」「水设备判定」「品类跳过」是毫秒级、无 DB、无 MQTT 的纯判定，
//   任何一条被实现改坏都应当在这里变红（同 AnShengCommandGuardTests 的口径）。
//
// 【断言口径】一律断言「设计要求的行为」：
//   · 间隔：默认 60、≤10s 钳到 10s、0/负数回落默认 60 —— 防打爆设备是硬约束；
//   · 水判定：元素级精确匹配（["watermeter"] 不是水设备）、大小写不敏感、
//     兼容裸值 / 逗号串的历史格式；
//   · 品类跳过：仅喇叭跳过，Unknown 不跳（D7 哲学：档案缺失不打死存量设备）。

using System;
using IoTPlatform.Configuration;
using IoTPlatform.Infrastructure.Protocol.AnSheng;
using IoTPlatform.Services;
using Xunit;

namespace IoTPlatform.AnSheng.Tests;

/// <summary>
/// <see cref="AnShengWaterFlowPollingHostedService"/> 纯函数 与
/// <see cref="AnShengWaterFlowOptions"/> 归一化逻辑的验收单测。
/// </summary>
public class AnShengWaterFlowPollingTests
{
    // ─────────────────────────────────────────────
    // 间隔钳制（AnShengWaterFlowOptions）
    // ─────────────────────────────────────────────

    [Fact]
    public void Options_Defaults_PollInterval_Is60Seconds()
    {
        var options = new AnShengWaterFlowOptions();

        Assert.Equal(60, options.PollIntervalSeconds);
        Assert.Equal(60, options.EffectivePollIntervalSeconds);
        Assert.True(options.PollEnabled);
        Assert.Equal(20, options.InitialDelaySeconds);
    }

    [Theory]
    [InlineData(0, 60)]      // 误配 0 ⇒ 回落默认 60
    [InlineData(-5, 60)]     // 负数 ⇒ 回落默认 60
    [InlineData(1, 10)]      // ≤10s ⇒ 钳到 10s 下限
    [InlineData(5, 10)]
    [InlineData(10, 10)]     // 恰好 10s：下限本身合法
    [InlineData(11, 11)]     // >10s 原值生效
    [InlineData(60, 60)]
    [InlineData(300, 300)]
    public void Options_EffectiveInterval_ClampsAndFallsBack(int configured, int expected)
    {
        var options = new AnShengWaterFlowOptions { PollIntervalSeconds = configured };

        Assert.Equal(expected, options.EffectivePollIntervalSeconds);
    }

    [Fact]
    public void Options_InitialDelay_NegativeClampedToZero()
    {
        var options = new AnShengWaterFlowOptions { InitialDelaySeconds = -1 };

        Assert.Equal(0, options.EffectiveInitialDelaySeconds);
    }

    // ─────────────────────────────────────────────
    // 水设备判定（IsWaterEnergyDevice）
    // ─────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("[\"electric\"]")]
    [InlineData("[\"electric\",\"gas\"]")]
    [InlineData("electric")]
    [InlineData("gas,electric")]
    public void IsWaterEnergyDevice_NonWater_ReturnsFalse(string? energyTypes)
    {
        Assert.False(AnShengWaterFlowPollingHostedService.IsWaterEnergyDevice(energyTypes));
    }

    [Theory]
    [InlineData("[\"water\"]")]
    [InlineData("[\"electric\",\"water\"]")]
    [InlineData("[\"WATER\"]")]                    // 大小写不敏感
    [InlineData("[\"Electric\",\"Water\"]")]
    [InlineData("water")]                          // 裸值（历史格式）
    [InlineData("electric,water")]                 // 逗号串（历史格式）
    [InlineData(" Water ")]                        // 前后空白
    public void IsWaterEnergyDevice_Water_ReturnsTrue(string energyTypes)
    {
        Assert.True(AnShengWaterFlowPollingHostedService.IsWaterEnergyDevice(energyTypes));
    }

    [Fact]
    public void IsWaterEnergyDevice_ElementLevelMatch_DoesNotMatchPrefixes()
    {
        // 元素级精确匹配：watermeter 不是 water，裸 Contains 会在这里误判。
        Assert.False(AnShengWaterFlowPollingHostedService.IsWaterEnergyDevice("[\"watermeter\"]"));
        Assert.False(AnShengWaterFlowPollingHostedService.IsWaterEnergyDevice("[\"hotwater\"]"));
    }

    [Fact]
    public void IsWaterEnergyDevice_MalformedJson_FallsBackSafely()
    {
        // 畸形 JSON 不抛异常、按兜底逗号拆分处理（匹配不到即 false）。
        Assert.False(AnShengWaterFlowPollingHostedService.IsWaterEnergyDevice("[\"water\","));
        // 兜底分支能识别逗号串里的 water。
        Assert.True(AnShengWaterFlowPollingHostedService.IsWaterEnergyDevice("[broken,water"));
    }

    // ─────────────────────────────────────────────
    // 品类跳过（ShouldSkipForPolling）
    // ─────────────────────────────────────────────

    [Theory]
    [InlineData(AnShengDeviceKind.Speaker4G)]
    [InlineData(AnShengDeviceKind.SpeakerWiFi)]
    public void ShouldSkipForPolling_SpeakerKinds_Skipped(AnShengDeviceKind kind)
    {
        // getFlowInfo 属 GroupSwitchAction 能力组，喇叭款结构性不支持。
        Assert.True(AnShengWaterFlowPollingHostedService.ShouldSkipForPolling(kind));
    }

    [Theory]
    [InlineData(AnShengDeviceKind.Switch4G)]
    [InlineData(AnShengDeviceKind.SwitchWiFi)]
    [InlineData(AnShengDeviceKind.Unknown)]
    public void ShouldSkipForPolling_SwitchAndUnknown_NotSkipped(AnShengDeviceKind kind)
    {
        // Unknown 不跳过：D7 哲学 —— 档案缺失不得打死存量设备的自动采集。
        Assert.False(AnShengWaterFlowPollingHostedService.ShouldSkipForPolling(kind));
    }
}
