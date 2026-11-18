using System.Text.Json;
using IoTPlatform.Models;
using Xunit;

namespace IoTPlatform.AnSheng.Tests;

/// <summary>
/// T12 水流量应答解析单测（严格镜像 T11 的 <see cref="AnShengEmParsing"/> 测试风格）。
///
/// 【守护什么】
///   纯函数 <see cref="AnShengWaterFlowParsing"/> 必须在「设备应答真的回来」之前就能被直接注入 JSON 断言，
///   不必绕整条 MQTT 链路。两组用例：
///     · getFlowInfo  → iFlow / tFlow / tPulses / imei / timestamp 解析正确；
///     · getFlowConfig → k / offset / minFlow / maxFlow / imei / timestamp 解析正确；
///     · result 非 "ok" 时解析被拒（返回 null）；缺失 result 视为成功（宽松）。
///
/// 【与 T11 同口径】解析器是纯函数：不碰数据库、不写日志、不抛业务异常，脏数据一律宽松取默认值。
/// </summary>
public class AnShengWaterFlowParsingTests
{
    private static JsonElement ParseBody(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    // ─────────────────────────────────────────────────────────────
    // getFlowInfo
    // ─────────────────────────────────────────────────────────────

    /// <summary>result="ok" 的完整应答，各字段必须被正确解析。</summary>
    [Fact]
    public void ParseFlowInfo_ValidOk_ParsesAllFields()
    {
        const string json =
            "{\"method\":\"getFlowInfo\",\"result\":\"ok\",\"imei\":\"864536072949900\","
            + "\"timestamp\":1745396759,\"iFlow\":12.5,\"tFlow\":345.6,\"tPulses\":98765}";

        var snap = AnShengWaterFlowParsing.ParseFlowInfo(ParseBody(json));

        Assert.NotNull(snap);
        Assert.Equal(12.5, snap!.IFlow);
        Assert.Equal(345.6, snap.TFlow);
        Assert.Equal(98765L, snap.TPulses);
        Assert.Equal("864536072949900", snap.Imei);
        Assert.Equal(1745396759L, snap.Timestamp);
    }

    /// <summary>result 非 "ok"（如 "method unsupported"）必须被拒，返回 null。</summary>
    [Fact]
    public void ParseFlowInfo_ResultNotOk_ReturnsNull()
    {
        const string json =
            "{\"method\":\"getFlowInfo\",\"result\":\"method unsupported\","
            + "\"iFlow\":12.5,\"tFlow\":345.6,\"tPulses\":98765}";

        Assert.Null(AnShengWaterFlowParsing.ParseFlowInfo(ParseBody(json)));
    }

    /// <summary>result 为任意非 ok 字符串（此处 "fail"）一律视为失败。</summary>
    [Theory]
    [InlineData("fail")]
    [InlineData("error")]
    [InlineData("")]
    public void ParseFlowInfo_NonOkResultVariants_Rejected(string result)
    {
        var json = "{\"result\":\"" + result + "\",\"iFlow\":1,\"tFlow\":2,\"tPulses\":3}";
        Assert.Null(AnShengWaterFlowParsing.ParseFlowInfo(ParseBody(json)));
    }

    /// <summary>字段以字符串形态上报时（如 "12.5"）也必须能解析为数值。</summary>
    [Fact]
    public void ParseFlowInfo_NumericStringFields_Parsed()
    {
        const string json =
            "{\"result\":\"ok\",\"iFlow\":\"12.5\",\"tFlow\":\"345.6\",\"tPulses\":\"98765\",\"imei\":\"123\"}";

        var snap = AnShengWaterFlowParsing.ParseFlowInfo(ParseBody(json));

        Assert.NotNull(snap);
        Assert.Equal(12.5, snap!.IFlow);
        Assert.Equal(345.6, snap.TFlow);
        Assert.Equal(98765L, snap.TPulses);
    }

    /// <summary>缺失 tPulses 等非强制字段时返回 null（宽松取默认），不抛异常。</summary>
    [Fact]
    public void ParseFlowInfo_MissingOptionalFields_StillParsesScalars()
    {
        const string json = "{\"result\":\"ok\",\"iFlow\":1.2,\"tFlow\":3.4}";

        var snap = AnShengWaterFlowParsing.ParseFlowInfo(ParseBody(json));

        Assert.NotNull(snap);
        Assert.Equal(1.2, snap!.IFlow);
        Assert.Equal(3.4, snap.TFlow);
        Assert.Null(snap.TPulses);
        Assert.Null(snap.Imei);
        Assert.Null(snap.Timestamp);
    }

    // ─────────────────────────────────────────────────────────────
    // getFlowConfig
    // ─────────────────────────────────────────────────────────────

    /// <summary>result="ok" 的完整配置应答，四参数 + imei + timestamp 必须被正确解析。</summary>
    [Fact]
    public void ParseFlowConfig_ValidOk_ParsesAllFields()
    {
        const string json =
            "{\"method\":\"getFlowConfig\",\"result\":\"ok\",\"imei\":\"864536072949900\","
            + "\"timestamp\":1745396759,\"k\":11,\"offset\":0,\"minFlow\":1,\"maxFlow\":25}";

        var snap = AnShengWaterFlowParsing.ParseFlowConfig(ParseBody(json));

        Assert.NotNull(snap);
        Assert.Equal(11.0, snap!.K);
        Assert.Equal(0.0, snap.Offset);
        Assert.Equal(1.0, snap.MinFlow);
        Assert.Equal(25.0, snap.MaxFlow);
        Assert.Equal("864536072949900", snap.Imei);
        Assert.Equal(1745396759L, snap.Timestamp);
    }

    /// <summary>result 非 "ok" 必须被拒，返回 null。</summary>
    [Fact]
    public void ParseFlowConfig_ResultNotOk_ReturnsNull()
    {
        const string json =
            "{\"method\":\"getFlowConfig\",\"result\":\"fail\",\"k\":11,\"offset\":0,\"minFlow\":1,\"maxFlow\":25}";

        Assert.Null(AnShengWaterFlowParsing.ParseFlowConfig(ParseBody(json)));
    }

    /// <summary>缺失 result 字段时按宽松策略视为成功（设备偶发省略）。</summary>
    [Fact]
    public void ParseFlowConfig_MissingResult_TreatedAsOk()
    {
        const string json = "{\"k\":11,\"offset\":2.5,\"minFlow\":3,\"maxFlow\":30}";

        var snap = AnShengWaterFlowParsing.ParseFlowConfig(ParseBody(json));

        Assert.NotNull(snap);
        Assert.Equal(11.0, snap!.K);
        Assert.Equal(2.5, snap.Offset);
        Assert.Equal(3.0, snap.MinFlow);
        Assert.Equal(30.0, snap.MaxFlow);
    }

    /// <summary>配置参数以字符串形态上报时也必须能解析为数值。</summary>
    [Fact]
    public void ParseFlowConfig_NumericStringFields_Parsed()
    {
        const string json =
            "{\"result\":\"ok\",\"k\":\"11\",\"offset\":\"2.5\",\"minFlow\":\"3\",\"maxFlow\":\"30\"}";

        var snap = AnShengWaterFlowParsing.ParseFlowConfig(ParseBody(json));

        Assert.NotNull(snap);
        Assert.Equal(11.0, snap!.K);
        Assert.Equal(2.5, snap.Offset);
        Assert.Equal(3.0, snap.MinFlow);
        Assert.Equal(30.0, snap.MaxFlow);
    }
}
