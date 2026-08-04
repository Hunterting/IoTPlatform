using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using IoTPlatform.DTOs.Requests;
using IoTPlatform.Infrastructure.Protocol.AnSheng;
using IoTPlatform.IntegrationTests.Infrastructure;
using IoTPlatform.IntegrationTests.Infrastructure.Auth;
using IoTPlatform.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IoTPlatform.IntegrationTests.AnSheng;

/// <summary>
/// 设计 §8.3「拒绝态 HTTP 信封」的集成验收 —— 补齐验收 <b>#1 / #2 / #3</b> 在 HTTP 层的缺口。
///
/// ═══════════════════════════════════════════════════════════════════════
/// 【与既有测试的分工：为什么这三条必须在集成层再验一遍】
/// ═══════════════════════════════════════════════════════════════════════
/// 「判定是否正确」已被单测锁死（<c>AnShengCommandGuard</c> 是同步纯函数，
/// 三条拒绝分支各有毫秒级单测）。<c>AnShengCommandAcceptanceTests</c> 则覆盖了
/// #5（超时闭环）与 #6（catalog 36 条）。<b>唯独「拒绝结论怎么出 HTTP 门」没人守</b>。
///
/// 这段路上有一个真实发生过的缺陷类型：控制器把服务层已经填好的 <c>result</c>
/// （含 <c>RejectReason</c> / <c>RequiredFirmware</c> / <c>Errors</c> / <c>CommandId</c>）
/// 丢掉，只回一句 <c>BadRequest(message)</c>。此时：
///   · Guard 单测<b>照样全绿</b>——它压根不经过控制器；
///   · 服务层单测<b>照样全绿</b>——<c>AnShengCommandResponse</c> 里字段都在；
///   · 前端却拿到 <c>data:null</c>，无法「品类不支持就灰掉按钮 / 固件不足就引导升级」，
///     只能去正则匹配中文 <c>message</c>——文案一改就崩。
/// 换言之，这是一条<b>只有端到端断言才拦得住</b>的静默降级，故必须落在集成层。
///
/// ═══════════════════════════════════════════════════════════════════════
/// 【全站信封约定（本文件的断言基准）】
/// ═══════════════════════════════════════════════════════════════════════
/// <code>
///   HTTP 200  +  body.code = 400  +  body.data = 机器可读上下文
/// </code>
/// 所以每条用例都必须<b>同时</b>断言三件事，缺一不可：
///   ① <c>StatusCode == 200</c>  —— 退化成裸 400 会让前端的统一拦截器把它当网络错误吞掉；
///   ② <c>code == 400</c>        —— 退化成 200 会让调用方以为命令发出去了；
///   ③ <c>data != null</c> 且 <c>data.rejectReason</c> 正确 —— 这一条才是本次修复的靶心。
/// 只断言 ①② 而不断言 ③，正是原缺陷能长期潜伏的原因。
///
/// ═══════════════════════════════════════════════════════════════════════
/// 【为什么还要断言「零出网 + 落库 Rejected」】
/// ═══════════════════════════════════════════════════════════════════════
/// 「返回了拒绝信封」与「真的没发出去」是两件独立的事。若 Guard 判拒但报文仍被发布，
/// 用户会看到「平台说不支持，设备却动了」——这是最坏的一类不一致。
/// <c>Adapter.Sent.Count == 0</c> 提供出网侧证据，
/// <c>AnShengCommandRecord.Status == Rejected</c> 提供留痕侧证据（且 FrameId/SentAt 恒为 null）。
///
/// ═══════════════════════════════════════════════════════════════════════
/// 【枚举上线形态：为什么用语义解析而不是硬编字符串】
/// ═══════════════════════════════════════════════════════════════════════
/// 本仓库<b>未</b>注册 <c>JsonStringEnumConverter</c>（全局搜索零命中），
/// 因此 <c>AnShengCommandRejectReason</c> 默认按 <b>int</b> 上线。
/// 若把断言写死成 <c>rejectReason == "RejectedByKind"</c>，测试会因为「序列化形态」
/// 而不是「业务结论」而红——那是在测 System.Text.Json，不是在测本项目的契约。
/// 故用 <see cref="ReadRejectReason"/> 同时接受「枚举名字符串」与「枚举底数值」，
/// 归一成 <see cref="AnShengCommandRejectReason"/> 后再断言<b>语义</b>。
/// 这样将来若有人补上 StringEnumConverter，本文件无需改动即继续有效。
/// </summary>
[Collection(SharedTestConstants.CollectionName)]
public sealed class AnShengCommandRejectionEnvelopeTests : IntegrationTestBase
{
    /// <summary>设计 §8.3 规定的拒绝态业务码。</summary>
    private const int RejectedCode = 400;

