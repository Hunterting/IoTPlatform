// T10-1：安圣定时任务镜像模型。
//
// 【与 T8 延时任务的关系】
//   延时任务（AnShengDelayTask）是「每设备每插槽恰好一行」——设备侧一路只有一个延时任务。
//   定时任务不同：一路插槽下同时挂着<b>两个数组</b>（timeTasks 普通定时 / loopTimeTasks 循环定时），
//   每个数组又可有多项。因此本表是「每设备每插槽每类每序号一行」，
//   唯一键 (DeviceId, SlotNum, TaskKind, TaskIndex)。
//
// 【设备权威（D6 Option A）】平台镜像是快照不是账本：
//   setTimeTasks / setSlotTimeTasks 是<b>整表覆盖</b>语义，成功后先落一份乐观镜像，
//   随后写后回读（getTimeTasks / getSlotTimeTasks）用设备真值推翻它。
//   timeEvent 携带完整 task 对象时就地更新对应行，不额外发命令。
//
// 【MySQL 5.7.26 兼容】
//   · TaskKind 是 CLR 枚举，EF Core 默认映射为 int 列，<b>不做任何显式值转换</b>，
//     也不使用数据库原生的枚举列类型（5.7 的原生枚举列改值要 DDL，运维代价高）；
//   · WeekDays 存 JSON 字符串（如 "[1,4,5]"），5.7 无数组列类型；
//   · 不使用数据库层的取值约束、不使用函数索引（5.7 静默忽略前者、不支持后者）；
//   · 时间列 datetime(6) 存 UTC。

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text.Json;
using IoTPlatform.Data;

namespace IoTPlatform.Models;

/// <summary>
/// 定时任务的两种形态。
///
/// 【为什么用 CLR 枚举而不是字符串列】
///   取值集合由协议钉死（普通 / 循环），永远只有两个；用 int 列存储既省空间又能进复合索引前缀。
///   出网时由全局注册的 <c>JsonStringEnumConverter</c> 序列化成 <c>"Normal"</c> / <c>"Loop"</c> 字符串，
///   前端拿到的是可读值而不是魔数 0/1。
/// </summary>
public enum AnShengTimeTaskKind
{
    /// <summary>普通定时任务（协议 <c>timeTasks[]</c>）：到点执行一次 <c>action</c>。</summary>
    Normal = 0,

    /// <summary>循环定时任务（协议 <c>loopTimeTasks[]</c>）：时间窗内按 onMins/offMins 往复通断。</summary>
    Loop = 1,
}

/// <summary>
/// 安圣定时任务镜像行（D6 Option A：设备权威 + 平台只读镜像）。
///
/// 【租户隔离】实现 <see cref="IHasAppCode"/>，由 <c>AppDbContext</c> 全局查询过滤器自动加
///   <c>WHERE AppCode = @current</c>。但<b>上行链路（Router / TimeEventHandler / 写后回读延时作用域）
///   跑在后台作用域</b>，<c>ITenantContextAccessor.Current</c> 为 null 导致过滤器不生效，
///   因此这些路径<b>必须</b> <c>IgnoreQueryFilters</c> + 显式按 <see cref="DeviceId"/>（全局唯一）定位，
///   新建行的 AppCode 显式取自 Devices 表（详见设计文档 §7.1）。
///
/// 【乐观并发】<see cref="RowVersion"/> 由 <c>AppDbContext</c> 声明为并发令牌：
///   EF 生成的 UPDATE / DELETE 会带上 <c>AND RowVersion = @original</c>。
///   两个管理员并发整表覆盖同一插槽时，后写者影响 0 行 → <c>DbUpdateConcurrencyException</c> → API 返回 409，
///   从而避免「后写者用自己看到的旧列表把先写者刚加的任务抹掉」。
/// </summary>
[Table("ansheng_time_tasks")]
public class AnShengTimeTask : IHasAppCode
{
    /// <summary><see cref="TaskId"/> 列宽。设备下发的 id 是毫秒时间戳字符串（13 位），留足冗余。</summary>
    public const int TaskIdColumnLength = 32;

