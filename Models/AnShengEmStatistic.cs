// T11-1：安圣电量计统计聚合表模型（设计 D5 Option C —— 分层存储的「统计」那一层）。
//
// 【为什么统计要独立建表，而不是塞进 DeviceDataRecord】
//   DeviceDataRecord 是「单点时序表」，一行 = 某个时刻的一次采样。
//   而 getEMStatistics 返回的是**周期聚合快照**：同一次应答里同时包含 total（标量）、
//   hourSumData[48]（日内半小时画像）、hourData/dayData/monthData（带 date 键、可能不连续）。
//   把它们塞进时序表会造出「同一时刻多条互相冲突的记录」，且无法表达不连续的日期序列。
//
// 【幂等的根】getEMStatistics 是**全量快照式**返回（dayData 保留最近 30 条、monthData 最近 12 条），
//   每拉一次就重复一次。因此唯一键 (DeviceId, SlotNum, Granularity, PeriodKey) 是本表的生命线 ——
//   有它才能做 UPSERT，没它重复拉取会把表撑成垃圾场（验收 #1）。
//
// 【平台不跟随设备清空】设备侧 clearEMStatistics / 新订单启动都会清空累计电量，
//   但平台侧**只累积保留**（验收 #4）。清零只记一条标记事件用于对账，绝不删聚合行。

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;
using System.Text.Json;
using IoTPlatform.Data;

namespace IoTPlatform.Models;

/// <summary>
/// 电量计统计粒度（设计 D5：<c>Granularity ∈ {HourSum, Hour, Day, Month, Total}</c>）。
///
/// 【为什么以 int 落库】沿用 <see cref="AnShengEventKind"/> / <see cref="AnShengTimeTaskKind"/> 的既有范式：
///   MySQL 5.7.26 下不用原生 <c>ENUM</c>（新增枚举值要 <c>ALTER TABLE</c> 锁表，且 Pomelo 跨版本映射不稳）。
///
/// 【枚举值一经发布不得重排】数值即数据库里的存量数据，改序号等于改历史。
///
/// 【出网形态】全局已注册 <c>JsonStringEnumConverter</c>，本枚举以字符串出网
///   （<c>"Total"</c> / <c>"HourSum"</c> …），前端拿到的是可读值而非魔数。
/// </summary>
public enum AnShengEmGranularity
{
    /// <summary>累计电量（<c>data[].total</c>），每插槽仅一行，<c>PeriodKey</c> 恒为 <c>total</c>。</summary>
    Total = 0,

    /// <summary>
    /// 日内半小时分布画像（<c>data[].hourSumData</c>，定长 48）。
    /// 语义上<b>不是</b>时间序列而是「隔天累加的日内分布」，<c>PeriodKey</c> 为 <c>00:00</c>~<c>23:30</c>。
    /// </summary>
    HourSum = 1,

    /// <summary>半小时累计电量（<c>data[].hourData</c>），<c>PeriodKey</c> 为设备原样 <c>yyyyMMddHHmm</c>。</summary>
    Hour = 2,

    /// <summary>日累计电量（<c>data[].dayData</c>），<c>PeriodKey</c> 为设备原样 <c>yyyyMMdd</c>。</summary>
    Day = 3,

    /// <summary>月累计电量（<c>data[].monthData</c>），<c>PeriodKey</c> 为设备原样 <c>yyyyMM</c>。</summary>
    Month = 4
}