    public AnShengCommandRejectionEnvelopeTests(DatabaseFixture fixture) : base(fixture)
    {
    }

    /// <summary>
    /// 用例结束后<b>再清一次</b>进程级静态状态。
    ///
    /// 【基类已经在 InitializeAsync 里清过了，为什么还要清】
    /// 基类那次是「进门前打扫自己的现场」，只能保证本用例不被上一个用例污染；
    /// 它保证不了<b>本用例不去污染下一个</b>——尤其是
    /// <c>AnShengCommandAcceptanceTests</c> 的超时用例：它断言
    /// <c>store.Count == 1</c>（下发后在途表恰好一条）。本文件的用例虽然理论上零登记
    /// （被 Guard 拦下就不该进在途表），但「理论上零登记」正是被测对象本身——
    /// 一旦哪天 Guard 之后误加了登记，脏条目会让超时用例莫名其妙红在别的文件里，
    /// 排查成本极高。出门再打扫一次，把故障定位钉死在本文件内。
    /// </summary>
    public override Task DisposeAsync()
    {
        StaticStateResetter.ResetAll(Fixture.Factory.Services);
        return base.DisposeAsync();
    }

    // ══════════════════════════════════════════════════════════════════
    // 验收 #1：品类不支持 → RejectedByKind
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 验收 #1：给 <b>WiFi 喇叭</b>（<c>SpeakerWiFi</c>）下发<b>开关动作</b> <c>action</c>。
    ///
    /// 【判据来源】<c>action</c> 属能力组 <c>GroupSwitchAction = Switch4G | SwitchWiFi</c>，
    /// 喇叭类不在其中，故 Guard 第 ② 环节 <c>CheckKind</c> 短路拒绝。
    ///
    /// 【为什么故意不带 parameters】<c>action</c> 的 <c>slotNum</c> / <c>action</c> 都是必填，
    /// 不传参本会触发 <c>RejectedByValidation</c>。但 Guard 的环节顺序是
    /// <c>② 品类 → ③ 参数</c>，品类先短路。<b>这恰恰是本用例的附加价值</b>：
    /// 若哪天有人把参数校验提到品类之前，喇叭用户会收到「slotNum 必填」这种
    /// 驴唇不对马嘴的提示（补了参数还是发不出去），本用例会立刻变红。
    /// </summary>
    [Fact(DisplayName = "验收#1 品类不支持 → 200 + code=400 + data.rejectReason=RejectedByKind + 零出网")]
    public async Task Reject_ByKind_ReturnsEnvelopeWithReason()
    {
        // Arrange —— SpeakerWiFi 落档。版本给足（V4.0.20），确保红的原因只可能是品类，
        //            不会被固件门槛抢先（诊断唯一性）。
        await SeedProfileAsync(
            Seed.DeviceId, Seed.Imei, AnShengDeviceKind.SpeakerWiFi, slotAmount: 0, version: "V4.0.20");

        var client = Client.AsAdmin();
        Adapter.Sent.Should().BeEmpty("基类已在 InitializeAsync 重置录制适配器");

        // Act
        var response = await client.PostAsJsonAsync(
            $"/api/v1/ansheng/{Seed.DeviceId}/command",
            new AnShengCommandRequest { Method = "action" });

        // Assert —— 信封三件套
        var (data, raw) = await AssertRejectionEnvelopeAsync(response);

        ReadRejectReason(data, raw).Should().Be(
            AnShengCommandRejectReason.RejectedByKind,
            "WiFi 喇叭不具备开关能力，必须以品类维度拒绝；" +
            $"若得到 RejectedByValidation 说明 Guard 的环节顺序被改动（参数校验抢在品类之前）。实际响应：{Truncate(raw)}");

        // Assert —— 零出网（与「返回了拒绝信封」是两件独立的事）
        Adapter.Sent.Should().BeEmpty(
            "被 Guard 拦下的命令必须零 MQTT 发布；" +
            "一旦出网，用户会看到「平台说不支持、设备却动了」这类最坏的不一致");

        // Assert —— 落库留痕
        await AssertRejectedRecordAsync(data, raw, "action");
    }