    /// <summary><see cref="Action"/> 列宽（on/off/toggle）。</summary>
    public const int ActionColumnLength = 16;

    /// <summary><see cref="WeekDays"/> 列宽。最长形态 <c>[1,2,3,4,5,6,7]</c> 仅 15 字符，64 足够冗余。</summary>
    public const int WeekDaysColumnLength = 64;

    /// <summary>内部主键。</summary>
    public long Id { get; set; }

    /// <summary>租户码。后台作用域写入路径<b>必须显式赋值</b>（全局过滤器不生效，EF 不会替你填）。</summary>
    [Required, MaxLength(50)]
    public string AppCode { get; set; } = string.Empty;

    /// <summary>关联正式设备主键；定时任务是该设备的镜像，故非空。</summary>
    public long DeviceId { get; set; }

    /// <summary>插槽编号，从 1 开始；= 设备 <c>tasks[]</c> 下标 + 1（§7-R9）。</summary>
    public int SlotNum { get; set; }

    /// <summary>任务类型：普通 / 循环。</summary>
    public AnShengTimeTaskKind TaskKind { get; set; } = AnShengTimeTaskKind.Normal;

    /// <summary>
    /// 同插槽同类型内的序号，<b>从 1 开始</b>，= 设备数组下标 + 1。
    ///
    /// <c>timeEvent</c> 报文的 <c>taskIndex</c> 即按此语义定位（协议：任务索引，从 1 开始）。
    /// </summary>
    public int TaskIndex { get; set; }

    /// <summary>设备分配的任务 id（设置定时任务时由设备生成，形如 <c>"1779345917718"</c>）；可能为空串。</summary>
    [MaxLength(TaskIdColumnLength)]
    public string TaskId { get; set; } = string.Empty;

    /// <summary>任务是否启用。<c>weekDays</c> 为空数组的一次性任务，设备执行后会自行置 false。</summary>
    public bool Enable { get; set; }

    /// <summary>
    /// 每周生效的星期几，JSON 数组字符串（如 <c>"[1,4,5]"</c>，1=周一 … 7=周日）。
    /// <c>"[]"</c> 表示仅执行一次。
    /// </summary>
    [MaxLength(WeekDaysColumnLength)]
    public string WeekDays { get; set; } = "[]";

    /// <summary>【普通定时】动作小时（0-23）。循环定时行恒为 0。</summary>
    public int Hour { get; set; }

    /// <summary>【普通定时】动作分钟（0-59）。循环定时行恒为 0。</summary>
    public int Minute { get; set; }

    /// <summary>【普通定时】动作：<c>on</c> / <c>off</c> / <c>toggle</c>。循环定时行恒为空串。</summary>
    [MaxLength(ActionColumnLength)]
    public string Action { get; set; } = string.Empty;

    /// <summary>【普通定时】任务触发时是否上报 <c>timeEvent</c>（固件 v5.0.1+）。</summary>
    public bool UploadEnable { get; set; }

    /// <summary>【循环定时】每天循环开始的小时。普通定时行恒为 0。</summary>
    public int SHour { get; set; }

    /// <summary>【循环定时】每天循环开始的分钟。普通定时行恒为 0。</summary>
    public int SMinute { get; set; }

    /// <summary>【循环定时】每天循环结束的小时。普通定时行恒为 0。</summary>
    public int EHour { get; set; }

    /// <summary>【循环定时】每天循环结束的分钟。普通定时行恒为 0。</summary>
    public int EMinute { get; set; }

    /// <summary>【循环定时】循环中打开的分钟数。普通定时行恒为 0。</summary>
    public int OnMins { get; set; }

    /// <summary>【循环定时】循环中关闭的分钟数。普通定时行恒为 0。</summary>
    public int OffMins { get; set; }

