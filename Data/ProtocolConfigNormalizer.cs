using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace IoTPlatform.Data;

/// <summary>
/// 存量协议连接配置（<see cref="IoTPlatform.Models.ProtocolConfig.Config"/> 字段里的那段 JSON 文本）归一化器。
/// </summary>
/// <remarks>
/// 【为什么需要它】
/// 旧版协议管理页的「通用连接配置」表单用裸键写入配置，产出的是<b>小写键 + 字符串值</b>，
/// 例如 <c>{"host":"1.2.3.4","port":"502"}</c>。适配器侧要把它反序列化成 PascalCase 的
/// <c>XxxProtocolOptions</c>（<c>Host</c>/<c>Port</c>）。
///
/// 后端已经给各适配器的 <c>Deserialize</c> 注入了大小写不敏感选项
/// （<c>ProtocolJsonOptions.CaseInsensitive</c>），这解决了<b>键名大小写</b>问题，
/// 但解决不了<b>值类型</b>问题：<c>"port":"502"</c> 是 JSON 字符串，绑定到 <c>int Port</c> 时
/// System.Text.Json 会直接抛 <see cref="JsonException"/>（不是静默回落默认值，是硬失败）。
/// 更糟的是 <c>{"port":"502","Port":5502}</c> 这类新旧键并存的行同样会抛异常。
///
/// 因此存量数据必须归一：<c>{"port":"502"}</c> → <c>{"Port":502}</c>（键 PascalCase + 值转数字）。
///
/// 【归一化规则】
/// 0. <b>JSON null 一律删键</b>（适用于<b>全部</b>属性，不只数值属性）。
///    依据：System.Text.Json 遇到显式 <c>null</c> 会<b>覆盖</b>属性初始化器给的默认值 ——
///    <c>public string Host { get; set; } = "localhost"</c> 碰上 <c>{"Host":null}</c> 绑定结果是 <c>null</c>，
///    而不是 <c>"localhost"</c>；数值属性上更是直接抛 <see cref="JsonException"/>。
///    也就是说保留 null 会产出「比默认值更坏」的结果，删掉才能让初始化器默认值生效。
///    该行为已由后端测试 <c>ExplicitJsonNull_OverridesPropertyInitializerDefault_NotIgnored</c> 实测证明。
/// 1. 键名：按协议类型解析目标属性名。先按<b>大小写不敏感</b>匹配该协议 DTO 的规范属性名
///    （<c>port</c>/<c>PORT</c> → <c>Port</c>），再查<b>历史别名表</b>（<c>serialPort</c> → <c>PortName</c>、
///    <c>endpoint</c> → <c>EndpointUrl</c>）。命中即重命名，旧键删除。
/// 2. 未识别的键<b>原样保留</b>（不猜、不丢），保证不破坏任何自定义扩展字段。
/// 3. 冲突：若规范键（精确 PascalCase）<b>存在且值非 null</b>，则丢弃与其重名的旧键，<b>规范键的值优先</b>
///    （规范键是修复后的前端写入的，视为更新的值）。
///    但<b>值为 null 的规范键视同不存在</b>：此时同名历史小写键的真实值会被 <b>rescue</b> 上来，
///    <c>{"host":"1.2.3.4","Host":null}</c> → <c>{"Host":"1.2.3.4"}</c>。
///    否则我们手上明明有真实值却把它丢掉、换成一个连默认值都不如的 null，是净损失。
///    同名旧键出现多次时，<b>先出现者胜</b>。
/// 4. 值：<see cref="NumericProperties"/> 中的数值属性做类型矫正 —— 字符串且是合法整数则转成 JSON 数字；
///    <b>空串</b>则<b>删除该键</b>（让 DTO 的默认值生效，否则同样会抛 <see cref="JsonException"/>）；
///    非整数格式的字符串<b>保持原值</b>（不猜测，留给人工处理）。
///    注意空串删键<b>只作用于数值属性</b> —— 空串对 <c>string Host</c> 是合法值，不能一起删。
/// 5. 幂等：已经是 PascalCase + 正确类型的输入，再归一化一次结果等价。
/// 6. 防御：<c>null</c> / 空白 / 非法 JSON / 非 JSON 对象 / 未知协议类型 → <b>原样返回</b>，绝不破坏数据。
///
/// 【与前端的一致性约束】
/// <c>Web/src/app/pages/ProtocolManagementPage.tsx</c> 的 <c>normalizeLegacyConfigKeys</c> 是本类的前端同构实现，
/// <b>两边规则必须逐条一致</b>（尤其是规则 0 与规则 3 的 null 语义）。改这里就要同步改那里，反之亦然。
/// <c>scripts/normalize_protocol_config_keys.sql</c> 是 SQL 侧同构实现，同样需要同步。
///
/// 【已知取舍】
/// MQTT 的别名表里保留了 <c>endpoint</c> → <c>EndpointUrl</c>（按迁移任务要求）。
/// 注意 <c>MqttProtocolOptions</c> / <c>AnShengMqttProtocolOptions</c> <b>并没有</b> <c>EndpointUrl</c> 属性，
/// 该键归一后仍是 DTO 的未知成员（会被 System.Text.Json 忽略，不会抛异常）。
/// 这么做只是让存量键名形态统一，避免同一份数据里 PascalCase 与小写键混杂。
/// </remarks>
public static class ProtocolConfigNormalizer
{
    /// <summary>
    /// 回写 JSON 时使用的选项。不缩进（与入库文本风格一致、体积小），
    /// 使用宽松编码器避免中文/符号被转义成 <c>\uXXXX</c>，保证运维直接看库里的值仍可读。
    /// </summary>
    private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// 需要「字符串 → 数字」类型矫正的属性名集合（大小写不敏感）。
    /// 覆盖各协议 DTO 中的整型属性；与 <c>scripts/normalize_protocol_config_keys.sql</c> 中的集合保持一致。
    /// </summary>
    private static readonly HashSet<string> NumericProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Port",
        "BaudRate",
        "TimeoutMs",
        "PollIntervalMs",
        "QosLevel",
        "DataBits",
        "TimeoutSeconds",
        "KeepAliveSeconds",
        "CommandMinIntervalMs",
    };

    /// <summary>各协议的「规范属性名 + 历史别名」表，键为归一化后的协议标识。</summary>
    private static readonly IReadOnlyDictionary<string, ProtocolSchema> Schemas = BuildSchemas();

    /// <summary>
    /// 把存量小写/字符串型 config 归一为 PascalCase + 正确类型。
    /// </summary>
    /// <param name="type">协议类型（大小写不敏感，允许 <c>modbus_tcp</c> / <c>MODBUS-TCP</c> / <c>ModbusTcp</c> 等写法）。</param>
    /// <param name="configJson">存量配置 JSON 文本，可为 <c>null</c>。</param>
    /// <returns>
    /// 归一化后的 JSON 文本；当输入为 <c>null</c>、空白、非法 JSON、非 JSON 对象，
    /// 或协议类型无法识别时，<b>原样返回输入</b>。
    /// </returns>
    public static string? Normalize(string? type, string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
        {
            // null / "" / 纯空白：原样返回，不制造 "{}" 这种凭空产生的数据。
            return configJson;
        }

        var schema = ResolveSchema(type);
        if (schema is null)
        {
            // 未知协议类型（http / tcp / bacnet / null 等）：不认识就不动，把爆炸半径压到最小。
            return configJson;
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(configJson);
        }
        catch (JsonException)
        {
            // 库里存了非法 JSON（历史脏数据）：原样返回，交给人工处理，绝不吞掉原文。
            return configJson;
        }

        if (root is not JsonObject source)
        {
            // JSON 数组 / 标量 / 字面量 null：不是配置对象，原样返回。
            return configJson;
        }

        var normalized = NormalizeObject(source, schema);
        return normalized.ToJsonString(WriteOptions);
    }

    /// <summary>
    /// 对单个 JSON 对象执行键重映射 + 数值矫正，返回新对象（保持原始键顺序）。
    /// </summary>
    /// <param name="source">原始 JSON 对象。</param>
    /// <param name="schema">协议对应的属性名表。</param>
    /// <returns>归一化后的新 <see cref="JsonObject"/>。</returns>
    private static JsonObject NormalizeObject(JsonObject source, ProtocolSchema schema)
    {
        // 预扫描：哪些「精确 PascalCase 规范键」已经存在【且值非 null】。它们的值优先级最高。
        // 只有「值非 null」的规范键才算数据权威。值为 null 的规范键【视同不存在】，
        // 好让同名的历史小写键把真实值 rescue 回来（{"host":"x","Host":null} → {"Host":"x"}）。
        // 若不加这个判断，就会出现「手上有真实值却丢掉、换成连默认值都不如的 null」的净损失。
        var exactCanonicalKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in source)
        {
            if (schema.CanonicalNames.Contains(property.Key)
                && property.Value is not null
                && property.Value.GetValueKind() != JsonValueKind.Null)
            {
                exactCanonicalKeys.Add(property.Key);
            }
        }

        var result = new JsonObject();

        foreach (var property in source)
        {
            var originalKey = property.Key;
            var targetKey = ResolveTargetKey(originalKey, schema);
            var isRenamed = !string.Equals(originalKey, targetKey, StringComparison.Ordinal);

            // 规则 3：旧键要改名成一个「已存在且值非 null」的规范键 → 丢弃旧键，保留规范键的值。
            // 反之若那个规范键的值是 null，它不在 exactCanonicalKeys 里，旧键会继续往下走，
            // 把真实值 rescue 成规范键的值；随后原本那个 null 规范键会被规则 0 删掉。
            if (isRenamed && exactCanonicalKeys.Contains(targetKey))
            {
                continue;
            }

            // 同一目标键被多个旧键命中（如 serialport / serialPort 并存）→ 先出现者胜，保证确定性。
            if (result.ContainsKey(targetKey))
            {
                continue;
            }

            var value = property.Value;

            // 规则 0：任何目标键的 JSON null 一律删除，让 DTO 的属性初始化器默认值生效。
            // 依据：System.Text.Json 遇到显式 null 会覆盖属性初始化器（`= "localhost"` → null），
            // 产出比默认值更坏的结果；数值属性上更是直接抛 JsonException。
            // 已由 ExplicitJsonNull_OverridesPropertyInitializerDefault_NotIgnored 实测证明。
            if (value is null || value.GetValueKind() == JsonValueKind.Null)
            {
                continue;
            }

            if (NumericProperties.Contains(targetKey))
            {
                // 注意：此处不再判断 null —— 规则 0 已在上面统一拦掉，这里只处理空串与字符串整数。
                // 空串删键刻意【只】留在数值分支：空串对 string Host 是合法值，不能跟着一起删。
                if (value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var rawText))
                {
                    var trimmed = rawText.Trim();
                    if (trimmed.Length == 0)
                    {
                        // {"Port":""} 绑定到 int 会抛 JsonException → 删除该键，让 DTO 默认值生效。
                        continue;
                    }

                    if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric))
                    {
                        result[targetKey] = JsonValue.Create(numeric);
                        continue;
                    }

                    // 非整数格式（如 "COM3"、"5000.5"）：不猜测，保持原值落回下面的通用分支。
                }
            }

            // DeepClone：JsonNode 不允许一个节点同时挂在两棵树上，必须克隆后再挂到新对象。
            // value 在规则 0 处已保证非 null，这里无需再做空判断。
            result[targetKey] = value.DeepClone();
        }

        return result;
    }

    /// <summary>
    /// 解析某个原始键应该被归一成什么键名。
    /// </summary>
    /// <param name="originalKey">原始 JSON 键名。</param>
    /// <param name="schema">协议对应的属性名表。</param>
    /// <returns>规范键名；未识别时返回原始键名。</returns>
    private static string ResolveTargetKey(string originalKey, ProtocolSchema schema)
    {
        var lower = originalKey.ToLowerInvariant();

        // 1) 大小写不敏感命中 DTO 规范属性名：port / PORT / Port → Port
        if (schema.CanonicalByLowerName.TryGetValue(lower, out var canonical))
        {
            return canonical;
        }

        // 2) 历史别名：serialPort → PortName、endpoint → EndpointUrl
        if (schema.LegacyAliases.TryGetValue(lower, out var aliased))
        {
            return aliased;
        }

        // 3) 未知键：原样保留
        return originalKey;
    }

    /// <summary>
    /// 把外部传入的协议类型字符串归一成内部协议标识。
    /// </summary>
    /// <param name="type">协议类型，允许大小写混写与 <c>_</c>/<c>-</c>/空格分隔。</param>
    /// <returns>匹配到的协议属性名表；无法识别时返回 <c>null</c>。</returns>
    private static ProtocolSchema? ResolveSchema(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return null;
        }

        var builder = new StringBuilder(type.Length);
        foreach (var ch in type)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
        }

        var key = builder.ToString();
        return Schemas.TryGetValue(key, out var schema) ? schema : null;
    }

    /// <summary>
    /// 构建各协议的属性名表。规范属性名严格取自
    /// <c>Infrastructure/Protocol/Adapters/</c> 下的 <c>XxxOptions</c> 类定义。
    /// </summary>
    /// <returns>协议标识 → 属性名表。</returns>
    private static IReadOnlyDictionary<string, ProtocolSchema> BuildSchemas()
    {
        // ── MQTT（MqttProtocolOptions）───────────────────────────────
        var mqtt = new ProtocolSchema(
            canonicalNames: new[]
            {
                "Host", "Port", "Username", "Password", "ClientIdPrefix", "CleanSession",
                "TimeoutSeconds", "KeepAliveSeconds", "SubscribeTopics", "CommandTopicTemplate",
                "CommandResponseTopic", "ReadTopicTemplate", "QosLevel",
            },
            legacyAliases: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["host"] = "Host",
                ["port"] = "Port",
                ["endpoint"] = "EndpointUrl",
                ["username"] = "Username",
                ["password"] = "Password",
                ["clientidprefix"] = "ClientIdPrefix",
                ["cleansession"] = "CleanSession",
                ["qoslevel"] = "QosLevel",
            });

        // ── 安圣 MQTT（AnShengMqttProtocolOptions）──────────────────
        // 连接类字段与通用 MQTT 同构，沿用同一套别名表。
        var anShengMqtt = new ProtocolSchema(
            canonicalNames: new[]
            {
                "Host", "Port", "Username", "Password", "ClientIdPrefix", "CleanSession",
                "TimeoutSeconds", "KeepAliveSeconds", "QosLevel", "PublishTopicPattern",
                "WillTopicPattern", "SubscribeTopicTemplate", "CommandMinIntervalMs",
                "AutoConfigureAutoReport", "DefaultAutoReport",
            },
            legacyAliases: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["host"] = "Host",
                ["port"] = "Port",
                ["endpoint"] = "EndpointUrl",
                ["username"] = "Username",
                ["password"] = "Password",
                ["clientidprefix"] = "ClientIdPrefix",
                ["cleansession"] = "CleanSession",
                ["qoslevel"] = "QosLevel",
            });

        // ── Modbus TCP（ModbusTcpOptions）───────────────────────────
        var modbusTcp = new ProtocolSchema(
            canonicalNames: new[] { "Host", "Port", "TimeoutMs", "PollIntervalMs", "Devices", "AppCode" },
            legacyAliases: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["host"] = "Host",
                ["port"] = "Port",
            });

        // ── Modbus RTU（ModbusRtuOptions）───────────────────────────
        var modbusRtu = new ProtocolSchema(
            canonicalNames: new[]
            {
                "PortName", "BaudRate", "DataBits", "StopBits", "Parity",
                "TimeoutMs", "PollIntervalMs", "Devices", "AppCode",
            },
            legacyAliases: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["serialport"] = "PortName",
                ["baudrate"] = "BaudRate",
            });

        // ── OPC UA（OpcUaOptions）───────────────────────────────────
        var opcUa = new ProtocolSchema(
            canonicalNames: new[]
            {
                "EndpointUrl", "TimeoutMs", "PollIntervalMs", "UsePollingMode", "SecurityPolicy",
                "SecurityMode", "CertificatePath", "Username", "Password", "Nodes", "AppCode",
            },
            legacyAliases: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["endpoint"] = "EndpointUrl",
            });

        return new Dictionary<string, ProtocolSchema>(StringComparer.Ordinal)
        {
            ["mqtt"] = mqtt,
            ["anshengmqtt"] = anShengMqtt,
            ["modbustcp"] = modbusTcp,
            // 历史上只写 "modbus" 的行按 TCP 处理：别名表只涉及 host/port，
            // 若该行其实是 RTU，其 serialPort 等键会作为「未知键」被原样保留，不会被改坏。
            ["modbus"] = modbusTcp,
            ["modbusrtu"] = modbusRtu,
            ["opcua"] = opcUa,
        };
    }

    /// <summary>
    /// 单个协议的属性名表：DTO 规范属性名 + 历史别名映射。
    /// </summary>
    private sealed class ProtocolSchema
    {
        /// <summary>
        /// 构造属性名表。
        /// </summary>
        /// <param name="canonicalNames">DTO 上的规范（PascalCase）属性名。</param>
        /// <param name="legacyAliases">历史别名（键必须为全小写）→ 规范属性名。</param>
        public ProtocolSchema(IReadOnlyList<string> canonicalNames, IReadOnlyDictionary<string, string> legacyAliases)
        {
            var exact = new HashSet<string>(StringComparer.Ordinal);
            var byLower = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var name in canonicalNames)
            {
                exact.Add(name);
                byLower[name.ToLowerInvariant()] = name;
            }

            CanonicalNames = exact;
            CanonicalByLowerName = byLower;
            LegacyAliases = legacyAliases;
        }

        /// <summary>规范属性名集合（精确大小写）。</summary>
        public IReadOnlySet<string> CanonicalNames { get; }

        /// <summary>小写属性名 → 规范属性名。</summary>
        public IReadOnlyDictionary<string, string> CanonicalByLowerName { get; }

        /// <summary>小写历史别名 → 规范属性名。</summary>
        public IReadOnlyDictionary<string, string> LegacyAliases { get; }
    }
}
