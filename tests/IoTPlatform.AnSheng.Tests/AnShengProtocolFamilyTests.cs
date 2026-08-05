using System.Text.Json;
using IoTPlatform.Infrastructure.Protocol.AnSheng;
using IoTPlatform.Infrastructure.Protocol.AnSheng.Legacy;
using Xunit;

namespace IoTPlatform.AnSheng.Tests;

/// <summary>
/// T14 协议族隔离验收：充电桩 Legacy 命令归位后的<b>显式判定</b>与<b>行为不变</b>。
///
/// 【这个测试类守护什么】
/// 改造前，下发侧判据是「method 不在 <see cref="AnShengCommandCatalog"/> ⇒ 按 Legacy 构造并外发」。
/// 这是一条<b>兜底放行</b>策略：拼错一个字母（<c>orderStrat</c>）、或传入协议里根本不存在的方法，
/// 都会被真实构造成充电桩报文打到现网设备上，而调用方还会收到「成功」。
/// T14 把它换成三态显式判定 —— 认识二开 / 认识充电桩 / <b>不认识就失败</b>。
///
/// 本类的用例分四组：
///   · 组 1：两份目录的协议族标注必须完备且互斥（<c>ProtocolFamily</c> 是显式字段，不是推断）；
///   · 组 2：<see cref="AnShengProtocolFamilyResolver"/> 三态判定，重点是<b>第三态必须为假</b>；
///   · 组 3：<b>验收标准 ②</b> —— Legacy 报文结构与改造前逐字节一致；
///   · 组 4：<b>验收标准 ③</b> —— <c>close</c> 遗嘱在两种报文形态下都能正确处理。
/// </summary>
public class AnShengProtocolFamilyTests
{
    private const string TestImei = "864536072949900";

    // ─────────────────────────────────────────────────────────────
    // 组 1：目录的协议族标注
    // ─────────────────────────────────────────────────────────────

    /// <summary>二开目录里的每一条都必须显式标注为 OpenProtocol。</summary>
    [Fact]
    public void OpenCatalog_AllCommands_TaggedAsOpenProtocol()
    {
        Assert.NotEmpty(AnShengCommandCatalog.Commands);

        foreach (var (method, spec) in AnShengCommandCatalog.Commands)
        {
            Assert.Equal(AnShengProtocolFamily.OpenProtocol, spec.ProtocolFamily);
            Assert.Equal(method, spec.Method);
        }
    }

    /// <summary>Legacy 目录恰好 3 条，且每一条都必须显式标注为 ChargingPile。</summary>
    [Fact]
    public void LegacyCatalog_ContainsExactlyThreeChargingPileCommands()
    {
        Assert.Equal(3, AnShengLegacyCommandCatalog.Count);

        var expected = new[] { "orderStart", "orderEnd", "orderUp" };
        Assert.Equal(
            expected.OrderBy(m => m, StringComparer.Ordinal),
            AnShengLegacyCommandCatalog.Methods.OrderBy(m => m, StringComparer.Ordinal));

        foreach (var (method, spec) in AnShengLegacyCommandCatalog.Commands)
        {
            Assert.Equal(AnShengProtocolFamily.ChargingPile, spec.ProtocolFamily);
            Assert.Equal(method, spec.Method);
            Assert.Equal(AnShengCommandDirection.Downlink, spec.Direction);
        }
    }

    /// <summary>
    /// 两份目录<b>不得</b>登记同名 method。
    /// 同名两处 = 同一 method 两种报文结构，判定顺序一变行为就变，是最难查的那类 bug。
    /// </summary>
    [Fact]
    public void Catalogs_DoNotOverlap()
    {
        var overlap = AnShengLegacyCommandCatalog.Methods
            .Where(m => AnShengCommandCatalog.Commands.ContainsKey(m))
            .ToList();

        Assert.Empty(overlap);
    }

    /// <summary>
    /// 已被物理删除的「伪命令」不得出现在任何一份目录里。
    /// 它们不属于安圣官方协议（asopen.md），也不属于 Legacy 充电桩协议。
    /// </summary>
    [Theory]
    [InlineData("setSwitch")]
    [InlineData("getSwitchStatus")]
    [InlineData("setSwitchConfig")]
    [InlineData("getSwitchConfig")]
    public void FabricatedMethods_AreInNoCatalog(string method)
    {
        Assert.False(AnShengCommandCatalog.Commands.ContainsKey(method));
        Assert.False(AnShengLegacyCommandCatalog.Contains(method));
        Assert.False(AnShengProtocolFamilyResolver.IsKnown(method));
    }

