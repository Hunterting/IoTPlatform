namespace IoTPlatform.Services.Guards;

/// <summary>
/// 统一越权异常（授权缺口修复公共基础，设计 §3.2 B）。
///
/// <para>Guard 在写库/MQTT 下发/物理文件删除<b>之前</b>抛出本异常（原则 G），
/// Controller 捕获后映射为 <c>ApiResponse.Forbidden()</c>（业务码 403，HTTP 恒 200）。</para>
/// </summary>
public sealed class ForbiddenAccessException : Exception
{
    /// <summary>以默认消息构造越权异常。</summary>
    public ForbiddenAccessException()
        : base("无权访问该资源")
    {
    }

    /// <summary>以自定义消息构造越权异常。</summary>
    /// <param name="message">面向调用方的说明。</param>
    public ForbiddenAccessException(string message)
        : base(message)
    {
    }

    /// <summary>以自定义消息和内部异常构造越权异常。</summary>
    /// <param name="message">面向调用方的说明。</param>
    /// <param name="innerException">内部异常。</param>
    public ForbiddenAccessException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
