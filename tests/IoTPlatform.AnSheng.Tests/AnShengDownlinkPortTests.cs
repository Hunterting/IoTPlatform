using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IoTPlatform.Infrastructure.Protocol.AnSheng;
using IoTPlatform.Infrastructure.Protocol.Adapters;
using IoTPlatform.Services.Interfaces;
using Xunit;

namespace IoTPlatform.AnSheng.Tests;

/// <summary>
/// T7-2 下行接缝 <see cref="IAnShengDownlinkPort"/> —— 单元测试。
///
/// 【为什么需要这条接缝】
///   <c>IProtocolAdapter.SendCommandAsync</c> 返回 frameId 但不接受 frameId，
///   frameId 在适配器内部生成 —— 调用方拿到它时报文已经发出去了（硬约束 N1）。
///   于是「先登记在途表、再下发」物理上做不到，只能先发后登记；
///   而设备应答可以毫秒级返回，一旦抢在登记之前到达，就会被路由判成主动上报，
///   命令记录停在 Pending 直到超时。本接缝把 frameId 的生成权上移，消除这个竞态。
///
/// 【本文件守住什么】
///   1. 适配器确实实现了该接缝（否则命令服务的 <c>is</c> 模式匹配会静默降级，谁都不会发现）；
///   2. 外部 frameId 必须<b>被真正采用</b>，绝不能悄悄换成自生成的——那等于登记的 key 永远对不上；
///   3. 两个入口（<c>SendCommandAsync</c> / <c>PublishAsync</c>）产出的报文<b>字节级一致</b>，
///      不允许出现第二条报文构建路径慢慢漂移。
/// </summary>
public sealed class AnShengDownlinkPortTests
{
    private const string Imei = "864536072949900";
    private const string PresetFrameId = "0123456789abcdef";

    // ─────────────────────────────────────────────────────────────
    // 接缝装配
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 适配器必须实现 <see cref="IAnShengDownlinkPort"/>。
    ///
    /// 命令服务用 <c>adapter is IAnShengDownlinkPort port</c> 做模式匹配，未命中会静默降级为
    /// 「先发后登记」。降级路径不抛异常、只记一条 Warning，人工几乎不可能察觉，
    /// 因此必须由测试在编译期之外再守一道。
    /// </summary>
    [Fact]
    public void Adapter_ImplementsDownlinkPort()
    {
        var adapter = new AnShengMqttProtocolAdapter(configId: 1);

        Assert.IsAssignableFrom<IAnShengDownlinkPort>(adapter);
    }

    /// <summary>
    /// 空 frameId 必须快速失败。
    ///
    /// 该接缝的语义就是「以<b>已登记</b>的 frameId 下发」；放空过去会让报文与在途条目对不上，
    /// 命令必然走到超时兜底——那是最难排查的一类故障，宁可在入口炸掉。
    /// 校验发生在连接检查之前，因此未连接的适配器也能验证这条契约。
    /// </summary>
    /// <param name="frameId">待校验的非法 frameId。</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PublishAsync_WithBlankFrameId_ThrowsArgumentException(string frameId)
    {
        IAnShengDownlinkPort port = new AnShengMqttProtocolAdapter(configId: 1);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            port.PublishAsync(1L, Imei, "action", null, frameId, CancellationToken.None));

        Assert.Equal("frameId", ex.ParamName);
    }

    /// <summary>未连接时下发应抛 <see cref="InvalidOperationException"/>，与 <c>SendCommandAsync</c> 行为一致。</summary>
    [Fact]
    public async Task PublishAsync_WhenNotConnected_ThrowsInvalidOperationException()
    {
        IAnShengDownlinkPort port = new AnShengMqttProtocolAdapter(configId: 1);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            port.PublishAsync(1L, Imei, "action", null, PresetFrameId, CancellationToken.None));
    }

    // ─────────────────────────────────────────────────────────────
    // 报文一致性：两个入口共用同一份构建实现
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 二开协议报文：给定相同入参，「外部指定 frameId」与「自生成 frameId」除该字段外<b>完全一致</b>。
    ///
    /// 断言方式是先自生成一次拿到 frameId，再用同一个 frameId 显式构建一次，要求两次输出字节相同 ——
    /// 这样连字段顺序、空值剔除、timestamp 注入策略的任何漂移都会被抓出来。
    /// 品类取 WiFi 开关：<c>action</c> 是开关类命令，且协议规定 WiFi 款不注入 timestamp，
    /// 输出因此是确定性的（4G 款会带秒级 timestamp，跨秒执行就会假失败）。
    /// </summary>
    [Fact]
    public void BuildCommand_ExternalFrameId_ProducesByteIdenticalPayload()
    {
        var builder = new AnShengCommandBuilder(null);
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["slotNum"] = 1,
            ["action"] = "on"
        };

        var (autoFrameId, autoPayload) = builder.BuildCommand(
            Imei, "action", parameters, AnShengDeviceKind.SwitchWiFi);

        var (echoedFrameId, echoedPayload) = builder.BuildCommand(
            Imei, "action", parameters, AnShengDeviceKind.SwitchWiFi, autoFrameId);

        Assert.Equal(autoFrameId, echoedFrameId);
        Assert.Equal(autoPayload, echoedPayload);
    }

    /// <summary>外部 frameId 必须原样出现在报文里，而不是被构建器换成自生成值。</summary>
    [Fact]
    public void BuildCommand_ExternalFrameId_IsActuallyUsed()
    {
        var builder = new AnShengCommandBuilder(null);

        var (frameId, payload) = builder.BuildCommand(
            Imei, "getDevStatus", null, AnShengDeviceKind.SpeakerWiFi, PresetFrameId);

        Assert.Equal(PresetFrameId, frameId);

        using var document = JsonDocument.Parse(payload);
        Assert.Equal(PresetFrameId, document.RootElement.GetProperty("frameId").GetString());
    }

    /// <summary>
    /// Legacy 充电桩报文同样要接受外部 frameId。
    ///
    /// 共用实现意味着 Legacy 分支也会拿到调用方传下来的 frameId；若这里仍自生成，
    /// 走该分支的命令登记的 key 与实际报文永远对不上，症状是「orderStart 一律超时」。
    /// </summary>
    [Fact]
    public void BuildLegacyCommand_ExternalFrameId_IsActuallyUsed()
    {
        var builder = new AnShengCommandBuilder(null);
        var param = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["sn"] = "SN-0001",
            ["order"] = 1
        };

        var (frameId, payload) = builder.BuildLegacyCommand(Imei, "orderStart", param, PresetFrameId);

        Assert.Equal(PresetFrameId, frameId);

        using var document = JsonDocument.Parse(payload);
        Assert.Equal(PresetFrameId, document.RootElement.GetProperty("frameId").GetString());

        // Legacy 结构不变：参数仍在 param 包裹内
        Assert.True(document.RootElement.TryGetProperty("param", out var wrapped));
        Assert.Equal("SN-0001", wrapped.GetProperty("sn").GetString());
    }

    /// <summary>不传 frameId 时行为与 T6 完全一致（自生成 16 位十六进制）。</summary>
    [Fact]
    public void BuildLegacyCommand_WithoutFrameId_StillAutoGenerates()
    {
        var builder = new AnShengCommandBuilder(null);

        var (frameId, _) = builder.BuildLegacyCommand(Imei, "orderEnd");

        Assert.Equal(AnShengCommandBuilder.FrameIdLength, frameId.Length);
        Assert.Matches("^[0-9a-f]{16}$", frameId);
    }
}
