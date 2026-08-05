using System.Text.Json;

namespace IoTPlatform.Infrastructure.Protocol.Adapters;

/// <summary>
/// 协议适配器共享的 JSON 反序列化选项。
/// </summary>
/// <remarks>
/// 协议连接配置在持久化时其键名原样透传（无命名策略），而读取后反序列化为 PascalCase 的
/// <c>XxxProtocolOptions</c> DTO。System.Text.Json 默认大小写敏感，会导致 UI 写入的小写键
/// （<c>host</c>/<c>port</c>）无法绑定到 <c>Host</c>/<c>Port</c>，适配器静默回落默认值。
/// 这里统一使用大小写不敏感反序列化：小写键与现有 PascalCase 键均可正确绑定，存量数据无需迁移。
/// </remarks>
internal static class ProtocolJsonOptions
{
    /// <summary>
    /// 大小写不敏感的 <see cref="JsonSerializerOptions"/> 单例。
    /// 仅用于协议连接配置（<c>XxxProtocolOptions</c>）的反序列化，禁止用于设备上行消息载荷。
    /// </summary>
    public static readonly JsonSerializerOptions CaseInsensitive = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
    };
}