    // ─────────────────────────────────────────────────────────────
    // 组 2：Resolver 三态判定
    // ─────────────────────────────────────────────────────────────

    /// <summary>充电桩三条命令必须解析为 ChargingPile，且带回显式规格。</summary>
    [Theory]
    [InlineData("orderStart")]
    [InlineData("orderEnd")]
    [InlineData("orderUp")]
    public void Resolve_ChargingPileMethods_ReturnsChargingPile(string method)
    {
        Assert.True(AnShengProtocolFamilyResolver.TryResolve(method, out var family, out var spec));
        Assert.Equal(AnShengProtocolFamily.ChargingPile, family);
        Assert.NotNull(spec);
        Assert.Equal(AnShengProtocolFamily.ChargingPile, spec!.ProtocolFamily);

        Assert.True(AnShengProtocolFamilyResolver.IsChargingPile(method));
        Assert.False(AnShengProtocolFamilyResolver.IsOpenProtocol(method));
        Assert.Equal(AnShengProtocolFamily.ChargingPile, AnShengProtocolFamilyResolver.Resolve(method));
    }

    /// <summary>二开命令必须解析为 OpenProtocol，绝不能被误判成充电桩。</summary>
    [Theory]
    [InlineData("reboot")]
    [InlineData("getDevStatus")]
    [InlineData("getDevInfo")]
    [InlineData("setAutoReport")]
    public void Resolve_OpenProtocolMethods_ReturnsOpenProtocol(string method)
    {
        Assert.True(AnShengProtocolFamilyResolver.TryResolve(method, out var family, out var spec));
        Assert.Equal(AnShengProtocolFamily.OpenProtocol, family);
        Assert.NotNull(spec);

        Assert.True(AnShengProtocolFamilyResolver.IsOpenProtocol(method));
        Assert.False(AnShengProtocolFamilyResolver.IsChargingPile(method));
    }

    /// <summary>
    /// <b>T14 的核心断言</b>：拼写错误与协议外方法一律「不认识」。
    ///
    /// 这些输入在改造前<b>全部</b>会命中「不在二开目录 ⇒ 按 Legacy 下发」的兜底分支，
    /// 被构造成充电桩报文真实外发。现在必须三个判定全为假、Resolve 返回 null。
    /// 特别注意大小写与首尾空格：Ordinal 比较，<c>OrderStart</c> 不等于 <c>orderStart</c>。
    /// </summary>
    [Theory]
    [InlineData("orderStrat")]      // 字母顺序拼错
    [InlineData("OrderStart")]      // 首字母大写
    [InlineData("orderstart")]      // 全小写
    [InlineData("ORDERSTART")]      // 全大写
    [InlineData("orderStart2")]     // 多一个字符
    [InlineData("order Start")]     // 中间混入空格
    [InlineData("getSwitchConfig")] // 历史伪命令
    [InlineData("rebooot")]         // 二开命令拼错
    [InlineData("definitelyNotAProtocolMethod")]
    public void Resolve_UnknownMethods_AreRejected(string method)
    {
        Assert.False(AnShengProtocolFamilyResolver.TryResolve(method, out _, out var spec));
        Assert.Null(spec);
        Assert.Null(AnShengProtocolFamilyResolver.Resolve(method));

        // 三个便捷判定都必须为假 —— 任何一个漏判都会让报文重新流向现网。
        Assert.False(AnShengProtocolFamilyResolver.IsKnown(method));
        Assert.False(AnShengProtocolFamilyResolver.IsChargingPile(method));
        Assert.False(AnShengProtocolFamilyResolver.IsOpenProtocol(method));
    }

    /// <summary>空值与空白同样属于「不认识」，不得进入任何构造分支。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_NullOrBlank_IsRejected(string? method)
    {
        Assert.False(AnShengProtocolFamilyResolver.TryResolve(method, out _, out _));
        Assert.False(AnShengProtocolFamilyResolver.IsKnown(method));
        Assert.False(AnShengProtocolFamilyResolver.IsChargingPile(method));
    }

