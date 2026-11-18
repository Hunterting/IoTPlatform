// T12：安圣水流量模块（设计镜像 T11 电量计，用户要求「和电的一样接入」）。
//
// 【本文件承载三件事】
//   ① 平台侧水流量配置镜像表 <see cref="AnShengWaterFlowConfig"/>（<c>ansheng_wf_config</c>），
//      只镜像设备配置的最新值（不建历史读数表，与 T11 的「设备权威、平台只读镜像」口径一致）；
//   ② 两个应答解析模型 <see cref="AnShengWaterFlowInfoSnapshot"/> / <see cref="AnShengWaterFlowConfigSnapshot"/>；
//   ③ 纯函数解析器 <see cref="AnShengWaterFlowParsing"/>（参考 T11 的 <c>AnShengEmParsing</c>）。
//
// 【为什么配置单独建表而不是塞进 DeviceDataRecord】
//   流量配置（k / offset / minFlow / maxFlow）是设备的<b>参数</b>而非采样值，与逐点时序读数
//   语义不同；单独建表便于按设备做幂等 UPSERT 与租户隔离（见 <see cref="ConfigureAnShengWaterFlowConfig"/>）。
//
// 【租户隔离】配置表实现 <see cref="IHasAppCode"/>，由 <c>AppDbContext</c> 全局查询过滤器自动加
//   <c>WHERE AppCode = @current</c>。但<b>上行链路（Router 钩子 → ApplyFlowConfigReadbackAsync）跑在后台作用域</b>，
//   <c>ITenantContextAccessor.Current</c> 为 null 导致过滤器不生效，因此这些路径<b>必须</b>
//   <c>IgnoreQueryFilters</c> + 显式按 <see cref="DeviceId"/>（全局唯一）+ <see cref="AppCode"/> 定位（铁律①）。

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;
using System.Text.Json;
using IoTPlatform.Data;

namespace IoTPlatform.Models;

/// <summary>
/// 安圣水流量配置镜像表（T12，<c>ansheng_wf_config</c>）。一行 = 一台设备的流量配置。
///
/// 【租户隔离】实现 <see cref="IHasAppCode"/>，由 <c>AppDbContext</c> 全局查询过滤器自动加
///   <c>WHERE AppCode = @current</c>。上行链路（后台作用域）写回时<b>必须 IgnoreQueryFilters</b> +
///   显式赋值 <see cref="AppCode"/>（设备换租户后数据不留在旧租户下）。
///
/// 【幂等键】唯一索引 <c>(DeviceId, AppCode)</c>：<see cref="DeviceId"/> 全局唯一即可唯一定位一台设备，
///   附带 <see cref="AppCode"/> 是为了让后台作用域的 <c>IgnoreQueryFilters</c> 查询仍能走索引快速定位，
///   同时适配全局租户过滤器（过滤器按 AppCode 过滤，索引含 AppCode 才能被命中）。
/// </summary>
[Table("ansheng_wf_config")]
public class AnShengWaterFlowConfig : IHasAppCode
{
    /// <summary>内部主键。</summary>
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    /// <summary>租户码。后台作用域写入路径<b>必须显式赋值</b>（全局过滤器不生效，EF 不会替你填）。</summary>
    [Required, MaxLength(50)]
    public string AppCode { get; set; } = string.Empty;

    /// <summary>关联正式设备主键；配置是该设备的镜像，故非空。</summary>
    public long DeviceId { get; set; }

    /// <summary>流量系数 k（协议默认 11）。</summary>
    public double K { get; set; }

    /// <summary>流量偏移量 offset（协议默认 0）。</summary>
    public double Offset { get; set; }

    /// <summary>有效流量范围下限 minFlow（L/min，协议默认 1）。</summary>
    public double MinFlow { get; set; }

    /// <summary>有效流量范围上限 maxFlow（L/min，协议默认 25）。</summary>
    public double MaxFlow { get; set; }

    /// <summary>本行最后一次被设备应答刷新的时刻（UTC）。</summary>
    public DateTime SyncedAt { get; set; }
}

/// <summary>
/// 一次 <c>getFlowInfo</c> 应答的解析快照（asopen.md 水流量协议）。
/// 字段严格按文档：<c>iFlow</c> / <c>tFlow</c> / <c>tPulses</c> / <c>imei</c> / <c>timestamp</c>。
/// </summary>
public sealed class AnShengWaterFlowInfoSnapshot
{
    /// <summary>瞬时流量 iFlow（L/min）。</summary>
    public double IFlow { get; init; }

    /// <summary>累计流量 tFlow（m³ 或 L，取决于设备单位）。</summary>
    public double TFlow { get; init; }

    /// <summary>累计脉冲数 tPulses（设备计数值，int 量级，用 long? 留余量）。</summary>
    public long? TPulses { get; init; }

    /// <summary>设备 IMEI。</summary>
    public string? Imei { get; init; }

    /// <summary>设备时间戳（秒级 int）。</summary>
    public long? Timestamp { get; init; }
}

/// <summary>
/// 一次 <c>getFlowConfig</c> 应答的解析快照（asopen.md 水流量协议）。
/// 字段严格按文档：<c>k</c> / <c>offset</c> / <c>minFlow</c> / <c>maxFlow</c> / <c>imei</c> / <c>timestamp</c>。
/// </summary>
public sealed class AnShengWaterFlowConfigSnapshot
{
    /// <summary>流量系数 k（协议默认 11）。</summary>
    public double K { get; init; }

    /// <summary>流量偏移量 offset（协议默认 0）。</summary>
    public double Offset { get; init; }

    /// <summary>有效流量范围下限 minFlow（L/min，协议默认 1）。</summary>
    public double MinFlow { get; init; }