/// <summary>
/// 安圣电量计统计聚合行（T11，设计 D5）。一行 = <c>(DeviceId, SlotNum, Granularity, PeriodKey, Kwh)</c>。
///
/// 【租户隔离】实现 <see cref="IHasAppCode"/>，由 <c>AppDbContext</c> 全局查询过滤器自动加
///   <c>WHERE AppCode = @current</c>。但<b>上行链路（Router 钩子 → ApplyStatisticsReadbackAsync）跑在后台作用域</b>，
///   <c>ITenantContextAccessor.Current</c> 为 null 导致过滤器不生效，因此这些路径<b>必须</b>
///   <c>IgnoreQueryFilters</c> + 显式按 <see cref="DeviceId"/>（全局唯一）定位（设计 §7.1，铁律①）。
///
/// 【MySQL 5.7.26 兼容】
///   · <see cref="Granularity"/> 为 <c>int</c> 列（默认映射），禁原生 ENUM；
///   · 不使用 CHECK 约束、不使用函数索引（5.7 静默忽略，制造「以为有校验」的假象）；
///   · <see cref="PeriodKey"/> 为 <c>varchar(16)</c>：最长的 <c>yyyyMMddHHmm</c> 也只有 12 字符，
///     留 4 字符余量；<b>非空</b>是硬要求 —— MySQL 唯一索引对 NULL 不去重，
///     一旦允许 NULL，<c>Total</c> 行就会无限重复插入，验收 #1 直接崩。
/// </summary>
[Table("ansheng_em_statistics")]
public class AnShengEmStatistic : IHasAppCode
{
    /// <summary><see cref="PeriodKey"/> 列宽（varchar(16)），供写入方截断时复用。</summary>
    public const int PeriodKeyMaxLength = 16;

    /// <summary><see cref="AnShengEmGranularity.Total"/> 行的固定 <see cref="PeriodKey"/>（不可为空，见类注释）。</summary>
    public const string TotalPeriodKey = "total";

    /// <summary><c>hourSumData</c> 的协议固定长度（asopen.md L4327：数组长度 48）。</summary>
    public const int HourSumSlotCount = 48;

    /// <summary>内部主键。</summary>
    public long Id { get; set; }

    /// <summary>租户码。后台作用域写入路径<b>必须显式赋值</b>（全局过滤器不生效，EF 不会替你填）。</summary>
    [Required, MaxLength(50)]
    public string AppCode { get; set; } = string.Empty;

    /// <summary>关联正式设备主键；统计是该设备的镜像，故非空。</summary>
    public long DeviceId { get; set; }

    /// <summary>
    /// 插槽编号，从 1 开始；= 设备 <c>data[]</c> 下标 + 1（§7-R8：应答不含 slotNum，只能按序推导）。
    /// </summary>
    public int SlotNum { get; set; }

    /// <summary>统计粒度（int 落库）。</summary>
    public AnShengEmGranularity Granularity { get; set; } = AnShengEmGranularity.Total;

    /// <summary>
    /// 周期键。按 <see cref="Granularity"/> 取值：
    /// <list type="bullet">
    ///   <item><see cref="AnShengEmGranularity.Total"/> → <c>total</c>；</item>
    ///   <item><see cref="AnShengEmGranularity.HourSum"/> → <c>00:00</c>~<c>23:30</c>；</item>
    ///   <item><see cref="AnShengEmGranularity.Hour"/> → <c>yyyyMMddHHmm</c>（设备原样）；</item>
    ///   <item><see cref="AnShengEmGranularity.Day"/> → <c>yyyyMMdd</c>（设备原样）；</item>
    ///   <item><see cref="AnShengEmGranularity.Month"/> → <c>yyyyMM</c>（设备原样）。</item>
    /// </list>
    /// <b>原样保存设备给的 date 串</b>而不解析成 DateTime：设备时钟漂移是已知现象，
    /// 把脏日期强转成时间戳只会制造「看似合法的错数据」，对账时反而找不回原文。
    /// </summary>
    [Required, MaxLength(PeriodKeyMaxLength)]
    public string PeriodKey { get; set; } = string.Empty;

    /// <summary>累计电量，单位 kWh（度）。</summary>
    public double Kwh { get; set; }

    /// <summary>本行最后一次被设备应答刷新的时刻（UTC）；陈旧判定与对账用。</summary>
    public DateTime SyncedAt { get; set; }

    /// <summary>
    /// 按粒度归一化 <paramref name="periodKey"/>：去空白 + 截断到列宽。
    ///
    /// 【为什么必须截断】这份字符串是<b>设备</b>写进来的，不是平台生成的。
    /// 固件一旦多给几位，落库就撞 <c>DbUpdateException</c>，整条上行链路当场断掉。
    /// </summary>
    /// <param name="periodKey">原始周期键。</param>
    /// <returns>可安全落库的周期键；输入为空白时返回空串（调用方据此丢弃该点）。</returns>
    public static string ClampPeriodKey(string? periodKey)
    {
        if (string.IsNullOrWhiteSpace(periodKey))
        {
            return string.Empty;
        }

        var trimmed = periodKey.Trim();
        return trimmed.Length <= PeriodKeyMaxLength
            ? trimmed
            : trimmed.Substring(0, PeriodKeyMaxLength);
    }