    /// <summary>镜像最后与设备同步的时刻（UTC）；陈旧判定（&gt; 24h 标 <c>IsStale</c>）用。</summary>
    public DateTime SyncedAt { get; set; }

    /// <summary>
    /// 乐观并发令牌，每次写入 +1。
    ///
    /// 【为什么是 long 而不是 byte[]】MySQL 没有 SQL Server 那种自增 rowversion 列类型；
    /// Pomelo 对 <c>IsRowVersion()</c> 的支持依赖 <c>timestamp</c> 列的秒级精度，
    /// 同一秒内的两次并发编辑会检测不出冲突 —— 而这正是验收 #5 要覆盖的场景。
    /// 用平台自增的 bigint 令牌语义最明确，且天然可回传给前端做「你看到的是第几版」判断。
    /// </summary>
    public long RowVersion { get; set; }

    /// <summary>
    /// 把星期数组序列化成落库串。
    ///
    /// 会做「去重 + 升序 + 丢弃 1~7 之外的值」的收敛：设备回读的数据不受平台校验保护，
    /// 存进来的脏值将来会被原样下发回设备，得在入口就掐掉。
    /// </summary>
    /// <param name="weekDays">星期数组，可为 null。</param>
    /// <returns>JSON 数组字符串，如 <c>"[1,4,5]"</c>；无有效项时为 <c>"[]"</c>。</returns>
    public static string SerializeWeekDays(IReadOnlyList<int>? weekDays)
    {
        if (weekDays == null || weekDays.Count == 0)
        {
            return "[]";
        }

        var cleaned = weekDays
            .Where(d => d >= 1 && d <= 7)
            .Distinct()
            .OrderBy(d => d)
            .ToArray();

        return JsonSerializer.Serialize(cleaned);
    }

    /// <summary>
    /// 反序列化 <see cref="WeekDays"/>。
    ///
    /// 【为什么吞异常】这份 JSON 的源头是设备报文而不是平台自身；
    /// 一条脏数据不该让「查定时任务」整个接口 500，解析不了就当成空数组（仅一次）。
    /// </summary>
    /// <param name="weekDays">落库串，可为 null。</param>
    /// <returns>星期数组；无值或格式非法时返回空数组。</returns>
    public static int[] ParseWeekDays(string? weekDays)
    {
        if (string.IsNullOrWhiteSpace(weekDays))
        {
            return Array.Empty<int>();
        }

        try
        {
            return JsonSerializer.Deserialize<int[]>(weekDays) ?? Array.Empty<int>();
        }
        catch (JsonException)
        {
            return Array.Empty<int>();
        }
    }
}

/// <summary>
/// 一条<b>普通</b>定时任务的传输视图（协议 <c>timeTasks[]</c> 的单项）。
///
/// 服务层用它在「控制器请求 / 设备报文 / 数据库行」三者间搬运数据，
/// 避免服务层反向依赖 <c>DTOs.Requests</c>（与 <see cref="AnShengDelayTaskItem"/> 同构）。
/// </summary>
public sealed class AnShengTimeTaskItem
{
    /// <summary>设备分配的任务 id；新建任务时为 null（由设备生成）。</summary>
    public string? Id { get; init; }

    /// <summary>是否启用。</summary>
    public bool Enable { get; init; }

    /// <summary>每周生效的星期几（1-7）；空数组表示仅一次。</summary>
    public IReadOnlyList<int> WeekDays { get; init; } = Array.Empty<int>();

    /// <summary>动作小时（0-23）。</summary>
    public int Hour { get; init; }

    /// <summary>动作分钟（0-59）。</summary>
    public int Minute { get; init; }

    /// <summary>动作：<c>on</c> / <c>off</c> / <c>toggle</c>。</summary>
    public string Action { get; init; } = "on";

    /// <summary>任务触发时是否上报 <c>timeEvent</c>（固件 v5.0.1+）。</summary>
    public bool UploadEnable { get; init; }
}