    // ══════════════════════════════════════════════════════════════════
    // 验收 #2：slotNum 越界 → RejectedByValidation
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 验收 #2：4 路 4G 开关收到 <c>slotNum = 9</c>。
    ///
    /// 【为什么这条只能靠档案拦】协议目录里 <c>slotNum</c> 只声明了 <c>minimum: 0</c>，
    /// <b>上界是几取决于这台设备到底有几路</b>，那是 <c>AnShengDeviceProfile.SlotAmount</c>
    /// 才知道的事（Guard 第 ④ 环节 <c>CheckSlotRange</c>）。
    /// 所以本用例的 <c>SlotAmount = 4</c> 不是随便填的：它就是被测判据本身。
    ///
    /// 【为什么 action 参数给合法值 "on"】把变量控制到只剩 slotNum 一个。
    /// 若 action 也写错，拒绝原因虽仍是 <c>RejectedByValidation</c>，
    /// 但究竟是第 ③ 环节（参数规格）还是第 ④ 环节（插槽越界）拦下的就无从分辨，
    /// 用例的诊断价值会被稀释成「反正错了」。
    /// </summary>
    [Fact(DisplayName = "验收#2 slotNum 越界 → 200 + code=400 + data.rejectReason=RejectedByValidation + 零出网")]
    public async Task Reject_ByValidation_ReturnsEnvelopeWithReason()
    {
        // Arrange —— 4 路开关；版本给足，排除固件门槛干扰
        await SeedProfileAsync(
            Seed.DeviceId, Seed.Imei, AnShengDeviceKind.Switch4G, slotAmount: 4, version: "V4.0.20");

        var client = Client.AsAdmin();

        // Act —— 9 号插槽在 4 路设备上不存在（合法区间 [0,4]，0 表示全部）
        var response = await client.PostAsJsonAsync(
            $"/api/v1/ansheng/{Seed.DeviceId}/command",
            new AnShengCommandRequest
            {
                Method = "action",
                Parameters = new Dictionary<string, object?>
                {
                    ["slotNum"] = 9,
                    ["action"] = "on"
                }
            });

        // Assert —— 信封三件套
        var (data, raw) = await AssertRejectionEnvelopeAsync(response);

        ReadRejectReason(data, raw).Should().Be(
            AnShengCommandRejectReason.RejectedByValidation,
            "设备只有 4 路，slotNum=9 越界，必须以参数校验维度拒绝；" +
            $"若得到放行说明 CheckSlotRange 没读到档案 SlotAmount（档案查询按 DeviceId，注意租户过滤）。实际响应：{Truncate(raw)}");

        // Assert —— 越界参数绝不能出网：设备侧对不存在的插槽行为未定义
        Adapter.Sent.Should().BeEmpty("越界命令必须零 MQTT 发布");

        // Assert —— 落库留痕
        await AssertRejectedRecordAsync(data, raw, "action");
    }