    /// <summary>有效流量范围上限 maxFlow（L/min，协议默认 25）。</summary>
    public double MaxFlow { get; init; }

    /// <summary>设备 IMEI。</summary>
    public string? Imei { get; init; }

    /// <summary>设备时间戳（秒级 int）。</summary>
    public long? Timestamp { get; init; }
}

/// <summary>
/// 水流量应答解析工具（T12）。
///
/// 【为什么是 public 而不是 internal】与 T11 的 <c>AnShengEmParsing</c> 同口径：
///   验收要求测试能<b>直接注入一段 getFlowInfo / getFlowConfig 应答 JSON</b> 并断言解析结果，
///   把纯函数暴露出来，测试就不必绕整条 MQTT 链路。
///
/// 【所有方法都是纯函数】不碰数据库、不写日志、不抛业务异常。
///   遇到脏数据一律「宽松取默认值 / 跳过该字段」而不是抛 —— 上行链路不能被一条烂报文打断。
///
/// 【result 非 "ok" 视为解析失败】设备应答的 <c>result</c> 字段非 <c>ok</c>（如 <c>method unsupported</c>
///   或具体失败原因）时，解析器返回 <c>null</c>，调用方据此跳过写回（参考 T11 的宽松哲学，但显式拒绝失败应答）。
/// </summary>
public static class AnShengWaterFlowParsing
{
    /// <summary>成功应答的 result 值。</summary>
    private const string ResultOk = "ok";

    /// <summary>
    /// 解析 <c>getFlowInfo</c> 应答体（字段平铺在 JSON 顶层）。
    /// </summary>
    /// <param name="root">应答体 JSON 根节点。</param>
    /// <returns>流量信息快照；报文非对象、或 <c>result</c> 非 <c>ok</c> 时返回 <c>null</c>（解析失败）。</returns>
    public static AnShengWaterFlowInfoSnapshot? ParseFlowInfo(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !IsResultOk(root))
        {
            return null;
        }

        return new AnShengWaterFlowInfoSnapshot
        {
            IFlow = TryGetDouble(root, "iFlow"),
            TFlow = TryGetDouble(root, "tFlow"),
            TPulses = TryGetNullableLong(root, "tPulses"),
            Imei = TryGetString(root, "imei"),
            Timestamp = TryGetNullableLong(root, "timestamp")
        };
    }

    /// <summary>
    /// 解析 <c>getFlowConfig</c> 应答体（字段平铺在 JSON 顶层）。
    /// </summary>
    /// <param name="root">应答体 JSON 根节点。</param>
    /// <returns>流量配置快照；报文非对象、或 <c>result</c> 非 <c>ok</c> 时返回 <c>null</c>（解析失败）。</returns>
    public static AnShengWaterFlowConfigSnapshot? ParseFlowConfig(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !IsResultOk(root))
        {
            return null;
        }

        return new AnShengWaterFlowConfigSnapshot
        {
            K = TryGetDouble(root, "k"),
            Offset = TryGetDouble(root, "offset"),
            MinFlow = TryGetDouble(root, "minFlow"),
            MaxFlow = TryGetDouble(root, "maxFlow"),
            Imei = TryGetString(root, "imei"),
            Timestamp = TryGetNullableLong(root, "timestamp")
        };
    }

    /// <summary>
    /// 判定应答 <c>result</c> 是否为成功。<c>result</c> 缺失时按宽松策略视为成功（设备偶发省略）。
    /// </summary>
    /// <param name="root">应答体 JSON 根节点。</param>
    /// <returns>是否可解析（成功应答）。</returns>
    private static bool IsResultOk(JsonElement root)
    {
        if (!root.TryGetProperty("result", out var resultEl))
        {
            return true;
        }

        var result = resultEl.ValueKind == JsonValueKind.String ? resultEl.GetString() : null;
        return string.Equals(result, ResultOk, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>读取数值属性（宽松：也接受可解析的字符串形态，如 <c>"12.5"</c>）。</summary>
    /// <param name="el">JSON 对象。</param>
    /// <param name="name">属性名。</param>
    /// <returns>数值；缺失或非数值时为 0。</returns>
    private static double TryGetDouble(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var prop))
        {
            return 0d;
        }

        switch (prop.ValueKind)
        {
            case JsonValueKind.Number:
                return prop.TryGetDouble(out var value) ? value : 0d;

            case JsonValueKind.String:
                return double.TryParse(
                    prop.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var textValue)
                    ? textValue
                    : 0d;

            default:
                return 0d;
        }
    }

    /// <summary>读取可空长整型属性（宽松：也接受可解析的字符串形态）。</summary>
    /// <param name="el">JSON 对象。</param>
    /// <param name="name">属性名。</param>
    /// <returns>长整型；缺失或非数值时为 <c>null</c>。</returns>
    private static long? TryGetNullableLong(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var prop))
        {
            return null;
        }

        switch (prop.ValueKind)
        {
            case JsonValueKind.Number:
                return prop.TryGetInt64(out var value) ? value : null;

            case JsonValueKind.String:
                return long.TryParse(
                    prop.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var textValue)
                    ? textValue
                    : null;

            default:
                return null;
        }
    }

    /// <summary>读取字符串属性；数值形态自动转成字符串（部分固件把 imei 当数字发）。</summary>
    /// <param name="el">JSON 对象。</param>
    /// <param name="name">属性名。</param>
    /// <returns>字符串；缺失时为 null。</returns>
    private static string? TryGetString(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var prop))
        {
            return null;
        }

        return prop.ValueKind switch
        {
            JsonValueKind.String => prop.GetString(),
            JsonValueKind.Number => prop.GetRawText(),
            _ => null
        };
    }
}
