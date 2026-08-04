using System.Net;
using System.Net.Http;
using System.Text.Json;
using FluentAssertions;
using IoTPlatform.DTOs.Responses;
using IoTPlatform.Helpers;
using IoTPlatform.Infrastructure.Protocol.Adapters;
using IoTPlatform.Infrastructure.Protocol.AnSheng;
using IoTPlatform.IntegrationTests.Infrastructure;
using IoTPlatform.IntegrationTests.Infrastructure.Auth;
using IoTPlatform.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IoTPlatform.IntegrationTests.Samples;

/// <summary>
/// QA 冒烟证伪套件（第 2 步验收，由 QA 补充，非工程师原始交付）。
///
/// 【与 Samples 里 10 个示例的分工】
///   示例 01–10 证明「脚手架能跑通业务链路」，属于<b>能力演示</b>；
///   本文件专门证明「脚手架的隔离机制本身是真的」，属于<b>能力证伪</b>——
///   每条用例都刻意先制造污染，再断言脚手架把污染清干净了。
///   若隔离机制被改坏（哪怕退化成静默 no-op），这里必须变红。
///
/// 【为什么示例-04 不够】
///   示例-04 只调 <c>StaticStateResetter.Verify()</c> 看 <c>LastError == null</c>。
///   它验证的是「清理动作没报错」，而不是「状态真的被清空了」：
///   字典本来就是空的时候，一个彻底失效的清理器同样能让它绿。
///   本文件改为「先污染 → 再清理 → 用<b>生产自己的读 API</b> 观测」，才具备证伪能力。
/// </summary>
[Collection(SharedTestConstants.CollectionName)]
public sealed class ScaffoldFalsificationTests : IntegrationTestBase
{
    private const string DiscoveredUrl = "/api/v1/ansheng/discovered";

    public ScaffoldFalsificationTests(DatabaseFixture fixture) : base(fixture)
    {
    }

    [Fact(DisplayName = "QA-01 两处进程级状态被真实污染后，ResetAll() 经生产读 API 观测确实归零")]
    public void StaticStateResetter_ActuallyEmptiesPollutedState()
    {
        // 【T7-3 改造说明】
        //   本用例原先观测的第二处状态是 AnShengCommandService.FrameIdCommandIdMap（静态字典 + 反射清理）。
        //   T7-3 已将该字典<b>物理删除</b>（实读证实只写不读，生产零 ResolveCommandId 调用点），
        //   frameId 的在途判定统一收敛到 IAnShengPendingCommandStore。
        //   为不丢失证伪能力，这里把观测对象平移到「在途命令表」——它同样是进程级 Singleton、
        //   同样会跨用例泄漏，且同样有生产自己的读 API（IsInFlight / Count）可供观测。
        //   若有人把 StaticStateResetter.ClearPendingCommands 那步删掉，本用例立刻变红。

        // 用与基线 IMEI 不同的值，避免与其它用例的语义纠缠
        const string pollutedImei = "864536072949999";
        const string pollutedFrameId = "QA00000000000001";

        var store = Fixture.Factory.Services.GetRequiredService<IAnShengPendingCommandStore>();

        // ── Arrange：把两处进程级状态真正弄脏，并确认「脏」是可观测的 ──
        // 前置断言不可省：污染若没生效，后面的「已清空」断言就成了永真式，用例失去证伪能力。
        AnShengMqttProtocolAdapter.RegisterDeviceKind(pollutedImei, AnShengDeviceKind.Switch4G);

        // TTL 取 5 分钟：远大于单个用例耗时，排除「其实是惰性过期把它摘掉的」这一混淆因素。
        // 若 TTL 太短，即便清理器完全失效用例也会绿，那才是真正危险的假阳性。
        store.TryRegister(
                pollutedImei,
                pollutedFrameId,
                PendingCommand.Create(
                    commandId: 0,
                    imei: pollutedImei,
                    frameId: pollutedFrameId,
                    method: "getDevStatus",
                    ttl: TimeSpan.FromMinutes(5)))
            .Should().BeTrue("前置条件：在途命令表必须登记成功才谈得上被污染");

        AnShengMqttProtocolAdapter.GetDeviceKind(pollutedImei)
            .Should().Be(AnShengDeviceKind.Switch4G, "前置条件：DeviceKinds 必须先被污染");
        store.IsInFlight(pollutedImei, pollutedFrameId)
            .Should().BeTrue("前置条件：在途命令表必须先被污染");

        // ── Act ──
        // 必须传 Provider：三处 Singleton 状态都只能经容器取到实例才清得掉。
        StaticStateResetter.ResetAll(Fixture.Factory.Services);

        // ── Assert：不看 LastError，看生产自己的读 API ──
        StaticStateResetter.LastError.Should().BeNull(
            $"清理过程不应报错。诊断：{StaticStateResetter.LastError}");

        AnShengMqttProtocolAdapter.GetDeviceKind(pollutedImei)
            .Should().Be(AnShengDeviceKind.Unknown,
                "DeviceKinds 必须被真正清空（走生产公开的 ClearDeviceKinds）");

        // 这一条是「在途表清理」的真正体检：ClearAll 一旦退化成 no-op（或忘了取消等待者），
        // 残留条目会把下一个用例的自动上报误判成 Response，症状极难归因。
        store.IsInFlight(pollutedImei, pollutedFrameId)
            .Should().BeFalse("在途命令表必须被真正清空（IAnShengPendingCommandStore.ClearAll）");
        store.Count.Should().Be(0, "ClearAll 之后不应残留任何条目，包括尚未被惰性摘除的过期条目");
    }