/// <summary>
/// 一条<b>循环</b>定时任务的传输视图（协议 <c>loopTimeTasks[]</c> 的单项）。
/// </summary>
public sealed class AnShengLoopTimeTaskItem
{
    /// <summary>设备分配的任务 id；新建任务时为 null。</summary>
    public string? Id { get; init; }

    /// <summary>是否启用。</summary>
    public bool Enable { get; init; }

    /// <summary>每周生效的星期几（1-7）；空数组表示仅一次。</summary>
    public IReadOnlyList<int> WeekDays { get; init; } = Array.Empty<int>();

    /// <summary>每天循环开始的小时。</summary>
    public int SHour { get; init; }

    /// <summary>每天循环开始的分钟。</summary>
    public int SMinute { get; init; }

    /// <summary>每天循环结束的小时。</summary>
    public int EHour { get; init; }

    /// <summary>每天循环结束的分钟。</summary>
    public int EMinute { get; init; }

    /// <summary>循环中打开的分钟数。</summary>
    public int OnMins { get; init; }

    /// <summary>循环中关闭的分钟数。</summary>
    public int OffMins { get; init; }
}

/// <summary>
/// 单个插槽的完整定时任务集合（= 协议 <c>tasks[]</c> 的单项 + 插槽号）。
///
/// <c>setTimeTasks</c> 的整表覆盖、<c>getTimeTasks</c> 的整表回读都以它为单位搬运。
/// </summary>
public sealed class AnShengSlotTimeTaskSet
{
    /// <summary>插槽编号，从 1 开始。整表回读时由数组下标 +1 推导（§7-R9）。</summary>
    public int SlotNum { get; init; }

    /// <summary>普通定时任务列表，按设备数组顺序。</summary>
    public IReadOnlyList<AnShengTimeTaskItem> TimeTasks { get; init; } = Array.Empty<AnShengTimeTaskItem>();

    /// <summary>循环定时任务列表，按设备数组顺序。</summary>
    public IReadOnlyList<AnShengLoopTimeTaskItem> LoopTimeTasks { get; init; } = Array.Empty<AnShengLoopTimeTaskItem>();
}

/// <summary>
/// <c>timeEvent</c> 报文里 <c>task</c> 对象的解析结果。
///
/// 【为什么两类字段合并在一个类里】协议文档只给出了「普通定时」形态的 <c>task</c> 说明，
/// 但没有任何一句话排除循环定时任务触发上报的可能。用一个带 <see cref="Kind"/> 判别位的载体，
/// 可以在字段形态确实是循环任务时也正确落到镜像上，而不是把 <c>onMins</c> 丢掉、
/// 再把一条循环任务错写成 <c>hour=0 minute=0</c> 的普通任务。
/// </summary>
public sealed class AnShengTimeEventTask
{
    /// <summary>由字段形态推断出的任务类型。</summary>
    public AnShengTimeTaskKind Kind { get; init; } = AnShengTimeTaskKind.Normal;

    /// <summary>设备分配的任务 id；缺失时为 null。</summary>
    public string? Id { get; init; }

    /// <summary>是否启用。一次性任务执行完后设备会置 false，本字段是权威值。</summary>
    public bool Enable { get; init; }

    /// <summary>每周生效的星期几（1-7）。</summary>
    public IReadOnlyList<int> WeekDays { get; init; } = Array.Empty<int>();

    /// <summary>【普通定时】动作小时。</summary>
    public int Hour { get; init; }

    /// <summary>【普通定时】动作分钟。</summary>
    public int Minute { get; init; }

    /// <summary>【普通定时】动作：<c>on</c> / <c>off</c> / <c>toggle</c>。</summary>
    public string Action { get; init; } = string.Empty;

    /// <summary>【普通定时】是否上报。</summary>
    public bool UploadEnable { get; init; }

    /// <summary>【循环定时】开始小时。</summary>
    public int SHour { get; init; }

    /// <summary>【循环定时】开始分钟。</summary>
    public int SMinute { get; init; }