    /// <summary>
    /// Legacy 构建器对非充电桩方法必须<b>快速失败</b>，而不是照单构造。
    /// 这是「零报文出网」的最后一道闸：即便上游判定被绕过，构造这一步也拦得住。
    /// </summary>
    [Theory]
    [InlineData("orderStrat")]
    [InlineData("getSwitchConfig")]
    [InlineData("reboot")]          // 属二开协议，同样不许走 Legacy 构造
    [InlineData("")]
    [InlineData(null)]
    public void LegacyBuilder_RejectsNonChargingPileMethod(string? method)
    {
        var builder = new AnShengLegacyCommandBuilder();

        Assert.Throws<NotSupportedException>(
            () => AnShengLegacyCommandBuilder.EnsureChargingPileMethod(method));

        Assert.Throws<NotSupportedException>(
            () => builder.BuildCommand(TestImei, method!));
    }

    /// <summary>充电桩方法则必须正常放行（防止上一条断言把闸门焊死）。</summary>
    [Theory]
    [InlineData("orderStart")]
    [InlineData("orderEnd")]
    [InlineData("orderUp")]
    public void LegacyBuilder_AcceptsChargingPileMethod(string method)
    {
        var builder = new AnShengLegacyCommandBuilder();

        AnShengLegacyCommandBuilder.EnsureChargingPileMethod(method); // 不抛即通过

        var (frameId, payload) = builder.BuildCommand(TestImei, method);

        Assert.False(string.IsNullOrWhiteSpace(frameId));
        Assert.Contains($"\"method\":\"{method}\"", payload);
    }

    // ─────────────────────────────────────────────────────────────
    // 组 3：验收标准 ② —— Legacy 报文结构保持不变
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// <b>验收标准 ②</b>：<c>orderStart</c> 报文结构与改造前完全一致 ——
    /// param 包裹、毫秒<b>字符串</b> timestamp、字段顺序 method→imei→frameId→timestamp→param。
    /// </summary>
    [Fact]
    public void BuildOrderStart_PreservesLegacyWireFormat()
    {
        var builder = new AnShengLegacyCommandBuilder();
        var (frameId, payload) = builder.BuildOrderStart(TestImei, "SN001", order: 2, limit: 3600);

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;

        // 1) 顶层字段与顺序
        var topLevelNames = root.EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Equal(new[] { "method", "imei", "frameId", "timestamp", "param" }, topLevelNames);

        Assert.Equal("orderStart", root.GetProperty("method").GetString());
        Assert.Equal(TestImei, root.GetProperty("imei").GetString());
        Assert.Equal(frameId, root.GetProperty("frameId").GetString());

        // 2) timestamp 必须是「毫秒字符串」，不是数字、也不是秒
        var tsProp = root.GetProperty("timestamp");
        Assert.Equal(JsonValueKind.String, tsProp.ValueKind);
        var tsRaw = tsProp.GetString();
        Assert.True(long.TryParse(tsRaw, out var tsMs), $"timestamp 不是数字字符串: {tsRaw}");
        Assert.Equal(13, tsRaw!.Length);                       // 毫秒时间戳 13 位
        var deltaMs = Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - tsMs);
        Assert.True(deltaMs < 60_000, $"timestamp 偏离当前时间过大: {deltaMs}ms");

        // 3) 业务参数必须<b>包在 param 内</b>，不得平铺到顶层
        var param = root.GetProperty("param");
        Assert.Equal(JsonValueKind.Object, param.ValueKind);
        Assert.Equal("SN001", param.GetProperty("sn").GetString());
        Assert.Equal(2, param.GetProperty("order").GetInt32());
        Assert.Equal(3600, param.GetProperty("limit").GetInt32());