    /// <summary>
    /// 把 <c>hourSumData</c> 的数组下标换算成槽位串（<c>00:00</c>~<c>23:30</c>，验收 #2）。
    /// 下标 <c>i</c> 覆盖 <c>[i*30min, (i+1)*30min)</c>。
    /// </summary>
    /// <param name="index">数组下标，0~47。</param>
    /// <returns>形如 <c>13:30</c> 的槽位串。</returns>
    public static string HourSumPeriodKey(int index)
    {
        var hour = index / 2;
        var minute = index % 2 == 0 ? 0 : 30;
        return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}", hour, minute);
    }
}

/// <summary>
/// 一次 <c>getEMStatistics</c> 应答里的单个统计点（粒度 + 周期键 + 电量）。
/// </summary>
public sealed class AnShengEmStatisticPoint
{
    /// <summary>统计粒度。</summary>
    public AnShengEmGranularity Granularity { get; init; } = AnShengEmGranularity.Total;

    /// <summary>周期键（含义见 <see cref="AnShengEmStatistic.PeriodKey"/>）。</summary>
    public string PeriodKey { get; init; } = string.Empty;

    /// <summary>累计电量（kWh）。</summary>
    public double Kwh { get; init; }
}

/// <summary>
/// 一次 <c>getEMStatistics</c> 应答里单个插槽的统计集合（按 <c>data[]</c> 下标 +1 推导插槽号）。
/// </summary>
public sealed class AnShengEmSlotStatistic
{
    /// <summary>插槽编号，从 1 开始。</summary>
    public int SlotNum { get; init; }

    /// <summary>该插槽的全部统计点（已按粒度展开）。</summary>
    public List<AnShengEmStatisticPoint> Points { get; init; } = new();

    /// <summary>
    /// 该插槽 <c>hourSumData</c> 的实际数组长度；未携带该键时为 <c>-1</c>。
    ///
    /// 【为什么要把它带出来】协议规定定长 48（asopen.md L4327）。长度不符说明固件违反协议，
    /// 此时 <see cref="Points"/> 里<b>不会</b>包含任何 HourSum 点 —— 宁可少写，
    /// 也不能把错位的半小时画像写进库（验收 #2 的反面）。调用方据此打告警。
    /// </summary>
    public int HourSumLength { get; init; } = -1;
}

/// <summary>
/// 一次 <c>getEMStatistics</c> 应答的整体快照（含 <c>data[]</c> 长度，供插槽数校验用）。
/// </summary>
public sealed class AnShengEmStatisticsSnapshot
{
    /// <summary><c>data[]</c> 数组长度；应答不含 <c>data[]</c> 时为 <c>-1</c>。</summary>
    public int DataLength { get; init; } = -1;

    /// <summary>逐插槽的统计集合。</summary>
    public List<AnShengEmSlotStatistic> Slots { get; init; } = new();
}

/// <summary>
/// 一次 <c>getEMRealtime</c> 应答里单个插槽的实时电量计读数（asopen.md L2401-2407）。
/// </summary>
public sealed class AnShengEmRealtimeSlot
{
    /// <summary>插槽编号，从 1 开始（= <c>data[]</c> 下标 + 1）。</summary>
    public int SlotNum { get; init; }

    /// <summary>有效电压 <c>v</c>（V，多相时为平均值）；未上报为 null。</summary>
    public double? Voltage { get; init; }

    /// <summary>有效电流 <c>c</c>（A，多相时为总和）；未上报为 null。</summary>
    public double? Current { get; init; }

    /// <summary>有效功率 <c>p</c>（W，多相时为总和）；未上报为 null。</summary>
    public double? Power { get; init; }

    /// <summary>插槽总运行度数 <c>e</c>（kWh）；未上报为 null。</summary>
    public double? Energy { get; init; }

    /// <summary>本插槽是否至少带回一个有效读数。全空的插槽不参与落库，避免造出全 null 的噪声行。</summary>
    public bool HasAnyValue => Voltage.HasValue || Current.HasValue || Power.HasValue || Energy.HasValue;
}

