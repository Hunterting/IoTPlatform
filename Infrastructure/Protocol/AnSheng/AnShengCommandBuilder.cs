using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace IoTPlatform.Infrastructure.Protocol.AnSheng;

/// <summary>
/// 安圣 MQTT 命令构建器
/// 生成符合安圣协议的 JSON 命令报文
/// 报文格式：{ method, imei, frameId, timestamp, param? }
/// </summary>
public class AnShengCommandBuilder
{
    private readonly ILogger<AnShengCommandBuilder>? _logger;

    public AnShengCommandBuilder(ILogger<AnShengCommandBuilder>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// 构建 setAutoReport 命令 — 配置设备定时上报间隔
    /// </summary>
    /// <param name="imei">设备 IMEI</param>
    /// <param name="getDevStatusSec">状态上报间隔（秒），默认 60</param>
    /// <param name="orderUpSec">订单进度上报间隔（秒），默认 300</param>
    /// <param name="rs485Sec">RS485 轮询间隔（秒），0=关闭</param>
    /// <param name="getDevStatusQ">额外查询参数</param>
    /// <returns>JSON 命令字符串 + FrameId</returns>
    public (string FrameId, string Payload) BuildSetAutoReport(string imei,
        int? getDevStatusSec = 60, int? orderUpSec = 300, int? rs485Sec = 0,
        string? getDevStatusQ = null)
    {
        var frameId = Guid.NewGuid().ToString("N");
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

        var param = new Dictionary<string, object?>
        {
            ["getDevStatusSec"] = getDevStatusSec,
            ["orderUpSec"] = orderUpSec,
            ["rs485Sec"] = rs485Sec
        };
        if (!string.IsNullOrEmpty(getDevStatusQ))
        {
            param["getDevStatusQ"] = getDevStatusQ;
        }

        var command = new Dictionary<string, object?>
        {
            ["method"] = "setAutoReport",
            ["imei"] = imei,
            ["frameId"] = frameId,
            ["timestamp"] = timestamp,
            ["param"] = param
        };

        var payload = JsonSerializer.Serialize(command);
        _logger?.LogDebug("构建 setAutoReport 命令: IMEI={IMEI}, FrameId={FrameId}", imei, frameId);
        return (frameId, payload);
    }

    /// <summary>
    /// 构建 getDevStatus 命令 — 查询设备当前状态
    /// </summary>
    /// <param name="imei">设备 IMEI</param>
    /// <param name="query">查询参数（如 "temperature,EMdata"），空字符串表示查询全部</param>
    /// <returns>(FrameId, JSON 命令字符串)</returns>
    public (string FrameId, string Payload) BuildGetDevStatus(string imei, string? query = null)
    {
        var frameId = Guid.NewGuid().ToString("N");
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

        var command = new Dictionary<string, object?>
        {
            ["method"] = "getDevStatus",
            ["imei"] = imei,
            ["frameId"] = frameId,
            ["timestamp"] = timestamp
        };

        if (!string.IsNullOrEmpty(query))
        {
            command["param"] = new Dictionary<string, object?>
            {
                ["q"] = query
            };
        }

        var payload = JsonSerializer.Serialize(command);
        _logger?.LogDebug("构建 getDevStatus 命令: IMEI={IMEI}, FrameId={FrameId}", imei, frameId);
        return (frameId, payload);
    }

    /// <summary>
    /// 构建 getDevInfo 命令 — 查询设备基础信息
    /// </summary>
    public (string FrameId, string Payload) BuildGetDevInfo(string imei)
    {
        var frameId = Guid.NewGuid().ToString("N");
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

        var command = new Dictionary<string, object?>
        {
            ["method"] = "getDevInfo",
            ["imei"] = imei,
            ["frameId"] = frameId,
            ["timestamp"] = timestamp
        };

        var payload = JsonSerializer.Serialize(command);
        return (frameId, payload);
    }

    /// <summary>
    /// 构建 orderStart 命令 — 开始充电/用电
    /// </summary>
    /// <param name="imei">设备 IMEI</param>
    /// <param name="sn">订单序列号</param>
    /// <param name="order">插槽编号（1-based）</param>
    /// <param name="limit">限时时长（秒）</param>
    /// <returns>(FrameId, JSON 命令字符串)</returns>
    public (string FrameId, string Payload) BuildOrderStart(string imei, string sn, int order = 1, int? limit = null)
    {
        var frameId = Guid.NewGuid().ToString("N");
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

        var param = new Dictionary<string, object?>
        {
            ["sn"] = sn,
            ["order"] = order
        };
        if (limit.HasValue) param["limit"] = limit;

        var command = new Dictionary<string, object?>
        {
            ["method"] = "orderStart",
            ["imei"] = imei,
            ["frameId"] = frameId,
            ["timestamp"] = timestamp,
            ["param"] = param
        };

        var payload = JsonSerializer.Serialize(command);
        _logger?.LogDebug("构建 orderStart 命令: IMEI={IMEI}, Sn={Sn}, FrameId={FrameId}", imei, sn, frameId);
        return (frameId, payload);
    }

    /// <summary>
    /// 构建 orderEnd 命令 — 结束充电/用电
    /// </summary>
    /// <param name="imei">设备 IMEI</param>
    /// <param name="sn">订单序列号</param>
    /// <param name="reason">结束原因（app/manual/auto）</param>
    /// <returns>(FrameId, JSON 命令字符串)</returns>
    public (string FrameId, string Payload) BuildOrderEnd(string imei, string sn, string reason = "app")
    {
        var frameId = Guid.NewGuid().ToString("N");
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

        var command = new Dictionary<string, object?>
        {
            ["method"] = "orderEnd",
            ["imei"] = imei,
            ["frameId"] = frameId,
            ["timestamp"] = timestamp,
            ["param"] = new Dictionary<string, object?>
            {
                ["sn"] = sn,
                ["reason"] = reason
            }
        };

        var payload = JsonSerializer.Serialize(command);
        _logger?.LogDebug("构建 orderEnd 命令: IMEI={IMEI}, Sn={Sn}, FrameId={FrameId}", imei, sn, frameId);
        return (frameId, payload);
    }

    /// <summary>
    /// 构建通用命令（任意安圣 method + 参数）
    /// </summary>
    public (string FrameId, string Payload) BuildCommand(string imei, string method,
        Dictionary<string, object?>? parameters = null)
    {
        var frameId = Guid.NewGuid().ToString("N");
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

        var command = new Dictionary<string, object?>
        {
            ["method"] = method,
            ["imei"] = imei,
            ["frameId"] = frameId,
            ["timestamp"] = timestamp
        };

        if (parameters != null && parameters.Count > 0)
        {
            command["param"] = parameters;
        }

        var payload = JsonSerializer.Serialize(command);
        _logger?.LogDebug("构建通用命令: Method={Method}, IMEI={IMEI}, FrameId={FrameId}", method, imei, frameId);
        return (frameId, payload);
    }
}