    // ══════════════════════════════════════════════════════════════════
    // 验收 #3：固件不足 → RejectedByFirmware + requiredFirmware
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 验收 #3：固件 <c>V4.0.8</c> 的设备使用 <c>getDevStatus</c> 的 <c>q</c> 参数
    /// （目录声明 <c>minFirmware = 4.0.20</c>）。
    ///
    /// 【本条比 #1/#2 多守一个字段】<c>data.requiredFirmware</c>。
    /// 决策 D5 选的是「直接拦截」而非「静默丢弃 q 参数降级下发」，
    /// 那么前端就<b>必须</b>拿到「升到哪个版本才能用」，否则用户只知道「不行」、不知道「怎么办」。
    /// 这个字段是本次修复「把 result 整体带回」相较「只带 message」的最直接收益，
    /// 因此单独断言，而不是笼统地断言 data 非空。
    ///
    /// 【顺带守住版本比较不能退化成字典序】4.0.8 与 4.0.20 按字符串比恰好是
    /// <c>"4.0.8" &gt; "4.0.20"</c>（字符 '8' &gt; '2'），会误判成「固件足够」而放行。
    /// 本用例选这一对版本号正是为了钉死 <c>AnShengFirmwareVersion</c> 的<b>按段数值比较</b>。
    /// </summary>
    [Fact(DisplayName = "验收#3 固件不足 → 200 + code=400 + rejectReason=RejectedByFirmware + requiredFirmware=4.0.20")]
    public async Task Reject_ByFirmware_ReturnsEnvelopeWithRequiredFirmware()
    {
        // Arrange —— 低版本档案。品类选 Switch4G：getDevStatus 属 GroupCommon（四品类全支持），
        //            品类环节必定放行，红的原因只可能是固件。
        await SeedProfileAsync(
            Seed.DeviceId, Seed.Imei, AnShengDeviceKind.Switch4G, slotAmount: 4, version: "V4.0.8");

        var client = Client.AsAdmin();

        // Act —— q 是可选参数，但一旦传了就要求固件 ≥ 4.0.20
        var response = await client.PostAsJsonAsync(
            $"/api/v1/ansheng/{Seed.DeviceId}/command",
            new AnShengCommandRequest
            {
                Method = "getDevStatus",
                Parameters = new Dictionary<string, object?> { ["q"] = "slots" }
            });

        // Assert —— 信封三件套
        var (data, raw) = await AssertRejectionEnvelopeAsync(response);

        ReadRejectReason(data, raw).Should().Be(
            AnShengCommandRejectReason.RejectedByFirmware,
            "q 参数要求固件 ≥ 4.0.20，设备为 4.0.8，必须以固件维度拒绝；" +
            "若放行了，多半是 AnShengFirmwareVersion.Compare 退化成了字典序" +
            $"（\"4.0.8\" > \"4.0.20\" 恰好成立，是个极隐蔽的坑）。实际响应：{Truncate(raw)}");

        // Assert —— requiredFirmware 必须原样带回，这是「引导升级」唯一的机器可读依据
        TryGetPropertyIgnoreCase(data, "requiredFirmware", out var required).Should().BeTrue(
            "拒绝态信封必须带 requiredFirmware，否则前端只能提示「不行」却说不出「升到哪」；" +
            $"缺失通常意味着控制器丢掉了 result、只回了 message。实际响应：{Truncate(raw)}");

        required.ValueKind.Should().Be(
            JsonValueKind.String, $"requiredFirmware 是版本串而非 null，实际响应：{Truncate(raw)}");
        required.GetString().Should().Be(
            "4.0.20", "门槛取自目录里 q 参数的 minFirmware，必须原样透传，不得被格式化或补 V 前缀");

        // Assert —— 零出网。固件不足若被静默降级下发（丢掉 q），老设备会返回残缺状态，
        //            上层却以为拿到了完整数据——决策 D5 明确否决了这条路。
        Adapter.Sent.Should().BeEmpty("固件不足必须零 MQTT 发布，不得静默降级下发");

        // Assert —— 落库留痕
        await AssertRejectedRecordAsync(data, raw, "getDevStatus");
    }

    // ══════════════════════════════════════════════════════════════════
    // Helpers
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 断言「拒绝态 HTTP 信封」的公共骨架（设计 §8.3），返回 <c>data</c> 节点与响应原文。
    ///
    /// 三条用例共用：把 <c>200 / code=400 / data 非空</c> 收敛到一处，
    /// 让每个用例体里只剩下它<b>各自独有</b>的那条断言（rejectReason / requiredFirmware），
    /// 失败时一眼能看出是「信封坏了」还是「判定错了」。
    /// </summary>
    /// <param name="response">HTTP 响应。</param>
    /// <returns><c>data</c> 节点与响应原文。</returns>
    private static async Task<(JsonElement Data, string Raw)> AssertRejectionEnvelopeAsync(
        HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();

        // ① HTTP 必须是 200：全站约定「业务失败也走 200，靠包体 code 表达」。
        //    退化成裸 400 会被前端统一拦截器当网络错误吞掉，用户只看到「请求失败」。
        response.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "全站约定 HTTP 200 + 业务 Code≠200；" +
            $"返回裸 400/500 说明控制器改用了 MVC 的 BadRequest()/Problem() 而非 ApiResponse。实际响应：{Truncate(raw)}");

        using var doc = JsonDocument.Parse(raw);
        // JsonDocument 一旦释放，其 JsonElement 全部失效。这里 Clone 出独立副本再返回，
        // 否则调用方拿到的 data 会在 using 结束后抛 ObjectDisposedException。
        var root = doc.RootElement.Clone();