/// <summary>
/// 电量计应答解析工具（T11）。
///
/// 【为什么是 public 而不是 internal】与 T10 的 <c>AnShengTimeTaskParsing</c> 不同，
///   T11 的验收 #1/#2/#3 要求测试能<b>直接注入一段 getEMStatistics 应答 JSON</b> 并断言
///   「行数不变 / 48 项齐全 / 不产生空洞行」。把纯函数暴露出来，测试就不必绕整条 MQTT 链路。
///
/// 【所有方法都是纯函数】不碰数据库、不写日志、不抛业务异常。
///   遇到脏数据一律「跳过该点」而不是抛 —— 上行链路不能被一条烂报文打断。
/// </summary>
public static class AnShengEmParsing
{
    /// <summary>
    /// 解析 <c>getEMStatistics</c> 应答体。
    ///
    /// 【空洞行的防线（验收 #3）】<c>hourData</c> / <c>dayData</c> / <c>monthData</c> 是
    ///   <b>可能不连续</b>的稀疏序列（协议明说「没记录到的日期表示无累计电量或超出保留期」）。
    ///   本方法<b>只为数组里真实存在的元素产出点</b>，绝不按日期区间补齐 —— 补出来的 0 行
    ///   会被前端当成「那天真的用了 0 度电」，与「那天没数据」是两回事。
    /// </summary>
    /// <param name="root">应答体 JSON 根节点。</param>
    /// <returns>统计快照；无 <c>data[]</c> 时 <see cref="AnShengEmStatisticsSnapshot.DataLength"/> 为 -1。</returns>
    public static AnShengEmStatisticsSnapshot ParseStatistics(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("data", out var dataEl) ||
            dataEl.ValueKind != JsonValueKind.Array)
        {
            return new AnShengEmStatisticsSnapshot { DataLength = -1 };
        }

        var slots = new List<AnShengEmSlotStatistic>();
        var index = 0;

        foreach (var slotEl in dataEl.EnumerateArray())
        {
            // §7-R8：data[] 按插槽 1~n 顺序排列，应答不含 slotNum，下标 i 对应插槽 i+1。
            var slotNum = index + 1;
            index++;

            if (slotEl.ValueKind != JsonValueKind.Object)
            {
                // 元素不是对象 —— 仍然占一个插槽位（否则后续插槽号会整体前移错位）。
                slots.Add(new AnShengEmSlotStatistic { SlotNum = slotNum });
                continue;
            }

            var points = new List<AnShengEmStatisticPoint>();

            // ① total：标量，PeriodKey 固定为 "total"（唯一键需要非空值）。
            if (TryGetDouble(slotEl, "total", out var total))
            {
                points.Add(new AnShengEmStatisticPoint
                {
                    Granularity = AnShengEmGranularity.Total,
                    PeriodKey = AnShengEmStatistic.TotalPeriodKey,
                    Kwh = total
                });
            }

            // ② hourSumData：定长 48 的裸数值数组（无 date 键），下标即槽位。
            var hourSumLength = -1;
            if (slotEl.TryGetProperty("hourSumData", out var hourSumEl) &&
                hourSumEl.ValueKind == JsonValueKind.Array)
            {
                hourSumLength = hourSumEl.GetArrayLength();

                if (hourSumLength == AnShengEmStatistic.HourSumSlotCount)
                {
                    var i = 0;
                    foreach (var item in hourSumEl.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.Number &&
                            item.TryGetDouble(out var kwh))
                        {
                            points.Add(new AnShengEmStatisticPoint
                            {
                                Granularity = AnShengEmGranularity.HourSum,
                                PeriodKey = AnShengEmStatistic.HourSumPeriodKey(i),
                                Kwh = kwh
                            });
                        }

                        i++;
                    }
                }
                // 长度不符 ⇒ 一个 HourSum 点都不产出（见 AnShengEmSlotStatistic.HourSumLength 注释）。
            }

            // ③ hourData / dayData / monthData：带 date 键的稀疏序列。
            AppendDatedPoints(slotEl, "hourData", AnShengEmGranularity.Hour, points);
            AppendDatedPoints(slotEl, "dayData", AnShengEmGranularity.Day, points);
            AppendDatedPoints(slotEl, "monthData", AnShengEmGranularity.Month, points);

