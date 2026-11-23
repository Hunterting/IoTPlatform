namespace IoTPlatform.Services.Guards;

/// <summary>
/// 统一资源不存在异常（授权缺口修复公共基础，设计 §3.2 B）。
///
/// <para>归属点查拿不到实体时抛出，避免把"不存在"误判为"越权"或静默放行；
/// 与 <see cref="ForbiddenAccessException"/> 严格区分，便于 Controller 映射不同业务码。</para>
/// </summary>
public sealed class NotFoundException : Exception
{
    /// <summary>以默认消息构造未找到异常。</summary>
    public NotFoundException()
        : base("资源不存在")
    {
    }

    /// <summary>以自定义消息构造未找到异常。</summary>
    /// <param name="message">面向调用方的说明。</param>
    public NotFoundException(string message)
        : base(message)
    {
    }

    /// <summary>以自定义消息和内部异常构造未找到异常。</summary>
    /// <param name="message">面向调用方的说明。</param>
    /// <param name="innerException">内部异常。</param>
    public NotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