    /// <summary>【循环定时】结束小时。</summary>
    public int EHour { get; init; }

    /// <summary>【循环定时】结束分钟。</summary>
    public int EMinute { get; init; }

    /// <summary>【循环定时】打开分钟数。</summary>
    public int OnMins { get; init; }

    /// <summary>【循环定时】关闭分钟数。</summary>
    public int OffMins { get; init; }
}

/// <summary>
/// 安圣定时任务相关报文的 JSON 解析工具（T10）。
///
/// 集中放这里而非散在 Router / Handler 里，是因为三类消费方（Router 写后回读、
/// TimeEventHandler 事件解析、未来测试）都要把设备报文映射成
/// <see cref="AnShengTimeTaskItem"/> / <see cref="AnShengLoopTimeTaskItem"/> /
/// <see cref="AnShengTimeEventTask"/>，规则必须只有一份。
///
/// 【健壮性】缺字段回落默认值、脏值（weekDays 越界 / 类型不符）直接丢弃，绝不抛异常——
/// 设备回读的数据不受平台校验保护，一条脏数据不该让「写后回读」整条链路 500。
/// </summary>
internal static class AnShengTimeTaskParsing
{
    /// <summary>从 <see cref="JsonElement"/> 取 int，缺字段或类型不符返回 0。</summary>
    public static int TryGetInt(JsonElement el, string name)
    {
        if (el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var v))
        {
            return v;
        }

