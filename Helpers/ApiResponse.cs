namespace IoTPlatform.Helpers;

/// <summary>
/// API统一响应格式
/// </summary>
/// <typeparam name="T">数据类型</typeparam>
public class ApiResponse<T>
{
    /// <summary>
    /// 状态码：200成功，400客户端错误，401未授权，403禁止访问，404未找到，500服务器错误
    /// </summary>
    public int Code { get; set; }

    /// <summary>
    /// 响应消息
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// 响应数据
    /// </summary>
    public T? Data { get; set; }

    /// <summary>
    /// 时间戳
    /// </summary>
    public long Timestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    /// <summary>
    /// 成功响应
    /// </summary>
    public static ApiResponse<T> Success(T data, string message = "操作成功")
    {
        return new ApiResponse<T>
        {
            Code = 200,
            Message = message,
            Data = data
        };
    }

    /// <summary>
    /// 成功响应（无数据）
    /// </summary>
    public static ApiResponse<T> Success(string message = "操作成功")
    {
        return new ApiResponse<T>
        {
            Code = 200,
            Message = message
        };
    }

    /// <summary>
    /// 失败响应
    /// </summary>
    public static ApiResponse<T> Fail(int code, string message)
    {
        return new ApiResponse<T>
        {
            Code = code,
            Message = message
        };
    }

    /// <summary>
    /// 客户端错误（400）
    /// </summary>
    public static ApiResponse<T> BadRequest(string message = "请求参数错误")
    {
        return Fail(400, message);
    }

    /// <summary>
    /// 未授权（401）
    /// </summary>
    public static ApiResponse<T> Unauthorized(string message = "未授权访问")
    {
        return Fail(401, message);
    }

    /// <summary>
    /// 禁止访问（403）
    /// </summary>
    public static ApiResponse<T> Forbidden(string message = "禁止访问")
    {
        return Fail(403, message);
    }

    /// <summary>
    /// 未找到（404）
    /// </summary>
    public static ApiResponse<T> NotFound(string message = "资源不存在")
    {
        return Fail(404, message);
    }

    /// <summary>
    /// 服务器错误（500）
    /// </summary>
    public static ApiResponse<T> Error(string message = "服务器内部错误")
    {
        return Fail(500, message);
    }
}

/// <summary>
/// 无数据的API响应
/// </summary>
public class ApiResponse : ApiResponse<object>
{
    /// <summary>
    /// 成功响应
    /// </summary>
    public static ApiResponse Success(string message = "操作成功")
    {
        return new ApiResponse
        {
            Code = 200,
            Message = message
        };
    }

    /// <summary>
    /// 成功响应（带数据）
    /// </summary>
    public static ApiResponse Success(object data, string message = "操作成功")
    {
        return new ApiResponse
        {
            Code = 200,
            Message = message,
            Data = data
        };
    }
}