        Assert.False(root.TryGetProperty("sn", out _));
        Assert.False(root.TryGetProperty("order", out _));
    }

    /// <summary>可选字段 <c>limit</c> 不传时不得出现在报文中（保持改造前行为）。</summary>
    [Fact]
    public void BuildOrderStart_WithoutLimit_OmitsLimitField()
    {
        var builder = new AnShengLegacyCommandBuilder();
        var (_, payload) = builder.BuildOrderStart(TestImei, "SN002");

        using var doc = JsonDocument.Parse(payload);
        var param = doc.RootElement.GetProperty("param");

        Assert.False(param.TryGetProperty("limit", out _));
        Assert.Equal(1, param.GetProperty("order").GetInt32()); // 默认插槽 1
    }

    /// <summary><c>orderEnd</c> 同样是 param 包裹结构。</summary>
    [Fact]
    public void BuildOrderEnd_PreservesLegacyWireFormat()
    {
        var builder = new AnShengLegacyCommandBuilder();
        var (_, payload) = builder.BuildOrderEnd(TestImei, "SN003", reason: "manual");

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;

        Assert.Equal("orderEnd", root.GetProperty("method").GetString());
        Assert.Equal(JsonValueKind.String, root.GetProperty("timestamp").ValueKind);

        var param = root.GetProperty("param");
        Assert.Equal("SN003", param.GetProperty("sn").GetString());
        Assert.Equal("manual", param.GetProperty("reason").GetString());
    }

    /// <summary>无参命令不输出空的 <c>param</c> 键（避免给设备端多送一个空对象）。</summary>
    [Fact]
    public void BuildCommand_WithoutParams_OmitsParamObject()
    {
        var builder = new AnShengLegacyCommandBuilder();
        var (_, payload) = builder.BuildCommand(TestImei, "orderUp");

        using var doc = JsonDocument.Parse(payload);
        Assert.False(doc.RootElement.TryGetProperty("param", out _));
    }

    /// <summary>
    /// 调用方预先登记的 frameId 必须被沿用 —— T7-2「先登记在途、后发 MQTT」依赖这条，
    /// 若构建器自作主张重新发号，在途表的 key 与报文对不上，命令只能走超时兜底。
    /// </summary>
    [Fact]
    public void BuildCommand_HonorsCallerSuppliedFrameId()
    {
        var builder = new AnShengLegacyCommandBuilder();
        const string preRegistered = "a1b2c3d4e5f60718";

        var (frameId, payload) = builder.BuildCommand(TestImei, "orderStart", null, preRegistered);

        Assert.Equal(preRegistered, frameId);
        using var doc = JsonDocument.Parse(payload);
        Assert.Equal(preRegistered, doc.RootElement.GetProperty("frameId").GetString());
    }

    /// <summary>
    /// 归位后的构建器与主构建器保留的兼容外壳必须产出<b>同构</b>报文。
    /// 既有测试仍在调 <see cref="AnShengCommandBuilder"/> 上的三个 Legacy 方法，
    /// 这条断言确保「转发壳」没有在转发途中改变任何结构。
    /// </summary>
    [Fact]
    public void LegacyBuilder_AndCompatibilityShell_ProduceSameShape()
    {
        var shell = new AnShengCommandBuilder();
        var legacy = new AnShengLegacyCommandBuilder();

        var (_, viaShell) = shell.BuildOrderStart(TestImei, "SN009", order: 3);
        var (_, viaLegacy) = legacy.BuildOrderStart(TestImei, "SN009", order: 3);

        using var docShell = JsonDocument.Parse(viaShell);
        using var docLegacy = JsonDocument.Parse(viaLegacy);

        // frameId / timestamp 每次都变，比较结构与业务字段即可
        Assert.Equal(
            docShell.RootElement.EnumerateObject().Select(p => p.Name),
            docLegacy.RootElement.EnumerateObject().Select(p => p.Name));

        Assert.Equal(
            docShell.RootElement.GetProperty("param").GetRawText(),
            docLegacy.RootElement.GetProperty("param").GetRawText());

        Assert.Equal(JsonValueKind.String, docShell.RootElement.GetProperty("timestamp").ValueKind);
        Assert.Equal(JsonValueKind.String, docLegacy.RootElement.GetProperty("timestamp").ValueKind);
    }

    // ─────────────────────────────────────────────────────────────
    // 组 4：验收标准 ③ —— close 遗嘱在两协议族下都正确
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// <b>验收标准 ③</b>：Legacy 形态（param 包裹 + 毫秒字符串 timestamp）的 <c>close</c> 遗嘱。
    /// </summary>
    [Fact]
    public void CloseWill_LegacyShape_IsRecognized()
    {
        var parser = new AnShengMessageParser();
        const string json = """
        {"method":"close","imei":"864536072949900","timestamp":"1745396759000","param":{"reason":"offline"}}
        """;

        var message = parser.Parse(json);

        Assert.NotNull(message);
        Assert.True(AnShengMessageParser.IsWillMessage(message));
        Assert.Equal(AnShengMessageCategory.Close, parser.GetCategory(message!));

        var normalized = parser.NormalizeForSensorData(message!, "/devtoser/pub/864536072949900");
        using var doc = JsonDocument.Parse(normalized);
        Assert.Equal("close", doc.RootElement.GetProperty("method").GetString());
        Assert.Equal("864536072949900", doc.RootElement.GetProperty("imei").GetString());
    }

    /// <summary>
    /// <b>验收标准 ③</b>：二开形态（字段平铺 + 秒级 int timestamp）的 <c>close</c> 遗嘱。
    /// 两族走的是同一条判定，差别只在报文体是否被 param 包裹。
    /// </summary>
    [Fact]
    public void CloseWill_OpenProtocolShape_IsRecognized()
    {
        var parser = new AnShengMessageParser();
        const string json = """
        {"method":"close","imei":"864536072949900","timestamp":1745396759}
        """;

        var message = parser.Parse(json);

        Assert.NotNull(message);
        Assert.True(AnShengMessageParser.IsWillMessage(message));
        Assert.Equal(AnShengMessageCategory.Close, parser.GetCategory(message!));

        var normalized = parser.NormalizeForSensorData(message!, "/iot/server/iot-board/864536072949900");
        using var doc = JsonDocument.Parse(normalized);
        Assert.Equal("close", doc.RootElement.GetProperty("method").GetString());
        Assert.Equal("864536072949900", doc.RootElement.GetProperty("imei").GetString());
    }

    /// <summary>
    /// 最精简的遗嘱报文（只有 imei + method）也必须被认出来 ——
    /// 设备侧 willMessage 常常就是这两个字段，缺 timestamp 不能导致判定失败。
    /// </summary>
    [Fact]
    public void CloseWill_MinimalPayload_IsRecognized()
    {
        var parser = new AnShengMessageParser();
        var message = parser.Parse("""{"imei":"864536072949900","method":"close"}""");

        Assert.NotNull(message);
        Assert.True(AnShengMessageParser.IsWillMessage(message));
        Assert.Equal(AnShengMessageCategory.Close, parser.GetCategory(message!));
    }

    /// <summary>
    /// <c>close</c> 是<b>上行事件</b>，不是可下发命令：它不在任何一份下行目录里。
    /// 这条断言防止有人「顺手」把 close 加进 Legacy 目录 —— 那会让平台可以向设备下发遗嘱。
    /// </summary>
    [Fact]
    public void CloseWill_IsUplinkEventOnly_NotADownlinkCommand()
    {
        Assert.False(AnShengLegacyCommandCatalog.Contains(AnShengCommandCatalog.WillMethod));
        Assert.False(AnShengCommandCatalog.Commands.ContainsKey(AnShengCommandCatalog.WillMethod));
        Assert.False(AnShengProtocolFamilyResolver.IsKnown(AnShengCommandCatalog.WillMethod));

        // 但它必须是被承认的上行事件方法
        Assert.Contains(AnShengCommandCatalog.WillMethod, AnShengCommandCatalog.EventMethods);
    }

    /// <summary>
    /// 解析侧对两族订单报文的分类必须一致地走 Legacy 类别，
    /// 且方法名取自 Legacy 目录常量（归位后不再有第二份硬编码字面量）。
    /// </summary>
    [Theory]
    [InlineData("orderStart", AnShengMessageCategory.OrderStart)]
    [InlineData("orderEnd", AnShengMessageCategory.OrderEnd)]
    [InlineData("orderUp", AnShengMessageCategory.OrderUp)]
    public void Parser_CategorizesLegacyOrderMessages(string method, AnShengMessageCategory expected)
    {
        var parser = new AnShengMessageParser();

        // 刻意不用原始插值字符串：JSON 结尾的 "}}" 会被解析成插值收尾符，
        // 显式拼接虽然啰嗦，但不会因为多一层大括号转义规则而读错。
        var json = "{\"method\":\"" + method + "\",\"imei\":\"864536072949900\","
                   + "\"timestamp\":\"1745396759000\",\"param\":{\"sn\":\"SN001\",\"order\":1}}";

        var message = parser.Parse(json);

        Assert.NotNull(message);
        Assert.Equal(expected, parser.GetCategory(message!));

        // Legacy 报文体位于 param 内，解析侧必须取到 param 而不是整条 RawJson
        var order = parser.ParseOrderData(message!);
        Assert.NotNull(order);
        Assert.Equal("SN001", order!.Sn);
    }
}