        // ② 业务码必须是 400：200 会让调用方误以为命令已发出。
        TryGetPropertyIgnoreCase(root, "code", out var code).Should().BeTrue(
            $"全站 ApiResponse 约定必须有 code，实际响应：{Truncate(raw)}");
        code.GetInt32().Should().Be(
            RejectedCode,
            $"命令被 Guard 拒绝时业务码必须是 400，实际响应：{Truncate(raw)}");

        // message 只做「有话可说」的底线检查——文案本身不做断言（会改，断言即脆弱）。
        TryGetPropertyIgnoreCase(root, "message", out var message).Should().BeTrue(
            "拒绝必须给出面向人的说明");
        message.GetString().Should().NotBeNullOrWhiteSpace(
            "message 为空等于让用户面对一个没有解释的失败");

        // ③ data 必须携带机器可读上下文 —— 本次修复的靶心。
        TryGetPropertyIgnoreCase(root, "data", out var data).Should().BeTrue(
            $"拒绝态信封必须有 data 节点，实际响应：{Truncate(raw)}");
        data.ValueKind.Should().Be(
            JsonValueKind.Object,
            "data 为 null 意味着控制器丢掉了服务层已填好的 result，" +
            "前端只能去正则匹配中文 message 来判断拒绝类别——文案一改就崩。" +
            $"这正是本次修复要消灭的退化形态。实际响应：{Truncate(raw)}");