            slots.Add(new AnShengEmSlotStatistic
            {
                SlotNum = slotNum,
                Points = points,
                HourSumLength = hourSumLength
            });
        }

        return new AnShengEmStatisticsSnapshot
        {
            DataLength = dataEl.GetArrayLength(),
            Slots = slots
        };
    }

    /// <summary>
    /// 解析 <c>getEMRealtime</c> 应答体的 <c>data[]</c>（每插槽 <c>v</c>/<c>c</c>/<c>p</c>/<c>e</c>）。
    /// </summary>
    /// <param name="root">应答体 JSON 根节点。</param>
    /// <returns>逐插槽实时读数；无 <c>data[]</c> 时为空列表。</returns>
    public static List<AnShengEmRealtimeSlot> ParseRealtime(JsonElement root)
    {
        var result = new List<AnShengEmRealtimeSlot>();

        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("data", out var dataEl) ||
            dataEl.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        var index = 0;
        foreach (var slotEl in dataEl.EnumerateArray())
        {
            var slotNum = index + 1;
            index++;

            if (slotEl.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            result.Add(new AnShengEmRealtimeSlot
            {
                SlotNum = slotNum,
                Voltage = TryGetNullableDouble(slotEl, "v"),
                Current = TryGetNullableDouble(slotEl, "c"),
                Power = TryGetNullableDouble(slotEl, "p"),
                Energy = TryGetNullableDouble(slotEl, "e")
            });
        }

        return result;
    }

    /// <summary>
    /// 把带 <c>date</c> 键的稀疏数组展开成统计点，追加进 <paramref name="sink"/>。
    /// 缺 <c>date</c> 或 <c>kwh</c> 非数值的元素直接跳过（不补 0、不补日期）。
    /// </summary>
    /// <param name="slotEl">单插槽 JSON 对象。</param>
    /// <param name="propertyName">数组属性名（hourData / dayData / monthData）。</param>
    /// <param name="granularity">对应粒度。</param>
    /// <param name="sink">输出容器。</param>
    private static void AppendDatedPoints(
        JsonElement slotEl, string propertyName, AnShengEmGranularity granularity,
        List<AnShengEmStatisticPoint> sink)
    {
        if (!slotEl.TryGetProperty(propertyName, out var arrayEl) ||
            arrayEl.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in arrayEl.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var periodKey = AnShengEmStatistic.ClampPeriodKey(TryGetString(item, "date"));
            if (periodKey.Length == 0)
            {
                // 没有 date 就无法定位周期 —— 写进去就是一条永远查不到的孤儿行。
                continue;
            }

            if (!TryGetDouble(item, "kwh", out var kwh))
            {
                continue;
            }

            sink.Add(new AnShengEmStatisticPoint
            {
                Granularity = granularity,
                PeriodKey = periodKey,
                Kwh = kwh
            });
        }
    }

    /// <summary>读取数值属性（宽松：也接受可解析的字符串形态）。</summary>
    /// <param name="el">JSON 对象。</param>
    /// <param name="name">属性名。</param>
    /// <param name="value">出参：解析结果。</param>
    /// <returns>是否成功取到数值。</returns>
    private static bool TryGetDouble(JsonElement el, string name, out double value)
    {
        value = 0d;

        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(name, out var prop))
        {
            return false;
        }

        switch (prop.ValueKind)
        {
            case JsonValueKind.Number:
                return prop.TryGetDouble(out value);

            case JsonValueKind.String:
                // 部分固件把电量以字符串回传（"5.5258"）。宽松接受，但不接受空串。
                return double.TryParse(
                    prop.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

            default:
                return false;
        }
    }

    /// <summary><see cref="TryGetDouble"/> 的可空包装。</summary>
    /// <param name="el">JSON 对象。</param>
    /// <param name="name">属性名。</param>
    /// <returns>数值；缺失或非数值时为 null。</returns>
    private static double? TryGetNullableDouble(JsonElement el, string name)
        => TryGetDouble(el, name, out var value) ? value : null;

    /// <summary>读取字符串属性；数值形态自动转成字符串（部分固件把 date 当数字发）。</summary>
    /// <param name="el">JSON 对象。</param>
    /// <param name="name">属性名。</param>
    /// <returns>字符串；缺失时为 null。</returns>
    private static string? TryGetString(JsonElement el, string name)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(name, out var prop))
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
