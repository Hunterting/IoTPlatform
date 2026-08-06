using System.Text.Json;
using System.Text.Json.Serialization;

namespace IoTPlatform.Infrastructure.Protocol.Adapters;

/// <summary>
/// 协议适配器共享的 JSON 反序列化选项（单例）。
/// </summary>
/// <remarks>
/// 协议连接配置在持久化时其键名原样透传（无命名策略），而读取后反序列化为 PascalCase 的
/// <c>XxxProtocolOptions</c> DTO。这里统一使用大小写不敏感反序列化：小写键与现有 PascalCase 键均可
/// 正确绑定，存量数据无需迁移。在此基础上另追加了 <see cref="JsonNumberHandling.AllowReadingFromString"/>，
/// 以兜住存量「字符串型数字」配置（详见 <see cref="CaseInsensitive"/> 字段注释中的取舍说明）。
/// </remarks>
internal static class ProtocolJsonOptions
{
    /// <summary>
    /// 大小写不敏感、且允许从 JSON 字符串读取数字的 <see cref="JsonSerializerOptions"/> 单例。
    /// 仅用于协议连接配置（<c>XxxProtocolOptions</c>）的反序列化，
    /// <b>禁止</b>用于设备上行消息载荷（理由见下方「作用域」）。
    /// </summary>
    /// <remarks>
    /// <para><b>配置一：PropertyNameCaseInsensitive = true</b></para>
    /// <para>System.Text.Json 默认大小写敏感，会导致 UI 写入的小写键（<c>host</c>/<c>port</c>）无法绑定到
    /// <c>Host</c>/<c>Port</c>，适配器静默回落默认值。开启后小写键与存量 PascalCase 键同时可绑定。</para>
    ///
    /// <para><b>配置二：NumberHandling = AllowReadingFromString（本项取舍说明）</b></para>
    /// <para>
    /// <b>① 为什么加。</b>存量协议配置里有 <c>{"host":"1.2.3.4","port":"502"}</c> 这种旧版协议管理表单写的
    /// <b>小写键 + 字符串值</b>。配置一解决了键名大小写，但解决不了值类型——<c>"502"</c> 绑到 <c>int Port</c>
    /// 时 System.Text.Json 直接抛 <see cref="JsonException"/>，而且是<b>流式</b>的：撞上就抛，后面正确的键
    /// 根本没机会读。净效果是倒挂：修复前这些行是「静默回落 localhost:502」（连错目标但连得上），
    /// 修复后变成「连接失败」，而且<b>不需要用户编辑就触发</b>（键就躺在 DB 里）。这个选项把这类存量
    /// 字符串数值救回来，使其正确绑定为 502。
    /// </para>
    /// <para>
    /// <b>② 代价（有意接受）。</b>该配置使数值属性的类型契约变宽松：前端如果哪天又把数字字段写成字符串值，
    /// 不会被立刻发现（静默接受）。这是<b>有意接受</b>的代价，换的是存量数据不炸。相比「连不上」，
    /// 「悄咪咪接受了一次字符串数字」是更可接受的故障形态。
    /// </para>
    /// <para>
    /// <b>③ 不影响的范围。</b><see cref="JsonNumberHandling.AllowReadingFromString"/> 只作用于<b>读</b>、
    /// 只作用于<b>数值类型</b>。布尔、字符串、枚举的绑定行为不变（已实测：字符串布尔 <c>"true"</c> 仍抛异常）。
    /// 序列化输出不受影响。
    /// </para>
    /// <para>
    /// <b>④ 与方案 A 的关系（兜底，非替代；且兜底有边界）。</b>本选项是<b>运行时兜底</b>，不是数据清洗的替代。
    /// <c>scripts/normalize_protocol_config_keys.sql</c>（方案 A）才是把数据真正洗干净的手段，两者不互斥，都要保留。
    /// <b>必须强调的边界与根因：</b>B 只覆盖「<b>无空白的合法整数字符串</b>」，且 B 与归一化器是
    /// <b>两套不同的解析器</b>，并非共用同一套规则。实测（.NET 8）下，<c>"502"</c> 能被救回，<c>"+502"</c>、<c>"0502"</c>
    /// 这类带前导正负号/前导零的整数字符串 B 也接受——但这<b>不是</b>因为 B 按 <c>NumberStyles.Integer</c> 解析
    /// （恰恰相反：<c>NumberStyles.Integer</c> 含空白宽容，若 B 真用它，<c>" 502"</c> 就该被接受，而实测 <c>" 502"</c> 抛异常）。
    /// 真实情况是：<b>B 走 System.Text.Json 自己的 UTF-8 数字解析</b>（不容任何空白，但允许前导正负号与前导零）；
    /// 归一化器走 <c>long.TryParse(trimmed, NumberStyles.Integer, ...)</c>，<b>先 Trim() 再解析</b>。二者之所以在
    /// <c>"+502"</c>/<c>"0502"</c> 上<b>看似一致</b>，纯粹因为归一化器先 Trim 过——Trim 让 <c>NumberStyles.Integer</c>
    /// 的空白宽容变成无关项，剩下的「符号 + 前导零」子集才恰好重合。即「巧合性对齐」的准确含义是：
    /// <b>仅发生在「已去空白的整数子集」上，超出该子集立刻分叉</b>。因此 <b>带前后空白的数字串
    /// （<c>"  502  "</c>、<c>" 502"</c>、<c>"502 "</c>、<c>"\t502"</c>）与空串（<c>""</c>）B 兜不住，仍抛
    /// <see cref="JsonException"/></b>（<c>NumberStyles.Integer</c> 反而能接受它们）——而 C# 归一化器、前端
    /// <c>normalizeLegacyConfigKeys</c>、SQL 脚本三处都先 <c>trim()</c> 且把空串按「删键」处理（回落 DTO 默认值），故能救回。
    /// 因此这批带空白/空串的行当下即是「连接失败」状态，<b>只有跑方案 A 才能恢复连接</b>。绝不可因「已有 B」而把 SQL 缓一缓：
    /// B 救得了「干净字符串数字」，救不了「带空白/空串」——<b>二者规则不同，B ≈ A 是错误推论</b>。A 必须执行，不可省略。
    /// </para>
    /// <para>
    /// <b>⑤ 作用域（红线）。</b>本 options 只用于 5 个适配器的 <c>XxxOptions</c> 反序列化
    /// （Mqtt / AnShengMqtt / ModbusTcp / ModbusRtu / OpcUa），<b>不得</b>用于设备上行消息载荷的解析——
    /// 那里必须保持严格类型，把设备发来的脏数据暴露出来（设备载荷若被放松，会把真实故障吞成「看似正常」）。
    /// 这条边界是强制约束，任何人在此 options 上继续放宽类型都属于越界。
    /// </para>
    /// </remarks>
    public static readonly JsonSerializerOptions CaseInsensitive = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };
}