        return (data, raw);
    }

    /// <summary>
    /// 断言这条命令在 <c>AnShengCommandRecords</c> 里留下了 <c>Rejected</c> 终态记录。
    ///
    /// 【为什么不止查 Status】被拒绝意味着<b>未出网</b>，那么
    /// <c>FrameId</c> 与 <c>SentAt</c> 就必须恒为 null——这两列是「零发布」的<b>持久化证据</b>，
    /// 比内存里的 <c>Adapter.Sent</c> 更耐得住进程重启后的事后审计。
    /// 二者一起断言，才能排除「适配器没发、但记录被错误地标成已发送」这种半截状态。
    /// </summary>
    /// <param name="data">响应 data 节点。</param>
    /// <param name="raw">响应原文（仅用于失败文案）。</param>
    /// <param name="expectedMethod">期望的协议方法名。</param>
    private async Task AssertRejectedRecordAsync(JsonElement data, string raw, string expectedMethod)
    {
        TryGetPropertyIgnoreCase(data, "commandId", out var commandIdElement).Should().BeTrue(
            "CommandId 从命令被受理那一刻就存在（拒绝态没有 FrameId，它是唯一的追溯键）；" +
            $"缺失说明控制器没把 result 带回来。实际响应：{Truncate(raw)}");

        var commandId = commandIdElement.GetString();
        commandId.Should().NotBeNullOrWhiteSpace($"CommandId 不得为空串，实际响应：{Truncate(raw)}");

        var record = await QueryDbAsync(db => db.AnShengCommandRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.CommandId == commandId));

        record.Should().NotBeNull(
            "拒绝也必须留痕，否则「这条命令为什么没发出去」事后无从追溯");

        record!.Status.Should().Be(
            AnShengCommandStatus.Rejected,
            "被 Guard 拦下的终态是 Rejected，不是 Failed（Failed 表示已尝试下发但出错）");
        record.Method.Should().Be(expectedMethod, "留痕的 method 必须与请求一致");
        record.RejectReason.Should().NotBeNull("Rejected 记录必须落拒绝原因列，供后续按类别统计");
        record.FrameId.Should().BeNull("未出网就没有帧号——这是「零发布」的持久化证据");
        record.SentAt.Should().BeNull("未出网就没有发送时刻");
        record.CompletedAt.Should().NotBeNull("Rejected 是终态，必须带完成时刻");
        record.AppCode.Should().Be(
            SharedTestConstants.AppCode,
            "租户码缺失会让这条记录在任何租户视图里都查不到，等于没留痕");
    }

    /// <summary>
    /// 从 <c>data</c> 里读出拒绝原因，<b>同时兼容枚举的两种上线形态</b>：
    /// 字符串枚举名（注册了 <c>JsonStringEnumConverter</c> 时）与整数底数值（当前仓库的默认行为）。
    ///
    /// 【为什么要兼容而不是写死】本文件要守的契约是「拒绝<b>类别</b>是否正确且可被机器读取」。
    /// 把断言写死成某一种序列化形态，等于把 System.Text.Json 的配置也变成被测对象：
    /// 将来谁加一行 <c>JsonStringEnumConverter</c>，三条用例会集体变红，
    /// 但业务行为其实<b>一点没坏</b>——这种假阳性会消耗信任，最终导致测试被随手改绿。
    /// 归一成枚举后再比，才是在断言语义。
    /// </summary>
    /// <param name="data">响应 data 节点。</param>
    /// <param name="raw">响应原文（仅用于失败文案）。</param>
    /// <returns>解析出的拒绝原因。</returns>
    private static AnShengCommandRejectReason ReadRejectReason(JsonElement data, string raw)
    {
        TryGetPropertyIgnoreCase(data, "rejectReason", out var element).Should().BeTrue(
            "拒绝态信封必须带 rejectReason —— 文案会改，枚举才是稳定契约；" +
            $"缺失说明控制器丢掉了服务层的 result。实际响应：{Truncate(raw)}");

        element.ValueKind.Should().NotBe(
            JsonValueKind.Null,
            $"rejectReason 为 null 意味着 Guard 判定结论没被透传出来，实际响应：{Truncate(raw)}");

        switch (element.ValueKind)
        {
            case JsonValueKind.String:
            {
                var text = element.GetString();
                Enum.TryParse<AnShengCommandRejectReason>(text, ignoreCase: true, out var parsed)
                    .Should().BeTrue(
                        $"rejectReason=\"{text}\" 不是 AnShengCommandRejectReason 的合法成员，实际响应：{Truncate(raw)}");
                return parsed;
            }

            case JsonValueKind.Number:
            {
                var value = element.GetInt32();
                Enum.IsDefined(typeof(AnShengCommandRejectReason), value).Should().BeTrue(
                    $"rejectReason={value} 不在枚举定义域内，实际响应：{Truncate(raw)}");
                return (AnShengCommandRejectReason)value;
            }

            default:
                throw new Xunit.Sdk.XunitException(
                    $"rejectReason 只应为枚举名字符串或整数，实际 ValueKind={element.ValueKind}，响应：{Truncate(raw)}");
        }
    }

    /// <summary>
    /// 插入一条设备能力档案（决策 D7：品类必须显式落档，否则 Guard 走「未知即放行」的降级分支）。
    ///
    /// 【AppCode 为什么必须显式赋值】播种走 DI 作用域直连 <c>AppDbContext</c>，
    /// 此时 <c>TenantContext</c> 为空，全局过滤器不会代填；漏了它，
    /// 服务层按租户查档案时会查不到，三条用例会齐刷刷退化成「品类未知 → 放行」。
    /// </summary>
    /// <param name="deviceId">设备主键。</param>
    /// <param name="imei">设备 IMEI。</param>
    /// <param name="kind">设备品类。</param>
    /// <param name="slotAmount">插槽数量。</param>
    /// <param name="version">固件版本串。</param>
    private Task SeedProfileAsync(
        long deviceId, string imei, AnShengDeviceKind kind, int slotAmount, string version)
        => ExecuteDbAsync(async db =>
        {
            db.AnShengDeviceProfiles.Add(new AnShengDeviceProfile
            {
                AppCode = SharedTestConstants.AppCode,
                Imei = imei,
                DeviceId = deviceId,
                Kind = kind,
                KindSource = AnShengKindSource.Manual,
                SlotAmount = slotAmount,
                Version = version,
                ProbeStatus = AnShengProbeStatus.Probed
            });
            await db.SaveChangesAsync();
        });

    /// <summary>
    /// 大小写不敏感地取属性。
    ///
    /// ASP.NET Core 默认 camelCase，但序列化策略属于「随时可能被全局调整」的基础设施配置；
    /// 用它取字段，能让本文件在 <c>rejectReason</c> / <c>RejectReason</c> 两种命名下都成立，
    /// 把断言的注意力留给业务语义。
    /// </summary>
    /// <param name="element">JSON 对象节点。</param>
    /// <param name="name">属性名。</param>
    /// <param name="value">取到的值。</param>
    /// <returns>是否存在该属性。</returns>
    private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = prop.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    /// <summary>失败文案里截断超长响应，避免测试输出被整包 JSON 淹没。</summary>
    /// <param name="s">原始文本。</param>
    /// <returns>截断后的文本。</returns>
    private static string Truncate(string s) => s.Length <= 800 ? s : s[..800] + "…";
}