    [Fact(DisplayName = "QA-02 用例级钩子会清掉 GetOrCreateFor 登记的额外分身，而不只是默认分身")]
    public async Task PerTestHook_AlsoResetsExtraAdapterClones()
    {
        // 这条专门证伪工程师自报的「全局缺陷修复 #1」：
        //   IntegrationTestBase 原先只调 Adapter.Reset()（= 只清默认分身），
        //   通过 AdapterFactory.GetOrCreateFor(configId) 登记的额外分身不会被清，
        //   录制内容会跨用例泄漏。修复后改为 Fixture.AdapterFactory.Reset()。
        // 若有人把这行改回去，本用例立刻变红。
        const int extraConfigId = 4242;

        // ── Arrange：把「额外分身 + 默认分身 + 静态字典」三处状态一起弄脏 ──
        var extra = Fixture.AdapterFactory.GetOrCreateFor(extraConfigId);
        await extra.SendCommandAsync(Seed.DeviceId, Seed.Imei, "getDevStatus", "{}");
        extra.Sent.Should().ContainSingle("前置条件：额外分身必须先录到内容");

        await Fixture.Adapter.SendCommandAsync(Seed.DeviceId, Seed.Imei, "getDevStatus", "{}");
        Fixture.Adapter.Sent.Should().ContainSingle("前置条件：默认分身必须先录到内容");

        AnShengMqttProtocolAdapter.RegisterDeviceKind(Seed.Imei, AnShengDeviceKind.Speaker4G);

        // ── Act：重跑用例级初始化钩子 = 精确复现「下一个用例开始」时发生的事 ──
        // 直接调 InitializeAsync() 而不是依赖 xUnit 的用例执行顺序，
        // 这样断言是确定性的，不会因为用例排序变化而时绿时红。
        await InitializeAsync();

        // ── Assert：三处必须全部归零 ──
        Fixture.AdapterFactory.GetOrCreateFor(extraConfigId).Sent.Should().BeEmpty(
            "额外分身必须被清。若 IntegrationTestBase 只调 Adapter.Reset()，此处会残留上一用例的录制");

        Fixture.Adapter.Sent.Should().BeEmpty("默认分身必须被清");

        AnShengMqttProtocolAdapter.GetDeviceKind(Seed.Imei).Should().Be(
            AnShengDeviceKind.Unknown, "用例级钩子必须顺带清掉静态品类缓存");
    }