        return 0;
    }

    /// <summary>从 <see cref="JsonElement"/> 取 bool，兼容 JSON 的 true/false 与 1/0。</summary>
    public static bool TryGetBool(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p))
        {
            return false;
        }

        if (p.ValueKind == JsonValueKind.True) return true;
        if (p.ValueKind == JsonValueKind.False) return false;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var n)) return n != 0;
        return false;
    }

    /// <summary>从 <see cref="JsonElement"/> 取字符串，缺字段或类型不符返回 null。</summary>
    public static string? TryGetString(JsonElement el, string name)
    {
        if (el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String)
        {
            return p.GetString();
        }

        return null;
    }

    /// <summary>
    /// 解析 <c>weekDays</c> 数组（JSON 数组，元素 1-7），去重升序、丢弃越界项。
    /// 缺字段或非数组返回空数组。
    /// </summary>
    public static IReadOnlyList<int> ParseWeekDaysArray(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p) || p.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<int>();
        }

        var list = new List<int>(7);
        foreach (var item in p.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var d)
                && d >= 1 && d <= 7 && !list.Contains(d))
            {
                list.Add(d);
            }
        }

        list.Sort();
        return list;
    }

    /// <summary>把 <c>timeTasks[]</c> 单项解析为 <see cref="AnShengTimeTaskItem"/>。</summary>
    public static AnShengTimeTaskItem ParseTimeTaskItem(JsonElement el)
        => new()
        {
            Id = TryGetString(el, "id"),
            Enable = TryGetBool(el, "enable"),
            WeekDays = ParseWeekDaysArray(el, "weekDays"),
            Hour = TryGetInt(el, "hour"),
            Minute = TryGetInt(el, "minute"),
            Action = TryGetString(el, "action") ?? "on",
            UploadEnable = TryGetBool(el, "uploadEnable")
        };

    /// <summary>把 <c>loopTimeTasks[]</c> 单项解析为 <see cref="AnShengLoopTimeTaskItem"/>。</summary>
    public static AnShengLoopTimeTaskItem ParseLoopTimeTaskItem(JsonElement el)
        => new()
        {
            Id = TryGetString(el, "id"),
            Enable = TryGetBool(el, "enable"),
            WeekDays = ParseWeekDaysArray(el, "weekDays"),
            SHour = TryGetInt(el, "sHour"),
            SMinute = TryGetInt(el, "sMinute"),
            EHour = TryGetInt(el, "eHour"),
            EMinute = TryGetInt(el, "eMinute"),
            OnMins = TryGetInt(el, "onMins"),
            OffMins = TryGetInt(el, "offMins")
        };

    /// <summary>
    /// 解析 <c>timeEvent</c> 报文的 <c>task</c> 对象为 <see cref="AnShengTimeEventTask"/>。
    ///
    /// <see cref="AnShengTimeEventTask.Kind"/> 由字段形态推断：携带循环专属字段
    /// （<c>onMins</c> / <c>offMins</c> / <c>sHour</c> / <c>eHour</c>）即判为循环任务，否则普通任务。
    /// </summary>
    public static AnShengTimeEventTask ParseTimeEventTask(JsonElement el)
    {
        var isLoop = el.TryGetProperty("onMins", out _)
            || el.TryGetProperty("offMins", out _)
            || el.TryGetProperty("sHour", out _)
            || el.TryGetProperty("eHour", out _);

        return new AnShengTimeEventTask
        {
            Kind = isLoop ? AnShengTimeTaskKind.Loop : AnShengTimeTaskKind.Normal,
            Id = TryGetString(el, "id"),
            Enable = TryGetBool(el, "enable"),
            WeekDays = ParseWeekDaysArray(el, "weekDays"),
            Hour = TryGetInt(el, "hour"),
            Minute = TryGetInt(el, "minute"),
            Action = TryGetString(el, "action") ?? string.Empty,
            UploadEnable = TryGetBool(el, "uploadEnable"),
            SHour = TryGetInt(el, "sHour"),
            SMinute = TryGetInt(el, "sMinute"),
            EHour = TryGetInt(el, "eHour"),
            EMinute = TryGetInt(el, "eMinute"),
            OnMins = TryGetInt(el, "onMins"),
            OffMins = TryGetInt(el, "offMins")
        };
    }

    /// <summary>
    /// 把 <c>getTimeTasks</c> 应答的 <c>tasks[]</c> 解析为插槽集合列表。
    /// 数组下标 i 对应插槽 i+1（§7-R9，<c>tasks[]</c> 不含 slotNum）。
    /// </summary>
    public static List<AnShengSlotTimeTaskSet> ParseTimeTaskSets(JsonElement root)
    {
        var sets = new List<AnShengSlotTimeTaskSet>();
        if (!root.TryGetProperty("tasks", out var tasksEl) || tasksEl.ValueKind != JsonValueKind.Array)
        {
            return sets;
        }

        for (var i = 0; i < tasksEl.GetArrayLength(); i++)
        {
            var el = tasksEl[i];
            if (el.ValueKind != JsonValueKind.Object) continue;

            sets.Add(new AnShengSlotTimeTaskSet
            {
                SlotNum = i + 1,
                TimeTasks = ParseTaskArray(el, "timeTasks", ParseTimeTaskItem),
                LoopTimeTasks = ParseTaskArray(el, "loopTimeTasks", ParseLoopTimeTaskItem)
            });
        }

        return sets;
    }

    /// <summary>
    /// 把 <c>getSlotTimeTasks</c> 应答（顶层直接带 <c>timeTasks[]</c> / <c>loopTimeTasks[]</c>）解析为单插槽集合。
    /// </summary>
    public static AnShengSlotTimeTaskSet? ParseSlotTimeTaskSet(JsonElement root, int slotNum)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;

        return new AnShengSlotTimeTaskSet
        {
            SlotNum = slotNum,
            TimeTasks = ParseTaskArray(root, "timeTasks", ParseTimeTaskItem),
            LoopTimeTasks = ParseTaskArray(root, "loopTimeTasks", ParseLoopTimeTaskItem)
        };
    }

    /// <summary>解析某个任务数组字段，容错空字段与非数组。</summary>
    private static IReadOnlyList<T> ParseTaskArray<T>(
        JsonElement el, string name, Func<JsonElement, T> itemParser)
    {
        if (!el.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<T>();
        }

        var list = new List<T>(arr.GetArrayLength());
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object)
            {
                list.Add(itemParser(item));
            }
        }

        return list;
    }
}