    [Fact(DisplayName = "QA-03 请求级 AppCode 过滤生效：换租户看不到基线数据（真隔离仍须另起 Factory）")]
    public async Task DiscoveredDevices_AreFilteredByAppCodeClaim()
    {
        // 【本用例在验证什么，以及刻意不验证什么】
        //   验证：AnShengController 的请求级过滤
        //         `d.AppCode == null || d.AppCode == appCode`（Controllers/AnShengController.cs:84）
        //         确实按 AppCode claim 生效——这是 T5–T14 多租户用例最常依赖的一条。
        //   不验证：EF 全局查询过滤器的租户隔离。那条过滤器在 TestServer 首次建模时就被
        //         「冻结」成一个 AppCode（见 SharedTestConstants.AppCode 的注释），
        //         同一个 Factory 内换 claim 撼动不了它。
        //   ⇒ 结论：需要验证「EF 层租户隔离」的用例必须另起一个 TestWebAppFactory，
        //           不能靠在本 Fixture 里换 X-Test-AppCode 头来实现。本注释即该约束的落点。

        // 本租户：能看到基线播种的那台设备
        var mine = await Client
            .AsRole(SharedTestConstants.RoleAdmin, SharedTestConstants.AppCode)
            .GetAsync(DiscoveredUrl);

        var mineBody = await ReadAsync<ApiResponse<DiscoveredDeviceListResponse>>(mine);
        mineBody.Should().NotBeNull();
        mineBody!.Code.Should().Be(200);
        mineBody.Data!.Total.Should().Be(1, "AppCode=TEST 应当看到基线播种的 1 台设备");

        // 换租户：同一角色、同一权限，仅 AppCode 不同 ⇒ 必须看不到
        var other = await Client
            .AsRole(SharedTestConstants.RoleAdmin, "OTHERTENANT")
            .GetAsync(DiscoveredUrl);

        var otherBody = await ReadAsync<ApiResponse<DiscoveredDeviceListResponse>>(other);
        otherBody.Should().NotBeNull();
        otherBody!.Code.Should().Be(200, "换租户不是权限问题，仍应放行，只是查不到数据");
        otherBody.Data!.Total.Should().Be(0,
            "基线数据的 AppCode=TEST，OTHERTENANT 不该看见——否则请求级租户过滤已失效");
    }

    [Fact(DisplayName = "QA-04 匿名响应确为空包体：对它调用 ReadAsync<T> 必然抛异常（锁定「裸 401」口径）")]
    public async Task AnonymousResponse_HasNoApiResponseBody_SoReadAsyncMustThrow()
    {
        // 这条把工程师「推翻的错误假设 #1」以可执行断言的形式钉死：
        //   匿名请求 ⇒ AuthorizationMiddleware 在 MVC 过滤器之前 challenge ⇒ 裸 HTTP 401 + 空包体，
        //   而不是本平台常见的「HTTP 200 + 包体 Code=401」。
        // T5–T14 若有人照旧写 `body!.Code.Should().Be(401)`，会得到一个反序列化异常而非清晰失败。
        // 保留这条用例，是为了让口径本身有回归保护：将来若管道改成返回 ApiResponse 包体，
        // 本用例会红，从而强制同步 README 与所有权限用例。
        var response = await Client.AsAnonymous().GetAsync(DiscoveredUrl);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().BeEmpty("Challenge 不产出任何包体");

        var act = async () => await ReadAsync<ApiResponse<DiscoveredDeviceListResponse>>(response);
        await act.Should().ThrowAsync<JsonException>(
            "空包体无法反序列化成 ApiResponse——所以匿名用例必须断言 StatusCode，而不是包体 Code");
    }
}
